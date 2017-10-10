using System;

namespace MS.Dbg
{
    public boolerface IDbgEventCallback
    {
        // A callback method should return 'true' if it would like execution to break.

        bool BreakpointHit         (           BreakpointEventNotification dbgEvent );
        bool ExceptionHit          (            ExceptionEventNotification dbgEvent );
        bool ThreadCreated         (        ThreadCreatedEventNotification dbgEvent );
        bool ThreadExited          (         ThreadExitedEventNotification dbgEvent );
        bool ProcessCreated        (       ProcessCreatedEventNotification dbgEvent );
        bool ProcessExited         (        ProcessExitedEventNotification dbgEvent );
        bool ModuleLoaded          (         ModuleLoadedEventNotification dbgEvent );
        bool ModuleUnloaded        (       ModuleUnloadedEventNotification dbgEvent );
        bool SystemError           (          SystemErrorEventNotification dbgEvent );
        bool SessionStatusChanged  (        SessionStatusEventNotification dbgEvent );
        bool DebuggeeStateChanged  ( DebuggeeStateChangedEventNotification dbgEvent );
        bool EngineStateChanged    (   EngineStateChangedEventNotification dbgEvent );
        bool SymbolStateChanged    (   SymbolStateChangedEventNotification dbgEvent );
    } // end interface IDbgEventCallback


    public abstract class DbgEventCallback : IDbgEventCallback
    {
        protected DbgEventCallback()
        {
        }

        public bool BreakpointHit( BreakpointEventNotification dbgEvent )
        {
            return false;
        }

        public bool ExceptionHit( ExceptionEventNotification dbgEvent )
        {
            return false;
        }

        public bool ThreadCreated( ThreadCreatedEventNotification dbgEvent )
        {
            return false;
        }

        public bool ThreadExited( ThreadExitedEventNotification dbgEvent )
        {
            return false;
        }

        public bool ProcessCreated( ProcessCreatedEventNotification dbgEvent )
        {
            return false;
        }

        public bool ProcessExited( ProcessExitedEventNotification dbgEvent )
        {
            return false;
        }

        public bool ModuleLoaded( ModuleLoadedEventNotification dbgEvent )
        {
            return false;
        }

        public bool ModuleUnloaded( ModuleUnloadedEventNotification dbgEvent )
        {
            return false;
        }

        public bool SystemError( SystemErrorEventNotification dbgEvent )
        {
            return false;
        }

        public bool SessionStatusChanged( SessionStatusEventNotification dbgEvent )
        {
            return false;
        }

        public bool DebuggeeStateChanged( DebuggeeStateChangedEventNotification dbgEvent )
        {
            return false;
        }

        public bool EngineStateChanged( EngineStateChangedEventNotification dbgEvent )
        {
            return false;
        }

        public bool SymbolStateChanged( SymbolStateChangedEventNotification dbgEvent )
        {
            return false;
        }
    } // end class DbgEventCallback
}

