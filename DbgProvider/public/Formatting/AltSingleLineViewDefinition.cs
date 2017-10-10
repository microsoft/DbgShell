using System;
using System.Management.Automation;

namespace MS.Dbg.Formatting
{
    public class AltSingleLineViewDefinition : IFormatInfo
    {
        string IFormatInfo.FormatCommand { get { return "Format-AltSingleLine"; } }
        string IFormatInfo.Module { get { return "Debugger"; } }

        public ScriptBlock Script { get; private set; }

        internal readonly PsContext Context;

        public AltSingleLineViewDefinition( ScriptBlock script )
            : this( script, false )
        {
        }

        public AltSingleLineViewDefinition( ScriptBlock script, bool captureContext )
        {
            if( null == script )
                throw new ArgumentNullException( "script" );

            Script = script;
            if( captureContext )
                Context = DbgProvider.CapturePsContext();
        } // end constructor
    } // end class AltSingleLineViewDefinition
}

