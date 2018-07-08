using System;
using System.Diagnostics;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsLifecycle.Resume, "Process" )] // TODO: or should this be Resume-Target? (to also be used with a kernel target?
    public class ResumeProcessCommand : ExecutionBaseCommand
    {

        [Parameter( Mandatory = false, Position = 2 )]
        public ResumeType ResumeType { get; set; }


        protected override void ProcessRecord()
        {
            using( SetDebuggerAndContextAndUpdateCallbacks() )
            {
                EnsureTargetCanStep( Debugger );

                // In the native debugger, if you are in some other frame, your scope
                // is reset (to the top frame), but not your thread.
                Debugger.SetCurrentScopeFrame( 0 );

                switch( ResumeType )
                {
                    case Dbg.ResumeType.ReverseStepBranch:
                    case Dbg.ResumeType.ReverseStepInto:
                    case Dbg.ResumeType.ReverseStepOver:
                    case Dbg.ResumeType.StepBranch:
                    case Dbg.ResumeType.StepInto:
                    case Dbg.ResumeType.StepOver:
                    case Dbg.ResumeType.StepOut:
                    case Dbg.ResumeType.StepToCall:
                        m_baselineRegisters = DbgRegisterSetBase.GetRegisterSet( Debugger );
                        break;
                }

                if( ResumeType.Passive != ResumeType )
                {
                    try
                    {
                        Debugger.ResumeExecution( ResumeType );
                    }
                    catch( DbgEngAlreadyRunningException )
                    {
                        SafeWriteWarning( "Target has already been resumed." );
                        //
                        // We'll just proceed, then, to wait...
                        //
                    }
                }

                bool noException = false;
                try
                {
                    Debugger.BreakpointHit += Debugger_BreakpointHit;

                    if( DbgProvider.IsInBreakpointCommand )
                    {
                        Util.Assert( DbgProvider.EntryDepth > 0 );

                        // If we are executing as a breakpoint command, and someone is
                        // resuming execution, we have to handle it specially:
                        //
                        // 1. We need to cancel the rest of the pipeline. This is the same
                        //    way that dbgeng breakpoint commands work (if your bp command
                        //    is "!foo;g;!bar", then !bar will not ever be executed.
                        //
                        //    At first glance you might think that you are getting robbed
                        //    of some cool power by canceling any remaining commands, but
                        //    if you think a little more about it, you realize it would be
                        //    very difficult to reason about what should happen. For
                        //    example, consider:
                        //
                        //       bp myDll!Foo "!stuff ; g ; !otherStuff"
                        //
                        //    The first time you hit the breakpoint, "!stuff" is run, then
                        //    execution is resumed. The seocnd time the breakpoint is
                        //    hit... what should be run first--"!otherStuff", or "!stuff"?
                        //    And you could get nested much further. So we'll take the
                        //    same easy out as dbgeng, and treat "g" as an "exit" in a
                        //    breakpoint command.
                        //
                        //    TODO: we could run the script through the parser and warn
                        //    about unreachable code after a "g" command.
                        //
                        // 2. When a breakpoint hits and a breakpoint command is run, the
                        //    dbgeng thread is blocked, waiting for the breakpoint command
                        //    to finish. If we want to resume execution from within a
                        //    breakpoint command, we can tell dbgeng to resume, but if we
                        //    then turn around and call WaitForEvent (which is what we
                        //    normally do), then you're deadlocked--WaitForEvent can never
                        //    return, because before dbgeng can let the target resume,
                        //    your breakpoint command has to return, but it can't return
                        //    because it's waiting for WaitForEvent.
                        //
                        //    So in this case, once we've instructed dbgeng to execute, we
                        //    need to return from the command and let dbgeng let the
                        //    target resume. We will depend on the "host" (whether it be
                        //    windbg or DbgShell or something else) to do the
                        //    WaitForEvent. Fortunately, "exit" should get us what we want
                        //    for this case, too.
                        //
                        //    TODO: what if we are nested a few levels deep? i.e.
                        //
                        //       bp myDll!foo "!dbgshell -Bp !dbgshell write-host 'so nested!' -fore Yellow , g"
                        //
                        //    I'm not sure how we'd get the "exit" to propagate out from
                        //    the inner !dbgshell, through the outer one, so that we could
                        //    return to the host.
                        //
                        //    Also, if we are nested, and multiple levels use -Bp, we're
                        //    definitely broken--I'd need to change inBreakpointCommand to
                        //    be a count instead of a bool, for one thing.
                        //

                        if( null != m_baselineRegisters ) // <-- shortcut for determining if we are stepping
                        {
                            // If someone is trying to step while inside a breakpoint
                            // command, let's let them know what's going on.
                            this.InvokeCommand.InvokeScript( "Write-Host 'Resuming execution from within a breakpoint " +
                                                             "command necessarily returns control to the host, for it " +
                                                             "to perform the wait.' -Fore Cyan" );
                            this.InvokeCommand.InvokeScript( "Write-Host '(it also acts like an 'exit'; terminating the " +
                                                             "current pipeline and discarding any remaining commands)' " +
                                                             " -Fore Cyan" );
                            this.InvokeCommand.InvokeScript( "Write-Host '(if you want to step in DbgShell instead of " +
                                                             "the host, just run !dbgshell again from the host and then " +
                                                             "step)' -Fore Cyan" );

                            // Give them a chance to notice that a message was printed out
                            // before we switch the foreground window:
                            this.InvokeCommand.InvokeScript( "Start-Sleep -Milliseconds 500" );
                        }

                        if( !DbgProvider.IsInGuestMode )
                        {
                            // If DbgShell.exe is the host, then we need to let it know
                            // that it shouldn't exit all the way; we're just trying to
                            // terminate the current breakpoint script.
                            DbgProvider.DontExitAllTheWay = true;
                        }

                        // Throwing an ExitException would be much more direct, but there
                        // are no public constructors for it.
                        //throw new ExitException();
                        this.InvokeCommand.InvokeScript( "exit" );
                    }

                    base.ProcessRecord(); // sets up ctrl-c interception, performs wait.
                    noException = true;
                }
                finally
                {
                    Debugger.BreakpointHit -= Debugger_BreakpointHit;

                    _TryRemoveSpecialBp();

                    // We check NoTarget before setting the auto-repeat command, because
                    // if -skipFinalBreakpoint was used, the target might be completely
                    // gone (leaving you back with a working dir of "Dbg:\"), and it would
                    // be nice if hitting [ENTER] for a newline right there didn't try to
                    // auto-repeat your last "g" command (which would blow up with a "no
                    // target!" error).
                    if( noException && !Debugger.NoTarget )
                        DbgProvider.SetAutoRepeatCommand( MyInvocation.Line.Trim() );
                }
                if( null != m_bea )
                {
                 // if( m_bea.Breakpoint.IsValid )
                 //     Console.WriteLine( "Breakpoint hit. The command was: {0}", m_bea.Breakpoint.Command );
                 // else
                 //     Console.WriteLine( "Breakpoint hit. (breakpoint no longer valid)" );
                }
            }
        } // end ProcessRecord()


        /// <summary>
        ///    "Manually" clears the special "gu" breakpoint in case we didn't hit it.
        ///    (for example, if someone pressed CTRL-C before we made it back up the
        ///    stack)
        /// </summary>
        private void _TryRemoveSpecialBp()
        {
            try
            {
                var bp = Debugger.TryGetBreakpointById( DbgBreakpointInfo.StepOutBreakpointId );
                if( null != bp )
                {
                    Debugger.RemoveBreakpoint( ref bp );
                }
            }
            catch( DbgProviderException dpe )
            {
                LogManager.Trace( "Huh... ignoring error trying to clear special \"gu\" breakpoint: {0}",
                                  Util.GetExceptionMessages( dpe ) );
            }
        } // end _TryRemoveSpecialBp()


        private BreakpointEventArgs m_bea;

        private void Debugger_BreakpointHit( object sender, BreakpointEventArgs eventArgs )
        {
            m_bea = eventArgs;
        }

        protected override void StopProcessing()
        {
            base.StopProcessing();
            Console.WriteLine( "StopProcessing" );
            Util.Fail( "I should have intercepted the ctrl-c." );
            // TODO: might need to failfast...
        } // end StopProcessing()
    } // end class ResumeProcessCommand
}
