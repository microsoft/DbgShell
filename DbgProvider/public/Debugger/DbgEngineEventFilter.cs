using System;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    public class DbgEngineEventFilter : DbgEventFilter // TODO: IEquatable?
    {
        // TODO: is there a better enum for this?
        public DEBUG_FILTER_EVENT Event { get; private set; }

        public string Argument { get; private set; }

        private static string _GetNameForEvent( DEBUG_FILTER_EVENT specificEvent )
        {
            switch( specificEvent )
            {
                case DEBUG_FILTER_EVENT.CREATE_THREAD:
                    return "ct";
                case DEBUG_FILTER_EVENT.EXIT_THREAD:
                    return "et";
                case DEBUG_FILTER_EVENT.CREATE_PROCESS:
                    return "cpr";
                case DEBUG_FILTER_EVENT.EXIT_PROCESS:
                    return "epr";
                case DEBUG_FILTER_EVENT.LOAD_MODULE:
                    return "ld";
                case DEBUG_FILTER_EVENT.UNLOAD_MODULE:
                    return "ud";
                case DEBUG_FILTER_EVENT.SYSTEM_ERROR:
                    return "ser";
                case DEBUG_FILTER_EVENT.INITIAL_BREAKPOINT:
                    return "ibp";
                case DEBUG_FILTER_EVENT.INITIAL_MODULE_LOAD:
                    return "iml";
                case DEBUG_FILTER_EVENT.DEBUGGEE_OUTPUT:
                    return "out";
                default:
                    Util.Fail( Util.Sprintf( "Uknown filter event: {0}", specificEvent ) );
                    return null;
            }
        } // end _GetNameForEvent()

        // TODO: or should this be internal?
        public DbgEngineEventFilter( DEBUG_FILTER_EVENT specificEvent,
                                     string friendlyName,
                                     DEBUG_FILTER_EXEC_OPTION executionOption,
                                     DEBUG_FILTER_CONTINUE_OPTION continueOption,
                                     string argument,
                                     string command )
            : base( friendlyName, executionOption, continueOption, command, _GetNameForEvent( specificEvent ) )
        {
            Event = specificEvent;
            Util.Assert( !String.IsNullOrEmpty( friendlyName ), "Shouldn't specific events always have a name?" );
            Argument = argument ?? String.Empty; // is replacing nulls with empty string okay?
        } // end constructor

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append( FriendlyName );
            sb.Append( " (" );
            sb.Append( Event );
            if( !String.IsNullOrEmpty( Argument ) )
            {
                sb.Append( ':' );
                sb.Append( Argument );
            }
            sb.Append( "), " );
            sb.Append( ExecutionOption );
            sb.Append( " / " );
            sb.Append( ContinueOption );
            if( !String.IsNullOrEmpty( Command ) )
            {
                sb.Append( ", Command: " );
                sb.Append( Command );
            }
            return sb.ToString();
        } // end ToString()
    } // end class DbgEngineEventFilter
}
