using System;
using System.Management.Automation;
using MS.Dbg.Commands;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Find, "WindbgDir" )]
    [OutputType( typeof( string ))]
    public class FindWindbgDirCommand : DbgBaseCommand
    {
        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            string dir = DbgProvider._TryFindDebuggersDir();
            if( !String.IsNullOrEmpty( dir ) )
            {
                WriteObject( dir );
            }
        } // end ProcessRecord()

        protected override bool TrySetDebuggerContext { get { return false; } }
    } // end class FindWindbgDirCommand
}

