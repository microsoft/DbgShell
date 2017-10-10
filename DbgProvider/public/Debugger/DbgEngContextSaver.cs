using System;

namespace MS.Dbg
{
    public sealed class DbgEngContextSaver : IDisposable
    {
        private DbgEngDebugger m_debugger;
        private DbgEngContext m_oldContext;
        public readonly DbgEngContext Context;

        public DbgEngContextSaver( DbgEngDebugger debugger, DbgEngContext temporaryContext )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            if( null == temporaryContext )
                throw new ArgumentNullException( "temporaryContext" );

            m_debugger = debugger;
            m_oldContext = debugger.GetCurrentDbgEngContext();
            Context = temporaryContext;
            debugger.SetCurrentDbgEngContext( temporaryContext, true );
        } // end constructor

        public void Dispose()
        {
            try
            {
                m_debugger.SetCurrentDbgEngContext( m_oldContext, true );
            }
            catch( DbgEngException dee )
            {
                if( dee.HResult == DebuggerObject.E_NOINTERFACE )
                    LogManager.Trace( "Failed to restore context: looks like it is gone." );
                else
                    throw;
             // else
             //     LogManager.Trace( "Failed to restore context: {0}",
             //                       Util.GetExceptionMessages( dee ) );
            }
        } // end Dispose()
    } // end class DbgEngContextSaver
}

