//
//    Copyright (C) Microsoft.  All rights reserved.
//
using System;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Globalization;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Security.Principal;
using System.Diagnostics.Tracing;

namespace MS.Dbg
{
    // For the plugin API, so that plugins can log to our log.
    public interface ILogger
    {
        void Trace( string message );
        void Trace( string messageFmt, params object[] inserts );
        void Flush();
    } // end interface ILogger

    internal class LogAdapter : ILogger, IDisposable
    {
        private string m_prefix;

        public LogAdapter( string prefix )
        {
            if( String.IsNullOrEmpty( prefix ) )
                throw new ArgumentNullException( "prefix" );

            m_prefix = prefix;
        }

        void ILogger.Trace( string message )
        {
            _CheckDisposed();
            LogManager.Trace( m_prefix + ": " + message );
        }

        void ILogger.Trace( string messageFmt, params object[] inserts )
        {
            _CheckDisposed();
            LogManager.Trace( m_prefix + ": " + Util.Sprintf( messageFmt, inserts ) );
        }

        void ILogger.Flush()
        {
            _CheckDisposed();
            LogManager.Flush();
        }

        void IDisposable.Dispose()
        {
            m_prefix = null;
        }

        private void _CheckDisposed()
        {
            if( null == m_prefix )
                throw new ObjectDisposedException( "LogAdapter" );
        }
    } // end class LogAdapter


    internal class LogManager
    {
        private static ulong sm_traceHandle;
        private static EventTraceProperties sm_etp;
        private static bool sm_useLowPriFile;
        private static string sm_logFile;


        internal static string CurrentLogFile { get { return sm_logFile; } }


        // Certain trace files have a sort of "lower priority" than normal--we don't want
        // them to cause normal trace files to get deleted during pruning.
        public static void UseLowPriorityLogFileIfNotAlreadyStarted()
        {
            // Won't have any effect if we've already selected a log file.
            sm_useLowPriFile = true;
        } // end UseAlternateLogIfNotStarted()


        private static string _GetLogDir()
        {
            string baseDir = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData,
                                                                      Environment.SpecialFolderOption.DoNotVerify ),
                                          "Temp",
                                          "DbgProvider" );
            Directory.CreateDirectory( baseDir );
            return baseDir;
        } // end _GetLogDir()


        private const string LOGFILE_NAME_PREFIX = "debugTrace";
        private const string LOWPRI_LOGFILE_NAME_PREFIX = "lowPriDebugTrace";


        private static IEnumerable< string > _ChooseLogFilePath()
        {
            string baseDir = _GetLogDir();
            _PruneLogFiles( baseDir );

            string fileNameFmt;
            if( sm_useLowPriFile )
                fileNameFmt = LOWPRI_LOGFILE_NAME_PREFIX + "_{0:x16}.etl";
            else
                fileNameFmt = LOGFILE_NAME_PREFIX + "_{0:x16}.etl";

            for( int i = 0; i < 100; i++ )
            {
                yield return Path.Combine( baseDir, Util.Sprintf( fileNameFmt, DateTime.UtcNow.ToBinary() ) );
                // Other processes could be trying to create the same file at the same
                // time. Sleep a bit to be sure we get a different timestamp.
                Thread.CurrentThread.Join( i % 5 );
            }
        } // end _ChooseLogFilePath()


        public static IEnumerable< string > EnumerateLogFiles()
        {
            return EnumerateLogFiles( false );
        }

        /// <summary>
        ///    Enumerates log files, most recently written first.
        /// </summary>
        public static IEnumerable< string > EnumerateLogFiles( bool includeLowPriFiles )
        {
            string baseDir = _GetLogDir();
            string pattern = includeLowPriFiles ? "*.etl" : LOGFILE_NAME_PREFIX + "*.etl";

            string[] logs = Directory.GetFiles( baseDir, pattern, SearchOption.TopDirectoryOnly );
            return logs.OrderByDescending( (x) =>
                {
                    return File.GetLastWriteTime( x );
                } );
        } // end EnumerateLogFiles()


        private static void _PruneLogFiles( string dir )
        {
            int defaultMaxLogBytes = RegistryUtils.GetRegValue( "MaxTraceDiskSpaceMB", 16 ) * 1024 * 1024;

            _PruneFilesNotInUse( dir,
                                 "debugTrace_*.etl",
                                 RegistryUtils.GetRegValue( "MaxLogFileBytes", defaultMaxLogBytes ) );

            _PruneFilesNotInUse( dir,
                                 "lowPriDebugTrace*.etl",
                                 RegistryUtils.GetRegValue( "MaxLowPriLogFileBytes", defaultMaxLogBytes / 6 ) );
        } // end _PruneLogFiles()


        private static void _PruneFilesNotInUse( string dir, string pattern, long cbMaxSize )
        {
            const int MIN_LOG_BYTES = 1024 * 1024;
            if( cbMaxSize < MIN_LOG_BYTES )
            {
                Util.Fail( "should not happen" ); // safe to use Util.Fail here
                cbMaxSize = MIN_LOG_BYTES;
            }

            using( ExceptionGuard disposer = new ExceptionGuard() )
            {
                DirectoryInfo di = new DirectoryInfo( dir );
                FileSystemInfo[] fsis = di.GetFileSystemInfos( pattern );
                List< FileInfo > files = new List< FileInfo >( fsis.Length );
                List< FileStream > fileLocks = new List< FileStream >( fsis.Length );
                disposer.ProtectAll( fileLocks ); // it does not make a copy of the list, so it's okay that we haven't put anything in yet.
                long totalSize = 0;
                foreach( FileSystemInfo fsi in fsis )
                {
                    FileInfo fi = fsi as FileInfo;
                    if( null != fi )
                    {
                        // We only count not-in-use files against our disk limit. If the
                        // file is not in use, this will "lock" it (making it appear
                        // in-use to anybody else) and put the resulting FileStream in the
                        // fileLocks list.
                        if( _FileNotInUse( fi, fileLocks ) )
                        {
#if DEBUG_LOG_PRUNING
                            Trace( "File \"{0}\" is NOT in use. (it will be considered for pruning)", fi.FullName );
#endif
                            fi.Refresh(); // in case _FileNotInUse finalized it.
                            totalSize += fi.Length;
                            files.Add( fi );
                        }
#if DEBUG_LOG_PRUNING
                        else
                        {
                            Trace( "File \"{0}\" is in use.", fi.FullName );
                        }
#endif
                    }
                }

                if( totalSize <= cbMaxSize )
                {
                    // Don't need to prune anything.
                    return;
                }

                // Sort by age: oldest first.
                files.Sort( (x, y) => x.CreationTime.CompareTo( y.CreationTime ) );

                long cbDeletedSoFar = 0;
                long cbNeedToDelete = totalSize - cbMaxSize;
                int idxCandidate = 0;
                while( (idxCandidate < files.Count) &&
                       (cbDeletedSoFar < cbNeedToDelete) )
                {
                    if( _TryDeleteOldFile( files[ idxCandidate ].FullName ) )
                    {
                        cbDeletedSoFar += files[ idxCandidate ].Length;
                    }
                    idxCandidate++;
                }

                if( cbDeletedSoFar < cbNeedToDelete )
                {
                    Trace( "Warning: could not delete enough log files matching \"{0}\" from \"{1}\" to get under the limit of {2}.",
                           pattern,
                           dir,
                           Util.FormatByteSize( cbMaxSize ) );
                }
            } // end using( disposer ) <-- the files will actually get deleted here when we close the FileStream handles.
        } // end _PruneFilesNotInUse()


        private static bool _FileNotInUse( FileInfo fi, List< FileStream > locks )
        {
            // If we were able to open the file for write access (without sharing write
            // access with anyone else), then no other process should be tracing to this
            // file, so it's safe to delete.
            FileStream fs = null;
            Exception e = Util.TryIO( () => fs = new FileStream( fi.FullName,
                                                                 FileMode.Open,
                                                                 FileAccess.ReadWrite,
                                                                 FileShare.Delete | FileShare.Read ) );
            if( null != fs )
            {
                // The UI tends to kill PS processes, leaving our logs unfinalized (and
                // HUGE). Let's trim them down so all those zeros don't count against our
                // disk limit.
                if( fs.Length / 1024 / 1024 >= sm_maxLogFileSizeMB )
                {
                    ColdFinalizeEtlFile( fi.FullName, fs );
                }
            }
            return null == e;
        } // end _FileNotInUse()


        private static bool _TryDeleteOldFile( string path )
        {
            Trace( "Attempting to prune old log file: {0}", path );
            return null == Util.TryIO( () => File.Delete( path ) );
        } // end _TryDeleteOldFile()


        // We don't want to trace while we are trying to set up tracing.
        private static bool sm_onInitPath;
        private static List< string > sm_preTracingTracing;


        private class MyEventSource : EventSource
        {
            private EventSourceException m_lastError;

            public MyEventSource()
                : base( "Microsoft.Dbg.DbgShellEventSource",
                        EventSourceSettings.EtwSelfDescribingEventFormat | EventSourceSettings.ThrowOnEventWriteErrors )
            {
            }

            [NonEvent]
            internal EventSourceException GetLastError() { return m_lastError; }

            [Event( 1 )]
            public bool WriteMessage( string msg )
            {
                bool retval = false;

                try
                {
                    WriteEvent( 1, msg );
                    retval = true;
                }
                catch( EventSourceException ese )
                {
#if DEBUG
                    if( System.Diagnostics.Debugger.IsAttached )
                    {
                        System.Diagnostics.Debugger.Break();
                    }
#endif
                    m_lastError = ese;
                }

                return retval;
            }

            [NonEvent]
            public bool WriteMessage( string msgFmt, params object[] inserts )
            {
                return WriteMessage( Util.Sprintf( msgFmt, inserts ) );
            }
        } // end class MyEventSource

        private static MyEventSource _InitDebugTraceEventSource()
        {
            sm_onInitPath = true;
            MyEventSource es = new MyEventSource();

            // This won't make it into the log. But in case the EventProvider
            // constructor ever decides to do lazy registration, we'll trace
            // this so that our provider GUID will be sure to be registered
            // before we call StartTrace.
            es.WriteMessage( "(starting session)" );

            Exception e = null;
            foreach( string potentialLogFile in _ChooseLogFilePath() )
            {
                e = Util.TryWin32IO( () =>
                {
                    sm_logFile = potentialLogFile;
                    _StartTracing( es );
                } );
                if( null == e )
                    break;
            }

            if( null != e )
            {
                sm_logFile = null;

                // N.B. Not using Util.Fail here, since Util.Fail traces.
                Debug.Fail( Util.Sprintf( "Could not start tracing: {0}", e ) );
                if( 0 == RegistryUtils.GetRegValue( "TolerateLoggingFailure", 0 ) )
                {
                    throw new DbgProviderException( Util.McSprintf( Resources.ErrMsgCouldNotStartLoggingFmt,
                                                                    Util.GetExceptionMessages( e ) ),
                                                    "StartTracingFailed",
                                                    ErrorCategory.OpenError,
                                                    e );
                }
            }
            else
            {
                AppDomain.CurrentDomain.UnhandledException += _UnhandledExceptionHappened;
                // The following is to attempt to workaround things taking too long during
                // finalization at process exit--the CLR gives up and stops finalizing
                // stuff after 2 seconds. If the EventProvider finalizer does not get a
                // chance to run, then it has a callback that does not get unregistered,
                // which can get called after the CLR has been torn down, causing a crash.
                AppDomain.CurrentDomain.ProcessExit += _Shutdown;

                // We also want to shutdown when our appdomain gets unloaded. This is for
                // "hosted" scenarios (the DomainUnload event does not get raised for the
                // default domain).
                AppDomain.CurrentDomain.DomainUnload += _Shutdown;
            }

            sm_onInitPath = false;
            if( null != sm_preTracingTracing )
            {
                _WriteToEventProvider( es, "(Begin trace messages generated while setting up tracing.)" );
                foreach( string preTrace in sm_preTracingTracing )
                {
                    _WriteToEventProvider( es, preTrace );
                }
                _WriteToEventProvider( es, "(End trace messages generated while setting up tracing.)" );
                sm_preTracingTracing = null;
            }
            return es;
        } // end _InitDebugTraceEventSource()


        private static void _UnhandledExceptionHappened( object sender, UnhandledExceptionEventArgs e )
        {
            string message = Util.Sprintf( "_UnhandledExceptionHappened: {0}", e.ExceptionObject );
#if DEBUG
            Console.WriteLine( message );
#endif
            try
            {
                Trace( message );
                Flush();
            }
#if DEBUG
            catch( Exception e2 )
            {
                Console.WriteLine( "Failed to trace unhandled exception: {0}", e2 );
            }
#else
            catch( Exception ) { }
#endif
        } // end _UnhandledExceptionHappened()


        private static void _Shutdown( object sender, EventArgs e )
        {
#if DEBUG
            Console.WriteLine( "LogManager::_Shutdown called, {0}.", DateTime.Now.ToString() );
#endif
            try
            {
                // We need to be as fast as possible; I don't want to even trace
                // "_Shutdown called" or anything like that; the last cmdlet
                // run should have flushed the trace session and I don't want to start
                // filling a new buffer that needs to be written out.
                lock( sm_syncRoot )
                {
                    if( !sm_shuttingDown )
                    {
                        sm_shuttingDown = true;
                        if( 0 != sm_traceHandle )
                        {
                            // For the non-hosted case (when the process is exiting), this
                            // (stopping the trace) is not strictly necessary. But it's
                            // important for the hosted case (AppDomain unloading),
                            // because we want to be able to get reloaded.
                            int err = NativeMethods.ControlTrace( sm_traceHandle,
                                                                  null,
                                                                  sm_etp,
                                                                  EventTraceControl.Stop );
                            if( (0 != err) && (ERROR_MORE_DATA != err) )
                            {
                                Debug.Fail( "This should not fail." );
                                throw new Win32Exception( err );
                            }
                        }

                        sm_eventSource.Value.Dispose();
                    }
                }
            }
#if DEBUG
            catch( Exception e2 )
            {
                Console.WriteLine( "Failed to dispose sm_eventSource: {0}", e2 );
            }
#else
            catch( Exception ) { }
#endif
        } // end _Shutdown()


        private static bool sm_shuttingDown;
        private static Lazy< MyEventSource > sm_eventSource =
            new Lazy< MyEventSource >( _InitDebugTraceEventSource, true );

        private static object sm_syncRoot = new object();

        private static readonly string[] sm_lineDelims = new string[] { Environment.NewLine, "\n" };
        private const int c_MaxLineLength = 1023;
        private const string c_TruncatedSuffix = "[...TRUNCATED]";


        // If we try to trace a line that is too big, we'll get an EventTooBig error (and
        // it will fail to get traced). Rather than suffer complete loss of trace data,
        // we'll truncate and at least get what we can.
        private static string _TruncateIfNecessary( string s )
        {
            if( s.Length < c_MaxLineLength )
                return s;
            else
                return s.Substring( 0, c_MaxLineLength - c_TruncatedSuffix.Length ) + c_TruncatedSuffix;
        } // end _TruncateIfNecessary()


        public static void Trace( string message )
        {
            lock( sm_syncRoot )
            {
                DateTime now = DateTime.Now;
                int tid = Thread.CurrentThread.ManagedThreadId;
                string prefix = Util.Sprintf( "[{0}] [{1,2}]: ", now, tid );
                foreach( string line in message.Split( sm_lineDelims, StringSplitOptions.None ) )
                {
                    string fullLine = _TruncateIfNecessary( prefix + line );
                    if( sm_onInitPath )
                    {
                        if( null == sm_preTracingTracing )
                            sm_preTracingTracing = new List< string >( 32 );

                        sm_preTracingTracing.Add( fullLine );
                    }
                    else
                    {
                        if( !sm_shuttingDown )
                            _WriteToEventProvider( sm_eventSource.Value, fullLine );
                    }
                } // end foreach( line )
            }
        } // end Trace()


        private static void _WriteToEventProvider( MyEventSource es, string fullLine )
        {
            if( !es.WriteMessage( fullLine ) )
            {
                // Before, when we were using EventProvider, we could check
                // EventProvider.GetLastWriteEventError(), and see if the error was
                // something like NoFreeBuffers, to help us decide whether or not it was
                // worth trying again. But now we don't have that, with EventSource... so
                // we'll just guess that maybe that could've been the problem, and try
                // again unconditionally.
#if DEBUG
                Console.WriteLine( "Tracing error: maybe NoFreeBuffers? Will try again..." );
#endif
                Thread.Sleep( 20 );

                if( !es.WriteMessage( fullLine ) )
                {
#if DEBUG
                     Console.WriteLine( "Tracing error: {0}", Util.GetExceptionMessages( es.GetLastError() ) );
                     Console.WriteLine( "Log message was: {0}", fullLine );
#endif
                }
            }
        } // end _WriteToEventProvider()


        public static void Trace( MulticulturalString message )
        {
            Trace( message.ToCulture( CultureInfo.InvariantCulture ) );
        }

        public static void Trace( string messageFmt, params object[] inserts )
        {
            Trace( Util.Sprintf( messageFmt, inserts ) );
        }

        public static void Trace( MulticulturalString messageFmt, params object[] inserts )
        {
            Trace( Util.McSprintf( messageFmt, inserts ) );
        }


        public static void Flush()
        {
            lock( sm_syncRoot )
            {
                if( 0 == sm_traceHandle )
                    return; // we haven't traced anything.

                if( sm_shuttingDown )
                {
                    Debug.Fail( "Flush called after _Shutdown: check stack for an assert on the shutdown path." );
                    return;
                }

                int err = NativeMethods.ControlTrace( sm_traceHandle,
                                                      null,
                                                      sm_etp,
                                                      EventTraceControl.Flush );
                if( (0 != err) && (ERROR_MORE_DATA != err) )
                {
                    // N.B. Not using Util.Fail here, since Util.Fail traces (and flushes).
                    Debug.Fail( Util.Sprintf( "This call should not fail: {0}.", err ) );
                }
            }
        }


        private static int sm_maxLogFileSizeMB;

        private static void _StartTracing( MyEventSource es )
        {
            try
            {
                // N.B. Not using Util.Assert here, since Util.Assert traces.
                Debug.Assert( !String.IsNullOrEmpty( sm_logFile ), "Must have a log file to start tracing." );
                sm_maxLogFileSizeMB = RegistryUtils.GetRegValue( "MaxTraceFileSizeMB", 8, (v) => Math.Max( 1, v ) );
                // If we are doing "low-priority" tracing, let's use a smaller file. And for
                // testing logging, we'll make it so we can wrap around a little quicker
                if( sm_useLowPriFile ||
                    !String.IsNullOrEmpty( Environment.GetEnvironmentVariable( "_DBGSHELL_TEST_MIN_TRACE_FILE_SIZE" ) ) )
                {
                    sm_maxLogFileSizeMB = 1;
                }
                int bufSizeKb = 0;
                string bufSizeStr = Environment.GetEnvironmentVariable( "_DBGSHELL_TEST_BUF_SIZE" );
                if( !String.IsNullOrEmpty( bufSizeStr ) )
                {
                    if( Int32.TryParse( bufSizeStr, out bufSizeKb ) )
                    {
                        if( bufSizeKb < 0 )
                        {
                            // N.B. Not using Util.Fail here, since Util.Fail traces.
                            Debug.Fail( "need a value >= 0" );
                            bufSizeKb = 0;
                        }
                    }
                }

                int bufsPerProc = 0;
                string bufsPerProcStr = Environment.GetEnvironmentVariable( "_DBGSHELL_TEST_BUFS_PER_PROC" );
                if( !String.IsNullOrEmpty( bufsPerProcStr ) )
                {
                    if( Int32.TryParse( bufsPerProcStr, out bufsPerProc ) )
                    {
                        if( bufsPerProc < 0 )
                        {
                            // N.B. Not using Util.Fail here, since Util.Fail traces.
                            Debug.Fail( "need a value >= 0" );
                            bufsPerProc = 0;
                        }
                    }
                }

                // BEWARE: The GUID returned from
                //    MyEventSource.GetGuid( typeof( MyEventSource ) )
                // is different from the GUID returned from
                //    es.Guid
                // !
                Guid theGuid = es.Guid;

                sm_etp = new EventTraceProperties( theGuid,
                                                   sm_logFile,
                                                   sm_maxLogFileSizeMB,
                                                   bufSizeKb,
                                                   bufsPerProc );

                int err = NativeMethods.StartTrace( out sm_traceHandle,
                                                    "Microsoft.DbgProvider.TraceSession",
                                                    sm_etp );
                if( 0 != err )
                {
                    var e = new Win32Exception( err );
                    e.Data[ "sm_logFile" ] = sm_logFile;
                    throw e;
                }

                err = NativeMethods.EnableTraceEx2( sm_traceHandle,
                                                    ref theGuid,
                                                    ControlCode.ENABLE_PROVIDER,
                                                    TraceLevel.Verbose,
                                                    0, // matchAnyKeyword
                                                    0, // matchAllKeyword
                                                    0, // timeout, zero means trace async
                                                    IntPtr.Zero );
                if( 0 != err )
                {
                    throw new Win32Exception( err );
                }

                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo( Assembly.GetExecutingAssembly().Location );
                lock( sm_syncRoot )
                if( !sm_shuttingDown )
                {
                    // Can't call Trace directly because this code is called in the code path that
                    // lazily initializes the sm_eventSource field.
                    es.WriteMessage( "==========================================================" );
                    es.WriteMessage( Util.Sprintf( "DbgProvider version {0}", fvi.FileVersion ) );
                    es.WriteMessage( Util.Sprintf( "CurrentCulture/CurrentUICulture: {0} / {1}", CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name ) );
                    es.WriteMessage( Util.Sprintf( "Process ID: {0} (0x{0:x4})", NativeMethods.GetCurrentProcessId() ) );
                    es.WriteMessage( Util.Sprintf( "Process command line: {0}", Environment.CommandLine ) );
                    es.WriteMessage( Util.Sprintf( "User type: {0}, interactive: {1}",
                                                        _GetUserType(),
                                                        Environment.UserInteractive ) );
                    es.WriteMessage( Util.Sprintf( "Current machine: {0}", Environment.MachineName ) );
                    Flush();
                }
            }
            catch( Win32Exception w32e )
            {
                MulticulturalString mcsErrMsg = Util.McSprintf( Resources.ErrMsgTraceFailureFmt,
                                                                sm_logFile,
                                                                Util.GetExceptionMessages( w32e ) );
                Exception e2 = Util.TryConvertWin32ExceptionToIOException( w32e, mcsErrMsg );
                if( null != e2 )
                    throw e2;
                else
                    throw;
            }
        } // end _StartTracing()


        private static string _GetUserType()
        {
            // We have to be careful about PII, so we don't want to log the actual user
            // name. We're really just interested if we are running as SYSTEM or a domain
            // account or something else.
            using( WindowsIdentity me = WindowsIdentity.GetCurrent() )
            {
                if( me.User.IsWellKnown( WellKnownSidType.LocalSystemSid ) )
                    return "SYSTEM";
                else if( null != me.User.AccountDomainSid )
                    return "<domain account>";
                else
                    return "<local account>";
            }
        } // end _GetUserType()


        private const int ERROR_MORE_DATA = 234;

        public static void SaveLogFile( string copyToFile, bool overwrite )
        {
            lock( sm_syncRoot )
            {
                Trace( "(saving log to \"{0}\")", copyToFile );

                if( null == sm_logFile )
                {
                    throw new DbgProviderException( Resources.ErrMsgTracingFailed,
                                                    "FailedToSaveTraceTracingDisabled",
                                                    ErrorCategory.OpenError );
                }

                Exception e = null;
                // If we stop the trace session, the tracing won't be cumulative (because
                // ETW does not support "appending" to a circular file, so when you
                // restart tracing, the old contents get wiped out). We don't want that,
                // so we're just going to copy the "live" trace file.
                //
                // Improvements in the ProcessTrace function in Win8 have made it tolerant
                // of un-finalized circular files (alexbe). But if we want to inspect such
                // trace files on down-level machines, we'll need to finalize them
                // ourselves.
                //
                // I'll preserve the old behavior, switched by a reg value, "just in
                // case".
                if( 0 == RegistryUtils.GetRegValue( "RestartTracing", 0 ) )
                {
                    Trace( "(not stopping tracing)" );
                    Flush();

                    string errorId = "FailedToSaveTrace";
                    e = Util.TryWin32IO( () =>
                    {
                        File.Copy( sm_logFile, copyToFile, overwrite );
                        errorId = "FailedToFinalizeTrace";
                        _FinalizeTraceFile( copyToFile );
                    } );

                    if( null != e )
                    {
                        ErrorCategory ec = Util.GetErrorCategoryForException( e );
                        throw new DbgProviderException( Util.McSprintf( Resources.ErrMsgCouldNotSaveTraceFmt,
                                                                        copyToFile,
                                                                        Util.GetExceptionMessages( e ) ),
                                                        errorId,
                                                        ec,
                                                        e );
                    }
                }
                else
                {
                    Trace( "========================= Stopping trace at {0} =========================", DateTime.Now );
                    Util.TryWin32IO( () =>
                    {
                        int err = NativeMethods.ControlTrace( sm_traceHandle,
                                                              null,
                                                              sm_etp,
                                                              EventTraceControl.Stop );
                        if( (0 == err) || (ERROR_MORE_DATA == err) )
                        {
                            File.Copy( sm_logFile, copyToFile, overwrite );
                            _StartTracing( sm_eventSource.Value );
                        }
                        else
                        {
                            // N.B. Not using Util.Fail here, since Util.Fail traces.
                            Debug.Fail( Util.Sprintf( "This call should not fail: {0}.", err ) );
                        }
                    } );

                    if( null != e )
                    {
                        ErrorCategory ec = Util.GetErrorCategoryForException( e );
                        throw new DbgProviderException( Util.McSprintf( Resources.ErrMsgCouldNotSaveTraceFmt,
                                                                        copyToFile,
                                                                        Util.GetExceptionMessages( e ) ),
                                                        "FailedToSaveTrace",
                                                        ec,
                                                        e );
                    }
                } // end else( we want to stop and restart the trace )
            } // end lock( sm_syncRoot )
        } // end SaveLogFile()


        // If we make a copy of the trace file before closing the tracing session, the
        // file will be "unfinalized":
        //
        //     * The EndTime field in the header will be incorrect.
        //
        //     * The BuffersWritten field in the header will be incorrect.
        //
        //     * The file size will be the full, pre-allocated amount, even though likely
        //       only a small fraction of it is used.
        //
        // When processing such a trace file on Windows 7, the file will be detected as
        // corrupt. However, improvements in the ProcessTrace function in Win8 will allow
        // these problems to be tolerated. So, when saving a copy of the trace file, we
        // could require that a Win8 machine be used to analyze the trace file... or we
        // could just fix the file header and size.
        //
        // Technically, the structure of an .etl file is not documented, although the
        // constituent structures show up in header files (WMI_BUFFER_HEADER,
        // SYSTEM_TRACE_HEADER, TRACE_LOGFILE_HEADER). So it's possible this stuff could
        // change in Win9.
        private static void _FinalizeTraceFile( string traceFile )
        {
            // N.B. We assume the lock is held (to prevent anyone from tracing while we
            // finalize the copied trace file).
            try
            {
                int err = NativeMethods.ControlTrace( sm_traceHandle,
                                                      null,
                                                      sm_etp,
                                                      EventTraceControl.Query );
                if( (0 != err) && (ERROR_MORE_DATA != err) ) // we don't need the additional data
                {
                    // N.B. Not using Util.Fail here, since Util.Fail traces.
                    Debug.Fail( Util.Sprintf( "This call should not fail: {0}.", err ) );
                    throw new Win32Exception( err );
                }

                int cbTraceBufferSize = sm_etp.BufferSize * 1024; // convert from KB to bytes
                int buffersWritten = sm_etp.BuffersWritten;
                int cbMaxFileSize = sm_etp.MaximumFileSize * 1024 * 1024; // convert from MB to bytes

                using( FileStream fileStream = Util.CreateFile( traceFile,
                                                                FileMode.Open,
                                                                FileAccess.ReadWrite,
                                                                FileShare.Read ) )
                {
                    if( fileStream.Length < 1024 )
                    {
                        // Of course this is not a true program invariant (we don't
                        // control the file), but in practice it should never happen.
                        // N.B. Not using Util.Fail here, since Util.Fail traces.
                        Debug.Fail( "should not happen" );
                        // If the file is corrupt, we'll just leave it as-is. Maybe
                        // something can be salvaged.
                        Trace( "Error: trace file \"{0}\" is impossibly short ({1} bytes).", traceFile, fileStream.Length );
                        return;
                    }

                    // I could define all the relevant structures that occur at the
                    // beginning of an .etl file (WMI_BUFFER_HEADER, SYSTEM_TRACE_HEADER,
                    // and TRACE_LOGFILE_HEADER), but I only need to poke two values and
                    // set the file size, so I'm just going to use offsets.

                    // Verify that the buffer size matches.
                    fileStream.Seek( NativeMethods.BytesBeforeTraceLogfileHeader, SeekOrigin.Begin );
                    // We do not dispose of reader because it would also dispose of the
                    // underlying stream, and we're not done with it yet.
                    BinaryReader reader = new BinaryReader( fileStream );
                    int fileBufSize = reader.ReadInt32();
                    if( fileBufSize != cbTraceBufferSize )
                    {
                        // Of course this is not a true program invariant (we don't
                        // control the file), but in practice it should never happen.
                        // N.B. Not using Util.Fail here, since Util.Fail traces.
                        Debug.Fail( "should not happen" );
                        // If the file is corrupt, we'll just leave it as-is. Maybe
                        // something can be salvaged.
                        Trace( "Error: trace file \"{0}\" buffer size does not match ControlTrace output ({1} vs {2}).", traceFile, fileBufSize, cbTraceBufferSize );
                        return;
                    }

                    fileStream.Seek( NativeMethods.BytesBeforeTraceLogfileHeader +
                                        NativeMethods.OffsetOfTraceLogFileHeaderEndTime,
                                     SeekOrigin.Begin );
                    // We do not dispose of writer because it would also dispose of the
                    // underlying stream, and we're not done with it yet.
                    BinaryWriter writer = new BinaryWriter( fileStream );

                    // Not sure why they don't use UTC.
                    writer.Write( DateTime.Now.ToFileTime() );

                    fileStream.Seek( NativeMethods.BytesBeforeTraceLogfileHeader +
                                        NativeMethods.OffsetOfTraceLogFileHeaderBuffersWritten,
                                     SeekOrigin.Begin );
                    writer.Write( buffersWritten );

                    fileStream.SetLength( Math.Min( buffersWritten * cbTraceBufferSize, cbMaxFileSize ) );
                } // end using( fileStream )
            }
            catch( Win32Exception w32e )
            {
                MulticulturalString mcsErrMsg = Util.McSprintf( Resources.ErrMsgTraceFailureFmt,
                                                                traceFile,
                                                                Util.GetExceptionMessages( w32e ) );
                Exception e2 = Util.TryConvertWin32ExceptionToIOException( w32e, mcsErrMsg );
                if( null != e2 )
                    throw e2;
                else
                    throw;
            }
        } // end _FinalizeTraceFile()


        // This is used for logging tests, so that we don't have to start a new process
        // every time we want to generate a fresh log.
        public static void RestartTraceSessionForTesting()
        {
            lock( sm_syncRoot )
            {
                Exception e = null;
                if( 0 == sm_traceHandle )
                {
                    // We haven't started yet, so nothing to stop.
                    return;
                }

                e = Util.TryWin32IO( () =>
                {
                    int err = NativeMethods.ControlTrace( sm_traceHandle,
                                                          null,
                                                          sm_etp,
                                                          EventTraceControl.Stop );
                    if( (0 == err) || (ERROR_MORE_DATA == err) )
                    {
                        _StartTracing( sm_eventSource.Value );
                    }
                    else
                    {
                        // N.B. Not using Util.Fail here, since Util.Fail traces.
                        Debug.Fail( Util.Sprintf( "This call should not fail: {0}.", err ) );
                    }
                } );

                if( null != e )
                {
                    ErrorCategory ec = Util.GetErrorCategoryForException( e );
                    throw new DbgProviderException( Util.McSprintf( "Could not stop trace session: {0}",
                                                                    Util.GetExceptionMessages( e ) ),
                                                    "FailedToStopTraceSession",
                                                    ec,
                                                    e );
                }
            } // end lock( sm_syncRoot )
        } // end RestartTraceSessionForTesting()


        // This performs a "cold" or "blind" finalize of an ETL file: we don't have a
        // handle to the ETW trace session, so we can't query the ETW API for the expected
        // trace buffer size or the number of buffers written. So we determine that
        // information from just looking at the file itself.
        //
        // The fileName parameter is just used for logging.
        public static void ColdFinalizeEtlFile( string fileName, Stream etlStream )
        {
            if( etlStream.Length < 1024 )
            {
                // Of course this is not a true program invariant (we don't control
                // the file), but in practice it should never happen.
                Util.Assert( 0 == etlStream.Length, "If etlStream.Length is less than 1k, it should be zero." );
                // If the file is corrupt, we'll just leave it as-is. Maybe
                // something can be salvaged.
                LogManager.Trace( "Error: trace file \"{0}\" is impossibly short ({1} bytes).", fileName, etlStream.Length );
                return;
            }

            // Verify that the buffer size matches.
            etlStream.Seek( NativeMethods.BytesBeforeTraceLogfileHeader, SeekOrigin.Begin );
            // We do not dispose of reader because it would also dispose of the
            // underlying stream, and we're not done with it yet.
            BinaryReader reader = new BinaryReader( etlStream );
            int traceBufSize = reader.ReadInt32();
            if( 0 != (traceBufSize % 1024) )
            {
                // The trace file buffer block size must be a whole number of
                // kilobytes. Not truly a program invariant since we don't control
                // the file, but in practice it should always be the case.
                Util.Fail( "shouldn't happen" ); // Util.Fail safe to use on this path
                // If the file is corrupt, we'll just leave it as-is. Maybe something
                // can be salvaged.
                LogManager.Trace( "Error: trace file \"{0}\" buffer block size is not a whole number of kilobytes ({1} bytes).", fileName, traceBufSize );
                return;
            }

            etlStream.Seek( NativeMethods.BytesBeforeTraceLogfileHeader +
                                NativeMethods.OffsetOfTraceLogFileHeaderEndTime,
                             SeekOrigin.Begin );

            // We construct a new reader because the last one may have done some
            // buffering.
            reader = new BinaryReader( etlStream );
            long fileEndTime = reader.ReadInt64();
            if( 0 != fileEndTime )
            {
                // File has already been finalized.
#if DEBUG_LOG_PRUNING
                LogManager.Trace( "Trace file \"{0}\" has already been finalized.", fileName );
#endif
                return;
            }

            etlStream.Seek( NativeMethods.BytesBeforeTraceLogfileHeader +
                                NativeMethods.OffsetOfTraceLogFileHeaderEndTime,
                             SeekOrigin.Begin );

            BinaryWriter writer = new BinaryWriter( etlStream );

            // Not sure why they don't use UTC.
            writer.Write( DateTime.Now.ToFileTime() );

            // Find maximum file size:
            etlStream.Seek( NativeMethods.BytesBeforeTraceLogfileHeader +
                                NativeMethods.OffsetOfTraceLogFileHeaderMaximumFileSize,
                             SeekOrigin.Begin );
            int cbMaxFileSize = reader.ReadInt32();
            cbMaxFileSize = cbMaxFileSize * 1024 * 1024; // convert from MB to bytes

            // Need to figure out how many buffers are actually valid.

            long totalLengthTmp = etlStream.Length;
            if( totalLengthTmp > Int32.MaxValue )
            {
                throw new NotSupportedException( "We don't support trace files > 2 GB." );
            }
            int totalLength = (int) totalLengthTmp;

            if( 0 != (totalLength % traceBufSize) )
            {
                // The trace file must be a whole number of traceBufSize-sized blocks.
                // Not truly a program invariant since we don't control the file, but
                // in practice it should not happen.
                Util.Fail( "should not happen" ); // Util.Fail safe to use on this path
                // If the file is corrupt, we'll just leave it as-is. Maybe something
                // can be salvaged.
                LogManager.Trace( "Error: trace file \"{0}\" size is not a multiple of the buffer block size ({1} bytes).", fileName, etlStream.Length );
                return;
            }

            long biggestTimestamp = 0;
            int totalBlocks = totalLength / traceBufSize;
            int idxOfBiggestTimestamp = 0;
            int idx;
            // We start at idx 1 because the first block is for the file header.
            for( idx = 1; idx < totalBlocks; idx++ )
            {
                long timestamp;
                bool inUse;
                bool corrupt;
                _InspectBlock( etlStream, idx, traceBufSize, out inUse, out timestamp, out corrupt );
                if( !inUse )
                    break; // if this block is all zero, then the rest of the file is all zero.

                if( corrupt )
                {
                    // If the file is corrupt, we'll just leave it as-is. Maybe
                    // something can be salvaged.
                    LogManager.Trace( "Error: trace file \"{0}\" has a bad block (idx {1}).", fileName, idx );
                    return;
                }

                if( timestamp > biggestTimestamp )
                    idxOfBiggestTimestamp = idx;
            }

            int buffersWritten = idx;
            if( idx == totalBlocks )
            {
                // In this case, every block is used. Because we use a circular file,
                // that means the beginning/end are probably not at the beginning and
                // end of the file. We may have wrapped multiple times, but pretending
                // we only wrapped once will be sufficient.
                idx += idxOfBiggestTimestamp;
            }

            etlStream.Seek( NativeMethods.BytesBeforeTraceLogfileHeader +
                                NativeMethods.OffsetOfTraceLogFileHeaderBuffersWritten,
                             SeekOrigin.Begin );
            writer.Write( buffersWritten );

            // We are probably only using a small fraction of the total file; we can
            // truncate all the zeros.
            etlStream.SetLength( Math.Min( buffersWritten * traceBufSize, cbMaxFileSize ) );
        } // end ColdFinalizeEtlFile()


        // This both verifies that a block appears valid, determines if it's in use or
        // not, and if so, what is the timestamp on it.
        private static void _InspectBlock( Stream etlStream,
                                           int blockIdx,
                                           int traceBufSize,
                                           out bool inUse,
                                           out long timestamp,
                                           out bool corrupt )
        {
            inUse = false;
            timestamp = 0;
            corrupt = false;
            etlStream.Seek( blockIdx * traceBufSize, SeekOrigin.Begin );
            BinaryReader reader = new BinaryReader( etlStream );
            int statedBufSize = reader.ReadInt32();
            if( 0 == statedBufSize )
            {
                if( _BlockIsAllZero( reader, traceBufSize - 4 ) ) // - 4 because we already read 4 bytes.
                {
                    return; // block is not in use (empty)
                }
            }
            if( statedBufSize != traceBufSize )
            {
                // Of course this is not a true program invariant (we don't control the
                // file), but in practice it should never happen.
                Util.Fail( "should not happen" ); // Util.Fail safe to use on this path
                // The block is invalid; has the wrong buffer size.
                corrupt = true;
                return;
            }
            inUse = true;
            // Skip the next twelve bytes to arrive at the timestamp.
            reader.ReadInt32();
            reader.ReadInt64();
            timestamp = reader.ReadInt64();
        } // end _InspectBlock()


        private static bool _BlockIsAllZero( BinaryReader reader, int cbBytesToCheck )
        {
            // There's probably a more efficient way to do this... oh well.
            byte[] bytes = reader.ReadBytes( cbBytesToCheck );
            for( int i = 0; i < bytes.Length; i++ )
            {
                if( 0 != bytes[ i ] )
                    return false;
            }
            return true;
        } // end _BlockIsAllZero()
    } // end class LogManager
}

