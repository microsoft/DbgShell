using System;
using System.Collections.Generic;
using System.Text;
using DbgEngWrapper;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    public abstract class DbgEventArgs : EventArgs, ISupportColor
    {
        internal const uint DEBUG_ANY_ID = unchecked( (uint) 0xffffffff );

        public DbgEngDebugger Debugger { get; private set; }

        public ColorString Message { get; private set; }

        public DateTimeOffset Timestamp { get; private set; }


        protected DbgEventArgs( DbgEngDebugger debugger, ColorString message )
            : this( debugger, message, DateTimeOffset.Now )
        {
        }

        protected DbgEventArgs( DbgEngDebugger debugger,
                                ColorString message,
                                DateTimeOffset timestamp )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            if( String.IsNullOrEmpty( message ) )
                throw new ArgumentException( "You must supply a message.", "message" );

            Debugger = debugger;
            Message = message;
            Timestamp = timestamp;

            // TODO: rename?
            Debugger.AddNotification( this );

            LogManager.Trace( Message );
        } // end constructor

        public bool ShouldBreak { get; set; }

        public override string ToString()
        {
            return Message;
        } // end ToString()

        public ColorString ToColorString()
        {
            return Message;
        } // end ToColorString()

        public abstract DbgEventFilter FindMatchingFilter( Dictionary<DEBUG_FILTER_EVENT, DbgEngineEventFilter> sefIndex,
                                                           Dictionary<uint, DbgExceptionEventFilter> eefIndex );
    } // end class DbgEventArgs


    public abstract class SpecificEventArgs : DbgEventArgs
    {
        public DEBUG_FILTER_EVENT FilterEvent { get; private set; }

        protected SpecificEventArgs( DbgEngDebugger debugger,
                                     ColorString message,
                                     DEBUG_FILTER_EVENT filterEvent )
            : this( debugger, message, filterEvent, DateTimeOffset.Now )
        {
        }

        protected SpecificEventArgs( DbgEngDebugger debugger,
                                     ColorString message,
                                     DEBUG_FILTER_EVENT filterEvent,
                                     DateTimeOffset timestamp )
            : base( debugger, message, timestamp )
        {
            FilterEvent = filterEvent;
        } // end constructor

        public override DbgEventFilter FindMatchingFilter( Dictionary<DEBUG_FILTER_EVENT, DbgEngineEventFilter> sefIndex,
                                                           Dictionary<uint, DbgExceptionEventFilter> eefIndex )
        {
            DbgEngineEventFilter sef;
            if( !sefIndex.TryGetValue( FilterEvent, out sef ) )
            {
                Util.Fail( Util.Sprintf( "How could we fail to find a filter matching {0}?", FilterEvent ) );
            }
            return sef;
        } // end FindMatchingFilter()
    } // end class SpecificEventArgs


    public abstract class ExceptionEventArgsBase : DbgEventArgs
    {
        public uint ExceptionCode { get; private set; }

        protected ExceptionEventArgsBase( DbgEngDebugger debugger,
                                          ColorString message,
                                          uint exceptionCode )
            : this( debugger, message, exceptionCode, DateTimeOffset.Now )
        {
        }

        protected ExceptionEventArgsBase( DbgEngDebugger debugger,
                                          ColorString message,
                                          uint exceptionCode,
                                          DateTimeOffset timestamp )
            : base( debugger, message, timestamp )
        {
            ExceptionCode = exceptionCode;
        } // end constructor

        public override DbgEventFilter FindMatchingFilter( Dictionary<DEBUG_FILTER_EVENT, DbgEngineEventFilter> sefIndex,
                                                           Dictionary<uint, DbgExceptionEventFilter> eefIndex )
        {
            DbgExceptionEventFilter eef;
            if( !eefIndex.TryGetValue( ExceptionCode, out eef ) )
            {
                eef = eefIndex[ 0 ]; // This filter matches everything else.
            }
            return eef;
        } // end FindMatchingFilter()
    } // end class ExceptionEventArgsBase


    public class BreakpointEventArgs : ExceptionEventArgsBase
    {
        public DbgBreakpointInfo Breakpoint { get; private set; }

        private static string _CreateMessage( DbgBreakpointInfo breakpoint )
        {
            if( null == breakpoint )
                throw new ArgumentNullException( "breakpoint" );

            return Util.Sprintf( "Breakpoint {0} hit:", breakpoint.Id );
        } // end _CreateMessage()


        public BreakpointEventArgs( DbgEngDebugger debugger,
                                    DbgBreakpointInfo breakpoint )
            : this( debugger, breakpoint, DateTimeOffset.Now )
        {
        }

        public BreakpointEventArgs( DbgEngDebugger debugger,
                                    DbgBreakpointInfo breakpoint,
                                    DateTimeOffset timestamp )
            : base( debugger, _CreateMessage( breakpoint ), 0x80000003, timestamp )
        {
            Breakpoint = breakpoint;
        } // end constructor
    } // end class BreakpointEventArgs


    // Example:
    //   (1780.968): Invalid handle - code c0000008 (first chance)
    //   First chance exceptions are reported before any exception handling.
    //   This exception may be expected and handled.
    //   ntdll!KiRaiseUserExceptionDispatcher+0x3a:
    public class ExceptionEventArgs : ExceptionEventArgsBase
    {
        public EXCEPTION_RECORD64 ExceptionRecord { get; private set; }
        public bool IsFirstChance { get; private set; }

        const uint STATUS_BREAKPOINT = 0x80000003;
        const uint STATUS_WAKE_SYSTEM_DEBUGGER = 0x80000007;
        const uint STATUS_ASSERTION_FAILURE = 0xC0000420;


        private static uint _TryGetProcessId( DbgEngDebugger debugger )
        {
            try
            {
                return debugger.GetProcessSystemId();
            }
            catch( DbgProviderException dpe )
            {
                LogManager.Trace( "Could not get current process system id: {0}",
                                  Util.GetExceptionMessages( dpe ) );
            }
            return 0xffffffff;
        } // end _TryGetProcessId()


        private static DbgUModeThreadInfo _TryGetCurrentThread( DbgEngDebugger debugger )
        {
            try
            {
                return debugger.GetCurrentThread();
            }
            catch( DbgProviderException dpe )
            {
                LogManager.Trace( "Could not get current thread: {0}",
                                  Util.GetExceptionMessages( dpe ) );
            }
            return null;
        } // end _TryGetCurrentThread()


        internal static ColorString _CreateMessage( DbgEngDebugger debugger,
                                                    EXCEPTION_RECORD64 exceptionRecord,
                                                    bool isFirstChance )
        {
            return _CreateMessage( debugger,
                                   exceptionRecord,
                                   isFirstChance,
                                   _TryGetProcessId( debugger ),
                                   _TryGetCurrentThread( debugger ),
                                   true );
        }

        internal static ColorString _CreateMessage( DbgEngDebugger debugger,
                                                    EXCEPTION_RECORD64 exceptionRecord,
                                                    bool isFirstChance,
                                                    uint osPid,
                                                    DbgUModeThreadInfo thread,
                                                    bool includePidTid )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            // N.B. thread could be null, and osPid could be DEBUG_ANY_ID.

            ColorString cs = new ColorString();

            if( includePidTid )
            {
                cs.Append( Util.Sprintf( "({0:x}.{1}): ",
                                         osPid,
                                         null == thread ? "xxxx" : thread.Tid.ToString( "x" ) ) );
            }

            if( isFirstChance )
            {
                if( 0x80000003 == exceptionRecord.ExceptionCode )
                    cs.AppendPushFgBg( ConsoleColor.Yellow, ConsoleColor.DarkGreen );
                else
                    cs.AppendPushFg( ConsoleColor.Yellow );
            }
            else
            {
                cs.AppendPushFg( ConsoleColor.Red );
            }

            cs.Append( _GetExceptionFriendlyName( exceptionRecord ) );

            cs.AppendPop();

            cs.Append( Util.Sprintf( " - code {0:x8} ",
                                     exceptionRecord.ExceptionCode ) );

            if( isFirstChance )
                cs.AppendPushPopFg( ConsoleColor.Yellow, "(first chance)" );
            else
                cs.AppendPushPopFg( ConsoleColor.Red, "(!!! second chance !!!)" );

            cs.AppendLine();

            if( isFirstChance )
            {
                // Show a verbose message for first-chance exceptions to try and explain to users
                // that they may not necessarily be a problem. Don't show the message for
                // hard-coded break instructions as those are common and very rarely handled.
                if( (exceptionRecord.ExceptionCode != STATUS_BREAKPOINT) &&
                    (exceptionRecord.ExceptionCode != STATUS_WAKE_SYSTEM_DEBUGGER) &&
                    (exceptionRecord.ExceptionCode != STATUS_ASSERTION_FAILURE) )
                {
                    cs.AppendLine( "First chance exceptions are reported before any exception handling." );
                    cs.AppendLine( "This exception may be expected and handled." );
                }
            }


            // Ideally we could do this:
            //
            //    cs.Append( thread.Stack.GetTopFrame().ToColorString() );
            //
            // But it's tricky to do when we are still in the process of building system
            // and process state. In particular, the stack frame wants to get the symbol
            // for the frame, and that wants to get the module, and that wants to get the
            // process, but we may not have "officially" created the process yet. That can
            // be dealt with, but since we only want a string anyway (we're not trying to
            // attach structured data about the stack frame to this event), we'll
            // compromise and just get the string.

            return debugger.ExecuteOnDbgEngThread( () =>
                {
                    string fullname;
                    ulong displacement;
                    WDebugControl dc = (WDebugControl) debugger.DebuggerInterface;
                    DEBUG_STACK_FRAME_EX[] frames;
                    int hr = dc.GetStackTraceEx( 0, 0, 0, 1, out frames );
                    if( 0 != hr )
                    {
                        LogManager.Trace( "Could not get top stack frame for exception event; error {0}. I'll try a different method.",
                                          Util.FormatErrorCode( hr ) );
                        try
                        {
                            // I doubt this will work where the other failed, but I guess I can try.
                            fullname = debugger.GetNameByInlineContext( exceptionRecord.ExceptionAddress,
                                                                        0,
                                                                        out displacement );
                        }
                        catch( DbgProviderException dpe )
                        {
                            LogManager.Trace( "Still couldn't get it: {0}", Util.GetExceptionMessages( dpe ) );
                            cs.AppendPushPopFgBg( ConsoleColor.Black,
                                                  ConsoleColor.Red,
                                                  Util.Sprintf( "Error: could not get symbol for address {0}",
                                                                Util.FormatQWord( exceptionRecord.ExceptionAddress ) ) );
                            return cs.MakeReadOnly();
                        }
                    }
                    else
                    {
                        Util.Assert( 1 == frames.Length );
                        try
                        {
                            fullname = debugger.GetNameByInlineContext( frames[ 0 ].InstructionOffset,
                                                                        frames[ 0 ].InlineFrameContext,
                                                                        out displacement );
                        }
                        catch( DbgProviderException dpe )
                        {
                            LogManager.Trace( "Could not GetNameByInlineContext for first frame: {0}", Util.GetExceptionMessages( dpe ) );
                            cs.AppendPushPopFgBg( ConsoleColor.Black,
                                                  ConsoleColor.Red,
                                                  Util.Sprintf( "Error: could not get symbol for frame instruction offset {0}",
                                                                Util.FormatQWord( frames[ 0 ].InstructionOffset ) ) );
                            return cs.MakeReadOnly();
                        }
                    }

                    string modName, funcName;
                    ulong dontCare;
                    DbgProvider.ParseSymbolName( fullname,
                                                 out modName,
                                                 out funcName,
                                                 out dontCare );

                    cs.Append( DbgProvider.ColorizeModuleName( modName ) )
                      .Append( "!" )
                      .Append( DbgProvider.ColorizeFunctionName( funcName ) );

                    if( displacement != 0 )
                        cs.Append( Util.Sprintf( "+0x{0:x}", displacement ) );

                    return cs.MakeReadOnly();
                } );
        } // end _CreateMessage()


        // I think I could also get this info from GetLastEventInformationWide. But the
        // problem with that is that I should not (and sometimes cannot) call back into
        // dbgeng while it is dispatching an event like it is here.
        //
        // Another source for part of this info could be the event filters (exception and
        // engine event filters). But we still can't call into dbgeng to get them.
        //
        // So I'm just going to have to keep my own tables of this info. :/
        private static unsafe string _GetExceptionFriendlyName( EXCEPTION_RECORD64 exceptionRecord )
        {
            switch( exceptionRecord.ExceptionCode )
            {
                case 0xc0000005: return "Access violation";
                case 0xc0000420: return "Assertion failure";
                case 0xcfffffff: return "Application hang";
                case 0x80000003: return "Break instruction exception";
                case 0xe06d7363: return "C++ EH exception";
                case 0xe0434f4d: return "CLR exception";
                case 0xe0444143: return "CLR notification exception";
                case 0x40010008: return "Control-Break exception";
                case 0x40010005: return "Control-C exception";
                case 0x80000002: return "Data misaligned";
                case 0x40010009: return "Debugger command exception";
                case 0x80000001: return "Guard page violation";
                case 0xc000001d: return "Illegal instruction";
                case 0xc0000006: return Util.Sprintf( "In-page I/O error {0:x}", exceptionRecord.ExceptionInformation[ 2 ] );
                case 0xc0000094: return "Integer divide-by-zero";
                case 0xc0000095: return "Integer overflow";
                case 0xc0000008: return "Invalid handle";
                case 0xc000001e: return "Invalid lock sequence";
                case 0xc000001c: return "Invalid system call";
                case 0xc0000037: return "Port disconnected";
                case 0xefffffff: return "Service hang";
                case 0x80000004: return "Single step exception";
                case 0xc0000409: return "Security check failure or stack buffer overrun";
                case 0xc00000fd: return "Stack overflow";
                case 0xc0000421: return "Verifier stop";
                case 0x406d1388: return "Visual C++ exception";
                case 0x80000007: return "Wake debugger";
                case 0x40080201: return "Windows Runtime Originate Error";
                case 0x40080202: return "Windows Runtime Transform Error";
                case 0x4000001f: return "WOW64 breakpoint";
                case 0x4000001e: return "WOW64 single step exception";

                default: return "Unknown exception";
            }
        } // end _GetExceptionFriendlyName()


        public ExceptionEventArgs( DbgEngDebugger debugger,
                                   EXCEPTION_RECORD64 exceptionRecord,
                                   bool isFirstChance )
            : this( debugger, exceptionRecord, isFirstChance, DateTimeOffset.Now )
        {
        }

        public ExceptionEventArgs( DbgEngDebugger debugger,
                                   EXCEPTION_RECORD64 exceptionRecord,
                                   bool isFirstChance,
                                   DateTimeOffset timestamp )
            : base( debugger,
                    _CreateMessage( debugger, exceptionRecord, isFirstChance ),
                    exceptionRecord.ExceptionCode,
                    timestamp )
        {
            ExceptionRecord = exceptionRecord;
            IsFirstChance = isFirstChance;
        } // end constructor
    } // end class ExceptionEventArgs

    public class ThreadCreatedEventArgs : SpecificEventArgs
    {
        public ulong Handle { get; private set; }
        public ulong DataOffset { get; private set; }
        public ulong StartOffset { get; private set; }
        public uint DebuggerId { get; private set; }
        public uint Tid { get; private set; }

        private static string _CreateMessage( DbgEngDebugger debugger,
                                              ulong handle,
                                              ulong dataOffset,
                                              ulong startOffset )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            // Normally you can't do much with the debugger API while the target is running, but we should be
            // able to get the thread engine Id and system Id.
            DbgUModeThreadInfo thread = debugger.GetCurrentThread();
            return Util.Sprintf( "Create thread {0}:{1:x}", thread.DebuggerId, thread.Tid );
        } // end _CreateMessage()

        public ThreadCreatedEventArgs( DbgEngDebugger debugger,
                                       ulong handle,
                                       ulong dataOffset,
                                       ulong startOffset )
            : this( debugger, handle, dataOffset, startOffset, DateTimeOffset.Now )
        {
        }

        public ThreadCreatedEventArgs( DbgEngDebugger debugger,
                                       ulong handle,
                                       ulong dataOffset,
                                       ulong startOffset,
                                       DateTimeOffset timestamp )
            : base( debugger,
                    _CreateMessage( debugger, handle, dataOffset, startOffset ),
                    DEBUG_FILTER_EVENT.CREATE_THREAD,
                    timestamp )
        {
            Handle = handle;
            DataOffset = dataOffset;
            StartOffset = startOffset;

            // Normally you can't do much with the debugger API while the target is running, but we should be
            // able to get the thread engine Id and system Id.
            DbgUModeThreadInfo thread = debugger.GetCurrentThread();
            DebuggerId = thread.DebuggerId;
            Tid = thread.Tid;
        } // end constructor
    } // end class ThreadCreatedEventArgs


    public class ThreadExitedEventArgs : SpecificEventArgs
    {
        public uint ExitCode { get; private set; }
        public uint DebuggerId { get; private set; }
        public uint Tid { get; private set; }

        private static string _CreateMessage( DbgEngDebugger debugger, uint exitCode )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            // Normally you can't do much with the debugger API while the target is running, but we should be
            // able to get the thread engine Id and system Id.
            DbgUModeThreadInfo thread = debugger.GetCurrentThread();
            return Util.Sprintf( "Exit thread {0}:{1:x}, code {2}",
                                 thread.DebuggerId,
                                 thread.Tid,
                                 Util.FormatErrorCode( exitCode ) );
        } // end _CreateMessage()

        public ThreadExitedEventArgs( DbgEngDebugger debugger, uint exitCode )
            : this( debugger, exitCode, DateTimeOffset.Now )
        {
        }

        public ThreadExitedEventArgs( DbgEngDebugger debugger,
                                      uint exitCode,
                                      DateTimeOffset timestamp )
            : base( debugger, _CreateMessage( debugger, exitCode ), DEBUG_FILTER_EVENT.EXIT_THREAD, timestamp )
        {
            ExitCode = exitCode;

            // Normally you can't do much with the debugger API while the target is running, but we should be
            // able to get the thread engine Id and system Id.
            DbgUModeThreadInfo thread = debugger.GetCurrentThread();
            DebuggerId = thread.DebuggerId;
            Tid = thread.Tid;
        } // end constructor
    } // end class ThreadExitedEventArgs


    public class ProcessCreatedEventArgs : SpecificEventArgs
    {
        public ulong ImageFileHandle { get; private set; }
        public ulong Handle { get; private set; }
        public ulong BaseOffset { get; private set; }
        public uint ModuleSize { get; private set; }
        public string ModuleName { get; private set; }
        public string ImageName { get; private set; }
        public uint CheckSum { get; private set; }
        public uint TimeDateStamp { get; private set; }
        public ulong InitialThreadHandle { get; private set; }
        public ulong ThreadDataOffset { get; private set; }
        public ulong StartOffset { get; private set; }

        private static string _CreateMessage( DbgEngDebugger debugger,
                                              ulong imageFileHandle,
                                              ulong handle,
                                              ulong baseOffset,
                                              uint moduleSize,
                                              string moduleName,
                                              string imageName,
                                              uint checkSum,
                                              uint timeDateStamp,
                                              ulong initialThreadHandle,
                                              ulong threadDataOffset,
                                              ulong startOffset )
        {
            // TODO: can we get the debugger engine Id and system pid of the new process?
            return Util.Sprintf( "Child process created: {0}", imageName );
        } // end _CreateMessage()

        public ProcessCreatedEventArgs( DbgEngDebugger debugger,
                                        ulong imageFileHandle,
                                        ulong handle,
                                        ulong baseOffset,
                                        uint moduleSize,
                                        string moduleName,
                                        string imageName,
                                        uint checkSum,
                                        uint timeDateStamp,
                                        ulong initialThreadHandle,
                                        ulong threadDataOffset,
                                        ulong startOffset )
            : this( debugger,
                    imageFileHandle,
                    handle,
                    baseOffset,
                    moduleSize,
                    moduleName,
                    imageName,
                    checkSum,
                    timeDateStamp,
                    initialThreadHandle,
                    threadDataOffset,
                    startOffset,
                    DateTimeOffset.Now )
        {
        }

        public ProcessCreatedEventArgs( DbgEngDebugger debugger,
                                        ulong imageFileHandle,
                                        ulong handle,
                                        ulong baseOffset,
                                        uint moduleSize,
                                        string moduleName,
                                        string imageName,
                                        uint checkSum,
                                        uint timeDateStamp,
                                        ulong initialThreadHandle,
                                        ulong threadDataOffset,
                                        ulong startOffset,
                                        DateTimeOffset eventTimestamp )
            : base( debugger,
                    _CreateMessage( debugger,
                                    imageFileHandle,
                                    handle,
                                    baseOffset,
                                    moduleSize,
                                    moduleName,
                                    imageName,
                                    checkSum,
                                    timeDateStamp,
                                    initialThreadHandle,
                                    threadDataOffset,
                                    startOffset ),
                    DEBUG_FILTER_EVENT.CREATE_PROCESS,
                    eventTimestamp )
        {
            ImageFileHandle = imageFileHandle;
            Handle = handle;
            BaseOffset = baseOffset;
            ModuleSize = moduleSize;
            ModuleName = moduleName;
            ImageName = imageName;
            CheckSum = checkSum;
            TimeDateStamp = timeDateStamp;
            InitialThreadHandle = initialThreadHandle;
            ThreadDataOffset = threadDataOffset;
            StartOffset = startOffset;
        } // end constructor
    } // end class ProcessCreatedEventArgs


    public class ProcessExitedEventArgs : SpecificEventArgs
    {
        public uint ExitCode { get; private set; }

        private static ColorString _CreateMessage( DbgEngDebugger debugger, uint exitCode )
        {
            // TODO: can we get the process debugger engine Id and system pid?
            return new ColorString( ConsoleColor.White,
                                    ConsoleColor.DarkRed,
                                    Util.Sprintf( "Child process exited, exit code: {0}",
                                                  Util.FormatErrorCode( exitCode ) ) );
        } // end _CreateMessage()

        public ProcessExitedEventArgs( DbgEngDebugger debugger, uint exitCode )
            : this( debugger, exitCode, DateTimeOffset.Now )
        {
        }

        public ProcessExitedEventArgs( DbgEngDebugger debugger,
                                       uint exitCode,
                                       DateTimeOffset timestamp )
            : base( debugger, _CreateMessage( debugger, exitCode ), DEBUG_FILTER_EVENT.EXIT_PROCESS, timestamp )
        {
            ExitCode = exitCode;
        } // end constructor
    } // end ProcessExitedEventArgs


    public class ModuleLoadedEventArgs : SpecificEventArgs
    {
        public ulong ImageFileHandle { get; private set; }
        public DbgModuleInfo ModuleInfo { get; private set; }

        private static ColorString _CreateMessage( DbgEngDebugger debugger,
                                                   ulong imageFileHandle,
                                                   DbgModuleInfo modInfo )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            // Example:
            //
            //    ModLoad: 000007fe`f86f0000 000007fe`f875f000   C:\Windows\SYSTEM32\MSCOREE.DLL
            //

            // Unfortunately, debugger.TargetIs32Bit cannot be trusted (we may not have
            // that info yet if we are attaching). So we'll just have to fake it for now.

            bool targetLooks32Bit = debugger.TargetIs32Bit;

            if( !targetLooks32Bit )
            {
                ulong hi = modInfo.BaseAddress & 0xffffffff00000000;
                if( (hi == 0) || (hi == 0xffffffff00000000) ) // because ReadPointersVirtual sign extends.
                {
                    targetLooks32Bit = true;
                }
            }

            var cs = new ColorString( ConsoleColor.Green, "ModLoad: " )
                            .Append( DbgProvider.FormatAddress( modInfo.BaseAddress,
                                                                targetLooks32Bit,
                                                                true ) )
                            .Append( " " )
                            .Append( DbgProvider.FormatAddress( modInfo.BaseAddress + modInfo.Size,
                                                                targetLooks32Bit,
                                                                true ) )
                            .Append( "   " );

            try
            {
                cs.Append( DbgProvider.ColorizeModuleName( modInfo.Name ) );
            }
            catch( DbgProviderException dpe )
            {
                // Sometimes we don't have the necessary context to retrieve the module
                // name. This would not be a problem if dbgeng would include the necessary
                // context as part of the events it raises.
                cs.AppendPushPopFg( ConsoleColor.Red, Util.Sprintf( "<error getting module name: {0}>", Util.GetExceptionMessages( dpe ) ) );
            }

            return cs.MakeReadOnly();
        } // end _CreateMessage()

        public ModuleLoadedEventArgs( DbgEngDebugger debugger,
                                      ulong imageFileHandle,
                                      DbgModuleInfo modInfo )
            : this( debugger, imageFileHandle, modInfo, DateTimeOffset.Now )
        {
        }

        public ModuleLoadedEventArgs( DbgEngDebugger debugger,
                                      ulong imageFileHandle,
                                      DbgModuleInfo modInfo,
                                      DateTimeOffset timestamp )
            : base( debugger,
                    _CreateMessage( debugger,
                                    imageFileHandle,
                                    modInfo ),
                    DEBUG_FILTER_EVENT.LOAD_MODULE,
                    timestamp )
        {
            ImageFileHandle = imageFileHandle;
            ModuleInfo = modInfo;
        } // end constructor
    } // end class ModuleLoadedEventArgs


    public class ModuleUnloadedEventArgs : SpecificEventArgs
    {
        public string ImageBaseName { get; private set; }
        public ulong BaseOffset { get; private set; }

        private static ColorString _CreateMessage( DbgEngDebugger debugger, string imageBaseName, ulong baseOffset )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            return new ColorString( "Unload module " )
                            .Append( DbgProvider.ColorizeModuleName( imageBaseName ) )
                            .Append( " at " )
                            .Append( DbgProvider.FormatAddress( baseOffset, debugger.TargetIs32Bit, true ) );
        } // end _CreateMessage()

        public ModuleUnloadedEventArgs( DbgEngDebugger debugger,
                                        string imageBaseName,
                                        ulong baseOffset )
            : this( debugger, imageBaseName, baseOffset, DateTimeOffset.Now )
        {
        }

        public ModuleUnloadedEventArgs( DbgEngDebugger debugger,
                                        string imageBaseName,
                                        ulong baseOffset,
                                        DateTimeOffset timestamp )
            : base( debugger,
                    _CreateMessage( debugger, imageBaseName, baseOffset ),
                    DEBUG_FILTER_EVENT.UNLOAD_MODULE,
                    timestamp )
        {
            ImageBaseName = imageBaseName;
            BaseOffset = baseOffset;
        } // end constructor
    } // end class ModuleUnloadedEventArgs


    public class SystemErrorEventArgs : SpecificEventArgs
    {
        public uint Error { get; private set; }
        public uint Level { get; private set; }

        private static string _CreateMessage( DbgEngDebugger debugger, uint error, uint level )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            return Util.Sprintf( "SYSTEM ERROR: {0}, level {1}.",
                                 Util.FormatErrorCode( error ),
                                 level );
        } // end _CreateMessage()

        public SystemErrorEventArgs( DbgEngDebugger debugger, uint error, uint level )
            : this( debugger, error, level, DateTimeOffset.Now )
        {
        }

        public SystemErrorEventArgs( DbgEngDebugger debugger,
                                     uint error,
                                     uint level,
                                     DateTimeOffset timestamp )
            : base( debugger,
                    _CreateMessage( debugger, error, level ),
                    DEBUG_FILTER_EVENT.SYSTEM_ERROR,
                    timestamp )
        {
            Error = error;
            Level = level;
        } // end constructor
    } // end class SystemErrorEventArgs


    public class SessionStatusEventArgs : DbgEventArgs
    {
        public DEBUG_SESSION Status { get; private set; }

        private static string _CreateMessage( DbgEngDebugger debugger, DEBUG_SESSION status )
        {
            return Util.Sprintf( "Session status changed: {0}", status );
        } // end _CreateMessage()

        public SessionStatusEventArgs( DbgEngDebugger debugger, DEBUG_SESSION status )
            : this( debugger, status, DateTimeOffset.Now )
        {
        }

        public SessionStatusEventArgs( DbgEngDebugger debugger,
                                       DEBUG_SESSION status,
                                       DateTimeOffset timestamp )
            : base( debugger, _CreateMessage( debugger, status ), timestamp )
        {
            Status = status;
        } // end constructor

        public override DbgEventFilter FindMatchingFilter( Dictionary<DEBUG_FILTER_EVENT, DbgEngineEventFilter> sefIndex,
                                                           Dictionary<uint, DbgExceptionEventFilter> eefIndex )
        {
            // TODO: what to do about this?
            return null;
        } // end FindMatchingFilter()
    } // end class SessionStatusEventArgs


    public class DebuggeeStateChangedEventArgs : DbgEventArgs
    {
        public DEBUG_CDS Flags { get; private set; }
        public ulong Argument { get; private set; }

        private static string _CreateMessage( DbgEngDebugger debugger, DEBUG_CDS flags, ulong argument )
        {
            if( DEBUG_CDS.ALL == flags )
            {
                return "Debuggee state changed: assume everything has changed.";
            }

            StringBuilder sb = new StringBuilder();
            if( flags.HasFlag( DEBUG_CDS.DATA ) )
            {
                DEBUG_DATA_SPACE dds = (DEBUG_DATA_SPACE) argument;
                sb.AppendFormat( "Debuggee data space has changed: {0}", dds );
            }

            if( flags.HasFlag( DEBUG_CDS.REGISTERS ) )
            {
                if( 0 != sb.Length )
                    sb.AppendLine();

                if( DEBUG_ANY_ID == argument )
                    sb.AppendFormat( "Debuggee registers have changed." );
                else
                    // TODO: I wonder if I can get the register name.
                    sb.AppendFormat( "Debuggee register {0} has changed.", argument );
            }

            if( flags.HasFlag( DEBUG_CDS.REFRESH ) )
            {
                if( 0 != sb.Length )
                    sb.AppendLine();

                sb.AppendFormat( "Debuggee state changed: should refresh: {0}",
                                 (DEBUG_CDS_REFRESH) argument );
            }

            return sb.ToString();
        } // end _CreateMessage()

        public DebuggeeStateChangedEventArgs( DbgEngDebugger debugger, DEBUG_CDS flags, ulong argument )
            : this( debugger, flags, argument, DateTimeOffset.Now )
        {
        }

        public DebuggeeStateChangedEventArgs( DbgEngDebugger debugger,
                                              DEBUG_CDS flags,
                                              ulong argument,
                                              DateTimeOffset timestamp )
            : base( debugger, _CreateMessage( debugger, flags, argument ), timestamp )
        {
            Flags = flags;
            Argument = argument;
        } // end constructor

        public override DbgEventFilter FindMatchingFilter( Dictionary<DEBUG_FILTER_EVENT, DbgEngineEventFilter> sefIndex,
                                                           Dictionary<uint, DbgExceptionEventFilter> eefIndex )
        {
            // TODO: what to do about this?
            return null;
        } // end FindMatchingFilter()
    } // end class DebuggeeStateChangedEventArgs


    public class EngineStateChangedEventArgs : DbgEventArgs
    {
        public DEBUG_CES Flags { get; private set; }
        public ulong Argument { get; private set; }

        private static string _CreateMessage( DbgEngDebugger debugger, DEBUG_CES flags, ulong argument )
        {
            if( DEBUG_CES.ALL == flags )
            {
                return "Debugger engine state changed.";
            }

            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            StringBuilder sb = new StringBuilder();
            sb.Append( "Debugger engine state changed:" );

            if( flags.HasFlag( DEBUG_CES.CURRENT_THREAD ) )
            {
                sb.AppendLine();

                uint procId = 0;
                try
                {
                    procId = debugger.GetCurrentProcessDbgEngId();
                }
                catch( DbgEngException dee )
                {
                    LogManager.Trace( "Ignoring error trying to get process ID: {0}",
                                      Util.GetExceptionMessages( dee ) );
                    // This can happen when we are abandoning a process.
                    procId = 0xffffffff;
                }
                sb.AppendFormat( "   Current thread is now id {0} (process id {1}).", argument, procId );
            }

            if( flags.HasFlag( DEBUG_CES.EFFECTIVE_PROCESSOR ) )
            {
                sb.AppendLine();
                sb.AppendFormat( "   Effective processor type is now {0}.", argument );
            }

            if( flags.HasFlag( DEBUG_CES.BREAKPOINTS ) )
            {
                sb.AppendLine();

                if( DEBUG_ANY_ID == (uint) argument )
                    sb.Append( "   Breakpoints have changed." );
                else
                    sb.AppendFormat( "   Breakpoint {0} has changed.", argument );
            }

            if( flags.HasFlag( DEBUG_CES.CODE_LEVEL ) )
            {
                sb.AppendLine();
                sb.AppendFormat( "   Code interpretation level is now {0}.", argument );
            }

            if( flags.HasFlag( DEBUG_CES.EXECUTION_STATUS ) )
            {
                sb.AppendLine();
                sb.AppendFormat( "   Execution status is now {0}.", (DEBUG_STATUS) argument );
                if( 0 != (argument & (ulong) DEBUG_STATUS_FLAGS.INSIDE_WAIT) )
                {
                    sb.Append( " (in wait)" );
                }
            }

            if( flags.HasFlag( DEBUG_CES.ENGINE_OPTIONS ) )
            {
                sb.AppendLine();
                sb.AppendFormat( "   Engine options are now {0:x}.", argument ); // TODO: :x8 or :x16?
            }

            if( flags.HasFlag( DEBUG_CES.LOG_FILE ) )
            {
                sb.AppendLine();
                // TODO: can I get the log file name?
                if( 0 == argument )
                    sb.Append( "   Log file closed." );
                else
                    sb.Append( "   Log file opened." );
            }

            if( flags.HasFlag( DEBUG_CES.RADIX ) )
            {
                sb.AppendLine();
                sb.AppendFormat( "   Default radix is now 0n{0}.", argument );
            }

            if( flags.HasFlag( DEBUG_CES.EVENT_FILTERS ) )
            {
                sb.AppendLine();
                if( DEBUG_ANY_ID == (uint) argument )
                    sb.Append( "   Event filters have changed." );
                else
                    sb.AppendFormat( "   Event filter {0} has changed.", argument );
            }

            if( flags.HasFlag( DEBUG_CES.PROCESS_OPTIONS ) )
            {
                sb.AppendLine();
                sb.AppendFormat( "   Process options are now {0:x}.", argument ); // TODO: :x8 or :x16?
            }

            if( flags.HasFlag( DEBUG_CES.EXTENSIONS ) )
            {
                sb.AppendLine();
                sb.Append( "   Extension DLLs have been loaded and/or unloaded." );
            }

            if( flags.HasFlag( DEBUG_CES.SYSTEMS ) )
            {
                sb.AppendLine();


                if( DEBUG_ANY_ID == (uint) argument )
                    sb.Append( "   A system was removed." );
                else
                    sb.AppendFormat( "   New system {0} added.", argument );
            }

            if( flags.HasFlag( DEBUG_CES.ASSEMBLY_OPTIONS ) )
            {
                sb.AppendLine();
                sb.AppendFormat( "   Assembly options are now {0:x}.", argument ); // TODO: :x8 or :x16?
            }

            if( flags.HasFlag( DEBUG_CES.EXPRESSION_SYNTAX ) )
            {
                sb.AppendLine();
                sb.AppendFormat( "   The default expression syntax is now {0}.", argument );
            }

            if( flags.HasFlag( DEBUG_CES.TEXT_REPLACEMENTS ) )
            {
                sb.AppendLine();
                sb.AppendFormat( "   TEXT_REPLACEMENTS: {0:x}", argument );
            }

            return sb.ToString();
        } // end _CreateMessage()

        public EngineStateChangedEventArgs( DbgEngDebugger debugger, DEBUG_CES flags, ulong argument )
            : this( debugger, flags, argument, DateTimeOffset.Now )
        {
        }

        public EngineStateChangedEventArgs( DbgEngDebugger debugger,
                                            DEBUG_CES flags,
                                            ulong argument,
                                            DateTimeOffset timestamp )
            : base( debugger, _CreateMessage( debugger, flags, argument ), timestamp )
        {
            Flags = flags;
            Argument = argument;
        } // end constructor

        public override DbgEventFilter FindMatchingFilter( Dictionary<DEBUG_FILTER_EVENT, DbgEngineEventFilter> sefIndex,
                                                           Dictionary<uint, DbgExceptionEventFilter> eefIndex )
        {
            // TODO: what to do about this?
            return null;
        } // end FindMatchingFilter()
    } // end class EngineStateChangedEventArgs


    public class SymbolStateChangedEventArgs : DbgEventArgs
    {
        public DEBUG_CSS Flags { get; private set; }
        public ulong Argument { get; private set; }

        private static ColorString _CreateMessage( DbgEngDebugger debugger, DEBUG_CSS flags, ulong argument )
        {
            if( DEBUG_CSS.ALL == flags )
            {
                return "Debugger symbol state changed.";
            }

            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            var cs = new ColorString( "Debugger symbol state changed:" );

            // TODO: BUGBUG: When mounting a dump file, I get this callback before I
            // have an opportunity to query the target bitness. And I can't do it
            // from here, because it throws (0x8000ffff) when mounting a dump file,
            // and it would throw if I were connected to a remote because you can't
            // call back into dbgeng at all when connected to a remote. I think the
            // dbgeng team will need to make a change to proactively notify me what
            // the target bitness is.
            bool targetIs32Bit = false;
            try
            {
                targetIs32Bit = debugger.TargetIs32Bit;
            }
            catch( DbgEngException dee )
            {
                LogManager.Trace( "BUGBUG: I don't know yet if the target is 32-bit or not: {0}",
                                  Util.GetExceptionMessages( dee ) );
            }

            if( flags.HasFlag( DEBUG_CSS.LOADS ) )
            {
                // TODO: can I get the module name?
                cs.AppendLine()
                  .Append( "   Symbols loaded for module at " )
                  .Append( DbgProvider.FormatAddress( argument, targetIs32Bit, true ) )
                  .Append( "." );
            }

            if( flags.HasFlag( DEBUG_CSS.UNLOADS ) )
            {
                cs.AppendLine();
                if( 0 == argument )
                    cs.Append( "   Unloaded symbols." );
                else
                    // TODO: can I get the module name?
                    cs.Append( "   Unloaded symbols for module at " )
                      .Append( DbgProvider.FormatAddress( argument, targetIs32Bit, true ) )
                      .Append( "." );
            }

            if( flags.HasFlag( DEBUG_CSS.SCOPE ) )
            {
                cs.AppendLine().Append( "   Symbol scope has changed." );
            }

            if( flags.HasFlag( DEBUG_CSS.PATHS ) )
            {
                cs.AppendLine().Append( "   Symbol path updated." );
            }

            if( flags.HasFlag( DEBUG_CSS.SYMBOL_OPTIONS ) )
            {
                cs.AppendLine().Append( Util.Sprintf( "   Symbol options are now: {0:x}.", argument ) );
            }

            if( flags.HasFlag( DEBUG_CSS.TYPE_OPTIONS ) )
            {
                cs.AppendLine().Append( "   Symbol type options have been updated." );
            }

            return cs;
        } // end _CreateMessage()

        public SymbolStateChangedEventArgs( DbgEngDebugger debugger, DEBUG_CSS flags, ulong argument )
            : this( debugger, flags, argument, DateTimeOffset.Now )
        {
        }

        public SymbolStateChangedEventArgs( DbgEngDebugger debugger,
                                            DEBUG_CSS flags,
                                            ulong argument,
                                            DateTimeOffset timestamp )
            : base( debugger, _CreateMessage( debugger, flags, argument ), timestamp )
        {
            Flags = flags;
            Argument = argument;
        } // end constructor

        public override DbgEventFilter FindMatchingFilter( Dictionary<DEBUG_FILTER_EVENT, DbgEngineEventFilter> sefIndex,
                                                           Dictionary<uint, DbgExceptionEventFilter> eefIndex )
        {
            // TODO: what to do about this?
            return null;
        } // end FindMatchingFilter()
    } // end class SymbolStateChangedEventArgs


    // =================================================================================
    //
    // The following event args classes do not correspond to events from
    // IDebugEventCallbacksWide, although some may look similar.
    //
    // =================================================================================


    public abstract class DbgInternalEventArgs : EventArgs
    {
        public DbgEngDebugger Debugger { get; private set; }

        protected DbgInternalEventArgs( DbgEngDebugger debugger )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            Debugger = debugger;
        } // end constructor
    } // end class DbgInternalEventArgs


    public class UmProcessRemovedEventArgs : DbgInternalEventArgs
    {
        public readonly IList< DbgUModeProcess > Removed;

        public UmProcessRemovedEventArgs( DbgEngDebugger debugger,
                                          IList< DbgUModeProcess > removed )
            : base( debugger )
        {
            if( (null == removed) || (0 == removed.Count) )
                throw new ArgumentException( "No processes removed, so why the event?" );

            Removed = removed;
        } // end constructor
    } // end class UmProcessRemovedEventArgs()


    public class UmProcessAddedEventArgs : DbgInternalEventArgs
    {
        public readonly IList< DbgUModeProcess > Added;

        public UmProcessAddedEventArgs( DbgEngDebugger debugger,
                                        IList< DbgUModeProcess > added )
            : base( debugger )
        {
            if( (null == added) || (0 == added.Count) )
                throw new ArgumentException( "No processes added, so why the event?" );

            Added = added;
        } // end constructor
    } // end class UmProcessAddedEventArgs()


    public class KmTargetRemovedEventArgs : DbgInternalEventArgs
    {
        public readonly IList< DbgKModeTarget > Removed;

        public KmTargetRemovedEventArgs( DbgEngDebugger debugger,
                                         IList< DbgKModeTarget > removed )
            : base( debugger )
        {
            if( (null == removed) || (0 == removed.Count) )
                throw new ArgumentException( "No kernel targets removed, so why the event?" );

            Removed = removed;
        } // end constructor
    } // end class KmTargetRemovedEventArgs



    public class KmTargetAddedEventArgs : DbgInternalEventArgs
    {
        public readonly IList< DbgKModeTarget > Added;

        public KmTargetAddedEventArgs( DbgEngDebugger debugger,
                                       IList< DbgKModeTarget > added )
            : base( debugger )
        {
            if( (null == added) || (0 == added.Count) )
                throw new ArgumentException( "No kernel targets added, so why the event?" );

            Added = added;
        } // end constructor
    } // end class KmTargetAddedEventArgs
}
