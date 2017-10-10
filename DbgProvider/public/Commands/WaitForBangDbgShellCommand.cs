using System;
using System.Diagnostics;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Dbg.Commands
{
    //
    // This is a special command, only meant to be used by the host, when in "guest mode"
    // (hosted by a debugger extension).
    //
    // It keeps DbgShell in a holding pattern, with an active pipeline (so that we can
    // write output, like modload events, etc.) while dormant (while the main debugger has
    // control).
    //
    // Timeline:
    //         windbg                        DbgShell
    //    user starts windbg
    //    user loads DbgShellExt
    //    user runs !dbgshell
    //                                Now DbgShell is running
    //                                user does DbgShell stuff
    //                                In DbgShell, user runs "exit"
    //                                DbgShell automagically runs Wait-ForBangDbgShellCommand
    //    control returns to windbg   .
    //    ... user does stuff ...     .
    //    user runs !dbgshell again   .
    //                                Wait-ForBangDbgShellCommand finishes
    //                                DbgShell execution continues
    //
    [Cmdlet( VerbsLifecycle.Wait, "ForBangDbgShellCommand" )]
    public class WaitForBangDbgShellCommandCommand : DbgBaseCommand
    {
        // Wait for the "guest mode" event to be signaled.
        private void _RegisterWaitForGuestModeEvent( ExceptionGuard disposer )
        {
            LogManager.Trace( "Registering wait for GuestMode event." );
            ThreadPool.RegisterWaitForSingleObject( DbgProvider.GuestModeEvent,
                                                    ( x, y ) => { disposer.Dispose(); SignalDone(); },
                                                    null,   // state
                                                    -1,     // INFINITE
                                                    true ); // executeOnlyOnce
        } // end _RegisterWaitForGuestModeEvent()


        private bool _CtrlCHandler( ConsoleBreakSignal ctrlType )
        {
            LogManager.Trace( "_CtrlCHandler ({0}), thread apartment type: {1}",
                              ctrlType,
                              Thread.CurrentThread.GetApartmentState() );
            LogManager.Flush();

            if( DbgProvider.GuestModeShareConsole )
            {
                // Ideally we could just let the CTRL-C go to ntsd or cdb or kd or whoever
                // the real console owner is. The problem is that if we return false here
                // (and let the signal go to the next in the chain), the PowerShell
                // runtime will handle it (by canceling the pipeline, resulting in calling
                // our StopProcessing). So in addition to keeping the debugger's handler
                // from getting it, it really discombobulates our dormant state.
                //
                // So what we're going to do instead is do the same work that the debugger
                // would have done. This is scary and fragile (what if they change it?),
                // but a) I don't have a better idea at the moment, and b) hopefully it's
                // not too risky--the only thing that the console debugger's handler does
                // is try to break in.

                // Note that only CTRL-C and CTRL-BREAK get handled by CtrlCInterceptor,
                // so we don't have to check for that.

                // This could block, so just to be safe I'm going to do it on a
                // threadpool thread. (Break() is safe to call from any thread.)
                ThreadPool.QueueUserWorkItem( ( x ) =>
                    {
                        try
                        {
                            Debugger.Break();
                        }
                        catch( DbgProviderException dpe )
                        {
                            SafeWriteWarning( "Could not break in: {0}", Util.GetExceptionMessages( dpe ) );
                        }
                    } );
                return true;
            } // end if( sharing console )

            if( ctrlType == ConsoleBreakSignal.CtrlBreak )
            {
                SafeWriteWarning( "Attempting to enter nested prompt... you will not be able to interact with the debugger." );
                // If they /do/ try to run a debugger command, they should get a
                // reasonable error explaining that they can't do while DbgShell is
                // dormant.
                QueueSafeAction( () =>
                    {
                        this.Host.EnterNestedPrompt();
                    } );
            }
            else
            {
                SafeWriteWarning( "You cannot break into the target from DbgShell while DbgShell is inactive." );
                SafeWriteWarning( "Instead, break in from the main debugger (windbg/ntsd/cdb/kd)." );
                SafeWriteWarning( "You can run !dbgshell from the debugger to re-activate DbgShell." );
            }

            return true;
        } // end _CtrlCHandler()

        protected override bool TrySetDebuggerContext
        {
            get
            {
                // We don't need to have the debugger context properly set. (we're just
                // going to be waiting for the guest mode event, and not interacting with
                // dbgeng)
                return false;
            }
        }

        // TODO: actually... I could probably just set this in BeginProcessing...
        protected IDisposable SetDebuggerAndContextAndUpdateCallbacks()
        {
            Debugger = DbgEngDebugger._GlobalDebugger;

            var disposable = Debugger.SetCurrentCmdlet( this );

            return disposable;
        } // end SetDebuggerAndContextAndUpdateCallbacks()


        protected IDisposable InterceptCtrlC()
        {
            return new CtrlCInterceptor( _CtrlCHandler );
        } // end InterceptCtrlC()


        protected override void ProcessRecord()
        {
            if( !DbgProvider.IsInGuestMode )
            {
                throw new InvalidOperationException( "This command is only intended to be used by the !dbgshell implementation." );
            }

            LogManager.Trace( "WaitForBangDbgShellCommandCommand.ProcessRecord: DbgShell is going dormant." );

            using( var disposer = new ExceptionGuard() )
            {
                disposer.Protect( SetDebuggerAndContextAndUpdateCallbacks() );
                disposer.Protect( InterceptCtrlC() );
                disposer.Protect( Debugger.SuspendEventOutput() ); // don't want our "modload" output etc. spewing while we are dormant

                base.ProcessRecord();
                // We need to perform the wait on another thread, because we need to "pump"
                // on this thread, so that debug event callbacks can queue WriteObject calls
                // to this thread.
                _RegisterWaitForGuestModeEvent( disposer );

                // Give the user a hint about what's going on.
                var s = new ColorString( ConsoleColor.DarkGray,
                                         "(DbgShell will remain running in the background until you run !dbgshell again)" ).ToString( true );
                Host.UI.WriteLine( s );

                MsgLoop.Prepare();

                // This will allow the extension command to return (it tells the dbgeng
                // thread that it can return).
                DbgProvider.GuestModePassivate();

                MsgLoop.Run(); // This will complete when the guest mode event gets signaled.
            } // end using( disposer )
        } // end ProcessRecord()


        protected override void StopProcessing()
        {
            base.StopProcessing();
            Console.WriteLine( "StopProcessing" );
            Util.Fail( "I should have intercepted the ctrl-c." );
            // TODO: might need to failfast...
            // If we get here, something is probably seriously, seriously wrong.
        } // end StopProcessing()


        protected override void EndProcessing()
        {
            bool noException = false;
            try
            {
                // TODO: I could skip all this if nothing has changed the debugger state.
                // Perhaps not worth bothering to detect that, though.

                // We don't want to try to interact with the debugger if we're exiting,
                // because the DbgEngThread won't be running.
                if( !DbgProvider.GuestModeTimeToExitAllTheWay )
                {
                    // TODO: Seems like this should not need to be called--DbgEngDebugger should
                    // just take care of it after WaitForEventAsync completes. But didn't I have a
                    // problem doing it then?
                    // (same code in ExecutionBaseCommand.EndProcessing())
                    Debugger.UpdateNamespace();

                    //
                    // TODO: When re-entering DbgShell, we currently show the registers. If we
                    // wanted to make that optional, here is where we would pass either true
                    // or false to make that happen.
                    //
                    bool showRegisters = false;
                    object objFlag = this.GetVariableValue( "ShowRegistersWhenEnteringBangDbgShell", false );
                    if( objFlag is bool )
                    {
                        showRegisters = (bool) objFlag;
                    }

                    RebuildNamespaceAndSetLocationBasedOnDebuggerContext( showRegisters );

                    if( !Debugger.NoTarget )
                    {
                        Task t = null;
                        t = Debugger.SetNextAddressStuffAsync();

                        // Now that we can detect the bitness of the target, let's make sure things look nice.
                        // TODO: BUGBUG: I think you could be attached to multiple targets, with
                        // different address widths. What should we do about that?
                        Debugger.AdjustAddressColumnWidths( Host.UI.RawUI.BufferSize.Width );

                        Util.Await( t );
                    }
                } // end if( !GuestModeTimeToExitAllTheWay )
                noException = true;
            }
            finally
            {
                if( !noException )
                    LogManager.Trace( "(WaitForBangCommandCommand.EndProcessing: leaving under exceptional circumstances)" );

                base.EndProcessing();
            }
        } // end EndProcessing()
    } // end class WaitForBangDbgShellCommandCommand
}
