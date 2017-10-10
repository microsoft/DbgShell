using System;
using Microsoft.Diagnostics.Runtime.Interop;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgShellLog" )]
    [OutputType( typeof( string ) )]
    public class GetDbgShellLogCommand : DbgBaseCommand
    {
        [Parameter]
        public SwitchParameter Current { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( Current )
            {
                WriteObject( LogManager.CurrentLogFile );
            }
            else
            {
                foreach( string log in LogManager.EnumerateLogFiles() )
                {
                    WriteObject( log );
                }
            }
        } // end ProcessRecord()
    } // end class GetDbgShellLogCommand
}

