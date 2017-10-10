using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace MS.DbgShell
{
    using MS.Dbg;
    using PowerShell = System.Management.Automation.PowerShell;

    internal partial class ColorHostUserInterface : System.Management.Automation.Host.PSHostUserInterface
    {

        private ConsoleColor warningForegroundColor = ConsoleColor.Black;
        private ConsoleColor warningBackgroundColor = ConsoleColor.Yellow;

        private bool TryInvokeUserDefinedReadLine(out string input, bool useUserDefinedCustomReadLine)
        {
            // We're using GetCommands instead of GetCommand so we don't auto-load a module should the command exist, but isn't loaded.
            // The idea is that if someone hasn't defined the command (say because they started -noprofile), we shouldn't auto-load
            // this function.

            input = null;
            if( !useUserDefinedCustomReadLine )
                return false;

            // If we need to look for user defined custom readline command, then we need to wait for Runspace to be created.

                // [danthom] TODO: What to do about this?
              //var runspace = this.parent.LocalRunspace;
              //if (runspace != null
              //    && runspace.Engine.Context.EngineIntrinsics.InvokeCommand.GetCommands(CustomReadlineCommand, CommandTypes.All, nameIsPattern: false).Any())
              //{
              //    PowerShell ps;
              //    if (runspace.ExecutionContext.EngineHostInterface.NestedPromptCount > 0)
              //    {
              //        ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
              //    }
              //    else
              //    {
              //        ps = PowerShell.Create();
              //        ps.Runspace = runspace;
              //    }

              //    try
              //    {
              //        var result = ps.AddCommand(CustomReadlineCommand).Invoke();
              //        if (result.Count == 1)
              //        {
              //            input = PSObject.Base(result[0]) as string;
              //            return true;
              //        }
              //    }
              //    catch (Exception e)
              //    {
              //        // TODO: what
              //        //CommandProcessorBase.CheckForSevereException(e);
              //        Util.FailFast( "Exception in TryInvokeUserDefinedReadLine.", e );
              //    }
              //}

            // Here's my attempt at duplicating the functionality of the above code,
            // without having access to PowerShell-internal stuff (which requires me to
            // turn some of the logic inside-out because I can't access the session state
            // when we are nested).

            PowerShell ps = null;
            try
            {
                var runspace = this.parent.Runspace;
                if( null == runspace )
                    return false;

                if( runspace.RunspaceAvailability == RunspaceAvailability.AvailableForNestedCommand )
                {
                    // Nested

                    // We don't have an easy way to check if we have a readline command
                    // defined-- we can't access the SessionState directly when nested (else
                    // we get an error complaining "A pipeline is already running. Concurrent
                    // SessionStateProxy method call is not allowed."). We could run some
                    // script, but we don't want readline commands to cause any auto-loading,
                    // so we have to save the old $PSModuleAutoloadingPreference, set it to
                    // "none", then check for the command (using a wildcard to make sure we
                    // don't generate errors if it doesn't exist), then restore the
                    // $PSModuleAutoloadingPreference... which seems like a pain.  So instead,
                    // if we are nested, I'm just going to remember what we had in the
                    // outer-most (non-nested) shell. This means that you won't be able to
                    // change the readline function when nested, but I think that's a fairly
                    // corner-case scenario.
                 // parent._ExecScriptNoExceptionHandling( ps,
                 //                                        Util.Sprintf( ...
                 //                                        false,
                 //                                        false );

                    if( m_customReadlineFuncExists )
                        ps = PowerShell.Create( RunspaceMode.CurrentRunspace );
                }
                else
                {
                    // Not Nested
                    if( runspace.SessionStateProxy.InvokeCommand.GetCommands(CustomReadlineCommand, CommandTypes.All, nameIsPattern: false).Any() )
                    {
                        m_customReadlineFuncExists = true;
                        ps = PowerShell.Create();
                        ps.Runspace = runspace;
                    }
                    else
                    {
                        m_customReadlineFuncExists = false;
                    }
                }

                if( null == ps )
                    return false;

                var result = ps.AddCommand(CustomReadlineCommand).Invoke();
                if (result.Count == 1)
                {
                    //input = PSObject.Base(result[0]) as string; [danthom]
                    input = PSObjectSubstitute.Base(result[0]) as string;
                    return true;
                }
                else if (result.Count > 1)
                {
                    var msg = Util.Sprintf( "Custom readline function returned multiple results ({0}).",
                                            result.Count );

                    LogManager.Trace( msg );
                    Util.Fail( msg ); // not a true invariant; just want to stop in the debugger if this ever happens
                    try
                    {
                        ps.Commands.Clear();
                        ps.AddCommand( "Write-Warning" ).AddParameter( "Message", msg );
                        ps.Invoke();
                    }
                    catch( Exception e2 )
                    {
                        Util.Fail( Util.Sprintf( "Could not write warning about multi-result readline! {0}",
                                                 Util.GetExceptionMessages( e2 ) ) );
                    }
                }
            }
            catch (Exception e)
            {
                // I've seen us get into this situation when running "rmo psreadline" from
                // within a nested prompt. It seems the prompt function doesn't want to
                // get reset until we exit the nested prompt.
                LogManager.Trace( "Custom readline function failed: {0}", e.ToString() );
                try
                {
                    ps.Commands.Clear();
                    ps.AddCommand( "Write-Warning" ).AddParameter( "Message",
                                                                   Util.Sprintf( "Custom readline function failed: {0}",
                                                                                 Util.GetExceptionMessages( e ) ) );
                    ps.Invoke();
                }
                catch( Exception e2 )
                {
                    Util.Fail( Util.Sprintf( "Could not write warning about failed readline! {0}",
                                             Util.GetExceptionMessages( e2 ) ) );
                }
            }

            input = null;
            return false;
        }


        private static ProgressRecord CopyProgressRecord( ProgressRecord record,
                                                          string currentOperation )
        {
            return new ProgressRecord(record.ActivityId, record.Activity, record.StatusDescription)
            {
                CurrentOperation = currentOperation,
                ParentActivityId = record.ParentActivityId,
                PercentComplete = record.PercentComplete,
                SecondsRemaining = record.SecondsRemaining,
                RecordType = record.RecordType
            };
        }
    }

}   // namespace

