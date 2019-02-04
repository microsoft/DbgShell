using System;
using System.Linq;
using Microsoft.Diagnostics.Runtime.Interop;
using DbgEngWrapper;

namespace MS.Dbg
{
    public partial class DbgEngDebugger : DebuggerObject
    {
        // This implements the "real" dbgeng callback interface. There can only be one
        // event callback object registered with dbgeng; we'll be that one, and then
        // forward event notifications on to our own subscribers.
        private class RealDebugEventCallbacks : IDebugEventCallbacksWideImp
        {
            private DbgEngDebugger m_debugger;
            private PipeCallbackSettings m_psPipeSettings;

            private ulong m_executionStatusCookie;

            /// <summary>
            ///    A value that is updated whenever dbgeng's execution status changes.
            /// </summary>
            public ulong ExecStatusCookie => m_executionStatusCookie;


            private IPipelineCallback _PsPipe
            {
                get { return m_psPipeSettings.Pipeline; }
            }



            // For guest mode, when sharing the console (e.g. with ntsd, we don't want
            // both our modload events showing up /and/ ntsd's).
            private bool m_suppressOutput;


            public RealDebugEventCallbacks( DbgEngDebugger debugger, PipeCallbackSettings psPipe )
            {
                if( null == psPipe )
                    throw new ArgumentNullException( "psPipe" );

                if( null == debugger )
                    throw new ArgumentNullException( "debugger" );

                m_psPipeSettings = psPipe;
                m_debugger = debugger;
            } // end constructor


            public PipeCallbackSettings SetPipe( PipeCallbackSettings newPipeSettings )
            {
                var oldPipe = m_psPipeSettings;
                m_psPipeSettings = newPipeSettings;
                return oldPipe;
            } // end SetPipe()


            /// <summary>
            ///    True if we should not WriteObject events that we normally would. (used
            ///    in guest mode when dormant and sharing the console with a console-based
            ///    debugger such as ntsd)
            /// </summary>
            public bool SuppressOutput
            {
                get { return m_suppressOutput; }
                set { m_suppressOutput = value; }
            }


            public int GetInterestMask( out DEBUG_EVENT Mask )
            {
                // We use the event callbacks to create nice managed objects representing the events,
                // therefore we are "interested" in all of them. But we will return DEBUG_STATUS.NO_CHANGE
                // (0) from all of them, relying on the built-in filters to decide if we should break or not.
                // TODO: I do however need to pay attention to the built-in filters to know which notification
                // objects I should write to the output stream (currently I write them all).
                Mask = DEBUG_EVENT.BREAKPOINT |
                        DEBUG_EVENT.EXCEPTION |
                        DEBUG_EVENT.CREATE_THREAD |
                        DEBUG_EVENT.EXIT_THREAD |
                        DEBUG_EVENT.CREATE_PROCESS |
                        DEBUG_EVENT.EXIT_PROCESS |
                        DEBUG_EVENT.LOAD_MODULE |
                        DEBUG_EVENT.UNLOAD_MODULE |
                        DEBUG_EVENT.SYSTEM_ERROR |
                        DEBUG_EVENT.SESSION_STATUS |
                        DEBUG_EVENT.CHANGE_DEBUGGEE_STATE |
                        DEBUG_EVENT.CHANGE_ENGINE_STATE |
                        DEBUG_EVENT.CHANGE_SYMBOL_STATE;
                return 0;
            } // end GetInterestMask()


            private int _RaiseEvent< TArgs >( EventHandler< TArgs > handlers, TArgs args )
                where TArgs : DbgEventArgs
            {
                if( null == handlers )
                    return 0;

                foreach( var handler in handlers.GetInvocationList() )
                {
                    try
                    {
                        // REVIEW: should sender be debugger?
                        handler.DynamicInvoke( null, args );
                    }
                    catch( Exception e )
                    {
                        // TODO: write error... remove callback? failfast?
                        Util.FailFast( "Unexpected exception from event callback.", e );
                    }
                }
                return args.ShouldBreak ? (int) DEBUG_STATUS.BREAK : 0;
            } // end _RaiseEvent()


            private bool _FiltersSayOutput( DbgEventArgs notification )
            {
                // TODO: this is super awkward...
                if( null == m_debugger.m_exceptionEventFilterIndex )
                    m_debugger._GetEventFilters();

                DbgEventFilter filter = notification.FindMatchingFilter( m_debugger.m_specificEventFilterIndex, m_debugger.m_exceptionEventFilterIndex );
                if( null == filter )
                {
                    // TODO: need to figure out how to decide if these events should be outputted
                    return false;
                }

                if( (filter.ExecutionOption == DEBUG_FILTER_EXEC_OPTION.BREAK) ||
                    (filter.ExecutionOption == DEBUG_FILTER_EXEC_OPTION.OUTPUT) )
                {
                    return true;
                }

                if( filter.ExecutionOption == DEBUG_FILTER_EXEC_OPTION.SECOND_CHANCE_BREAK )
                {
                    ExceptionEventArgs eea = notification as ExceptionEventArgs;
                    if( null == eea )
                    {
                        Util.Fail( "How did we get a filter set to 2nd-chance break when the event is not an exception event?" );
                        return true;
                    }
                    return !eea.IsFirstChance;
                }

                return false;
            } // end _FiltersSayOutput()


            private bool _ShouldOutput( int status, DbgEventArgs eventArgs )
            {
                if( m_suppressOutput )
                    return false;

                return (status != 0) || _FiltersSayOutput( eventArgs );
            } // end _ShouldOutput()


            public int Breakpoint( WDebugBreakpoint Bp )
            {
                try
                {
                    var bi = new DbgBreakpointInfo( m_debugger, Bp );
                    if( bi.IsOneShot )
                    {
                        // If we /hit/ a breakpoint, and it was "one-shot", then it's
                        // going to get deleted out from under us. Prevent AVs later by
                        // marking it invalid now.
                        bi.MarkInvalid();
                    }

                    var eventArgs = new BreakpointEventArgs( m_debugger, bi );
                    int retVal = _RaiseEvent( m_debugger.BreakpointHit, eventArgs );
                    if( _ShouldOutput( retVal, eventArgs ) )
                    {
                        // Special case for "gu"/"step out" breakpoints: we're going to
                        // pretend they don't exist.
                        if( DbgBreakpointInfo.StepOutBreakpointId != bi.Id )
                            _PsPipe.WriteObject( eventArgs );
                    }
                    return retVal;
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception during event callback.", e );
                    return 0;
                }
            } // end Breakpoint()

            public int Exception( ref EXCEPTION_RECORD64 Exception, uint FirstChance )
            {
                try
                {
                    var eventArgs = new ExceptionEventArgs( m_debugger, Exception, FirstChance != 0 );
                    int retVal = _RaiseEvent( m_debugger.ExceptionHit, eventArgs );
                    if( _ShouldOutput( retVal, eventArgs ) )
                    {
                        _PsPipe.WriteObject( eventArgs );
                    }
                    return retVal;
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception during event callback.", e );
                    return 0;
                }
            } // end Exception()

            public int CreateThread( ulong Handle, ulong DataOffset, ulong StartOffset )
            {
                try
                {
                    var eventArgs = new ThreadCreatedEventArgs( m_debugger, Handle, DataOffset, StartOffset );
                    int retVal = _RaiseEvent( m_debugger.ThreadCreated, eventArgs );
                    if( _ShouldOutput( retVal, eventArgs ) )
                    {
                        _PsPipe.WriteObject( eventArgs );
                    }
                    return retVal;
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception during event callback.", e );
                    return 0;
                }
            } // end CreateThread()

            public int ExitThread( uint ExitCode )
            {
                try
                {
                    var eventArgs = new ThreadExitedEventArgs( m_debugger, ExitCode );
                    int retVal = _RaiseEvent( m_debugger.ThreadExited, eventArgs );
                    if( _ShouldOutput( retVal, eventArgs ) )
                    {
                        _PsPipe.WriteObject( eventArgs );
                    }
                    return retVal;
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception during event callback.", e );
                    return 0;
                }
            } // end ExitThread()

            public int CreateProcess( ulong ImageFileHandle,
                                      ulong Handle,
                                      ulong BaseOffset,
                                      uint ModuleSize,
                                      string ModuleName,
                                      string ImageName,
                                      uint CheckSum,
                                      uint TimeDateStamp,
                                      ulong InitialThreadHandle,
                                      ulong ThreadDataOffset,
                                      ulong StartOffset )
            {
                try
                {
                    var eventArgs = new ProcessCreatedEventArgs( m_debugger,
                                                                 ImageFileHandle,
                                                                 Handle,
                                                                 BaseOffset,
                                                                 ModuleSize,
                                                                 ModuleName,
                                                                 ImageName,
                                                                 CheckSum,
                                                                 TimeDateStamp,
                                                                 InitialThreadHandle,
                                                                 ThreadDataOffset,
                                                                 StartOffset );
                    int retVal = _RaiseEvent( m_debugger.ProcessCreated, eventArgs );
                    if( _ShouldOutput( retVal, eventArgs ) )
                    {
                        _PsPipe.WriteObject( eventArgs );
                    }
                    return retVal;
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception during event callback.", e );
                    return 0;
                }
            } // end CreateProcess()

            public int ExitProcess( uint ExitCode )
            {
                try
                {
                    var eventArgs = new ProcessExitedEventArgs( m_debugger, ExitCode );
                    int retVal = _RaiseEvent( m_debugger.ProcessExited, eventArgs );
                    if( _ShouldOutput( retVal, eventArgs ) )
                    {
                        _PsPipe.WriteObject( eventArgs );
                    }
                    return retVal;
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception during event callback.", e );
                    return 0;
                }
            } // end ExitProcess()

            public int LoadModule( ulong ImageFileHandle,
                                   ulong BaseOffset,
                                   uint ModuleSize,
                                   string ModuleName,
                                   string ImageName,
                                   uint CheckSum,
                                   uint TimeDateStamp )
            {
                try
                {
                    // TODO: BUGBUG: We have no way to know what the process context
                    // should be. We'll probably need a new callback API from dbgeng. :(
                    DbgModuleInfo modInfo = new DbgModuleInfo( m_debugger,
                                                               m_debugger.m_debugSymbols,
                                                             //ImageFileHandle,
                                                               BaseOffset,
                                                               ModuleSize,
                                                               ModuleName,
                                                               ImageName,
                                                               CheckSum,
                                                               TimeDateStamp,
                                                               null ); // <-- process
                    m_debugger.DiscardCachedModuleInfo();
                    var eventArgs = new ModuleLoadedEventArgs( m_debugger,
                                                               ImageFileHandle,
                                                               modInfo );
                    int retVal = _RaiseEvent( m_debugger.ModuleLoaded, eventArgs );
                    if( _ShouldOutput( retVal, eventArgs ) )
                    {
                        _PsPipe.WriteObject( eventArgs );
                    }
                    return retVal;
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception during event callback.", e );
                    return 0;
                }
            } // end LoadModule()

            public int UnloadModule( string ImageBaseName, ulong BaseOffset )
            {
                // I wish we could do something a bit more specific based on ImageBaseName
                // and BaseOffset, but we don't know what the target is, and we can't call
                // into dbgeng to find out. (something like "nsTarget.RepresentedObject.ModuleUnloaded( ImageBaseName, BaseOffset );")
                m_debugger.DiscardCachedModuleInfo();

                try
                {
                    // TODO: the event args should take a ModuleInfo
                    var eventArgs = new ModuleUnloadedEventArgs( m_debugger,
                                                                 ImageBaseName,
                                                                 BaseOffset );
                    int retVal = _RaiseEvent( m_debugger.ModuleUnloaded, eventArgs );
                    if( _ShouldOutput( retVal, eventArgs ) )
                    {
                        _PsPipe.WriteObject( eventArgs );
                    }
                    return retVal;
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception during event callback.", e );
                    return 0;
                }
            } // end UnloadModule()

            public int SystemError( uint Error, uint Level )
            {
                try
                {
                    var eventArgs = new SystemErrorEventArgs( m_debugger, Error, Level );
                    int retVal = _RaiseEvent( m_debugger.SystemError, eventArgs );
                    if( _ShouldOutput( retVal, eventArgs ) )
                    {
                        _PsPipe.WriteObject( eventArgs );
                    }
                    return retVal;
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception during event callback.", e );
                    return 0;
                }
            } // end SystemError()

            public int SessionStatus( DEBUG_SESSION Status )
            {
                try
                {
                    var eventArgs = new SessionStatusEventArgs( m_debugger, Status );
                    int retVal = _RaiseEvent( m_debugger.SessionStatusChanged, eventArgs );
                    if( _ShouldOutput( retVal, eventArgs ) )
                    {
                        _PsPipe.WriteObject( eventArgs );
                    }
                    return retVal;
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception during event callback.", e );
                    return 0;
                }
            } // end SessionStatus()

            public int ChangeDebuggeeState( DEBUG_CDS Flags, ulong Argument )
            {
                try
                {
                    var eventArgs = new DebuggeeStateChangedEventArgs( m_debugger, Flags, Argument );
                    int retVal = _RaiseEvent( m_debugger.DebuggeeStateChanged, eventArgs );
                    if( _ShouldOutput( retVal, eventArgs ) )
                    {
                        _PsPipe.WriteObject( eventArgs );
                    }
                    return retVal;
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception during event callback.", e );
                    return 0;
                }
            } // end ChangeDebuggeeState()

            public int ChangeEngineState( DEBUG_CES Flags, ulong Argument )
            {
                LogManager.Trace( "ChangeEngineState: {0}, 0x{1:x}", Flags, Argument );
                try
                {
                    var eventArgs = new EngineStateChangedEventArgs( m_debugger, Flags, Argument );
                    if( eventArgs.Flags.HasFlag( DEBUG_CES.EVENT_FILTERS ) )
                    {
                        // Need to refresh our filters.
                        if( eventArgs.Argument == DbgEventArgs.DEBUG_ANY_ID )
                        {
                            m_debugger._ResetFilters();
                        }
                        else
                        {
                            m_debugger._RefreshSingleFilter( (uint) eventArgs.Argument );
                        }
                    }

                    if( eventArgs.Flags.HasFlag( DEBUG_CES.BREAKPOINTS ) )
                    {
                        // Need to refresh our breakpoints.
                        m_debugger.FixupBpStuff( eventArgs.Argument );
                    }

                    if( (eventArgs.Flags.HasFlag( DEBUG_CES.SYSTEMS )) )
                    {
                        // DbgEng doesn't seem to set up its symbol path until you first
                        // attach to something, but it doesn't raise the event saying that
                        // the symbol path has changed when that happens. As a workaround,
                        // we'll dump our cached symbol path here.
                        m_debugger.m_sympath = null;
                        // Another oddity with the symbol path: before attaching to any
                        // system, dbgeng will say that the symbol path is blank. Then,
                        // once it gets attached to something, it will append the value of
                        // the environment variable _NT_SYMBOL_PATH to it.

                        if( (DEBUG_ANY_ID != (uint) eventArgs.Argument) )
                        {
                            // We're adding a system. This might be a handy spot to do
                            // something, so I'm leaving the condition here even though I'm
                            // not currently using it.
                            // (this gets traced by the eventArgs constructor)
                        }
                        else
                        {
                            // Removing a system. (this gets traced by the eventArgs
                            // constructor)
                        }
                    }

                    if( eventArgs.Flags.HasFlag( DEBUG_CES.EFFECTIVE_PROCESSOR ) )
                    {
                        //DbgProvider.ForceRebuildNamespace();
                        LogManager.Trace( "Effective processor changed; queueing request to rebuild namespace." );
                        DbgProvider.RequestExecuteBeforeNextPrompt( "[MS.Dbg.DbgProvider]::ForceRebuildNamespace()" );
                    }

                    if( eventArgs.Flags.HasFlag( DEBUG_CES.EXECUTION_STATUS ) )
                    {
                        m_executionStatusCookie++;
                    }

                    int retVal = _RaiseEvent( m_debugger.EngineStateChanged, eventArgs );
                    if( _ShouldOutput( retVal, eventArgs ) )
                    {
                        _PsPipe.WriteObject( eventArgs );
                    }

                    if( DEBUG_CES.EXECUTION_STATUS == Flags )
                    {
                        var status = (DEBUG_STATUS) (Argument & (ulong) DEBUG_STATUS.MASK);
                        bool insideWait = false;
                        bool waitTimedOut = false;
                        if( status != (DEBUG_STATUS) Argument )
                        {
                            insideWait = 0 != (Argument & (ulong) DEBUG_STATUS_FLAGS.INSIDE_WAIT);
                            waitTimedOut = 0 != (Argument & (ulong) DEBUG_STATUS_FLAGS.WAIT_TIMEOUT);
                        }

                        LogManager.Trace( "EXECUTION_STATUS changed. Argument: 0x{0:x} ({1}{2}{3})",
                                          Argument,
                                          status,
                                          insideWait ? " + INSIDE_WAIT" : "",
                                          waitTimedOut ? " + WAIT_TIMEOUT" : "" );

                        DbgEngDebugger._GlobalDebugger.DbgEngCookie++;
                        if( 0 == ((ulong) DEBUG_STATUS_FLAGS.INSIDE_WAIT & Argument) )
                        {
                         // Formerly, we would use this code to signal the currently-
                         // running cmdlet to exit its message loop. The idea was that
                         // there had been problems with more notification events arriving
                         // /after/ WaitForEvent returned. But I can't currently repro
                         // that. (There /are/ some notifications that occur after
                         // WaitForEvent returns, but they are caused by my own actions--
                         // setting assembly options and text aliases.)
                         //
                         // There is also the difference that now I call WaitForEvent on
                         // the dbgeng thread, whereas originally I had been calling it on
                         // a threadpool thread. I'm not sure how that would have made a
                         // difference, though.
                         //
                         // For now, I'm going to get rid of this stuff. It seems fragile
                         // and horribly complicated--I really hope I don't have to bring
                         // it back.
                         //
                         // // TODO: This seems terribly fragile. But I don't see how else to make
                         // // sure we process all callbacks before exiting ProcessRecord. I'm probably
                         // // going to have to add more cases... :(
                         // if( DEBUG_STATUS.BREAK == (DEBUG_STATUS) Argument )
                         // {
                         //     if( _SignalPipeOnBreak )
                         //     {
                         //         LogManager.Trace( "EXECUTION_STATUS is BREAK; signaling pipeline." );
                         //         _PsPipe.SignalDone();
                         //     }
                         //     else
                         //     {
                         //         LogManager.Trace( "EXECUTION_STATUS is BREAK, but we're ignoring it." );
                         //     }
                         // }

                            if( DEBUG_STATUS.NO_DEBUGGEE == (DEBUG_STATUS) Argument )
                            {
                                // We have no target. This might be a handy spot to do
                                // something, so I'm leaving the condition here even
                                // though I'm not currently using it.
                                LogManager.Trace( "No debuggee." );
                           }
                        } // end if( inside wait )
                    } // end if( exec status )

                    return retVal;
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception during event callback.", e );
                    return 0;
                }
            } // end ChangeEngineState()

            public int ChangeSymbolState( DEBUG_CSS Flags, ulong Argument )
            {
                try
                {
                    if( (0 != (int) (Flags & (DEBUG_CSS.LOADS | DEBUG_CSS.UNLOADS))) )
                    {
                        // If we load symbols using DbgEng's Reload command, it will send
                        // us back an Argument of 0, and we'll be forced to throw away everything.
                        // But in at least some other cases it faithfully passes along the 
                        // base address - we still don't know which target it is for, but it is
                        // at least for no more than one module.
                        m_debugger.DiscardCachedModuleInfo( Argument );

                        // TODO: BUGBUG: To do this right requires knowing the current
                        // context, AND being able to get the dbghelp handle for it. When
                        // we call into our DbgHelp wrapper to dump synthetic type info,
                        // we want to be able to look it up by hProc (the pseudo handle to
                        // the process) (which if we could call into dbgeng, we would get
                        // by calling IDebugSystemObjects.GetCurrentProcessHandle, but
                        // that's a no-no here). Instead we just dump synthetic type info
                        // for all processes.
                        m_debugger.BumpSymbolCookie( modBase: Argument );
                    } // end if( we need to update module info )

                    if( (0 != (int) (Flags & DEBUG_CSS.SCOPE)) )
                    {
                        // TODO: I don't really know what a change in symbol "scope"
                        // means... but its one of the things that gets fired when I do
                        // "Invoke-DbgEng '.reload /f msvcrt.dll'", so I guess I need to
                        // refresh symbol info when that happens. Need to ask dbgsig about
                        // the proper way to detect symbol reload.
                        m_debugger.DiscardCachedModuleInfo();
                    }

                    if( (0 != (int) (Flags & DEBUG_CSS.PATHS)) )
                    {
                        m_debugger.m_sympath = null;
                    }

                    var eventArgs = new SymbolStateChangedEventArgs( m_debugger, Flags, Argument );
                    int retVal = _RaiseEvent( m_debugger.SymbolStateChanged, eventArgs );
                    if( _ShouldOutput( retVal, eventArgs ) )
                    {
                        _PsPipe.WriteObject( eventArgs );
                    }
                    return retVal;
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception during event callback.", e );
                    return 0;
                }
            } // end ChangeSymbolState()
        } // end class RealDebugEventCallbacks
    } // end class DbgEngDebugger
}
