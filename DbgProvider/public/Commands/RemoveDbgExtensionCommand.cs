using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Remove, "DbgExtension" )]
    public class RemoveDbgExtensionCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true )]
        // aliases?
        public string PathOrName { get; set; }


        protected override bool TrySetDebuggerContext { get { return false; } }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            try
            {
                // Are there any extensions that expect to write output when they are
                // unloaded? (We're not handling that.)
                Debugger.RemoveExtension( PathOrName );
                SafeWriteVerbose( "Removed extension: {0}", PathOrName );
            }
            catch( DbgProviderException dpe )
            {
                // This should not be a terminating error.
                SafeWriteError( dpe );
            }
        } // end ProcessRecord()
    } // end class RemoveDbgExtensionCommand
}

