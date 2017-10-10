using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace MS.Dbg
{
    public partial class DbgEngDebugger : DebuggerObject
    {
        /// <summary>
        ///    An IPipelineCallback for when we don't actually have a running pipeline. It
        ///    "holds" anything sent to it, to be forwarded later when we do have a
        ///    pipeline.
        /// </summary>
        private class HoldingPipelineCallback : PipelineCallbackBase
        {
            List< Action< IPipelineCallback > > m_q = new List< Action< IPipelineCallback > >();


            public int Count
            {
                get { return m_q.Count; }
            }

            public override void WriteProgress( ProgressRecord pr )
            {
                ThrowIfDisposed();
                // Actually, maybe let's just ignore progres?
                //m_q.Add( ( nextPipe ) => nextPipe.WriteProgress( pr ) );
            }

            public override void WriteError( ErrorRecord er )
            {
                Util.LogError( "HoldingPipeline", er );
                ThrowIfDisposed();
                m_q.Add( ( nextPipe ) => nextPipe.WriteError( er ) );
            }

            public override void WriteWarning( string warningText )
            {
                ThrowIfDisposed();
                LogManager.Trace( "HoldingPipeline WARNING: {0}", warningText );
                m_q.Add( ( nextPipe ) => nextPipe.WriteWarning( warningText ) );
            }

            public override void WriteVerbose( string verboseText )
            {
                ThrowIfDisposed();
                LogManager.Trace( "HoldingPipeline VERBOSE: {0}", verboseText );
                m_q.Add( ( nextPipe ) => nextPipe.WriteVerbose( verboseText ) );
            }

            public override void WriteDebug( string debugText )
            {
                ThrowIfDisposed();
                LogManager.Trace( "HoldingPipeline DEBUG: {0}", debugText );
                m_q.Add( ( nextPipe ) => nextPipe.WriteDebug( debugText ) );
            }

            public override void WriteWarning( MulticulturalString warningText )
            {
                ThrowIfDisposed();
                LogManager.Trace( "HoldingPipeline WARNING: {0}", warningText );
                m_q.Add( ( nextPipe ) => nextPipe.WriteWarning( warningText ) );
            }

            public override void WriteVerbose( MulticulturalString verboseText )
            {
                ThrowIfDisposed();
                LogManager.Trace( "HoldingPipeline VERBOSE: {0}", verboseText );
                m_q.Add( ( nextPipe ) => nextPipe.WriteVerbose( verboseText ) );
            }

            public override void WriteDebug( MulticulturalString debugText )
            {
                ThrowIfDisposed();
                LogManager.Trace( "HoldingPipeline DEBUG: {0}", debugText );
                m_q.Add( ( nextPipe ) => nextPipe.WriteDebug( debugText ) );
            }

            public override void WriteObject( object sendToPipeline )
            {
                ThrowIfDisposed();
                m_q.Add( ( nextPipe ) => nextPipe.WriteObject( sendToPipeline ) );
            }

            public override void SignalDone()
            {
                ThrowIfDisposed();
                Util.Fail( "I don't think this should ever be called." );
            }

            /// <summary>
            ///    When you actually get a pipeline, call drain to send all the held
            ///    items to it.
            /// </summary>
            public void Drain( IPipelineCallback into )
            {
                if( into == this )
                    throw new InvalidOperationException( "Can't drain a pipe into itself." );

                foreach( var held in m_q )
                {
                    held( into );
                }
                m_q.Clear();
            } // end Drain()
        } // end class HoldingPipelineCallback
    } // end class DbgEngDebugger
}

