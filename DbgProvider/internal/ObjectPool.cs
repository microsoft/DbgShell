using System;
using System.Collections.Concurrent;

namespace MS.Dbg
{
    /// <summary>
    ///    Manages a pool of objects.
    /// </summary>
    [System.Diagnostics.DebuggerDisplay( "ObjectPool: {m_leases.Count} in use, {m_freeStack.Count} free" )]
    internal sealed class ObjectPool< T > : IDisposable where T : class
    {
        private sealed class ObjectLease : IDisposable
        {
            private ObjectPool< T > m_pool;

            public ObjectLease( T t, ObjectPool< T > pool )
            {
                m_pool = pool;
                m_pool._AddLease( this, t );
            }

            public void Dispose()
            {
                if( null != m_pool )
                {
                    m_pool._RemoveLease( this );
                    m_pool = null;
                }
            }
        } // end class ObjectLease


        private readonly ConcurrentStack< T > m_freeStack
            = new ConcurrentStack< T >();

        // I guess I could use WeakRefs to be more resilient to leaks... but that's
        // probably overkill.
        private readonly ConcurrentDictionary< ObjectLease, T > m_leases
            = new ConcurrentDictionary< ObjectLease, T >();

        private Func< T > m_Factory;
        private Action< T > m_Clean;


        public ObjectPool( Func< T > factory )
            : this( factory, null )
        {
        }

        public ObjectPool( Func< T > factory, Action< T > clean )
        {
            if( null == factory )
                throw new ArgumentNullException( "factory" );

            if( null == clean )
                clean = (x) => { };

            m_Factory = factory;
            m_Clean = clean;
        } // end constructor


        private void _AddLease( ObjectLease lease, T t )
        {
            Util.Assert( m_leases.TryAdd( lease, t ) );
        }

        private void _RemoveLease( ObjectLease lease )
        {
            T t;
            bool removed = m_leases.TryRemove( lease, out t );
            Util.Assert( removed );
            if( removed )
            {
                m_Clean( t );
                m_freeStack.Push( t );
            }
        } // end _RemoveLease()


        /// <summary>
        ///    Allocates an object from the pool. Dispose of the return value when
        ///    finished with the object.
        /// </summary>
        public IDisposable Lease( out T t )
        {
            if( !m_freeStack.TryPop( out t ) )
            {
                t = m_Factory();
            }
            return new ObjectLease( t, this );
        } // end Lease()


        public void Dispose()
        {
            T t;
            while( m_freeStack.TryPop( out t ) )
            {
                var disposable = t as IDisposable;
                if( null != disposable )
                    disposable.Dispose();
            }

            Util.Assert( 0 == m_leases.Count );
        } // end Dispose()
    } // end class ObjectPool
}

