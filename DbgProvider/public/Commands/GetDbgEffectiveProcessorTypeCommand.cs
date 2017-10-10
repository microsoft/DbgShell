using System;
using Microsoft.Diagnostics.Runtime.Interop;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgEffectiveProcessorType" )]
    [OutputType( typeof( IMAGE_FILE_MACHINE ) )]
    public class GetDbgEffectiveProcessorTypeCommand : DbgBaseCommand
    {
        private const string c_x86 = "x86";
        private const string c_x64 = "x64";

        protected override void ProcessRecord()
        {
            base.ProcessRecord();
            IMAGE_FILE_MACHINE effmach = Debugger.GetEffectiveProcessorType();
            PSObject pso = new PSObject( effmach );
            LogManager.Trace( "Effective processor type: {0}", effmach );

            // HACK ALERT: For I386 and AMD64, let's display something more familiar.
            // TODO: Actually, maybe this is better to do with a custom view definition?
            Func< object > del = null;
            switch( effmach )
            {
                case IMAGE_FILE_MACHINE.I386:
                    del = () => { return c_x86; };
                    pso.Methods.Add( new PSDbgMethodInfo( "ToString",
                                                          "System.String",
                                                          del ) );
                    break;

                case IMAGE_FILE_MACHINE.AMD64:
                    del = () => { return c_x64; };
                    pso.Methods.Add( new PSDbgMethodInfo( "ToString",
                                                          "System.String",
                                                          del ) );
                    break;

            }
            WriteObject( pso );
        } // end ProcessRecord()
    } // end class GetDbgEffectiveProcessorTypeCommand
}

