using System;
using System.Management.Automation;
using MS.Dbg.Commands;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsLifecycle.Register, "DbgPlugin" )]
    public class RegisterDbgPluginCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true, Position = 0, ValueFromPipeline = true )]
        [ValidateNotNullOrEmpty]
        [SupportsWildcards]
        public string[] Path { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            using( var pipe = GetPipelineCallback() )
            foreach( string tmp in Path )
            {
                ProviderInfo dontCare;
                foreach( string definitePath in SessionState.Path.GetResolvedProviderPathFromPSPath( tmp, out dontCare ) )
                {
                    PluginManager.LoadPlugin( definitePath, pipe );
                }
            }
        } // end ProcessRecord()

        protected override bool TrySetDebuggerContext { get { return false; } }
    } // end class RegisterDbgPluginCommand
}

