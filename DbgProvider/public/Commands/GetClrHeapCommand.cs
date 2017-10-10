using Microsoft.Diagnostics.Runtime;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "ClrHeap" )]
    [OutputType( typeof( ClrHeap ) )]
    public class GetClrHeapCommand : DbgBaseCommand
    {
        protected override void ProcessRecord()
        {
            //var process = Debugger.GetCurrentTarget() as DbgUModeProcess;
            var process = Debugger.GetCurrentUModeProcess();

            if( null == process )
            {
                SafeWriteError( "No current user-mode process.",
                                "NoUmodeProcess",
                                ErrorCategory.NotImplemented,
                                null );
            }
            foreach( var runtime in process.ClrRuntimes )
            {
                var heap = runtime.GetHeap();
                WriteObject( heap );
            }
        }
    }
}
