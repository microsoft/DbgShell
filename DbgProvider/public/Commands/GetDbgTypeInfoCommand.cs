using System.Collections.Generic;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgTypeInfo", DefaultParameterSetName = c_NameParamSet )]
    [OutputType( typeof( DbgTypeInfo ) )]
    public class GetDbgTypeInfoCommand : DbgBaseCommand
    {
        private const string c_NameParamSet = "NameParamSet";
        private const string c_IdParamSet = "IdParamSet";
        private const string c_AddressParamSet = "AddressParamSet";

        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ParameterSetName = c_NameParamSet )]
        [SupportsWildcards]
        public string Name { get; set; }


        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ParameterSetName = c_IdParamSet )]
        public uint TypeId { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
                    ParameterSetName = c_IdParamSet )]
        [ModuleTransformation]
        public DbgModuleInfo Module { get; set; }


        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true,
                    ParameterSetName = c_AddressParamSet )]
        [AddressTransformation( FailGracefully = true )] // FailGracefully to allow ValueFromPipelineByPropertyName to work
        public ulong Address { get; set; }


        [Parameter( ParameterSetName = c_NameParamSet )]
        public SwitchParameter NoFollowTypedefs { get; set; }

        [Parameter]
        public SwitchParameter Raw { get; set; }

        // Unfortunately, the linker or whoever writes the PDBs sometimes includes
        // duplicates. It's a pain to run "Get-DbgTypeInfo foo" and /sometimes/ get back
        // multiple results when you expected just one, and the multiple results are all
        // identical except for TypeId. So by default we'll suppress that.
        //
        // You might want to set this to true if you are dumping lots of types (because
        // this will necessarily keep the whole pipeline's worth in memory).
        [Parameter( ParameterSetName = c_NameParamSet )]
        public SwitchParameter AllowDuplicates { get; set; }


        // Used to prevent emitting duplicates.
        private HashSet< DbgTypeInfo > m_set;


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( Raw && NoFollowTypedefs )
            {
                // Is this necessary? Maybe I should just be quiet.
                WriteWarning( "-Raw implies -NoFollowTypedefs." );
            }

            if( !AllowDuplicates )
            {
                m_set = new HashSet< DbgTypeInfo >();
            }
            else if( Raw )
            {
                WriteWarning( "-AllowDuplicates has no effect when used with -Raw." );
            }

            try
            {
                if( 0 == Util.Strcmp_OI( c_NameParamSet, ParameterSetName ) )
                {
                    _GetTypeInfoByName( Name );
                }
                else if( 0 == Util.Strcmp_OI( c_IdParamSet, ParameterSetName ) )
                {
                    _GetTypeInfoById( TypeId, Module );
                }
                else
                {
                    _GetTypeInfoByAddress( Address );
                }
            }
            catch( DbgProviderException dpe )
            {
                WriteError( dpe );
            }
        } // end ProcessRecord()


        private bool _IsNotDuplicate( DbgTypeInfo ti )
        {
            if( null == m_set )
                return true;
            else
                return m_set.Add( ti );
        } // end _IsNotDuplicate()


        private void _GetTypeInfoByName( string name )
        {
            if( Raw )
            {
                foreach( var pso in Debugger.GetTypeInfoByNameRaw( name,
                                                                   CancelTS.Token ) )
                {
                    if( Stopping )
                        break;

                    WriteObject( pso );
                }
            }
            else
            {
                // TODO: can we prioritize things that actually match the name???
                foreach( var ti in Debugger.GetTypeInfoByName( name,
                                                               NoFollowTypedefs,
                                                               CancelTS.Token ) )
                {
                    if( Stopping )
                        break;

                    if( _IsNotDuplicate( ti ) )
                        WriteObject( ti );
                }
            }
        } // end _GetTypeInfoByName()


        private void _GetTypeInfoById( uint typeId, DbgModuleInfo module )
        {
            if( Raw )
                WriteObject( Debugger._GetTypeInfoRaw( module.BaseAddress, typeId ) );
            else
            {
                WriteObject( DbgTypeInfo.GetTypeInfo( Debugger,
                                                      module,
                                                      typeId ) );
            }
        } // end _GetTypeInfoById()


        private void _GetTypeInfoByAddress( ulong address )
        {
            uint typeId = Debugger.GetTypeIdForAddress( address );
            var mod = Debugger.GetModuleByAddress( address );
            if( Raw )
                WriteObject( Debugger._GetTypeInfoRaw( mod.BaseAddress, typeId ) );
            else
                WriteObject( DbgTypeInfo.GetTypeInfo( Debugger, mod, typeId ) );
        } // end _GetTypeInfoByAddress()
    } // end class GetDbgTypeInfoCommand
}

