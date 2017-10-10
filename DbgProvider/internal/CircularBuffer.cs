using System;
using System.Collections;
using System.Collections.Generic;

namespace MS.Dbg
{
    internal class CircularBuffer< T > : IReadOnlyList< T >
    {
        private T[] m_buf;
        private int m_index; // index of the next spot to write to.
        private bool m_hasWrapped;


        public CircularBuffer( int capacity )
        {
            if( capacity <= 0 )
                throw new ArgumentOutOfRangeException( "capacity" );

            m_buf = new T[ capacity ];
        } // end constructor


        private int _TranslateIndex( int virtualIndex )
        {
            // This is to allow PowerShell-style negative indexing (where [-1] means "the
            // last element", [-2] means "the second-to-last element", etc.).
            if( virtualIndex < 0 )
            {
                virtualIndex = Count + virtualIndex;
            }

            if( m_hasWrapped )
            {
                if( virtualIndex >= m_buf.Length )
                    throw new ArgumentOutOfRangeException( "virtualIndex " );

                int lengthToEnd = m_buf.Length - m_index;

                if( virtualIndex < lengthToEnd )
                    return m_index + virtualIndex;
                else
                    return virtualIndex - lengthToEnd;
            }
            else
            {
                if( virtualIndex >= m_index )
                    throw new ArgumentOutOfRangeException( "virtualIndex" );

                return virtualIndex;
            }
        } // end _TranslateIndex()


        public void Add( T item )
        {
            m_buf[ m_index ] = item;
            m_index++;
            if( m_index == m_buf.Length )
            {
                m_index = 0;
                m_hasWrapped = true;
            }
        } // end Add()


        public void Clear()
        {
            m_index = 0;
            m_hasWrapped = false;
        } // end Clear()


        public int Count
        {
            get
            {
                if( m_hasWrapped )
                    return m_buf.Length;
                else
                    return m_index;
            }
        } // end property Count


        public int Capacity
        {
            get
            {
                return m_buf.Length;
            }
        } // end property Capacity


        public T this[ int index ]
        {
            get
            {
                return m_buf[ _TranslateIndex( index ) ];
            }
            set
            {
                m_buf[ _TranslateIndex( index ) ] = value;
            }
        } // end indexer


        public IEnumerator< T > GetEnumerator()
        {
            for( int i = 0; i < Count; i++ )
            {
                yield return this[ i ];
            }
        } // end GetEnumerator()


        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }


        public void Resize( int newCapacity )
        {
            // This is inefficient (we do more work than necessary when the buffer
            // has not wrapped, or when the new capacity is smaller), but it's very
            // easy to code, and this is not expected to be used on a hot path.
            CircularBuffer< T > newBuf = new CircularBuffer< T >( newCapacity );
            foreach( T t in this )
            {
                newBuf.Add( t );
            }
            this.m_buf = newBuf.m_buf;
            this.m_index = newBuf.m_index;
            this.m_hasWrapped = newBuf.m_hasWrapped;
        } // end Resize()

#if DEBUG
        public static void SelfTest()
        {
            CircularBuffer< int > buf;
            int i;
            
            buf = new CircularBuffer< int >( 1 );
            Util.Assert( buf.Capacity == 1 );
            Util.Assert( buf.Count == 0 );
            try
            {
                i = buf[ 0 ];
                Util.Fail( "Should have thrown." );
            }
            catch( ArgumentException ) { }

            buf.Add( 0 );
            Util.Assert( buf.Capacity == 1 );
            Util.Assert( buf.Count == 1 );
            i = buf[ 0 ];
            Util.Assert( i == 0 );

            try
            {
                i = buf[ 1 ];
                Util.Fail( "Should have thrown." );
            }
            catch( ArgumentException ) { }

            buf.Add( 1 );
            Util.Assert( buf.Capacity == 1 );
            Util.Assert( buf.Count == 1 );
            i = buf[ 0 ];
            Util.Assert( i == 1 );

            buf.Add( 2 );
            Util.Assert( buf.Capacity == 1 );
            Util.Assert( buf.Count == 1 );
            i = buf[ 0 ];
            Util.Assert( i == 2 );

            buf = new CircularBuffer< int >( 2 );
            Util.Assert( buf.Capacity == 2 );
            Util.Assert( buf.Count == 0 );

            try
            {
                i = buf[ 0 ];
                Util.Fail( "Should have thrown." );
            }
            catch( ArgumentException ) { }

            buf.Add( 0 );
            Util.Assert( buf.Capacity == 2 );
            Util.Assert( buf.Count == 1 );
            i = buf[ 0 ];
            Util.Assert( i == 0 );

            try
            {
                i = buf[ 1 ];
                Util.Fail( "Should have thrown." );
            }
            catch( ArgumentException ) { }

            try
            {
                i = buf[ 2 ];
                Util.Fail( "Should have thrown." );
            }
            catch( ArgumentException ) { }

            buf.Add( 1 );
            Util.Assert( buf.Capacity == 2 );
            Util.Assert( buf.Count == 2 );
            i = buf[ 0 ];
            Util.Assert( i == 0 );
            i = buf[ 1 ];
            Util.Assert( i == 1 );

            try
            {
                i = buf[ 2 ];
                Util.Fail( "Should have thrown." );
            }
            catch( ArgumentException ) { }

            buf.Add( 2 );
            Util.Assert( buf.Capacity == 2 );
            Util.Assert( buf.Count == 2 );
            i = buf[ 0 ];
            Util.Assert( i == 1 );
            i = buf[ 1 ];
            Util.Assert( i == 2 );

            buf.Add( 3 );
            Util.Assert( buf.Capacity == 2 );
            Util.Assert( buf.Count == 2 );
            i = buf[ 0 ];
            Util.Assert( i == 2 );
            i = buf[ 1 ];
            Util.Assert( i == 3 );
        } // end SelfTest()
#endif
    } // end class CircularBuffer
}
