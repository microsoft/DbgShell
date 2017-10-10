using System.Management.Automation;
using MS.Dbg.Commands;

namespace MS.Dbg.Formatting.Commands
{
    /// <summary>
    ///    Sets the default IFormatInfo (view definition) for the specified PSObject.
    /// </summary>
    [Cmdlet( VerbsCommon.Set, "AltFormatDefaultViewDef" )]
    public class SetAltFormatDefaultViewDefCommand : DbgBaseCommand
    {
        protected override bool TrySetDebuggerContext { get { return false; } }
        protected override bool LogCmdletOutline { get { return false; } }


        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true )]
        [ValidateNotNull()]
        public PSObject ForObject { get; set; }

        [Parameter( Mandatory = true, Position = 1 )]
        [ValidateNotNull()]
        public IFormatInfo FormatInfo { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            AltFormattingManager.SetDefaultFormatInfoForObject( ForObject, FormatInfo );
        } // end ProcessRecord()
    } // end class SetAltFormatDefaultViewDefCommand
}

