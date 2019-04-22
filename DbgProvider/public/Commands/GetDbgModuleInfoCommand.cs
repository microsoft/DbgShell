using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgModuleInfo",
             DefaultParameterSetName = c_NameParamSet )]
    [OutputType( typeof( DbgModuleInfo ) )]
    public class GetDbgModuleInfoCommand : DbgBaseCommand
    {
        // TODO: add filter params, like name wildcard, loaded only, etc.

        private const string c_NameParamSet = "NameParamSet";
        private const string c_AddressParamSet = "AddressParamSet";

        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true,
                    ParameterSetName = c_NameParamSet )]
        [SupportsWildcards]
        public string[] Name { get; set; }


        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true,
                    ParameterSetName = c_AddressParamSet )]
        [AddressTransformation]
        public ulong Address { get; set; }


        [Parameter]
        public SwitchParameter Unloaded { get; set; }


        private bool _Matches( string name, DbgModuleInfo mod )
        {
            if( String.IsNullOrEmpty( name ) )
                return true;

            if( WildcardPattern.ContainsWildcardCharacters( name ) )
            {
                var pat = new WildcardPattern( name, WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase );
                return pat.IsMatch( mod.Name );
            }

            return 0 == Util.Strcmp_OI( mod.Name, name );
        } // end _Matches()


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( 0 != Address )
            {
                WriteObject( Debugger.GetModuleByAddress( Address ) );
                return;
            }

            //
            // If you just ran something like "lm 123`456789ab", PS will interpret that
            // address as a string. If you use "-Address", the [AddressTransformation]
            // attribute will handle the conversion, but otherwise we'll have to do it
            // manually.
            //
            // Let's check if Name is actually an address:
            //

            if( (null != Name) && (Name.Length == 1) && !String.IsNullOrEmpty( Name[ 0 ] ) )
            {
                object addrObj = AddressTransformationAttribute.Transform( null,  // EngineIntrinsics
                                                                           SessionState.Path.CurrentProviderLocation( DbgProvider.ProviderId ).ProviderPath,
                                                                           true,  // skipGlobalSymbolTest
                                                                           false, // throwOnFailure
                                                                           false, // dbgMemoryPassThru
                                                                           false, // allowList
                                                                           Name[ 0 ] );

                if( (null != addrObj) && (addrObj is ulong) )
                {
                    WriteObject( Debugger.GetModuleByAddress( (ulong) addrObj ) );
                    return;
                }
            }

            IList< DbgModuleInfo > modules;
            if( Unloaded )
            {
                modules = Debugger.UnloadedModules;
            }
            else
            {
                modules = Debugger.Modules;
            }

            if( (null == Name) || (0 == Name.Length) )
            {
                Name = new string[] { String.Empty }; // matches every module
            }

            foreach( string name in Name )
            {
                foreach( var mod in modules )
                {
                    if( _Matches( name, mod ) )
                    {
                        WriteObject( mod );
                    }
                }
            }
        } // end ProcessRecord()
    } // end class GetDbgModuleInfoCommand
}
