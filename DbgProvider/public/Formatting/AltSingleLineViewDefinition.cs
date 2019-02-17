using System;
using System.Management.Automation;

namespace MS.Dbg.Formatting
{
    public class AltSingleLineViewDefinition : IFormatInfo
    {
        string IFormatInfo.FormatCommand { get { return "Format-AltSingleLine"; } }
        string IFormatInfo.Module { get { return "Debugger"; } }

        public ScriptBlock Script { get; private set; }


        public AltSingleLineViewDefinition( ScriptBlock script )
        {
            Script = script ?? throw new ArgumentNullException( nameof(script) );
        } // end constructor


        bool IFormatInfo.LooksLikeExistingFromPropertyDefinition( IFormatInfo other )
        {
            Util.Fail( "nobody should be calling this..." );
            return false;
        }
    } // end class AltSingleLineViewDefinition
}

