using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgStack", DefaultParameterSetName = c_MaxFrameCountParamSet )]
    [OutputType( typeof( DbgStackInfo ) )]
    public class GetDbgStackCommand : DbgBaseCommand
    {
        private const string c_MaxFrameCountParamSet = "MaxFrameCountParamSet";
        private const string c_ContextPathParamSet = "ContextPathParamSet";
        private const string c_ThreadParamSet = "ThreadParamSet";

        // The path in the debugger namespace that indicates the context we should
        // be getting the stack for.
        [Parameter( Mandatory = false,
                    ParameterSetName = c_ContextPathParamSet,
                    Position = 2,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true )]
        [Alias( "PSPath" )] // this, combined with ValueFromPipelineByPropertyName, allows you to "dir | Get-DbgStack".
        public string[] ContextPath { get; set; }


        [Parameter( Mandatory = false,
                    Position = 1 )]
        public int MaxFrameCount { get; set; }


        // If you have the thread objects, you could just get the stack from them. But being
        // able to do it this way too makes certain things easier, like "~10 k".
        [Parameter( Mandatory = false,
                    ParameterSetName = c_ThreadParamSet,
                    Position = 2,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true )]
        [ThreadTransformation]
        public DbgUModeThreadInfo[] Thread { get; set; }

        // TODO: options for how the stack should be displayed

        protected override void ProcessRecord()
        {
            if( null != Thread )
            {
                foreach( var t in Thread )
                {
                    if( null != t )
                        WriteObject( t.GetStackWithMaxFrames( MaxFrameCount ) );
                    else
                        SafeWriteWarning( "Null entry in -Thread parameter array." );
                }

                return;
            }

            IList< string > paths;
            if( (null == ContextPath) || (0 == ContextPath.Length) )
            {
                PathInfo pi = this.CurrentProviderLocation( DbgProvider.ProviderId );
                paths = new string[] { pi.ProviderPath };
            }
            else
            {
                ProviderInfo pi;
                var tmpList = new List<string>();
                foreach( string p in ContextPath )
                    tmpList.AddRange( SessionState.Path.GetResolvedProviderPathFromPSPath( p, out pi ) );

                paths = tmpList;
            }

            foreach( string path in paths )
            {
                DbgStackInfo stack = DbgProvider.GetStackInfoForPath( path, MaxFrameCount );
                if( null != stack )
                {
                    // TODO: Make sure the formatting is working such that we are not waiting
                    // for the entire stack to be calculated before seeing any output. (Could
                    // a test be written for that?)
                    SafeWriteObject( stack );
                }
                else
                {
                    var ioe = new InvalidOperationException( Util.Sprintf( "No stack context for path: {0}", path ) );
                    try { throw ioe; } catch ( Exception ) { } // give it a stack

                    // TODO: Need a "no stack context exception" or something
                    WriteError( ioe,
                                "NoStackContext",
                                ErrorCategory.InvalidOperation,
                                path );
                }
            } // end foreach( path )
        } // end ProcessRecord()
    } // end class GetDbgStackCommand
}
