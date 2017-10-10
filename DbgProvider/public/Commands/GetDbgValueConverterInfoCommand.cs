using System;
using System.Management.Automation;
using MS.Dbg.Commands;

namespace MS.Dbg.Formatting.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgValueConverterInfo", DefaultParameterSetName = c_TypeNameParamSet )]
    [OutputType( typeof( DbgValueConverterInfo ) )]
    public class GetDbgValueConverterInfoCommand : DbgBaseCommand
    {
        private const string c_TypeNameParamSet = "TypeNameParamSet";
        private const string c_SymbolParamSet = "SymbolParamSet";

        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true,
                    ParameterSetName = c_TypeNameParamSet )]
        [AllowEmptyString]
        [AllowNull]
        public string TypeName { get; set; }

        [Parameter( Mandatory = false,
                    Position = 1,
                    ParameterSetName = c_TypeNameParamSet )]
        [AllowEmptyString]
        [AllowNull]
        [SupportsWildcards]
        public string ModuleName { get; set; }

        [Parameter( Mandatory = false, ParameterSetName = c_TypeNameParamSet )]
        public SwitchParameter ExactMatchOnly { get; set; }


        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ParameterSetName = c_SymbolParamSet )]
        [ValidateNotNull]
        [Alias( "InputObject" )]
        public DbgSymbol ForSymbol { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( null != ForSymbol )
            {
                Util.Assert( ParameterSetName == c_SymbolParamSet );
                foreach( var converter in DbgValueConversionManager.EnumerateAllConvertersForSymbol( ForSymbol ) )
                {
                    WriteObject( converter );
                }
                return;
            }

            if( String.IsNullOrEmpty( ModuleName ) && String.IsNullOrEmpty( TypeName ) )
            {
                if( ExactMatchOnly )
                {
                    // I guess this doesn't have to be terminating.
                    WriteError( new ArgumentException( "You asked for exact matches only, but did not specify a type name." ),
                                "InvalidParam_ExactMatchOnly",
                                ErrorCategory.InvalidArgument,
                                null );
                }

                foreach( var converter in DbgValueConversionManager.GetEntries() )
                {
                    WriteObject( converter );
                }
                return;
            }

            foreach( var converter in DbgValueConversionManager.GetMatchingEntries( ModuleName,
                                                                                     TypeName,
                                                                                     ExactMatchOnly ) )
            {
                WriteObject( converter );
            }
        } // end ProcessRecord()

        protected override bool TrySetDebuggerContext { get { return false; } }
    } // end class GetDbgValueConverterInfoCommand
}

