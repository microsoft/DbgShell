using System;
using System.Management.Automation;
using MS.Dbg.Commands;

namespace MS.Dbg.Formatting.Commands
{
    [Cmdlet( VerbsLifecycle.Register, "AltTypeFormatEntry" )]
    public class RegisterAltTypeFormatEntryCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true, Position = 0, ValueFromPipeline = true )]
        [ValidateNotNull]
        public AltTypeFormatEntry Entry { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();
            AltFormattingManager.RegisterViewDefinition( Entry );
        } // end ProcessRecord()

        protected override bool TrySetDebuggerContext { get { return false; } }
    } // end class RegisterAltTypeFormatEntryCommand
}

