using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management.Automation;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents the interface presented by a PowerShell pipeline.
    /// </summary>
    //internal interface IPipelineCallback
    // TODO: If I wanted to keep MulticulturalString internal, I might be able split the callback into
    // internal and external, a la the ClusterAwareUpdating callback interfaces.
    public interface IPipelineCallback
    {
        void WriteProgress( ProgressRecord pr );

        void WriteError( ErrorRecord er );

        void WriteWarning( string warningText );
        void WriteWarning( string warningTextFormat, params object[] inserts );

        void WriteVerbose( string verboseText );
        void WriteVerbose( string verboseTextFormat, params object[] inserts );

        void WriteDebug( string debugText );
        void WriteDebug( string debugTextFormat, params object[] inserts );

        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        void WriteError( string message, string errorId, ErrorCategory errorCategory, object targetObject );

        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        void WriteError( MulticulturalString message, string errorId, ErrorCategory errorCategory, object targetObject );

        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        void WriteError( Exception exception, string errorId, ErrorCategory errorCategory, object targetObject );

        void WriteError( DbgProviderException dpe );

        void WriteWarning( MulticulturalString warningTextFormat, params object[] inserts );

        void WriteWarning( MulticulturalString warningText );

        void WriteVerbose( MulticulturalString verboseTextFormat, params object[] inserts );

        void WriteVerbose( MulticulturalString verboseText );

        void WriteDebug( MulticulturalString debugTextFormat, params object[] inserts );

        void WriteDebug( MulticulturalString debugText );

        void WriteObject( object sendToPipeline );

        /// <summary>
        ///    Tells the implementing object that the cmdlet pipeline can finish.
        /// </summary>
        void SignalDone();
    } // end interface IPipelineCallback


    // Implement all the "convenience" methods once, here.
    internal abstract class PipelineCallbackBase : IPipelineCallback, IDisposable
    {
        public abstract void WriteProgress( ProgressRecord pr );
        public abstract void WriteError( ErrorRecord er );
        public abstract void WriteWarning( string warningText );
        public abstract void WriteVerbose( string verboseText );
        public abstract void WriteDebug( string debugText );
        public abstract void WriteWarning( MulticulturalString warningText );
        public abstract void WriteVerbose( MulticulturalString verboseText );
        public abstract void WriteDebug( MulticulturalString debugText );
        public abstract void WriteObject( object sendToPipeline );
        public abstract void SignalDone();

        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        public void WriteError( string message, string errorId, ErrorCategory errorCategory, object targetObject )
        {
            WriteError( new DbgProviderException( message, errorId, errorCategory, targetObject ) );
        }

        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        public void WriteError( MulticulturalString message, string errorId, ErrorCategory errorCategory, object targetObject )
        {
            WriteError( new DbgProviderException( message, errorId, errorCategory, targetObject ) );
        }

        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        public void WriteError( Exception exception, string errorId, ErrorCategory errorCategory, object targetObject )
        {
            WriteError( new ErrorRecord( exception, errorId, errorCategory, targetObject ) );
        }

        public void WriteError( DbgProviderException dpe )
        {
            WriteError( dpe.ErrorRecord );
        }

        public void WriteWarning( MulticulturalString warningTextFormat, params object[] inserts )
        {
            WriteWarning( Util.McSprintf( warningTextFormat, inserts ) );
        }

        public void WriteVerbose( MulticulturalString verboseTextFormat, params object[] inserts )
        {
            WriteVerbose( Util.McSprintf( verboseTextFormat, inserts ) );
        }

        public void WriteDebug( MulticulturalString debugTextFormat, params object[] inserts )
        {
            WriteDebug( Util.McSprintf( debugTextFormat, inserts ) );
        }

        public void WriteWarning( string warningTextFormat, params object[] inserts )
        {
            WriteWarning( Util.McSprintf( warningTextFormat, inserts ) );
        }

        public void WriteVerbose( string verboseTextFormat, params object[] inserts )
        {
            WriteVerbose( Util.McSprintf( verboseTextFormat, inserts ) );
        }

        public void WriteDebug( string debugTextFormat, params object[] inserts )
        {
            WriteDebug( Util.McSprintf( debugTextFormat, inserts ) );
        }


        private bool m_disposed;

        //protected void ThrowIfDisposed( string associatedNode )
        protected void ThrowIfDisposed()
        {
            if( m_disposed )
                throw new ObjectDisposedException( Util.McSprintf( Resources.ExMsgCallbackDisposedFmt,
                                                                   Util.GetGenericTypeName( this ) ) );
        }

        public void Dispose()
        {
            GC.SuppressFinalize( this );
            Dispose( true );
        }

        protected virtual void Dispose( bool disposing )
        {
            m_disposed = true;
        }
    } // end class PipelineCallbackBase


    internal class DummyPipelineCallback : PipelineCallbackBase
    {
     // private List< ErrorRecord > m_errors = new List< ErrorRecord >();
     // internal IList< ErrorRecord > ErrorsReported { get { return m_errors.AsReadOnly(); } }

        public DummyPipelineCallback()
        {
        } // end constructor


        public override void WriteProgress( ProgressRecord pr )
        {
            ThrowIfDisposed();
            // ignore
        }

        public override void WriteError( ErrorRecord er )
        {
            ThrowIfDisposed();
            DbgProvider.LogError( er );
          //m_errors.Add( er );
        }

        public override void WriteWarning( string warningText )
        {
            ThrowIfDisposed();
            LogManager.Trace( "WriteWarning: {0}", warningText );
        }

        public override void WriteVerbose( string verboseText )
        {
            ThrowIfDisposed();
            LogManager.Trace( "WriteVerbose: {0}", verboseText );
        }

        public override void WriteDebug( string debugText )
        {
            ThrowIfDisposed();
            LogManager.Trace( "WriteDebug: {0}", debugText );
        }

        // Internal-only stuff:

        public override void WriteWarning( MulticulturalString warningText )
        {
            ThrowIfDisposed();
            LogManager.Trace( "WriteWarning: {0}", warningText );
        }

        public override void WriteVerbose( MulticulturalString verboseText )
        {
            ThrowIfDisposed();
            LogManager.Trace( "WriteVerbose: {0}", verboseText );
        }

        public override void WriteDebug( MulticulturalString debugText )
        {
            ThrowIfDisposed();
            LogManager.Trace( "WriteDebug: {0}", debugText );
        }

        public override void WriteObject( object sendToPipeline )
        {
            ThrowIfDisposed();
            // ignore
        }

        public override void SignalDone()
        {
            ThrowIfDisposed();
            // ignore
        }
    } // end class DummyPipelineCallback
}
