using System;
using System.Management.Automation;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsLifecycle.Invoke, "DbgExtension" )]
    public class InvokeDbgExtensionCommand : ExecutionBaseCommand
    {
        [Parameter( Mandatory = true, Position = 0 )]
        [Alias( "Command" )]
        public string ExtensionCommand { get; set; }

        [Parameter( Mandatory = false,
                    Position = 1,
                    ValueFromPipeline = true,
                    ValueFromRemainingArguments = true )]
        [Alias( "Arguments" )]
        public string ExtensionArgs { get; set; }


        private void _ConsumeLine( string line )
        {
            SafeWriteObject( line, true ); // this will get sent to the pipeline thread
        }

        protected override bool TrySetDebuggerContext { get { return true; } }


        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            m_dontDisplayRegisters = true;
        } // end BeginProcessing()



        private async Task _WaitIfNeededAsync()
        {
            DEBUG_STATUS execStatus = Debugger.GetExecutionStatus();
            if( _StatusRequiresWait( execStatus ) )
            {
                LogManager.Trace( "InvokeDbgExtensionCommand: Current execution status ({0}) indicates that we should enter a wait.",
                                  execStatus );

                await WaitForBreakAsync();
            }
        } // end _WaitIfNeededAsync()


        private async Task _CallExtensionAsync( ulong hExt, string extCommand, string extArgs )
        {
            LogManager.Trace( "_CallExtensionAsync: {0} {1}", extCommand, extArgs );

            try
            {
                // N.B. We need to be sure that we have unregistered for DbgEngOutput etc.
                // /BEFORE/ SignalDone is called. If not, there could be some DbgEng
                // output that comes after SignalDone has been called, but before we get
                // back from MsgLoop.Run() and dispose of the registrations, and that
                // would blow chunks. (because things like SafeWriteVerbose would not
                // work, because the q was completed when SignalDone was called)
                using( Debugger.HandleDbgEngOutput( _ConsumeLine ) )
                {
                    await Debugger.CallExtensionAsync( hExt, extCommand, extArgs );
                    await _WaitIfNeededAsync();
                }
            }
            finally
            {
                MsgLoop.SignalDone();
            }
        } // end _CallExtensionAsync()


        protected override void ProcessRecord()
        {
            var inputCallbacks = Debugger.GetInputCallbacks() as DebugInputCallbacks;
            if( null != inputCallbacks )
                inputCallbacks.UpdateCmdlet( this );

            // TODO: Why does this cmdlet even need to be an ExecutionBaseCommand?

            using( InterceptCtrlC() )
            using( Debugger.SetCurrentCmdlet( this ) )
            {
                ulong hExt = 0;

                int dotIdx = ExtensionCommand.LastIndexOf( '.' );
                if( dotIdx > 0 )
                {
                    if( dotIdx == (ExtensionCommand.Length - 1) )
                    {
                        throw new ArgumentException( "ExtensionCommand cannot end with a '.'." );
                    }

                    string extName = ExtensionCommand.Substring( 0, dotIdx );
                    var elr = Debugger.GetExtensionDllHandle( extName, true );
                    if( null == elr )
                    {
                        throw new DbgProviderException( Util.Sprintf( "Could not find/load extension: {0}.",
                                                                      extName ),
                                                        "CouldNotLoadExtension",
                                                        ErrorCategory.ObjectNotFound,
                                                        extName );
                    }
                    if( !String.IsNullOrEmpty( elr.LoadOutput ) )
                        SafeWriteObject( elr.LoadOutput );

                    ExtensionCommand = ExtensionCommand.Substring( dotIdx + 1 );
                }

                MsgLoop.Prepare();

                Task t = _CallExtensionAsync( hExt, ExtensionCommand, ExtensionArgs );

                MsgLoop.Run();

                Host.UI.WriteLine();
                Util.Await( t ); // in case it threw
            } // end using( InterceptCtrlC )
        } // end ProcessRecord()
    } // end class InvokeDbgExtensionCommand
}
