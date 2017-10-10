using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgSymbol" )]
    [OutputType( typeof( DbgSymbol ) )]
    public class GetDbgSymbolCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = false, Position = 0, ValueFromPipeline = true )]
        [SupportsWildcards]
        public string[] Name { get; set; }


        private GlobalSymbolCategory m_category = GlobalSymbolCategory.All;

        [Parameter( Mandatory = false, Position = 1 )]
        public GlobalSymbolCategory Category
        {
            get { return m_category; }
            set { m_category = value; }
        }

        [Parameter( Mandatory = false )]
        public SwitchParameter UseSymSearch { get; set; }

        [Parameter( Mandatory = false )]
        public SwitchParameter UseSymEnum { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            foreach( var name in Name )
            {
                if( UseSymSearch )
                {
                    foreach( var sym in Debugger.FindSymbol_Search( name,
                                                                    Category,
                                                                    CancelTS.Token ) )
                    {
                        WriteObject( sym );
                    }
                }
                else if( UseSymEnum )
                {
                    foreach( var sym in Debugger.FindSymbol_Enum( name,
                                                                  Category,
                                                                  CancelTS.Token ) )
                    {
                        WriteObject( sym );
                    }
                }
                else
                {
                    // Default behavior is to use SymSearch... unless that yields nothing,
                    // in which case we'll try SymEnum as well.
                    //
                    // SymSearch seems to work fine when we have true private symbols...
                    // but not as well with stripped public PDBs that have had some
                    // information removed.

                    int count = 0;
                    foreach( var sym in Debugger.FindSymbol_Search( name,
                                                                    Category,
                                                                    CancelTS.Token ) )
                    {
                        count++;
                        WriteObject( sym );
                    }

                    if( 0 == count )
                    {
                        foreach( var sym in Debugger.FindSymbol_Enum( name,
                                                                      Category,
                                                                      CancelTS.Token ) )
                        {
                            WriteObject( sym );
                        }
                    }
                } // end else( use default symbol find method )
            } // end foreach( name )
        } // end ProcessRecord()
    } // end class GetDbgSymbolCommand
}

