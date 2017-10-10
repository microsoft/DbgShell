using System.Diagnostics;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommunications.Disconnect, "DbgProcess", DefaultParameterSetName = "NoSwitchesParamSet" )]
    public class DisconnectDbgProcessCommand : ExecutionBaseCommand
    {
        [Parameter( ParameterSetName = "AbandonParamSet" )]
        public SwitchParameter LeaveSuspended { get; set; }

        [Parameter( ParameterSetName = "KillParamSet" )]
        public SwitchParameter Kill { get; set; }


        protected override void ProcessRecord()
        {
            using( SetDebuggerAndContextAndUpdateCallbacks() )
            {
                if( Kill )
                {
                    if( !Debugger.IsLive )
                        WriteVerbose( "Target is not live; we will simply detach." );
                }

                _DoDisconnect( Debugger, LeaveSuspended, Kill );

                try
                {
                    // Normally I would expect to have to call WaitForEvent here. But
                    // apparently not--some time back there was a change to dbgeng, such
                    // that calling WaitForEvent here would not only fail, it would raise
                    // a DebugBreak() exception, due to a failed assert (when running
                    // under a debugger, including under VS). This was untenable--there
                    // was no way to actually handle the exception (wrapping the call in a
                    // SEH handler and saying "continue execution" put things into an
                    // infinite loop), and there was even no way to manually continue
                    // execution when running under VS with managed debugging (the
                    // debugger would just stop execution with no error or anything).
                    //
                    // So now we just don't call WaitForEvent.
                    //
                    // Why not just stop deriving from ExecutionBaseCommand? We do use the
                    // SetDebuggerAndContextAndUpdateCallbacks stuff... so we could do
                    // that, but we'd need to do some refactoring.
                    //
                 // base.ProcessRecord();
                }
                catch( DbgEngException dee )
                {
                    // From the doc: If none of the targets are capable of generating
                    // events -- for example, all the targets have exited -- this
                    // method will end the current session, discard the targets, and
                    // then return E_UNEXPECTED.
                    if( dee.HResult != DebuggerObject.E_UNEXPECTED )
                        throw;
                }
            } // end using( psPipe )
        } // end ProcessRecord()


        internal static void _DoDisconnect( DbgEngDebugger debugger,
                                            bool leaveSuspended,
                                            bool kill )
        {
            DbgEngContext ctxBefore;
            debugger.GetCurrentDbgEngContext().TryAsProcessContext( out ctxBefore );

            if( leaveSuspended )
            {
                debugger.AbandonProcess();
            }
            else
            {
                if( kill )
                {
                    if( debugger.IsLive )
                        debugger.KillProcess();
                }
                debugger.DetachProcess();
            }

            DbgEngContext ctxAfter = null;
            try
            {
                debugger.GetCurrentDbgEngContext().TryAsProcessContext( out ctxAfter );
            }
            catch( DbgProviderException )
            {
                // Couldn't get the context: great--that means it's gone, like we
                // wanted.
            }

            // Just because we were able to get /a/ context, doesn't mean the
            // disconnect didn't work--it could be the context of a different
            // process (if we had been attached to multiple systems or processes).
            if( ctxAfter == ctxBefore )
            {
                LogManager.Trace( "Looks like we need to .abandon as well." );
                // This can happen if the process has already exited--in that state,
                // a simple ".kill" (KillProcess and DetachProcess) doesn't work. It
                // doesn't work in windbg, either, but I'd like the UX to be better in
                // DbgShell: .kill should /always/ "get rid of" the current process;
                // not just if it's running.
                debugger.AbandonProcess();
            }
        } // end _DoDisconnect()
    } // end class DisconnectDbgProcessCommand
}

