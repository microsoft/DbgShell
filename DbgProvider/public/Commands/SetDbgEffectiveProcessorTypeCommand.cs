using System.Management.Automation;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Set, "DbgEffectiveProcessorType" )]
    public class SetDbgEffectiveProcessorTypeCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true, Position = 0 )]
        [EffMachTransformation]
        public IMAGE_FILE_MACHINE EffectiveProcessorType { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();
            Debugger.SetEffectiveProcessorType( EffectiveProcessorType );
            Debugger.AdjustAddressColumnWidths( Host.UI.RawUI.BufferSize.Width );
            DbgProvider.ForceRebuildNamespace();
        } // end ProcessRecord()
    } // end class SetDbgEffectiveProcessorTypeCommand
}

