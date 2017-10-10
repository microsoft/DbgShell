using System;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsData.Initialize, "DbgSymbols" )]
    public class InitializeDbgSymbolsCmd : DbgBaseCommand
    {

        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true )]
        [ModuleNameTransformation]
        public string Module { get; set; }

        [Parameter]
        public SwitchParameter Literal { get; set; }


        [Parameter]
        public SwitchParameter Force { get; set; }

        [Parameter]
        public SwitchParameter IgnoreMismatched { get; set; }

        [Parameter]
        public SwitchParameter OverwriteDownstream { get; set; }

        [Parameter]
        [AddressTransformation]
        public ulong Address { get; set; }

        [Parameter]
        public ulong Size { get; set; }

        [Parameter]
        public string Timestamp { get; set; }


        // Do not use directly; this is just for trying to auto-rewrite commands written
        // by people who are accustomed to windbg syntax.
        [Parameter( ValueFromRemainingArguments = true )]
        public string[] Compat { get; set; }


        // TODO: Unloading symbols should probably just be a separate cmdlet. But what
        // verb?! ("Initialize" is a stretch as it is, and there's no Uninitialize.) I'll
        // be lazy for now and just include it here.
        [Parameter]
        public SwitchParameter Unload { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();
            string mod;
            const string whackFmsg = "The '/f' syntax does not work with PowerShell parameters. Use '-f' instead.";

            mod = Module;
            if( 0 == Util.Strcmp_OI( mod, "/f" ) )
            {
                if( (null == Compat) || (0 == Compat.Length) )
                {
                    WriteError( new ArgumentException( whackFmsg ),
                                "BadParamWhackF",
                                ErrorCategory.InvalidArgument,
                                null );
                    return;
                }
                WriteWarning( whackFmsg );
                // Let's just hope this works.
                mod = Compat[ 0 ];
                Force = true;
            }

            if( (null != Compat) && (0 != Compat.Length) )
            {
                if( 0 == Util.Strcmp_OI( Compat[ 0 ], "/f" ) )
                {
                    WriteWarning( whackFmsg );
                    Force = true;
                }
            }

            SafeWriteVerbose( "Reloading symbols for {0}", mod );

            Debugger.ReloadSymbols( mod,
                                    Literal,
                                    Address,
                                    Size,
                                    Timestamp,
                                    Force,
                                    IgnoreMismatched,
                                    OverwriteDownstream,
                                    Unload );

            // We want stacks to get re-generated if we got new symbols.
            DbgProvider.ForceRebuildNamespace();
        } // end ProcessRecord()
    } // end class InitializeDbgSymbolsCmd
}
