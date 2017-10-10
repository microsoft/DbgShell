using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    public class DbgExceptionEventFilter : DbgEventFilter // TODO: IEquatable?
    {
        public uint ExceptionCode { get; private set; }

        public string SecondCommand { get; private set; }

        private const uint STATUS_ACCESS_VIOLATION       = 0xC0000005;
        private const uint STATUS_ASSERTION_FAILURE      = 0xC0000420;
        private const uint STATUS_APPLICATION_HANG       = 0xcfffffff;
        private const uint STATUS_BREAKPOINT             = 0x80000003;
        private const uint STATUS_CPP_EH_EXCEPTION       = 0xE06D7363;
        private const uint STATUS_CLR_EXCEPTION          = 0xe0434f4d;
        private const uint CLRDATA_NOTIFY_EXCEPTION      = 0xe0444143;
        private const uint DBG_CONTROL_BREAK             = 0x40010008;
        private const uint DBG_CONTROL_C                 = 0x40010005;
        private const uint STATUS_DATATYPE_MISALIGNMENT  = 0x80000002;
        private const uint DBG_COMMAND_EXCEPTION         = 0x40010009;
        private const uint STATUS_GUARD_PAGE_VIOLATION   = 0x80000001;
        private const uint STATUS_ILLEGAL_INSTRUCTION    = 0xC000001D;
        private const uint STATUS_IN_PAGE_ERROR          = 0xC0000006;
        private const uint STATUS_INTEGER_DIVIDE_BY_ZERO = 0xC0000094;
        private const uint STATUS_INTEGER_OVERFLOW       = 0xC0000095;
        private const uint STATUS_INVALID_HANDLE         = 0xC0000008;
        private const uint STATUS_INVALID_LOCK_SEQUENCE  = 0xC000001E;
        private const uint STATUS_INVALID_SYSTEM_SERVICE = 0xC000001C;
        private const uint STATUS_PORT_DISCONNECTED      = 0xC0000037;
        private const uint STATUS_SERVICE_HANG           = 0xefffffff;
        private const uint STATUS_SINGLE_STEP            = 0x80000004;
        private const uint STATUS_STACK_BUFFER_OVERRUN   = 0xC0000409;
        private const uint STATUS_STACK_OVERFLOW         = 0xC00000FD;
        private const uint STATUS_VERIFIER_STOP          = 0xC0000421;
        private const uint STATUS_VCPP_EXCEPTION         = 0x406d1388;
        private const uint STATUS_WAKE_SYSTEM_DEBUGGER   = 0x80000007;
        private const uint STATUS_WX86_BREAKPOINT        = 0x4000001F;
        private const uint STATUS_WX86_SINGLE_STEP       = 0x4000001E;

        // "DKSN": Don't Know Symbolic Name (these constants seem to be hardcoded in
        // dbgeng's filter table).
        private const uint _DKSN_RT_ORIGINATE_ERROR = 0x40080201;
        private const uint _DKSN_RT_TRANSFORM_ERROR = 0x40080202;


        private static string _GetNameForException( uint exceptionCode )
        {
            switch( exceptionCode )
            {
                case STATUS_ACCESS_VIOLATION:
                    return "av";
                case STATUS_ASSERTION_FAILURE:
                    return "asrt";
                case STATUS_APPLICATION_HANG:
                    return "aph";
                case STATUS_BREAKPOINT:
                    return "bpe";
                case STATUS_CPP_EH_EXCEPTION:
                    return "eh";
                case STATUS_CLR_EXCEPTION:
                    return "clr";
                case CLRDATA_NOTIFY_EXCEPTION:
                    return "clrn";
                case DBG_CONTROL_BREAK:
                    return "cce";
                case DBG_CONTROL_C:
                    return "cce"; // TODO: duplicate!
                case STATUS_DATATYPE_MISALIGNMENT:
                    return "dm";
                case DBG_COMMAND_EXCEPTION:
                    return "dbce";
                case STATUS_GUARD_PAGE_VIOLATION:
                    return "gp";
                case STATUS_ILLEGAL_INSTRUCTION:
                    return "ii";
                case STATUS_IN_PAGE_ERROR:
                    return "ip";
                case STATUS_INTEGER_DIVIDE_BY_ZERO:
                    return "dz";
                case STATUS_INTEGER_OVERFLOW:
                    return "iov";
                case STATUS_INVALID_HANDLE:
                    return "ch";
                case STATUS_INVALID_LOCK_SEQUENCE:
                    return "lsq";
                case STATUS_INVALID_SYSTEM_SERVICE:
                    return "isc";
                case STATUS_PORT_DISCONNECTED:
                    return "3c";
                case STATUS_SERVICE_HANG:
                    return "svh";
                case STATUS_SINGLE_STEP:
                    return "sse";
                case STATUS_STACK_BUFFER_OVERRUN:
                    return "sbo";
                case STATUS_STACK_OVERFLOW:
                    return "sov";
                case STATUS_VERIFIER_STOP:
                    return "vs";
                case STATUS_VCPP_EXCEPTION:
                    return "vcpp";
                case STATUS_WAKE_SYSTEM_DEBUGGER:
                    return "wkd";
                case STATUS_WX86_BREAKPOINT:
                    return "wob";
                case STATUS_WX86_SINGLE_STEP:
                    return "wos";
                case _DKSN_RT_ORIGINATE_ERROR:
                    return "rto";
                case _DKSN_RT_TRANSFORM_ERROR:
                    return "rtt";
            }

            return null;
        } // end _GetNameForException()


        private static Dictionary< string, uint > sm_nameToExceptionMap
            = new Dictionary< string, uint >( StringComparer.OrdinalIgnoreCase )
        {
            { "av", STATUS_ACCESS_VIOLATION },
            { "asrt", STATUS_ASSERTION_FAILURE },
            { "aph", STATUS_APPLICATION_HANG },
            { "bpe", STATUS_BREAKPOINT },
            { "eh", STATUS_CPP_EH_EXCEPTION },
            { "clr", STATUS_CLR_EXCEPTION },
            { "clrn", CLRDATA_NOTIFY_EXCEPTION },
            // "cce" is used for both DBG_CONTROL_BREAK and DBG_CONTROL_C...
            { "cce", DBG_CONTROL_BREAK },
            //{ "cce", DBG_CONTROL_C },
            { "dm", STATUS_DATATYPE_MISALIGNMENT },
            { "dbce", DBG_COMMAND_EXCEPTION },
            { "gp", STATUS_GUARD_PAGE_VIOLATION },
            { "ii", STATUS_ILLEGAL_INSTRUCTION },
            { "ip", STATUS_IN_PAGE_ERROR },
            { "dz", STATUS_INTEGER_DIVIDE_BY_ZERO },
            { "iov", STATUS_INTEGER_OVERFLOW },
            { "ch", STATUS_INVALID_HANDLE },
            { "lsq", STATUS_INVALID_LOCK_SEQUENCE },
            { "isc", STATUS_INVALID_SYSTEM_SERVICE },
            { "3c", STATUS_PORT_DISCONNECTED },
            { "svh", STATUS_SERVICE_HANG },
            { "sse", STATUS_SINGLE_STEP },
            { "sbo", STATUS_STACK_BUFFER_OVERRUN },
            { "sov", STATUS_STACK_OVERFLOW },
            { "vs", STATUS_VERIFIER_STOP },
            { "vcpp", STATUS_VCPP_EXCEPTION },
            { "wkd", STATUS_WAKE_SYSTEM_DEBUGGER },
            { "wob", STATUS_WX86_BREAKPOINT },
            { "wos", STATUS_WX86_SINGLE_STEP },
            { "rto", _DKSN_RT_ORIGINATE_ERROR },
            { "rtt", _DKSN_RT_TRANSFORM_ERROR },
        };


        // TODO: or should this be internal
        public DbgExceptionEventFilter( uint exceptionCode,
                                        string friendlyName,
                                        DEBUG_FILTER_EXEC_OPTION executionOption,
                                        DEBUG_FILTER_CONTINUE_OPTION continueOption,
                                        string command,
                                        string secondCommand )
            : this( exceptionCode,
                    friendlyName,
                    executionOption,
                    continueOption,
                    command,
                    secondCommand,
                    _GetNameForException( exceptionCode ) )
        {
        }

        internal DbgExceptionEventFilter( uint exceptionCode,
                                          string friendlyName,
                                          DEBUG_FILTER_EXEC_OPTION executionOption,
                                          DEBUG_FILTER_CONTINUE_OPTION continueOption,
                                          string command,
                                          string secondCommand,
                                          string name )
            : base( friendlyName, executionOption, continueOption, command, name )
        {
            ExceptionCode = exceptionCode;
            SecondCommand = secondCommand ?? String.Empty;
        } // end constructor


        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            if( String.IsNullOrEmpty( FriendlyName ) )
                sb.Append( "(custom)" );
            else
                sb.Append( FriendlyName );

            sb.AppendFormat( " ({0:x8}), {1} / {2}", ExceptionCode, ExecutionOption, ContinueOption );

            if( !String.IsNullOrEmpty( Command ) )
            {
                sb.Append( ", 1st-chance cmd: \"" );
                sb.Append( Command );
                sb.Append( "\"" );
            }

            if( !string.IsNullOrEmpty( SecondCommand ) )
            {
                sb.Append( ", 2nd-chance cmd: \"" );
                sb.Append( SecondCommand );
                sb.Append( "\"" );
            }
            return sb.ToString();
        } // end ToString()
    } // end class DbgExceptionEventFilter
}
