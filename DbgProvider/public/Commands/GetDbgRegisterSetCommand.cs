using System;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgRegisterSet", DefaultParameterSetName = "RegisterSet" )]
    [OutputType( typeof( DbgRegisterSetBase ), ParameterSetName = new string[] { "RegisterSet" } )]
    public class GetDbgRegisterSetCommand : DbgBaseCommand
    {
        [Parameter( Position = 0, Mandatory = true, ParameterSetName = "History" )]
        [ValidateNotNullOrEmpty()]
        public string Id { get; set; }


        protected override void ProcessRecord()
        {
            if( ParameterSetName.Equals( "History" ) )
            {
                var powershell = PowerShell.Create( RunspaceMode.CurrentRunspace );
                powershell.AddCommand( "Invoke-History" );
                powershell.AddParameter( "Id", Id );
                powershell.Invoke();
            }
            else
            {
                string path = this.CurrentProviderLocation( DbgProvider.ProviderId ).ProviderPath;
                var regSet = DbgProvider.GetRegisterSetForPath( path );
                if( null != regSet )
                {
                    SafeWriteObject( regSet );
                }
                else
                {
                    // TODO: Need a "no register context exception" or something
                    WriteError( new InvalidOperationException( Util.Sprintf( "No register context for path: {0}", path ) ),
                                "NoRegisterContext",
                                ErrorCategory.InvalidOperation,
                                path );
                }

            }

        } // end ProcessRecord()
    } // end class GetDbgRegisterSetCommand
}
