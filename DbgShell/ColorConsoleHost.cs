using System;
using System.IO;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using MS.Dbg;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.DbgShell
{
    internal class ColorConsoleHost : PSHost, IDisposable, IHostSupportsInteractiveSession
    {
        public const string c_DbgAutoRepeatCmd = "DbgAutoRepeatCommand";

        public const string c_SkipForOneShotPrefix = "SkipForOneShot_";

        public override CultureInfo CurrentCulture
        {
            get { return CultureInfo.CurrentCulture; }
        }

        public override CultureInfo CurrentUICulture
        {
            get { return CultureInfo.CurrentUICulture; }
        }

        private class InputLoop : IDisposable
        {
            //
            // For guest mode:
            //

            private static Queue< string > sm_injectedCommands;

            internal static void QueueInjectedCommands( IEnumerable< string > commands )
            {
                foreach( var command in commands )
                {
                    QueueInjectedCommand( command );
                }
            } // end QueueInjectedCommands()


            internal static void QueueInjectedCommand( string command )
            {
                if( null == sm_injectedCommands )
                    sm_injectedCommands = new Queue< string >();

                sm_injectedCommands.Enqueue( command );
            } // end QueueInjectedCommand()


            //
            // Normal instance stuff:
            //

            private ColorConsoleHost m_host;
            private CtrlCInterceptor m_ctrlCInterceptor;
            private PowerShell m_shell;

            public InputLoop( ColorConsoleHost host )
                : this( host, null )
            {
            }

            public InputLoop( ColorConsoleHost host, PowerShell shell )
            {
                m_host = host;
                m_ctrlCInterceptor = new CtrlCInterceptor( _CtrlC, true );

                if( null == shell )
                {
                    shell = PowerShell.Create();
                    shell.Runspace = m_host.Runspace;
                }

                m_shell = shell;
            } // end constructor


            private bool _CtrlC( ConsoleBreakSignal ctrlType )
            {
                // This is similar to the top-level "backstop" ctrl-c handler. The reason
                // each input loop needs its own is because in a nested prompt, if you
                // press ctrl-C when a command isn't running, the ctrl-c will go to the
                // parent shell (which is running $host.EnterNestedPrompt()), and try to
                // stop that shell.  It will block while the nested shell is running, and
                // when you next try to run a command, the nested input loop will try to
                // create a CtrlCInterceptor for that command, and it will block trying to
                // get into a lock to set the console control handler (in
                // kernel32!SetConsoleCtrlHandler)--deadlock.

                LogManager.Trace( "InputLoop._CtrlC ({0}), thread apartment type: {1}",
                                  ctrlType,
                                  Thread.CurrentThread.GetApartmentState() );
                LogManager.Flush();

                if( ctrlType == ConsoleBreakSignal.CtrlBreak )
                {
                    // We don't want to keep the CTRL-C thread tied up because we're
                    // holding a lock (which protects the list of CTRL-C handlers, and I
                    // don't know what else)--we don't want to create deadlock if someone
                    // in the nested prompt tries to install their own CTRL-C handler.
                    ThreadPool.QueueUserWorkItem( ( h ) => ((ColorConsoleHost) h).EnterNestedPrompt(), m_host );
                }

                if( ConsoleBreakSignal.Close == ctrlType )
                {
                    LogManager.Trace( "_CtrlC: received a CLOSE signal." );
                    m_timeToStop = true;
                    return false; // let the next handler know, too...
                }
                else
                {
                    // TODO: Now that I have this one here, do I need to top-level backstop?
                    // Because even the main input loop uses InputLoop now...
                    return true;
                }
            } // end _CtrlC

            private bool m_timeToStop;

            private void _ClearAutoRepeatCmd()
            {
                if( !m_shell.IsNested )
                {
                    m_shell.Runspace.SessionStateProxy.PSVariable.Set( c_DbgAutoRepeatCmd, null );
                }
                else
                {
                    // If we are nested, we can't directly access the session state (else
                    // we get an exception "A pipeline is already running. Concurrent
                    // SessionStateProxy method call is not allowed."). Super annoying,
                    // but there doesn't seem to be any way around it except to use some
                    // script to get the job done.
                    m_host._ExecScriptNoExceptionHandling( m_shell,
                                                           Util.Sprintf( "Set-Variable {0} $null",
                                                                         c_DbgAutoRepeatCmd ),
                                                           false,
                                                           false );
                }
            } // end _ClearAutoRepeatCmd()


            private bool _IsOutermostInputLoop
            {
                get { return this == m_host.m_topInputLoop; }
            }

            private bool _HaveInjectedCommands
            {
                get {  return (null != sm_injectedCommands) && (sm_injectedCommands.Count > 0); }
            }


            private static bool _StatusRequiresWait( DEBUG_STATUS execStatus )
            {
                switch( execStatus )
                {
                    case DEBUG_STATUS.BREAK:
                    case DEBUG_STATUS.NO_DEBUGGEE:
                    case DEBUG_STATUS.RESTART_REQUESTED:
                        return false;

                    case DEBUG_STATUS.GO:
                    case DEBUG_STATUS.STEP_OVER:
                    case DEBUG_STATUS.STEP_INTO:
                    case DEBUG_STATUS.STEP_BRANCH:
                    case DEBUG_STATUS.REVERSE_GO:
                    case DEBUG_STATUS.REVERSE_STEP_BRANCH:
                    case DEBUG_STATUS.REVERSE_STEP_OVER:
                    case DEBUG_STATUS.REVERSE_STEP_INTO:
                        return true;

                    case DEBUG_STATUS.OUT_OF_SYNC:
                    case DEBUG_STATUS.TIMEOUT:
                    case DEBUG_STATUS.WAIT_INPUT:
                        Util.Fail( Util.Sprintf( "How should I handle this status? {0}", execStatus ) );
                        return false;

                    case DEBUG_STATUS.GO_HANDLED:
                    case DEBUG_STATUS.GO_NOT_HANDLED:
                    case DEBUG_STATUS.IGNORE_EVENT:
                    case DEBUG_STATUS.NO_CHANGE:
                        Util.Fail( Util.Sprintf( "I don't think I should ever receive this status: {0}", execStatus ) );
                        return false;

                    default:
                        string msg = Util.Sprintf( "Unexpected DEBUG_STATUS: {0} ({1}).", execStatus, (int) execStatus );
                        Util.Fail( msg );
                        throw new Exception( msg );
                } // end switch( execStatus )
            } // end _StatusRequiresWait()


            public void Run()
            {
                StringBuilder sbCmd = new StringBuilder();

                while( !m_timeToStop && !m_host.ShouldExit )
                {
                    string input;
                    bool addToHistory;

                    if( _IsOutermostInputLoop && _HaveInjectedCommands )
                    {
                        // From "guest mode".
                        addToHistory = false;
                        input = sm_injectedCommands.Dequeue();
                    }
                    else
                    {
                        addToHistory = true;

                        string prompt;
                        if( 0 == sbCmd.Length )
                        {
                            prompt = m_host._GetPrompt( m_shell );
                        }
                        else
                        {
                            prompt = "\u009b97m>>\u009bm ";
                        }

                        m_host.UI.Write( prompt );

                        try
                        {
                            input = m_host.m_ui.ReadLineWithTabCompletion( true );
                        }
                        catch( PipelineStoppedException ) // thrown if calling user-defined tab completion (TODO: except we don't actually ever call user-defined tab completion yet)
                        {
                            sbCmd.Clear();
                            _ClearAutoRepeatCmd();
                            continue;
                        }
                    }

                    if( null == input )
                    {
                        // It returns null if input was canceled via CTRL-C.
                        //
                        // (and it's a good thing--if we tried to use a CtrlCInterceptor
                        // to cancel multi-line input, it wouldn't work well, because the
                        // CTRL-C thread races with the input thread)
                        sbCmd.Clear();
                        _ClearAutoRepeatCmd();
                        m_host.m_ui.WriteLine();
                        continue;
                    }

                    if( String.IsNullOrWhiteSpace( input ) )
                    {
                        if( sbCmd.Length != 0 )
                        {
                            sbCmd.Append( input ); // let's keep the whitespace for looks.
                            sbCmd.AppendLine();
                            continue;
                        }
                        else
                        {
                            if( 0 == input.Length )
                            {
                                //
                                // Someone just hit [Enter].
                                //
                                // We'll retrieve the DbgAutoRepeatCommand command.
                                //

                                if( !m_shell.IsNested )
                                {
                                    string autoRepeatCmd = m_shell.Runspace.SessionStateProxy.PSVariable.GetValue(
                                                                c_DbgAutoRepeatCmd,
                                                                null ) as string;
                                    m_shell.Runspace.SessionStateProxy.PSVariable.Set( c_DbgAutoRepeatCmd, null );
                                    sbCmd.Append( autoRepeatCmd );
                                }
                                else
                                {
                                    // Unbelievably, you cannot access session state via the Runspace.SessionStateProxy
                                    // (doing so will yield a PSInvalidOperationException: "A pipeline is already
                                    // running. Concurrent SessionStateProxy method call is not allowed.", whether you
                                    // access it from the pipeline execution thread or the main thread (that owns the
                                    // shell). So we'll get around that by just running some script to get and set the
                                    // variable.
                                    var results = m_host._ExecScriptNoExceptionHandling( m_shell,
                                                                                         Util.Sprintf( "Get-Variable {0}",
                                                                                                       c_DbgAutoRepeatCmd ),
                                                                                         false,
                                                                                         false );
                                    if( (null != results) &&
                                        (results.Count > 0) &&
                                        (results[ 0 ].BaseObject is PSVariable) )
                                    {
                                        PSVariable psvar = (PSVariable) results[ 0 ].BaseObject;
                                        if( psvar.Value is string )
                                            sbCmd.Append( (string) psvar.Value );
                                    }
                                    m_host._ExecScriptNoExceptionHandling( m_shell,
                                                                           Util.Sprintf( "Set-Variable {0} $null",
                                                                                         c_DbgAutoRepeatCmd ),
                                                                           false,
                                                                           false );
                                } // end else( nested shell )

                                if( 0 == sbCmd.Length )
                                    continue;
                            }
                            else
                            {   // input.Length != 0
                                //
                                // This is for the type-space-then-enter scenario, which
                                // windbg users use to clear the auto-run command.
                                //
                                _ClearAutoRepeatCmd();
                                continue;
                            }
                        } // end else( no multiline input )
                    } // end if( current input is empty )
#if DEBUG
                    else if( 0 == Util.Strcmp_OI( "Test-CaStringUtil", input ) )
                    {
                        CaStringUtil.SelfTest();
                        continue;
                    }
                    else if( 0 == Util.Strcmp_OI( "Test-MassageTypeName", input ) )
                    {
                        Util.TestMassageTypeName();
                        continue;
                    }
#endif
                    else
                    {
                        _ClearAutoRepeatCmd();
                        sbCmd.Append( input );
                    }

                    try
                    {
                        using( _CreateTryStopShellCtrlHandler( m_shell ) )
                        {
                            LogManager.Trace( "Attempting to execute input: {0}", sbCmd.ToString() );
                            m_host._ExecScriptNoExceptionHandling( m_shell,
                                                                   sbCmd.ToString(),
                                                                   addToHistory );
                            sbCmd.Clear();

                            //
                            // Now that the command has been executed, we'll discard any
                            // leftover progress stuff.
                            //

                            m_host.m_ui.ResetProgress();

                            if( !m_timeToStop && !m_host.ShouldExit )
                            {
                                try
                                {
                                    // If we make it back to here and the execution status is
                                    // "go", then we need to wait.
                                    //
                                    // This can happen, for instance, when DbgShell.exe is the
                                    // host (we are not in guest mode), and a breakpoint
                                    // command runs a !dbgshell command which resumes
                                    // execution. Resuming execution from the bp cmd will use
                                    // "exit" to terminate the bp cmd, and we'll end up back
                                    // here. So rather than just show another prompt, we need
                                    // to actually do the wait here.
                                    //
                                    // In the guest mode case, the "exit" causes us to
                                    // passivate, returning control to windbg (or whoever the
                                    // host is), and the host performs the wait.

                                    var execStatus = DbgEngDebugger._GlobalDebugger.GetExecutionStatus();
                                    bool statusRequiresWait = _StatusRequiresWait( execStatus );

                                    while( statusRequiresWait )
                                    {
                                        LogManager.Trace( "InputLoop.Run: current status requires we wait: {0}", execStatus );

                                        // Rather than just calling
                                        // DbgEngDebugger._GlobalDebugger.WaitForEventAsync(),
                                        // we will call a cmdlet (Resume-Process) to do the
                                        // wait for us. This will set up a pipeline so that
                                        // events can be reported, a CTRL-C handler, etc.

                                        m_host._ExecScriptNoExceptionHandling( m_shell,
                                                                               "Resume-Process -ResumeType Passive",
                                                                               addToHistory: false );

                                        execStatus = DbgEngDebugger._GlobalDebugger.GetExecutionStatus();
                                        statusRequiresWait = _StatusRequiresWait( execStatus );
                                    }
                                }
                                catch( DbgEngException dee )
                                {
                                    m_host._ReportException( dee, m_shell ); // <-- this may be a nested shell
                                }
                            } // end if( !m_timeToStop && !m_host.ShouldExit )
                        } // end using( _CreateTryStopShellCtrlHandler( m_shell ) )
                    }
                    catch( IncompleteParseException )
                    {
                        // We'll just keep appending to sbCmd.
                        sbCmd.AppendLine();
                    }
                    catch( PipelineStoppedException )
                    {
                        sbCmd.Clear();
                        // ignore
                    }
                    catch( RuntimeException rte )
                    {
                        sbCmd.Clear();
                        m_host._ReportException( rte, m_shell ); // <-- this may be a nested shell
                    }
                    catch( Exception e )
                    {
                        Util.Fail( Util.Sprintf( "What error is this? {0}", e ) );
                        sbCmd.Clear();
                    }
                } // end while( !m_timeToStop && !m_host.m_shouldExit )
            } // end Run()

            public void Break()
            {
                m_timeToStop = true;
            } // end Break()

            public void Dispose()
            {
                if( null != m_ctrlCInterceptor )
                {
                    m_ctrlCInterceptor.Dispose();
                    m_ctrlCInterceptor = null;
                }
                if( null != m_shell )
                {
                    m_shell.Dispose();
                    m_shell = null;
                }
            } // end Dispose()


            internal void EnterNestedPrompt( ref InputLoop currentLoop )
            {
                PowerShell nestedShell = null;
                var gate = new object();
                lock( gate )
                {
                    if( (null == m_shell) ||
                        (m_shell.InvocationStateInfo.State == PSInvocationState.NotStarted) )
                    {
                        // Trying to call m_shell.CreateNestedPowerShell will fail
                        // here with a PSInvalidOperationException, because it
                        // doesn't make sense to "nest" when it's not even running
                        // in the first place.
                        //
                        // Hopefully we don't have any more ways we can get into
                        // this situation, but if we do, let's catch it.
                        Util.Fail( "Can't nest if parent shell isn't running." );
                        throw new Exception( "Can't nest if parent shell isn't running." );
                    }

                    m_host.QueueActionToMainThreadDuringPipelineExecution( () =>
                        {
                            nestedShell = m_shell.CreateNestedPowerShell();
                            // N.B. It seems like this should not be necessary, but it is.
                            // Creating a nested shell will create a new runspace, which
                            // is essentially "one-use".  (If you keep the runspace it
                            // comes with, you can invoke the nested shell only
                            // once--subsequent invocations will complain that the
                            // pipeline isn't running anymore.)
                            nestedShell.Runspace = m_host.Runspace;
                            lock( gate ) { Monitor.PulseAll( gate ); }
                        } );
                    Monitor.Wait( gate );
                } // end lock( gate )

                // This call should come in on the pipeline execution thread. We must keep
                // the pipeline exeuction thread alive while the nested shell is alive...
                // we'll run the new input loop right here.
                //
                // TODO: BUGBUG: What if we're NOT on the pipeline execution thread? In
                // particular, what if we're on the CTRL-BREAK thread?
                using( InputLoop newLoop = new InputLoop( m_host, nestedShell ) )
                {
                    currentLoop = newLoop;
                    newLoop.Run();
                    currentLoop = this;
                }
            } // end InputLoop.EnterNestedPrompt()
        } // end class InputLoop


        private InputLoop m_currentInputLoop;

        // For use by guest mode, to inject commands when re-entered from the debugger extension.
        private InputLoop m_topInputLoop;


        public override void EnterNestedPrompt()
        {
            m_currentInputLoop.EnterNestedPrompt( ref m_currentInputLoop );
        } // end EnterNestedPrompt()

        public override void ExitNestedPrompt()
        {
            m_currentInputLoop.Break();
        } // end ExitNestedPrompt()

        private Guid m_instanceId = Guid.NewGuid();

        public override Guid InstanceId
        {
            // Is this supposed to be constant (tied to the class) or tied to the instance?
            get { return m_instanceId; }
        }

        public override string Name
        {
            get { return Util.GetGenericTypeName( this ); }
        }

        public override void NotifyBeginApplication()
        {
            //throw new NotImplementedException();
        }

        public override void NotifyEndApplication()
        {
            //throw new NotImplementedException();
        }

        public bool ShouldExit { get; private set; }


        public int ExitCode { get; private set; }


        public override void SetShouldExit( int exitCode )
        {
            LogManager.Trace( "In SetShouldExit( {0} ), Guest mode? {1}", exitCode, DbgProvider.IsInGuestMode );
            if( !DbgProvider.IsInGuestMode )
            {
                if( DbgProvider.EntryDepth == 1 )
                {
                    if( DbgProvider.DontExitAllTheWay )
                    {
                        DbgProvider.DontExitAllTheWay = false;
                        LogManager.Trace( "DontExitAllTheWay: Eating 'exit'." );
                    }
                    else
                    {
                        ExitCode = exitCode;
                        ShouldExit = true;
                    }
                }
                else
                {
                    Util.Assert( DbgProvider.EntryDepth > 1 );
                    // We're not in guest mode, but EntryDepth is > 1... so that means
                    // that an invocation of !dbgshell wanted to exit. By the time we get
                    // to here, we've already exited enough for that; we don't need to do
                    // anything else.
                    LogManager.Trace( "SetShouldExit: not in guest mode, but EntryDepth > 1 ({0})--not exiting.",
                                      DbgProvider.EntryDepth );
                }
            }
            else
            {
                if( DbgProvider.GuestModeTimeToExitAllTheWay )
                {
                    // Someone clicked the red 'x'.
                    LogManager.Trace( "Rude exit: skipping Wait-ForBangDbgShellCommand." );
                }
                else
                {
                    //
                    // We're in "guest mode" and the PS runtime is telling us that somebody
                    // ran "exit" (or "q"). Normally that means that the process should go
                    // away, but in "guest mode" (where we are hosted by a debugger extension)
                    // that just means we need to return control back to the debugger (windbg
                    // or one of its friends) (from the debugger extension).
                    //
                    // Unlike process exit, we don't want to tear everything down... we want
                    // to keep stuff "alive" so that the next time somebody runs "!dbgshell",
                    // they can pick up right where they left off before. So what we do when
                    // somebody runs "exit" is to sort of suspend ourselves by running a
                    // special command (Wait-ForBangDbgShellCommand), which blocks until the
                    // next time somebody runs the debugger extension. (In other words, we
                    // "eat" the "exit".)
                    //
                    // Using that command to block instead of, for example, just dumbly
                    // blocking on an event right here allows us to have a live pipeline
                    // while we are dormant, which allows us to properly handle CTRL-C and
                    // spit out notifications (like modload events).
                    //

                    // TODO: Should we do anything with the exitCode? We could let the
                    // debugger extension use it as its return code, I suppose.


                    // TODO: Is this necessary? (checking the runspace) I thought I ran into a
                    // case where I needed to, but I think that whatever caused that situation
                    // to arise went away.
                    PowerShell shell;
                    if( m_runspace.RunspaceAvailability == RunspaceAvailability.Busy )
                    {
                        //Console.WriteLine( "runspace is busy" );
                        shell = PowerShell.Create( RunspaceMode.CurrentRunspace );
                    }
                    else
                    {
                        //Console.WriteLine( "runspace is not busy: {0}", m_runspace.RunspaceAvailability );
                        shell = PowerShell.Create();
                        shell.Runspace = m_runspace;
                    }
                    //Console.WriteLine( "runspace state info: {0}, {1}", m_runspace.RunspaceStateInfo.State, m_runspace.RunspaceStateInfo.Reason );

                    using( shell )
                    {
                        // Wait-ForBangDbgShellCommand will call
                        // DbgProvider.GuestModePassivate().
                        LogManager.Trace( "Running Wait-ForBangDbgShellCommand" );
                        this._ExecScriptNoExceptionHandling( shell,
                                                             "Wait-ForBangDbgShellCommand",
                                                             false ); // addToHistory
                        LogManager.Trace( "Finished Wait-ForBangDbgShellCommand" );
                    }
                } // end else( !rude exit )

                DbgProvider.GuestModeEvent.Reset();

                LogManager.Trace( "host: reset GuestModeEvent" );

                //
                // When we resume here, we've been re-entered.
                //
                // It might be because somebody ran "!dbgshell", in which case we can just
                // pick up where we left off.
                //
                // OR it could be that somebody ran ".unload DbgShellExt", and it's time
                // to clean up and exit.
                //
                if( DbgProvider.GuestModeTimeToExitAllTheWay )
                {
                    //
                    // Somebody unloaded the extension (".unload DbgShellExt").
                    //
                    LogManager.Trace( "host: time to really exit" );
                    ExitCode = exitCode;
                    ShouldExit = true;

                    // In case of rude exit (red 'x'), it would be nice to see these last
                    // few trace messages. Note that in such a case we're on the ctrl-c
                    // thread, but there isn't much point in trying to coordinate with
                    // any other thread--if we try to do too much the close signal will
                    // just time out and we'll get whacked.
                    LogManager.Flush();
                }
                else
                {
                    LogManager.Trace( "host: we're just resuming--not time to exit all the way yet" );
                    //
                    // We do not need to take care of updating dbgeng
                    // state--Wait-ForBangDbgShellCommand did that.
                    //
                }
            } // end else( guest mode )
        } // end SetShouldExit()


        private ColorHostUserInterface m_ui;

        public override PSHostUserInterface UI
        {
            get { return m_ui; }
        }

        public override Version Version
        {
            // TODO: get actual version
            get { return new Version( 1, 0, 0, 0 ); }
        }


        private RunspaceConfiguration m_runspaceConfig;

        // This interceptor functions as the "backstop" that prevents CTRL-C from killing
        // the whole app if you press it while just sitting at a prompt.
        private CtrlCInterceptor m_ctrlCBackstop;

        public ColorConsoleHost( RunspaceConfiguration runspaceConfig,
                                 string banner,
                                 string[] args )
        {
            m_runspaceConfig = runspaceConfig;
            m_ctrlCBackstop = new CtrlCInterceptor( _CtrlCBackstopHandler, true );
            m_ui = new ColorHostUserInterface( this );

            // TODO: parse args

            ConsoleTextWriter = new ConsoleTextWriter( m_ui );

         // // If not -nologo, show banner
         // if( !String.IsNullOrEmpty( banner ) )
         //     m_ui.WriteLine( banner );

         // m_ui.WriteLine();
        } // end constructor

        public void Dispose()
        {
            if( null != m_ctrlCBackstop )
            {
                m_ctrlCBackstop.Dispose();
                m_ctrlCBackstop = null;
            }
        } // end Dispose()


        private bool _CtrlCBackstopHandler( ConsoleBreakSignal ctrlType )
        {
            if( ConsoleBreakSignal.Close == ctrlType )
            {
                LogManager.Trace( "Somebody clicked the red 'X'; preparing to exit." );

                //
                // Even though we return 'true' from this function to indicate that we've
                // handled the signal, that won't stop us from going down. Might as well
                // try to go cleanly... prepare to exit all the way, even if we're in
                // guest mode.
                //
                // I wish I could handle it, because in guest mode it might be nice to
                // de-alloc our console and leave windbg up. But then again, maybe the
                // host isn't windbg (could be ntsd, etc.). Oh well.
                //
                // (We /really/ can't block the signal--even if we were to sleep here, the
                // system would get impatient and whack us.)
                //

                DbgProvider.GuestModeTimeToExitAllTheWay = true;
                SetShouldExit( 0 );
            }

            // We're just keeping the CTRL-C from hitting the default OS handler.
            return true;
        } // end _CtrlCBackstopHandler()


        private static void _TryStopShell( PowerShell shell )
        {
            try
            {
                shell.Stop();
            }
            catch( RuntimeException ) { /* ignore */ }
            catch( PSInvalidOperationException ) { /* ignore */ }
            catch( Exception e )
            {
                Util.FailFast( Util.Sprintf( "Unexpected exception while trying to stop a shell: {0}", e ), e );
            }
        } // end _TryStopShell()


        private void _ReportException( Exception e )
        {
            _ReportException( e, null );
        }


        private static IDisposable _CreateTryStopShellCtrlHandler( PowerShell shell )
        {
            // Don't block the CTRL-C thread while calling _TryStopShell, because we're
            // holding a CTRL-C lock, and something which runs during shell cancellation
            // might try to install their own CTRL-C handler (which would deadlock us if
            // we were blocking this thread) (for example, ".kill").
            HandlerRoutine ctrlRoutine = (x) =>
                {
                    ThreadPool.QueueUserWorkItem(
                        ( s ) => _TryStopShell( (PowerShell) s ), shell );
                    return true;
                };
            return new CtrlCInterceptor( ctrlRoutine );
        } // end _CreateTryStopShellCtrlHandler()


        /// <summary>
        ///    To display an exception using the display formatter, run a second pipeline
        ///    passing in the error record.  The runtime will bind this to the $input
        ///    variable, which is why $input is being piped to the Out-String cmdlet. The
        ///    WriteErrorLine method is called to make sure the error gets displayed in
        ///    the correct error color.
        /// </summary>
        /// <param name="e">
        ///    The exception to display.
        /// </param>
        /// <param name="shell">
        ///    The runspace to use. We have to allow passing this in for the nested prompt
        ///    case. Otherwise a new shell is created using the current runspace
        ///    (m_runspace).
        /// </param>
        private void _ReportException( Exception e, PowerShell shell )
        {
            Util.Assert( null != e );
            ErrorRecord er;
            IContainsErrorRecord icer = e as IContainsErrorRecord;
            if( icer != null )
            {
                er = icer.ErrorRecord;
            }
            else
            {
                er = new ErrorRecord( e,
                                      "Host._ReportException_" + Util.GetGenericTypeName( e ),
                                      ErrorCategory.NotSpecified,
                                      null );
            }

            PSObject wrappedError = new PSObject( er );
            PSNoteProperty note = new PSNoteProperty( "writeErrorStream", true );
            wrappedError.Properties.Add( note );

            // The "real" ConsoleHost sends it to a Pipeline. What's the difference?

            using( var disposer = new ExceptionGuard() )
            {
                if( null == shell )
                {
                    shell = disposer.Protect( PowerShell.Create() );
                }
                else
                {
                    shell.Commands.Clear();
                    shell.Streams.ClearStreams();
                }
                disposer.Protect( _CreateTryStopShellCtrlHandler( shell ) );

                shell.AddScript( "$input" ).AddCommand( "Out-Default" );

                // Do not merge errors, this function will swallow errors.
                Collection< PSObject > result;
                PSDataCollection< object > inputCollection = new PSDataCollection< object >();
                inputCollection.Add( er );
                inputCollection.Complete();
                // N.B. We do not handle RuntimeExceptions thrown from here, because there should
                // not be any. If there are, it indicates a bug in our formatting system, which is
                // a serious bug, and a crash is desired.
                result = shell.Invoke( inputCollection );

                // Out-Default should have already written the error to the host's output.
                Util.Assert( 0 == result.Count );
                Util.Assert( 0 == shell.Streams.Error.Count );
                Util.Assert( !shell.HadErrors || (e is PipelineStoppedException) ); // don't know why PipelineStoppedException causes HadErrors to be set to true when the error count is 0.
                if( shell.HadErrors && !(e is PipelineStoppedException) )
                {
                    // I saw the assert above fire once, but neglected to debug it, and it
                    // doesn't seem to repro easily (it involved banging on CTRL-C when a
                    // bunch of doomed commands had been pasted in).
                    LogManager.Trace( "e is a: {0}", Util.GetGenericTypeName( e ) );
                    Util.Fail( "No really; check this, danthom." );
                }
                if( result.Count > 0 )
                {
                    string str = result[ 0 ].BaseObject as string;
                    if( !String.IsNullOrEmpty( str ) )
                    {
                        // Remove \r\n, which is added by the Out-String cmdlet.
                        UI.WriteErrorLine( str.Substring( 0, str.Length - 2 ) );
                    }
                }
            } // end using( disposer (ctrlC, maybe shell) )
        } // end _ReportException()


        private class CrossThreadMessagePump
        {
            // This queue is used by other threads to queue actions that need to be run on
            // the pipeline thread.
            private BlockingCollection< Action > m_q = new BlockingCollection< Action >();


            public void QueueAction( Action action )
            {
                m_q.Add( action );
            }

            public void SignalDone()
            {
                m_q.CompleteAdding();
            }

            public void ProcessActions()
            {
                foreach( Action action in m_q.GetConsumingEnumerable() )
                {
                    action();
                }
            } // end ProcessActions()
        } // end class CrossThreadMessagePump

        private CrossThreadMessagePump m_msgPump;


        internal void QueueActionToMainThreadDuringPipelineExecution( Action action )
        {
            if( null == action )
                throw new ArgumentNullException( "action" );

            if( null == m_msgPump )
                throw new InvalidOperationException( "No pipeline is running." );

            m_msgPump.QueueAction( action );
        } // end QueueActionToMainThreadDuringPipelineExecution()


        /// <summary>
        ///    Executes the script using the specified shell object, merging output and
        ///    errors, and sending to Out-Default. Does no exception handling or CTRL-C
        ///    interception.
        /// </summary>
        /// <remarks>
        ///    This method returns "void" because it adds "Out-Default" to the pipeline,
        ///    and it should be impossible to get any output in that case.
        /// </remarks>
        private void _ExecScriptNoExceptionHandling( PowerShell shell,
                                                     string script,
                                                     bool addToHistory )
        {
            _ExecScriptNoExceptionHandling( shell, script, addToHistory, true );
        }

        /// <summary>
        ///    Executes the script using the specified shell object, optionally merging
        ///    output and errors and sending to Out-Default. Does no exception handling or
        ///    CTRL-C interception.
        /// </summary>
        // Kind of annoying that synchronous and asynchronous invocation result in
        // different return types. Fortunately they both implement IList< PSObject >.
        private IList< PSObject > _ExecScriptNoExceptionHandling( PowerShell shell,
                                                                  string script,
                                                                  bool addToHistory,
                                                                  bool sendToOutDefault )
        {
            return _ExecScriptNoExceptionHandling( shell,
                                                   script,
                                                   null,
                                                   addToHistory,
                                                   sendToOutDefault );
        }


        /// <summary>
        ///    Executes the script using the specified shell object, optionally merging
        ///    output and errors and sending to Out-Default. Does no exception handling or
        ///    CTRL-C interception.
        /// </summary>
        // Kind of annoying that synchronous and asynchronous invocation result in
        // different return types. Fortunately they both implement IList< PSObject >.
        private IList< PSObject > _ExecScriptNoExceptionHandling( PowerShell shell,
                                                                  string script,
                                                                  object[] args,
                                                                  bool addToHistory,
                                                                  bool sendToOutDefault )
        {
            if( !String.IsNullOrEmpty( script ) )
            {
                shell.Commands.Clear();
                shell.Streams.ClearStreams();
                shell.AddScript( script );

                if( null != args )
                {
                    foreach( var arg in args )
                    {
                        shell.AddArgument( arg );
                    }
                }
            }
            // else we assume the shell has already been prepped and loaded with commands.

            // Add the default outputter to the end of the pipe and then call the
            // MergeMyResults method to merge the output and error streams from the
            // pipeline. This will result in the output being written using the PSHost and
            // PSHostUserInterface classes instead of returning objects to the host
            // application.
            if( sendToOutDefault )
            {
                shell.AddCommand( "Out-Default" );
                shell.Commands.Commands[ 0 ].MergeMyResults( PipelineResultTypes.Error,
                                                             PipelineResultTypes.Output );
            }
            var psis = new PSInvocationSettings();
            psis.AddToHistory = addToHistory;

            // The stock host always re-sets the runspace, but I think that's only necessary if
            // you are actually changing the runspace... maybe for that runspace push/pop thing,
            // whatever that is, which I'm not doing right now.
            //shell.Runspace = m_runspace;
            Util.Assert( shell.Runspace == m_runspace );

            IList< PSObject > results;
            if( shell.IsNested )
            {
                Util.Assert( null != m_msgPump );
                // assert( we are on pipeline execution thread )
                // A nested shell will throw if invoked asynchronously.
                results = shell.Invoke( null, psis );
            }
            else
            {
                Util.Assert( null == m_msgPump );
                // TODO: don't create a new one every time.
                m_msgPump = new CrossThreadMessagePump();
                AsyncCallback callback = new AsyncCallback( (x) => m_msgPump.SignalDone() );
                IAsyncResult iar = shell.BeginInvoke< PSObject >( null, psis, callback, null );
                m_msgPump.ProcessActions();
                m_msgPump = null;
                results = shell.EndInvoke( iar );
            }
            Util.Assert( !sendToOutDefault ||
                         ((results == null) || (0 == results.Count)) );
            return results;
        } // end _ExecScriptNoExceptionHandling()


        /// <summary>
        ///    Deep in some code path, someone might want to do something like
        ///    "Write-Warning 'Could not load DAC for CLR version blah.'", but a) they
        ///    don't have access to a cmdlet or an IPipelineCallback, and b) even if they
        ///    did, it might not be a good time to write a warning (perhaps we're in the
        ///    middle of table formatting or something).
        ///
        ///    So DbgProvider allows them to queue a little snippet of script to be
        ///    executed before the next prompt. And here's where we execute those little
        ///    bits of code.
        /// </summary>
        private void _DoExecuteBeforeNextPromptStuff( PowerShell shell )
        {
            while( DbgProvider.StuffToExecuteBeforeNextPrompt.Count > 0 )
            {
                var scriptAndArgs = DbgProvider.StuffToExecuteBeforeNextPrompt.Dequeue();

                try
                {
                    _ExecScriptNoExceptionHandling( shell,
                                                    scriptAndArgs.Script,
                                                    scriptAndArgs.Args,
                                                    addToHistory: false,
                                                    sendToOutDefault: true );
                }
                catch( PipelineStoppedException )
                {
                    // Ignore it. This can happen if you CTRL-C at just the right time.
                }
                catch( RuntimeException rte )
                {
                    Util.Fail( "Bad script?" ); // Not an invariant; just to help catch mistakes.
                    _ReportException( rte, shell );
                }
            } // end while( more stuff )
        } // end _DoExecuteBeforeNextPromptStuff()


        private string _GetPrompt( PowerShell shell ) // like InputLoop.EvaluatePrompt
        {
            _DoExecuteBeforeNextPromptStuff( shell );

            try
            {
                IList< PSObject > results = _ExecScriptNoExceptionHandling( shell,
                                                                            "prompt",
                                                                            addToHistory:false,
                                                                            sendToOutDefault: false );
                // TODO: allow a ColorString prompt
                if( (null != results) && (results.Count > 0) && (results[ 0 ].BaseObject is string) )
                {
                    // Can't be null since we checked the type. I'll allow
                    // empty/whitespace prompt; why not?
                    // Color is [bright] white, followed by a "reset":
                    return "\u009b97m" + (string) results[ 0 ].BaseObject + "\u009bm";
                }
            }
            catch( PipelineStoppedException )
            {
                // Ignore it. This can happen if you CTRL-C at just the right time.
            }
            catch( RuntimeException rte )
            {
                // Hmmm... could be expensive if there is no prompt function...
                Util.Fail( "Bad prompt function?" ); // Not an invariant; just to help catch mistakes.
                LogManager.Trace( "Ignoring RTE from prompt function: {0}", rte );
            }

            // Color is [bright] white, followed by a "reset":
            return "\u009b97mPS>\u009bm";
            // TODO: Check for "pushed" runspace, do "remote" prompt?
        } // end _GetPrompt()



        internal static void GuestModeInjectCommand( string command )
        {
            if( (null == command) || (0 == command.Length) )
                return;

            InputLoop.QueueInjectedCommand( command );
        } // end GuestModeInjectCommand()


        private Runspace m_runspace;

        public int Run( CommandLineParameterParser cpp )
        {
            int rc = 0;
            // The PS console host creates the runspace and does the input loop on a
            // different thread. Will it mess anything up if I don't?
            using( var runspace = RunspaceFactory.CreateRunspace( this, m_runspaceConfig ) )
            {
                runspace.Open();
                m_runspace = runspace;

                // We want to create a shell to use during runspace initialization so that
                // we can use $host.EnterNestedPrompt() to help debug those scripts, too.
                using( var shell = PowerShell.Create() )
                {
                    shell.Runspace = runspace;
                    using( InputLoop inputLoop = new InputLoop( this, shell ) )
                    {
                        m_currentInputLoop = inputLoop;
                        m_topInputLoop = inputLoop;

                        bool oneShot = (!String.IsNullOrEmpty( cpp.File ) ||
                                        !String.IsNullOrEmpty( cpp.InitialCommand )) && !cpp.NoExit;

                        // TODO: Apparently, even though I gave the runspace config to the
                        // CreateRunspace method, it seems /I/ am responsible for loading
                        // profiles, running initial config scripts, etc. Ugh.

                        _DoRunspaceInitialization( cpp.SkipProfiles,
                                                   cpp.DumpFile,
                                                   cpp.InitialCommand,
                                                   cpp.File,
                                                   cpp.Args,
                                                   oneShot,
                                                   shell );

                        if( oneShot )
                        {
                            // Re-using the main command-line parsing logic would be
                            //
                            //   a) way hairy,
                            //   b) way overkill (we don't need a bunch of those options
                            //      for the debugger extension)
                            //   c) non-cromulant (the options don't make sense for guest
                            //      mode)
                            //
                            // Thus guest-mode argument parsing is handled completely
                            // separately. (and so we can't have gotten into a oneShot
                            // situation when in guest mode)
                            Util.Assert( !DbgProvider.IsInGuestMode );

                            // ExitCode, if non-zero, should have been set by
                            // _DoRunspaceInitialization. (or even by the script itself,
                            // if it ended with an "exit" statement).
                            ShouldExit = true;
                        }
                        else
                        {
                            ExitCode = 0;
                            inputLoop.Run();
                        }
                    } // end using( inputLoop )
                } // end using( shell )
                m_runspace = null;
                rc = ExitCode;
            } // end using( runspace )

            return rc;
        } // end Run()


        internal static int Start( RunspaceConfiguration configuration,
                                   string bannerText,
                                   string helpText, // TODO: what to do with this?
                                   string[] args )
        {
            int result = 0;
            Thread.CurrentThread.Name = "ColorConsoleHost main thread";
            // Hm, I can't easily copy this functionality...
            //PSHost.IsStdOutputRedirected = ConsoleHost.theConsoleHost.IsStandardOutputRedirected;
            if( args == null )
            {
                args = new string[ 0 ];
            }
            try
            {
                using( var host = new ColorConsoleHost( configuration, bannerText, args ) )
                {
                    var cpp = new CommandLineParameterParser( host, bannerText, helpText );
                    cpp.Parse( args );
                    // -NonInteractive:
                    ((ColorHostUserInterface) (host.UI)).ThrowOnReadAndPrompt = cpp.ThrowOnReadAndPrompt;

                    if( cpp.AbortStartup )
                        result = unchecked( (int) cpp.ExitCode );
                    else
                        result = host.Run( cpp );
                } // end using( host )
            }
            catch( DbgProviderException dpe ) // TODO: different exception type?
            {
                LogManager.Trace( "Host construction or run failed: {0}", dpe );
                result = dpe.HResult;
            }
            LogManager.Trace( "ColorConsoleHost.Start returning: {0}", Util.FormatErrorCode( result ) );
            return result;
        } // end Start()

        // TODO: I don't understand what this is for or how it should be used... It seems
        // like it should have something to do with a stack, but the PS host doesn't use a
        // stack...
        #region IHostSupportsInteractiveSession Members

        public bool IsRunspacePushed
        {
            get { throw new NotImplementedException(); }
        }

        public void PopRunspace()
        {
            throw new NotImplementedException();
        }

        public void PushRunspace( Runspace runspace )
        {
            throw new NotImplementedException();
        }

        public Runspace Runspace
        {
            get { return m_runspace; }
        }

        #endregion

        // TODO TODO TODO: Test error conditions (what if init scripts fail, etc.).
        private void _DoRunspaceInitialization( bool skipProfiles,
                                                string dumpFile,
                                                string initialCommand,
                                                string file,
                                                Collection< CommandParameter > fileArgs,
                                                bool oneShot,
                                                PowerShell shell )
        {
            DbgProvider.HostSupportsColor = true;
            m_runspace.SessionStateProxy.PSVariable.Set( DbgProvider.GetHostSupportsColorPSVar() );

            // Run the built-in scripts
            RunspaceConfigurationEntryCollection< ScriptConfigurationEntry > scripts = null;
            if (m_runspaceConfig != null)
                scripts = m_runspaceConfig.InitializationScripts;

            if( (scripts == null) || (scripts.Count == 0) )
            {
                LogManager.Trace("There are no built-in scripts to run");
            }
            else
            {
                foreach( ScriptConfigurationEntry s in scripts )
                {
                    if( oneShot && (s.Name.StartsWith( c_SkipForOneShotPrefix, StringComparison.OrdinalIgnoreCase )) )
                    {
                        LogManager.Trace( "Skipping script because we're in \"one shot\" mode: {0}", s.Name );
                        continue;
                    }
                    LogManager.Trace( "Running script: '{0}'", s.Name );

                    // [danthom] TODO BUGBUG
                    // spec claims that Ctrl-C is not supposed to stop these.

                    // TODO: I think I shouldn't even need this... top-level handler can handle ctrl-C, not do anything
                    //using( new CtrlCInterceptor( ( x ) => { return true; } ) )
                    try
                    {
                        _ExecScriptNoExceptionHandling( shell,
                                                        s.Definition,
                                                        addToHistory: false );
                    }
                    catch( RuntimeException rte )
                    {
                        _ReportException( rte, shell );
                        // TODO BUGBUG: the real PowerShell will set the exit code and
                        // exit the input loop if init fails. (throwing from here)
                    }
                } // end foreach( ScriptConfigurationEntry )
            } // end( we have some init scripts )

            // If -iss has been specified, then there won't be a runspace
            // configuration to get the shell ID from, so we'll use the default...
            string shellId = null;
            if (m_runspaceConfig != null)
                shellId = m_runspaceConfig.ShellId;
            else
                shellId = "Microsoft.PowerShell"; // TODO: what will happen for custom shells built using Make-Shell.exe

            string allUsersProfile = HostUtilities.GetFullProfileFileName( null, false );
            string allUsersHostSpecificProfile = HostUtilities.GetFullProfileFileName( shellId, false );
            string currentUserProfile = HostUtilities.GetFullProfileFileName( null, true );
            string currentUserHostSpecificProfile = HostUtilities.GetFullProfileFileName( shellId, true );
            string dbgShellProfile = HostUtilities.GetFullDbgShellProfileFileName();

            // $PROFILE has to be set from the host
            // Should be "per-user,host-specific profile.ps1"
            // This should be set even if -noprofile is specified
            Runspace.SessionStateProxy.SetVariable( "PROFILE",
                                                    HostUtilities.GetDollarProfile(
                                                        allUsersProfile,
                                                        allUsersHostSpecificProfile,
                                                        currentUserProfile,
                                                        currentUserHostSpecificProfile,
                                                        dbgShellProfile) );
            if( !skipProfiles )
            {
                // Run the profiles.
                // Profiles are run in the following order:
                // 1. host independent profile meant for all users
                // 2. host specific profile meant for all users
                // 3. host independent profile of the current user
                // 4. host specific profile  of the current user

                // TODO: Shouldn't these be using 'shell' instead of creating their own?
                // So that $host.EnterNestedPrompt() will work?
                _RunProfileFile( allUsersProfile );
                _RunProfileFile( allUsersHostSpecificProfile );
                _RunProfileFile( currentUserProfile );
                _RunProfileFile( currentUserHostSpecificProfile );
                _RunProfileFile( dbgShellProfile );
            }
            else
            {
                LogManager.Trace( "-noprofile option specified: skipping profiles." );
            }

            // TODO: this will be nice to have for automated debug monitor-type thingies
            // If a file was specified as the argument to run, then run it...
            if( !String.IsNullOrEmpty( file ) )
            {
                LogManager.Trace( "Running file: {0}", file );
                shell.Commands.Clear();
                shell.Streams.ClearStreams();
                shell.AddCommand( file );
                if( fileArgs != null )
                {
                    foreach( CommandParameter p in fileArgs )
                    {
                        shell.AddParameter( p.Name, p.Value );
                    }
                }

                try
                {
                    _ExecScriptNoExceptionHandling( shell,
                                                    null,
                                                    addToHistory: false );
                }
                catch( RuntimeException rte )
                {
                    _ReportException( rte, shell );
                    // This might not get used if -NoExit was passed.
                    ExitCode = rte.HResult;
                }
            }
            else
            {
                if( !String.IsNullOrEmpty( dumpFile ) )
                {
                    LogManager.Trace( "Mounting dump file specified on command line: {0}", dumpFile );

                    try
                    {
                        _ExecScriptNoExceptionHandling( shell,
                                                        Util.Sprintf( "Mount-DbgDumpFile '{0}'", dumpFile ),
                                                        addToHistory: false );
                    }
                    catch( RuntimeException rte )
                    {
                        _ReportException( rte, shell );
                     // // This might not get used if -NoExit was passed.
                     // ExitCode = rte.HResult;
                    }
                }

                if( !String.IsNullOrEmpty( initialCommand ) )
                {
                    // Run the command passed on the command line
                    LogManager.Trace( "Running initial command: {0}", initialCommand );

                    try
                    {
                        _ExecScriptNoExceptionHandling( shell,
                                                        initialCommand,
                                                        addToHistory: false );
                    }
                    catch( RuntimeException rte )
                    {
                        _ReportException( rte, shell );
                        // This might not get used if -NoExit was passed.
                        ExitCode = rte.HResult;
                    }
                }
            }
        } // end _DoRunspaceInitialization()


        private void _RunProfileFile( string profileFileName )
        {
            if( String.IsNullOrEmpty( profileFileName ) )
                return;

            if( !File.Exists( profileFileName ) )
            {
                LogManager.Trace( "Profile file does not exist: {0}", profileFileName );
                return;
            }

            try
            {
                LogManager.Trace( "Running profile file: {0}", profileFileName );
                using( PowerShell tmpShell = PowerShell.Create() )
                {
                    tmpShell.Runspace = m_runspace;
                    _ExecScriptNoExceptionHandling( tmpShell,
                                                    ". '" + EscapeSingleQuotes( profileFileName ) + "'",
                                                    addToHistory: false );
                }
            }
            catch( Exception e ) // PS source sez: "Catch-all OK, 3rd party callout"
            {
                _ReportException( e );
            }
        } // end _RunProfileFile()


        /// <summary>
        ///    Escapes backtick and tick characters with a backtick.
        /// </summary>
        internal static string EscapeSingleQuotes( string str )
        {
            // worst case we have to escape every character, so capacity is twice as large as input length
            StringBuilder sb = new StringBuilder(str.Length * 2);

            for (int i = 0; i < str.Length; ++i)
            {
                char c = str[i];
                if (c == '\'')
                {
                    sb.Append(c);
                }
                sb.Append(c);
            }

            string result = sb.ToString();
            //tracer.WriteLine(result);

            return result;
        } // end EscapeSingleQuotes()


        // TODO TODO TODO
        // TODO TODO TODO Properties required by the UI layers below us, need to actually implement logic for them.
        // TODO TODO TODO

        public bool IsStandardOutputRedirected { get; set; }

        public bool IsInteractive { get; set; }

        public TextWriter ConsoleTextWriter { get; set; }

        public TextWriter StandardOutputWriter { get; set; }

        //public bool IsRunningAsync { get; set; }

        public bool IsStandardErrorRedirected { get; set; }

        public TextWriter StandardErrorWriter { get; set; }

        public bool IsStandardInputRedirected { get; set; }

        public TextReader StandardInReader { get; set; }
    } // end class ColorConsoleHost
}
