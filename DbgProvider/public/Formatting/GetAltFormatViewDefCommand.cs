using System;
using System.Management.Automation;
using MS.Dbg.Commands;

namespace MS.Dbg.Formatting.Commands
{
    /// <summary>
    ///    Given a PSObject (and optionally a view info type), chooses a single
    ///    IFormatInfo for it. If none is found, does not return anything.
    /// </summary>
    [Cmdlet( VerbsCommon.Get, "AltFormatViewDef", DefaultParameterSetName = "Default" )]
    [OutputType( typeof( IFormatInfo ) )]
    public class GetAltFormatViewDefCommand : DbgBaseCommand
    {
        private const string c_TypeParamSet = "TypeNameParamSet";

        protected override bool TrySetDebuggerContext { get { return false; } }
        protected override bool LogCmdletOutline { get { return false; } }


        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true )]
        [ValidateNotNull()]
        public PSObject ForObject { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
                    ParameterSetName = c_TypeParamSet )]
        public Type FormatInfoType { get; set; }


        // TODO: -All parameter?

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            IFormatInfo fi = AltFormattingManager.ChooseFormatInfoForPSObject( ForObject,
                                                                               FormatInfoType );

            if( null != fi )
            {
                WriteObject( fi );
            }
        } // end ProcessRecord()
    } // end class GetAltFormatViewDefCommand
}

