//-----------------------------------------------------------------------
// <copyright file="EtwDefinitions.cs" company="Microsoft">
//    Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MS.Dbg
{
    internal static partial class NativeMethods
    {
        // I could define all the relevant structures that occur at the beginning of an
        // .etl file (WMI_BUFFER_HEADER, SYSTEM_TRACE_HEADER, and TRACE_LOGFILE_HEADER),
        // but I only need to poke two values and set the file size, so I'm just going to
        // use offsets.

        // 0x68: size of the WMI_BUFFER_HEADER and SYSTEM_TRACE_HEADER.
        public const int BytesBeforeTraceLogfileHeader = 0x68;

        public const int OffsetOfTraceLogFileHeaderEndTime = 16;

        public const int OffsetOfTraceLogFileHeaderMaximumFileSize = 28;

        public const int OffsetOfTraceLogFileHeaderBuffersWritten = 36;


        [DllImport( "advapi32.dll", CharSet = CharSet.Unicode )]
        public static extern int StartTrace( out ulong traceHandle,
                                             string instanceName,  // case-sensitive, must be unique
                                             [In] [Out] EventTraceProperties properties );

        [DllImport( "advapi32.dll", CharSet = CharSet.Unicode )]
        public static extern int ControlTrace( ulong traceHandle,
                                               string instanceName,
                                               [In] [Out] EventTraceProperties properties,
                                               EventTraceControl controlCode );

        [DllImport( "advapi32.dll", CharSet = CharSet.Unicode )]
        public static extern int EnableTraceEx2( ulong traceHandle,
                                                 ref Guid providerId,
                                                 ControlCode controlCode,
                                                 TraceLevel level,
                                                 long matchAnyKeyword,
                                                 long matchAllKeyword,
                                                 uint timeout,
                                                 IntPtr useIntPtrZero );
    } // end class NativeMethods


    enum ControlCode : int
    {
        DISABLE_PROVIDER = 0,
        ENABLE_PROVIDER  = 1,
        CAPTURE_STATE    = 2,
    }

    enum TraceLevel : byte
    {
        None        = 0,  // Tracing is not on
        Critical    = 1,  // Abnormal exit or termination
        Fatal       = 1,  // Deprecated name for Abnormal exit or termination
        Error       = 2,  // Severe errors that need logging
        Warning     = 3,  // Warnings such as allocation failure
        Information = 4,  // Includes non-error cases(e.g.,Entry-Exit)
        Verbose     = 5,  // Detailed traces from intermediate steps
    }


    [Flags]
    enum WNodeFlags
    {
        ALL_DATA =        0x00000001, // set for WNODE_ALL_DATA
        SINGLE_INSTANCE = 0x00000002, // set for WNODE_SINGLE_INSTANCE
        SINGLE_ITEM =     0x00000004, // set for WNODE_SINGLE_ITEM
        EVENT_ITEM =      0x00000008, // set for WNODE_EVENT_ITEM

         // Set if data block size is identical for all instances (used with  WNODE_ALL_DATA
         // only)
        FIXED_INSTANCE_SIZE = 0x00000010,

        TOO_SMALL =           0x00000020, // set for WNODE_TOO_SMALL

        // Set when a data provider returns a WNODE_ALL_DATA in which the number of instances
        // and their names returned are identical to those returned from the previous
        // WNODE_ALL_DATA query. Only data blocks registered with dynamic instance names
        // should use this flag.
        INSTANCES_SAME =  0x00000040,

        // Instance names are not specified in WNODE_ALL_DATA; values specified at
        // registration are used instead. Always set for guids registered with static instance
        // names
        STATIC_INSTANCE_NAMES = 0x00000080,

        INTERNAL =      0x00000100,  // Used internally by WMI

        // timestamp should not be modified by a historical logger
        USE_TIMESTAMP = 0x00000200,

        PERSIST_EVENT = 0x00000400,

        EVENT_REFERENCE = 0x00002000,

        // Set if Instance names are ansi. Only set when returning from
        // WMIQuerySingleInstanceA and WMIQueryAllDataA
        ANSI_INSTANCENAMES = 0x00004000,

        // Set if WNODE is a method call
        METHOD_ITEM =     0x00008000,

        // Set if instance names originated from a PDO
        PDO_INSTANCE_NAMES =  0x00010000,

        // The second byte, except the first bit is used exclusively for tracing
        TRACED_GUID =   0x00020000, // denotes a trace

        LOG_WNODE =     0x00040000, // request to log Wnode

        USE_GUID_PTR =  0x00080000, // Guid is actually a pointer

        USE_MOF_PTR =   0x00100000, // MOF data are dereferenced

        NO_HEADER =     0x00200000, // Trace without header

        SEND_DATA_BLOCK =  0x00400000, // Data Block delivery

        // Set for events that are WNODE_EVENT_REFERENCE Mask for event severity level. Level
        // 0xff is the most severe type of event
        SEVERITY_MASK = unchecked( (int) 0xff000000 )
    } // end enum WNodeFlags


    enum ClientContext : int
    {
        QueryPerformanceCounter = 1,
        SystemTime = 2,
        CpuCycleCounter = 3
    }


    [Flags]
    enum LogFileMode : int
    {
        FILE_MODE_NONE        = 0x00000000, // Logfile is off
        FILE_MODE_SEQUENTIAL  = 0x00000001, // Log sequentially
        FILE_MODE_CIRCULAR    = 0x00000002, // Log in circular manner
        FILE_MODE_APPEND      = 0x00000004, // Append sequential log

        REAL_TIME_MODE        = 0x00000100, // Real time mode on
        DELAY_OPEN_FILE_MODE  = 0x00000200, // Delay opening file
        BUFFERING_MODE        = 0x00000400, // Buffering mode only
        PRIVATE_LOGGER_MODE   = 0x00000800, // Process Private Logger
        ADD_HEADER_MODE       = 0x00001000, // Add a logfile header

        USE_GLOBAL_SEQUENCE   = 0x00004000, // Use global sequence no.
        USE_LOCAL_SEQUENCE    = 0x00008000, // Use local sequence no.

        RELOG_MODE            = 0x00010000, // Relogger

        USE_PAGED_MEMORY      = 0x01000000, // Use pageable buffers

        //
        // Logger Mode flags on XP and above
        //
        FILE_MODE_NEWFILE     = 0x00000008, // Auto-switch log file
        FILE_MODE_PREALLOCATE = 0x00000020, // Pre-allocate mode

        //
        // Logger Mode flags on Vista and above
        //
        NONSTOPPABLE_MODE     = 0x00000040, // Session cannot be stopped (Autologger only)
        SECURE_MODE           = 0x00000080, // Secure session
        USE_KBYTES_FOR_SIZE   = 0x00002000, // Use KBytes as file size unit
        PRIVATE_IN_PROC       = 0x00020000, // In process private logger
        MODE_RESERVED         = 0x00100000, // Reserved bit, used to signal Heap/Critsec tracing

        //
        // Logger Mode flags on Win7 and above
        //
        NO_PER_PROCESSOR_BUFFERING   = 0x10000000, // Use this for low frequency sessions.


        //
        // Logger Mode flags on Win8 and above
        //
        SYSTEM_LOGGER_MODE          = 0x02000000, // Receive events from SystemTraceProvider
        ADDTO_TRIAGE_DUMP           = unchecked( (int) 0x80000000 ), // Add ETW buffers to triage dumps
        STOP_ON_HYBRID_SHUTDOWN     = 0x00400000, // Stop on hybrid shutdown
        PERSIST_ON_HYBRID_SHUTDOWN  = 0x00800000, // Persist on hybrid shutdown

        //
        // Logger Mode flags on Blue and above
        //
        INDEPENDENT_SESSION_MODE    = 0x08000000, // Independent logger session

        //
        // Logger Mode flags on Redstone and above
        //
        COMPRESSED_MODE             = 0x04000000, // Compressed logger session.
    } // end enum LogFileMode


    [StructLayout( LayoutKind.Sequential )]
    struct WNodeHeader
    {
        public int BufferSize;
        public int ProviderId;
        public long HistoricalContext;
        public long CountLost_KernelHandle_TimeStamp_Union;
        public Guid Guid;
        public ClientContext ClientContext;
        public WNodeFlags Flags;
    } // end struct WNodeHeader


    [StructLayout( LayoutKind.Sequential )]
    unsafe class EventTraceProperties
    {
        public WNodeHeader WNode;

        // data provided by caller
        public int BufferSize;                   // buffer size for logging (kbytes)
        public int MinimumBuffers;               // minimum to preallocate
        public int MaximumBuffers;               // maximum buffers allowed
        public int MaximumFileSize;              // maximum logfile size (in MBytes)
        public LogFileMode LogFileMode;          // sequential, circular, etc.
        public int FlushTimer;                   // buffer flush timer, in seconds
        public int EnableFlags;                  // trace enable flags
        public int AgeLimit;                     // unused

        // data returned to caller
        public int NumberOfBuffers;              // no of buffers in use
        public int FreeBuffers;                  // no of buffers free
        public int EventsLost;                   // event records lost
        public int BuffersWritten;               // no of buffers written to file
        public int LogBuffersLost;               // no of logfile write failures
        public int RealTimeBuffersLost;          // no of rt delivery failures
        public IntPtr LoggerThreadId;            // thread id of Logger
        public int LogFileNameOffset;            // Offset to LogFileName
        public int LoggerNameOffset;             // Offset to LoggerName

        public int Padding; // Or should this be IntPtr? All I know so far is that on x86, EtwpValidateTraceProperties thinks the size of EVENT_TRACE_PROPERTIES is 0x78, not 0x74, even though the actual type from advapi32 in the debugger is clearly only 0x74 bytes. Or should I use the StructLayout.Pack property?

        public const int FixedBufferSize = 522;
        [MarshalAs( UnmanagedType.ByValArray, SizeConst = FixedBufferSize, ArraySubType = UnmanagedType.U2 )]
        public char[] Buf = new char[ FixedBufferSize ];

        public EventTraceProperties( Guid guid, string logfile, int maxFileSizeInMb )
            : this( guid, logfile, maxFileSizeInMb, 0, 0 )
        {
        }

        public EventTraceProperties( Guid guid, string logfile, int maxFileSizeInMb, int bufSizeInKb, int minBufsPerProc )
        {
            if( logfile.Length >= (FixedBufferSize / 2) ) // need to leave half the room for the LoggerNameOffset
            {
                throw new ArgumentOutOfRangeException( "logfile" );
            }

            WNode.BufferSize = Marshal.SizeOf( typeof( EventTraceProperties ) );
            WNode.Guid = guid;
            WNode.ClientContext = ClientContext.SystemTime;
            WNode.Flags = WNodeFlags.TRACED_GUID;

            LogFileMode = LogFileMode.PRIVATE_IN_PROC |
                          LogFileMode.PRIVATE_LOGGER_MODE |
                          LogFileMode.NO_PER_PROCESSOR_BUFFERING |
                          LogFileMode.FILE_MODE_PREALLOCATE |
                          LogFileMode.FILE_MODE_CIRCULAR;

            MaximumFileSize = maxFileSizeInMb;
            BufferSize = bufSizeInKb;
            MinimumBuffers = minBufsPerProc * Environment.ProcessorCount;

            // WARNING: Marshal.SizeOf( typeof( EventTraceProperties ) ) returns the same
            // answer on x86 and amd64... despite the IntPtr LoggerThreadId field!
            // Fortunately, Marshal.OffsetOf works correctly (and the actual marshaling
            // works correctly). So use OffsetOf, not SizeOf, when doing offset
            // calculations.

            // N.B. The LoggerNameOffset must come before the LogFileNameOffset in memory.
            // I have no idea why, but that's what MSDN says, and it won't work right if
            // it isn't that way.
            LoggerNameOffset = (int) Marshal.OffsetOf( typeof( EventTraceProperties ), "Buf" );
            LogFileNameOffset = LoggerNameOffset + Buf.Length; // remember, Buf.Length is is chars, not bytes
            logfile.CopyTo( 0, Buf, Buf.Length / 2, logfile.Length );
        } // end constructor
    } // end class EventTraceProperties


    enum EventTraceControl : int
    {
        Query         = 0,
        Stop          = 1,
        Update        = 2,
        Flush         = 3       // Flushes all the buffers (XP+)
    }
}

