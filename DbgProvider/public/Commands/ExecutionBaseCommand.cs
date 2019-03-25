using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg.Commands
{
    public abstract class ExecutionBaseCommand : DbgBaseCommand
    {
        protected ExecutionBaseCommand()
        {
        } // end constructor

        private bool _CtrlCHandler( ConsoleBreakSignal ctrlType )
        {
            LogManager.Trace( "_CtrlCHandler ({0}), thread apartment type: {1}",
                              ctrlType,
                              Thread.CurrentThread.GetApartmentState() );
            LogManager.Flush();

            if( ctrlType == ConsoleBreakSignal.CtrlBreak )
            {
                QueueSafeAction( () =>
                    {
                        this.Host.EnterNestedPrompt();
                    } );
            }
            else
            {
                Debugger.Break();
                CancelTS.Cancel();
            }

            return true;
        } // end _CtrlCHandler()

        protected override bool TrySetDebuggerContext
        {
            get
            {
                // Of course execution commands need the debugger context set, but we will
                // set it ourselves in ProcessRecord, and fail if it doesn't work.
                return false;
            }
        }

        // TODO: actually... I could probably just set this in BeginProcessing... my only
        // concern is that I think BeginProcessing gets called only once (and
        // ProcessRecord multiple times) when pipe-ing stuff in (like multiple targets). I
        // need to try out some multi-target stuff first.
        protected IDisposable SetDebuggerAndContextAndUpdateCallbacks()
        {
            return SetDebuggerAndContextAndUpdateCallbacks( null );
        }

        protected IDisposable SetDebuggerAndContextAndUpdateCallbacks( string contextPath )
        {
            if( String.IsNullOrEmpty( contextPath ) )
            {
                PathInfo pi = this.CurrentProviderLocation( DbgProvider.ProviderId );
                contextPath = pi.ProviderPath;
            }
            else
            {
                contextPath = GetUnresolvedProviderPathFromPSPath( contextPath );
            }

            Debugger = DbgProvider.GetDebugger( contextPath );

            if( (null == Debugger) || Debugger.NoTarget )
            {
                throw new DbgProviderException( "No target.",
                                                "NoTarget",
                                                ErrorCategory.InvalidOperation );
            }

            var inputCallbacks = Debugger.GetInputCallbacks() as DebugInputCallbacks;
            if( null != inputCallbacks )
                inputCallbacks.UpdateCmdlet( this );

            var disposable = Debugger.SetCurrentCmdlet( this );

            Debugger.SetContextByPath( contextPath );

            return disposable;
        } // end SetDebuggerAndContextAndUpdateCallbacks()


        protected IDisposable InterceptCtrlC()
        {
            return new CtrlCInterceptor( _CtrlCHandler );
        } // end InterceptCtrlC()


        protected static bool _StatusRequiresWait( DEBUG_STATUS execStatus )
        {
            switch( execStatus )
            {
                case DEBUG_STATUS.BREAK:
                case DEBUG_STATUS.NO_DEBUGGEE:
                case DEBUG_STATUS.RESTART_REQUESTED:
                    return false;

                case DEBUG_STATUS.GO:
                case DEBUG_STATUS.STEP_OVER:
                case DEBUG_STATUS.STEP_INTO:
                case DEBUG_STATUS.STEP_BRANCH:
                case DEBUG_STATUS.REVERSE_GO:
                case DEBUG_STATUS.REVERSE_STEP_BRANCH:
                case DEBUG_STATUS.REVERSE_STEP_OVER:
                case DEBUG_STATUS.REVERSE_STEP_INTO:
                    return true;

                case DEBUG_STATUS.OUT_OF_SYNC:
                case DEBUG_STATUS.TIMEOUT:
                case DEBUG_STATUS.WAIT_INPUT:
                    Util.Fail( Util.Sprintf( "I wonder if I should handle this status here: {0}", execStatus ) );
                    return false;

                case DEBUG_STATUS.GO_HANDLED:
                case DEBUG_STATUS.GO_NOT_HANDLED:
                case DEBUG_STATUS.IGNORE_EVENT:
                case DEBUG_STATUS.NO_CHANGE:
                        Util.Fail( Util.Sprintf( "I don't think I should ever receive this status: {0}", execStatus ) );
                        return false;

                default:
                    string msg = Util.Sprintf( "Unexpected DEBUG_STATUS: {0} ({1}).", execStatus, (int) execStatus );
                    Util.Fail( msg );
                    throw new Exception( msg );
            } // end switch( execStatus )
        } // end _StatusRequiresWait


        protected async Task WaitForBreakAsync()
        {
            // The actual wait is performed on the dbgeng thread, allowing us to "pump"
            // "messages" on this thread (so that debug event callbacks can queue
            // WriteObject calls to this thread).
            LogManager.Trace( "ExecutionBaseCommand.WaitForBreakAsync()" );

            bool execStatusRequiresWait = true;
            while( execStatusRequiresWait )
            {
                await Debugger.WaitForEventAsync();

                DEBUG_STATUS execStatus = Debugger.GetExecutionStatus();
                execStatusRequiresWait = _StatusRequiresWait( execStatus );
                if( execStatusRequiresWait )
                {
                    LogManager.Trace( "Execution status requires an additional wait: {0}", execStatus );
                }
            }
        } // end WaitForBreakNoLoop()


        protected override void ProcessRecord()
        {
            ProcessRecord( false );
        }

        protected void ProcessRecord( bool callWaitForEventDespiteCurrentStatus )
        {
            base.ProcessRecord();

            DEBUG_STATUS currentStatus = Debugger.GetExecutionStatus();
            LogManager.Trace( "ExecutionBaseCommand.ProcessRecord: Current status is {0}.", currentStatus );
            if( !callWaitForEventDespiteCurrentStatus && (DEBUG_STATUS.BREAK == currentStatus) )
            {
                // TODO: This is for attaching to a debugging server. Need to test it. Other cases?
                // Perhaps this logic should be changed to be more clear?
                LogManager.Trace( "ExecutionBaseCommand: current status is already BREAK; will not call WaitForEvent." );
                return; // Don't need to call WaitForEvent.
            }

            using( InterceptCtrlC() )
            {
                // We need to perform the wait on another thread, because we need to "pump"
                // on this thread, so that debug event callbacks can queue WriteObject calls
                // to this thread.
                LogManager.Trace( "ExecutionBaseCommand: calling WaitForEvent (on dbgeng thread)." );

                MsgLoop.Prepare();

                Task waitTask = Debugger.WaitForEventAsync();
                waitTask.ContinueWith( ( x ) => { MsgLoop.SignalDone(); } );

                MsgLoop.Run();

                Util.Await( waitTask ); // in case it threw
            } // end using( InterceptCtrlC )
        } // end ProcessRecord()

        protected DbgRegisterSetBase m_baselineRegisters;
        protected bool m_dontDisplayRegisters;

        protected override void EndProcessing()
        {
            bool noException = false;
            try
            {
                // TODO: Seems like this should not need to be called--DbgEngDebugger should
                // just take care of it after WaitForEventAsync completes. But didn't I have a
                // problem doing it then?
                // (same code in WaitForBangCommandCommand.EndProcessing())
                //
                // > DbgEngDebugger should just take care of it after WaitForEventAsync
                // We also have the problem that DisconnectDbgProcessCommand does not call
                // base.ProcessRecord, and thus does not call WaitForEvent. But maybe that
                // command just needs to not derive from ExecutionBaseCommand.
                Debugger.UpdateNamespace();

                RebuildNamespaceAndSetLocationBasedOnDebuggerContext( !m_dontDisplayRegisters,
                                                                      m_baselineRegisters );

                if( !Debugger.NoTarget )
                {
                    Task t = null;
                    if( !TrySetDebuggerContext ) // hack to exclude Invoke-DbgEngCommand
                    {
                        t = Debugger.SetNextAddressStuffAsync();
                    }

                    // Now that we can detect the bitness of the target, let's make sure things look nice.
                    // TODO: BUGBUG: I think you could be attached to multiple targets, with
                    // different address widths. What should we do about that?
                    Debugger.AdjustAddressColumnWidths( Host.UI.RawUI.BufferSize.Width );

                    if( null != t )
                        Util.Await( t );
                }
                noException = true;
            }
            finally
            {
                if( !noException )
                    LogManager.Trace( "(ExecutionBaseCommand.EndProcessing: leaving under exceptional circumstances)" );

                base.EndProcessing();
            }
        } // end EndProcessing()


        internal class DebugInputCallbacks : IDebugInputCallbacks
        {
            private ExecutionBaseCommand m_cmdlet;

            public DebugInputCallbacks( ExecutionBaseCommand cmdlet )
            {
                if( null == cmdlet )
                    throw new ArgumentNullException( "cmdlet" );

                m_cmdlet = cmdlet;
            } // end constructor

            public void UpdateCmdlet( ExecutionBaseCommand cmdlet )
            {
                m_cmdlet = cmdlet;
            } // end UpdateCmdlet()

            public int StartInput( uint BufferSize )
            {
                try
                {
                    // TODO: powershell-ize
                    Console.Write( "StartInput( {0} )> ", BufferSize );
                    string input = Console.ReadLine();
                    // TODO: should this be in the debugger class?
                    IDebugControl6 dc6 = (IDebugControl6) m_cmdlet.Debugger.DebuggerInterface;
                    int hresult = dc6.ReturnInputWide( input );
                    // TODO: apparently it can return S_FALSE, meaning it "already
                    // received the input", because it asks all clients for input. How
                    // should I inform the user?
                    if( hresult < 0 )
                    {
                        Util.FailFast( "ReturnInput failed", new Win32Exception( hresult ) );
                    }
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception in StartInput callback.", e );
                }
                return 0;
            }

            public int EndInput()
            {
                // TODO: crud... how do I "cancel" an outstanding ReadLine()?
                return 0;
            }
        } // end class DebugInputCallbacks


        /*protected*/ internal void CheckCanAddNewTargetType( DbgEngDebugger.TargetType newTargetType )
        {
            bool? canAddTargetType = Debugger.CanAddTargetType( newTargetType );

            if( canAddTargetType == false )
            {
                throw new DbgProviderException(
                    Util.Sprintf( "DbgEng does not support adding this type of target ({0}) to the set of existing targets ({1}).",
                                  newTargetType,
                                  Debugger.GetCurrentTargetTypes() ),
                    "TargetTypeCombinationNotSupported",
                    ErrorCategory.InvalidOperation );
            }
            else if( canAddTargetType == null )
            {
                SafeWriteWarning( "This combination of targets ({0} + {1}) has not been tested. Does it work?",
                                  Debugger.GetCurrentTargetTypes(),
                                  newTargetType );
            }
        }
    } // end class ExecutionBaseCommand

}
