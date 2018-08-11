using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg.Commands
{
    // TODO: This only works with local variables, not global.

    [Cmdlet( VerbsCommon.Get, "DbgSymbolValue", DefaultParameterSetName = c_NameParamSet )]
    public class GetDbgSymbolValueCommand : DbgBaseCommand
    {
        private const string c_NameParamSet = "NameParamSet";
        private const string c_AddressAndTypeParamSet = "AddressAndTypeParamSet";
        private const string c_AddressAndTypeNameParamSet = "AddressAndTypeNameParamSet";

        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true,
                    ParameterSetName = c_NameParamSet )]
        [SupportsWildcards]
        public string Name { get; set; }


        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true,
                    ParameterSetName = c_AddressAndTypeParamSet )]
        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true,
                    ParameterSetName = c_AddressAndTypeNameParamSet )]
        [AddressTransformation( FailGracefully = true )] // FailGracefully to allow ValueFromPipelineByPropertyName to work
        public ulong Address { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
                    ParameterSetName = c_AddressAndTypeParamSet )]
        [ValidateNotNull]
        [TypeTransformation]
        public DbgNamedTypeInfo Type { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
                    ParameterSetName = c_AddressAndTypeNameParamSet )]
        [ValidateNotNullOrEmpty]
        public string TypeName { get; set; }

        [Parameter( Mandatory = false,
                    Position = 2,
                    ParameterSetName = c_AddressAndTypeParamSet )]
        [Parameter( Mandatory = false,
                    Position = 2,
                    ParameterSetName = c_AddressAndTypeNameParamSet )]
        [ValidateNotNullOrEmpty]
        public string NameForNewSymbol { get; set; }


        // TODO: Hm... the problem with these parameters (NoConversion and
        // NoDerivedTypeDetection) is that they only apply to the first symbol (i.e. if
        // you do "Get-DbgSymbolValue foo!blah.bar.zip -NoConversion", then the "no
        // conversion" directive only applies to foo!blah. Perhaps I should just remove
        // these.
        //
        // You might think "that's easy; just make -NoWhatever 'sticky' so that once you
        // get a value that way, all children have the same -NoWhatever applied."
        // Unfortunately, that doesn't work out so well--I tried it, but it broke
        // everything, and was a terrible user experience. The problem is that the user
        // probably just wants to avoid conversion/dtd for just one symbol, but expects
        // them for everything else, including children. If you make the -NoX 'sticky',
        // things like converters which start off by getting the "stock" value
        // (-NoConversion) are now "stuck" in a world of unconverted things, which makes
        // it a giant pain to use.
        [Parameter( Mandatory = false )]
        public SwitchParameter NoConversion { get; set; }

        [Parameter( Mandatory = false )]
        public SwitchParameter NoDerivedTypeDetection { get; set; }


        private GlobalSymbolCategory m_category = GlobalSymbolCategory.All;

        [Parameter( Mandatory = false, ParameterSetName = c_NameParamSet )]
        public GlobalSymbolCategory Category
        {
            get { return m_category; }
            set { m_category = value; }
        }



        private void _DumpByName()
        {
            // TODO: this should probably throw if we don't have private symbols
            DbgSymbolGroup dsg = new DbgSymbolGroup( Debugger, DEBUG_SCOPE_GROUP.ALL );

            if( String.IsNullOrEmpty( Name ) )
            {
                Name = "*";

                var pat = new WildcardPattern( Name, WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase );
                foreach( var sym in dsg.Items )
                {
                    if( null == sym.Parent ) // we only write out the top-level symbols
                    {
                        if( pat.IsMatch( sym.Name ) )
                            WriteObject( _GetValue( sym ) );
                    }
                }
            }
            else
            {
                var tokens = Name.Split( '.' );
                string startName = tokens[ 0 ];
                IList< DbgSymbol > rootSyms;
                if( startName.IndexOf( '!' ) >= 0 )
                {
                    // Global(s).
                    rootSyms = Debugger.FindSymbol_Search( startName,
                                                           Category,
                                                           CancelTS.Token ).ToList();
                }
                else
                {
                    // Local(s).
                    WildcardPattern pat = new WildcardPattern( startName );
                    rootSyms = dsg.Items.Where( ( s ) => pat.IsMatch( s.Name ) ).Cast< DbgSymbol >().ToList();
                }

                if( (null == rootSyms) || (0 == rootSyms.Count) )
                {
                    // TODO: Some sort of "symbol not found" exception
                    WriteError( new ArgumentException( Util.Sprintf( "Symbol \"{0}\" not found.", startName ) ),
                                "SymbolNotFound",
                                ErrorCategory.ObjectNotFound,
                                startName );
                    return;
                }

                foreach( DbgSymbol rootSym in rootSyms )
                {
                    PSObject val = _GetValue( rootSym );
                    int i = 1;
                    var sbPrefix = new StringBuilder( tokens[ 0 ] );
                    while( i < tokens.Length )
                    {
                        if( val == null )
                        {
                            // TODO: what?
                            // TODO: Some sort of "symbol null" exception?
                            WriteError( new ArgumentException( Util.Sprintf( "Symbol \"{0}\" was null; cannot dereference {1}.", sbPrefix.ToString(), tokens[ i ] ) ),
                                        "SymbolNull",
                                        ErrorCategory.ObjectNotFound,
                                        sbPrefix.ToString() + "." + tokens[ i ] );
                            break;
                        }

                        if( 0 == tokens[ i ].Length )
                        {
                            // A trailing '.'? Or consecutive '.'s?
                            WriteError( new ArgumentException( "You need something after the '.' (or between \"..\")." ),
                                        "MissingSymbolToken",
                                        ErrorCategory.InvalidArgument,
                                        Name );
                            val = null;
                            break;
                        }

                        // TODO: Wildcard support?

                        var prop = val.Properties[ tokens[ i ] ];
                        if( null == prop )
                        {
                            // TODO: Some sort of "symbol field not found" exception?
                            WriteError( new ArgumentException( Util.Sprintf( "Symbol \"{0}\" has no such property {1}.", sbPrefix.ToString(), tokens[ i ] ) ),
                                        "SymbolLacksRequestedProperty",
                                        ErrorCategory.ObjectNotFound,
                                        sbPrefix.ToString() + "." + tokens[ i ] );
                            val = null;
                            break;
                        }
                        val = (PSObject) prop.Value;
                        sbPrefix.Append( '.' ).Append( tokens[ i ] );
                        i++;
                    } // end while( more tokens )
                    if( null != val )
                        WriteObject( val );
                } // end foreach( rootSyms )
            } // end else( not '*' )
        } // end _DumpByName()


        private PSObject _GetValue( DbgSymbol symbol )
        {
            return symbol.GetValue( NoConversion, NoDerivedTypeDetection );
        }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( ParameterSetName == c_NameParamSet )
            {
                _DumpByName();
            }
            else
            {
                if( ParameterSetName == c_AddressAndTypeNameParamSet )
                {
                    Type = Debugger._PickTypeInfoForName( TypeName, CancelTS.Token );
                }

                Util.Assert( null != Type );

                PSObject val = Debugger.GetValueForAddressAndType( Address,
                                                                   Type,
                                                                   NameForNewSymbol,
                                                                   NoConversion,
                                                                   NoDerivedTypeDetection );
                WriteObject( val );
            } // end else( address and type[name] )
        } // end ProcessRecord()
    } // end class GetDbgSymbolValueCommand
}

