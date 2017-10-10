using System;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    public class DbgLastEventInfo : ISupportColor
    {
        private DbgEngDebugger m_debugger;

        public readonly DEBUG_EVENT EventType;

        /// <summary>
        ///    The [dbgeng] id of the system that the last event occurred in.
        /// </summary>
        public readonly uint SystemId;

        /// <summary>
        ///    The [dbgeng] id of the process that the last event occurred in.
        /// </summary>
        public readonly uint ProcessId;

        /// <summary>
        ///    The [dbgeng] id of the thread that the last event occurred in.
        /// </summary>
        public readonly uint ThreadId;

        public readonly string Description;

        public readonly DEBUG_LAST_EVENT_INFO ExtraInformation;

        public DbgLastEventInfo( DbgEngDebugger debugger,
                                 DEBUG_EVENT eventType,
                                 uint systemId,
                                 uint processId,
                                 uint threadId,
                                 string description,
                                 DEBUG_LAST_EVENT_INFO extraInformation )
        {
            EventType = eventType;
            SystemId = systemId;
            ProcessId = processId;
            ThreadId = threadId;
            Description = description;
            ExtraInformation = extraInformation;

            m_debugger = debugger;
        }


        private DateTimeOffset _GetDbgEngLastEventTimestamp()
        {
            // Example:
            //
            //    Last event: ac8.2f8: Break instruction exception - code 80000003 (first/second chance not available)
            //      debugger time: Mon Oct 20 06:41:19.392 2014 (UTC - 7:00)

            DateTimeOffset timestamp = DateTimeOffset.MinValue;
            using( m_debugger.HandleDbgEngOutput( (x) =>
                {
                    x = x.Trim();
                    const string debuggerTime = "debugger time: ";

                    if( x.StartsWith( debuggerTime, StringComparison.OrdinalIgnoreCase ) )
                    {
                        var tsStr = x.Substring( debuggerTime.Length );
                        if( !DbgProvider.TryParseDebuggerTimestamp( tsStr,
                                                                    out timestamp ) )
                        {
                            var msg = Util.Sprintf( "Huh... unable to parse dbgeng timestamp: {0}", tsStr );
                            LogManager.Trace( msg );
                            Util.Fail( msg );
                        }
                    }
                } ) )
            {
                m_debugger.InvokeDbgEngCommand( ".lastevent", false );
            }
            return timestamp;
        } // end _GetDbgEngLastEventTimestamp()


        private static string[] sm_newline = new string[] { "\r\n" };


        public ColorString ToColorString()
        {
            if( DEBUG_EVENT.NONE == EventType )
                return ColorString.Empty;

            ColorString csThreadId;
            if( 0xffffffff == ThreadId )
                csThreadId = new ColorString( ConsoleColor.Red, "ffffffff" );
            else
                csThreadId = new ColorString( m_debugger.GetUModeThreadByDebuggerId( ThreadId ).Tid.ToString( "x" ) );

            ColorString cs = new ColorString( ConsoleColor.Black, ConsoleColor.White, "     Last event:" )
                .Append( " " )
                .Append( m_debugger.GetProcessSystemId( ProcessId ).ToString( "x" ) )
                .Append( "." )
                .Append( csThreadId )
                .Append( ": " );

            DbgModuleInfo mod = null;
            string tmp = null;
            string[] lines = null;

            switch( EventType )
            {
                case DEBUG_EVENT.NONE:
                    Util.Fail( "can't get here" ); // we returned ColorString.Empty above
                    break;

                case DEBUG_EVENT.BREAKPOINT:
                    cs.Append( Description );

                    break;
                case DEBUG_EVENT.EXCEPTION:
                    tmp = ExceptionEventArgs._CreateMessage( m_debugger,
                                                             ExtraInformation.Exception.ExceptionRecord,
                                                             ExtraInformation.Exception.FirstChance != 0,
                                                             0,
                                                             null,
                                                             false ).ToString( DbgProvider.HostSupportsColor );

                    lines = tmp.Split( sm_newline, StringSplitOptions.None );

                    cs.AppendLine( lines[ 0 ] );
                    for( int i = 1; i < lines.Length; i++ )
                    {
                        cs.AppendPushPopFgBg( ConsoleColor.Black, ConsoleColor.White, "                " )
                          .Append( " " )
                          .Append( lines[ i ] );

                        if( i != lines.Length - 1 )
                            cs.AppendLine();
                    }

                        break;
                case DEBUG_EVENT.CREATE_THREAD:
                    cs.Append( Description );

                    break;
                case DEBUG_EVENT.EXIT_THREAD:
                    cs.Append( Description );

                    break;
                case DEBUG_EVENT.CREATE_PROCESS:
                    cs.Append( Description );

                    break;
                case DEBUG_EVENT.EXIT_PROCESS:
                    cs.Append( Description );

                    break;
                case DEBUG_EVENT.LOAD_MODULE:
                    //
                    //   Last event: 9c84.3984: Load module C:\WINDOWS\system32\ADVAPI32.dll at 00007ffb`b0cd0000
                    //     debugger time: Mon Oct 20 19:30:56.164 2014 (UTC - 7:00)

                    mod = m_debugger.GetModuleByAddress( ExtraInformation.LoadModule.Base );
                    cs.Append( "Loaded module " )
                        .AppendPushPopFg( ConsoleColor.White, mod.ImageName )
                        .Append( " at " )
                        .Append( DbgProvider.FormatAddress( ExtraInformation.LoadModule.Base,
                                                            m_debugger.TargetIs32Bit,
                                                            true ) );
                    break;
                case DEBUG_EVENT.UNLOAD_MODULE:
                    // TODO: test this!
                    mod = m_debugger.GetModuleByAddress( ExtraInformation.UnloadModule.Base );
                    cs.AppendPushPopFg( ConsoleColor.DarkRed, "Unloaded module " )
                        .AppendPushPopFg( ConsoleColor.White, mod.ImageName )
                        .Append( " at " )
                        .Append( DbgProvider.FormatAddress( ExtraInformation.UnloadModule.Base,
                                                            m_debugger.TargetIs32Bit,
                                                            true ) );
                    break;
                case DEBUG_EVENT.SYSTEM_ERROR:
                    // TODO: test this!
                    cs.AppendPushPopFgBg( ConsoleColor.White, ConsoleColor.Red, "System error:" )
                        .Append( " " )
                        .AppendPushPopFg( ConsoleColor.Red, Util.FormatErrorCode( ExtraInformation.SystemError.Error ) )
                        .Append( "." )
                        .AppendPushPopFg( ConsoleColor.Red, Util.FormatErrorCode( ExtraInformation.SystemError.Level ) );

                    // TODO: This is a hack.
                    var w32e = new System.ComponentModel.Win32Exception( (int) ExtraInformation.SystemError.Error );
                    cs.AppendLine();
                    cs.AppendPushPopFg( ConsoleColor.Red, w32e.Message ).Append( "." );

                    break;
                case DEBUG_EVENT.SESSION_STATUS:
                    // DbgEng doesn't save these as "last events".
                    Util.Fail( Util.Sprintf( "Unexpected last event type: {0}", EventType ) );

                    break;
                case DEBUG_EVENT.CHANGE_DEBUGGEE_STATE:
                    // DbgEng doesn't save these as "last events".
                    Util.Fail( Util.Sprintf( "Unexpected last event type: {0}", EventType ) );

                    break;
                case DEBUG_EVENT.CHANGE_ENGINE_STATE:
                    // DbgEng doesn't save these as "last events".
                    Util.Fail( Util.Sprintf( "Unexpected last event type: {0}", EventType ) );

                    break;
                case DEBUG_EVENT.CHANGE_SYMBOL_STATE:
                    // DbgEng doesn't save these as "last events".
                    Util.Fail( Util.Sprintf( "Unexpected last event type: {0}", EventType ) );

                    break;
                default:
                    Util.Fail( Util.Sprintf( "Unexpected event type: {0}", EventType ) );
                    throw new Exception( Util.Sprintf( "Unexpected event type: {0}", EventType ) );
            } // end switch( event type )


            cs.AppendLine()
                .AppendPushPopFgBg( ConsoleColor.Black, ConsoleColor.White, "  Debugger time:" )
                .Append( " " )
                .Append( DbgProvider.FormatTimestamp( _GetDbgEngLastEventTimestamp(), true ) );

            return cs;
        } // end ToColorString()


        public override string ToString()
        {
            return ToColorString();
        }
    } // end class DbgLastEventInfo
}

