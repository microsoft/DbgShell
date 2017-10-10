using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using MS.Dbg.Commands;

namespace MS.Dbg.Formatting.Commands
{

    [Cmdlet( VerbsData.Update, "AltFormatData" )]
    public class UpdateAltFormatDataCommand : DbgBaseCommand
    {
        // TODO: need to document how our precedence works and how it differs from regular
        // formatting... when a format isn't specified, we pick the first format that was
        // registered, but if multiple scripts register the same format, the last writer
        // wins.
        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true )]
        [Alias( "PSPath", "Path" )]
        [ValidateNotNull]
        public string[] AppendPath { get; set; }

        [Parameter( Mandatory = false )]
        [ValidateNotNull]
        public string[] PrependPath { get; set; }


        protected override bool TrySetDebuggerContext { get { return false; } }


        // TODO: I think this is the wrong way to do it. Instead of gathering all the paths
        // in ProcessRecord and then doing everything in EndProcessing, we should dump everything
        // in BeginProcessing and then load stuff as we go in ProcessRecord.

        private List< string > m_appendPaths = new List< string >();
        private List< string > m_prependPaths = new List< string >();

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( null != AppendPath )
            {
                foreach( var path in AppendPath )
                {
                    ProviderInfo unused;
                    Collection< string > resolvedPaths = GetResolvedProviderPathFromPSPath( path,
                                                                                            out unused );
                    m_appendPaths.AddRange( resolvedPaths );
                }
            }

            if( null != PrependPath )
            {
                foreach( var path in PrependPath )
                {
                    ProviderInfo unused;
                    Collection< string > resolvedPaths = GetResolvedProviderPathFromPSPath( path,
                                                                                            out unused );
                    m_prependPaths.AddRange( resolvedPaths );
                }
            }
        } // end ProcessRecord()


        protected override void EndProcessing()
        {
            using( var pipe = GetPipelineCallback() )
            {
                AltFormattingManager.Reload( InvokeCommand,
                                             m_appendPaths,
                                             m_prependPaths,
                                             pipe );
            }
        } // end EndProcessing()

    } // end class UpdateAltFormatDataCommand
}


