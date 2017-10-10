using System;
using System.Diagnostics;
using System.Management.Automation;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommunications.Connect, "Process" )]
    public class ConnectProcessCommand : ExecutionBaseCommand
    {
        [Parameter( Mandatory = true, Position = 0 , ValueFromPipeline = true, ValueFromPipelineByPropertyName = true )]
        [Alias( "p", "Pid", "ProcessId" )] // you know, like ntsd -p :D
        public int Id { get; set; }

        // The name we should use in the root of the Dbg:\ drive to represent this target.
        [Parameter( Mandatory = false, Position = 1  )]
        public string TargetName { get; set; }

        // The aliases for SkipInitialBreakpoint SkipFinalBreakpoint can't quite match
        // windbg because they're the same except for case ("-g", "-G"), so we'll use
        // something "close": -gi, -gf (for "Go Initial", "Go Final"). And we'll throw in
        // -si and -sf as abbreviations that actually match the parameters.

        [Parameter]
        [Alias( "gi", "si" )]
        public SwitchParameter SkipInitialBreakpoint { get; set; }

        [Parameter]
        [Alias( "gf", "sf" )]
        public SwitchParameter SkipFinalBreakpoint { get; set; }

        // TODO: is -ReAttach valid with any of -SkipInitialBreakpoint, -SkipFinalBreakpoint?
        [Parameter]
        [Alias( "pe" )] // corresponds to -pe
        public SwitchParameter ReAttach { get; set; }


        protected override void ProcessRecord()
        {
            if( 0 == Id )
            {
                // TODO: get our base cmdlet stuff
                WriteError( new ErrorRecord( new InvalidOperationException(), "CannotAttachToSystemProcess", ErrorCategory.InvalidOperation, Id ) );
                return;
            }

            string existingTargetName = null;
            if( String.IsNullOrEmpty( TargetName ) )
            {
                // Check to see if it already has a name before generating one.
                if( !Debugger.TryGetExistingUmTargetName( (uint) Id, out existingTargetName ) ||
                    DbgProvider.IsTargetNameInUse( existingTargetName ) )
                {
                    using( Process p = Process.GetProcessById( Id ) )
                    {
                        TargetName = Util.Sprintf( "{0} ({1})", p.ProcessName, Id );
                    }
                }
                else
                {
                    TargetName = existingTargetName;
                }
                // TODO: keep generating new target names until we succeed.
            }

            if( DbgProvider.IsTargetNameInUse( TargetName ) )
            {
                ThrowTerminatingError( new ArgumentException( Util.Sprintf( "The target name '{0}' is already in use. Please use -TargetName to specify a different target name.",
                                                                            TargetName ),
                                                              "TargetName" ),
                                       "TargetNameInUse",
                                       ErrorCategory.ResourceExists,
                                       TargetName );
            }

            LogManager.Trace( "Connecting to process 0x{0:x} ({1}).", Id, TargetName );

            Debugger = DbgEngDebugger.NewDebugger();

            using( Debugger.SetCurrentCmdlet( this ) )
            {
                Debugger.AttachToProcess( Id,
                                          SkipInitialBreakpoint,
                                          SkipFinalBreakpoint,
                                          ReAttach ? DEBUG_ATTACH.EXISTING : DEBUG_ATTACH.DEFAULT,
                                          TargetName );

                // This will call Debugger.WaitForEvent()
                base.ProcessRecord( true );

                if( ReAttach )
                {
                    // Workaround for INT60c4fc41: "Reattaching to an .abandoned
                    // process leaves thread suspend count too high"
                    //
                    // When abandoning a process, the debugger increments the suspend
                    // count of all the threads. When re-attaching, the debugger
                    // SHOULD undo that, but... it doesn't. So we'll try to fix that.
                    foreach( var t in Debugger.EnumerateThreads() )
                    {
                        for( int sc = t.SuspendCount; sc > 1; sc-- )
                        {
                            t.DecrementSuspendCount();
                        }
                    }
                }
            } // end using( psPipe )
        } // end ProcessRecord()
    } // end class ConnectProcessCommand
}

