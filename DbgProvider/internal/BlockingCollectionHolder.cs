//-----------------------------------------------------------------------
// <copyright file="BlockingCollectionHolder.cs" company="Microsoft">
//    Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace MS.Dbg
{
    /// <summary>
    ///    Manages a BlockingCollection that is used to asynchronously shuttle results
    ///    from a producer to a consumer.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <remarks>
    ///    The difficulty lies in the fact that there are so many ways for things to
    ///    stop:
    ///
    ///    1) The consumer may decide to signal the cancelToken,
    ///    2) The consumer may experience an exception, and dispose of the
    ///       BlockingCollection,
    ///    3) The producer may experience an exception, and terminate production,
    ///    4) The producer may finish and complete production normally,
    ///    5) Or some combination of the above!
    /// </remarks>
    internal class BlockingCollectionHolder< T > : IDisposable
    {
        // TODO: Would I be better off re-writing all this stuff to use something else,
        // like a ConcurrentQueue and an event?

        private BlockingCollection< T > m_bc;
        private Exception m_exception;
        private object m_syncRoot = new object();
#if DEBUG
        private bool m_testGoSlow;
#endif

        CancellationTokenSource m_localCts = new CancellationTokenSource();
        CancellationTokenSource m_masterCts;

        public CancellationToken CancelToken { get { return m_masterCts.Token; } }

        public BlockingCollectionHolder( CancellationToken cancelToken )
        {
            m_bc = new BlockingCollection< T >();
            m_masterCts = CancellationTokenSource.CreateLinkedTokenSource( cancelToken,
                                                                           m_localCts.Token );
#if DEBUG
            // Handy for testing cancellation.
            m_testGoSlow = !String.IsNullOrEmpty( Environment.GetEnvironmentVariable( "__DBGSHELL_TEST_SLOW" ) );
#endif
        }

        public void Add( T item )
        {
            lock( m_syncRoot )
            {
                if( null != m_bc )
                {
                    m_bc.Add( item );
#if DEBUG
                    if( m_testGoSlow )
                    {
                        for( int i = 0; i < 10; i++ )
                        {
                            Console.WriteLine( "slow stuff..." );
                            Thread.Sleep( 1000 );
                        }
                    }
#endif
                }
            }
        }

        public void SetException( Exception e )
        {
            if( null == e )
                throw new ArgumentNullException( "e" );

            m_exception = e;
            CompleteAdding();
        }

        public IEnumerable< T > GetConsumingEnumerable()
        {
            try
            {
                foreach( T item in m_bc.GetConsumingEnumerable() )
                {
                    yield return item;
                }
            }
            // We are precluded from logging an exception that is going to be hidden (see
            // comment in finally block) because you can't "yield return" from a try block
            // that has a "catch" clause. Perhaps this restriction will be removed in a
            // future version of C#.
         // catch( Exception e )
         // {
         //     if( (null != m_exception) && (e != m_exception) )
         //     {
         //         LogManager.Trace( "Warning: there are two exceptions happening. Ignoring this one: {0}",
         //                           Util.GetExceptionMessages( e ) );
         //         LogManager.Trace( "In favor of this one: {0}", Util.GetExceptionMessages( m_exception ) );
         //     }
         //     throw;
         // }
            finally
            {
                Dispose();
                // If we throw here, it could have the effect of trampling another
                // exception already in progress (as logged above). We could let the
                // caller wait until it's all clear (no other exception in progress), and
                // then throw this exception itself, but then we will hide this exception.
                // Since we hide an exception either way, let's go the convenient route.
                if( null != m_exception )
                    ExceptionDispatchInfo.Capture( m_exception ).Throw();
            }
        }

        public void CompleteAdding()
        {
            lock( m_syncRoot )
            {
                if( null != m_bc )
                    m_bc.CompleteAdding();
            }
        }

        public void Dispose()
        {
            BlockingCollection< T > local;
            lock( m_syncRoot )
            {
                local = m_bc;
                m_bc = null;
            }
            if( null != local )
            {
                // We might be disposing of this on some exception path, so give the
                // producer a chance to know that they should stop producing by
                // signaling the CancelToken.
                m_localCts.Cancel();
                local.Dispose();
            }
        }
    } // end class BlockingCollectionHolder
}

