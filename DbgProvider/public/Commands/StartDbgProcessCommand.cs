using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Text;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsLifecycle.Start, "DbgProcess" )]
    public class StartDbgProcessCommand : ExecutionBaseCommand
    {
        [Parameter( Mandatory = true, Position = 0 )]
        public string FilePath { get; set; }

        [Parameter(Mandatory = false)]
        public string[] ArgumentList { get; set; }

        // The name we should use in the root of the Dbg:\ drive to represent this target.
        [Parameter(Mandatory = false, Position = 1)]
        public string TargetName { get; set; }

        [Parameter(Mandatory = false)]
        public string StartDirectory { get; set; }

        [Parameter(Mandatory = false)]
        [Alias( "o" )]
        public SwitchParameter DebugChildProcesses { get; set; }


        // The aliases for SkipInitialBreakpoint SkipFinalBreakpoint can't quite match
        // windbg because they're the same except for case ("-g", "-G"), so we'll use
        // something "close": -gi, -gf (for "Go Initial", "Go Final"). And we'll throw in
        // -si and -sf as abbreviations that actually match the parameters.

        [Parameter(Mandatory = false)]
        [Alias( "gi", "si" )]
        public SwitchParameter SkipInitialBreakpoint { get; set; }


        [Parameter(Mandatory = false)]
        [Alias( "gf", "sf" )]
        public SwitchParameter SkipFinalBreakpoint { get; set; }

        [Parameter(Mandatory = false)]
        [Alias( "hd" )]
        public SwitchParameter NoDebugHeap { get; set; }

        [Parameter(Mandatory = false)]
        public CreateProcessConsoleOption ConsoleOption { get; set; }



        private int processId;


        private string _BuildCommandline()
        {
            // Support PS paths (ex "Bin:\DbgShellTest\TestNativeConsoleApp.exe"):
            string realPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath( FilePath );

            var sbCmdline = new StringBuilder( realPath.Length + 100 );
            sbCmdline.Append( '"' ).Append( realPath ).Append( '"' );
            if( null != ArgumentList )
            {
                foreach( string arg in ArgumentList )
                {
                    sbCmdline.Append( ' ' );
                    sbCmdline.Append( arg );
                }
            }
            return sbCmdline.ToString();
        }


        protected override void ProcessRecord()
        {
            //LogManager.Trace( "Connecting to process 0x{0:x} ({1}).", Id, TargetName );

            if( String.IsNullOrEmpty( TargetName ) )
            {
                TargetName = Util.Sprintf("{0} ({1})",
                    Path.GetFileNameWithoutExtension(FilePath), processId);
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

            Debugger = DbgEngDebugger.NewDebugger();

            using( Debugger.SetCurrentCmdlet( this ) )
            {
                Debugger.ProcessCreated += DebuggerOnProcessCreated;

                Debugger.CreateProcessAndAttach2( _BuildCommandline(),
                                                  StartDirectory,
                                                  TargetName,
                                                  SkipInitialBreakpoint,
                                                  SkipFinalBreakpoint,
                                                  NoDebugHeap,
                                                  DebugChildProcesses,
                                                  ConsoleOption );


                //Debugger.SetInputCallbacks( new DebugInputCallbacks( this ) );
                //Debugger.SetOutputCallbacks( new DebugOutputCallbacks( this ) );

                try
                {
                    // will call Debugger.WaitForEvent()
                    base.ProcessRecord();
                }
                finally
                {
                    Debugger.ProcessCreated -= DebuggerOnProcessCreated;
                }
            } // end using( current cmdlet )
        } // end ProcessRecord()

        private void DebuggerOnProcessCreated(object sender, ProcessCreatedEventArgs processCreatedEventArgs)
        {
            processId = NativeMethods.GetProcessId((IntPtr) processCreatedEventArgs.Handle);
        }

    } // end class ConnectProcessCommand
}

