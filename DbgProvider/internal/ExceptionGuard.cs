//-----------------------------------------------------------------------
// <copyright file="ExceptionGuard.cs" company="Microsoft">
//    Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Threading;

namespace MS.Dbg
{
    // This is an "airtight" implementation of ExceptionGuard. See the remarks of the
    // other class to see why we prefer the other class.
    //
    //internal struct ExceptionGuard< T > : IDisposable where T : class, IDisposable
    //{
    //    public T Disposable { get; private set; }

    //    public ExceptionGuard( T d ) : this() { Disposable = d; }

    //    public T Dismiss()
    //    {
    //        T tmp = Disposable;
    //        Disposable = null;
    //        return tmp;
    //    }

    //    public void Dispose()
    //    {
    //        if( null != Disposable )
    //        {
    //            Disposable.Dispose();
    //        }
    //    }
    //}


    /// <summary>
    ///    Protects IDisposable resources from being left un-disposed in a method that
    ///    returns said IDisposable resources in the event that an exception is thrown
    ///    out of the method.
    /// </summary>
    /// <remarks>
    /// <para>
    ///    This class is specifically designed to solve the problem pointed out by CA2000
    ///    ("Call Dispose on object 'foo' before all references to it are out of scope.")
    ///    It is not strictly correct however, because 1) it is a reference type, not a
    ///    value type, so it's own allocation could fail, and 2) the List allocation or
    ///    re-allocation could fail. However, we do not care to handle
    ///    OutOfMemoryExceptions anyway (TODO: They are CSEs now anyway, right?).  But it
    ///    is much more convenient to use than a strictly correct implementation.
    /// </para>
    /// <para>
    ///    Unfortunately, it also fails to eliminate the CA2000 warning that it is meant
    ///    to deal with. However, even the airtight implementation will not eliminate
    ///    CA2000 warnings--the CA2000 warning is extremely picky, in that it will always
    ///    fire unless you write code /exactly/ as specified in the "how to fix" example
    ///    in its documentation.
    /// </para>
    /// <para>
    ///    N.B. It disposes of items in the reverse order that they were passed to
    ///    Protect.
    /// </para>
    /// <para>
    ///    This class is thread-safe.
    /// </para>
    /// </remarks>
    internal sealed class ExceptionGuard : IDisposable
    {
        private object m_syncRoot = new object();
        private List< IDisposable > m_disposables;
        private TransferTicket m_outstandingTransfer;


        public ExceptionGuard()
            : this( new List< IDisposable >() )
        {
        }


        public ExceptionGuard( IDisposable d )
            : this( new List< IDisposable >() )
        {
            _Protect( d );
        }


        private ExceptionGuard( List< IDisposable > disposables )
        {
            Util.Assert( null != disposables );
            m_disposables = disposables;
        }


        private void _CheckDisposed()
        {
            if( null == m_disposables )
                throw new ObjectDisposedException( this.ToString() );
        }


        private void _Protect( IDisposable d )
        {
            if( null != d )
            {
                lock( m_syncRoot )
                {
                    _CheckDisposed();
                    m_disposables.Add( d );
                }
            }
        }


        public T Protect<T>( T d ) where T : IDisposable
        {
            _Protect( d );
            return d;
        }


        private sealed class ListDisposer< TList > : IDisposable
            where TList : class, IEnumerable< IDisposable >
        {
            private TList m_list;
            public ListDisposer( TList list ) { m_list = list; }
            public void Dispose()
            {
                if( null != m_list )
                {
                    foreach( IDisposable d in m_list )
                    {
                        if( null != d )
                            d.Dispose();
                    }
                    m_list = null;
                }
            }
        } // end class ListDisposer


        // NOTE: Does not make a copy of the list.
        public TList ProtectAll< TList >( TList list )
            where TList : class, IEnumerable<IDisposable>
        {
            _Protect( new ListDisposer< TList >( list ) );
            return list;
        }


        /// <summary>
        ///    Clears the list of items that would have been disposed when the
        ///    ExceptionGuard was disposed.
        /// </summary>
        public void Dismiss()
        {
            lock( m_syncRoot )
            {
                _CheckDisposed();
                m_disposables.Clear();
            }
        } // end Dismiss()


        /// <summary>
        ///    Disposes of all items protected by the ExceptionGuard, in reverse order.
        /// </summary>
        public void Dispose()
        {
            List< IDisposable > tmp;
            lock( m_syncRoot )
            {
                if( null == m_disposables )
                    return;

                tmp = m_disposables;
                m_disposables = null;
            } // end lock

            // We might be disposed of in Exceptional circumstances, with an oustanding
            // transfer that was not committed. In that case, we don't want to let the
            // receiving thread get stuck waiting for the commit that will never happen.
            if( null != m_outstandingTransfer )
            {
                m_outstandingTransfer.CancelTransferIfNecessary();
            }

            for( int i = tmp.Count - 1; i >= 0; i-- )
            {
                tmp[ i ].Dispose();
            }
        } // end Dispose()


        /// <summary>
        ///    Represents an object that can transfer an ExceptionGuard object from one
        ///    thread to another.
        ///
        ///    The source thread calls ExceptionGuard.Transfer to get a transfer ticket,
        ///    and then posts the ticket to some other thread. If the post is successful,
        ///    it calls CommitTransfer().
        ///
        ///    Once CommitTransfer() is called, the responsibility for the items protected
        ///    by the original ExceptionGuard is now with the holder of the
        ///    TransferTicket(), which must call Redeem() to retrieve a new ExceptionGuard
        ///    protecting those items.
        ///
        ///    If the originating thread disposes of the original ExceptionGuard without
        ///    committing the transfer first, the transfer will be canceled.
        /// </summary>
        public interface ITransferTicket
        {
            /// <summary>
            ///    To be called by the originating thread to commit the transfer. Once
            ///    this is called, the transfer is "past the point of no return": the
            ///    destination thread /must/ redeem the transfer and dispose of the
            ///    transferred ExceptionGuard, or noone will.
            /// </summary>
            void CommitTransfer();

            /// <summary>
            ///    To be called by the receiving thread to retrieve an ExceptionGuard
            ///    representing the items to be guarded. This call may block until the
            ///    originating thread has committed the transfer.
            /// </summary>
            ExceptionGuard Redeem();
        } // end interface ITransferTicket


        private class TransferTicket : ITransferTicket
        {
            private object m_syncRoot = new object();

            //
            // We begin life with:
            //
            //         m_source: <ExceptionGuard>
            //    m_transferred: null
            //
            // When the transfer is committed:
            //
            //         m_source: null
            //    m_transferred: <ExceptionGuard>
            //
            // When the transfer is redeemed:
            //
            //         m_source: null
            //    m_transferred: null
            //

            private ExceptionGuard m_source;
            private ExceptionGuard m_transferred;

            internal TransferTicket( ExceptionGuard source )
            {
                m_source = source;
            }

            public ExceptionGuard Redeem()
            {
                lock( m_syncRoot )
                {
                    if( (null == m_source) && (null == m_transferred) )
                        throw new InvalidOperationException( "Transfer already redeemed." );

                    if( null == m_transferred )
                    {
                        // We need to wait for the transfer to be committed by
                        // CommitTransfer().
                        Monitor.Wait( m_syncRoot );
                        Util.Assert( null != m_transferred );
                    }
                    // else the CommitTransfer call already went through.

                    var tmp = m_transferred;
                    m_transferred = null;
                    return tmp;
                } // end lock
            } // end Redeem()


            public void CommitTransfer()
            {
                lock( m_syncRoot )
                {
                    if( null == m_source )
                        throw new InvalidOperationException( "Transfer already committed." );

                    m_transferred = m_source._CompleteTransfer();
                    m_source = null;

                    // The thread that will Redeem() the transfer may or may not be
                    // waiting. If it is, signal it:
                    Monitor.PulseAll( m_syncRoot );
                } // end lock
            } // end CommitTransfer()


            /// <summary>
            ///    To be called in the even that the transfer was not able to be
            ///    committed. (For instance, if the originating thread failed to post the
            ///    transfer to the receiving thread.)
            /// </summary>
            internal void CancelTransferIfNecessary()
            {
                lock( m_syncRoot )
                {
                    if( null == m_source )
                        return; // transfer is already committed or redeemed.

                    // If the destination thread is stuck waiting, we'll prepare an empty
                    // ExceptionGuard to give to it and then unblock it.
                    m_transferred = new ExceptionGuard();
                    m_source = null;
                    Monitor.PulseAll( m_syncRoot );
                } // end lock
            } // end CancelTransferIfNecessary()
        } // end class TransferTicket


        /// <summary>
        ///    Transfers the IDisposable items tracked by this instance into a new
        ///    instance.
        /// </summary>
        public ITransferTicket Transfer()
        {
            lock( m_syncRoot )
            {
                if( null != m_outstandingTransfer )
                    throw new InvalidOperationException( "There is already an outstanding transfer." );

                m_outstandingTransfer = new TransferTicket( this );
                return m_outstandingTransfer;
            } // end lock
        } // end Transfer()


        private ExceptionGuard _CompleteTransfer()
        {
            lock( m_syncRoot )
            {
                _CheckDisposed();
                var eg = new ExceptionGuard( m_disposables );
                m_disposables = new List< IDisposable >();
                return eg;
            }
        } // end _CompleteTransfer()
    } // end class ExceptionGuard
}

