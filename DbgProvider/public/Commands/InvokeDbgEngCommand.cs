using System.Management.Automation;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg.Commands
{
    // TODO: This class is one reason why we need "cd" to set the proper
    // debugger context, because we want to be able to invoke straight
    // dbgeng commands and have them work according to the right context.

    [Cmdlet( VerbsLifecycle.Invoke, "DbgEng" )]
    public class InvokeDbgEngCommand : ExecutionBaseCommand
    {
        // ValueFromRemainingArguments is convenient, but it also requires that the param
        // type be an array. (In script, it doesn't; but the parameter binder won't
        // convert to a single string for a C# cmdlet.)
        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromRemainingArguments = true,
                    ValueFromPipeline = true )]
        [ValidateNotNullOrEmpty]
        public string[] Command { get; set; }

        [Parameter( Mandatory = false )]
        [AllowEmptyString]
        public string OutputPrefix { get; set; }


        private void _ConsumeLine( string line )
        {
            SafeWriteObject( line, true ); // this will get sent to the pipeline thread
        }

        protected override bool TrySetDebuggerContext
        {
            get
            {
                return true;
            }
        }


        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            m_dontDisplayRegisters = true;

            if( null == OutputPrefix )
            {
                if( HostSupportsColor )
                    OutputPrefix = "\u009b34;106mDbgEng>\u009bm ";
                else
                    OutputPrefix = "DbgEng> ";
            }
            else if( (OutputPrefix.Length > 0) && HostSupportsColor )
            {
                OutputPrefix = "\u009b34;106m" + OutputPrefix + "\u009bm ";
            }
        } // end BeginProcessing()


        protected override void ProcessRecord()
        {
            var inputCallbacks = Debugger.GetInputCallbacks() as DebugInputCallbacks;
            if( null != inputCallbacks )
                inputCallbacks.UpdateCmdlet( this );

          //var outputCallbacks = Debugger.GetOutputCallbacks() as DebugOutputCallbacks;
          //if( null != outputCallbacks )
          //    outputCallbacks.UpdateCmdlet( this );

            using( var disposer = new ExceptionGuard() )
            {
                disposer.Protect( Debugger.SetCurrentCmdlet( this ) );
                disposer.Protect( InterceptCtrlC() );

                MsgLoop.Prepare();

                string actualCommand = string.Join( " ", Command );
                Task t = Debugger.InvokeDbgEngCommandAsync( actualCommand, OutputPrefix, _ConsumeLine );
                Task t2 = t.ContinueWith( async ( x ) =>
                {
                    DEBUG_STATUS execStatus = Debugger.GetExecutionStatus();
                    if( _StatusRequiresWait( execStatus ) )
                    {
                        LogManager.Trace( "InvokeDbgExtensionCommand: Current execution status ({0}) indicates that we should enter a wait.",
                                          execStatus );

                        await WaitForBreakAsync();
                    }

                    disposer.Dispose();
                    SignalDone();
                } ).Unwrap();

                MsgLoop.Run();

                Host.UI.WriteLine();
                Util.Await( t ); // in case it threw
                Util.Await( t2 ); // in case it threw
            } // end using( disposer )
        } // end ProcessRecord()
    } // end class InvokeDbgEngCommand
}
