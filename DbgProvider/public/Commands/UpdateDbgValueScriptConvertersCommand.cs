using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    //
    // This is modeled after Update-[Alt]FormatData.
    //

    [Cmdlet( VerbsData.Update, "DbgValueScriptConverters" )]
    public class UpdateDbgValueScriptConvertersCommand : DbgBaseCommand
    {
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
                DbgValueConversionManager.Reload( InvokeCommand,
                                                   m_appendPaths,
                                                   m_prependPaths,
                                                   pipe );
            }
        } // end EndProcessing()

        protected override bool TrySetDebuggerContext { get { return false; } }
    } // end class UpdateDbgValueScriptConvertersCommand
}

