using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgNearSymbol" )]
    [OutputType( typeof( DbgNearSymbol ) )]
    public class GetDbgNearSymbolCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true )]
        [AddressTransformation]
        public ulong Address { get; set; }

        [Parameter( Mandatory = false, Position = 1 )]
        [Alias( "d" )] // So that -d is not ambiguous between -Debug and -Delta.
        public uint Delta { get; set; }


        public GetDbgNearSymbolCommand()
        {
            Delta = 1;
        } // end constructor


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            for( int curDelta = 0 - (int) Delta; curDelta <= (int) Delta; curDelta++ )
            {
                long disp;
                string name = Debugger.GetNearNameByOffset( Address, curDelta, out disp );
                if( null != name )
                {
                    Util.Assert( 0 != name.Length );

                    // We pass "Default" for symEnumOptions so that we don't get inline
                    // symbols. We don't want to get inline symbols because
                    // GetNearNameByOffset does not return names of inline symbols, so
                    // they can't possibly be what we want. It could be confusing enough
                    // as it is if we have more than one symbol with that name...
                    foreach( var sym in Debugger.FindSymbol_Enum( name,
                                                                  GlobalSymbolCategory.All,
                                                                  CancelTS.Token,
                                                                  SymEnumOptions.Default ) )
                    {
                        WriteObject( new DbgNearSymbol( Address, disp, sym ) );
                    }
                }
                else
                {
                    if( curDelta > 0 )
                        break; // we can go ahead and give up; if there wasn't a near
                               // symbol at delta N, then there won't be one at N+1.
                }
            } // end for( curDelta )
        } // end ProcessRecord()
    } // end class GetDbgNearSymbolCommand
}

