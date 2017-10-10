using System;
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Management.Automation.Runspaces;

namespace MS.Dbg
{
    public abstract class CmdletProviderBase : CmdletProvider
    {
        public abstract string ProviderId { get; }

        // For most providers, PowerShell feels it must create multiple instances of the
        // provider... annoying. For fun, I'll keep track of how many instances get
        // created. Since this particular provider has no drives or real instance methods,
        // it seems it only gets created once!
        private static int sm_id;

        // Another consequence of PS not using a single provider instance is that I can't
        // store state in this object. I can hang drive-specific state off of drives
        // (which can be accessed via ProviderInfo.Drives). For runspace/session-specific
        // state, I'll have to have a static dictionary of settings objects, indexed by
        // runspace instance Id. INTeb83a002.

        protected CmdletProviderBase()
        {
            sm_id++;
            //Console.WriteLine( "CmdletProviderBase: Constructing instance {0}.", sm_id );
        } // end constructor


        // TODO: rationalize this and the LogManager stuff.
        [Conditional( "DEBUG" )]
        private void _WriteDebugFmt( string msgFmt, params object[] inserts )
        {
   // The following code is extremely slow... don't turn it on if you want any
   // performance.
   //       string msg = Util.Sprintf( msgFmt, inserts );
   //       // The problem with this is that it doesn't work everywhere--for instance, you
   //       // can't call it from the constructor, and you won't see output from it during
   //       // tab completion.
   //       //WriteDebug( msg );
   //       //Console.WriteLine( msg );
   //       TraceSource.TraceInformation( "{0}._WriteDebugFmt: {1}", Providerid, msg );

            // This is no better.
            //LogManager.Trace( msgFmt, inserts );
        } // end WriteDebug


        public new void WriteError( ErrorRecord er )
        {
            WriteError( er, false );
        } // end WriteError()


        internal void WriteError( ErrorRecord er, bool alreadyLogged )
        {
            if( !alreadyLogged )
                _LogError( er );

            base.WriteError( er );
        } // end WriteError()


        public new void WriteVerbose( string verboseText )
        {
            WriteVerbose( verboseText, false );
        }

        internal void WriteVerbose( string verboseText, bool alreadyLogged )
        {
            if( !alreadyLogged )
                LogManager.Trace( "{0} WriteVerbose: {1}", ProviderId, verboseText );

            base.WriteVerbose( verboseText );
        }

        internal void WriteVerbose( MulticulturalString verboseText )
        {
            LogManager.Trace( "{0} WriteVerbose: {1}", ProviderId, verboseText );

            WriteVerbose( verboseText, true );
        }

        public new void WriteWarning( string warningText )
        {
            WriteWarning( warningText, false );
        }

        internal void WriteWarning( string warningText, bool alreadyLogged )
        {
            if( !alreadyLogged )
                LogManager.Trace( "{0} WriteWarning: {1}", ProviderId, warningText );

            base.WriteWarning( warningText );
        }

        internal void WriteWarning( MulticulturalString warningText )
        {
            LogManager.Trace( "{0} WriteWarning: {1}", ProviderId, warningText );

            WriteWarning( warningText, true );
        }

        public new void WriteDebug( string debugText )
        {
            WriteDebug( debugText, false );
        }

        internal void WriteDebug( string debugText, bool alreadyLogged )
        {
            if( !alreadyLogged )
                LogManager.Trace( "{0} WriteDebug: {1}", ProviderId, debugText );

            base.WriteDebug( debugText );
        }

        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        protected void WriteError( Exception exception, string errorId, ErrorCategory errorCategory, object targetObject )
        {
            WriteError( new ErrorRecord( exception, errorId, errorCategory, targetObject ) );
        }

        protected void WriteError( DbgProviderException tpe )
        {
            WriteError( tpe.ErrorRecord );
        }

        private void _LogError( ErrorRecord er )
        {
            Util.LogError( ProviderId, er );
        } // end _LogError()

        protected new void ThrowTerminatingError( ErrorRecord er )
        {
            LogManager.Trace( "{0} ThrowTerminatingError1: {1}", ProviderId, er.ErrorDetails == null ? "(no details)" : er.ErrorDetails.Message );
            LogManager.Trace( "{0} ThrowTerminatingError2: {1}", ProviderId, er.CategoryInfo == null ? "(no category info)" : er.CategoryInfo.ToString() );
            LogManager.Trace( "{0} ThrowTerminatingError3: {1}", ProviderId, er.FullyQualifiedErrorId );
            LogManager.Trace( "{0} ThrowTerminatingError4: {1}", ProviderId, er.Exception );

            base.ThrowTerminatingError( er );
        }


        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        protected void ThrowTerminatingError( Exception ex, string errorId, ErrorCategory ec, object targetObject )
        {
            ThrowTerminatingError( new ErrorRecord( ex, errorId, ec, targetObject ) );
        }

        protected void ThrowTerminatingError( DbgProviderException tpe )
        {
            ThrowTerminatingError( tpe.ErrorRecord );
        }

        protected static void VerifyThreadHasRunspace()
        {
            // DefaultRunspace relies on thread-local storage to get the runspace
            // associated with the current thread.
            if( null == Runspace.DefaultRunspace )
                throw new InvalidOperationException( "This method can only be called from a thread that is associated with a runspace." );
        } // end VerifyThreadHasRunspace()


        protected abstract ProviderInfo CreateProviderInfo( Guid runspaceId,
                                                            ProviderInfo providerInfo,
                                                            SessionState sessionState );

        protected override ProviderInfo Start( ProviderInfo providerInfo )
        {
            VerifyThreadHasRunspace();
            var pi = CreateProviderInfo( Runspace.DefaultRunspace.InstanceId,
                                         providerInfo,
                                         SessionState );
            return base.Start( pi );
        }

        protected abstract void RemoveRunspaceSettings();

        protected override void Stop()
        {
            RemoveRunspaceSettings();
            base.Stop();
        }


        protected abstract TraceSource TraceSource { get; }

        protected override void StopProcessing()
        {
            _WriteDebugFmt( "{0} StopProcessing()", ProviderId );
            base.StopProcessing();
        }
    } // end class CmdletProviderBase
}

