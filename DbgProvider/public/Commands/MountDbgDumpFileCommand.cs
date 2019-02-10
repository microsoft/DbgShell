using System;
using System.IO;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsData.Mount, "DbgDumpFile" )]
    public class MountDbgDumpFileCommand : ExecutionBaseCommand
    {
        // The z alias: you know, like ntsd -z
        // The PSPath alias: along with ValueFromPipelineByPropertyName, this lets you get
        // the expected results when you pipe in the output of Get-ChildItem
        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true )]
        [Alias( "z", "PSPath" )]
        [ValidateNotNullOrEmpty]
        public string DumpFile { get; set; }

        // The name that will be used in the virtual namespace to represent the dump file.
        [Parameter( Mandatory = false, Position = 1  )]
        public string TargetName { get; set; }

        [Parameter( Mandatory = false )]
        public SwitchParameter AllowClobber { get; set; }


        protected override void ProcessRecord()
        {
            // Support relative paths in PS.
            string dumpFileResolved = SessionState.Path.GetUnresolvedProviderPathFromPSPath( DumpFile );

            // TODO: don't check this here... catch and write instead
            if( !File.Exists( dumpFileResolved ) )
            {
                // It could be wildcarded... unfortunately attaching to multiple dumps
                // doesn't seem to work. But if it only resolves to one file... let's
                // allow it.

                ProviderInfo dontCare;
                var multi = SessionState.Path.GetResolvedProviderPathFromPSPath( DumpFile,
                                                                                 out dontCare );

                if( (null == multi) || (0 == multi.Count) )
                {
                    ThrowTerminatingError( new FileNotFoundException(),
                                           "NoSuchDumpFile",
                                           ErrorCategory.ObjectNotFound,
                                           DumpFile );
                    return;
                }
                else if( multi.Count > 1 )
                {
                    ThrowTerminatingError( new NotSupportedException( "Attaching to multiple dump files does not really work." ),
                                           "AttachToMultipleDumpsNotsupported",
                                           ErrorCategory.OpenError,
                                           DumpFile );
                    return;
                }
                else
                {
                    dumpFileResolved = multi[ 0 ];
                }
            }

            if( String.IsNullOrEmpty( TargetName ) )
            {
                TargetName = Path.GetFileNameWithoutExtension( dumpFileResolved );
            }

            if( DbgProvider.IsTargetNameInUse( TargetName ) )
            {
                if( AllowClobber )
                {
                    throw new NotImplementedException();
                }
                else
                {
                    ThrowTerminatingError( new ArgumentException( Util.Sprintf( "The target name '{0}' is already in use. Please use -TargetName to specify a different target name.",
                                                                                TargetName ),
                                                                  "TargetName" ),
                                           "TargetNameInUse",
                                           ErrorCategory.ResourceExists,
                                           TargetName );
                }
            }

            Debugger = DbgEngDebugger.NewDebugger();
            using( Debugger.SetCurrentCmdlet( this ) )
            {
                Debugger.LoadCrashDump( dumpFileResolved, TargetName );
                base.ProcessRecord( true );
            } // end using( psPipe )
        } // end ProcessRecord()

    } // end class MountDbgDumpFileCommand

    // Dismount-DumpFile is implemented in script, since it's just a wrapper around Remove-Item.
    // TODO: I don't think that's okay... we need to shut down the debugger engine (observe what
    // happens when you dismount a dump file and then re-attach). But how? IDebugClient.EndSession?
}
