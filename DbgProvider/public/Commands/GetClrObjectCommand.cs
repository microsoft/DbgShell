using Microsoft.Diagnostics.Runtime;
using System.Linq;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "ClrObject" )]
    [OutputType( typeof( ClrObject ) )]
    public class GetClrObjectCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true )]
        [ValidateNotNullOrEmpty]
        public ClrHeap[] ClrHeap { get; set; }

        protected override void ProcessRecord()
        {
            if( ClrHeap == null )
            {
                ClrHeap = Debugger
                    .GetCurrentUModeProcess()
                    .ClrRuntimes
                    .Select( x => x.GetHeap() )
                    .Where( x => x.CanWalkHeap )
                    .ToArray();
            }

            foreach( var heap in ClrHeap )
            {
                if( !heap.CanWalkHeap )
                {
                    WriteError( new DbgProviderException( "Cannot walk heap",
                                                          "HeapNotWalkable",
                                                          ErrorCategory.InvalidArgument,
                                                          heap ) );
                }
                else
                {
                    foreach( var address in heap.EnumerateObjectAddresses() )
                    {
                        var clrType = heap.GetObjectType( address );
                        if( clrType != null )
                        {
                            WriteObject( new ClrObject( address, clrType ) );
                        }
                    }
                }
            } // end foreach( heap )
        } // end ProcessRecord()
    } // end GetClrObjectCommand
}
