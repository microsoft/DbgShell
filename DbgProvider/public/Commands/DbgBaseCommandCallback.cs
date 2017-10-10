using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    public abstract partial class DbgBaseCommand : PSCmdlet, IDisposable
    {
        // Embodies all the output methods (including convenience overloads).
        internal class PipelineCallback< TCmdlet > : PipelineCallbackBase where TCmdlet : DbgBaseCommand
        {
            protected TCmdlet Cmdlet { get; private set; }
            protected int RootActivityId { get; private set; }
            private Func< int > m_getNextActivityId;
            protected object SyncRoot { get; private set; }

         // private List< ErrorRecord > m_errors = new List< ErrorRecord >();
         // internal IList< ErrorRecord > ErrorsReported { get { return m_errors.AsReadOnly(); } }

            protected PipelineCallback( TCmdlet cmdlet,
                                        int rootActivityId,
                                        Func< int > getNextActivityId )
            {
                if( null == cmdlet )
                    throw new ArgumentNullException( "cmdlet" );

                if( null == getNextActivityId )
                    throw new ArgumentNullException( "getNextActivityId" );

                Cmdlet = cmdlet;
                RootActivityId = rootActivityId;
                m_getNextActivityId = getNextActivityId;
                SyncRoot = new object();
            } // end constructor


            private Dictionary< int, int> m_idMap = new Dictionary< int, int >();

            private void _FixActivityIds( ref ProgressRecord pr )
            {
                _AssignNewActivityId( ref pr );

                if( pr.ParentActivityId < 0 )
                {
                    pr.ParentActivityId = RootActivityId;
                }
                else
                {
                    int newParentId;
                    lock( SyncRoot )
                    {
                        if( !m_idMap.TryGetValue( pr.ParentActivityId, out newParentId ) )
                        {
                            WriteWarning( Util.McSprintf( Resources.WarningBadActivityIdFmt, pr.ParentActivityId ) );
                            Util.Fail( "Our should not have this problem." );
                            pr.ParentActivityId = RootActivityId;
                        }
                        else
                        {
                            pr.ParentActivityId = newParentId;
                        }
                    }
                }
            } // end _FixActivityIds()


            private void _AssignNewActivityId( ref ProgressRecord pr )
            {
                // The problem is that ProgressRecord.ActivityId is get-only. So
                // in order to change it, we have to create an entirely new
                // ProgressRecord.
                int newId;

                lock( SyncRoot )
                {
                    if( !m_idMap.TryGetValue( pr.ActivityId, out newId ) )
                    {
                        // This could wrap. But if I worry about that, then I also have to worry
                        // about collisions once it wraps as the space fills up. I'm not going to
                        // worry about that.
                        newId = m_getNextActivityId();
                        m_idMap.Add( pr.ActivityId, newId );
                    }
                }

                ProgressRecord newPr = new ProgressRecord( newId, pr.Activity, pr.StatusDescription );
                newPr.CurrentOperation = pr.CurrentOperation;
                newPr.PercentComplete = pr.PercentComplete;
                newPr.RecordType = pr.RecordType;
                newPr.SecondsRemaining = pr.SecondsRemaining;
                newPr.ParentActivityId = pr.ParentActivityId; // this will get updated later

                pr = newPr;
            } // ed _AssignNewActivityId()


            public override void WriteProgress( ProgressRecord pr )
            {
                ThrowIfDisposed();
                _FixActivityIds( ref pr );

                Cmdlet.SafeWriteProgress( pr );
            }

            public override void WriteError( ErrorRecord er )
            {
                ThrowIfDisposed();
              //m_errors.Add( er );
                Cmdlet.SafeWriteError( er );
            }

            public override void WriteWarning( string warningText )
            {
                ThrowIfDisposed();
                Cmdlet.SafeWriteWarning( warningText );
            }

            public override void WriteVerbose( string verboseText )
            {
                ThrowIfDisposed();
                Cmdlet.SafeWriteVerbose( verboseText );
            }

            public override void WriteDebug( string debugText )
            {
                ThrowIfDisposed();
                Cmdlet.SafeWriteDebug( debugText );
            }

            // Internal-only stuff:

            public override void WriteWarning( MulticulturalString warningText )
            {
                ThrowIfDisposed();
                Cmdlet.SafeWriteWarning( warningText );
            }

            public override void WriteVerbose( MulticulturalString verboseText )
            {
                ThrowIfDisposed();
                Cmdlet.SafeWriteVerbose( verboseText );
            }

            public override void WriteDebug( MulticulturalString debugText )
            {
                ThrowIfDisposed();
                Cmdlet.SafeWriteDebug( debugText );
            }

            public override void WriteObject( object sendToPipeline )
            {
                ThrowIfDisposed();
                Cmdlet.SafeWriteObject( sendToPipeline );
            }

            public override void SignalDone()
            {
                ThrowIfDisposed();
                Cmdlet.SignalDone();
            }
        } // end class PipelineCallback< TCmdlet >


        internal class PipelineCallback : PipelineCallback< DbgBaseCommand >
        {
            public PipelineCallback( DbgBaseCommand cmdlet,
                                     int rootActivityId,
                                     Func< int > getNextActivityId )
                : base( cmdlet, rootActivityId, getNextActivityId )
            {
            }
        } // end class PipelineCallback
    } // end partial class DbgBaseCommand
}

