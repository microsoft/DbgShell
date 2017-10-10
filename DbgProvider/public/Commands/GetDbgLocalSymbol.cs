using System;
using System.Management.Automation;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgLocalSymbol" )]
    // TODO: OutputType attribute
    public class GetDbgLocalSymbolCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = false, Position = 0 )]
        [SupportsWildcards]
        public string Name { get; set; }


        [Parameter( Mandatory = false, Position = 1 )]
        public DEBUG_SCOPE_GROUP Scope { get; set; }


        public GetDbgLocalSymbolCommand()
        {
            Scope = DEBUG_SCOPE_GROUP.ALL;
        } // end constructor

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( String.IsNullOrEmpty( Name ) )
                Name = "*";
            else if( Name.IndexOf( '!' ) >= 0 )
                WriteWarning( "Locals symbols shouldn't have a '!' in them." );

         // // I would love to use the DbgHelp API to enumerate locals directly. Unfortunately,
         // // the SYMBOL_INFO information for locals will take a LOT of work to interpret (thanks
         // // to "register relative", etc.). For now, we'll use the dbgeng API.
         // foreach( var si in DbgHelp.EnumSymbols( Debugger.DebuggerInterface,
         //                                         0, // modBase of 0 and no '!' in the mask means we're looking for locals/params
         //                                         Name,
         //                                         CancelTS.Token ) )
         // {
         //     //WriteObject( si );
         //     WriteObject( new DbgPublicSymbol( Debugger, si, Debugger.GetCurrentProcess() ) );
         // }
         // //throw new NotImplementedException();

            // TODO: this should probably throw if we don't have private symbols
            DbgSymbolGroup dsg = new DbgSymbolGroup( Debugger, Scope );

            var pat = new WildcardPattern( Name, WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase );
            foreach( var sym in dsg.Items )
            {
                if( null == sym.Parent ) // we only write out the top-level symbols
                {
                    if( pat.IsMatch( sym.Name ) )
                        WriteObject( sym );
                }
            }
        } // end ProcessRecord()
    } // end class GetDbgLocalSymbolCommand
}

