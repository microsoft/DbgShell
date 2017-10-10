using System;
using System.ComponentModel;
using System.Threading;

namespace MS.Dbg
{
    /// <summary>
    ///    A replacement for the ConsoleSpecialKey enum, which does not have all the
    ///    values.
    /// </summary>
    // TODO: Fix it in coreclr?
    public enum ConsoleBreakSignal : uint
    {
        CtrlC     = 0,
        CtrlBreak = 1,
        Close     = 2,
        Logoff    = 5,   // only received by services
        Shutdown  = 6,   // only received by services
    }

    /// <summary>
    ///    Installs a control key handler which handles special signals like CTRL-C.
    /// </summary>
    /// <remarks>
    ///    N.B. Be careful about blocking the CTRL-handler thread. The handler is
    ///    dispatched on essentially a random threadpool thread, and a lock is held while
    ///    dispatching. So if you block the CTRL-handler thread inside your handler, and
    ///    somebody else tries to install a CTRL handler on a different thread (which your
    ///    handler is dependent on), you will deadlock.
    /// </remarks>
    internal sealed class CtrlCInterceptor : IDisposable
    {
        private HandlerRoutine m_handler;
        private HandlerRoutine m_handlerWrapper;
        private bool m_allowExtendedEvents;

        private bool _HandlerWrapper( ConsoleBreakSignal ctrlType )
        {
            if( !m_allowExtendedEvents )
            {
                // There are actually other signals which are not represented by the
                // ConsoleSpecialKey type (like CTRL_CLOSE_EVENT). We won't handle
                // those.
                if( (ctrlType != ConsoleBreakSignal.CtrlBreak) &&
                    (ctrlType != ConsoleBreakSignal.CtrlC) )
                {
                    return false;
                }
            }
            return m_handler( ctrlType );
        } // end _HandlerWrapper

        /// <summary>
        ///    Installs a control key handler which handles special signals like CTRL-C.
        /// </summary>
        /// <remarks>
        ///    N.B. Be careful about blocking the CTRL-handler thread. The handler is
        ///    dispatched on essentially a random threadpool thread, and a lock is held
        ///    while dispatching. So if you block the CTRL-handler thread inside your
        ///    handler, and somebody else tries to install a CTRL handler on a different
        ///    thread (which your handler is dependent on), you will deadlock.
        /// </remarks>
        public CtrlCInterceptor( HandlerRoutine replacementHandler )
            : this( replacementHandler, false )
        {
        }

        public CtrlCInterceptor( HandlerRoutine replacementHandler, bool allowExtendedEvents )
        {
            if( null == replacementHandler )
                throw new ArgumentNullException( "replacementHandler" );

            m_handler = replacementHandler;
            m_handlerWrapper = _HandlerWrapper;
            m_allowExtendedEvents = allowExtendedEvents;

            if( !NativeMethods.SetConsoleCtrlHandler( m_handlerWrapper, true ) )
            {
                throw new Win32Exception(); // automatically uses last win32 error
            }
        } // end constructor

        public void Dispose()
        {
            if( !NativeMethods.SetConsoleCtrlHandler( m_handlerWrapper, false ) )
            {
                // TODO: normally you don't want to throw from Dispose, so maybe I should
                // just assert...
                throw new Win32Exception(); // automatically uses last win32 error
            }
        } // end Dispose()
    } // end class CtrlCInterceptor


    /// <summary>
    ///    Like CtrlCInterceptor, but just signals a CancellationToken when canceled
    ///    (isntead of calling a handler).
    /// </summary>
    internal class CancelOnCtrlC : IDisposable
    {
        private CancellationTokenSource m_cts;
        private CtrlCInterceptor m_ctrlCInterceptor;

        public CancellationTokenSource CTS
        {
            get { return m_cts; }
        }

        private bool _Handler( ConsoleBreakSignal ctrlType )
        {
            m_cts.Cancel();
            return true;
        }

        public CancelOnCtrlC() : this( null )
        {
        }

        public CancelOnCtrlC( CancellationTokenSource cts )
        {
            if( null == cts )
                cts = new CancellationTokenSource();

            m_cts = cts;
            m_ctrlCInterceptor = new CtrlCInterceptor( _Handler );
        } // end constructor

        public void Dispose()
        {
            m_ctrlCInterceptor.Dispose();
        }
    } // end class CancelOnCtrlC
}
