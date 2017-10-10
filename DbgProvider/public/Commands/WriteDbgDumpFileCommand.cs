using System;
using System.IO;
using System.Management.Automation;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommunications.Write, "DbgDumpFile",
             DefaultParameterSetName = "dummy" )]
    public class WriteDbgDumpFileCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true, Position = 0 )]
        [ValidateNotNullOrEmpty]
        public string DumpFile { get; set; }

        [Parameter( Mandatory = false, Position = 1 )]
        [ValidateNotNullOrEmpty]
        public string Comment { get; set; }


        [Parameter( Mandatory = false )]
        public SwitchParameter AllowClobber { get; set; }


        [Parameter( Mandatory = false, ParameterSetName = "CompressParameterSet" )]
        public SwitchParameter Compress { get; set; }

        [Parameter( Mandatory = false, ParameterSetName = "CompressWithSymbolsParameterSet" )]
        public SwitchParameter CompressWithSymbols { get; set; }

        // I think this should be for kernel-mode only.
     // [Parameter( Mandatory = false )]
     // public SwitchParameter IgnoreInaccessibleMemory { get; set; }



        protected override void ProcessRecord()
        {
            // Support relative paths in PS.
            string dumpFileResolved = SessionState.Path.GetUnresolvedProviderPathFromPSPath( DumpFile );

            DEBUG_FORMAT flags = DEBUG_FORMAT.DEFAULT;

            flags |= DEBUG_FORMAT.USER_SMALL_FULL_MEMORY |
                     DEBUG_FORMAT.USER_SMALL_HANDLE_DATA |
                     DEBUG_FORMAT.USER_SMALL_UNLOADED_MODULES |
                     DEBUG_FORMAT.USER_SMALL_INDIRECT_MEMORY |
                     DEBUG_FORMAT.USER_SMALL_DATA_SEGMENTS |
                     DEBUG_FORMAT.USER_SMALL_PROCESS_THREAD_DATA |
                     DEBUG_FORMAT.USER_SMALL_PRIVATE_READ_WRITE_MEMORY |
                     DEBUG_FORMAT.USER_SMALL_FULL_MEMORY_INFO |
                     DEBUG_FORMAT.USER_SMALL_THREAD_INFO |
                     DEBUG_FORMAT.USER_SMALL_CODE_SEGMENTS |
                     DEBUG_FORMAT.USER_SMALL_FULL_AUXILIARY_STATE;  // TODO: Need to learn what this means


            // I think this should be for kernel-mode only.
         // if( IgnoreInaccessibleMemory )
         // {
         //     flags |= DEBUG_FORMAT.USER_SMALL_IGNORE_INACCESSIBLE_MEM;
         // }

            // TODO: Figure out these things, and see if there are new ones I don't know about:
            //       DEBUG_FORMAT.USER_SMALL_FILTER_MEMORY |
            //       DEBUG_FORMAT.USER_SMALL_FILTER_PATHS |

            //       DEBUG_FORMAT.USER_SMALL_NO_OPTIONAL_DATA |

            //       DEBUG_FORMAT.USER_SMALL_NO_AUXILIARY_STATE |


            if( !AllowClobber )
            {
                flags |= DEBUG_FORMAT.NO_OVERWRITE;
            }

            if( Compress )
            {
                flags |= DEBUG_FORMAT.WRITE_CAB;
            }

            if( CompressWithSymbols )
            {
                flags |= DEBUG_FORMAT.WRITE_CAB;
                flags |= DEBUG_FORMAT.CAB_SECONDARY_FILES;
                // TODO: What is CAB_SECONDARY_ALL_IMAGES for? DbgEng.h says
                //
                //    "When creating a CAB with secondary images do searches
                //    for all image files, regardless of whether they're
                //    needed for the current session or not."
                //
                // but I don't know what it means for an image file to be "needed" for the
                // current session versus not.
            }

            if( (Compress || CompressWithSymbols) &&
                !DumpFile.EndsWith( ".cab", StringComparison.OrdinalIgnoreCase ) )
            {
                WriteWarning( "Output will be compressed, but output file name does not end in '.cab'." );
                WriteWarning( "You will need to rename the resulting file to end with '.cab' if you want to be able to mount the dump file with Mount-DbgDumpFile." );
            }

            // TODO: Just let the API fail instead of doing this pre-check?
            if( File.Exists( dumpFileResolved ) )
            {
                if( !AllowClobber )
                {
                    // is there a better "already exists" exception?
                    WriteError( new InvalidOperationException( Util.Sprintf( "The file '{0}' already exists. Use -AllowClobber to overwrite it.",
                                                                             DumpFile ) ),
                                "DumpFileAlreadyExists",
                                ErrorCategory.ResourceExists,
                                DumpFile );
                    return;
                }
            }

            using( var disposer = new ExceptionGuard() )
            {
                disposer.Protect( Debugger.HandleDbgEngOutput( ( x ) => SafeWriteVerbose( x.TrimEnd() ) ) );
                disposer.Protect( new CtrlCInterceptor( _CtrlCHandler ) );

                try
                {
                    MsgLoop.Prepare();

                    var task = Debugger.WriteDumpAsync( DumpFile,
                                                        flags,
                                                        Comment,
                                                        CancelTS.Token );
                    task.ContinueWith( ( _t ) => { disposer.Dispose() ; SignalDone(); } );

                    MsgLoop.Run();

                    Util.Await( task ); // in case it threw
                }
                catch( DbgProviderException dpe )
                {
                    if( (dpe.HResult == DebuggerObject.E_UNEXPECTED) &&
                        CancelTS.IsCancellationRequested )
                    {
                        WriteWarning( "Dump file creation canceled." );
                    }
                    else
                    {
                        WriteError( dpe );
                    }
                }
            } // end using( dbgeng output, CTRL-C )
        } // end ProcessRecord()


        private bool _CtrlCHandler( ConsoleBreakSignal ctrlType )
        {
            LogManager.Trace( "WriteDbgDumpFileCommand._CtrlCHandler ({0})", ctrlType );
            LogManager.Flush();

            if( ctrlType == ConsoleBreakSignal.CtrlBreak )
            {
                QueueSafeAction( () =>
                    {
                        this.Host.EnterNestedPrompt();
                    } );
            }
            else
            {
                SafeWriteWarning( "Canceling... Canceling dump creation might take a long time. Sorry." );
                CancelTS.Cancel();
            }

            return true;
        } // end _CtrlCHandler()

    } // end class WriteDbgDumpFileCommand
}

