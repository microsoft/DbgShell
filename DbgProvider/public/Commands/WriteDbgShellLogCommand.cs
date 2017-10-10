using System;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommunications.Write, "DbgShellLog" )]
    public class WriteDbgShellLogCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true )]
        [AllowEmptyString]
        [AllowNull]
        public string Line { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            // Or should I just trace it directly?
            LogManager.Trace( "Write-DbgShellLog: {0}", Line );
        } // end ProcessRecord()

        protected override bool TrySetDebuggerContext { get { return false; } }
        protected override bool LogCmdletOutline { get { return false; } }
    } // end class WriteDbgShellLogCommand
}

