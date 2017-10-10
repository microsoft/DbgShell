using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgSymbolPath" )]
    [OutputType( typeof( string ) )]
    public class GetSymbolPathCommand : DbgBaseCommand
    {
        protected override bool TrySetDebuggerContext { get { return false; } }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();
            WriteObject( Debugger.SymbolPath );
        } // end ProcessRecord()
    } // end class GetSymbolPathCommand
}

