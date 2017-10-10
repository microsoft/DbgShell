using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;


namespace MS.Dbg.Commands
{
    /// <summary>
    ///    A base class for DbgProvider cmdlets.
    /// </summary>
    public abstract partial class DbgBaseCommand : PSCmdlet, IDisposable
    {
        private volatile int m_iDisposed;
        protected bool Disposed { get { return m_iDisposed != 0; } }

        private MessageLoop m_msgLoop;

        protected MessageLoop MsgLoop { get { return m_msgLoop; } }

        protected CancellationTokenSource CancelTS { get; private set; }
        private Thread m_pipelineThread;
        private DbgEngDebugger __debugger;

        // Throws 'NoTarget' if it can't get a debugger.
        protected DbgEngDebugger Debugger
        {
            [DebuggerStepThrough]
            get
            {
                if( null == __debugger )
                {
                    PathInfo pi = this.CurrentProviderLocation( DbgProvider.ProviderId );
                    __debugger = DbgProvider.GetDebugger( pi.ProviderPath );
                }
                return __debugger;
            }
            set
            {
                __debugger = value;
            }
        } // end property Debugger


        protected DbgBaseCommand()
        {
            CancelTS = new CancellationTokenSource();
            m_msgLoop = new MessageLoop( this );
        }


        protected void CheckForCancellation()
        {
            CancelTS.Token.ThrowIfCancellationRequested();
        } // end CheckForCancellation()


        public void Dispose()
        {
            GC.SuppressFinalize( this );
            Dispose( true );
        }

        protected virtual void Dispose( bool disposing )
        {
            if( disposing )
            {
                int alreadDisposed = Interlocked.Exchange( ref m_iDisposed, 1 );
                if( alreadDisposed != 0 )
                    return;

                // TODO: Do we need a Dispose()?
            }
        } // end Dispose( bool )


        private static int sm_lastActivityId;

        protected static int GetNextActivityId()
        {
            return Interlocked.Increment( ref sm_lastActivityId );
        }

        private string GetCommand()
        {
            string command = MyInvocation.Line;
            if( String.IsNullOrEmpty( command ) )
            {
                // This happens if we were called programmatically, such as from the UI.
                // In that case, we could build the command manually (i.e. make this
                // method abstract, and then each command has to override it, each going
                // through each of its parameters and building a command string based on
                // them).

                return "tbd: command not available when invoked programmatically";
            }
            return command.Trim();
        }


        private bool? m_hostSupportsColor;
        protected bool HostSupportsColor
        {
            get
            {
                if( null == m_hostSupportsColor )
                {
                    object obj = SessionState.PSVariable.GetValue( "HostSupportsColor" );
                    m_hostSupportsColor = (null != obj) && (obj is bool) && ((bool) obj);
                }
                return (bool) m_hostSupportsColor;
            }
        } // end Property HostSupportsColor


        protected virtual bool TrySetDebuggerContext
        {
            get
            {
                // Most of our commands will need context set, set we'll default to true.
                return true;
            }
        }

        protected virtual bool LogCmdletOutline
        {
            get { return true; }
        }

        // NOTE: If a derived class needs to override BeginProcessing, it should be sure
        // to call base.BeginProcessing() /before/ calling any of the SafeWrite* methods.
        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            m_pipelineThread = Thread.CurrentThread;
            if( LogCmdletOutline )
            {
                LogManager.Trace( "---------------------------" );
                LogManager.Trace( "BeginProcessing for command {0}: {1}", this, GetCommand() );
            }

            // You might think that the host should be the one to clear the auto-repeat
            // command, and in general, you'd be right. But the host doesn't actually get
            // involved with EVERY command that is run--it is only involved with every
            // /input/. And that input could be a script, and that script could run some
            // commands that set up an auto-repeat command (like stepping), and then run
            // some other commands (like .detach, or anything), leaving the auto-repeat
            // command set up, but the user definitely does not expect pressing [enter]
            // to step again (or do whatever the auto-repeat command was). So, for non-
            // formatting commands, we'll always start out by clearing the auto-repeat
            // command. (We exclude formatting commands, because then auto-repeat commands
            // would never work, because formatting commands are always tacked on to the
            // end of pipelines.)
            //
            // Note that this does not completely solve the problem, because cmdlets that
            // derive from DbgBaseCommand are not the only commands that could be run
            // after an auto-repeat command. But it at least helps.
            //
            // A more complete alternative might be to hook the PreCommandLookupAction and
            // clear the auto-repeat command there, but it would have to be a lot
            // trickier, because we'd need to do things like exclude auto-repeat-command-
            // clearing during execution of the "prompt" command, the PSReadline command,
            // etc. (in addition to formatting commands).
            if( !typeof( Formatting.Commands.FormatBaseCommand ).IsAssignableFrom( GetType() ) &&
                !typeof( Formatting.Commands.GetAltFormatViewDefCommand ).IsAssignableFrom( GetType() ) )
            {
                DbgProvider.SetAutoRepeatCommand( null );
            }

            if( TrySetDebuggerContext )
            {
                // Need to set context based on current path.
                PathInfo pi = this.CurrentProviderLocation( DbgProvider.ProviderId );
                string curPath = pi.ProviderPath;
                // It's okay if it fails (maybe current dir is "Dbg:\")--some commands don't
                // need a context.
                Debugger.TrySetContextByPath( curPath );
            }
        } // end BeginProcessing()


        public new void WriteError( ErrorRecord er )
        {
            WriteError( er, false );
        } // end WriteError()


        internal void WriteError( ErrorRecord er, bool alreadyLogged )
        {
            if( !alreadyLogged )
                DbgProvider.LogError( er );

            ((PSCmdlet) this).WriteError( er );
        } // end WriteError()


        public new void WriteVerbose( string verboseText )
        {
            WriteVerbose( verboseText, false );
        }

        internal void WriteVerbose( string verboseText, bool alreadyLogged )
        {
            if( !alreadyLogged )
                LogManager.Trace( "WriteVerbose: {0}", verboseText );

            ((PSCmdlet) this).WriteVerbose( verboseText );
        }

        internal void WriteVerbose( MulticulturalString verboseText )
        {
            Util.Fail( "Internal callers should use SafeWriteVerbose." );

            LogManager.Trace( "WriteVerbose: {0}", verboseText );

            WriteVerbose( verboseText, true );
        }

        public new void WriteWarning( string warningText )
        {
            WriteWarning( warningText, false );
        }

        internal void WriteWarning( string warningText, bool alreadyLogged )
        {
            if( !alreadyLogged )
                LogManager.Trace( "WriteWarning: {0}", warningText );

            ((PSCmdlet) this).WriteWarning( warningText );
        }

        internal void WriteWarning( MulticulturalString warningText )
        {
            Util.Fail( "Internal callers should use SafeWriteWarning." );

            LogManager.Trace( "WriteWarning: {0}", warningText );

            WriteWarning( warningText, true );
        }

        public new void WriteDebug( string debugText )
        {
            WriteDebug( debugText, false );
        }

        internal void WriteDebug( string debugText, bool alreadyLogged )
        {
            if( !alreadyLogged )
                LogManager.Trace( "WriteDebug: {0}", debugText );

            ((PSCmdlet) this).WriteDebug( debugText );
        }


        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        protected void WriteError( Exception exception, string errorId, ErrorCategory errorCategory, object targetObject )
        {
            WriteError( new ErrorRecord( exception, errorId, errorCategory, targetObject ) );
        }

        protected void WriteError( DbgProviderException dpe )
        {
            WriteError( dpe.ErrorRecord );
        }

        // Could be called multiple times in certain exception scenarios.
        protected virtual void Uninitialize()
        {
            LogManager.Trace( "{0}.Uninitialize().", Util.GetGenericTypeName( this ) );
        } // end Uninitialize()


        protected new void ThrowTerminatingError( ErrorRecord er )
        {
            LogManager.Trace( "ThrowTerminatingError1: {0}", er.ErrorDetails == null ? "(no details)" : er.ErrorDetails.Message );
            LogManager.Trace( "ThrowTerminatingError2: {0}", er.CategoryInfo == null ? "(no category info)" : er.CategoryInfo.ToString() );
            LogManager.Trace( "ThrowTerminatingError3: {0}", er.FullyQualifiedErrorId );
            LogManager.Trace( "ThrowTerminatingError4: {0}", er.Exception );

            // If someone calls ThrowTerminatingError, EndProcessing does not get called.
            Uninitialize();
            base.ThrowTerminatingError( er );
        }


        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        protected void ThrowTerminatingError( Exception ex, string errorId, ErrorCategory ec, object targetObject )
        {
            ThrowTerminatingError( new ErrorRecord( ex, errorId, ec, targetObject ) );
        }

        protected void ThrowTerminatingError( DbgProviderException dpe )
        {
            ThrowTerminatingError( dpe.ErrorRecord );
        }

        protected bool IsOnPipelineThread { get { return Thread.CurrentThread == m_pipelineThread; } }


        protected override void StopProcessing()
        {
            LogManager.Trace( "!!!!!!!!!!!!!!!!!!!!  StopProcessing: CANCELING ({0})  !!!!!!!!!!!!!!!!!!!!", this );
            LogManager.Flush();
            CancelTS.Cancel();
            base.StopProcessing();
            LogManager.Flush();
        } // end StopProcessing();

        protected override void EndProcessing()
        {
            base.EndProcessing();
            if( LogCmdletOutline )
            {
                LogManager.Trace( "====================  EndProcessing ({0})  ====================", this );
                LogManager.Flush();
            }
        }

        protected void IgnorePipelineStopped( Action action )
        {
            try
            {
                action();
            }
            catch( PipelineStoppedException )
            {
                // Ignore it. If the pipeline has stopped, then StopProcessing has been
                // called and we are already trying to cancel.
            }
        } // end IgnorePipelineStopped()


        protected void SafeWriteObject( object sendToPipeline )
        {
            SafeWriteObject( sendToPipeline, false ); // Or should the default be 'true'?
        }

        protected void SafeWriteObject( object sendToPipeline, bool skipIfCanceled )
        {
            Action action = () =>
            {
                if( skipIfCanceled && CancelTS.IsCancellationRequested )
                {
                    //WriteObject( Util.Sprintf( "Post-cancel: {0}", sendToPipeline ) );
                    return;
                }

                // Hmmm... I wish there was something I could do about this:
                //
                // There are exceptions that can get thrown under here which will end up
                // getting wholly eaten by the PS runtime.
                //
                // For instance, for some reason the WriteObject call will end up trying
                // to do some parameter binding (for a ForEach-Object command, I believe).
                // This parameter binding might call the object's ToString() method. And
                // if that throws... it will get swallowed and nobody will be the wiser.
                //
                // Example stack (when an exception is thrown from here it will be
                // swallowed):
                //
                //    DbgProvider.dll!MS.Dbg.DbgStackInfo.EnumerateStackFrames() Line 164	C#
                //    DbgProvider.dll!MS.Dbg.DbgStackInfo.ToString() Line 209	C#
                //    System.Management.Automation.dll!System.Management.Automation.PSObject.ToString(System.Management.Automation.ExecutionContext context, object obj, string separator, string format, System.IFormatProvider formatProvider, bool recurse, bool unravelEnumeratorOnRecurse) Line 1470	C#
                //    System.Management.Automation.dll!System.Management.Automation.PSObject.ToString() Line 1509	C#
                //    System.Management.Automation.dll!System.Management.Automation.ParameterBinderBase.BindParameter(System.Management.Automation.CommandParameterInternal parameter, System.Management.Automation.CompiledCommandParameter parameterMetadata, System.Management.Automation.ParameterBindingFlags flags) Line 664	C#
                //    System.Management.Automation.dll!System.Management.Automation.CmdletParameterBinderController.BindParameter(System.Management.Automation.CommandParameterInternal argument, System.Management.Automation.MergedCompiledCommandParameter parameter, System.Management.Automation.ParameterBindingFlags flags) Line 1489	C#
                //    System.Management.Automation.dll!System.Management.Automation.CmdletParameterBinderController.BindParameter(uint parameterSets, System.Management.Automation.CommandParameterInternal argument, System.Management.Automation.MergedCompiledCommandParameter parameter, System.Management.Automation.ParameterBindingFlags flags) Line 1429	C#
                //    System.Management.Automation.dll!System.Management.Automation.CmdletParameterBinderController.BindPipelineParameter(object parameterValue, System.Management.Automation.MergedCompiledCommandParameter parameter, System.Management.Automation.ParameterBindingFlags flags) Line 4309	C#
                //    System.Management.Automation.dll!System.Management.Automation.CmdletParameterBinderController.BindValueFromPipeline(System.Management.Automation.PSObject inputToOperateOn, System.Management.Automation.MergedCompiledCommandParameter parameter, System.Management.Automation.ParameterBindingFlags flags) Line 3676	C#
                //    System.Management.Automation.dll!System.Management.Automation.CmdletParameterBinderController.BindUnboundParametersForBindingStateInParameterSet(System.Management.Automation.PSObject inputToOperateOn, System.Management.Automation.CmdletParameterBinderController.CurrentlyBinding currentlyBinding, uint validParameterSets) Line 3620	C#
                //    System.Management.Automation.dll!System.Management.Automation.CmdletParameterBinderController.BindUnboundParametersForBindingState(System.Management.Automation.PSObject inputToOperateOn, System.Management.Automation.CmdletParameterBinderController.CurrentlyBinding currentlyBinding, uint validParameterSets) Line 3540	C#
                //    System.Management.Automation.dll!System.Management.Automation.CmdletParameterBinderController.BindPipelineParametersPrivate(System.Management.Automation.PSObject inputToOperateOn) Line 3476	C#
                //    System.Management.Automation.dll!System.Management.Automation.CmdletParameterBinderController.BindPipelineParameters(System.Management.Automation.PSObject inputToOperateOn) Line 3350	C#
                //    System.Management.Automation.dll!System.Management.Automation.CommandProcessor.Read() Line 475	C#
                //    System.Management.Automation.dll!System.Management.Automation.CommandProcessor.ProcessRecord() Line 282	C#
                //    System.Management.Automation.dll!System.Management.Automation.CommandProcessorBase.DoExecute() Line 556	C#
                //    System.Management.Automation.dll!System.Management.Automation.Internal.Pipe.AddToPipe(object obj) Line 449	C#
                //    mscorlib.dll!System.Security.SecurityContext.Run(System.Security.SecurityContext securityContext, System.Threading.ContextCallback callback, object state) Line 357	C#
                //    System.Management.Automation.dll!System.Management.Automation.MshCommandRuntime.WriteObject(object sendToPipeline) Line 212	C#
                //    System.Management.Automation.dll!System.Management.Automation.Cmdlet.WriteObject(object sendToPipeline) Line 387	C#
                //    DbgProvider.dll!MS.Dbg.Commands.DbgBaseCommand.SafeWriteObject.AnonymousMethod__1() Line 349	C#
                //    DbgProvider.dll!MS.Dbg.Commands.DbgBaseCommand.IgnorePipelineStopped(System.Action action) Line 319	C#
                //    DbgProvider.dll!MS.Dbg.Commands.DbgBaseCommand.SafeWriteObject(object sendToPipeline, bool skipIfCanceled) Line 353	C#
                //    DbgProvider.dll!MS.Dbg.Commands.DbgBaseCommand.SafeWriteObject(object sendToPipeline) Line 331	C#
                //    DbgProvider.dll!MS.Dbg.Commands.GetDbgStackCommand.ProcessRecord() Line 76	C#
                //    System.Management.Automation.dll!System.Management.Automation.CommandProcessor.ProcessRecord() Line 366	C#
                //    System.Management.Automation.dll!System.Management.Automation.CommandProcessorBase.DoExecute() Line 556	C#
                //    System.Management.Automation.dll!System.Management.Automation.Internal.PipelineProcessor.SynchronousExecuteEnumerate(object input, System.Collections.Hashtable errorResults, bool enumerate) Line 596	C#
                //    System.Management.Automation.dll!System.Management.Automation.PipelineOps.InvokePipeline(object input, bool ignoreInput, System.Management.Automation.CommandParameterInternal[][] pipeElements, System.Management.Automation.Language.CommandBaseAst[] pipeElementAsts, System.Management.Automation.CommandRedirection[][] commandRedirections, System.Management.Automation.Language.FunctionContext funcContext) Line 417	C#
                //    System.Management.Automation.dll!System.Management.Automation.Interpreter.ActionCallInstruction<object,bool,System.Management.Automation.CommandParameterInternal[][],System.Management.Automation.Language.CommandBaseAst[],System.Management.Automation.CommandRedirection[][],System.Management.Automation.Language.FunctionContext>.Run(System.Management.Automation.Interpreter.InterpretedFrame frame) Line 570	C#
                //    System.Management.Automation.dll!System.Management.Automation.Interpreter.EnterTryCatchFinallyInstruction.Run(System.Management.Automation.Interpreter.InterpretedFrame frame) Line 323	C#
                //    System.Management.Automation.dll!System.Management.Automation.Interpreter.EnterTryCatchFinallyInstruction.Run(System.Management.Automation.Interpreter.InterpretedFrame frame) Line 323	C#
                //    System.Management.Automation.dll!System.Management.Automation.Interpreter.EnterTryCatchFinallyInstruction.Run(System.Management.Automation.Interpreter.InterpretedFrame frame) Line 323	C#
                //    System.Management.Automation.dll!System.Management.Automation.Interpreter.EnterTryCatchFinallyInstruction.Run(System.Management.Automation.Interpreter.InterpretedFrame frame) Line 323	C#
                //    System.Management.Automation.dll!System.Management.Automation.Interpreter.Interpreter.Run(System.Management.Automation.Interpreter.InterpretedFrame frame) Line 126	C#
                //    System.Management.Automation.dll!System.Management.Automation.Interpreter.LightLambda.RunVoid1<System.Management.Automation.Language.FunctionContext>(System.Management.Automation.Language.FunctionContext arg0) Line 78	C#
                //    System.Management.Automation.dll!System.Management.Automation.PSScriptCmdlet.RunClause(System.Action<System.Management.Automation.Language.FunctionContext> clause, object dollarUnderbar, object inputToProcess) Line 1878	C#
                //    System.Management.Automation.dll!System.Management.Automation.PSScriptCmdlet.DoProcessRecord() Line 1795	C#
                //    System.Management.Automation.dll!System.Management.Automation.CommandProcessor.ProcessRecord() Line 366	C#
                //    System.Management.Automation.dll!System.Management.Automation.CommandProcessorBase.DoExecute() Line 556	C#
                //    System.Management.Automation.dll!System.Management.Automation.Internal.PipelineProcessor.SynchronousExecuteEnumerate(object input, System.Collections.Hashtable errorResults, bool enumerate) Line 596	C#
                //    System.Management.Automation.dll!System.Management.Automation.PipelineOps.InvokePipeline(object input, bool ignoreInput, System.Management.Automation.CommandParameterInternal[][] pipeElements, System.Management.Automation.Language.CommandBaseAst[] pipeElementAsts, System.Management.Automation.CommandRedirection[][] commandRedirections, System.Management.Automation.Language.FunctionContext funcContext) Line 417	C#
                //    System.Management.Automation.dll!System.Management.Automation.Interpreter.ActionCallInstruction<object,bool,System.Management.Automation.CommandParameterInternal[][],System.Management.Automation.Language.CommandBaseAst[],System.Management.Automation.CommandRedirection[][],System.Management.Automation.Language.FunctionContext>.Run(System.Management.Automation.Interpreter.InterpretedFrame frame) Line 570	C#
                //    System.Management.Automation.dll!System.Management.Automation.Interpreter.EnterTryCatchFinallyInstruction.Run(System.Management.Automation.Interpreter.InterpretedFrame frame) Line 323	C#
                //    System.Management.Automation.dll!System.Management.Automation.Interpreter.EnterTryCatchFinallyInstruction.Run(System.Management.Automation.Interpreter.InterpretedFrame frame) Line 323	C#
                //    System.Management.Automation.dll!System.Management.Automation.Interpreter.Interpreter.Run(System.Management.Automation.Interpreter.InterpretedFrame frame) Line 126	C#
                //    System.Management.Automation.dll!System.Management.Automation.Interpreter.LightLambda.RunVoid1<System.Management.Automation.Language.FunctionContext>(System.Management.Automation.Language.FunctionContext arg0) Line 78	C#
                //    System.Management.Automation.dll!System.Management.Automation.DlrScriptCommandProcessor.RunClause(System.Action<System.Management.Automation.Language.FunctionContext> clause, object dollarUnderbar, object inputToProcess) Line 516	C#
                //    System.Management.Automation.dll!System.Management.Automation.DlrScriptCommandProcessor.Complete() Line 410	C#
                //    System.Management.Automation.dll!System.Management.Automation.CommandProcessorBase.DoComplete() Line 632	C#
                //    System.Management.Automation.dll!System.Management.Automation.Internal.PipelineProcessor.DoCompleteCore(System.Management.Automation.CommandProcessorBase commandRequestingUpstreamCommandsToStop) Line 703	C#
                //    System.Management.Automation.dll!System.Management.Automation.Internal.PipelineProcessor.SynchronousExecuteEnumerate(object input, System.Collections.Hashtable errorResults, bool enumerate) Line 618	C#
                //    System.Management.Automation.dll!System.Management.Automation.Runspaces.LocalPipeline.InvokeHelper() Line 527	C#
                //    System.Management.Automation.dll!System.Management.Automation.Runspaces.LocalPipeline.InvokeThreadProc() Line 622	C#
                //    mscorlib.dll!System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext executionContext, System.Threading.ContextCallback callback, object state, bool preserveSyncCtx) Line 825	C#
                //    mscorlib.dll!System.Threading.ExecutionContext.Run(System.Threading.ExecutionContext executionContext, System.Threading.ContextCallback callback, object state, bool preserveSyncCtx) Line 775	C#
                //    mscorlib.dll!System.Threading.ExecutionContext.Run(System.Threading.ExecutionContext executionContext, System.Threading.ContextCallback callback, object state) Line 764	C#
                //    mscorlib.dll!System.Threading.ThreadHelper.ThreadStart() Line 111	C#
                //
                // (the exception is swallowed somewhere in the parameter binder stuff)
                //
                // Update: according to someone on the PS team, the ToString() is getting
                // called by some tracing code (so all that work is tossed if you don't
                // have tracing turned on).
                //
                WriteObject( sendToPipeline );
            };

            if( IsOnPipelineThread )
                IgnorePipelineStopped( action );
            else
                m_msgLoop.Post( action );
        }


        protected void SafeWriteError( ErrorRecord er )
        {
            DbgProvider.LogError( er );
            if( IsOnPipelineThread )
                IgnorePipelineStopped( () => WriteError( er, true ) );
            else
                m_msgLoop.Post( () => WriteError( er, true ) );
        }

        protected void SafeWriteError( string message, string errorId, ErrorCategory errorCategory, Exception innerException )
        {
            SafeWriteError( new DbgProviderException( message, errorId, errorCategory, innerException ) );
        }

        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        protected void SafeWriteError( string message, string errorId, ErrorCategory errorCategory, object targetObject )
        {
            SafeWriteError( new DbgProviderException( message, errorId, errorCategory, targetObject ) );
        }

        internal void SafeWriteError( MulticulturalString message, string errorId, ErrorCategory errorCategory, Exception innerException )
        {
            SafeWriteError( new DbgProviderException( message, errorId, errorCategory, innerException ) );
        }

        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        internal void SafeWriteError( MulticulturalString message, string errorId, ErrorCategory errorCategory, object targetObject )
        {
            SafeWriteError( new DbgProviderException( message, errorId, errorCategory, targetObject ) );
        }


        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        protected void SafeWriteError( Exception exception, string errorId, ErrorCategory errorCategory, object targetObject )
        {
            SafeWriteError( new ErrorRecord( exception, errorId, errorCategory, targetObject ) );
        }

        protected void SafeWriteError( DbgProviderException dpe )
        {
            SafeWriteError( dpe.ErrorRecord );
        }

        protected void SafeWriteProgress( ProgressRecord pr )
        {
            if( IsOnPipelineThread )
                IgnorePipelineStopped( () => WriteProgress( pr ) );
            else
                m_msgLoop.Post( () => WriteProgress( pr ) );
        }

        protected void SafeWriteWarning( string warningTextFormat, params object[] inserts )
        {
            SafeWriteWarning( new NonLocString( warningTextFormat ), inserts );
        }

        internal void SafeWriteWarning( MulticulturalString warningTextFormat, params object[] inserts )
        {
            SafeWriteWarning( Util.McSprintf( warningTextFormat, inserts ) );
        }

        protected void SafeWriteWarning( string warningText )
        {
            SafeWriteWarning( new NonLocString( warningText ) );
        }

        internal void SafeWriteWarning( MulticulturalString warningText )
        {
            LogManager.Trace( "WriteWarning: {0}", warningText );

            if( IsOnPipelineThread )
                IgnorePipelineStopped( () => WriteWarning( warningText, true ) );
            else
                m_msgLoop.Post( () => WriteWarning( warningText, true ) );
        }

        protected void SafeWriteVerbose( string verboseTextFormat, params object[] inserts )
        {
            SafeWriteVerbose( new NonLocString( verboseTextFormat ), inserts );
        }

        internal void SafeWriteVerbose( MulticulturalString verboseTextFormat, params object[] inserts )
        {
            SafeWriteVerbose( Util.McSprintf( verboseTextFormat, inserts ) );
        }

        protected void SafeWriteVerbose( string verboseText )
        {
            SafeWriteVerbose( new NonLocString( verboseText ) );
        }

        internal void SafeWriteVerbose( MulticulturalString verboseText )
        {
            LogManager.Trace( "WriteVerbose: {0}", verboseText );

            if( IsOnPipelineThread )
                IgnorePipelineStopped( () => WriteVerbose( verboseText, true ) );
            else
                m_msgLoop.Post( () => WriteVerbose( verboseText, true ) );
        }

        protected void SafeWriteDebug( string debugTextFormat, params object[] inserts )
        {
            SafeWriteDebug( new NonLocString( debugTextFormat ), inserts );
        }

        internal void SafeWriteDebug( MulticulturalString debugTextFormat, params object[] inserts )
        {
            SafeWriteDebug( Util.McSprintf( debugTextFormat, inserts ) );
        }

        protected void SafeWriteDebug( string debugText )
        {
            SafeWriteDebug( new NonLocString( debugText ) );
        }

        internal void SafeWriteDebug( MulticulturalString debugText )
        {
            LogManager.Trace( "WriteDebug: {0}", debugText );

            if( IsOnPipelineThread )
                IgnorePipelineStopped( () => WriteDebug( debugText, true ) );
            else
                m_msgLoop.Post( () => WriteDebug( debugText, true ) );
        }


        protected void QueueSafeAction( Action action )
        {
            if( IsOnPipelineThread )
                IgnorePipelineStopped( action );
            else
                m_msgLoop.Post( action );
        }

        protected void SignalDone()
        {
            m_msgLoop.SignalDone();
        }


        protected class MessageLoop
        {
            // This queue is used by other threads to queue actions that need to be run on the
            // pipeline thread.
            private BlockingCollection< Action > m_q;
            private ActionQueue m_actionQueue;
            private DbgBaseCommand m_cmdlet;

            public MessageLoop( DbgBaseCommand cmdlet )
            {
                m_cmdlet = cmdlet;
            } // end constructor


            public void Prepare()
            {
                if( null != m_q )
                    throw new InvalidOperationException( "Previous loop not completed." );

                m_q = new BlockingCollection< Action >();
                m_actionQueue = new ActionQueue( m_cmdlet.InvokeCommand, m_q );
            } // end Prepare()


            public void Post( Action action )
            {
                if( null == m_q )
                    throw new InvalidOperationException( "You must call Prepare() before posting." );

                m_q.Add( action );
            }


            public void Run()
            {
                if( (null == m_q) || m_q.IsCompleted )
                    throw new InvalidOperationException( "You must call Prepare() before calling Run()." );

                foreach( Action action in m_q.GetConsumingEnumerable() )
                {
                    // We do not exit early if cancellation has been requested, because
                    // actions may need to be run during cancellation.
                    m_cmdlet.IgnorePipelineStopped( action );
                }
                m_actionQueue.Dispose();
                m_q.Dispose();
                m_q = null;
            } // end Run()


            public void SignalDone()
            {
                m_q.CompleteAdding();
            } // end SignalDone()
        } // end class MessageLoop


        protected bool ShouldDoIt( string verboseDescription, string verboseWarning, string caption, bool force )
        {
            if( !ShouldProcess( verboseDescription,
                                verboseWarning,
                                caption ) )
            {
                return false;
            }

            if( !(force || ShouldContinue( verboseWarning, caption )) )
            {
                return false;
            }

            return true;
        } // end ShouldDoIt()


        protected static bool FailFast( string cmdletName, Exception unhandled )
        {
            //LogManager.MarkLogFileUnexpectedException();
            int failFastPolicy = RegistryUtils.GetRegValue( "FailFastPolicy", 0 );
            switch( failFastPolicy )
            {
                // The default policy value is '0', which means "crash if
                // non-interactive, else let PowerShell handle it".
                // TODO: Is this default policy what I want for DbgShell?
                case 0:
                {
                    if( !Environment.UserInteractive ||
                        (-1 != Environment.CommandLine.IndexOf( "NonInteractive", StringComparison.OrdinalIgnoreCase )) ||
                        (-1 != Environment.CommandLine.IndexOf( "EncodedCommand", StringComparison.OrdinalIgnoreCase )) )
                    {
                        Util.FailFast( Util.CcSprintf( Resources.ErrMsgUnhandledExceptionFmt, cmdletName, unhandled ),
                                       unhandled );
                        return true; // won't be executed; needed to satisfy compiler
                    }
                    else
                    {
                        Util.Fail( Util.Sprintf( "Unexpected exception in {0}: {1}", cmdletName, unhandled ) );
                        return false;
                    }
                }

                // Crash always.
                case 1:
                {
                    Util.FailFast( Util.CcSprintf( Resources.ErrMsgUnhandledExceptionFmt, cmdletName, unhandled ),
                                   unhandled );
                    return true; // won't be executed; needed to satisfy compiler
                }

                // Crash never.
                case 2:
                {
                    Util.Fail( Util.Sprintf( "Unexpected exception in {0}: {1}", cmdletName, unhandled ) );
                    return false;
                }

                default:
                    // Not a true program invariant, since someone could put anything in
                    // the registry.
                    Util.Fail( Util.Sprintf( "Invalid FailFastPolicy value: {0}", failFastPolicy ) );
                    Util.FailFast( Util.CcSprintf( Resources.ErrMsgUnhandledExceptionFmt, cmdletName, unhandled ),
                                   unhandled );
                    return true; // won't be executed; needed to satisfy compiler
            }
        } // end FailFast()


        // Normally, cmdlets can call DbgEngDebugger.SetCurrentCmdlet. But when calling
        // the DbgEngDebugger constructor, we also need access to the pipeline, so this
        // method is used for bootstrapping that.
        internal PipelineCallbackBase GetPipelineCallback()
        {
            return new PipelineCallback( this, 0, () => { throw new NotImplementedException(); } );
        } // end GetPipelineCallback()


     // protected void EnsureLiveTarget( DbgEngDebugger debugger )
     // {
     //     if( !debugger.IsLive )
     //     {
     //         DbgProviderException dpe = new DbgProviderException( "Target is not live.",
     //                                                              "TargetNotLive",
     //                                                              ErrorCategory.InvalidOperation );
     //         try { throw dpe; } catch( Exception ) { } // give it a stack trace
     //         ThrowTerminatingError( dpe );
     //     }
     // } // end EnsureLiveTarget()


        protected void EnsureTargetCanStep( DbgEngDebugger debugger )
        {
            if( !debugger.CanStep )
            {
                DbgProviderException dpe = new DbgProviderException( "Target is not live (cannot step).",
                                                                     "TargetNotLiveAndCantStep",
                                                                     ErrorCategory.InvalidOperation );
                try { throw dpe; } catch( Exception ) { } // give it a stack trace
                ThrowTerminatingError( dpe );
            }
        } // end EnsureTargetCanStep()


        protected void RebuildNamespaceAndSetLocationBasedOnDebuggerContext()
        {
            RebuildNamespaceAndSetLocationBasedOnDebuggerContext( true, null );
        }

        protected void RebuildNamespaceAndSetLocationBasedOnDebuggerContext( DbgRegisterSetBase baselineRegisters )
        {
            RebuildNamespaceAndSetLocationBasedOnDebuggerContext( true, baselineRegisters );
        }

        protected void RebuildNamespaceAndSetLocationBasedOnDebuggerContext( bool outputRegisters )
        {
            RebuildNamespaceAndSetLocationBasedOnDebuggerContext( outputRegisters, null );
        }

        protected void RebuildNamespaceAndSetLocationBasedOnDebuggerContext( bool outputRegisters,
                                                                             DbgRegisterSetBase baselineRegisters )
        {
            string newCurrentPath = DbgProvider.SetLocationByDebuggerContext();

            if( outputRegisters && (null != newCurrentPath) )
            {
                // This basically just removes the leading whack.
                string providerPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath( newCurrentPath );
                var regSet = DbgProvider.GetRegisterSetForPath( providerPath, baselineRegisters );
                if( null != regSet )
                {
                    // TODO: Set ReadDbgAssemblyCommand.NextAddrToDisassemble here? (and the others)
                    SafeWriteObject( regSet );
                }
            }
        } // end SetLocationBasedOnDebuggerContext()


        public override string ToString()
        {
            return Util.GetGenericTypeName( this );
        }


        private class ActionQueue : IActionQueue, IDisposable
        {
            private CommandInvocationIntrinsics m_invokeCommand;
            private BlockingCollection< Action > m_q;
            private IActionQueue m_oldQ;

            public ActionQueue( CommandInvocationIntrinsics invokeCommand, BlockingCollection< Action > q )
            {
                if( null == invokeCommand )
                    throw new ArgumentNullException( "invokeCommand" );

                if( null == q )
                    throw new ArgumentNullException( "q" );

                m_invokeCommand = invokeCommand;
                m_q = q;
                m_oldQ = DbgProvider.CurrentActionQueue;

                DbgProvider.CurrentActionQueue = this;
            } // end constructor

            public void Dispose()
            {
                DbgProvider.CurrentActionQueue = m_oldQ;
            }


            private static object[] sm_noInput = new object[ 0 ];

            public void PostCommand( Action< CommandInvocationIntrinsics > action )
            {
                m_q.Add( () =>
                    {
                        action( m_invokeCommand );
                    } );
            }
        } // end class ActionQueue
    } // end class DbgBaseCommand
}
