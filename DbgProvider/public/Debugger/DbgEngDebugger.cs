using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;
using DbgEngWrapper;
using MS.Dbg.Commands;
using MS.Dbg.Formatting;

namespace MS.Dbg
{
    public partial class DbgEngDebugger : DebuggerObject
    {
        private static DbgEngDebugger g_Debugger;


        internal static DbgEngDebugger _GlobalDebugger
        {
            get
            {
                if( null == g_Debugger )
                {
                    g_Debugger = DbgEngThread.Singleton.Execute( () =>
                        {
                            WDebugClient dc;
                            StaticCheckHr( WDebugClient.DebugCreate( out dc ) );
                            return new DbgEngDebugger( dc, DbgEngThread.Singleton );
                        } );
                }
                return g_Debugger;
            }
        } // end property _GlobalDebugger


        /// <summary>
        ///    Gets a DbgEngDebugger object.
        /// </summary>
        internal static DbgEngDebugger NewDebugger()
        {
            // There's really just a single object. But I don't want them to know that.
            return _GlobalDebugger;
        }

        internal void LoadCrashDump( string dumpFileName,
                                   string targetFriendlyName )
        {
            DbgEngThread.Singleton.Execute( () =>
                {
                    CheckHr( m_debugClient.OpenDumpFileWide( dumpFileName, 0 ) );
                    SetNextTargetName( targetFriendlyName );
                } );
        }

        public static DbgEngDebugger OpenDumpFile( string dumpFileName, string targetFriendlyName = "" )
        {
            return OpenDumpFileAsync( dumpFileName, targetFriendlyName ).Result;
        }

        public static async Task< DbgEngDebugger > OpenDumpFileAsync( string dumpFileName, string targetFriendlyName = "" )
        {
            return await DbgEngThread.Singleton.ExecuteAsync( () =>
             {
                 if( String.IsNullOrEmpty( targetFriendlyName ) )
                 {
                     targetFriendlyName = dumpFileName;
                 }
                 var debugger = _GlobalDebugger;
                 debugger.LoadCrashDump( dumpFileName, targetFriendlyName );
                 debugger.CheckHr( debugger.m_debugControl.WaitForEvent( DEBUG_WAIT.DEFAULT, UInt32.MaxValue ) );
                 debugger.GetCurrentTargetInternal();
                 return debugger;
             } );
        }

        public void AttachToProcess( int pid, IPipelineCallback psPipe )
        {
            AttachToProcess( pid, false, false, DEBUG_ATTACH.DEFAULT, null );
        }


        public void AttachToProcess( int pid,
                                     bool skipInitialBreakpoint,
                                     bool skipFinalBreakpoint,
                                     DEBUG_ATTACH attachType,
                                     string targetFriendlyName )
        {
            DbgEngThread.Singleton.Execute( () =>
                {
                    int hr = m_debugClient.AttachProcess( 0, (uint) pid, attachType );
                    if( unchecked( (int) HR_STATUS_PORT_ALREADY_SET ) == hr )
                    {
                        throw new DbgProviderException( Util.Sprintf( "Cannot attach to process 0x{0:x}; it is already being debugged. Try using the -ReAttach parameter.",
                                                                      pid ),
                                                        "AttachFailed_STATUS_PORT_ALREADY_SET",
                                                        System.Management.Automation.ErrorCategory.OpenError,
                                                        pid ).SetHResult( hr );
                        // TODO: Or should I just -ReAttach for them?
                    }
                    else if( unchecked( (int) HR_STATUS_PORT_NOT_SET ) == hr )
                    {
                        Util.Assert( attachType == DEBUG_ATTACH.EXISTING );
                        throw new DbgProviderException( Util.Sprintf( "Cannot re-attach to process 0x{0:x}; there is no DebugPort associated with the process. Try removing the -ReAttach parameter.",
                                                                      pid ),
                                                        "AttachFailed_STATUS_PORT_NOT_SET",
                                                        System.Management.Automation.ErrorCategory.OpenError,
                                                        pid ).SetHResult( hr );
                        // TODO: Or should I just get rid of -ReAttach for them?
                    }
                    else if( unchecked( (int) HR_STATUS_NOT_SUPPORTED ) == hr )
                    {
                        throw new DbgProviderException( Util.Sprintf( "Cannot attach to process 0x{0:x}; dbgeng says \"not supported\". Are you trying to use a 32-bit debugger to attach to a 64-bit process?",
                                                                      pid ),
                                                        "AttachFailed_STATUS_NOT_SUPPORTED",
                                                        System.Management.Automation.ErrorCategory.OpenError,
                                                        pid ).SetHResult( hr );
                    }

                    StaticCheckHr( hr );

                    SkipInitialBreakpoint = skipInitialBreakpoint;
                    SkipFinalBreakpoint = skipFinalBreakpoint;

                    SetNextTargetName( targetFriendlyName );
                } );
        } // end AttachToProcess()


        public static DbgEngDebugger CreateProcessAndAttach( string commandLine, string targetFriendlyName )
        {
            return CreateProcessAndAttachAsync( commandLine, targetFriendlyName, false, false ).Result;
        }

        public async static Task< DbgEngDebugger > CreateProcessAndAttachAsync( string commandLine,
                                            string targetFriendlyName,
                                            bool skipInitialBreakpoint,
                                            bool skipFinalBreakpoint )
        {
            return await DbgEngThread.Singleton.ExecuteAsync( () =>
                {
                    var debugger = _GlobalDebugger;

                    DEBUG_CREATE_PROCESS processCreationFlags = DEBUG_CREATE_PROCESS.DEBUG_PROCESS |
                                                                DEBUG_CREATE_PROCESS.DEBUG_ONLY_THIS_PROCESS |
                                                                DEBUG_CREATE_PROCESS.CREATE_NEW_CONSOLE;
                    StaticCheckHr( debugger.m_debugClient.CreateProcessAndAttachWide( 0, // server
                                                                  commandLine,
                                                                  processCreationFlags,
                                                                  0, // processId
                                                                  DEBUG_ATTACH.DEFAULT ) );

                    debugger.SkipInitialBreakpoint = skipInitialBreakpoint;
                    debugger.SkipFinalBreakpoint = skipFinalBreakpoint;
                    debugger.SetNextTargetName( targetFriendlyName );
                    return debugger;
                } );
        } // end CreateProcessAndAttach()


        public unsafe void CreateProcessAndAttach2( string commandLine,
                                                    string initialDirectory,
                                                    string targetFriendlyName,
                                                    bool skipInitialBreakpoint,
                                                    bool skipFinalBreakpoint,
                                                    bool noDebugHeap,
                                                    bool debugChildProcesses,
                                                    CreateProcessConsoleOption consoleOption )
        {
            DbgEngThread.Singleton.Execute( () =>
                {
                    var debugger = _GlobalDebugger;

                    DEBUG_CREATE_PROCESS processCreationFlags = DEBUG_CREATE_PROCESS.DEFAULT; // 0

                    if( noDebugHeap )
                        processCreationFlags |= DEBUG_CREATE_PROCESS.NO_DEBUG_HEAP;

                    if( debugChildProcesses )
                        processCreationFlags |= DEBUG_CREATE_PROCESS.DEBUG_PROCESS;
                    else
                        processCreationFlags |= DEBUG_CREATE_PROCESS.DEBUG_ONLY_THIS_PROCESS;

                    switch( consoleOption )
                    {
                        case CreateProcessConsoleOption.NoConsole:
                            processCreationFlags |= DEBUG_CREATE_PROCESS.CREATE_NO_WINDOW;
                            break;

                        case CreateProcessConsoleOption.ReuseCurrentConsole:
                            // nothing
                            break;

                        case CreateProcessConsoleOption.NewConsole:
                            processCreationFlags |= DEBUG_CREATE_PROCESS.CREATE_NEW_CONSOLE;
                            break;
                    }

                    DEBUG_CREATE_PROCESS_OPTIONS options = new DEBUG_CREATE_PROCESS_OPTIONS()
                    {
                        CreateFlags = processCreationFlags
                    };

                    StaticCheckHr( debugger.m_debugClient.CreateProcessAndAttach2Wide(
                            0, // server
                            commandLine,
                            &options,
                            //(uint) Marshal.SizeOf( typeof( DEBUG_CREATE_PROCESS_OPTIONS ) ),
                            initialDirectory,
                            null, // environment
                            0,    // processId
                            DEBUG_ATTACH.DEFAULT ) );

                    debugger.SkipInitialBreakpoint = skipInitialBreakpoint;
                    debugger.SkipFinalBreakpoint = skipFinalBreakpoint;
                    debugger.SetNextTargetName( targetFriendlyName );
                } );
        } // end CreateProcessAndAttach2()


        public static DbgEngDebugger ConnectToServer( string remoteParams, IPipelineCallback psPipe )
        {
            return DbgEngThread.Singleton.Execute(() => {

                psPipe.WriteWarning( "Unfortunately remote clients are not really " +
                                     "supported. In particular, you won't be able to " +
                                     "get any type information until we get better " +
                                     "support from the dbgeng API." );

                // TODO TODO BUGBUG: reconcile with global g_Debugger... can I just swap
                // out its m_debugClient?
                WDebugClient dc;
                StaticCheckHr( WDebugClient.DebugConnect( remoteParams, out dc ) );

                // TODO: re: isLive: Actually, couldn't it be a remote to a dump? And
                // maybe we need an isRemoteClient property.
                var debugger = new DbgEngDebugger( dc, DbgEngThread.Singleton );
                debugger._SetCurrentPipeline( new PipeCallbackSettings( psPipe ) );

                return debugger;
            } );
        } // end ConnectToServer()


        // We need to adjust column widths for columns that hold addresses.
        //
        // Note that we need to do this whether the target is 32- or 64-bit. Even though
        // the default in the .psfmt is one way or the other, we could detach and later
        // re-attach to a new, different-bitness target in the same runspace.
        //
        // Also note that this needs to be done late enough in the attach process that we
        // can determine if the target is 32- or 64-bit (basically last, after we've
        // broken in).
        //
        // N.B. The "bufferWidth" parameter has nothing to do with address width... it's
        // just a convenient place to let dbgeng know what our output width is.
        internal void AdjustAddressColumnWidths( int bufferWidth )
        {
            var tableViewDefDict = AltFormattingManager.GetEntriesOfType( typeof( AltTableViewDefinition ) );
            foreach( var viewDefList in tableViewDefDict.Values )
            {
                foreach( var viewDef in viewDefList )
                {
                    _AdjustAddressColumnWidthsForTable( this, (AltTableViewDefinition) viewDef.ViewDefinition );
                } // end foreach( viewDef )
            } // end foreach( viewDefList )

            // We need to be notified if more definitions are registered (such as if all
            // the formatting info gets reloaded).
            //
            // It's okay to over-unsubscribe, so this serves to make sure we aren't
            // registered multiple times.
            AltFormattingManager.ViewDefinitionRegistered -= _ViewDefinitionRegistered;
            AltFormattingManager.ViewDefinitionRegistered += _ViewDefinitionRegistered;
            // TODO: De-register! (when we have code to detach)

            // Other things that are convenient to do here:
            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugClient.SetOutputWidth( (uint) bufferWidth ) );
                    // TODO: BUG: If the user had set the assembly options to something
                    // else, then disconnected and reconnected, they will be annoyed that
                    // we keep resetting it back to IgnoreOutputWidth.
                    CheckHr( m_debugControl.SetAssemblyOptions( DEBUG_ASMOPT.IGNORE_OUTPUT_WIDTH ) );
                } );
        } // end AdjustAddressColumnWidths()


        private void _ViewDefinitionRegistered( object sender, AltTypeFormatEntry e )
        {
            foreach( var vdi in e.ViewDefinitions )
            {
                var tableView = vdi.ViewDefinition as AltTableViewDefinition;
                if( null != tableView )
                {
                    _AdjustAddressColumnWidthsForTable( this, tableView );
                }
            }
        } // end _ViewDefinitionRegistered()


        private static void _AdjustAddressColumnWidthsForTable( DbgEngDebugger debugger, AltTableViewDefinition tableView )
        {
            for( int colIdx = 0; colIdx < tableView.Columns.Count; colIdx++ )
            {
                if( 0 == Util.Strcmp_OI( tableView.Columns[ colIdx ].Tag, "Address" ) )
                {
                    tableView.SetColumnWidth( colIdx, debugger.TargetIs32Bit ? 8 : 17 );
                }
            } // end for( each column )
        } // end _AdjustAddressColumnWidthsForTable()


        public Task< TRet > ExecuteOnDbgEngThreadAsync< TRet >( Func< TRet > f )
        {
            return m_dbgEngThread.ExecuteAsync( f );
        }

        public Task ExecuteOnDbgEngThreadAsync( Action a )
        {
            return m_dbgEngThread.ExecuteAsync( a );
        }

        public TRet ExecuteOnDbgEngThread< TRet >( Func< TRet > f )
        {
            return m_dbgEngThread.Execute( f );
        }

        public void ExecuteOnDbgEngThread( Action a )
        {
            m_dbgEngThread.Execute( a );
        }

        public IEnumerable< TRet > StreamFromDbgEngThread< TRet >( CancellationToken cancelToken,
                                                                   Action< CancellationToken, Action< TRet > > producer )
        {
            using( var bcHolder = new BlockingCollectionHolder< TRet >( cancelToken ) )
            {
                Task t = ExecuteOnDbgEngThreadAsync( () =>
                    {
                        try
                        {
                            producer( bcHolder.CancelToken, ( x ) => bcHolder.Add( x ) );
                        }
                        finally
                        {
                            bcHolder.CompleteAdding();
                        }
                    } ); // end dbgeng thread task

                foreach( var item in bcHolder.GetConsumingEnumerable() )
                {
                    yield return item;
                }

                Util.Await( t ); // in case it threw
            } // end using( bcHolder )
        } // end StreamFromDbgEngThread()


        private object m_syncRoot = new object();

        public WDebugClient DebuggerInterface { get; private set; }

        internal DataTarget CreateDataTargetForProcess( DbgTarget target )
        {
            return DataTarget.CreateFromDataReader( new DbgShellDebugClientDataReader( this, target ) );
        } // end CreateDataTargetForProcess()


        private DbgEngThread m_dbgEngThread;

        private HoldingPipelineCallback m_holdingPipe = new HoldingPipelineCallback();

        private WDebugControl m_debugControl;
        private WDebugClient m_debugClient;
        private WDebugSystemObjects m_debugSystemObjects;
        private WDebugSymbols m_debugSymbols;
        private WDebugDataSpaces m_debugDataSpaces;
        private WDebugAdvanced m_debugAdvanced;


        internal void ReleaseEverything()
        {
            ExecuteOnDbgEngThread( () =>
                {
                    m_debugClient.SetEventCallbacksWide( null );
                    m_debugClient.SetInputCallbacks( null );
                    m_debugClient.SetOutputCallbacksWide( null );

                    m_debugControl.Dispose();
                    m_debugSystemObjects.Dispose();
                    m_debugSymbols.Dispose();
                    m_debugDataSpaces.Dispose();
                    m_debugAdvanced.Dispose();
                    m_debugClient.Dispose();

                    m_debugControl = null;
                    m_debugSystemObjects = null;
                    m_debugSymbols = null;
                    m_debugDataSpaces = null;
                    m_debugAdvanced = null;
                    m_debugClient = null;
                } );

            m_dbgEngThread.Dispose();
        } // end ReleaseEverything()


        internal void Passivate()
        {
            m_dbgEngThread.SignalDone();
        } // end Passivate()


        // This needs to be called on the debugger engine thread.
        private DbgEngDebugger( WDebugClient debuggerInterface, DbgEngThread dbgEngThread )
            : base( null )
        {
            if( null == debuggerInterface )
                throw new ArgumentNullException( "debuggerInterface" );

            DebuggerInterface = debuggerInterface;
            m_recentNotificationBuf = new CircularBuffer< DbgEventArgs >( m_maxNotificationsToKeep );
            m_dbgEngThread = dbgEngThread;

            m_dbgEngThread.Execute( () =>
                {
                    m_debugControl = (WDebugControl) DebuggerInterface;
                    m_debugClient = (WDebugClient) DebuggerInterface;
                    m_debugSystemObjects = (WDebugSystemObjects) DebuggerInterface;
                    m_debugSymbols = (WDebugSymbols) DebuggerInterface;
                    m_debugDataSpaces = (WDebugDataSpaces) DebuggerInterface;
                    m_debugAdvanced = (WDebugAdvanced) DebuggerInterface;
                    _SetEventCallbacks( new RealDebugEventCallbacks( this, new PipeCallbackSettings( m_holdingPipe ) ) );

                    // DbgEng has terrible problems maintaining its list of output callbacks.
                    // I'm just going to set it once and re-use it. When we're not interested
                    // in its output, we'll just send it to the bitbucket.
                    var outputCallbacks = new DebugOutputCallbacks( "DbgEng> ", ( x ) => { } );
                    _SetOutputCallbacks( outputCallbacks );
                } );
        } // end constructor


        /// <summary>
        ///    Indicates if the target is live. However, you might be more interested in
        ///    the CanStep property, which includes iDNA traces.
        /// </summary>
        public bool IsLive
        {
            get
            {
                return ExecuteOnDbgEngThread( () =>
                    {
                        DEBUG_CLASS targetClass;
                        DEBUG_CLASS_QUALIFIER qualifier;
                        CheckHr( m_debugControl.GetDebuggeeType( out targetClass, out qualifier ) );
                        if( (DEBUG_CLASS.UNINITIALIZED == targetClass) ||
                            (DEBUG_CLASS.IMAGE_FILE == targetClass) )
                        {
                            return false;
                        }

                        if( (DEBUG_CLASS_QUALIFIER.KERNEL_IDNA == qualifier) ||
                            (DEBUG_CLASS_QUALIFIER.KERNEL_SMALL_DUMP == qualifier) ||
                            (DEBUG_CLASS_QUALIFIER.KERNEL_DUMP == qualifier) ||
                            (DEBUG_CLASS_QUALIFIER.KERNEL_FULL_DUMP == qualifier) ||
                            (DEBUG_CLASS_QUALIFIER.USER_WINDOWS_IDNA == qualifier) ||
                            (DEBUG_CLASS_QUALIFIER.USER_WINDOWS_SMALL_DUMP == qualifier) ||
                            (DEBUG_CLASS_QUALIFIER.USER_WINDOWS_DUMP == qualifier) )
                        {
                            return false;
                        }
                        return true;
                    } );
            }
        } // end property IsLive


        public bool CanStep
        {
            get
            {
                return ExecuteOnDbgEngThread( () =>
                    {
                        DEBUG_CLASS targetClass;
                        DEBUG_CLASS_QUALIFIER qualifier;
                        CheckHr( m_debugControl.GetDebuggeeType( out targetClass, out qualifier ) );
                        if( (DEBUG_CLASS.UNINITIALIZED == targetClass) ||
                            (DEBUG_CLASS.IMAGE_FILE == targetClass) )
                        {
                            return false;
                        }

                        if( (DEBUG_CLASS_QUALIFIER.KERNEL_SMALL_DUMP == qualifier) ||
                            (DEBUG_CLASS_QUALIFIER.KERNEL_DUMP == qualifier) ||
                            (DEBUG_CLASS_QUALIFIER.KERNEL_FULL_DUMP == qualifier) ||
                            (DEBUG_CLASS_QUALIFIER.USER_WINDOWS_SMALL_DUMP == qualifier) ||
                            (DEBUG_CLASS_QUALIFIER.USER_WINDOWS_DUMP == qualifier) )
                        {
                            return false;
                        }
                        return true;
                    } );
            }
        } // end property CanStep


        public bool IsTimeTravelTrace
        {
            get
            {
                return ExecuteOnDbgEngThread( () =>
                    {
                        DEBUG_CLASS targetClass;
                        DEBUG_CLASS_QUALIFIER qualifier;
                        CheckHr( m_debugControl.GetDebuggeeType( out targetClass, out qualifier ) );
                        if( (DEBUG_CLASS.UNINITIALIZED == targetClass) ||
                            (DEBUG_CLASS.IMAGE_FILE == targetClass) )
                        {
                            return false;
                        }

                        if( (DEBUG_CLASS_QUALIFIER.KERNEL_IDNA == qualifier) ||
                            (DEBUG_CLASS_QUALIFIER.USER_WINDOWS_IDNA == qualifier) )
                        {
                            return true;
                        }
                        return false;
                    } );
            }
        } // end property IsTimeTravelTrace


        public bool IsKernelMode
        {
            get
            {
                return ExecuteOnDbgEngThread( () =>
                    {
                        DEBUG_CLASS targetClass;
                        DEBUG_CLASS_QUALIFIER qualifier;
                        CheckHr( m_debugControl.GetDebuggeeType( out targetClass, out qualifier ) );
                        return (DEBUG_CLASS.KERNEL == targetClass);
                    } );
            }
        } // end property IsKernelMode()


        public bool IsUserMode
        {
            get
            {
                return ExecuteOnDbgEngThread( () =>
                    {
                        DEBUG_CLASS targetClass;
                        DEBUG_CLASS_QUALIFIER qualifier;
                        CheckHr( m_debugControl.GetDebuggeeType( out targetClass, out qualifier ) );
                        return (DEBUG_CLASS.USER_WINDOWS == targetClass) ||
                               (DEBUG_CLASS.IMAGE_FILE == targetClass);
                    } );
            }
        } // end property IsUserMode()


        private bool _IsEngineOptionSet( DEBUG_ENGOPT opt )
        {
            DEBUG_ENGOPT options;
            CheckHr( m_debugControl.GetEngineOptions( out options ) );
            return DEBUG_ENGOPT.NONE != (options & opt);
        } // end _IsEngineOptionSet()


        public void _SetEngineOption( DEBUG_ENGOPT opt, bool enable )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    if( enable )
                        CheckHr( m_debugControl.AddEngineOptions( opt ) );
                    else
                        CheckHr( m_debugControl.RemoveEngineOptions( opt ) );
                } );
        } // end _SetEngineOption()



        public bool SkipInitialBreakpoint
        {
            get { return !_IsEngineOptionSet( DEBUG_ENGOPT.INITIAL_BREAK ); }
            set { _SetEngineOption( DEBUG_ENGOPT.INITIAL_BREAK, !value ); }
        } // end property SkipInitialBreakpoint


        public bool SkipFinalBreakpoint
        {
            get { return !_IsEngineOptionSet( DEBUG_ENGOPT.FINAL_BREAK ); }
            set { _SetEngineOption( DEBUG_ENGOPT.FINAL_BREAK, !value ); }
        } // end property SkipFinalBreakpoint


        public bool NoTarget
        {
            get
            {
                return ExecuteOnDbgEngThread( () =>
                {
                    uint numSysIds;
                    CheckHr( m_debugSystemObjects.GetNumberSystems( out numSysIds ) );

                    if( numSysIds == 0 )
                        return true;

                    //
                    // We'll assume that if there is a system, then it should probably be
                    // the current system.
                    //

                    uint numProcs;
                    int hr = m_debugSystemObjects.GetNumberProcesses( out numProcs );

                    return ((hr == S_OK) && (numProcs == 0)) ||
                           (hr == E_UNEXPECTED);
                } );
            }
        }

        public IEnumerable< DbgUModeThreadInfo > EnumerateThreads()
        {
            uint numThreads = 0;
            uint sysId = 0;
            uint procId = 0;
            uint[] debuggerIds = null;
            uint[] sysTids = null;

            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugSystemObjects.GetNumberThreads( out numThreads ) );

                    // TODO: potential perf problem: uncomment this line, and note that it gets hit a lot--once
                    // for every thread... we are doing too much re-enumeration.
                    //Console.WriteLine( "There are {0} threads.", numThreads );

                    CheckHr( m_debugSystemObjects.GetThreadIdsByIndex( 0, numThreads, out debuggerIds, out sysTids ) );
                    CheckHr( m_debugSystemObjects.GetCurrentSystemId( out sysId ) );
                    CheckHr( m_debugSystemObjects.GetCurrentProcessId( out procId ) );
                } );

            Util.Assert( (null != debuggerIds) && (null != sysTids) );

            for( int i = 0; i < numThreads; i++ )
            {
                yield return new DbgUModeThreadInfo( this,
                                                     new DbgEngContext( sysId,
                                                                        procId,
                                                                        debuggerIds[ i ],
                                                                        0 ),
                                                     sysTids[ i ] );
            }
        } // end EnumerateThreads()


        internal DbgEngContext GetCurrentThreadContext( out uint sysTid )
        {
            uint sysId = 0;
            uint procId = 0;
            uint debuggerId = 0;
            uint _sysTid = 0;

            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugSystemObjects.GetCurrentSystemId( out sysId ) );
                    CheckHr( m_debugSystemObjects.GetCurrentProcessId( out procId ) );
                    CheckHr( m_debugSystemObjects.GetCurrentThreadId( out debuggerId ) );

                    if( !IsKernelMode )
                    {
                        CheckHr( m_debugSystemObjects.GetCurrentThreadSystemId( out _sysTid ) );
                    }
                } );
            sysTid = _sysTid;
            return new DbgEngContext( sysId,
                                      procId,
                                      debuggerId,
                                      0 );
        } // end GetCurrentThreadContext()


        public DbgUModeThreadInfo GetCurrentThread()
        {
            uint sysTid;
            var ctx = GetCurrentThreadContext( out sysTid );

            return new DbgUModeThreadInfo( this,
                                           ctx,
                                           sysTid );
        } // end GetCurrentThread()


        /// <summary>
        ///    Gets the thread for the last event. Can return null if no last event or no
        ///    thread associate with last event.
        /// </summary>
        public DbgUModeThreadInfo GetEventThread()
        {
            uint debuggerThreadId = DEBUG_ANY_ID;
            uint debuggerProcId = DEBUG_ANY_ID;
            ExecuteOnDbgEngThread( () =>
                {
                    // The event thread might be in a process other than the current one.
                    int hr = m_debugSystemObjects.GetEventProcess( out debuggerProcId );
                    if( E_UNEXPECTED == hr )
                    {
                        // No event thread, I guess. This can happen when killing and
                        // detaching. I suppose the "event thread" is in the process that
                        // is now gone.
                        return;
                    }
                    else
                    {
                        CheckHr( hr );
                    }
                    hr = m_debugSystemObjects.GetEventThread( out debuggerThreadId );
                    if( E_UNEXPECTED == hr )
                    {
                        // No event thread, I guess. This can happen when there is no
                        // specific thread associated with the last event (for example,
                        // 0xc0000194 (STATUS_POSSIBLE_DEADLOCK). In these cases, windbg
                        // will show -1 for the last event thread when you run .lastevent
                        // (like "Last event: b34.ffffffff: Unknown exception - code
                        // c0000194"), and when you first attach to such a dump, you might
                        // not have any thread context (the prompt will be "0:???>").
                        return;
                    }
                    else
                    {
                        CheckHr( hr );
                    }
                } );
            if( (DEBUG_ANY_ID == debuggerProcId) || (DEBUG_ANY_ID == debuggerThreadId) )
                return null; // no event thread.

            return GetUModeThreadByDebuggerId( debuggerProcId, debuggerThreadId );
        } // end GetEventThread()


        public DbgUModeThreadInfo GetUModeThreadByDebuggerId( uint debuggerTid )
        {
            return GetUModeThreadByDebuggerId( DEBUG_ANY_ID, debuggerTid );
        }


        public DbgUModeThreadInfo GetUModeThreadByDebuggerId( uint debuggerProcId,
                                                              uint debuggerThreadId )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    var curCtx = GetCurrentDbgEngContext();

                    // TODO: check user-mode?

                    if( DEBUG_ANY_ID == debuggerProcId ) // means "current process"
                    {
                        debuggerProcId = curCtx.RequireUModeProcessIndex();
                    }

                    DbgEngContext threadContext = new DbgEngContext( curCtx.SystemIndex,
                                                                     debuggerProcId,
                                                                     debuggerThreadId,
                                                                     0 );

                    using( new DbgEngContextSaver( this, threadContext ) )
                    {
                        uint[] debuggerIds;
                        uint[] sysTids;

                        uint numThreads;
                        CheckHr( m_debugSystemObjects.GetNumberThreads( out numThreads ) );

                        CheckHr( m_debugSystemObjects.GetThreadIdsByIndex( 0,
                                                                           numThreads,
                                                                           out debuggerIds,
                                                                           out sysTids ) );

                        // Unfortunately the index is NOT the same as the debugger id.
                        int idx = Array.IndexOf( debuggerIds, debuggerThreadId );
                        if( -1 == idx )
                        {
                            throw new DbgProviderException( Util.Sprintf( "Could not find info for thread {0} (system {1}, process {2}).",
                                                                          debuggerThreadId,
                                                                          curCtx.SystemIndex,
                                                                          debuggerProcId ),
                                                            "GetUModeThreadByDebuggerId_IndexNotFound",
                                                            System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                            debuggerThreadId );
                        }

                        return new DbgUModeThreadInfo( this, threadContext, sysTids[ idx ] );
                    } // end using( context saver )
                } );
        } // end GetUModeThreadByDebuggerId()


        public uint GetThreadDebuggerIdBySystemTid( uint tid )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    uint debuggerId;
                    CheckHr( m_debugSystemObjects.GetThreadIdBySystemId( tid, out debuggerId ) );
                    return debuggerId;
                } );
        } // end GetThreadDebuggerIdBySystemTid()


        public DbgUModeThreadInfo GetThreadBySystemTid( uint tid )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    var curCtx = GetCurrentDbgEngContext();
                    uint debuggerId;
                    CheckHr( m_debugSystemObjects.GetThreadIdBySystemId( tid, out debuggerId ) );
                    var threadCtx = new DbgEngContext( curCtx.SystemIndex,
                                                       curCtx.ProcessIndexOrAddress,
                                                       debuggerId,
                                                       0 );
                    return new DbgUModeThreadInfo( this, threadCtx, tid );
                } );
        } // end GetThreadBySystemTid()


      //public DEBUG_EVENT_CONTEXT GetEventContext()
      //{
      //    DEBUG_EVENT_CONTEXT dec = new DEBUG_EVENT_CONTEXT();
      //    dec.Size = (uint) Marshal.SizeOf( typeof( DEBUG_EVENT_CONTEXT ) );

      //    CheckHr( m_debugSystemObjects.GetCurrentProcessId( out dec.ProcessEngineId ) );
      //    CheckHr( m_debugSystemObjects.GetCurrentThreadId( out dec.ThreadEngineId ) );
      //    // TODO: when is dec.FrameEngineId useful?

      //    return dec;
      //} // end GetEventContext()


        internal static DEBUG_STATUS ResumeTypeToDEBUG_STATUS( ResumeType rt )
        {
            switch( rt )
            {
                case ResumeType.Go:
                    return DEBUG_STATUS.GO;
                case ResumeType.GoHandled:
                    return DEBUG_STATUS.GO_HANDLED;
                case ResumeType.GoNotHandled:
                    return DEBUG_STATUS.GO_NOT_HANDLED;
                case ResumeType.StepBranch:
                    return DEBUG_STATUS.STEP_BRANCH;
                case ResumeType.StepInto:
                    return DEBUG_STATUS.STEP_INTO;
                case ResumeType.StepOver:
                    return DEBUG_STATUS.STEP_OVER;
                case ResumeType.ReverseGo:
                    return DEBUG_STATUS.REVERSE_GO;
                case ResumeType.ReverseStepBranch:
                    return DEBUG_STATUS.REVERSE_STEP_BRANCH;
                case ResumeType.ReverseStepInto:
                    return DEBUG_STATUS.REVERSE_STEP_INTO;
                case ResumeType.ReverseStepOver:
                    return DEBUG_STATUS.REVERSE_STEP_OVER;
                default:
                    string msg = Util.Sprintf( "Unexpected ResumeType value: {0}", rt );
                    Util.Fail( msg );
                    throw new NotImplementedException( msg );
            }
        } // end ResumeTypeToDEBUG_STATUS()


        public void ResumeExecution( ResumeType rt )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    int hr = 0;
                    if( ResumeType.StepOut == rt )
                    {
                        DEBUG_STACK_FRAME_EX[] frames;
                        hr = m_debugControl.GetStackTraceEx( 0, 0, 0, 1, out frames );

                        CheckHr( hr );
                        Util.Assert( 1 == frames.Length );

                        var f = frames[ 0 ];
                        if( IsFrameInline( f.InlineFrameContext ) )
                        {
                            // Inline.
                            // TODO: Ask for a real way to do this.
                            InvokeDbgEngCommand( "gu", suppressOutput: true );
                        }
                        else
                        {
                            // Not inline.
                            // We'll use a special breakpoint id, which can be used to
                            // help keep it hidden.
                            var bp = NewBreakpoint( DEBUG_BREAKPOINT_TYPE.CODE, DbgBreakpointInfo.StepOutBreakpointId );
                            bp.Offset = f.ReturnOffset;
                            bp.IsEnabled = true;
                            bp.IsOneShot = true; // Alternative: bp.Command = Util.Sprintf( "bc {0}", bp.Id );

                            uint tid;
                            CheckHr( m_debugSystemObjects.GetCurrentThreadId( out tid ) );
                            bp.MatchThread = tid;

                            hr = m_debugControl.SetExecutionStatus( DEBUG_STATUS.GO );
                        }
                    }
                    else if( ResumeType.StepToCall == rt )
                    {
                        // I guess we'll have to do with this for now.
                        InvokeDbgEngCommand( "pc", suppressOutput: true );
                    }
                    else if( ResumeType.GoConditional == rt )
                    {

                        // Unfortunately this operation can't be performed by using a
                        // proper API--it depends on knowing internal dbgeng state. So
                        // we'll just have to ask dbgeng to do it for us.
                        InvokeDbgEngCommand( "gc", suppressOutput: true );
                    }
                    else
                    {
                        hr = m_debugControl.SetExecutionStatus( ResumeTypeToDEBUG_STATUS( rt ) );
                    }

                    if( E_NOINTERFACE == hr )
                    {
                        throw new DbgEngException( hr,
                                                   "No runnable debuggees.",
                                                   "NoRunnable",
                                                   System.Management.Automation.ErrorCategory.InvalidOperation );
                    }
                    else if( E_ACCESSDENIED == hr )
                    {
                        throw new DbgEngAlreadyRunningException( hr );
                    }
                    else
                    {
                        CheckHr( hr );
                    }
                } );
        } // end ResumeExecution()


        public void Break()
        {
            // TODO: Threading requirements when connected to a remote?
            try
            {
                CheckHr( m_debugControl.SetInterrupt( DEBUG_INTERRUPT.ACTIVE ) ); // TODO: allow passing different flags
            }
            catch( Exception e )
            {
                // TODO
                Console.WriteLine( "Uh-oh... Break: {0}", e );
            }
        } // end Break()


        // TODO: cache in namespace?
        public bool TargetIs32Bit
        {
            get
            {
                DbgEngContext ctx;
                try
                {
                    ctx = GetCurrentDbgEngContext();
                    ctx.TryAsTargetContext( out ctx );

                    if( null == ctx )
                        return false; // No current process.
                }
                catch( DbgProviderException dpe )
                {
                    // I've seen this happen when running Update-FormatData when not
                    // currently attached to anything, but after we have been attached
                    // to stuff.
                    LogManager.Trace( "TargetIs32Bit: Hm, couldn't get the current debugger context: {0}",
                                      Util.GetExceptionMessages( dpe ) );
                    return false;
                }

                DbgTarget target;
                if( m_targets.TryGetValue( ctx, out target ) )
                {
                    return target.Is32Bit;
                }

                // This happens when we are still attaching. There is nothing we can
                // do; we need this info to be part of event callbacks.
                return false;
            }
        } // end property TargetIs32Bit


        public uint PointerSize
        {
            get
            {
                if( TargetIs32Bit )
                    return 4;
                else
                    return 8;
            }
        }


        internal bool QueryTargetIs32Bit()
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    int hr = m_debugControl.IsPointer64Bit();
                    if( 0 == hr )
                        return false;
                    else if( S_FALSE == hr )
                        return true;
                    else
                        throw new DbgEngException( hr );
                } );
        } // end QueryTargetIs32Bit()

        internal bool QueryTargetIsWow64()
        {
            return ExecuteOnDbgEngThread( () =>
            {
                CheckHr(m_debugControl.GetActualProcessorType(out IMAGE_FILE_MACHINE type ));
                if( type == IMAGE_FILE_MACHINE.AMD64 || (uint)type == 0xAA64 /*ARM64*/ ) //Wow64 supported architectures
                {
                    CheckHr(m_debugSystemObjects.GetCurrentThreadTeb( out var teb ));
                    var wow64offset = teb + 0x1488; //TODO: TlsSlots[1] - 1 == WOW64_TLS_CPURESERVED
                    //Note that we want to do a 64-bit pointer read here regardless of the current effective pointer size
                    _CheckMemoryReadHr( wow64offset, m_debugDataSpaces.ReadVirtualValue( wow64offset, out ulong cpureservedOffset ) );
                    //now we have a pointer to a WOW64_CPURESERVED struct, which is a pair of ushort values
                    //If we wanted to know the WoW guest architecture, we'd want to read the second one (only actually necessary
                    //for windows builds >= 9925, which I presume was the first time it might have been a value other than i386).
                    //but that it populated at all tells us it is indeed Wow64.
                    return cpureservedOffset != 0;
                }
                return false;
            } );
        }

        private RealDebugEventCallbacks m_internalEventCallbacks;
        private IDebugInputCallbacksImp m_inputCallbacks;
        //private IDebugOutputCallbacksWide m_outputCallbacks;
        private DebugOutputCallbacks m_outputCallbacks;


        private void _SetEventCallbacks( RealDebugEventCallbacks callbacks )
        {
            CheckHr( m_debugClient.SetEventCallbacksWide( callbacks ) );
            m_internalEventCallbacks = callbacks;
        } // end _SetEventCallbacks()


        public void SetInputCallbacks( IDebugInputCallbacksImp callbacks )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugClient.SetInputCallbacks( callbacks ) );
                    m_inputCallbacks = callbacks;
                } );
        } // end SetInputCallbacks()

        public IDebugInputCallbacksImp GetInputCallbacks()
        {
            return m_inputCallbacks;
        } // end GetInputCallbacks()

        private void _SetOutputCallbacks( DebugOutputCallbacks callbacks )
        {
            CheckHr( m_debugClient.SetOutputCallbacksWide( callbacks ) );
            m_outputCallbacks = callbacks;
        } // end _SetOutputCallbacks()


        /// <summary>
        ///    Executes the WaitForEvent on the dedicated DbgEng thread, so that the cmdlet thread
        ///    can be used for event callbacks and such.
        /// </summary>
        public Task WaitForEventAsync()
        {
            return m_dbgEngThread.ExecuteAsync( () =>
                {
                    LogManager.Trace( "Calling WaitForEvent." );
                    bool noException = false;
                    try
                    {
                        int hr = m_debugControl.WaitForEvent( DEBUG_WAIT.DEFAULT,
                                                              unchecked( (uint) -1 ) );
                        if( E_UNEXPECTED == hr )
                        {
                            if( NoTarget )
                            {
                                // This can happen, for instance, when you use -SkipFinalBreakpoint.
                                // I don't know why dbgeng considers it an "unexpected" error.
                                hr = 0; // No problem.
                            }
                        }
                        else if( (ERROR_NOACCESS == hr) ||
                                 (HR_ERROR_PARTIAL_COPY == hr) ||
                                 (HR_BAD_BP_OFFSET == hr) )
                        {
                            LogManager.Trace( "Whoops... did I mess up a breakpoint? Error: {0}",
                                              Util.FormatErrorCode( hr ) );

                            throw new DbgEngException( hr,
                                                       Util.Sprintf( "WaitForEvent failed ({0}). Check breakpoint addresses, as one or more may be invalid.",
                                                                     Util.FormatErrorCode( hr ) ),
                                                       "WaitForEventFailed_AccessError",
                                                       System.Management.Automation.ErrorCategory.NotSpecified );
                        }
                        CheckHr( hr );

                        // The state of the world may have changed. It's difficult to
                        // update our own state in response to debug event callbacks,
                        // because anything that involves calling back into dbgeng is
                        // verboten (because it won't work when connected to a remote,
                        // and might not work for other reasons too, sometimes). So we
                        // need to refresh our own state now that control has come back
                        // to us.
                        // TODO: I don't think I need this call to cache the current context anymore.
                        var curCtx = GetCurrentDbgEngContext(); // This will cause the current context to be cached.
                        noException = true;
                    }
                    finally
                    {
                        if( noException )
                            LogManager.Trace( "WaitForEvent returned." );
                        else
                            LogManager.Trace( "WaitForEvent threw!" );
                    }
                } );
        }


        private const int c_DefaultMaxNotifications = 1024;

        private int m_maxNotificationsToKeep = c_DefaultMaxNotifications;

        public int MaxRecentNotifications
        {
            get { return m_maxNotificationsToKeep; }
            set
            {
                lock( m_syncRoot )
                {
                    if( value <= 0 )
                        value = c_DefaultMaxNotifications;

                    if( value != m_maxNotificationsToKeep )
                    {
                        m_maxNotificationsToKeep = value;
                        m_recentNotificationBuf.Resize( m_maxNotificationsToKeep );
                    }
                }
            }
        }

        private CircularBuffer< DbgEventArgs > m_recentNotificationBuf;
        public IReadOnlyList< DbgEventArgs > RecentNotifications { get { return m_recentNotificationBuf; } }

        internal void AddNotification( DbgEventArgs den )
        {
            lock( m_syncRoot )
            {
                m_recentNotificationBuf.Add( den );
            }
        } // end AddNotification()


        /// <summary>
        ///    DbgEng can be attached to multiple processes, and each one is assigned an
        ///    id by DbgEng. This gets that id, not the OS pid. This id is stable--it
        ///    might get re-used later, but it won't "slide" down when another process
        ///    exits.
        /// </summary>
        public uint GetCurrentProcessDbgEngId()
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    uint id;
                    CheckHr( m_debugSystemObjects.GetCurrentProcessId( out id ) );
                    return id;
                } );
        } // end GetCurrentProcessDbgEngId()


        /// <summary>
        ///    This gets the thing that people outside of the debugger think of as the
        ///    "pid" or "process id". Here, "system" means "host OS", not "DbgEng
        ///    system".
        /// </summary>
        public uint GetProcessSystemId()
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    uint pid;
                    CheckHr( m_debugSystemObjects.GetCurrentProcessSystemId( out pid ) );
                    return pid;
                } );
        } // end GetProcessSystemId()


        /// <summary>
        ///    This gets the thing that people outside of the debugger think of as the
        ///    "pid" or "process id". Here, "system" means "host OS", not "DbgEng
        ///    system".
        /// </summary>
        public uint GetProcessSystemId( uint dbgEngId )
        {
            uint[] dbgEngIds;
            uint[] sysIds;
            GetProcessIds( out dbgEngIds, out sysIds );

            int idx = Array.IndexOf( dbgEngIds, dbgEngId );
            if( idx >= 0 )
                return sysIds[ idx ];

            throw new DbgProviderException( Util.Sprintf( "No such dbgEng process id found: {0}", dbgEngId ),
                                            "NoSuchDbgEngProcId",
                                            System.Management.Automation.ErrorCategory.ObjectNotFound,
                                            dbgEngId );
        } // end GetProcessSystemId()


        // TODO: need to hook into the event callbacks so that when filters change, we dump / update.
        private List< DbgEngineEventFilter > m_specificEventFilters;
        private List< DbgExceptionEventFilter > m_exceptionEventFilters;

        private Dictionary< DEBUG_FILTER_EVENT, DbgEngineEventFilter > m_specificEventFilterIndex;
        private Dictionary< uint, DbgExceptionEventFilter > m_exceptionEventFilterIndex;

        private void _GetEventFilters()
        {
            ExecuteOnDbgEngThread( () =>
            {
                uint numSpecificEventFilters;
                uint numSpecificExceptionFilters;
                uint numArbitraryExceptionFilters;
                CheckHr( m_debugControl.GetNumberEventFilters( out numSpecificEventFilters, out numSpecificExceptionFilters, out numArbitraryExceptionFilters ) );

                uint numAllFilters = numSpecificEventFilters + numSpecificExceptionFilters + numArbitraryExceptionFilters;
                uint numExFilters = numSpecificExceptionFilters + numArbitraryExceptionFilters;

                string[] commands = new string[ numAllFilters ];
                string[] secondCommands = new string[ numExFilters ];
                string[] names = new string[ numAllFilters ];
                string[] arguments = new string[ numSpecificEventFilters ];

                for( uint i = 0; i < numAllFilters; i++ )
                {
                    string tmp;
                    CheckHr( m_debugControl.GetEventFilterCommandWide( i, out tmp ) );
                    commands[ i ] = tmp;

                    CheckHr( m_debugControl.GetEventFilterTextWide( i, out tmp ) );
                    names[ i ] = tmp;

                    if( i < numSpecificEventFilters )
                    {
                        int hr = m_debugControl.GetSpecificFilterArgumentWide( i, out tmp );
                        if( 0 == hr )
                        {
                            arguments[ i ] = tmp;
                        }
                        else if( E_INVALIDARG != hr ) // it returns this for events that don't take an argument.
                        {
                            CheckHr( hr );
                        }
                    }
                    else
                    {
                        // Note that we use the 'global' index, not the exception-filters-only relative index.
                        CheckHr( m_debugControl.GetExceptionFilterSecondCommandWide( i, out tmp ) );
                        secondCommands[ i - numSpecificEventFilters ] = tmp;
                    }
                } // end foreach( filter )


                DEBUG_SPECIFIC_FILTER_PARAMETERS[] specEventFilterParams;

                CheckHr( m_debugControl.GetSpecificFilterParameters( 0, numSpecificEventFilters, out specEventFilterParams ) );

                m_specificEventFilters = new List<DbgEngineEventFilter>( (int) numSpecificEventFilters );
                m_specificEventFilterIndex = new Dictionary<DEBUG_FILTER_EVENT, DbgEngineEventFilter>();

                for( int i = 0; i < specEventFilterParams.Length; i++ )
                {
                    DbgEngineEventFilter sef = new DbgEngineEventFilter( (DEBUG_FILTER_EVENT) i,
                                                                         names[ i ],
                                                                         specEventFilterParams[ i ].ExecutionOption,
                                                                         specEventFilterParams[ i ].ContinueOption,
                                                                         arguments[ i ],
                                                                         commands[ i ] );
                    m_specificEventFilters.Add( sef );
                    m_specificEventFilterIndex.Add( sef.Event, sef );
                }

                DEBUG_EXCEPTION_FILTER_PARAMETERS[] exFilterParams;

                // Note we are using the "global" index (2nd param).
                CheckHr( m_debugControl.GetExceptionFilterParameters( numExFilters, numSpecificEventFilters, out exFilterParams ) );


                m_exceptionEventFilters = new List<DbgExceptionEventFilter>( (int) numExFilters );
                m_exceptionEventFilterIndex = new Dictionary<uint, DbgExceptionEventFilter>();

                for( uint i = numSpecificEventFilters; i < numAllFilters; i++ )
                {
                    DEBUG_EXCEPTION_FILTER_PARAMETERS filterParams = exFilterParams[ i - numSpecificEventFilters ];
                    DbgExceptionEventFilter eef = new DbgExceptionEventFilter( filterParams.ExceptionCode,
                                                                               names[ i ],
                                                                               filterParams.ExecutionOption,
                                                                               filterParams.ContinueOption,
                                                                               commands[ i ],
                                                                               secondCommands[ i - numSpecificEventFilters ] );
                    m_exceptionEventFilters.Add( eef );
                    m_exceptionEventFilterIndex.Add( eef.ExceptionCode, eef );
                }
            } );
        } // end _GetEventFilters()


        public IList< DbgEngineEventFilter > GetSpecificEventFilters()
        {
            if( null == m_specificEventFilters )
                _GetEventFilters();

            return m_specificEventFilters.AsReadOnly();
        } // end GetSpecificEventFilters()

        public IList< DbgExceptionEventFilter > GetExceptionEventFilters()
        {
            if( null == m_exceptionEventFilters )
                _GetEventFilters();

            return m_exceptionEventFilters.AsReadOnly();
        } // end GetExceptionEventFilters()

        private void _ResetFilters()
        {
            m_specificEventFilterIndex = null;
            m_exceptionEventFilterIndex = null;
            m_specificEventFilters = null;
            m_exceptionEventFilters = null;
        } // end _ResetFilters()

        private void _RefreshSingleFilter( uint filterIndex )
        {
            if( null == m_specificEventFilters )
                return; // no need to refresh; we'll just get the current value if/when we get them all

            // TODO: Is it worth writing the code to just grab a single filter?
            _ResetFilters();
        } // end _RefreshSingleFilter()


        // I wish we could de-powershell-ize this a bit.
        // We could require people just pass in an IPipelineCallback... but many
        // of the types used there are not public. We could either cut down IPipelineCallback,
        // or make the types public.

        /// <summary>
        ///    Sets the cmdlet context that the debugger will use to interact with the pipeline.
        /// </summary>
        /// <remarks>
        ///    During cmdlet execution, the debugger will need to interact with the
        ///    pipeline (write objects, write errors, etc.). This method gives the debugger a
        ///    way to do that, via the passed-in cmdlet. Only people who implement cmdlets
        ///    that directly interact with the debugger might need to call this.
        /// </remarks>
        internal IDisposable SetCurrentCmdlet( DbgBaseCommand cmdlet )
        {
            var psPipe = new DbgBaseCommand.PipelineCallback( cmdlet,
                                                              0,
                                                              () => { throw new NotImplementedException(); } );

            var oldPipeSettings = _SetCurrentPipeline( new PipeCallbackSettings( psPipe ) );
            Util.Assert( null != oldPipeSettings );

            var pipeSwapper = new Disposable( () => _SetCurrentPipeline( oldPipeSettings ) );
            ExceptionGuard disposer = new ExceptionGuard( pipeSwapper );
            disposer.Protect( psPipe );

            return disposer;
        } // end SetCurrentCmdlet()


        private class PipeCallbackSettings
        {
            public readonly IPipelineCallback Pipeline;

            public PipeCallbackSettings( IPipelineCallback pipe )
            {
                Pipeline = pipe;
            }
        } // end class PipeCallbackSettings


        private PipeCallbackSettings _SetCurrentPipeline( PipeCallbackSettings newPipeSettings )
        {
            var oldPipeSettings = m_internalEventCallbacks.SetPipe( newPipeSettings );

            Util.Assert( newPipeSettings.Pipeline != oldPipeSettings.Pipeline );

            if( oldPipeSettings.Pipeline == m_holdingPipe )
            {
                m_holdingPipe.Drain( newPipeSettings.Pipeline );
            }

            return oldPipeSettings;
        } // end _SetCurrentPipeline()


        //
        // These events correspond to the events in IDebugEventCallbacksWide.
        // Unfortunately, they lack lots of key information (for instance, system and
        // process id context information).
        //
        public event EventHandler< BreakpointEventArgs >           BreakpointHit;
        public event EventHandler< ExceptionEventArgs >            ExceptionHit;
        public event EventHandler< ThreadCreatedEventArgs >        ThreadCreated;
        public event EventHandler< ThreadExitedEventArgs >         ThreadExited;
        public event EventHandler< ProcessCreatedEventArgs >       ProcessCreated;
        public event EventHandler< ProcessExitedEventArgs >        ProcessExited;
        public event EventHandler< ModuleLoadedEventArgs >         ModuleLoaded;
        public event EventHandler< ModuleUnloadedEventArgs >       ModuleUnloaded;
        public event EventHandler< SystemErrorEventArgs >          SystemError;
        public event EventHandler< SessionStatusEventArgs >        SessionStatusChanged;
        public event EventHandler< DebuggeeStateChangedEventArgs > DebuggeeStateChanged;
        public event EventHandler< EngineStateChangedEventArgs >   EngineStateChanged;
        public event EventHandler< SymbolStateChangedEventArgs >   SymbolStateChanged;

        //
        // DbgShell/DbgProvider/DbgEngDebugger-specific events:
        //
        // These events are synthesized by me, and are not related to the
        // IDebugEventCallbacksWide events. They are used to notify the PS namespace
        // provider to update the namespace, and for DbgTypeInfo's cache.
        //
        public event EventHandler< UmProcessRemovedEventArgs >   UmProcessRemoved;
        public event EventHandler< UmProcessAddedEventArgs >     UmProcessAdded;
        public event EventHandler< KmTargetRemovedEventArgs >    KmTargetRemoved;
        public event EventHandler< KmTargetAddedEventArgs >      KmTargetAdded;


        private void _RaiseEvent< TArgs >( EventHandler< TArgs > handlers, TArgs args )
            where TArgs : DbgInternalEventArgs
        {
            if( null == handlers )
                return;

            foreach( var handler in handlers.GetInvocationList() )
            {
                try
                {
                    handler.DynamicInvoke( this, args );
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception from internal event callback.", e );
                }
            }
        } // end _RaiseEvent()


        /// <summary>
        ///    Invokes the specified debugger command on the dbgeng thread.
        /// </summary>
        public Task InvokeDbgEngCommandAsync( string command,
                                              string outputPrefix,
                                              Action< string > consumeLine )
        {
            return InvokeDbgEngCommandAsync( command,
                                             outputPrefix,
                                             DEBUG_OUTCTL.THIS_CLIENT,
                                             consumeLine );
        }

        public Task InvokeDbgEngCommandAsync( string command,
                                              string outputPrefix,
                                              DEBUG_OUTCTL outputControl,
                                              Action< string > consumeLine )
        {
            if( null == outputPrefix )
                outputPrefix = "DbgEng> ";

            // TODO: we should probably set input callbacks too, in case something wants input?

         // using( var outputCallbacks = new DebugOutputCallbacks( outputPrefix, consumeLine ) )
         // {
         //     _SetOutputCallbacks( outputCallbacks );
                // TODO: should I use ambient_dml or ambient_text, or what?
                return ExecuteOnDbgEngThreadAsync( () =>
                    {
                        m_outputCallbacks.Prefix = outputPrefix;
                        m_outputCallbacks.Flush(); // get rid of any leftover cruft (we've been sending all output to the bitbucket)
                        m_outputCallbacks.ConsumeLine = consumeLine;
                        try
                        {
                            CheckHr( m_debugControl.ExecuteWide( outputControl,
                                                                 command,
                                                                 DEBUG_EXECUTE.NO_REPEAT ) );
                        }
                        finally
                        {
                            // TODO: a disposable thing to do this
                            //_SetOutputCallbacks( null );

                            m_outputCallbacks.Flush();
                            m_outputCallbacks.ConsumeLine = (x) => {};
                        }
                    } );
         // } // end using( outputCallbacks )
        } // end InvokeDbgEngCommandAsync()


        /// <summary>
        ///    Invokes the specified debugger command on the dbgeng thread.
        /// </summary>
        public Task InvokeDbgEngCommandAsync( string command )
        {
            return ExecuteOnDbgEngThreadAsync( () => InvokeDbgEngCommand( command ) );
        } // end InvokeDbgEngCommandAsync()


        /// <summary>
        ///    Invokes the specified debugger command on the dbgeng thread.
        /// </summary>
        public Task InvokeDbgEngCommandAsync( string command, bool suppressOutput )
        {
            return ExecuteOnDbgEngThreadAsync( () => InvokeDbgEngCommand( command, suppressOutput ) );
        } // end InvokeDbgEngCommandAsync()


        /// <summary>
        ///    Invokes the specified debugger command on the current thread.
        /// </summary>
        public void InvokeDbgEngCommand( string command )
        {
            InvokeDbgEngCommand( command, false );
        }

        public void InvokeDbgEngCommand( string command, bool suppressOutput )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugControl.ExecuteWide( suppressOutput ? DEBUG_OUTCTL.IGNORE : DEBUG_OUTCTL.ALL_CLIENTS, // TODO: or should it be NOT_LOGGED?
                                                         command,
                                                         DEBUG_EXECUTE.NO_REPEAT ) );
                } );
        } // end InvokeDbgEngCommand()


        internal void InvokeDbgEngCommand( string command, DEBUG_OUTCTL outctl )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugControl.ExecuteWide( outctl,
                                                         command,
                                                         DEBUG_EXECUTE.NO_REPEAT /*| DEBUG_EXECUTE.NOT_LOGGED*/ ) );
                } );
        } // end InvokeDbgEngCommand()


        public IDisposable HandleDbgEngOutput( Action<string> consumeLine )
        {
            var currentCl = m_outputCallbacks.ConsumeLine;
            var currentPrefix = m_outputCallbacks.Prefix;

            var disposer = new Disposable( () =>
                {
                    m_outputCallbacks.Flush();
                    m_outputCallbacks.Prefix = currentPrefix;
                    m_outputCallbacks.ConsumeLine = currentCl;
                } );
            m_outputCallbacks.Flush();
            m_outputCallbacks.Prefix = String.Empty;
            m_outputCallbacks.ConsumeLine = consumeLine;
            return disposer;
        } // end HandleDbgEngOutput()

        // TODO: Maybe I should just make CircularBuffer public.
        public IReadOnlyList< string > RecentDbgEngOutput
        {
            get { return m_outputCallbacks.RecentDbgEngOutput; }
        }


        // Breakpoint cache, indexed by breakpoint id.
        private Dictionary< uint, DbgBreakpointInfo > m_breakpoints;
        private IReadOnlyDictionary< uint, DbgBreakpointInfo > m_roBreakpoints;

        // For DbgShell-created breakpoints that want to run ScriptBlocks when hit, we
        // stow the ScriptBlocks here, indexed by breakpoint guid. When we get a
        // breakpoint change notification, we go through the current breakpoints and apply
        // a regex to the breakpoint command to see if it is a DbgShell command, so that
        // we can cull ScriptBlocks that are no longer used.
        private Dictionary< Guid, System.Management.Automation.ScriptBlock > m_bpDbgShellCommands
            = new Dictionary< Guid, System.Management.Automation.ScriptBlock >();

        // I don't bother to validate that the guid is composed of hex digits.
        private const string __guid = "(?<guid>........-....-....-....-............)";
        // N.B. This pattern must match the command set in DbgBreakpointInfo.ScriptBlockCommand.
        private static Regex sm_specialBpCmdPat = new Regex(
                @"^!DbgShellExt.dbgshell -Bp (-NoExit )?& \(\(Get-DbgBreakpoint -Guid '" + __guid + @"'\).ScriptBlockCommand\)\z",
                RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );


        /// <summary>
        ///    Indicates if a breakpoint command is one set by DbgShell to run a
        ///    ScriptBlock when it is hit.
        /// </summary>
        private static bool _IsSpecialDbgShellBpCommand( DbgBreakpointInfo bp )
        {
            var matches = sm_specialBpCmdPat.Matches( bp.DbgEngCommand );
            if( 1 == matches.Count )
            {
                var match = matches[ 0 ];
                Guid bpIdInCommand;
                bool parseResult = Guid.TryParse( match.Groups[ "guid" ].Value, out bpIdInCommand );
                // Not a true program invariant, but if it fails, somebody's been messing
                // with our breakpoints.
                Util.Assert( parseResult );
                Util.Assert( bpIdInCommand == bp.Guid );
                return parseResult;
            }
            return false;
        } // end _IsSpecialDbgShellBpCommand()


        internal void SetBpCommandScriptBlock( Guid bpGuid, System.Management.Automation.ScriptBlock scriptBlock )
        {
            if( null == scriptBlock )
                m_bpDbgShellCommands.Remove( bpGuid );
            else
                m_bpDbgShellCommands[ bpGuid ] = scriptBlock;
        }


        internal System.Management.Automation.ScriptBlock TryGetBpCommandScriptBlock( Guid bpGuid )
        {
            System.Management.Automation.ScriptBlock sb;
            if( m_bpDbgShellCommands.TryGetValue( bpGuid, out sb ) )
            {
                return sb;
            }
            return null;
        }


        public IReadOnlyDictionary< uint, DbgBreakpointInfo >  GetBreakpoints()
        {
            if( null == m_roBreakpoints )
            {
                m_breakpoints = ExecuteOnDbgEngThread( () =>
                    {
                        var breakpoints = new Dictionary< uint, DbgBreakpointInfo >();

                        uint numBps;
                        CheckHr( m_debugControl.GetNumberBreakpoints( out numBps ) );
                        if( 0 == numBps )
                        {
                            return breakpoints;
                        }

                        DEBUG_BREAKPOINT_PARAMETERS[] bpParams;
                        // INT9cbae984: GetNumberBreakpoints might include "hidden"
                        // breakpoints in its count, which will cause
                        // GetBreakpointParameters to return S_FALSE.
                        int hr = m_debugControl.GetBreakpointParameters( numBps, 0, out bpParams );
                        if( hr == S_FALSE )
                        {
                            LogManager.Trace( "Hmm... GetBreakpointParameters could not get params for all breakpoints." );
                            hr = 0;
                        }

                        CheckHr( hr );

                        for( int i = 0; i < bpParams.Length; i++ )
                        {
                            if( bpParams[ i ].Id != DEBUG_ANY_ID ) // <-- another effect of INT9cbae984
                            {
                                breakpoints[ bpParams[ i ].Id ] = new DbgBreakpointInfo( this, bpParams[ i ] );
                            }
                        }

                        return breakpoints;
                    } );
            }
            Util.Assert( null != m_breakpoints );
            m_roBreakpoints = new ReadOnlyDictionary< uint, DbgBreakpointInfo >( m_breakpoints );

            //
            // Cull unused ScriptBlock commands.
            //
#if DEBUG
            foreach( var bp in m_breakpoints.Values )
            {
                if( _IsSpecialDbgShellBpCommand( bp ) )
                {
                    // Not truly a program invariant, but if it fails, someone's been
                    // messing with our breakpoints.
                    Util.Assert( m_bpDbgShellCommands.ContainsKey( bp.Guid ) );
                }
            } // end foreach( current bp )
#endif

            // This isn't strictly as accurate as using _IsSpecialDbgShellBpCommand
            // (because event though breakpoint G still exists, its Command might have
            // been removed, so we'd be unnecessarily holding onto the ScriptBlock...
            // but I think that's fine.
            Guid[] stillInUse = m_breakpoints.Values.Select( (x) => x.Guid ).ToArray();
            foreach( var g in m_bpDbgShellCommands.Keys.ToArray() )
            {
                if( !stillInUse.Contains( g ) )
                {
                    m_bpDbgShellCommands.Remove( g );
                }
            }

            return m_roBreakpoints;
        } // end GetBreakpoints()


        // We should already be on the dbgeng thread.
        internal void FixupBpStuff( ulong bpId )
        {
            // We used to do a bunch of work to fix up breakpoints that we already knew
            // about, but that required calling back into dbgeng, which is not legal from
            // within a dbgeng event callback. So now we just wipe our cache every time.

            m_breakpoints = null;
            m_roBreakpoints = null;
        } // end FixupBpStuff()


        public DbgBreakpointInfo GetBreakpointById( uint id )
        {
            var bp = TryGetBreakpointById( id );
            if( null == bp )
            {
                throw new DbgProviderException( Util.Sprintf( "Breakpoint id {0} not found.",
                                                              id ),
                                                "NoSuchBreakpoint",
                                                System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                null,
                                                id );
            }
            return bp;
        } // end GetBreakpointById()


        public DbgBreakpointInfo TryGetBreakpointById( uint id )
        {
            DbgBreakpointInfo bp;
            if( !GetBreakpoints().TryGetValue( id, out bp ) )
            {
                return null;
            }
            return bp;
        } // end TryGetBreakpointById()


        private uint _GetNextUnusedBreakpointId()
        {
            var bps = GetBreakpoints();
            uint i = 0;
            DbgBreakpointInfo dontCare;
            for( i = 0; i < bps.Count; i++ )
            {
                if( !bps.TryGetValue( i, out dontCare ) )
                    return i;
            }
            return i;
        } // end _GetNextUnusedBreakpointId()


        public DbgBreakpointInfo NewBreakpoint( DEBUG_BREAKPOINT_TYPE type )
        {
            return NewBreakpoint( type, _GetNextUnusedBreakpointId() );
        }

        public DbgBreakpointInfo NewBreakpoint( DEBUG_BREAKPOINT_TYPE type, uint id )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    if( DEBUG_ANY_ID == id )
                    {
                        id = _GetNextUnusedBreakpointId();
                    }

                    WDebugBreakpoint bp;
                    // TODO: dbgeng will return 0x80070057 if the bp id is already in use.
                    CheckHr( m_debugControl.AddBreakpoint2( type, id, out bp ) );
                    return new DbgBreakpointInfo( this, bp );
                } );
        } // end _NewBreakpoint()


        public void RemoveBreakpoint( ref DbgBreakpointInfo breakpoint )
        {
            if( null == breakpoint )
                throw new ArgumentNullException( "breakpoint" );

            // Calling IDebugControl.RemoveBreakpoint2 will release the interface, so we
            // want to make sure nobody touches it again.
            var tmp = breakpoint;
            breakpoint = null;

            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugControl.RemoveBreakpoint2( tmp.Bp ) );
                } );
        } // end RemoveBreakpointById()


        internal DbgBreakpointInfo TryFindMatchingBreakpoint( string expression, ulong addr )
        {
            foreach( var bp in GetBreakpoints().Values )
            {
                if( (0 != addr) && (bp.Offset == addr) )
                {
                    return bp;
                }
                else if( !String.IsNullOrEmpty( expression ) &&
                         (0 == Util.Strcmp_OI( expression, bp.OffsetExpression )) )
                {
                    return bp;
                }
            }
            return null;
        } // end TryFindMatchingBreakpoint()


        public DbgStackFrameInfo GetCurrentScopeFrame()
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    ulong instructionPointer;
                    DEBUG_STACK_FRAME_EX nativeFrame;
                    CheckHr( m_debugSymbols.GetScopeEx( out instructionPointer,
                                                        out nativeFrame,
                                                        IntPtr.Zero, // scopeContext
                                                        0 ) );       // scopeContextSize
                    return new DbgStackFrameInfo( this,
                                                  GetCurrentThread(),
                                                  nativeFrame,
                                                  null,    // managedFrame
                                                  false ); // alreadySearchedManagedFrames
                } );
        } // end GetCurrentScopeFrame()


        // Returns 'true' if it was able to set the frame; false if the frameNum was out
        // of bounds.
        internal bool SetCurrentScopeFrame( uint frameNum )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    var flags = DEBUG_FRAME.DEFAULT;

                    int hr = m_debugSymbols.SetScopeFrameByIndexEx( flags, frameNum );
                    if( S_OK == hr )
                    {
                        return true;
                    }
                    else
                    {
                        if( E_INVALIDARG != hr )
                            CheckHr( hr );

                        return false;
                    }
                } );
        } // end SetCurrentScopeFrame()


        internal void SetContextByPath( string path )
        {
            if( !TrySetContextByPath( path ) )
            {
                uint[] sysIds = GetDbgEngSystemIds();
                throw new DbgProviderException( Util.Sprintf( "Ambiguous target: there are {0} systems to choose from, and none specified by the path '{1}'. Use Set-Location ('cd') to select a debugging target.",
                                                              sysIds.Length,
                                                              @"Dbg:\" + path ),
                                                "AmbiguousDebuggerContextForPath",
                                                System.Management.Automation.ErrorCategory.InvalidOperation,
                                                path );
            }
        } // end SetContextByPath()

        internal bool TrySetContextByPath( string path )
        {
            if( null == path )
                throw new ArgumentNullException( "path" );

            LogManager.Trace( "Attempting to set debugger context by path: {0}", path );
            bool setSomeContext = false;
            List< DebuggerObject > dbgObjects = DbgProvider.GetDebuggerObjectsForPath( path );
         // for( int i = 0; i < dbgObjects.Count; i++ )
         // {
         //     ICanSetContext ics = dbgObjects[ i ] as ICanSetContext;
         //     if( null == ics )
         //         continue;

         //     ics.SetContext();
         //     setSomeContext = true;
         // }

            for( int i = dbgObjects.Count - 1; i >= 0; i-- )
            {
                ICanSetContext ics = dbgObjects[ i ] as ICanSetContext;
                if( null == ics )
                    continue;

                ics.SetContext();
                setSomeContext = true;
                break;
            }

            if( !setSomeContext )
            {
                // Maybe there is only one target, in which case we can just assum
                // that's the one they mean (and in fact, we don't even have to switch to
                // it since it's not like current context could be something else)
                // TODO: test that after going from two targets to one.

                uint[] sysIds = GetDbgEngSystemIds();
                if( 1 == sysIds.Length )
                {
                    uint[] dbgEngIds;
                    uint[] procSysIds;
                    GetProcessIds( out dbgEngIds, out procSysIds );
                    if( 1 == dbgEngIds.Length )
                    {
                        return true;
                    }
                }
                LogManager.Trace( "Unable to set context." );
            }
            return setSomeContext;
        } // end TrySetContextByPath()


        public void GetModuleInfo( out List< DbgModuleInfo > modules,
                                   out List< DbgModuleInfo > unloadedModules )
        {
            List< DbgModuleInfo > tmpModules = null;
            List< DbgModuleInfo > tmpUnloadedModules = null;
            ExecuteOnDbgEngThread( () =>
                {
                    var target = GetCurrentTarget();
                    uint numLoadedMods, numUnloadedMods;
                    CheckHr( m_debugSymbols.GetNumberModules( out numLoadedMods, out numUnloadedMods ) );
                    uint totalMods = numLoadedMods + numUnloadedMods;
                    DEBUG_MODULE_PARAMETERS[] modParams;
                    CheckHr( m_debugSymbols.GetModuleParameters( totalMods, null, 0, out modParams ) );

                    tmpModules = new List<DbgModuleInfo>( (int) numLoadedMods );
                    tmpUnloadedModules = new List<DbgModuleInfo>( (int) numUnloadedMods );
                    for( uint i = 0; i < totalMods; i++ )
                    {
                        var mod = new DbgModuleInfo( this, modParams[ i ], target );
                        if( mod.IsUnloaded )
                            tmpUnloadedModules.Add( mod );
                        else
                            tmpModules.Add( mod );
                    }
                } );
            modules = tmpModules;
            unloadedModules = tmpUnloadedModules;
        } // end GetModuleInfo()


     // private DbgEngContext _GetProcessContextOrThrow()
     // {
     //     DbgEngContext ctx;
     //     if( !GetCurrentDbgEngContext().TryAsProcessContext( out ctx ) )
     //     {
     //         throw new DbgProviderException( "No current process.",
     //                                         "NoCurrentProcess",
     //                                         System.Management.Automation.ErrorCategory.InvalidOperation );
     //     }
     //     return ctx;
     // } // end _GetProcessContextOrThrow()


        private DbgEngContext _GetTargetContextOrThrow()
        {
            DbgEngContext ctx;
            if( !GetCurrentDbgEngContext().TryAsTargetContext( out ctx ) )
            {
                throw new DbgProviderException( "No current target.",
                                                "NoCurrentTarget",
                                                System.Management.Automation.ErrorCategory.InvalidOperation );
            }
            return ctx;
        } // end _GetTargetContextOrThrow()


        public IList< DbgModuleInfo > Modules
        {
            get
            {
                var ctx = _GetTargetContextOrThrow();
                DbgTarget target;
                if( !m_targets.TryGetValue( ctx, out target ) )
                {
                    // I think this might happen when we're still attaching.
                    return null;
                }
                return target.Modules;
            }
        } // end property Modules

        public IList< DbgModuleInfo > UnloadedModules
        {
            get
            {
                var ctx = _GetTargetContextOrThrow();
                DbgTarget target;
                if( !m_targets.TryGetValue( ctx, out target ) )
                {
                    // I think this might happen when we're still attaching.
                    return null;
                }
                return target.UnloadedModules;
            }
        } // end property UnloadedModules

        private void _AddModule( DbgModuleInfo modInfo )
        {
            var nsTarget = DbgProvider.GetNsTargetByCurrentDebuggerContext( this );
            if( null != nsTarget ) // it may not have been created yet (we're still attaching)
                ((DbgTarget) nsTarget.RepresentedObject).AddModule( modInfo );
        } // end _AddModule()


        public DbgModuleInfo GetModuleByAddress( ulong addrInModule )
        {
            // I could cache here?
            var process = GetCurrentTarget();
            return new DbgModuleInfo( this, addrInModule, process );
        } // end GetModuleByAddress()


        public IEnumerable< DbgModuleInfo > GetModuleByName( string moduleName )
        {
            return GetModuleByName( moduleName, DEBUG_GETMOD.NO_UNLOADED_MODULES );
        }

        public IEnumerable< DbgModuleInfo > GetModuleByName( string moduleName,
                                                             CancellationToken cancelToken )
        {
            return GetModuleByName( moduleName, DEBUG_GETMOD.NO_UNLOADED_MODULES, cancelToken );
        }

        // There can be more than one module with the same name.
        public IEnumerable< DbgModuleInfo > GetModuleByName( string moduleName,
                                                             DEBUG_GETMOD which )
        {
            return GetModuleByName( moduleName, which, CancellationToken.None );
        }


        public IEnumerable< DbgModuleInfo > GetModuleByName( string moduleName,
                                                             DEBUG_GETMOD which,
                                                             CancellationToken cancelToken )
        {
            if( String.IsNullOrEmpty( moduleName ) )
                throw new ArgumentException( "You must supply a module name.", "moduleName" );

            return StreamFromDbgEngThread<DbgModuleInfo>( cancelToken, ( ct, emit ) =>
            {
                // I could cache here?
                var process = GetCurrentTarget();

                uint index = 0;
                ulong modBase;
                do
                {
                    // To get all the modules, we keep calling with increasing index.
                    int hr = m_debugSymbols.GetModuleByModuleName2Wide( moduleName,
                                                                        index,
                                                                        which,
                                                                        out index,
                                                                        out modBase );
                    if( 0 == hr )
                        emit( GetModuleByAddress( modBase ) );
                    else if( E_INVALIDARG == hr )
                        break; // no more
                    else
                        CheckHr( hr ); // some other error?

                    cancelToken.ThrowIfCancellationRequested();
                    index++;
                } while( true );
            } );
        } // end GetModuleByName()

        //Retrieves the ntdll module associated with what DbgEng considers to be the native architecture
        //regardless of the current effective machine type
        public DbgModuleInfo GetNtdllModuleNative()
        {
            return ExecuteOnDbgEngThread( () =>
            {
                CheckHr( m_debugSymbols.GetSymbolModuleWide( "${$ntnsym}!", out var modBase ) );
                return GetModuleByAddress( modBase );
            } );
        } // end GetNativeNtDllModule()

        //Retrieves the 32 bit ntdll module, whether it is a pure 32 bit process or WoW64
        //Throws on pure 64 bit processes
        public DbgModuleInfo GetNtdllModule32()
        {
            return ExecuteOnDbgEngThread( () =>
            {
                CheckHr( m_debugSymbols.GetSymbolModuleWide( "${$ntwsym}!", out var modBase ) );
                return GetModuleByAddress( modBase );
            } );
        } // end Get32bitNtDllModule()

        public ulong GetCurrentThreadTebAddressNative()
        {
            return Debugger.ExecuteOnDbgEngThread( () =>
            {
                CheckHr( m_debugSystemObjects.GetCurrentThreadTeb( out var teb ) );
                return teb;
            } );
        } // end GetCurrentThreadTebAddressNative()

        public DbgSymbol GetCurrentThreadTebNative( CancellationToken token = default )
        {
            return _CreateNtdllSymbolForAddress( force32bit: false,
                                                 GetCurrentThreadTebAddressNative(),
                                                 "_TEB",
                                                 $"Thread_0x{m_cachedContext.ThreadIndexOrAddress}_TEB",
                                                 token );
        }  // end GetCurrentThreadTebNative()

        public ulong GetCurrentThreadTebAddress32()
        {
            return Debugger.ExecuteOnDbgEngThread( () =>
            {
                var nativeTebAddress = GetCurrentThreadTebAddressNative();
                if(QueryTargetIsWow64())
                {
                    var tebType = Debugger.GetModuleTypeByName( GetNtdllModuleNative(), "_TEB" );
                    var wowtebOffset = 8192u; //It's been that for 15 years now, so seems like a safe enough default
                    if(tebType is DbgUdtTypeInfo udtType && udtType.Members.HasItemNamed( "WowTebOffset" ))
                    {
                        var wowtebOffsetOffset = udtType.FindMemberOffset( "WowTebOffset" );
                        wowtebOffset = ReadMemAs< uint >( nativeTebAddress + wowtebOffsetOffset );
                    }
                    return nativeTebAddress + wowtebOffset;
                }
                else
                {
                    if( !TargetIs32Bit )
                    {
                        throw new DbgProviderException( "No 32 bit guest TEB to retrieve",
                                                        "Not32bit",
                                                        System.Management.Automation.ErrorCategory.InvalidOperation,
                                                        this );
                    }
                    return nativeTebAddress;
                }
            } );
        } // end GetCurrentThreadTebAddress32()

        public DbgSymbol GetCurrentThreadTeb32( CancellationToken token = default )
        {
            return _CreateNtdllSymbolForAddress( force32bit: true, 
                                                 GetCurrentThreadTebAddress32(), 
                                                 "_TEB", 
                                                 $"Thread_0x{m_cachedContext.ThreadIndexOrAddress}_TEB32", 
                                                 token );
        }  // end GetCurrentThreadTeb32()

        internal DbgSymbol _CreateNtdllSymbolForAddress( bool force32bit, ulong address, string type, string symbolName, CancellationToken token = default )
        {
            return Debugger.ExecuteOnDbgEngThread( () =>
            {
                try
                {
                    var module = force32bit ? GetNtdllModule32() : GetNtdllModuleNative();
                    var tebType = GetModuleTypeByName( module, type, token );
                    var tebSym = CreateSymbolForAddressAndType( address, tebType, symbolName );
                    return tebSym;
                }
                catch( DbgProviderException dpe )
                {
                    throw new DbgProviderException( $"Could not create symbol for {type}. Are you missing the PDB for ntdll?",
                                                    "NoTebSymbol",
                                                    System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                    dpe,
                                                    this );
                }
            } );
        } // end _CreateNtdllSymbolForAddress

        public byte[] ReadMem( ulong address, uint lengthDesired )
        {
            return ReadMem( address, lengthDesired, true );
        }

        public byte[] ReadMem( ulong address, uint lengthDesired, bool failIfReadSmaller )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    byte[] raw;
                    _CheckMemoryReadHr( address, m_debugDataSpaces.ReadVirtual( address, lengthDesired, out raw ) );
                    if( raw.Length != lengthDesired )
                    {
                        string addrString = DbgProvider.FormatAddress( address, TargetIs32Bit, true );
                        if( failIfReadSmaller )
                        {
                            throw new DbgMemoryAccessException( address,
                                                                Util.Sprintf( "Error reading virtual memory at {0}: could not read full 0x{1:x} bytes (read 0x{2:x}).",
                                                                              addrString,
                                                                              lengthDesired,
                                                                              raw.Length ),
                                                                "UnderRead",
                                                                System.Management.Automation.ErrorCategory.ReadError );
                        }
                        LogManager.Trace( "Warning: under-read: Wanted {0} bytes from address {1}, but only got {2}.",
                                          lengthDesired,
                                          addrString,
                                          raw.Length );
                    }
                    return raw;
                } );
        } // end ReadMem()

        public T ReadMemAs< T >( ulong address ) where T : unmanaged
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    _CheckMemoryReadHr( address, m_debugDataSpaces.ReadVirtualValue( address, out T tempValue ) );
                    return tempValue;
                } );
        }

        /// <summary>
        ///    Reads the specified number of bytes from the specified address in the
        ///    target's memory space, filling the specified pre-allocated buffer. This
        ///    method requires you to read a valid number of bytes, rather than letting
        ///    you specify a "failIfReadSmaller" bool and filling the remainder of the
        ///    buffer with zeroes or a pattern or something.
        /// </summary>
        public unsafe void ReadMem< T >( ulong address, Memory< T > fillMe ) where T : unmanaged
        {
            ExecuteOnDbgEngThread( () =>
                {
                    uint lengthDesired = (uint) (fillMe.Length * sizeof( T ));

                    using( var memHandle = fillMe.Pin() )
                    {
                        _CheckMemoryReadHr( address,
                                            m_debugDataSpaces.ReadVirtualDirect( address,
                                                                                 lengthDesired,
                                                                                 (byte*) memHandle.Pointer,
                                                                                 out uint bytesRead ) );


                        if( lengthDesired != bytesRead )
                        {
                            string addrString = DbgProvider.FormatAddress( address, TargetIs32Bit, true );
                            LogManager.Trace( "Error: under-read: Wanted {0} bytes from address {1}, but only got {2}.",
                                              lengthDesired,
                                              addrString,
                                              bytesRead );

                            throw new DbgMemoryAccessException( address,
                                                                Util.Sprintf( "Error reading virtual memory at {0}: could not read full 0x{1:x} bytes (read 0x{2:x}).",
                                                                              addrString,
                                                                              lengthDesired,
                                                                              bytesRead ),
                                                                "UnderRead",
                                                                System.Management.Automation.ErrorCategory.ReadError );
                        }
                    }
                } );
        } // end ReadMem< T >()


        private void _HandleWriteMemError( int hr,
                                           ulong address,
                                           uint cbRequested,
                                           uint cbWritten )
        {
            if( hr != 0 )
            {
                throw new DbgMemoryAccessException( address,
                                                    Util.Sprintf( "Error writing virtual memory at {0}: failed with error {1}.",
                                                                  DbgProvider.FormatAddress( address, Debugger.TargetIs32Bit, true ),
                                                                  Util.FormatErrorCode( hr ) ),
                                                    "MemoryWriteFailure",
                                                    System.Management.Automation.ErrorCategory.WriteError );
            }
            if( cbWritten != cbRequested )
            {
                throw new DbgMemoryAccessException( address,
                                                    Util.Sprintf( "Error writing virtual memory at {0}: only {1} of {2} bytes were written.",
                                                                  DbgProvider.FormatAddress( address, Debugger.TargetIs32Bit, true ),
                                                                  cbWritten,
                                                                  cbRequested ),
                                                    "MemoryWriteFailure",
                                                    System.Management.Automation.ErrorCategory.WriteError );
            }
        } // end _HandleWriteMemError()

        public unsafe void WriteMem( ulong address, byte* changes, uint cbChanges )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    uint written = 0;
                    int hr = m_debugDataSpaces.WriteVirtual( address, changes, cbChanges, out written );
                    _HandleWriteMemError( hr, address, cbChanges, written );
                } );
        } // end WriteMem()

        public unsafe void WriteMem< T >( ulong address, ReadOnlyMemory< T > changes ) where T : unmanaged
        {
            ExecuteOnDbgEngThread( () =>
                {
                    using( var memHandle = changes.Pin() )
                    {
                        WriteMem( address,
                                  (byte*) memHandle.Pointer,
                                  (uint) (changes.Length * sizeof( T )) );
                    }
                } );
        } // end WriteMem()

        public unsafe void WriteMem< T >( ulong address, T[] changes ) where T : unmanaged
        {
            WriteMem( address, new ReadOnlyMemory< T >( changes ) );
        } // end WriteMem()


        public bool TryReadMem( ulong address, uint lengthDesired, bool failIfReadSmaller, out byte[] mem )
        {
            byte[] tmpMem = null;
            var retval = ExecuteOnDbgEngThread( () =>
                {
                    byte[] raw;
                    int hr = m_debugDataSpaces.ReadVirtual( address, lengthDesired, out raw );
                    if( 0 != hr )
                    {
                        // I wonder if this will be too noisy?
                        LogManager.Trace( "ReadVirtual( {0} ) failed with error {1}.",
                                          DbgProvider.FormatAddress( address, Debugger.TargetIs32Bit, true ),
                                          Util.FormatErrorCode( hr ) );
                        return false;
                    }

                    if( raw.Length != lengthDesired )
                    {
                        if( failIfReadSmaller )
                            return false;

                        // TODO: log warning?
                    }
                    tmpMem = raw;
                    return true;
                } );
            mem = tmpMem;
            return retval;
        } // end TryReadMem()


        private void _CheckMemoryReadHr( ulong address, int hr )
        {
            // Seems odd that we could get E_NOINTERFACE from ReadVirtual... but it can
            // happen. (see UserMiniFullDumpTargetInfo::VirtualToOffset). I wonder if
            // that should be a bug?
            if( (HR_ERROR_READ_FAULT == hr) ||
                (HR_STATUS_NO_PAGEFILE == hr) ||
                (E_NOINTERFACE == hr) )
            {
                throw new DbgMemoryAccessException( address, TargetIs32Bit );
            }
            CheckHr( hr );
        } // end _CheckMemoryReadHr()

        public ulong[] ReadMemPointers( ulong address, uint numDesired )
        {
            // TODO: or should the default be to throw if we didn't read the desired number?
            return ReadMemPointers( address, numDesired, false );
        }

        public ulong[] ReadMemPointers( ulong address, uint numDesired, bool failIfReadFewer )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    ulong[] ptrs;
                    // TODO: On 32-bit, it seems ReadPointersVirtual will sign-extend. That is, if the
                    // pointer value is actually 0xffffffff, then it will return a ulong with a value
                    // of 0xffffffffffffffff. What should I do about that? For now, for display, I can
                    // chop off the top half in FormatAddress, but I wonder if that's enough.
                    _CheckMemoryReadHr( address,
                                        m_debugDataSpaces.ReadPointersVirtual( numDesired, address, out ptrs ) );
                    // TODO: what happens if we can't read that many pointers? What error is returned?
                    Util.Assert( numDesired == ptrs.Length );
                    return ptrs;
                } );
        } // end ReadMemPointers()

        /// <summary>
        ///    Tries to read pointers from the target's memory. It still throws if we
        ///    get an error besides ERROR_READ_FAULT.
        /// </summary>
        public bool TryReadMemPointers( ulong address, uint numDesired, bool failIfReadFewer, out ulong[] pointers )
        {
            ulong[] tmpPointers = null;
            var retval = ExecuteOnDbgEngThread( () =>
                {
                    int hr = m_debugDataSpaces.ReadPointersVirtual( numDesired, address, out tmpPointers );
                    // TODO: what happens if we can't read that many pointers? What error is returned?
                    if( 0 == hr )
                        return true;
                    else if( HR_ERROR_READ_FAULT == hr )
                        return false;
                    else
                    {
                        CheckHr( hr ); // we'll still throw for other types of problems.
                        return false;
                    }
                } );
            pointers = tmpPointers;
            return retval;
        } // end ReadMemPointers()


        public ulong ReadMemSlotInPointerArray( ulong baseAddr, uint index )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    ulong[] tmpPointers;
                    CheckHr( m_debugDataSpaces.ReadPointersVirtual( 1,
                                                                    (ulong) (baseAddr + (index * PointerSize)),
                                                                    out tmpPointers ) );

                    return tmpPointers[ 0 ];
                } );
        } // end ReadMemSlotInPointerArray()


        public short ReadMemAs_short( ulong address )
        {
            return ReadMemAs< short >( address );
        } // end ReadMemAs_short()

        public ushort ReadMemAs_ushort( ulong address )
        {
            return ReadMemAs< ushort >( address );
        } // end ReadMemAs_ushort()

        public byte ReadMemAs_byte( ulong address )
        {
            return ReadMemAs< byte >( address );
        } // end ReadMemAs_byte()

        public sbyte ReadMemAs_sbyte( ulong address )
        {
            return ReadMemAs< sbyte >( address );
        } // end ReadMemAs_sbyte()

        public char ReadMemAs_WCHAR( ulong address )
        {
            return ReadMemAs< char >( address );
        } // end ReadMemAs_WCHAR()

        public int ReadMemAs_Int32( ulong address )
        {
            return ReadMemAs< int >( address );
        } // end ReadMemAs_Int32()

        public uint ReadMemAs_UInt32( ulong address )
        {
            return ReadMemAs< uint >( address );
        } // end ReadMemAs_UInt32()

        public long ReadMemAs_Int64( ulong address )
        {
            return ReadMemAs< long >( address );
        } // end ReadMemAs_Int64()

        public ulong ReadMemAs_UInt64( ulong address )
        {
            return ReadMemAs< ulong >( address );
        } // end ReadMemAs_UInt64()

        public bool ReadMemAs_CPlusPlusBool( ulong address )
        {
            return ReadMemAs< byte >( address ) != 0;
        } // end ReadMemAs_CPlusPlusBool()

        public float ReadMemAs_float( ulong address )
        {
            return ReadMemAs< float >( address );
        } // end ReadMemAs_float()

        public double ReadMemAs_double( ulong address )
        {
            return ReadMemAs< double >( address );
        } // end ReadMemAs_double()

        public ulong ReadMemAs_pointer( ulong address )
        {
            ulong[] raw = ReadMemPointers( address, 1, true );
            return raw[ 0 ];
        } // end ReadMemAs_pointer()

        public Guid ReadMemAs_Guid( ulong address )
        {
            return ReadMemAs< Guid >( address );
        } // end ReadMemAs_Guid()

        public T[] ReadMemAs_TArray< T >( ulong address, uint count ) where T: unmanaged
        {
            T[] dest = new T[ count ];

            ReadMem( address, (Memory< T >) dest );

            return dest;
        } // end ReadMemAs_TArray< T >()

        // This way looks weird, but it's much more convenient for PowerShell.
        // If this ever gets implemented in PowerShell, then we could get rid of this
        // method: https://github.com/PowerShell/PowerShell/issues/5146
        public Array ReadMemAs_TArray( ulong address, uint count, Type T )
        {
            if( null == T )
                throw new ArgumentNullException( "T" );

            // It would be nice change this to read directly into the pre-allocated
            // buffer, like the generic overload of this method, but I don't have a way to
            // pin or get the address of the first element of an "Array" instance.
            byte[] raw = ReadMem( address, count * (uint) Marshal.SizeOf( T ), true );
            Array dest = Array.CreateInstance( T, (int) count );
            Buffer.BlockCopy( raw, 0, dest, 0, raw.Length );
            return dest;
        } // end ReadMemAs_TArray()


        //
        // ANSI, zero-terminated
        //


        /// <summary>
        ///    Interprets memory as a [pointer to a] NULL-terminated ANSI string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to 4096 characters. (It
        ///    does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_pszString( ulong address )
        {
            return ReadMemAs_pszString( address, 4096, CODE_PAGE.ACP );
        }

        /// <summary>
        ///    Interprets memory as a [pointer to a] NULL-terminated ANSI string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to maxCch characters. (It
        ///    does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_pszString( ulong address, uint maxCch )
        {
            return ReadMemAs_pszString( address, maxCch, CODE_PAGE.ACP );
        }

        /// <summary>
        ///    Interprets memory as a [pointer to a] NULL-terminated MBCS string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to maxCch characters. (It
        ///    does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_pszString( ulong address, uint maxCch, CODE_PAGE codePage )
        {
            ulong deref = ReadMemAs_pointer( address );
            if( 0 == deref )
                return null;

            return ReadMemAs_szString( deref, maxCch, codePage );
        }

        /// <summary>
        ///    Interprets memory as a NULL-terminated ANSI string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to 2048 characters (4096
        ///    bytes). (It does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_szString( ulong address )
        {
            return ReadMemAs_szString( address, 2048, CODE_PAGE.ACP );
        } // end ReadMemAs_szString()

        /// <summary>
        ///    Interprets memory as a NULL-terminated ANSI string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to maxCch characters. (It
        ///    does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_szString( ulong address, uint maxCch )
        {
            return ReadMemAs_szString( address, maxCch, CODE_PAGE.ACP );
        }

        /// <summary>
        ///    Interprets memory as a NULL-terminated MBCS string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to maxCch characters. (It
        ///    does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_szString( ulong address, uint maxCch, CODE_PAGE codePage )
        {
            // TODO: might want to put a cap on maxCch?

            return ExecuteOnDbgEngThread( () =>
                {
                    // ReadMultiByteStringVirtualWide is buggy.
                    // https://github.com/Microsoft/DbgShell/issues/39

                 // string result;
                 // _CheckMemoryReadHr( address,
                 //                     m_debugDataSpaces.ReadMultiByteStringVirtualWide( address,
                 //                                                                       maxCch,
                 //                                                                       codePage,
                 //                                                                       out result ) );
                 // return result;

                    var encoding = Encoding.GetEncoding( (int) codePage );
                    return _ReadMemAs_xszString_worker( address,
                                                        maxCch,
                                                        1,
                                                        encoding,
                                                        failIfNoNullTerminatorFound: false );
                } );
        } // end ReadMemAs_szString()


        //
        // UTF-16, zero-terminated
        //


        /// <summary>
        ///    Interprets memory as a [pointer to a] NULL-terminated, UTF-16 string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to 2048 characters. (It
        ///    does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_pwszString( ulong address )
        {
            ulong deref = ReadMemAs_pointer( address );
            if( 0 == deref )
                return null;

            return ReadMemAs_wszString( deref );
        } // end ReadMemAs_pwszString()

        /// <summary>
        ///    Interprets memory as a [pointer to a] NULL-terminated, UTF-16 string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to maxCch characters. (It
        ///    does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_pwszString( ulong address, uint maxCch )
        {
            ulong deref = ReadMemAs_pointer( address );
            if( 0 == deref )
                return null;

            return ReadMemAs_wszString( deref, maxCch );
        } // end ReadMemAs_pwszString()

        /// <summary>
        ///    Interprets memory as a NULL-terminated, UTF-16 string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to 2048 characters. (It
        ///    does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_wszString( ulong address )
        {
            return ReadMemAs_wszString( address, 2048 );
        }

        /// <summary>
        ///    Interprets memory as a NULL-terminated, UTF-16 string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to maxCch characters. (It
        ///    does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_wszString( ulong address, uint maxCch )
        {
            return ReadMemAs_wszString( address, maxCch, false );
        }


        private static Exception _CreateNoNullTerminatorException( string opName,
                                                                   ulong address,
                                                                   int maxCch )
        {
            return new DbgProviderException( Util.Sprintf( "No null terminator found when attempting to read string at {0}, maxCch {1}.",
                                                           Util.FormatQWord( address ),
                                                           maxCch ),
                                             Util.Sprintf( "{0}_NoNullTerminator", opName ),
                                             System.Management.Automation.ErrorCategory.InvalidData,
                                             address );
        }

        /// <summary>
        ///    Interprets memory as a NULL-terminated, UTF-16 string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to maxCch characters.
        ///    Whether or not finding a NULL terminator is a problem is up to the caller.
        ///    (so this can be used for "counted" strings as well as NULL-terminated
        ///    strings, which is handy for times when you have a WCHAR[], but don't know
        ///    if the entire thing is all string, or if you should stop after a NULL.)
        /// </remarks>
        public string ReadMemAs_wszString( ulong address,
                                           uint maxCch,
                                           bool failIfNoNullTerminatorFound )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    // TODO: might want to put a cap on maxCch?

                    // On the plus side, they fixed my bug (INTa0696e45,
                    // "ReadUnicodeStringVirtualWide does not respect MaxBytes
                    // parameter"). Unfortunately, they didn't take my suggestion to make
                    // ReadUnicodeStringVirtualWide return a different error code if it
                    // read MaxBytes without finding a NULL terminator. So for actual
                    // "counted" strings, you can't use it. *sigh*
                    //
                    // So we'll just have to implement our own using ReadVirtual.
                    //
                 // string result;
                 // _CheckMemoryReadHr( address,
                 //                     m_debugDataSpaces.ReadUnicodeStringVirtualWide( address,
                 //                                                                     maxCch * 2,
                 //                                                                     out result ) );
                 // return result;

                    return _ReadMemAs_xszString_worker( address,
                                                        maxCch,
                                                        2,
                                                        Encoding.Unicode,
                                                        failIfNoNullTerminatorFound );
                } );
        } // end ReadMemAs_wszString()


        private int __zStringChunkReadSize;

        /// <summary>
        /// The memory chunk read size for zero-terminated string reading functions.
        /// Exposing this as a property is primarily useful for testing.
        /// </summary>
        public int ZStringChunkReadSize
        {
            get
            {
                if( 0 == __zStringChunkReadSize )
                    return 1024;

                return __zStringChunkReadSize;
            }

            set
            {
                if( (value < 0) || (value > (1 << 26)) ) // "64 MB ought to be enough for anybody"
                    throw new ArgumentOutOfRangeException();

                __zStringChunkReadSize = value;
            }
        }


        /// <summary>
        ///    Worker function to read memory until it finds a null, has read maxCch
        ///    elements, or runs out of readable memory (whichever comes first), and
        ///    returns the results as a string.
        /// </summary>
        private unsafe string _ReadMemAs_xszString_worker( ulong address,
                                                           uint maxCch,
                                                           uint elemSize, // too bad this isn't part of Encoding
                                                           Encoding encoding,
                                                           bool failIfNoNullTerminatorFound )
        {
            if( elemSize > 4 )
                throw new Exception( "What sort of encoding is this?" );

            List< byte[] > chunks = null;

            int cchChunk = Math.Min( (int) maxCch, ZStringChunkReadSize );
            int totalCharsRead = 0;
            int totalBytesRead = 0;

            while( true )
            {
                int charsLeft = (int) maxCch - totalCharsRead;

                int cchRequested = Math.Min( charsLeft, cchChunk );

                bool ranOutOfReadableMemory = false;

                uint bytesRequested = (uint) cchRequested * elemSize;
                byte[] bytes = new byte[ bytesRequested ];
                uint bytesRead;
                fixed( byte* pBytes = bytes )
                {
                    int hr = m_debugDataSpaces.ReadVirtualDirect( address + ((uint) totalBytesRead),
                                                                  bytesRequested,
                                                                  pBytes,
                                                                  out bytesRead );

                    if( (null == chunks) || (0 == chunks.Count) )
                    {
                        // This is the first read.
                        _CheckMemoryReadHr( address, hr );
                    }

                    // Else if we've already read some and we subsequently run out of
                    // memory to read, that's okay.
                    if( (0 != hr) || (bytesRead < bytesRequested) )
                    {
                        ranOutOfReadableMemory = true;

                        if( 0 != hr )
                        {
                            Util.Assert( bytesRead == 0 );
                        }
                    }
                } // end fixed( pBytes )

                bool isNullTerminator( int baseIdx )
                {
                    for( int j = 0; j < elemSize; j++ )
                    {
                        if( 0 != bytes[ baseIdx + j ] )
                        {
                            return false;
                        }
                    }

                    return true;
                }

                // If we read an odd number of bytes, we ignore the remainder. That should
                // be okay, since that should only be possible on the last read (because
                // we ask for a multiple of the element size), and only for a misaligned
                // request.
                for( int i = 0; i < (bytesRead / elemSize); i++ )
                {
                    int baseIdx = (int) (i * elemSize);
                    if( isNullTerminator( baseIdx ) )
                    {
                        return _GatherString( chunks, bytes, baseIdx, encoding );
                    }
                }

                //
                // We haven't found a null terminator yet.
                //

                totalCharsRead += (int) (bytesRead / elemSize);

                if( ranOutOfReadableMemory ||     // <- we won't be able to get any more
                    (totalCharsRead >= maxCch) )  // <- we mustn't get any more
                {
                    if( failIfNoNullTerminatorFound )
                    {
                        throw _CreateNoNullTerminatorException( "ReadEncodedString", address, (int) maxCch );
                    }
                    return _GatherString( chunks, bytes, (int) bytesRead, encoding );
                }

                if( null == chunks )
                    chunks = new List< byte[] >();

                totalBytesRead += (int) bytesRead;
                chunks.Add( bytes );

                // TODO: check for cancellation? Not likely needed, but not hard to do, either?
            } // end while( need to read another chunk )
        } // end ReadMemAs_xszString()


        private unsafe static string _GatherString( List< byte[] > chunks,
                                                    byte[] last,
                                                    int cbValid,
                                                    Encoding encoding )
        {
            if( (null == chunks) && (cbValid == 0) )
            {
                // This isn't just an optimization--we need to skip the code below because
                // the "fixed" keyword, when used with a 0-length array, will yield a null
                // pointer, and then the decoder.Convert API will complain.
                //
                // (I did file a bug for Decoder.Convert to accept a null char buffer when
                // the size of it is zero anyway, but it was Wont' Fix-ed. I didn't bother
                // filing a bug for the "fixed" keyword; I doubt they could change that.)
                return String.Empty;
            }

            var decoder = encoding.GetDecoder();

            int cch = 0;

            if( null != chunks )
            {
                foreach( var chunk in chunks )
                {
                    cch += decoder.GetCharCount( chunk, 0, chunk.Length, flush: false );
                }
            }
            // N.B. we pass cbValid not last.Length!
            cch += decoder.GetCharCount( last, 0, cbValid, flush: true );

            Util.Assert( cch > 0 );

            var chars = new char[ cch ];

            int cchRemaining = cch;
            int bytesConsumed = 0;
            int charsFilledIn = 0;
            bool completed = false;

            fixed( char* pChars = chars )
            {
                char* cursor = pChars;

                if( null != chunks )
                {
                    foreach( var chunk in chunks )
                    {
                        fixed( byte* pChunk = chunk )
                        {
                            decoder.Convert( pChunk,
                                             chunk.Length,
                                             cursor,
                                             cchRemaining,
                                             false, // flush
                                             out bytesConsumed,
                                             out charsFilledIn,
                                             out completed );

                            cchRemaining -= charsFilledIn;
                            cursor += charsFilledIn;
                            Util.Assert( bytesConsumed == chunk.Length );
                            // completed may or may not be true; for instance we might
                            // have run into only the first byte of a pair.
                        } // fixed( pChunk )
                    } // end foreach( chunk )
                } // end if( chunks )

                fixed( byte* pLast = last )
                {
                    decoder.Convert( pLast,
                                     cbValid, // N.B. Not last.Length!
                                     cursor,
                                     cchRemaining,
                                     true, // flush
                                     out bytesConsumed,
                                     out charsFilledIn,
                                     out completed );

                    cchRemaining -= charsFilledIn;
                    Util.Assert( cchRemaining == 0 );
                    Util.Assert( bytesConsumed == cbValid );

                    if( !completed )
                    {
                        // Since we're going to do our best even in the face of bad data,
                        // this isn't a reason to fail, but we'll at least note it in the
                        // log.
                        LogManager.Trace( "Warning: string decoding did not complete. Bad data?" );
                    }
                }
            } // fixed( pChars )

            return new String( chars );
        } // end _GatherString()



        //
        // UTF-8, zero-terminated
        //


        /// <summary>
        ///    Interprets memory as a [pointer to a] NULL-terminated, UTF-8 string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to 2048 characters. (It
        ///    does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_putf8szString( ulong address )
        {
            ulong deref = ReadMemAs_pointer( address );
            if( 0 == deref )
                return null;

            return ReadMemAs_utf8szString( deref );
        } // end ReadMemAs_putf8szString()

        /// <summary>
        ///    Interprets memory as a [pointer to a] NULL-terminated, UTF-8 string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to maxCch characters. (It
        ///    does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_putf8szString( ulong address, uint maxCch )
        {
            ulong deref = ReadMemAs_pointer( address );
            if( 0 == deref )
                return null;

            return ReadMemAs_utf8szString( deref, maxCch );
        } // end ReadMemAs_putf8szString()

        /// <summary>
        ///    Interprets memory as a NULL-terminated, UTF-8 string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to 2048 characters. (It
        ///    does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_utf8szString( ulong address )
        {
            return ReadMemAs_utf8szString( address, 2048 );
        }

        /// <summary>
        ///    Interprets memory as a NULL-terminated, UTF-8 string.
        /// </summary>
        /// <remarks>
        ///    If there is no NULL terminator, it will read up to maxCch characters. (It
        ///    does not fail if it doesn't find a NULL.)
        /// </remarks>
        public string ReadMemAs_utf8szString( ulong address, uint maxCch )
        {
            return ReadMemAs_szString( address, maxCch, CODE_PAGE.UTF8 );
        } // end ReadMemAs_utf8szString()


        //
        // Counted strings
        //


        /// <summary>
        ///    Interprets memory as a [pointer to a] "counted" ASCII string.
        /// </summary>
        /// <remarks>
        ///    It will not trim a NULL terminator or any other embedded NULLs.
        /// </remarks>
        public string ReadMemAs_pAsciiString( ulong address,
                                              uint cb )
        {
            ulong deref = ReadMemAs_pointer( address );
            if( 0 == deref )
                return null;

            return ReadMemAs_String( deref, cb, Encoding.ASCII );
        } // end ReadMemAs_pAsciiString()

        /// <summary>
        ///    Interprets memory as a "counted" ASCII string.
        /// </summary>
        /// <remarks>
        ///    It will not trim a NULL terminator or any other embedded NULLs.
        /// </remarks>
        public string ReadMemAs_AsciiString( ulong address,
                                             uint cb )
        {
            return ReadMemAs_String( address, cb, Encoding.ASCII );
        } // end ReadMemAs_AsciiString()

        /// <summary>
        ///    Interprets memory as a [pointer to a] "counted" UTF8 string.
        /// </summary>
        /// <remarks>
        ///    It will not trim a NULL terminator or any other embedded NULLs.
        /// </remarks>
        public string ReadMemAs_pUtf8String( ulong address,
                                             uint cb )
        {
            ulong deref = ReadMemAs_pointer( address );
            if( 0 == deref )
                return null;

            return ReadMemAs_String( deref, cb, Encoding.UTF8 );
        } // end ReadMemAs_pUtf8String()

        /// <summary>
        ///    Interprets memory as a "counted" UTF8 string.
        /// </summary>
        /// <remarks>
        ///    It will not trim a NULL terminator or any other embedded NULLs.
        /// </remarks>
        public string ReadMemAs_Utf8String( ulong address,
                                            uint cb )
        {
            return ReadMemAs_String( address, cb, Encoding.UTF8 );
        } // end ReadMemAs_Utf8String()

        /// <summary>
        ///    Interprets memory as a [pointer to a] "counted" UTF16 string.
        /// </summary>
        /// <remarks>
        ///    It will not trim a NULL terminator or any other embedded NULLs.
        /// </remarks>
        public string ReadMemAs_pUnicodeString( ulong address,
                                                uint cb )
        {
            ulong deref = ReadMemAs_pointer( address );
            if( 0 == deref )
                return null;

            return ReadMemAs_String( deref, cb, Encoding.Unicode );
        } // end ReadMemAs_pUnicodeString()

        /// <summary>
        ///    Interprets memory as a "counted" UTF16 string.
        /// </summary>
        /// <remarks>
        ///    It will not trim a NULL terminator or any other embedded NULLs.
        /// </remarks>
        public string ReadMemAs_UnicodeString( ulong address,
                                               uint cb )
        {
            return ReadMemAs_String( address, cb, Encoding.Unicode );
        } // end ReadMemAs_UnicodeString()

        /// <summary>
        ///    Interprets memory as a "counted" string.
        /// </summary>
        /// <remarks>
        ///    It will not trim a NULL terminator or any other embedded NULLs.
        /// </remarks>
        public string ReadMemAs_String( ulong address,
                                        uint cb,
                                        Encoding encoding )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    byte[] raw = ReadMem( address, cb, failIfReadSmaller: true );
                    // This is supposed to be a counted string, not NULL-terminated, so we
                    // do not attempt to trim terminating NULLs. (The whole string could
                    // be full of embedded NULLs, and we wouldn't want to chop one out
                    // just because it happened to be at the end.) (We don't know what
                    // size the characters are, anyway.)
                    return encoding.GetString( raw );
                } );
        } // end ReadMemAs_String()


        public bool TryReadMemAs_pointer( ulong address, out ulong value )
        {
            ulong[] raw;
            if( TryReadMemPointers( address, 1, true, out raw ) )
            {
                value = raw[ 0 ];
                return true;
            }
            else
            {
                value = InvalidAddress;
                return false;
            }
        } // end ReadMemAs_double()


        /// <summary>
        ///    Attempts to read a 1-, 2-, 4-, or 8-byte integer and expand it to a ulong.
        /// </summary>
        public bool TryReadMemAs_integer( ulong address, uint length, bool signExtend, out ulong result )
        {
            result = 0;
            if( (length != 1) &&
                (length != 2) &&
                (length != 4) &&
                (length != 8) )
            {
                throw new DbgProviderException( Util.Sprintf( "This API does not support {0}-byte integers.",
                                                              length ),
                                                "UnsupportedIntegerSize",
                                                System.Management.Automation.ErrorCategory.LimitsExceeded );
            }

            byte[] raw;
            if( !TryReadMem( address, length, true, out raw ) )
                return false;

            switch( length )
            {
                case 1:
                    if( signExtend )
                        result = (ulong) (sbyte) raw[ 0 ];
                    else
                        result = (ulong) raw[ 0 ];
                    break;
                case 2:
                    if( signExtend )
                        result = (ulong) BitConverter.ToInt16( raw, 0 );
                    else
                        result = (ulong) BitConverter.ToUInt16( raw, 0 );
                    break;
                case 4:
                    if( signExtend )
                        result = (ulong) BitConverter.ToInt32( raw, 0 );
                    else
                        result = (ulong) BitConverter.ToUInt32( raw, 0 );
                    break;
                case 8:
                    if( signExtend )
                        result = (ulong) BitConverter.ToInt64( raw, 0 );
                    else
                        result = BitConverter.ToUInt64( raw, 0 );
                    break;
            }
            return true;
        } // end TryReadMemAs_integer()

      //public double ReadMemAs_longDouble( ulong address )
      //{
      //    byte[] raw = ReadMem( address, 10, true );
      //    return BitConverter.To...
      //    TODO: need to write code to do this for RegisterSet as well.
      //} // end ReadMemAs_double()


        private const string _NT_SYMBOL_PATH = "_NT_SYMBOL_PATH";

        private void _EnsureSymbolsLoaded( DbgModuleInfo mod, CancellationToken cancelToken )
        {
            if( null == m_sympath )
            {
                if( String.IsNullOrEmpty( SymbolPath ) )
                {
                    // If we don't have a symbol path, the call to ReloadWide will fail
                    // (with error 0x80070057). We can set it to anything as long as it is
                    // not empty. (The path does not have to be valid or accessible; but
                    // we might as well set it to something useful.)
                    //
                    // We can end up here if there is no _NT_SYMBOL_PATH environment
                    // variable.
                    //
                    // BUT... we can ALSO end up here depending on when we ask dbgeng for
                    // its symbol path. If it's before dbgeng has done its own
                    // initialization, it will tell us "empty", but then later initialize
                    // it to _NT_SYMBOL_PATH. So we'll have to try to use _NT_SYMBOL_PATH
                    // too, and then fall back if we also find that it does not exist.

                    string tmp = Environment.GetEnvironmentVariable( _NT_SYMBOL_PATH );
                    if( String.IsNullOrEmpty( tmp ) )
                    {
                        tmp = "SRV*";
                        LogManager.Trace( "No symbol path; initializing it to default value ({0}).", tmp );
                    }
                    SymbolPath = tmp;
                }
            }

            ExecuteOnDbgEngThread( () =>
                {
                    cancelToken.ThrowIfCancellationRequested();

                    string modFileName = Path.GetFileName( mod.ImageName );
                    if( mod.SymbolType == DEBUG_SYMTYPE.DEFERRED )
                    {
                        // TODO: progress output?
                        CheckHr( m_debugSymbols.ReloadWide( " /f " + modFileName ) );
                    }

                    if( mod.SymbolType == DEBUG_SYMTYPE.NONE )
                    {
                        // Couldn't find symbols? Let's try looking next to the binary.
                        //
                        // If we find something, we'll only temporarily set the symbol path, to
                        // keep the symbol path looking clean.
                        //
                        // NOTE: After I checked in this code, the next day at work I could not
                        // manage to get into this code path anymore. Turns out it only repro'ed
                        // on the home wifi where I don't have access to http://symweb (and
                        // OpenDNS answers for it; don't know if that makes a difference or not).
                        // I thought I was able to repro it with the [latest] native debuggers
                        // yesterday, but I can't today, so this code /probably/ doesn't need to
                        // exist once I update dbgeng.
                        var curDir = Environment.CurrentDirectory;

                        var imgNames = new string[] { mod.ImageName, mod.LoadedImageName, mod.MappedImageName };
                        foreach( var imgName in imgNames )
                        {
                            cancelToken.ThrowIfCancellationRequested();

                            string path = imgName;
                            if( !Path.IsPathRooted( path ) )
                                path = Path.Combine( curDir, path );

                            LogManager.Trace( "Looking for image at: {0}", path );
                            if( File.Exists( path ) )
                            {
                                LogManager.Trace( "Found it. Now looking for .pdb next to it..." );
                                var pdbPath = Path.ChangeExtension( path, "pdb" );
                                if( File.Exists( pdbPath ) )
                                {
                                    var newSymPathElem = Path.GetDirectoryName( pdbPath );
                                    LogManager.Trace( "Found pdb. Attempting to add {0} to the symbol path.",
                                                      newSymPathElem );

                                    var oldSymPath = SymbolPath;
                                    bool noException = false;
                                    try
                                    {
                                        SymbolPath = newSymPathElem + ";" + oldSymPath;
                                        CheckHr( m_debugSymbols.ReloadWide( " /f " + modFileName ) );
                                        if( mod.SymbolType == DEBUG_SYMTYPE.PDB )
                                            LogManager.Trace( "It worked! Loaded the pdb." );
                                        else
                                            LogManager.Trace( "It didn't work." ); // could be mismatched .pdb, etc.
                                        noException = true;
                                    }
                                    finally
                                    {
                                        LogManager.Trace( "Putting old symbol path back: {0}", oldSymPath );
                                        try
                                        {
                                            SymbolPath = oldSymPath;
                                        }
                                        catch( DbgEngException dee )
                                        {
                                            if( noException )
                                                throw;

                                            LogManager.Trace( "Ignoring error resetting symbol path: {0}",
                                                              Util.GetExceptionMessages( dee ) );
                                        }
                                    } // end finally
                                } // end if( pdb exists )
                            } // end if( file exists )
                        } // end for( each img name )
                    } // end if( first reload attempt failed )
                } );
        } // end _EnsureSymbolsLoaded()


        private void _EnsureSymbolsLoaded( string modPattern, CancellationToken cancelToken )
        {
            int numMatches = 0;
            foreach( DbgModuleInfo mod in MatchModuleName( modPattern ) )
            {
                numMatches++;
                _EnsureSymbolsLoaded( mod, cancelToken );
            }
            if( (0 == numMatches) &&
                !System.Management.Automation.WildcardPattern.ContainsWildcardCharacters( modPattern ) )
            {
                // TODO: better error
                throw new DbgProviderException( Util.Sprintf( "No such module: {0}", modPattern ),
                                                "NoSuchModule",
                                                System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                modPattern );
            }
        }

        public IEnumerable< DbgModuleInfo > MatchModuleName( string modNamePattern )
        {
            System.Management.Automation.WildcardPattern wcPat =
                new System.Management.Automation.WildcardPattern( modNamePattern, System.Management.Automation.WildcardOptions.CultureInvariant |
                                                                                  System.Management.Automation.WildcardOptions.IgnoreCase );
            foreach( var mod in Modules )
            {
                if( wcPat.IsMatch( mod.Name ) )
                    yield return mod;
            }
        } // end MatchModuleName()


        private static bool _SymMatchesCategory( SymTag symTag,
                                                 uint typeIndex,
                                                 ulong address,
                                                 GlobalSymbolCategory category )
        {
            if( GlobalSymbolCategory.All == category )
                return true;

            if( 0 != (int) (category & GlobalSymbolCategory.Function) )
            {
                if( (symTag == SymTag.FunctionType) ||
                    (symTag == SymTag.Function) || // TODO: I forget... can this show up on a global symbol?
                    (symTag == SymTag.InlineSite) )
                {
                    return true;
                }
            }

            if( 0 != (int) (category & GlobalSymbolCategory.NoType) )
            {
                // Could be symTag of 'PublicSymbol', but typeIndex of 0.
                if( (symTag == SymTag.Null) || (typeIndex == 0) )
                {
                    return true;
                }
            }

            if( 0 != (int) (category & GlobalSymbolCategory.Data) )
            {
                // TODO: I'm not sure this is totally right. What about a BaseType with
                // BasicType.btNoType? But perhaps I don't need to worry about that here.
                if( (symTag != SymTag.Null) &&
                    (symTag != SymTag.FunctionType) &&
                    (symTag != SymTag.Function) &&
                    (typeIndex != 0) &&
                    (address != 0) )
                {
                    return true;
                }
            }
            return false;
        } // end _SymMatchesCategory()


        //
        // When to use FindSymbol_Enum (corresponding to dbghelp's SymEnumSymbols) versus
        // FindSymbol_Search (corresponding to dbghelp's SymSearch)?
        //
        // One is not a superset of the other. SymSearch seems to work better for global-
        // type things (for instance, it can return constants), whereas SymEnumSymbols
        // seems to return better results for things like "near" symbols and locals (not
        // that you'd really want to use SymEnumSymbols for locals).
        //
        // So you just have to try them out and see which is better for the scenario in
        // question.
        //
        // Q: Well, what does the documentation say about their differences?
        // A: Bwa-ha-ha-ha-ha!
        // Q: Okay, then what did the debugger team say?
        // A: *crickets*
        //
        // SymSearch seems to work fine when we have true private symbols... but not as
        // well with stripped public PDBs that have had some information removed.
        //

        public IEnumerable< DbgSymbol > FindSymbol_Enum( string pattern )
        {
            return FindSymbol_Enum( pattern, GlobalSymbolCategory.All );
        }

        public IEnumerable< DbgSymbol > FindSymbol_Enum( string pattern, GlobalSymbolCategory category )
        {
            return FindSymbol_Enum( pattern, category, CancellationToken.None );
        }

        public IEnumerable< DbgSymbol > FindSymbol_Enum( string pattern,
                                                         GlobalSymbolCategory category,
                                                         CancellationToken cancelToken )
        {
            return FindSymbol_Enum( pattern, category, cancelToken, SymEnumOptions.All );
        }

        public IEnumerable< DbgSymbol > FindSymbol_Enum( string pattern,
                                                         GlobalSymbolCategory category,
                                                         CancellationToken cancelToken,
                                                         SymEnumOptions symEnumOptions )
        {
            // May need to trigger symbol load; dbghelp won't do it.

            string modName;
            string bareSym;
            ulong offset;
            DbgProvider.ParseSymbolName( pattern, out modName, out bareSym, out offset );
            pattern = AdjustPattern( pattern, ref modName );

            _EnsureSymbolsLoaded( modName, cancelToken );

            return StreamFromDbgEngThread< DbgPublicSymbol >( cancelToken, ( ct, emit ) =>
            {
                var target = GetCurrentTarget();
                foreach( var si in DbgHelp.EnumSymbols( DebuggerInterface,
                                                        0,
                                                        pattern,
                                                        cancelToken,
                                                        symEnumOptions ) )
                {
                    if( cancelToken.IsCancellationRequested )
                        break;

                    if( _SymMatchesCategory( si.Tag, si.TypeIndex, si.Address, category ) )
                        emit( new DbgPublicSymbol( this, si, target ) );
                }
            } );
        } // end FindSymbol_Enum()

        private string AdjustPattern( string pattern, ref string modName )
        {
            var bangIdx = pattern.IndexOf( '!' );
            if( bangIdx < 0 )
            {
                Util.Assert( 0 == Util.Strcmp_OI( "*", modName ) );
                pattern = "*!" + pattern;
            }
            else if ( "nt" == modName )
            {
                var ntdllModule = GetNtdllModuleNative();
                modName = ntdllModule.Name;
                pattern = modName + "!" + pattern.Substring( bangIdx + 1 );
            }
            else if( "nt32" == modName )
            {
                var ntdllModule = GetNtdllModule32();
                modName = ntdllModule.Name;
                pattern = modName + "!" + pattern.Substring( bangIdx + 1 );
            }

            return pattern;
        }

        public IEnumerable< DbgSymbol > FindSymbol_Search( string pattern )
        {
            return FindSymbol_Search( pattern, GlobalSymbolCategory.All );
        }

        public IEnumerable< DbgSymbol > FindSymbol_Search( string pattern, GlobalSymbolCategory category )
        {
            return FindSymbol_Search( pattern, category, CancellationToken.None );
        }

        public IEnumerable< DbgSymbol > FindSymbol_Search( string pattern,
                                                           GlobalSymbolCategory category,
                                                           CancellationToken cancelToken )
        {
            return FindSymbol_Search( pattern, category, cancelToken, SymSearchOptions.GlobalsOnly );
        }

        public IEnumerable< DbgSymbol > FindSymbol_Search( string pattern,
                                                           GlobalSymbolCategory category,
                                                           CancellationToken cancelToken,
                                                           SymSearchOptions symSearchOptions )
        {
            // May need to trigger symbol load; dbghelp won't do it.

            string modName;
            string bareSym;
            ulong offset;
            DbgProvider.ParseSymbolName( pattern, out modName, out bareSym, out offset );
            pattern = AdjustPattern( pattern, ref modName );

            _EnsureSymbolsLoaded( modName, cancelToken );

            return StreamFromDbgEngThread< DbgPublicSymbol >( cancelToken, ( ct, emit ) =>
            {
                var process = GetCurrentTarget();
                foreach( var si in DbgHelp.SymSearch( DebuggerInterface,
                                                        0,
                                                        pattern,
                                                        cancelToken,
                                                        symSearchOptions ) )
                {
                    if( cancelToken.IsCancellationRequested )
                        break;

                    if( _SymMatchesCategory( si.Tag, si.TypeIndex, si.Address, category ) )
                        emit( new DbgPublicSymbol( this, si, process ) );
                }
            } );
        } // end FindSymbol_Search()


        public IEnumerable< DbgSymbol > FindSymbolForAddr( ulong address,
                                                           GlobalSymbolCategory category )
        {
            return FindSymbolForAddr( address, category, CancellationToken.None );
        }

        // TODO: Unfortunately, this does not seem to be working for certain sorts of
        // addresses/symbols. For instance, vtable addresses don't seem to yield any
        // results, and yet GetNearNameByOffset seems to find something. The problem is
        // that I think some addresses might have multiple of those sorts of symbols
        // associated with them--for instance, sometimes the linker "collapses" vtables
        // together when they look exactly the same, but I think there should be or might
        // be records for all of the original vtables in the .pdb, but I don't have a way
        // to enumerat them yet, since GetNearNameByOffset only returns a single thing,
        // and FindSymbolForAddr is not finding anything at all. Perhaps this could be
        // fixed by a move to DIA.
        public IEnumerable< DbgSymbol > FindSymbolForAddr( ulong address,
                                                           GlobalSymbolCategory category,
                                                           CancellationToken cancelToken )
        {
            // May need to trigger symbol load; dbghelp won't do it.

            var mod = GetModuleByAddress( address );
            _EnsureSymbolsLoaded( mod.Name, cancelToken );

            return StreamFromDbgEngThread<DbgPublicSymbol>( cancelToken, ( ct, emit ) =>
            {
                Task t = ExecuteOnDbgEngThreadAsync( () =>
                    {
                        var target = GetCurrentTarget();
                        foreach( var si in DbgHelp.EnumSymbolsForAddr( DebuggerInterface,
                                                                       address,
                                                                       ct ) )
                        {
                            if( ct.IsCancellationRequested )
                                break;

                            // Or should I just pass "address" here?
                            if( _SymMatchesCategory( si.Tag, si.TypeIndex, si.Address, category ) )
                                emit( new DbgPublicSymbol( this, si, target ) );
                        }
                    } ); // end dbgeng thread task
            } );
        } // end FindSymbolForAddr()


        public DbgNamedTypeInfo TryGetSingleTypeByName( string typeName )
        {
            return TryGetSingleTypeByName( typeName, CancellationToken.None );
        }

        /// <summary>
        ///    Gets just a single type--no wildcards allowed. Returns null if it can't
        ///    find it.
        /// </summary>
        public DbgNamedTypeInfo TryGetSingleTypeByName( string typeName,
                                                        CancellationToken cancelToken )
        {
            // May need to trigger symbol load; dbghelp won't do it.

            if( typeName.IndexOf( '!' ) < 0 )
            {
                throw new ArgumentException( "You must include the module name. Wildcards are not permitted.",
                                             "typeName" );
            }

            string modName;
            string bareSym;
            ulong offset;
            DbgProvider.ParseSymbolName( typeName, out modName, out bareSym, out offset );


            var mods = GetModuleByName( modName, cancelToken ).ToArray();

            if( mods.Length > 1 )
            {
                throw new ArgumentException( Util.Sprintf( "Module name matches more than one module: {0}",
                                                           modName ) );
            }
            else if( 0 == mods.Length )
            {
                throw new ArgumentException( Util.Sprintf( "Module not found: {0}",
                                                           modName ) );
            }

            DbgModuleInfo mod = mods[ 0 ];

            return GetModuleTypeByName( mod, bareSym, cancelToken );
        } // end GetSingleTypeByName()

        public DbgNamedTypeInfo GetModuleTypeByName( DbgModuleInfo module, 
                                                     string typeName,
                                                     CancellationToken cancelToken = default)
        {

            _EnsureSymbolsLoaded( module, cancelToken );
            return ExecuteOnDbgEngThread( () =>
            {
                SymbolInfo symInfo = DbgHelp.TryGetTypeFromName( m_debugClient,
                                                                 module.BaseAddress,
                                                                 typeName );

                if( null != symInfo )
                {
                    return DbgNamedTypeInfo.GetNamedTypeInfo( this,
                                                              symInfo.ModBase,
                                                              symInfo.TypeIndex,
                                                              symInfo.Tag,
                                                              GetCurrentTarget() );
                }

                return null;
            } );
        } // end GetModuleTypeByName()

        public IEnumerable<DbgNamedTypeInfo> GetTypeInfoByName( string pattern )
        {
            return GetTypeInfoByName( pattern, false, CancellationToken.None );
        }

        public IEnumerable<DbgNamedTypeInfo> GetTypeInfoByName( string pattern,
                                                                bool noFollowTypeDefs )
        {
            return GetTypeInfoByName( pattern, noFollowTypeDefs, CancellationToken.None );
        }

        public IEnumerable<DbgNamedTypeInfo> GetTypeInfoByName( string pattern,
                                                                CancellationToken cancelToken )
        {
            return GetTypeInfoByName( pattern, false, cancelToken );
        }

        public IEnumerable< DbgNamedTypeInfo > GetTypeInfoByName( string pattern,
                                                                  bool noFollowTypeDefs,
                                                                  CancellationToken cancelToken )
        {
            var target = GetCurrentTarget();
            foreach( var dnti in _GetTypeInfoByName( pattern,
                                                     cancelToken,
                                                     ( si ) => DbgTypeInfo.GetNamedTypeInfo( this,
                                                                                             si.ModBase,
                                                                                             si.TypeIndex,
                                                                                             si.Tag,
                                                                                             target ) ) )
            {
                if( noFollowTypeDefs )
                {
                    yield return dnti;
                }
                else
                {
                    DbgNamedTypeInfo followed = dnti;
                    while( SymTag.Typedef == followed.SymTag )
                    {
                        followed = ((DbgTypedefTypeInfo) dnti).RepresentedType;
                    }
                    yield return followed;
                }
            } // end foreach( dnti )
        } // end GetTypeInfoByName()


        private IEnumerable< T > _GetTypeInfoByName< T >( string pattern,
                                                          CancellationToken cancelToken,
                                                          Func< SymbolInfo, T > convert )
        {
            // May need to trigger symbol load; dbghelp won't do it.
            string modName;
            string bareSym;
            ulong offset;

            DbgProvider.ParseSymbolName( pattern, out modName, out bareSym, out offset );
            pattern = AdjustPattern( pattern, ref modName );

            _EnsureSymbolsLoaded( modName, cancelToken );

            return StreamFromDbgEngThread<T>( cancelToken, ( ct, emit ) =>
            {
#if DEBUG
                // Handy for testing cancellation.
                bool testGoSlow = !String.IsNullOrEmpty( Environment.GetEnvironmentVariable( "__DBGSHELL_TEST_SLOW" ) );
#endif
                foreach( var si in DbgHelp.EnumTypesByName( DebuggerInterface,
                                                            0,
                                                            pattern,
                                                            ct ) )
                {
#if DEBUG
                    if( testGoSlow )
                    {
                        for( int i = 0; i < 10; i++ )
                        {
                            Console.WriteLine( "slow stuff..." );
                            Thread.Sleep( 1000 );
                        }
                    }
#endif
                    emit( convert( si ) );

                    if( ct.IsCancellationRequested )
                        break;
                }
            } );
        } // end _GetTypeInfoByName()


        internal string _TryGetImplementingTypeNameFromVTableAddr( ulong vtableAddr,
                                                                   out ulong firstSlotAddr )
        {
            firstSlotAddr = 0;
            ulong disp;
            string vtableSymName;

            if( !TryReadMemAs_pointer( vtableAddr, out firstSlotAddr ) )
            {
                LogManager.Trace( "_TryGetImplementingTypeNameFromVTableAddr: could not read address {0}.",
                                  Util.FormatHalfOrFullQWord( vtableAddr ) );

                return null;
            }

            if( !TryGetNameByOffset( firstSlotAddr, out vtableSymName, out disp ) )
            {
                LogManager.Trace( "Warning: failed getting symbolic name ({0}). Probably bad data.",
                                  Util.FormatHalfOrFullQWord( firstSlotAddr ) );
                return null;
            }

            if( 0 != disp )
            {
                LogManager.Trace( "Warning: could not get exact symbolic name for vftable." );
                return null;
            }

            const string vftableSuffix = "::`vftable'";
            if( !vtableSymName.EndsWith( vftableSuffix, StringComparison.OrdinalIgnoreCase ) )
            {
                LogManager.Trace( "Warning: I did not expect a vftable with this format of a name: {0}", vtableSymName );
                return null;
            }

            string implementingTypeName = vtableSymName.Substring( 0, vtableSymName.Length - vftableSuffix.Length );
            return implementingTypeName;
        } // end _TryGetImplementingTypeNameFromVTableAddr()


        /// <summary>
        ///    Attempts to get a DbgUdtTypeInfo for a name, when there are no wildcards
        ///    ("*" means "pointer"), and __ptr64 decorations are ignored.
        /// </summary>
        internal DbgUdtTypeInfo _TryGetUdtForTypeNameExact( string implementingTypeName )
        {
            // Note that we use GetSingleTypeByName, instead of EnumTypesByName. This is
            // because we want an exact match, and sometimes a type name might have a '*'
            // in it (ex. "foo<unsigned int *, char>), which would be interpreted as a
            // wildcard by EnumTypesByName.

            var derivedType = TryGetSingleTypeByName( implementingTypeName ) as DbgUdtTypeInfo;

            if( null == derivedType )
            {
                string without__ptr64_thing = implementingTypeName.Replace( " __ptr64", "" );
                if( without__ptr64_thing != implementingTypeName )
                {
                    LogManager.Trace( "Attempting another lookup, but without __ptr64" );

                    derivedType = TryGetSingleTypeByName( without__ptr64_thing ) as DbgUdtTypeInfo;
                }
            }

            return derivedType;
        } // end _TryGetUdtForTypeNameExact()


        public DbgUdtTypeInfo TryGuessTypeForAddress( ulong addr )
        {
            DbgUdtTypeInfo udt = null;

            ulong firstSlotPtr;
            string implementingTypeName = _TryGetImplementingTypeNameFromVTableAddr( addr, // hopefully the vtable is at offset 0
                                                                                     out firstSlotPtr );

            if( !String.IsNullOrEmpty( implementingTypeName ) )
            {
                LogManager.Trace( "Address {0} looks like a {1}.",
                                  Util.FormatHalfOrFullQWord( addr ),
                                  implementingTypeName );

                udt = _TryGetUdtForTypeNameExact( implementingTypeName );

                if( null == udt )
                {
                    LogManager.Trace( "Hmmm... couldn't find that type, though." );
                }
            }

            return udt;
        } // end TryGuessTypeForAddress()


        private string m_sympath;
        public string SymbolPath
        {
            get
            {
                if( null == m_sympath )
                {
                    ExecuteOnDbgEngThread( () =>
                        {
                            CheckHr( m_debugSymbols.GetSymbolPathWide( out m_sympath ) );
                        } );
                }
                return m_sympath;
            }
            set
            {
                m_sympath = null;
                ExecuteOnDbgEngThread( () =>
                    {
                        CheckHr( m_debugSymbols.SetSymbolPathWide( value ) );
                    } );
                // There's some oddity with how DbgEng deals with the symbol path: before
                // attaching to any system, DbgEng will say that the symbol path is blank.
                // Then, once it gets attached to something, it will append the value of
                // the environment variable _NT_SYMBOL_PATH to it. So if you fire up
                // DbgShell, run ".sympath D:\foo", then attach to something, your symbol
                // path will be "D:\foo;%_NT_SYMBOL_PATH%". One way we can try to prevent
                // that behavior from resulting in an unexpected symbol path is to change
                // the environment variable, too. (DbgEng won't re-append
                // %_NT_SYMBOL_PATH% if it's already there for some reason.)
                //
                // This only affects the environment of this process, and child processes.
                Environment.SetEnvironmentVariable( _NT_SYMBOL_PATH, value );
                // Unfortunately, this workaround does not currently work. DbgEng either
                // caches the value from the process start, or grabs the environment
                // variable from a different context (such as the machine-wide var).
                // TODO: File a bug against DbgEng about it.
            }
        }


        public void ReloadSymbols( string module,
                                   bool moduleIsLiteral,
                                   ulong address,
                                   ulong size,
                                   string timestamp, // TODO: I don't even know what format this is in... I think a hex number of certain size, but I'm not sure
                                   bool notLazy,
                                   bool ignoreMismatched,
                                   bool overwriteDownstream,
                                   bool unload )
        {
            if( moduleIsLiteral && String.IsNullOrEmpty( module ) )
                throw new ArgumentException( "The moduleIsLiteral flag was passed, but no module." );

            if( unload )
            {
                if( ignoreMismatched || overwriteDownstream )
                    throw new ArgumentException( "The ignoreMismatched and overwriteDownstream options are not valid with the unload option." );

                if( (0 != address) || (0 != size) || (!String.IsNullOrEmpty( timestamp )) )
                    throw new ArgumentException( "The address/size/timestamp options are not valid with the unload option." );
            }

            // The IDebugSymbolsX.ReloadWide takes a string of parameters just like the ".reload" command.
            // That's not my favorite way to expose an API, but okay, whatever.
            StringBuilder sb = new StringBuilder();
            if( notLazy )
                sb.Append( " /f" );

            if( ignoreMismatched )
                sb.Append( " /i" );

            if( overwriteDownstream )
                sb.Append( " /o" );

            if( unload )
            {
                sb.Append( " /u" );
                // TODO: BUGBUG:
                throw new Exception( "The underlying dbgeng command appears to be broken when it comes to unloading. At least for managed, user-mode apps." );
            }

            if( moduleIsLiteral )
                sb.Append( " /w" );

            sb.Append( " /v" ); // TODO:

            // If the symbol path is empty, that's okay. (The user was already warned when
            // the symbol path was set, if they went through Set-DbgSymbolPath/.sympath).
            if( String.IsNullOrEmpty( SymbolPath ) )
                sb.Append( " /P" ); // internal-only switch ("SkipPathChecks") because we don't want an error

            if( !String.IsNullOrEmpty( module ) )
                sb.Append( " " ).Append( module );

            if( 0 != address )
            {
                sb.Append( "=" ).Append( address.ToString( "x" ) ); // TODO: BUGBUG: verify this. What if the default radix is changed?
                if( 0 != size )
                {
                    sb.Append( "," ).Append( size.ToString( "x" ) );
                    if( !String.IsNullOrEmpty( timestamp ) )
                    {
                        sb.Append( "," ).Append( timestamp );
                    }
                }
            }

            ExecuteOnDbgEngThread( () =>
                {
                    string reloadCmd = sb.ToString();
                    LogManager.Trace( "ReloadSymbols: reloadCmd is: {0}", reloadCmd );
                    // TODO: progress output?
                    // TODO: better error, esp. for 0x80070002 (file not found)
                    CheckHr( m_debugSymbols.ReloadWide( reloadCmd ) );

                    if( unload )
                    {
                        // If you just unload, entries disappear from your module list. We want the modules there; just
                        // with unloaded symbols. So now we execute a plain ".reload" (which lazily defers actual symbol loading)
                        // to re-populate the module list.
                        CheckHr( m_debugSymbols.ReloadWide( String.Empty ) );
                    }
                } );
        } // end ReloadSymbols


        public void AbandonProcess()
        {
            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugClient.AbandonCurrentProcess() );
                } );
        } // end AbandonProcess()


        public void KillProcess()
        {
            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugClient.TerminateCurrentProcess() );
                } );
        } // end KillProcess()


        public void DetachProcess()
        {
            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugClient.DetachCurrentProcess() );
                } );
        } // end DetachProcess()


        public DEBUG_STATUS GetExecutionStatus()
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    DEBUG_STATUS ds;
                    CheckHr( m_debugControl.GetExecutionStatusEx( out ds ) );
                    return ds;
                } );
        } // end GetExecutionStatus()


        public uint GetTypeIdForAddress( ulong address )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    uint typeId;
                    ulong modBase;
                    int hr = m_debugSymbols.GetOffsetTypeId( address, out typeId, out modBase );
                    if( E_NOINTERFACE == hr )
                    {
                        // TODO: Better "not found" error?
                        throw new DbgProviderException( Util.Sprintf( "No type ID found for address 0x{0:D8}.",
                                                                      DbgProvider.FormatUInt64( address, true ) ),
                                                        "NoTypeIdForAddress",
                                                        System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                        address ).SetHResult( hr );
                    }
                    else
                    {
                        CheckHr( hr );
                    }
                    return typeId;
                } );
        } // end GetOffsetTypeId()


        public DbgAssemblyOptions AssemblyOptions
        {
            get
            {
                return ExecuteOnDbgEngThread( () =>
                    {
                        DEBUG_ASMOPT options;
                        CheckHr( m_debugControl.GetAssemblyOptions( out options ) );
                        return (DbgAssemblyOptions) options;
                    } );
            }
            set
            {
                ExecuteOnDbgEngThread( () =>
                    {
                        CheckHr( m_debugControl.SetAssemblyOptions( (DEBUG_ASMOPT) value ) );
                    } );
            }
        } // end property AssemblyOptions


        public string Disassemble( ulong address, out ulong endAddress )
        {
            ulong tmpEndAddress = 0;
            string disasm = null;
            ExecuteOnDbgEngThread( () =>
                 {
                     CheckHr( m_debugControl.DisassembleWide( address,
                                                              (DEBUG_DISASM) 0,
                                                              out disasm,
                                                              out tmpEndAddress ) );
                     return disasm;
                 } );
            endAddress = tmpEndAddress;
            return disasm;
        } // end Disassemble()


        public ulong GetNearInstruction( ulong address, int delta )
        {
            ulong nearAddress = 0;
            ExecuteOnDbgEngThread( () =>
                 {
                     CheckHr( m_debugControl.GetNearInstruction( address, delta, out nearAddress ) );
                 } );
            return nearAddress;
        } // end GetNearInstruction()


        /// <summary>
        ///    DbgEng can be attached to multiple "systems". (For instance multiple dump
        ///    files, multiple kernel targets, a set of processes.) This gets the id for
        ///    the current "system".
        ///
        ///    Returns DEBUG_ANY_ID if there is no current system (which, btw, does not
        ///    necessarily mean that there are no systems at all).
        ///
        /// </summary>
        public uint GetCurrentDbgEngSystemId()
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    uint sysId = DEBUG_ANY_ID;
                    int hr = m_debugSystemObjects.GetCurrentSystemId( out sysId );

                    if( (hr != 0) && (hr != E_UNEXPECTED) )
                    {
                        CheckHr( hr );
                    }

                    return sysId;
                } );
        } // end GetCurrentDbgEngSystemId()


        public uint[] GetDbgEngSystemIds()
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    uint numSysIds;
                    CheckHr( m_debugSystemObjects.GetNumberSystems( out numSysIds ) );
                    uint[] sysIds;
                    CheckHr( m_debugSystemObjects.GetSystemIdsByIndex( 0, numSysIds, out sysIds ) );
                    return sysIds;
                } );
        } // end GetDbgEngSystemIds()


        public void SetCurrentDbgEngSystemId( uint sysId )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugSystemObjects.SetCurrentSystemId( sysId ) );
                } );
        } // end GetCurrentDbgEngSystemId()


        public void GetProcessIds( out uint[] dbgEngIds, out uint[] sysIds )
        {
            uint[] tmpEngIds = null;
            uint[] tmpSysIds = null;
            ExecuteOnDbgEngThread( () =>
                {
                    uint numIds;
                    CheckHr( m_debugSystemObjects.GetNumberProcesses( out numIds ) );
                    CheckHr( m_debugSystemObjects.GetProcessIdsByIndex( 0,
                                                                        numIds,
                                                                        out tmpEngIds,
                                                                        out tmpSysIds ) );
                } );
            dbgEngIds = tmpEngIds;
            sysIds = tmpSysIds;
        } // end GetProcessIds()


        public void SetCurrentProcessId( uint dbgEngId )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugSystemObjects.SetCurrentProcessId( dbgEngId ) );
                } );
        }


        /// <summary>
        ///    Gets a DbgEngContext object that represents dbgeng.dll's current context.
        ///    (If no context, returns DbgEngContext.CurrentOrNone.) (This method
        ///    shouldn't throw unless something is really messed up.)
        /// </summary>
        public DbgEngContext GetCurrentDbgEngContext()
        {
            uint sysId           = DEBUG_ANY_ID; // -1
            ulong procIdOrAddr   = DEBUG_ANY_ID; // -1
            ulong threadIdOrAddr = DEBUG_ANY_ID; // -1
            uint frameId         = DEBUG_ANY_ID; // -1
            bool? isKernel       = null;

            ExecuteOnDbgEngThread( () =>
                {
                    int hr = m_debugSystemObjects.GetCurrentSystemId( out sysId );
                    if( E_UNEXPECTED == hr )
                    {
                        LogManager.Trace( "GetCurrentDbgEngContext: no current system." );
                        return;
                    }
                    else
                    {
                        // TODO: Maybe I should ignore all errors, and not just E_UNEXPECTED?
                        CheckHr( hr );
                    }

                    DEBUG_CLASS targetClass;
                    DEBUG_CLASS_QUALIFIER qualifier;
                    hr = m_debugControl.GetDebuggeeType( out targetClass, out qualifier );
                    if( 0 != hr )
                    {
                        LogManager.Trace( "GetCurrentDbgEngContext: could not determine if kernel mode: {0}.", hr );
                        return;
                    }

                    isKernel = (DEBUG_CLASS.KERNEL == targetClass);

                    if( isKernel.Value )
                    {
                        hr = m_debugSystemObjects.GetImplicitProcessDataOffset( out procIdOrAddr );
                        if( 0 != hr )
                        {
                            LogManager.Trace( "GetCurrentDbgEngContext: no current kernel-mode process: {0}", hr );

                            // We seem to get E_UNEXPECTED when .kill-ing a kernel target.
                            // (maybe I shouldn't even allow that, but I haven't provided
                            // any alternative yet)
                            if( hr != E_UNEXPECTED )
                            {
                                Util.Fail( "Unexpectedly couldn't GetImplicitProcessDataOffset: " +
                                           Util.FormatErrorCode( hr ).ToString() );
                            }
                            return;
                        }

                        hr = m_debugSystemObjects.GetImplicitThreadDataOffset( out threadIdOrAddr );
                        if( 0 != hr )
                        {
                            LogManager.Trace( "GetCurrentDbgEngContext: no current kernel-mode thread: {0}", hr );
                            Util.Fail( "is this possible?" );
                            return;
                        }
                    }
                    else
                    {
                        uint uiProcId, uiThreadId;

                        hr = m_debugSystemObjects.GetCurrentProcessId( out uiProcId );
                        if( 0 != hr )
                        {
                            LogManager.Trace( "GetCurrentDbgEngContext: no current process: {0}.", hr );
                            return;
                        }
                        procIdOrAddr = uiProcId;

                        hr = m_debugSystemObjects.GetCurrentThreadId( out uiThreadId );
                        if( 0 != hr )
                        {
                            LogManager.Trace( "GetCurrentDbgEngContext: no current thread: {0}.", hr );
                            return;
                        }
                        threadIdOrAddr = uiThreadId;
                    }

                    hr = m_debugSymbols.GetCurrentScopeFrameIndexEx( DEBUG_FRAME.DEFAULT, out frameId );
                    if( 0 != hr )
                    {
                        LogManager.Trace( "GetCurrentDbgEngContext: no current frame: {0}." , hr );
                        return;
                    }
                } );

            m_cachedContext = new DbgEngContext( sysId,
                                                 procIdOrAddr,
                                                 threadIdOrAddr,
                                                 frameId,
                                                 isKernel );
            return m_cachedContext;
        } // end GetCurrentDbgEngContext()


        private DbgEngContext m_cachedContext = DbgEngContext.CurrentOrNone;

        public void SetCurrentDbgEngContext( DbgEngContext context )
        {
            SetCurrentDbgEngContext( context, false, false );
        }

        /// <summary>
        ///    Changes the current DbgEng.dll context.
        /// </summary>
        /// <param name="context">The new context.</param>
        /// <param name="temporary">If true, will not change the next address to disassemble (SetNextAddressStuffAsync).</param>
        public void SetCurrentDbgEngContext( DbgEngContext context, bool temporary )
        {
            SetCurrentDbgEngContext( context, temporary, false );
        }

        /// <summary>
        ///    Changes the current DbgEng.dll context.
        /// </summary>
        /// <param name="context">The new context.</param>
        /// <param name="temporary">If true, will not change the next address to disassemble (SetNextAddressStuffAsync).</param>
        /// <param name="force">Set the context even if the last cached context indicates we don't need to.</param>
        public void SetCurrentDbgEngContext( DbgEngContext context, bool temporary, bool force )
        {
            if( null == context )
                throw new ArgumentNullException( "context" );

            if( (context == m_cachedContext) && !force )
                return;

            if( m_cachedContext.IsStrictSuperSetOf( context ) && !force )
                return;

            LogManager.Trace( "SetCurrentDbgEngContext( {0}, {1} )", context, force );

            ExecuteOnDbgEngThread( () =>
                {
                    bool noException = false;
                    uint sId = m_cachedContext.SystemIndex;
                    ulong pIdOrAddr = m_cachedContext.ProcessIndexOrAddress;
                    ulong tIdOrAddr = m_cachedContext.ThreadIndexOrAddress;
                    uint fId = m_cachedContext.FrameIndex;

                    try
                    {
                        bool setSomething = false;
                        if( context.SystemIndex != DEBUG_ANY_ID )
                        {
                         // if( force ||
                         //     (context.SystemIndex != m_cachedContext.SystemIndex) )
                         // {
                                LogManager.Trace( "Setting system: {0}", context.SystemIndex );
                                CheckHr( m_debugSystemObjects.SetCurrentSystemId( context.SystemIndex ) );
                                setSomething = true;
                                sId = context.SystemIndex;
                         // }
                        }

                        if( context.ProcessIndexOrAddress != DEBUG_ANY_ID )
                        {
                         // if( setSomething ||
                         //     force ||
                         //     (context.ProcessIndex != m_cachedContext.ProcessIndex) )
                         // {

                                uint pId = (uint) (context.ProcessIndexOrAddress & 0x00000000ffffffff);

                                if( ((ulong) pId) != context.ProcessIndexOrAddress)
                                {
                                    //
                                    // Kernel-mode process address.
                                    //

                                    LogManager.Trace( "Setting process context: 0x{0:x}", context.ProcessIndexOrAddress );
                                    CheckHr( m_debugSystemObjects.SetImplicitProcessDataOffset( context.ProcessIndexOrAddress ) );
                                }
                                else
                                {
                                    //
                                    // User-mode process dbgeng id.
                                    //

                                    LogManager.Trace( "Setting process id: {0}", pId );
                                    CheckHr( m_debugSystemObjects.SetCurrentProcessId( pId ) );
                                }

                                setSomething = true;
                                pIdOrAddr = context.ProcessIndexOrAddress;
                         // }
                        }

                        if( context.ThreadIndexOrAddress != DEBUG_ANY_ID )
                        {
                         // if( setSomething ||
                         //     force ||
                         //     (context.ThreadIndex != m_cachedContext.ThreadIndex) )
                         // {

                                uint tId = (uint) (context.ThreadIndexOrAddress & 0x00000000ffffffff);

                                if( ((ulong) tId) != context.ThreadIndexOrAddress)
                                {
                                    //
                                    // Kernel-mode thread address.
                                    //

                                    LogManager.Trace( "Setting thread context: 0x{0:x}", context.ThreadIndexOrAddress );
                                    CheckHr( m_debugSystemObjects.SetImplicitThreadDataOffset( context.ThreadIndexOrAddress ) );
                                }
                                else
                                {
                                    //
                                    // User-mode thread dbgeng id.
                                    //

                                    LogManager.Trace( "Setting thread id: {0}", tId );

                                    // TODO: If this returns 0x80004002 (E_NOINTERFACE), then
                                    // that means the current thread is not valid.
                                    int hr = m_debugSystemObjects.SetCurrentThreadId( tId );
                                    if( E_NOINTERFACE == hr )
                                    {
                                        throw new DbgEngException( hr,
                                                                   Util.Sprintf( "The current/requested thread ({0}) is not valid. You " +
                                                                                 "can manually switch threads or try running " +
                                                                                 "\"[MS.Dbg.DbgProvider]::ForceRebuildNamespace()\" to recover.",
                                                                                 context.ThreadIndexOrAddress ),
                                                                   "SetCurrentThreadIdFailed",
                                                                   System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                                   context );
                                    }
                                    else
                                    {
                                        CheckHr( hr );
                                    }
                                }

                                setSomething = true;
                                tIdOrAddr = context.ThreadIndexOrAddress;
                         // }
                        }

                        if( context.FrameIndex != DEBUG_ANY_ID )
                        {
                         // if( setSomething ||
                         //     force ||
                         //     (context.FrameIndex != m_cachedContext.FrameIndex) )
                         // {
                                LogManager.Trace( "Setting frame: {0}", context.FrameIndex );
                                CheckHr( m_debugSymbols.SetScopeFrameByIndexEx( DEBUG_FRAME.DEFAULT, context.FrameIndex ) );
                                setSomething = true;
                                fId = context.FrameIndex;
                         // }
                        }

                        Util.Assert( setSomething );

                        if( !temporary )
                        {
                            // This will execute synchrously since we're already on the dbgeng
                            // thread.
                            SetNextAddressStuffAsync();
                        }
                        noException = true;
                    }
                    finally
                    {
                        if( noException )
                            m_cachedContext = context;
                        else
                            m_cachedContext = new DbgEngContext( sId, pIdOrAddr, tIdOrAddr, fId );
                    }
                } );
        } // end SetCurrentDbgEngContext()


        private void _EnsureWeHaveFrameScopeContext()
        {
            LogManager.Trace( "_EnsureWeHaveFrameScopeContext" );

            // This doesn't really make any sense... but sometimes, after
            // killing/detaching, and then attaching to something else, we seemingly end
            // up with no scope frame context, so displaying registers fails. This tickles
            // dbgeng somehow, enough to make that not happen.

            var ctx = Debugger.GetCurrentDbgEngContext();
            if( ctx != DbgEngContext.CurrentOrNone )
            {
                Debugger.SetCurrentDbgEngContext( ctx, true, true );
            }
        } // end _EnsureWeHaveFrameScopeContext()


        internal Task SetNextAddressStuffAsync()
        {
            return ExecuteOnDbgEngThreadAsync( () =>
                {
                    ulong instructionPointer;
                    DEBUG_STACK_FRAME_EX nativeFrame;
                    CheckHr( m_debugSymbols.GetScopeEx( out instructionPointer, out nativeFrame, IntPtr.Zero, 0 ) );
                    Commands.ReadDbgDisassemblyCommand.NextAddrToDisassemble = instructionPointer;
                    Commands.ReadDbgMemoryCommand.NextAddressToDump = instructionPointer;
                } );
        } // end SetNextAddressStuffAsync()


     // public DbgEngContext MakeThreadContext( DbgEngContext ctx )
     // {
     //     return ExecuteOnDbgEngThread( () =>
     //         {
     //             uint sysId = ctx.SystemIndex;
     //             ulong procId = ctx.ProcessIndexOrAddress;
     //             ulong threadId = ctx.ThreadIndexOrAddress;
     //             bool neededChange = false;

     //             if( DEBUG_ANY_ID == sysId )
     //             {
     //                 CheckHr( m_debugSystemObjects.GetCurrentSystemId( out sysId ) );
     //                 neededChange = true;
     //             }

     //             if( IsKernelMode )
     //             {
     //                 TODO
     //             }
     //             else
     //             {
     //                 uint uiProcId, uiThreadId;

     //                 if( DEBUG_ANY_ID == procId )
     //                 {
     //                     CheckHr( m_debugSystemObjects.GetCurrentProcessId( out uiProcId ) );
     //                     neededChange = true;
     //                     procId = uiProcId;
     //                 }

     //                 if( DEBUG_ANY_ID == threadId )
     //                 {
     //                     CheckHr( m_debugSystemObjects.GetCurrentThreadId( out uiThreadId ) );
     //                     neededChange = true;
     //                     threadId = uiThreadId;
     //                 }
     //             }

     //             if( DEBUG_ANY_ID != ctx.FrameIndex )
     //                 neededChange = true;

     //             if( !neededChange )
     //                 return ctx;
     //             else
     //                 return new DbgEngContext( sysId,
     //                                           procId,
     //                                           threadId,
     //                                           DEBUG_ANY_ID,
     //                                           IsKernelMode );
     //         } );
     // } // end MakeThreadContext()


        private Dictionary< DbgEngContext, DbgTarget > m_targets = new Dictionary< DbgEngContext, DbgTarget >();

        /// <summary>
        ///    Returns a DbgTarget object representing the current target, or null if
        ///    there is none.
        /// </summary>
        public DbgTarget GetCurrentTarget() => ExecuteOnDbgEngThread( GetCurrentTargetInternal );

        private DbgTarget GetCurrentTargetInternal()
        {
            DbgEngContext ctx;
            int hr = m_debugSystemObjects.GetCurrentSystemId( out uint sysId );

            if( E_UNEXPECTED == hr )
            {
                return null; // No current target.
            }

            CheckHr( hr );

            bool kernelMode = IsKernelMode;

            if( kernelMode )
            {
                ctx = new DbgEngContext( sysId, true );
            }
            else
            {
                // User-mode
                CheckHr( m_debugSystemObjects.GetCurrentProcessId( out uint procId ) );
                ctx = new DbgEngContext( sysId, procId );
            }

            if( !m_targets.TryGetValue( ctx, out DbgTarget t ) )
            {
                if( kernelMode )
                {
                    t = new DbgKModeTarget( this, ctx, _GetKmTargetName( sysId ) );
                }
                else
                {
                    // User-mode
                    CheckHr( m_debugSystemObjects.GetCurrentProcessSystemId( out uint sysProcId ) );
                    t = new DbgUModeProcess( this, ctx, sysProcId, _GetUmTargetName( sysProcId ) );
                }

                t.ClrMdDisabled = m_clrMdDisabled;
                m_targets.Add( ctx, t );
            }

            return t;
        } // end GetCurrentTargetInternal()


        public DbgUModeProcess GetCurrentUModeProcess()
        {
            var proc = GetCurrentTarget() as DbgUModeProcess;

            if( null == proc )
            {
                throw new DbgProviderException( "No current user-mode process.",
                                                "NoUmodeProcess",
                                                System.Management.Automation.ErrorCategory.InvalidOperation,
                                                null );
            }

            return proc;
        } // end GetCurrentUModeProcess()


        public DbgKModeTarget GetCurrentKModeTarget()
        {
            var target = GetCurrentTarget() as DbgKModeTarget;

            if( null == target )
            {
                throw new DbgProviderException( "No current kernel-mode target.",
                                                "NoKmodeTarget",
                                                System.Management.Automation.ErrorCategory.InvalidOperation,
                                                null );
            }

            return target;
        } // end GetCurrentKModeTarget()


        public DbgTarget GetTargetForContext( DbgEngContext context )
        {
            DbgTarget target;
            context = context.AsTargetContext();
            if( !m_targets.TryGetValue( context, out target ) )
            {
                // This can happen in a (somewhat) legitimate case: somebody holds on to a
                // context past the end of the process, then try to get the process for
                // it.
                //
                // The other possibility is that our "process tree" is out of sync, which
                // would be pretty unfortunate, but I don't think there's anything we
                // could/should do about that here.

                LogManager.Trace( "No target found for context: {0}", context );
            }
            return target;
        } // end GetTargetForContext()


        /// <summary>
        ///    Enumerates "text replacements" (also known as aliases) in dbgeng. Note that
        ///    some aliases cannot always be retrieved (like "$ntsym" when not attached to
        ///    something).
        /// </summary>
        public IEnumerable< DbgAlias > EnumerateTextReplacements()
        {
            return StreamFromDbgEngThread< DbgAlias >( CancellationToken.None, ( ct, emit ) =>
            {
                uint numReplacements;
                CheckHr( m_debugControl.GetNumberTextReplacements( out numReplacements ) );
                for( uint i = 0; i < numReplacements; i++ )
                {
                    string aliasName, aliasValue;
                    int hr = m_debugControl.GetTextReplacementWide( i,
                                                                    out aliasName,
                                                                    out aliasValue );
                    if( hr == E_NOINTERFACE )
                    {
                        //
                        // This can happen when not attached to anything, when we get
                        // $ntsym et al.
                        //

                        LogManager.Trace( "   (could not get text replacement {0} (of {1}))",
                                          i,
                                          numReplacements );
                    }
                    else if( hr == 0 )
                    {
                        emit( new DbgAlias( aliasName, aliasValue, i ) );
                    }
                    else
                    {
                        Util.Fail( Util.Sprintf( "Unexpected failure from GetTextReplacementWide({0}): {1}",
                                                 i,
                                                 Util.FormatErrorCode( hr ) ) );
                    }
                }
            } );
        } // end EnumerateTextReplacements()


        public string GetTextReplacement( string aliasName )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    string aliasValue;
                    CheckHr( m_debugControl.GetTextReplacementWide( aliasName, out aliasValue ) );
                    return aliasValue;
                } );
        } // end GetTextReplacement()


        /// <summary>
        ///    Tries to get the dbg alias at the specified index. Note that alias indexes
        ///    are not stable, so be sure to check that the name matches what you expect.
        /// </summary>
        public DbgAlias GetTextReplacement( uint index )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    string aliasName, aliasValue;
                    int hr = m_debugControl.GetTextReplacementWide( index,
                                                                    out aliasName,
                                                                    out aliasValue );
                    if( hr == 0 )
                    {
                        return new DbgAlias( aliasName, aliasValue, index );
                    }
                    else if( hr != E_NOINTERFACE ) // can happen when not attached to anything, for things like $ntsym
                    {
                        CheckHr( hr );
                    }
                    return null;
                } );
        } // end GetTextReplacement()


        public bool TryGetTextReplacement( string aliasName, out string aliasValue )
        {
            string tmpAliasValue = null;
            bool retVal = ExecuteOnDbgEngThread( () =>
                {
                    int hr = m_debugControl.GetTextReplacementWide( aliasName, out tmpAliasValue );
                    return 0 == hr;
                    // If it fails, it probably returned E_NOINTERFACE, but we won't be so
                    // picky as to check.
                } );
            aliasValue = tmpAliasValue;
            return retVal;
        } // end GetTextReplacement()


        /// <summary>
        ///    Tries to get the dbg alias at the specified index. Note that alias indexes
        ///    are not stable, so be sure to check that the name matches what you expect.
        /// </summary>
        public bool TryGetTextReplacement( uint index, out DbgAlias alias )
        {
            alias = null;
            string aliasName = null;
            string aliasValue = null;
            bool retVal = ExecuteOnDbgEngThread( () =>
                {
                    int hr = m_debugControl.GetTextReplacementWide( index, out aliasName, out aliasValue );
                    return 0 == hr;
                    // If it fails, it probably returned E_NOINTERFACE, such as if we
                    // tried to get $ntsym when not attached to anything, but we won't be
                    // so picky as to check.
                } );

            if( retVal )
                alias = new DbgAlias( aliasName, aliasValue, index );

            return retVal;
        } // end TryGetTextReplacement()


        public void SetTextReplacement( string aliasName, string aliasValue )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugControl.SetTextReplacementWide( aliasName, aliasValue ) );
                } );
        } // end SetTextReplacement()


        private DbgSystemInfo[] m_sysTree;

        public IReadOnlyList< DbgSystemInfo > SystemTree
        {
            get
            {
                return m_sysTree.ToList().AsReadOnly();
            }
        }


        private const string cUmTargetNameTextReplacementNameFmt = "__DbgShell_targetName_u_{0}";
        private const string cKmTargetNameTextReplacementNameFmt = "__DbgShell_targetName_k_{0}";

        // We need to make sure that we never have two targets with the same name, so
        // that they'll all fit into a DbgProvider namespace.
        private HashSet< string > m_usedTargetNames = new HashSet< string >( StringComparer.OrdinalIgnoreCase );
        private int m_nextDistinguisher;

        private string _GetUmTargetName( uint sysPid )
        {
            string textReplName = Util.Sprintf( cUmTargetNameTextReplacementNameFmt,
                                                sysPid );
            string val;
            if( !String.IsNullOrEmpty( m_nextTargetName ) )
            {
                val = m_nextTargetName;
                m_nextTargetName = null;
            }
            else if( !TryGetTextReplacement( textReplName, out val ) )
            {
                string imageName = GetCurrentProcessExecutableName();

                // It could have path information with it. (I've seen that with a dump
                // file.)
                imageName = Path.GetFileNameWithoutExtension( imageName );

                // Debugger.chm sez "If the engine cannot determine the name of the
                // executable file, it [returns] "?NoImage?"". So it might need sanitizing
                // to get rid of characters that the namespace doesn't like.
                imageName = DbgProvider.SanitizeNamespaceName( imageName );

                val = Util.Sprintf( "{0}_{1}", imageName, sysPid );
            }

            while( m_usedTargetNames.Contains( val ) )
            {
                m_nextDistinguisher++;
                val += Util.Sprintf( "_{0}", m_nextDistinguisher );
            }
            m_usedTargetNames.Add( val );
            SetTextReplacement( textReplName, val );
            // Now if we detach and re-attach, we get the same target name
            // (unless overriden during re-attach).
            // TODO: We should clean up the "text replacement" when the process exits,
            // but right now I don't think there's a way to know what process exited.
            return val;
        } // end _GetUmTargetName()


        private string _GetKmTargetName( uint sysId )
        {
            string textReplName = Util.Sprintf( cKmTargetNameTextReplacementNameFmt,
                                                sysId );
            string val;
            if( !String.IsNullOrEmpty( m_nextTargetName ) )
            {
                val = m_nextTargetName;
                m_nextTargetName = null;
            }
            else if( !TryGetTextReplacement( textReplName, out val ) )
            {
                val = Util.Sprintf( "kmSys_{0}", sysId );
            }

            while( m_usedTargetNames.Contains( val ) )
            {
                m_nextDistinguisher++;
                val += Util.Sprintf( "_{0}", m_nextDistinguisher );
            }
            m_usedTargetNames.Add( val );
            SetTextReplacement( textReplName, val );
            // Now if we detach and re-attach, we get the same target name
            // (unless overriden during re-attach).
            // TODO: We should clean up the "text replacement" when the target exits,
            // but right now I don't think there's a way to know what process exited.
            return val;
        } // end _GetKmTargetName()


        public bool TryGetExistingUmTargetName( uint sysPid, out string targetName )
        {
            targetName = null;
            string textReplName = Util.Sprintf( cUmTargetNameTextReplacementNameFmt,
                                                sysPid );
            return TryGetTextReplacement( textReplName, out targetName );
        } // end TryGetExistingUmTargetName()


        private string m_nextTargetName;

        public void SetNextTargetName( string targetName )
        {
            // I used to assert that m_nextTargetName was not set, but it's not really
            // that helpful. For instance, it could happen in some pathological case, like
            // if we somehow failed horribly in the middle of an attach or something--but
            // it's not like we don't already know something is wrong in that case (and
            // we're about to fix it by setting the proper next target name). And there
            // are other corner-y cases, like if you start a process and use both
            // -SkipInitialBreakpoint and -SkipFinalBreakpoint, and nothing breaks in
            // before the process exits, then the process slides right by without ever
            // consuming m_nextTargetName. So I'll just make a note of it in the log
            // instead of asserting.
            if( !String.IsNullOrEmpty( m_nextTargetName ) )
                LogManager.Trace( "Ignoring stale m_nextTargetName (\"{0}\").", m_nextTargetName );

            m_nextTargetName = targetName;
        } // end SetNextTargetName()


        public IReadOnlyList< DbgTarget > Targets
        {
            get
            {
                return m_targets.Values.ToList().AsReadOnly();
            }
        } // end property Processes

        public void ForceRebuildProcessTree()
        {
            m_cachedContext = null;
            m_sysTree = null;
            m_targets.Clear();
            m_usedTargetNames.Clear();
            UpdateNamespace();
        }

        /// <summary>
        ///    This is merely a workaround for the fact that there is no way to cleanly
        ///    abandon a process, such that you'll be able to re-attach. It doesn't (and
        ///    can't) handle the general case; just a common case: if there is only a
        ///    single system, and no processes, call EndSession to clean stuff up.
        ///
        ///    TODO: BUG#
        /// </summary>
        private void _PruneEmptyCurrentSystem()
        {
            ExecuteOnDbgEngThread( () =>
                {
                    uint numSysIds;
                    CheckHr( m_debugSystemObjects.GetNumberSystems( out numSysIds ) );

                    if( numSysIds == 1 )
                    {
                        //
                        // We'll assume that if there is a system, then it should probably
                        // be the current system.
                        //

                        uint numProcs;
                        int hr = m_debugSystemObjects.GetNumberProcesses( out numProcs );

                        if( ((hr == S_OK) && (numProcs == 0)) ||
                            (hr == E_UNEXPECTED) )
                        {
                            LogManager.Trace( "Pruning empty system." );
                            EndSession( DEBUG_END.PASSIVE );
                        }
                    }
                } );
        } // end _PruneEmptyCurrentSystem()

        public void UpdateNamespace()
        {
            _PruneEmptyCurrentSystem();
            DbgSystemInfo[] curSystemTree = DbgSystemInfo.BuildSystemTree( this );
            List< DbgUmSystemInfo > umProcessesRemoved;
            List< DbgUmSystemInfo > umProcessesAdded;
            List< DbgKmSystemInfo > kmTargetsRemoved;
            List< DbgKmSystemInfo > kmTargetsAdded;

            if( null == m_sysTree )
                m_sysTree = new DbgSystemInfo[ 0 ];

            DbgSystemInfo.CompareTrees( m_sysTree,     // "before"
                                        curSystemTree, // "after"
                                        out umProcessesRemoved,
                                        out umProcessesAdded,
                                        out kmTargetsRemoved,
                                        out kmTargetsAdded );

            if( umProcessesRemoved.Count > 0 )
            {
                var toRemove = new List< DbgUModeProcess >( umProcessesRemoved.Count );
                // TODO: Is it just me or is this a little outa control?
                foreach( var procToRemove in umProcessesRemoved.SelectMany(
                    (sys) => { return sys.ProcessIds.Select(
                        (p,i) => { return new { SysId = sys.SystemIndex, ProcId = p, OsProcId = sys.ProcessOsIds[ i ] }; } ); } ) )
                {
                    foreach( var target in m_targets.Values )
                    {
                        var proc = target as DbgUModeProcess;
                        if( null == proc )
                            continue;

                        if( (proc.DbgEngSystemId == procToRemove.SysId) &&
                            (proc.DbgEngProcessId == procToRemove.ProcId) )
                        {
                            Util.Assert( proc.OsProcessId == procToRemove.OsProcId );
                            toRemove.Add( proc );
                            break;
                        }
                    }
                } // end foreach( procToRemove )

                foreach( var target in toRemove )
                {
                    bool removed = m_targets.Remove( target.Context );
                    Util.Assert( removed );
                    if( !String.IsNullOrEmpty( target.TargetFriendlyName ) )
                        m_usedTargetNames.Remove( target.TargetFriendlyName );
                }

                _RaiseEvent( UmProcessRemoved, new UmProcessRemovedEventArgs( this, toRemove ) );
            } // end if( umProcessesRemoved.Count )

            if( umProcessesAdded.Count > 0 )
            {
                var procsAdded = new List< DbgUModeProcess >( umProcessesAdded.Count );
                // TODO: Is it just me or is this a little outa control?
                foreach( var procToAdd in umProcessesAdded.SelectMany(
                    (sys) => { return sys.ProcessIds.Select(
                        (p,i) => { return new { SysId = sys.SystemIndex, ProcId = p, OsProcId = sys.ProcessOsIds[ i ] }; } ); } ) )
                {
                    var ctx = new DbgEngContext( procToAdd.SysId, procToAdd.ProcId );
                    // TODO: Would be nice to get rid of the requirement to have the
                    // context set when creating this object. (Currently needed to get
                    // IsLive, I think.)
                    using( new DbgEngContextSaver( this, ctx ) )
                    {
                        // TODO: Need to add TargetFriendlyName property or something.
                        var p = new DbgUModeProcess( this,
                                                     ctx,
                                                     procToAdd.OsProcId,
                                                     _GetUmTargetName( procToAdd.OsProcId ) );
                        p.ClrMdDisabled = m_clrMdDisabled;
                        procsAdded.Add( p );
                        //m_targets.Add( ctx, p );
                        // We'll use the indexer instead of Add, in case the "unfortunate"
                        // case in GetTargetForContext was hit.
                        m_targets[ ctx ] = p;
                    } // end using( context saver )
                } // end foreach( procToAdd )
                _RaiseEvent( UmProcessAdded, new UmProcessAddedEventArgs( this, procsAdded ) );

                _EnsureWeHaveFrameScopeContext();
            } // end if( umProcessesAdded.Count )

            if( kmTargetsRemoved.Count > 0 )
            {
                var toRemove = new List< DbgKModeTarget >( kmTargetsRemoved.Count );
                foreach( var si in kmTargetsRemoved )
                {
                    foreach( var target in m_targets.Values )
                    {
                        var kt = target as DbgKModeTarget;
                        if( null == kt )
                            continue;

                        if( kt.DbgEngSystemId == si.SystemIndex )
                        {
                            toRemove.Add( kt );
                            break;
                        }
                    }
                }

                foreach( var target in toRemove )
                {
                    bool removed = m_targets.Remove( target.Context );
                    Util.Assert( removed );
                    if( !String.IsNullOrEmpty( target.TargetFriendlyName ) )
                        m_usedTargetNames.Remove( target.TargetFriendlyName );
                }

                _RaiseEvent( KmTargetRemoved, new KmTargetRemovedEventArgs( this, toRemove ) );
            }

            if( kmTargetsAdded.Count > 0 )
            {
                var list = new List< DbgKModeTarget >( kmTargetsAdded.Count );
                foreach( var si in kmTargetsAdded )
                {
                    var ctx = new DbgEngContext( si.SystemIndex, true );
                    // TODO: Would be nice to get rid of the requirement to have the
                    // context set when creating this object. (Currently needed to get
                    // IsLive, I think.)
                    using( new DbgEngContextSaver( this, ctx ) )
                    {
                        // TODO: Need to add TargetFriendlyName property or something.
                        var t = new DbgKModeTarget( this,
                                                    ctx,
                                                    _GetKmTargetName( si.SystemIndex ) );
                        t.ClrMdDisabled = m_clrMdDisabled;
                        list.Add( t );
                        //m_targets.Add( ctx, t );
                        // We'll use the indexer instead of Add, in case the "unfortunate"
                        // case in GetTargetForContext was hit.
                        m_targets[ ctx ] = t;
                    } // end using( context saver )
                }

                _RaiseEvent( KmTargetAdded, new KmTargetAddedEventArgs( this, list ) );
            }

            m_sysTree = curSystemTree;
        } // end UpdateNamespace


        public void DiscardCachedModuleInfo()
        {
            foreach( var target in m_targets.Values )
            {
                target.DiscardCachedModuleInfo();
            }
        } // end DiscardCachedModuleInfo()

        // TODO: should I use this?
     // public void DiscardCachedModuleInfo( ulong modBase )
     // {
     //     foreach( var target in m_targets.Values )
     //     {
     //         target.RefreshModuleAt( modBase );
     //     }
     // } // end DiscardCachedModuleInfo()

        public void BumpSymbolCookie( ulong modBase )
        {
            // TODO/BUG: We don't know what context this is for, so we will do it for all
            // of them.
            foreach( var target in m_targets.Values )
            {
                target.BumpSymbolCookie( modBase );
            }

            //DbgHelp.DumpSyntheticTypeInfoForModule( m_debugClient, modBase );
            DbgHelp.DumpSyntheticTypeInfoForModule( modBase );
        } // end BumpSymbolCookie()


        public IEnumerable< dynamic > GetTypeInfoByNameRaw( string name )
        {
            return GetTypeInfoByNameRaw( name, CancellationToken.None );
        }

        public IEnumerable< dynamic > GetTypeInfoByNameRaw( string name,
                                                            CancellationToken cancelToken )
        {
            return _GetTypeInfoByName( name,
                                       cancelToken,
                                       ( si ) => _GetTypeInfoRaw( si.ModBase, si.TypeIndex ) );
        } // end GetTypeInfoByName()


        internal System.Management.Automation.PSObject _GetTypeInfoRaw( ulong modBase,
                                                                        uint typeId )
        {
            // TODO: Does this need to happen on the dbgeng thread?
            var pso = new System.Management.Automation.PSObject();

            foreach( var typeInfoObj in Enum.GetValues( typeof( IMAGEHLP_SYMBOL_TYPE_INFO ) ) )
            {
                var typeInfo = (IMAGEHLP_SYMBOL_TYPE_INFO) typeInfoObj;

                if( typeInfo == IMAGEHLP_SYMBOL_TYPE_INFO.IMAGEHLP_SYMBOL_TYPE_INFO_MAX )
                {
                    continue;
                }

                try
                {
                    object val = DbgHelp.SymGetTypeInfo( Debugger.DebuggerInterface,
                                                         modBase,
                                                         typeId,
                                                         typeInfo );
                    pso.Properties.Add( new SimplePSPropertyInfo( typeInfo.ToString(), val ) );
                }
                catch( DbgEngException )
                {
                    // Property not available.
                }
                catch( NotSupportedException )
                {
                }
            } // end foreach( IMAGEHLP_SYMBOL_TYPE_INFO )

            return pso;
        } // end _GetTypeInfoRaw()

        public Task WriteDumpAsync( string dumpfile,
                                    DEBUG_FORMAT flags,
                                    string comment,
                                    CancellationToken cancelToken )
        {
            var cancelRegistration = cancelToken.Register( Break, false );
            // TODO: Would be nice if dbgeng could give proper progress output somehow.
            return ExecuteOnDbgEngThreadAsync( () =>
                {
                    if( cancelToken.IsCancellationRequested )
                        return;

                    CheckHr( m_debugClient.WriteDumpFileWide( dumpfile,
                                                              0,                // file handle
                                                              DEBUG_DUMP.SMALL, // "mini", which can actually contain the most information
                                                              flags,
                                                              comment ) );
                    cancelRegistration.Dispose();
                } );
        } // end WriteDump()

        public string GetDumpFile( uint index, out uint type )
        {
            type = 0;
            uint tmpType;
            string dumpFile = null;
            ExecuteOnDbgEngThread( () =>
                 {
                     ulong handle;
                     CheckHr( m_debugClient.GetDumpFileWide( index, out dumpFile, out handle, out tmpType ) );
                 } );
            return dumpFile;
        }


        /// <summary>
        ///    Gets module version info. Should not return null, but members of the
        ///    returned object may be null or empty, such as if memory is not accessible.
        /// </summary>
        internal ModuleVersionInfo CreateModuleVersionInfo( ulong modBase )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    FixedFileInfo ffi = null;
                    VS_FIXEDFILEINFO nativeFfi;
                    int hr = m_debugSymbols.GetModuleVersionInformationWide_VS_FIXEDFILEINFO( modBase, out nativeFfi );
                    if( 0 == hr )
                        ffi = new FixedFileInfo( nativeFfi );

                    string comments = null;
                    string internalName = null;
                    string productName = null;
                    string companyName = null;
                    string legalCopyright = null;
                    string productVersion = null;
                    string fileDescription = null;
                    string legalTrademarks = null;
                    string privateBuild = null;
                    string fileVersion = null;
                    string originalFilename = null;
                    string specialBuild = null;

                    CultureInfo lang = null;
                    Encoding charSet = null;

                    // TODO: Should I expose the ability to get other translations?
                    uint[] langCodepagePairs;
                    hr = m_debugSymbols.GetModuleVersionInformationWide_Translations( modBase, out langCodepagePairs );
                    if( 0 == hr )
                    {
                        const uint english = 0x04b00409;
                        uint langCodepagePair = 0;
                        if( langCodepagePairs.Length > 0 )
                        {
                            langCodepagePair = langCodepagePairs[ 0 ];
                            if( langCodepagePairs.Contains( english ) )
                                langCodepagePair = english;
                        }

                        // We ignore the return code from all these calls, because none of
                        // these resources are required to be there.

                        m_debugSymbols.GetModuleVersionInformationWide_StringInfo( modBase,
                                                                                   langCodepagePair,
                                                                                   "Comments",
                                                                                   out comments );
                        m_debugSymbols.GetModuleVersionInformationWide_StringInfo( modBase,
                                                                                   langCodepagePair,
                                                                                   "InternalName",
                                                                                   out internalName );
                        m_debugSymbols.GetModuleVersionInformationWide_StringInfo( modBase,
                                                                                   langCodepagePair,
                                                                                   "ProductName",
                                                                                   out productName );
                        m_debugSymbols.GetModuleVersionInformationWide_StringInfo( modBase,
                                                                                   langCodepagePair,
                                                                                   "CompanyName",
                                                                                   out companyName );
                        m_debugSymbols.GetModuleVersionInformationWide_StringInfo( modBase,
                                                                                   langCodepagePair,
                                                                                   "LegalCopyright",
                                                                                   out legalCopyright );
                        m_debugSymbols.GetModuleVersionInformationWide_StringInfo( modBase,
                                                                                   langCodepagePair,
                                                                                   "ProductVersion",
                                                                                   out productVersion );
                        m_debugSymbols.GetModuleVersionInformationWide_StringInfo( modBase,
                                                                                   langCodepagePair,
                                                                                   "FileDescription",
                                                                                   out fileDescription );
                        m_debugSymbols.GetModuleVersionInformationWide_StringInfo( modBase,
                                                                                   langCodepagePair,
                                                                                   "LegalTrademarks",
                                                                                   out legalTrademarks );
                        m_debugSymbols.GetModuleVersionInformationWide_StringInfo( modBase,
                                                                                   langCodepagePair,
                                                                                   "PrivateBuild",
                                                                                   out privateBuild );
                        m_debugSymbols.GetModuleVersionInformationWide_StringInfo( modBase,
                                                                                   langCodepagePair,
                                                                                   "FileVersion",
                                                                                   out fileVersion );
                        m_debugSymbols.GetModuleVersionInformationWide_StringInfo( modBase,
                                                                                   langCodepagePair,
                                                                                   "OriginalFilename",
                                                                                   out originalFilename );
                        m_debugSymbols.GetModuleVersionInformationWide_StringInfo( modBase,
                                                                                   langCodepagePair,
                                                                                   "SpecialBuild",
                                                                                   out specialBuild );

                        int langWord = (int) (langCodepagePair & 0x0000ffff);
                        int charSetWord = (int) ((langCodepagePair & 0xffff0000) >> 16);

                        if( 0 != langWord )
                            lang = new CultureInfo( langWord );

                        if( 0 != charSetWord )
                            charSet = Encoding.GetEncoding( charSetWord );
                    }

                    var mvi = new ModuleVersionInfo( ffi,
                                                     lang,
                                                     charSet,
                                                     comments,
                                                     internalName,
                                                     productName,
                                                     companyName,
                                                     legalCopyright,
                                                     productVersion,
                                                     fileDescription,
                                                     legalTrademarks,
                                                     privateBuild,
                                                     fileVersion,
                                                     originalFilename,
                                                     specialBuild );
                    return mvi;
                } );
        } // end CreateModuleVersionInfo()


        internal DbgNamedTypeInfo _PickTypeInfoForName( string typeName,
                                                        CancellationToken cancelToken )
        {
            if( String.IsNullOrEmpty( typeName ) )
                throw new ArgumentException( "You must supply a type name.", "typeName" );

            if( typeName.IndexOf( '!' ) < 0 )
            {
                typeName = "*!" + typeName;
            }

            var types = GetTypeInfoByName( typeName, cancelToken ).ToList();
            // Not sure; GetTypeInfoByName might just return empty on cancellation?
            cancelToken.ThrowIfCancellationRequested();

            if( 0 == types.Count )
            {
                var dpe = new DbgProviderException( Util.Sprintf( "Could not find type '{0}'.",
                                                                  typeName ),
                                                    "TypeNotFound",
                                                    System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                    typeName );
                throw dpe;
            }
            else if( types.Count > 1 )
            {
                // Hmmm... I think I'll just use the first one. Hopefully that's okay.
                LogManager.Trace( "Warning: ambiguous type name \"{0}\" resolved to \"{1}\" ({2} other match(es)).",
                                  typeName,
                                  types[ 0 ].FullyQualifiedName,
                                  types.Count - 1 );
             // var dpe = new DbgProviderException( Util.Sprintf( "Type name '{0}' is ambiguous ({1} matches).",
             //                                                   typeName,
             //                                                   types.Count ),
             //                                     "TypeNameAmbiguous",
             //                                     System.Management.Automation.ErrorCategory.InvalidArgument,
             //                                     typeName );
             // throw dpe;
            }

            return types[ 0 ];
        } // end _PickTypeInfoForName()

        public System.Management.Automation.PSObject GetValueForAddressAndType( ulong address,
                                                                                string typeName,
                                                                                CancellationToken cancelToken )
        {

            DbgNamedTypeInfo type = _PickTypeInfoForName( typeName, cancelToken );
            return GetValueForAddressAndType( address, type );
        } // end GetValueForAddressAndType()

        public System.Management.Automation.PSObject GetValueForAddressAndType( ulong address,
                                                                                DbgNamedTypeInfo type )
        {
            return GetValueForAddressAndType( address, type, null, false, false );
        }

        public System.Management.Automation.PSObject GetValueForAddressAndType( ulong address,
                                                                                DbgNamedTypeInfo type,
                                                                                string nameForSymbol,
                                                                                bool skipConversion,
                                                                                bool skipDerivedTypeDetection )
        {
            if( null == type )
                throw new ArgumentNullException( "type" );

            var sym = CreateSymbolForAddressAndType( address, type, nameForSymbol );
            if( null == sym )
                return null;

            return sym.GetValue( skipConversion, skipDerivedTypeDetection );
        } // end GetValueForAddressAndType()

        public DbgSymbol CreateSymbolForAddressAndType( ulong address,
                                                        string typeName,
                                                        CancellationToken cancelToken )
        {
            return CreateSymbolForAddressAndType( address, typeName, cancelToken, null );
        }

        public DbgSymbol CreateSymbolForAddressAndType( ulong address,
                                                        string typeName,
                                                        CancellationToken cancelToken,
                                                        string symbolName )
        {
            DbgNamedTypeInfo type = _PickTypeInfoForName( typeName, cancelToken );
            return CreateSymbolForAddressAndType( address, type, symbolName );

        }

        public DbgSymbol CreateSymbolForAddressAndType( ulong address, DbgNamedTypeInfo type )
        {
            return CreateSymbolForAddressAndType( address, type, null );
        }

        public DbgSymbol CreateSymbolForAddressAndType( ulong address,
                                                        DbgNamedTypeInfo type,
                                                        string symbolName )
        {
            if( null == type )
                throw new ArgumentNullException( "type" );

            if( String.IsNullOrEmpty( symbolName ) )
                symbolName = Util.Sprintf( "<manual_{0}_{1}>", address, type.Name );

            var sym = new DbgSimpleSymbol( Debugger,
                                           symbolName,
                                           type,
                                           address );
            return sym;
        } // end CreateSymbolForAddressAndType()

        public void SetEffectiveProcessorType( IMAGE_FILE_MACHINE effMach )
        {
            ExecuteOnDbgEngThread( () => CheckHr( m_debugControl.SetEffectiveProcessorType( effMach ) ) );
        } // end SetEffectiveProcessorType()

        public IMAGE_FILE_MACHINE GetEffectiveProcessorType()
        {
            return ExecuteOnDbgEngThread( () =>
            {
                IMAGE_FILE_MACHINE effMach;
                CheckHr( m_debugControl.GetEffectiveProcessorType( out effMach ) );
                return effMach;
            } );
        }

        public MEMORY_BASIC_INFORMATION64 QueryVirtual( ulong address )
        {
            return ExecuteOnDbgEngThread( () =>
            {
                MEMORY_BASIC_INFORMATION64 mem;
                CheckHr( m_debugDataSpaces.QueryVirtual( address, out mem ) );
                return mem;
            } );
        }

        public int TryQueryVirtual( ulong address, out MEMORY_BASIC_INFORMATION64 memInfo )
        {
            MEMORY_BASIC_INFORMATION64 tmpMem = new MEMORY_BASIC_INFORMATION64();
            memInfo = new MEMORY_BASIC_INFORMATION64();
            int hr = ExecuteOnDbgEngThread( () =>
            {
                return m_debugDataSpaces.QueryVirtual( address, out tmpMem );
            } );

            if( 0 == hr )
                memInfo = tmpMem;

            return hr;
        }

        public static bool IsFrameInline( uint inlineFrameCtx )
        {
            if( inlineFrameCtx == 0xffffffff )
                return false;

            return ((inlineFrameCtx & 0x00000200) > 0);
        }


        /// <summary>
        ///    Incremented each time the dbgeng engine execution state changes.
        /// </summary>
        public int DbgEngCookie { get; internal set; }


        public bool SourceMode
        {
            get
            {
                return ExecuteOnDbgEngThread( () =>
                {
                    DEBUG_LEVEL level;
                    CheckHr( m_debugControl.GetCodeLevel( out level ) );
                    return DEBUG_LEVEL.SOURCE == level;
                } );
            }
            set
            {
                ExecuteOnDbgEngThread( () =>
                {
                    var level = value ? DEBUG_LEVEL.SOURCE : DEBUG_LEVEL.ASSEMBLY;
                    CheckHr( m_debugControl.SetCodeLevel( level ) );
                } );
            }
        } // end property SourceMode


        internal void DecrementThreadSuspendCount( uint dbgEngId )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    unsafe
                    {
                        uint outSize = 0;
                        uint tid = GetUModeThreadByDebuggerId( dbgEngId ).Tid;
                        int hr = m_debugAdvanced.Request( DEBUG_REQUEST.RESUME_THREAD,
                                                          (byte*) &tid,
                                                          4,
                                                          null,
                                                          0,
                                                          out outSize );
                     // Console.WriteLine( "ResumeThreads: {0}", hr );
                    }
                } );
        } // end DecrementThreadSuspendCount()


        internal void IncrementThreadSuspendCount( uint dbgEngId )
        {
            // TODO: Find an API for this.
            InvokeDbgEngCommand( Util.Sprintf( "~{0} n", dbgEngId ),
                                 suppressOutput: true );
        } // end IncrementThreadSuspendCount()


        /// <summary>
        ///    Gets the last dbgeng event. Returns null if none.
        /// </summary>
        /// <returns></returns>
        public DbgLastEventInfo GetLastEventInfo()
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    DEBUG_EVENT eventType;
                    uint systemId, processId, threadId;
                    string description;
                    DEBUG_LAST_EVENT_INFO extraInfo;

                    int hr = m_debugControl.GetLastEventInformationWide(
                        out eventType,
                        out processId,
                        out threadId,
                        out extraInfo,
                        out description );

                    CheckHr( hr );

                    hr = m_debugSystemObjects.GetEventSystem( out systemId );

                    CheckHr( hr );

                    var lei = new DbgLastEventInfo( this,
                                                    eventType,
                                                    systemId,
                                                    processId,
                                                    threadId,
                                                    description,
                                                    extraInfo );

                    if( DEBUG_EVENT.NONE == lei.EventType )
                        return null;
                    else
                        return lei;
                } );
        } // end GetLastEventInfo()


        internal void ResetDbgEngThread()
        {
            m_dbgEngThread.ResetQ();
        }


        public void EndSession( DEBUG_END flags )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugClient.EndSession( flags ) );
                } );
        }


        public string GetCurrentProcessExecutableName()
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    string name;
                    CheckHr( m_debugSystemObjects.GetCurrentProcessExecutableNameWide( out name ) );
                    return name;
                } );
        }


        /// <summary>
        ///    Used in guest mode to quiet our spew while DbgShell is dormant and we're
        ///    sharing the console with a console-based debugger (such as ntsd).
        /// </summary>
        internal IDisposable SuspendEventOutput()
        {
            m_internalEventCallbacks.SuppressOutput = true;
            return new Disposable( () => m_internalEventCallbacks.SuppressOutput = false );
        } // end SuspendEventOutput()



        /// <summary>
        ///    Given an extension DLL "name", enumerates fully-qualified paths that could
        ///    correspond to it. Does not check if the path exists.
        /// </summary>
        private IEnumerable< string > _EnumerateExtensionDllCandidates( string extDllName )
        {
            // Given any of the following inputs:
            //
            //    uiext
            //    uiext.dll
            //    C:\foo\bar\uiext
            //    C:\foo\bar\uiext.dll
            //
            // we must attempt to load:
            //
            //    C:\foo\bar\uiext.dll
            //
            // (if it exists on disk)
            //
            // See comments in _TryReallyLoadExtension for why.

            string path = extDllName;

            if( 0 != Util.Strcmp_OI( ".dll", Path.GetExtension( path ) ) )
            {
                path += ".dll";
            }

            if( Path.IsPathRooted( path ) )
            {
                yield return path;
                yield break;
            }

            foreach( string dir in GetExtensionSearchPath().Split( ';' ) )
            {
                if( 0 == dir.Length )
                    continue;

                yield return Path.Combine( dir, path );
            } // end foreach( dir in search path )
        } // end _EnumerateExtensionDllCandidates()



        private ulong _TryReallyLoadExtension( string fullyQualifiedPath,
                                               bool throwForFailure,
                                               out string loadOutput )
        {
            loadOutput = null;

            if( !Path.IsPathRooted( fullyQualifiedPath ) )
            {
                if( throwForFailure )
                {
                    throw new DbgEngException( E_INVALIDARG,
                                               Util.Sprintf( "You must pass a fully qualified path (not '{0}').",
                                                             fullyQualifiedPath ),
                                               "LoadExtensionFailed_PathNotRooted",
                                               System.Management.Automation.ErrorCategory.InvalidArgument,
                                               fullyQualifiedPath );
                }
                return 0;
            }

            if( !File.Exists( fullyQualifiedPath ) )
            {
                if( throwForFailure )
                {
                    throw new DbgEngException( E_INVALIDARG,
                                               Util.Sprintf( "Path does not exist: '{0}'.",
                                                             fullyQualifiedPath ),
                                               "LoadExtensionFailed_PathNotFound",
                                               System.Management.Automation.ErrorCategory.InvalidArgument,
                                               fullyQualifiedPath );
                }
                return 0;
            }

            ulong hExt;
            int hr = m_debugControl.AddExtensionWide( fullyQualifiedPath, 0, out hExt );
            if( hr != 0 )
            {
                if( throwForFailure )
                {
                    throw new DbgEngException( hr,
                                               Util.Sprintf( "Failed to load extension: {0}", fullyQualifiedPath ),
                                               "AddExtensionFailed",
                                               System.Management.Automation.ErrorCategory.NotSpecified,
                                               fullyQualifiedPath );
                }
                return 0;
            }

            // Unfortunately IDebugControlN.AddExtensionWide does nothing except
            // add "path" to a list--it does not check to see if it can find the
            // DLL, nor does it attempt to load anything. To know if things worked
            // or not, we'll have to trick dbgeng into actually loading it.
            //
            // TODO: If one doesn't exist (I don't think it does) add a Request
            // call to force the extension to actually be loaded. For now we'll
            // have to try this.

            StringBuilder sb = new StringBuilder( 1024 );
            using( HandleDbgEngOutput( (s) => sb.AppendLine( s ) ) )
            {
                IntPtr alsoDontCare;
                hr = m_debugControl.GetExtensionFunctionWide( hExt, "DummyDoesNotExist", out alsoDontCare );
                // Ignore hr (it should always be E_NOINTERFACE, whether the library
                // was loaded or not).
            }
            loadOutput = sb.ToString();
            if( loadOutput.StartsWith( "The call to LoadLibrary" ) )
            {
                if( throwForFailure )
                {
                    // If I were really ambitious or if I cared I could parse the error code
                    // out. Blech.
                    throw new DbgEngException( E_FAIL,
                                               sb.ToString(),
                                               "LoadExtensionFailed_Unknown",
                                               System.Management.Automation.ErrorCategory.NotSpecified,
                                               fullyQualifiedPath );
                }
                return 0;
            }
            Util.Assert( 0 != hExt );
            return hExt;
        } // end _TryReallyLoadExtension();


        public class ExtensionLoadResult
        {
            public ulong hExt;
            public string LoadOutput;
            public ExtensionLoadResult( ulong hExt_, string loadOutput )
            {
                hExt = hExt_;
                LoadOutput = loadOutput;
                Util.Assert( 0 != hExt );
            }

            public ExtensionLoadResult( ulong hExt_ )
                : this( hExt_, null )
            {
            }
        } // end class ExtensionLoadResult


        public Task< ExtensionLoadResult > AddExtensionAsync( string path )
        {
            return ExecuteOnDbgEngThreadAsync( () =>
                {
                    foreach( string candidate in _EnumerateExtensionDllCandidates( path ) )
                    {
                        if( File.Exists( candidate ) )
                        {
                            string loadOutput;
                            ulong hExt = _TryReallyLoadExtension( candidate, false, out loadOutput );
                            if( 0 != hExt )
                            {
                                return new ExtensionLoadResult( hExt, loadOutput );
                            }
                        }
                    } // end foreach( candidate )

                    throw new DbgEngException( HR_ERROR_FILE_NOT_FOUND,
                                               Util.Sprintf( "Could not find extension: {0}", path ),
                                               "AddExtensionFailed_fileNotFound",
                                               System.Management.Automation.ErrorCategory.ObjectNotFound,
                                               path );
                } );
        } // end AddExtensionAsync()


        public void RemoveExtension( string pathOrName )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    ExtensionLoadResult elr = GetExtensionDllHandle( pathOrName, false );
                    if( null == elr )
                    {
                        throw new DbgEngException( HR_ERROR_FILE_NOT_FOUND,
                                                   Util.Sprintf( "Extension not loaded: {0}", pathOrName ),
                                                   "RemoveExtensionFailed_notLoaded",
                                                   System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                   pathOrName );
                    }

                    int hr = m_debugControl.RemoveExtension( elr.hExt );
                    if( hr != 0 )
                    {
                        throw new DbgEngException( hr,
                                                   Util.Sprintf( "Could not remove extension: {0}", pathOrName ),
                                                   "RemoveExtensionFailed",
                                                   System.Management.Automation.ErrorCategory.NotSpecified,
                                                   pathOrName );
                    }
                    Util.Assert( 0 == hr );
                } );
        }

        public void CallExtension( ulong hExt, string function, string args )
        {
            Util.Await( CallExtensionAsync( hExt, function, args ) );
        }

        public Task CallExtensionAsync( ulong hExt, string function, string args )
        {
            return ExecuteOnDbgEngThreadAsync( () =>
                {
                    CheckHr( m_debugControl.CallExtensionWide( hExt, function, args ) );
                    // I think we shouldn't have to flush callbacks.
                    //CheckHr( m_debugClient.FlushCallbacks() );
                } );
        }


        /// <summary>
        ///    Gets the source file and line number for the specified address. Note that
        ///    this gets the actual source file name that is in the PDB, so unless the
        ///    app was built locally, the path may not exist.
        /// </summary>
        public DbgSourceLineInfo GetSourceLineByAddress( ulong address )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    string file;
                    uint line;
                    ulong displacement;
                    CheckHr( m_debugSymbols.GetLineByOffsetWide( address,
                                                                 out line,
                                                                 out file,
                                                                 out displacement ) );
                    return new DbgSourceLineInfo( address, file, line, displacement );
                } );
        } // end GetSourceLineByAddress()


        /// <summary>
        ///    In user mode, this seems to return "logical" processors (8 on my machine),
        ///    whereas in kernel mode it returns physical processors (4 on my machine).
        /// </summary>
        public uint GetNumberProcessors()
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    uint numProcs = 0;
                    CheckHr( m_debugControl.GetNumberProcessors( out numProcs ) );
                    return numProcs;
                } );
        }


        /// <summary>
        ///    In kernel mode, this is the EPROCESS of the current process. In user mode,
        ///    it's the PEB.
        /// </summary>
        public ulong GetImplicitProcessDataOffset()
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    ulong ipdo;
                    CheckHr( m_debugSystemObjects.GetImplicitProcessDataOffset( out ipdo ) );
                    return ipdo;
                } );
        }


        public void SetImplicitProcessDataOffset( ulong process )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugSystemObjects.SetImplicitProcessDataOffset( process ) );
                } );
        }


        /// <summary>
        ///    In kernel mode, this is the KTHREAD of the current process. In user mode,
        ///    it's the TEB.
        /// </summary>
        public ulong GetImplicitThreadDataOffset()
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    ulong itdo;
                    CheckHr( m_debugSystemObjects.GetImplicitThreadDataOffset( out itdo ) );
                    return itdo;
                } );
        }


        public void SetImplicitThreadDataOffset( ulong thread )
        {
            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugSystemObjects.SetImplicitThreadDataOffset( thread ) );
                } );
        }


        private bool m_clrMdDisabled;

        /// <summary>
        ///    Temp hack to disable using ClrMd. After setting it to $true, run
        ///    [MS.Dbg.DbgProvider]::ForceRebuildNamespace() if you have already generated
        ///    stacks and want to re-generate them.
        /// </summary>
        public bool ClrMdDisabled
        {
            get { return m_clrMdDisabled; }
            set
            {
                m_clrMdDisabled = value;
                foreach( var p in m_targets.Values )
                {
                    p.ClrMdDisabled = m_clrMdDisabled;
                }
            }
        }

        public void ControlledOutput( DEBUG_OUTCTL outputControl,
                                      DEBUG_OUTPUT mask,
                                      string str )
        {
            Util.Await( ControlledOutputAsync( outputControl, mask, str ) );
        } // end ControlledOutput()

        public Task ControlledOutputAsync( DEBUG_OUTCTL outputControl,
                                           DEBUG_OUTPUT mask,
                                           string str )
        {
            return ExecuteOnDbgEngThreadAsync( () =>
                {
                    CheckHr( m_debugControl.ControlledOutputWide( outputControl, mask, str ) );
                } );
        } // end ControlledOutput()


        /// <summary>
        ///    Gets the dbgeng extension search path. If there is no current target, this
        ///    falls back to using the _NT_DEBUGGER_EXTENSION_PATH environment variable.
        /// </summary>
        public string GetExtensionSearchPath()
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    unsafe
                    {
                        try
                        {
                            uint outSize = 0;
                            byte[] outBuf = new byte[ 4000 ];
                            fixed( byte* pOutBuf = outBuf )
                            {
                                CheckHr( m_debugAdvanced.Request( DEBUG_REQUEST.GET_EXTENSION_SEARCH_PATH_WIDE,
                                                                  null, // inBuffer: unused
                                                                  0,
                                                                  pOutBuf,
                                                                  (uint) outBuf.Length,
                                                                  out outSize ) );
                            } // end fixed
                            // outsize probably includes a trailing null--we'll want to get
                            // rid of that.
                            if( (outSize > 1) &&
                                (0 == outBuf[ outSize - 1 ]) &&
                                (0 == outBuf[ outSize - 2 ]) )
                            {
                                outSize -= 2;
                            }

                            return Encoding.Unicode.GetString( outBuf, 0, (int) outSize );
                        }
                        catch( DbgEngException dee )
                        {
                            LogManager.Trace( "Warning: failed to get extension search path: {0}",
                                              Util.GetExceptionMessages( dee ) );

                            // Not an invariant; just wondering if there are other ways
                            // this can blow up.
                            Util.Assert( NoTarget );
                        }

                        // We'll have to fall back on the environment variable. This isn't
                        // strictly correct, since I think dbgeng may adjust the path
                        // depending on attributes of the target, but it's usually better
                        // than blowing up.
                        var str = Environment.GetEnvironmentVariable( "_NT_DEBUGGER_EXTENSION_PATH" );
                        if( null == str )
                            str = String.Empty;

                        LogManager.Trace( "We'll have to fallback to using the environment variable: {0}",
                                          str );

                        return str;
                    } // end unsafe
                } );
        } // end GetExtensionSearchPath()


        /// <summary>
        ///    Returns null if the extension was not loaded or could not be found, or
        ///    throws if there was a problem loading a DLL.
        /// </summary>
        public ExtensionLoadResult GetExtensionDllHandle( string extDllName, bool loadIfNotAlready )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    ulong hExt;
                    int hr = m_debugControl.GetExtensionByPathWide( extDllName, out hExt );
                    if( 0 == hr )
                    {
                        return new ExtensionLoadResult( hExt ); // well, that was easy
                    }
                    else if( E_NOINTERFACE != hr )
                    {
                        throw new DbgEngException( hr );
                    }

                    // Now we get to look for it. Depending on what path was used to load
                    // it, may or may not recognize what we're asking for.
                    //
                    // For example, if you load an extenstion by full path, dbgeng can
                    // figure out what you meant pretty well:
                    //
                    //    .load C:\Debuggers\winext\uiext.dll
                    //    S_OK: GetExtensionByPathWide( "C:\Debuggers\winext\uiext.dll" )
                    //    S_OK: GetExtensionByPathWide( "uiext.dll" )
                    //    S_OK: GetExtensionByPathWide( "uiext" )
                    //
                    // But if you loaded with some shortened form (missing the directory
                    // and/or extension), dbgeng (GetExtensionByPathWide) will not find
                    // it. Rather than pass that pain on to users, we'll try to fix it up.

                    Exception lastException = null;

                    foreach( var candidate in _EnumerateExtensionDllCandidates( extDllName ) )
                    {
                        // Note that we do not check for the file on disk here--it could
                        // have been renamed since being loaded.
                        hr = m_debugControl.GetExtensionByPathWide( candidate, out hExt );
                        if( 0 == hr )
                        {
                            return new ExtensionLoadResult( hExt );
                        }
                        else if( E_NOINTERFACE != hr )
                        {
                            throw new DbgEngException( hr );
                        }

                        if( File.Exists( candidate ) &&
                            loadIfNotAlready )
                        {
                            // We'll keep trying other candidates if this one fails. This
                            // would enable an arch-unified extension search path (32-bit
                            // and 64-bit DLLs all on the same search path).
                            //
                            // Except that probably wouldn't be a good idea--a user who
                            // was trying to load an extension wouldn't want the noise of
                            // a failed 32-bit-DLL-into-a-64-bit-process load when the
                            // real problem is that the 64-bit DLL wasn't found.
                            try
                            {
                                return Util.Await( AddExtensionAsync( candidate ) );
                            }
                            catch( DbgProviderException dpe )
                            {
                                LogManager.Trace( "Failed to load '{0}': {1}",
                                                  candidate,
                                                  Util.GetExceptionMessages( dpe ) );
                                lastException = dpe;
                            }
                        }
                    } // end foreach( candidate )

                    if( null != lastException )
                    {
                        ExceptionDispatchInfo.Capture( lastException ).Throw();
                    }

                    Util.Assert( 0 == hExt );
                    return null;
                } );
        } // end GetExtensionDllHandle()


        public int HoldingPipelineCount
        {
            get { return m_holdingPipe.Count; }
        }


        internal string TryGetInfoFromFrame( DEBUG_STACK_FRAME_EX nativeFrame,
                                             out string modName,
                                             out string funcName,
                                             out ulong offset )
        {
            modName = null;
            funcName = null;
            offset = 0;

            string fullname = TryGetNameByInlineContext( nativeFrame.InstructionOffset,
                                                         nativeFrame.InlineFrameContext,
                                                         out offset );

            if( !String.IsNullOrEmpty( fullname ) )
            {
                ulong tmpOffset;
                DbgProvider.ParseSymbolName( fullname,
                                             out modName,
                                             out funcName,
                                             out tmpOffset );
                Util.Assert( 0 == tmpOffset ); // it's not part of fullname
            }

            return fullname;
        }


        internal IMAGE_NT_HEADERS64 ReadImageNtHeaders( DbgModuleInfo image )
        {
            return ReadImageNtHeaders( image.BaseAddress );
        }

        internal IMAGE_NT_HEADERS64 ReadImageNtHeaders( ulong imageBase )
        {
            return ExecuteOnDbgEngThread( () =>
            {
                IMAGE_NT_HEADERS64 ntHeaders;
                int hr = m_debugDataSpaces.ReadImageNtHeaders( imageBase, out ntHeaders );
                if( HR_ERROR_READ_FAULT == hr )
                {
                    throw new DbgMemoryAccessException( imageBase,
                                                        Util.Sprintf( "Could not read image headers for image at {0}. Try setting your executable image path (using \".exepath\") and reloading.",
                                                                      Util.FormatHalfOrFullQWord( imageBase ) ),
                                                        "MissingImageNtHeaders",
                                                        System.Management.Automation.ErrorCategory.ReadError );
                }
                CheckHr( hr );

                return ntHeaders;
            } );
        }


        public IEnumerable< DbgValue > EnumerateLIST_ENTRY( string headSymName,
                                                            string entryTypeName,
                                                            string listEntryMemberPath ) // optional
        {
            ulong headAddr = 0;

            try
            {
                var headSym = FindSymbol_Search( headSymName,
                                                 GlobalSymbolCategory.Data ).FirstOrDefault();

                headAddr = headSym.Address;
            }
            catch( DbgProviderException dpe )
            {
                throw new DbgProviderException( Util.Sprintf( "Could not find head symbol \"{0}\". Are you missing the PDB?",
                                                              headSymName ),
                                                "HeadSymNotFound_maybeNoPdb",
                                                System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                dpe,
                                                headSymName );
            }

            return EnumerateLIST_ENTRY( headAddr, entryTypeName, listEntryMemberPath );
        }

        public IEnumerable< DbgValue > EnumerateLIST_ENTRY( ulong headAddr,
                                                            string entryTypeName,
                                                            string listEntryMemberPath ) // optional
        {
            DbgUdtTypeInfo entryType = null;
            try
            {
                entryType = (DbgUdtTypeInfo) GetTypeInfoByName( entryTypeName ).FirstOrDefault();
            }
            catch( DbgProviderException dpe )
            {
                throw new DbgProviderException( Util.Sprintf( "Could not find type information for \"{0}\". Are you missing the PDB?",
                                                              entryTypeName ),
                                                "NoTypeInfo_maybeMissingPdb",
                                                System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                dpe,
                                                entryTypeName );
            }

            return EnumerateLIST_ENTRY( headAddr, entryType, listEntryMemberPath );
        }


        public IEnumerable< DbgValue > EnumerateLIST_ENTRY( DbgValue head,
                                                            DbgUdtTypeInfo entryType,
                                                            string listEntryMemberPath )
        {
            if( null == head )
                throw new ArgumentNullException( "head" );

            if( head is DbgPointerValue )
            {
                // You're allowed to pass a LIST_ENTRY, a LIST_ENTRY*, a LIST_ENTRY**, ...
                head = (DbgValue) (((DbgPointerValue) head).DbgFollowPointers()).BaseObject;
            }

            DbgSymbol headSym = head.DbgGetOperativeSymbol();
            string itemNamePrefix = headSym.Name + "_";

            ulong headAddr = headSym.Address;

            return EnumerateLIST_ENTRY( headAddr,
                                        entryType,
                                        listEntryMemberPath,
                                        itemNamePrefix );
        }

        public IEnumerable< DbgValue > EnumerateLIST_ENTRY( ulong headAddr,
                                                            DbgUdtTypeInfo entryType,
                                                            string listEntryMemberPath ) // optional
        {
            return EnumerateLIST_ENTRY( headAddr,
                                        entryType,
                                        listEntryMemberPath,
                                        null ); // itemNamePrefix
        }

        public IEnumerable< DbgValue > EnumerateLIST_ENTRY( ulong headAddr,
                                                            DbgUdtTypeInfo entryType,
                                                            string listEntryMemberPath,  // optional
                                                            string itemNamePrefix )      // optional
        {
            int idx = 0;

            if( String.IsNullOrEmpty( itemNamePrefix ) )
            {
                itemNamePrefix = "listEntry_";
            }

            foreach( ulong entryAddr in EnumerateLIST_ENTRY_raw( headAddr,
                                                                 entryType,
                                                                 listEntryMemberPath ) )
            {
                var pso = Debugger.GetValueForAddressAndType( entryAddr,
                                                              entryType,
                                                              "listEntry_" + idx.ToString(),
                                                              skipConversion: false,
                                                              skipDerivedTypeDetection: false );

                yield return (DbgValue) pso.BaseObject;

                idx++;
            }
        }


        public IEnumerable< ulong > EnumerateLIST_ENTRY_raw( string headSymName,
                                                             string entryTypeName,
                                                             string listEntryMemberPath ) // optional
        {
            ulong headAddr = 0;

            try
            {
                var headSym = FindSymbol_Search( headSymName,
                                                 GlobalSymbolCategory.Data ).FirstOrDefault();

                headAddr = headSym.Address;
            }
            catch( DbgProviderException dpe )
            {
                throw new DbgProviderException( Util.Sprintf( "Could not find head symbol \"{0}\". Are you missing the PDB?",
                                                              headSymName ),
                                                "HeadSymNotFound_maybeNoPdb",
                                                System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                dpe,
                                                headSymName );
            }

            return EnumerateLIST_ENTRY_raw( headAddr, entryTypeName, listEntryMemberPath );
        }



        public IEnumerable< ulong > EnumerateLIST_ENTRY_raw( ulong headAddr,
                                                             string entryTypeName,
                                                             string listEntryMemberPath ) // optional
        {
            DbgUdtTypeInfo entryType = null;
            try
            {
                entryType = (DbgUdtTypeInfo) GetTypeInfoByName( entryTypeName ).FirstOrDefault();
            }
            catch( DbgProviderException dpe )
            {
                throw new DbgProviderException( Util.Sprintf( "Could not find type information for \"{0}\". Are you missing the PDB?",
                                                              entryTypeName ),
                                                "NoTypeInfo_maybeMissingPdb",
                                                System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                dpe,
                                                entryTypeName );
            }

            return EnumerateLIST_ENTRY_raw( headAddr, entryType, listEntryMemberPath );
        }


        public IEnumerable< ulong > EnumerateLIST_ENTRY_raw( ulong headAddr,
                                                             DbgUdtTypeInfo entryType,
                                                             string listEntryMemberPath ) // optional
        {
            if( null == entryType )
                throw new ArgumentNullException( "entryType" );

            int listEntryOffset;
            if( String.IsNullOrEmpty( listEntryMemberPath ) )
            {
                // We'll try to deduce it automatically.
                var listEntryMembers = entryType.Members.Where( ( x ) => 0 == Util.Strcmp_OI( x.DataType.Name, "_LIST_ENTRY" ) ).ToArray();
                if( listEntryMembers.Length > 1 )
                {
                    // TODO: write warning
                }
                else if( 0 == listEntryMembers.Length )
                {
                    throw new ArgumentException( Util.Sprintf( "Could not find a _LIST_ENTRY member in type '{0}'.",
                                                               entryType.FullyQualifiedName ) );
                }
                listEntryOffset = (int) listEntryMembers[ 0 ].Offset;
            }
            else
            {
                const string dotFlink = ".Flink";
                if( listEntryMemberPath.EndsWith( dotFlink, StringComparison.OrdinalIgnoreCase ) )
                {
                    // TODO: write warning
                    // Trim off ".Flink".
                    listEntryMemberPath.Substring( 0, listEntryMemberPath.Length - dotFlink.Length );
                }
                listEntryOffset = (int) entryType.FindMemberOffset( listEntryMemberPath );
            }

            return EnumerateLIST_ENTRY_raw( headAddr, listEntryOffset );
        }


        public IEnumerable< ulong > EnumerateLIST_ENTRY_raw( ulong headAddr,
                                                             int listEntryOffset )
        {
            return StreamFromDbgEngThread< ulong >( CancellationToken.None, ( ct, emit ) =>
            {
                try
                {
                    ulong curListEntry = ReadMemAs_pointer( headAddr ); // reading first .Flink (or .Next, for singly-linked lists)

                    int idx = 0;

                    while( (curListEntry != headAddr) && (curListEntry != 0) )
                    {
                        // The listEntryOffset is a signed integer in case somebody wants
                        // to do something crazy like have a negative offset.
                        unchecked
                        {
                            ulong curItemAddr = (ulong) ((long) curListEntry - listEntryOffset);

                            emit( curItemAddr );
                        }

                        if( ct.IsCancellationRequested )
                            break;

                        // The .Flink (or .Next) field should be the very first thing in the list entry.
                        curListEntry = ReadMemAs_pointer( curListEntry );

                        idx++;
                    } // end while( (cur != head) && cur )
                }
                catch( DbgEngException dee )
                {
                    throw new DbgProviderException( Util.Sprintf( "LIST_ENTRY enumeration failed: {0}",
                                                                  Util.GetExceptionMessages( dee ) ),
                                                    "LIST_ENTRY_EnumerationFailed_raw",
                                                    System.Management.Automation.ErrorCategory.ReadError,
                                                    dee,
                                                    headAddr );
                }
            } );
        } // end EnumerateLIST_ENTRY_raw()


        public dynamic GetKmProcByAddr( ulong addr )
        {
            string symName = Util.Sprintf( "EPROCESS_0x{0}", addr );
            using( var ctrlC = new CancelOnCtrlC() )
            {
                try
                {
                    var sym = Debugger.CreateSymbolForAddressAndType( addr,
                                                                      "nt!_EPROCESS",
                                                                      ctrlC.CTS.Token,
                                                                      symName );

                    //return sym.Value;
                    // Workaround for WSSC VSO 2782400 ("Properties dynamically added to PSObject are not seen by C# "dynamic""):

                    return sym.Value;
                    //Console.WriteLine( "sym.Value type: {0}", Util.GetGenericTypeName( sym.Value ) );

                    //return ((dynamic) sym.Value).WrappingPSObject;
                }
                catch( DbgProviderException dpe )
                {
                    throw new DbgProviderException( "Could not create _EPROCESS symbol. Are you missing the PDB for nt?",
                                                    "NoNtSymbol",
                                                    System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                    dpe,
                                                    addr );
                }
            } // end using( ctrlC )
        } // end GetKmProcByAddr


        public IEnumerable< DbgKmProcessInfo > KmEnumerateProcesses()
        {
            // maybe this is too presumptious to intercept ctrl-c here...
            using( var ctrlC = new CancelOnCtrlC() )
            {
                return KmEnumerateProcesses( ctrlC.CTS.Token );
            } // end using( ctrlC )
        } // end KmEnumerateProcesses()


        public IEnumerable< DbgKmProcessInfo > KmEnumerateProcesses( CancellationToken cancelToken )
        {
            var target = GetCurrentKModeTarget();

            return StreamFromDbgEngThread< DbgKmProcessInfo >( cancelToken, ( ct, emit ) =>
            {
                DbgSymbol headSym = null;
                DbgUdtTypeInfo entryType = null;

                try
                {
                    headSym = FindSymbol_Search( "nt!PsActiveProcessHead",
                                                 GlobalSymbolCategory.Data,
                                                 ct ).FirstOrDefault();
                }
                catch( DbgProviderException dpe )
                {
                    throw new DbgProviderException( "Could not find nt!PsActiveProcessHead. Are you missing the PDB for nt?",
                                                    "NoNtSymbol_noPsActiveProcessHead",
                                                    System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                    dpe,
                                                    target );
                }

                try
                {
                    entryType = (DbgUdtTypeInfo) GetTypeInfoByName( "nt!_EPROCESS",
                                                                    ct ).FirstOrDefault();
                }
                catch( DbgProviderException dpe )
                {
                    throw new DbgProviderException( "Could not create EPROCESS symbol. Are you missing the PDB for nt?",
                                                    "NoNtSymbol",
                                                    System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                    dpe,
                                                    target );
                }

                foreach( var addr in EnumerateLIST_ENTRY_raw( headSym.Address,
                                                              entryType,
                                                              "ActiveProcessLinks" ) )
                {
                    emit( new DbgKmProcessInfo( this, target, addr ) );
                }
            } );
        } // end KmEnumerateProcesses()


        /// <summary>
        ///    Corresponds to the IDebugControlX GetSystemVersion method. Note that the
        ///    numbers reported here are often lies, particularly in user mode.
        ///
        ///    Notes from dbgeng.h:
        ///
        ///    "GetSystemVersion always returns the kd major/minor version numbers, which
        ///    are different than the Win32 version numbers. GetSystemVersionValues can be
        ///    used to determine the Win32 version values."
        /// </summary>
        public void GetSystemVersion( out uint platformId,
                                      out uint major,
                                      out uint minor,
                                      out string servicePackString,
                                      out uint servicePack,
                                      out string build )
        {
            uint tmpPlatformId  = platformId  = 0;
            uint tmpMajor       = major       = 0;
            uint tmpMinor       = minor       = 0;
            uint tmpServicePack = servicePack = 0;

            string tmpServicePackString = servicePackString = null;
            string tmpBuild             = build             = null;

            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugControl.GetSystemVersion( out tmpPlatformId,
                                                              out tmpMajor,
                                                              out tmpMinor,
                                                              out tmpServicePackString,
                                                              out tmpServicePack,
                                                              out tmpBuild ) );
                } );

            platformId        = tmpPlatformId;
            major             = tmpMajor;
            minor             = tmpMinor;
            servicePackString = tmpServicePackString;
            servicePack       = tmpServicePack;
            build             = tmpBuild;
        }


        /// <summary>
        ///    Corresponds to the IDebugControlX GetSystemVersionValues method. Note that
        ///    the numbers reported here are often lies, particularly in user mode.
        /// </summary>
        public void GetSystemVersionValues( out uint platformId,
                                            out uint win32Major,
                                            out uint win32Minor,
                                            out uint kdMajor,
                                            out uint kdMinor )
        {
            uint tmpPlatformId  = platformId = 0;
            uint tmpWin32Major  = win32Major = 0;
            uint tmpWin32Minor  = win32Minor = 0;
            uint tmpKdMajor     = kdMajor    = 0;
            uint tmpKdMinor     = kdMinor    = 0;

            ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugControl.GetSystemVersionValues( out tmpPlatformId,
                                                                    out tmpWin32Major,
                                                                    out tmpWin32Minor,
                                                                    out tmpKdMajor,
                                                                    out tmpKdMinor ) );
                } );

            platformId = tmpPlatformId;
            win32Major = tmpWin32Major;
            win32Minor = tmpWin32Minor;
            kdMajor    = tmpKdMajor;
            kdMinor    = tmpKdMinor;
        }


        /// <summary>
        ///    Corresponds to the IDebugControlX GetSystemVersionStringWide method.
        /// </summary>
        public string GetSystemVersionString( DEBUG_SYSVERSTR which )
        {
            return ExecuteOnDbgEngThread( () =>
                {
                    CheckHr( m_debugControl.GetSystemVersionStringWide( which, out string str ) );
                    return str;
                } );
        }
    } // end class DbgEngDebugger
}
