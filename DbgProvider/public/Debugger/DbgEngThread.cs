using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Dbg
{
    public partial class DbgEngDebugger : DebuggerObject
    {
        // We need to have a separate thread to create and call dbgeng on. One reason is
        // issues with COM marshaling done by the RCW, but even if we bypassed the RCW to
        // smuggle the interface across threads like we would in native code (and you have
        // to, to call SetInterrupt), another reason is that it queues APCs to the thread
        // you create it on and we need to make sure that it stays around. A third reason
        // is that if you are connected to a remote debugger, the IDebug* interfaces are
        // tightly connected to RPC, which has a lot of thread affinity.
        private sealed class DbgEngThread : IDisposable
        {
            // The design of dbgeng itself is to be a global singleton, so once we create
            // any debugger stuff, we're going to want to stick with that thread for the
            // rest of the lifetime of the process.
            public static readonly DbgEngThread Singleton = _ProcureDbgEngThread();

            private static DbgEngThread _ProcureDbgEngThread()
            {
                if( null != DbgProvider.GuestModeGuestThread )
                    return new DbgEngThread( DbgProvider.GuestModeGuestThread );
                else
                    return new DbgEngThread();
            } // end _ProcureDbgEngThread()


            private object m_syncRoot = new object();
            private bool m_disposed;
            private Thread m_dbgEngThread;
            private bool _IsOnPipelineThread { get { return Thread.CurrentThread == m_dbgEngThread; } }
            // This queue is used by other threads to queue actions that need to be run on the
            // dbgeng thread.
            private BlockingCollection< Action > m_q = new BlockingCollection< Action >();


            private DbgEngThread()
            {
                // TODO: Do we need to set the apartment state?
                m_dbgEngThread = new Thread( _ThreadProcProcessActions );
                m_dbgEngThread.IsBackground = true;
                m_dbgEngThread.Name = "Dedicated DbgEng Thread";
                m_dbgEngThread.Start();
            } // end constructor


            // For "guest mode".
            private DbgEngThread( Thread existingThread )
            {
                m_dbgEngThread = existingThread;
                if( String.IsNullOrEmpty( m_dbgEngThread.Name ) )
                    m_dbgEngThread.Name = "Dedicated DbgEng Thread (host-supplied)";

                DbgProvider.GuestModeGuestThreadActivity = _ThreadProcProcessActions;
                DbgProvider.GuestModeEvent.Set();
            }

            // For "guest mode".
            internal void ResetQ()
            {
                Util.Assert( !m_disposed );
                Util.Assert( m_q.IsCompleted );

                m_q.Dispose();
                m_q = new BlockingCollection< Action >();

                // Maybe this isn't a program invariant... but I'm not expecting windbg to
                // switch threads on me.
                Util.Assert( m_dbgEngThread == Thread.CurrentThread );
            }

            // TODO: so... somebody should dispose of me...
            public void Dispose()
            {
                lock( m_syncRoot )
                {
                    if( m_disposed )
                        return;

                    m_disposed = true;
                }

                m_q.Dispose();
            } // end Dispose()

            public void QueueAction( Action action )
            {
                if( _IsOnPipelineThread )
                    _CrashOnException( action );
                else
                {
                    try
                    {
                        m_q.Add( action );
                    }
                    catch( InvalidOperationException ioe )
                    {
                        if( !DbgProvider.IsInGuestMode )
                            throw;

                        throw new DbgProviderException( "You cannot interact with dbgeng while DbgShell is dormant. Run \"!dbgshell\" from the debugger to re-activate DbgShell.",
                                                        "DbgShellIsDormant",
                                                        System.Management.Automation.ErrorCategory.InvalidOperation,
                                                        ioe );
                    }
                } // end else( not on pipeline thread )
            } // end QueueAction()


            public Task< TRet > ExecuteAsync< TRet >( Func< TRet > f )
            {
                TaskCompletionSource< TRet > tcs = new TaskCompletionSource< TRet >();
                QueueAction( () =>
                    {
                        try
                        {
                            tcs.TrySetResult( f() );
                        }
                        catch( Exception e )
                        {
                            tcs.TrySetException( e );
                        }
                    } );

                return tcs.Task;
            } // end ExecuteAsync()

            public Task ExecuteAsync( Action a )
            {
                return ExecuteAsync< object >( () => { a(); return null; } );
            }

            public TRet Execute< TRet >( Func< TRet > f )
            {
                return Util.Await( ExecuteAsync( f ) );
            }

            public void Execute( Action a )
            {
                Util.Await( ExecuteAsync( a ) );
            }

            private static void _CrashOnException( Action action )
            {
                try
                {
                    action();
                }
                catch( Exception e )
                {
                    Util.FailFast( Util.Sprintf( "Unexpected exception: {0}", e ), e );
                }
            } // end _CrashOnException()

            public void SignalDone()
            {
                m_q.CompleteAdding();
            }

            // If we wanted to make this thread more generically accessible for execution, we
            // could use a Dispatcher object. Would that be better/useful? Until we decide so,
            // we'll limit the ability to queue work to this thread to ourselves.
            private void _ThreadProcProcessActions()
            {
                //Console.WriteLine( "DbgEngThread apartment state: {0}", Thread.CurrentThread.GetApartmentState() );
                foreach( Action action in m_q.GetConsumingEnumerable() )
                {
                    lock( m_syncRoot )
                    {
                        if( m_disposed )
                            return;
                    }
                    _CrashOnException( action );
                }
            } // end _ThreadProcProcessActions()
        } // end class DbgEngThread
    } // end class DbgEngDebugger
}

