using System;
using System.Management.Automation;
using MS.Dbg.Commands;

namespace MS.Dbg.Formatting.Commands
{
    [Cmdlet( VerbsCommon.Remove, "AltTypeFormatEntry" )]
    public class RemoveAltTypeFormatEntryCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true, Position = 0, ValueFromPipeline = true )]
        [ValidateNotNullOrEmpty]
        public string TypeName { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();
            // BUG: Hmm... we don't support wildcards, and we don't complain if nothing is
            // removed. Oh well.
            AltFormattingManager.RemoveByName( TypeName );
        } // end ProcessRecord()

        protected override void EndProcessing()
        {
            AltFormattingManager.ScrubFileList();
            base.EndProcessing();
        }

        protected override bool TrySetDebuggerContext { get { return false; } }
    } // end class RemoveAltTypeFormatEntryCommand
}

