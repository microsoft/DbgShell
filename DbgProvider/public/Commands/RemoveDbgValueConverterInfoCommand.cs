using System;
using System.Management.Automation;
using MS.Dbg.Commands;

namespace MS.Dbg.Formatting.Commands
{
    [Cmdlet( VerbsCommon.Remove, "DbgValueConverterInfo", DefaultParameterSetName = c_TypeNameParamSet )]
    [OutputType( typeof( DbgValueConverterInfo ) )]
    public class RemoveDbgValueConverterInfoCommand : DbgBaseCommand
    {
        private const string c_TypeNameParamSet = "TypeNameParamSet";
        private const string c_FileParamSet = "FileParamSet";

        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ParameterSetName = c_TypeNameParamSet )]
        [ValidateNotNullOrEmpty()]
        public string TypeName { get; set; }

        [Parameter( Mandatory = false, ParameterSetName = c_TypeNameParamSet )]
        public SwitchParameter AllMatching { get; set; }


        // TODO:
     // [Parameter( Mandatory = true,
     //             Position = 0,
     //             ValueFromPipeline = true,
     //             ParameterSetName = c_FileParamSet )]
     // [ValidateNotNullOrEmpty]
     // public string File { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( !String.IsNullOrEmpty( TypeName ) )
            {
                string mod, template;
                ulong dontCare;
                DbgProvider.ParseSymbolName( TypeName, out mod, out template, out dontCare );
                if( 0 != dontCare )
                    throw new ArgumentException( "The TypeName parameter does not look like a type name.", "TypeName" );

                DbgValueConversionManager.RemoveEntries( mod, template, !AllMatching );
            }
            else
                Util.Fail( "impossible" );
         // else
         // {
         //     Util.Assert( !String.IsNullOrEmpty( File ) );
         //     string fullpath = GetUnresolvedProviderPathFromPSPath( File );
         //     DbgValueConversionManager.RemoveAllEntriesFromFile( fullpath, !AllMatching );
         // }
        } // end ProcessRecord()

        protected override void EndProcessing()
        {
            DbgValueConversionManager.ScrubFileList();
            base.EndProcessing();
        }

        protected override bool TrySetDebuggerContext { get { return false; } }
    } // end class RemoveDbgValueConverterInfoCommand
}

