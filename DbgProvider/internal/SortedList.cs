using System;
using System.Collections;
using System.Collections.Generic;

namespace MS.Dbg
{
    /// <summary>
    ///    Like a normal List&lt;T&gt;, but maintains items in sorted order, with
    ///    duplicates allowed. It's a little bit like a "priority list" (but it's
    ///    implemented with just a plain list, not a heap or anything fancy).
    /// </summary>
    /// <remarks>
    ///    SortedList uses two comparers: one to decide where in the list to insert or
    ///    find an item (an IComparer), and another to decide if the list contains a
    ///    specified item (IEqualityComparer).
    ///
    ///    If items are inserted that compare as "equal" using the sort comparer, the
    ///    items are stored in the order they were inserted.
    /// </remarks>
    /// <typeparam name="TItem"></typeparam>
    internal class SortedList< TItem > : IList< TItem >
    {
        private List< TItem > m_list;
        private IComparer< TItem > m_sortComparer;  // duplicates allowed
        private IEqualityComparer< TItem > m_equalityComparer;
        //private bool m_isReadOnly; // TODO: make this thing freezable?

        public SortedList( IComparer< TItem > sortComparer,
                           IEqualityComparer< TItem > equalityComparer )
        {
            if( null == sortComparer )
                throw new ArgumentNullException( "sortComparer" );

            if( null == equalityComparer )
                throw new ArgumentNullException( "equalityComparer" );

            m_sortComparer = sortComparer;
            m_equalityComparer = equalityComparer;
            m_list = new List< TItem >();
        } // end constructor

        public SortedList( int initialSize,
                           IComparer< TItem > sortComparer,
                           IEqualityComparer< TItem > equalityComparer )
        {
            if( null == sortComparer )
                throw new ArgumentNullException( "sortComparer" );

            if( null == equalityComparer )
                throw new ArgumentNullException( "equalityComparer" );

            m_sortComparer = sortComparer;
            m_equalityComparer = equalityComparer;
            m_list = new List< TItem >( initialSize );
        } // end constructor


        private int _FindBeginningOfRun( TItem item, int startIdx )
        {
            int idx = startIdx;
            // We allow duplicates (using the "loose" comparer), so a binary search could
            // land us anywhere in the middle of a "run". We need to get back to the
            // beginning.
            while( (idx >= 0) && (0 == m_sortComparer.Compare( item, m_list[ idx ] )) )
                idx--;

            idx++; // We went one too far.
            Util.Assert( 0 == m_sortComparer.Compare( item, m_list[ idx ] ) );
            return idx;
        }

        public IEnumerable< int > LooselyMatchingIndexes( TItem item )
        {
            int idx = m_list.BinarySearch( item, m_sortComparer );
            if( idx < 0 )
                yield break;

            // A binary search could land us anywhere in the middle of a "run". We need to
            // get back to the beginning.
            idx = _FindBeginningOfRun( item, idx );

            while( 0 == m_sortComparer.Compare( item, m_list[ idx ] ) )
            {
                yield return idx;
                idx++;
            }
        } // end LooselyMatchingIndexes()

        /// <summary>
        ///    Finds the index of the specified item, using the "strict" comparer ("exact match").
        /// </summary>
        public int IndexOf( TItem item )
        {
            foreach( int idx in LooselyMatchingIndexes( item ) )
            {
                if( m_equalityComparer.Equals( item, m_list[ idx ] ) )
                    return idx;
            }
            return -1;
        }

        private void _ComplainSorted()
        {
            throw new InvalidOperationException( "This is a sorted list, so you are not permitted to specify the insertion index." );
        }

        public void Insert( int index, TItem item )
        {
            _ComplainSorted();
        }

        public void RemoveAt( int index )
        {
            m_list.RemoveAt( index );
        }

        public TItem this[ int index ]
        {
            get
            {
                return m_list[ index ];
            }
            set
            {
                // Allow direct replacement if the sort key is equal.
                if( 0 != m_sortComparer.Compare( m_list[ index ], value ) )
                    _ComplainSorted();

                m_list[ index ] = value;
            }
        }

        public void Add( TItem item )
        {
            int idx = m_list.BinarySearch( item, m_sortComparer );
            if( idx < 0 )
            {
                int insertIdx = ~idx;
                if( insertIdx == m_list.Count )
                    m_list.Add( item );
                else
                    m_list.Insert( insertIdx, item );
            }
            else
            {
                // We allow duplicates, so a binary search could land anywhere in a "run"
                // of equivalent items. Let's preserve insertion order, so we'll need to
                // find the end of the run.
                while( (idx < m_list.Count) && (0 == m_sortComparer.Compare( m_list[ idx ], item )) )
                    idx++;

                Util.Assert( (idx == m_list.Count) || (m_sortComparer.Compare( item, m_list[ idx ] ) < 0) );

                if( idx == m_list.Count )
                    m_list.Add( item );
                else
                    m_list.Insert( idx, item );
            }
        }

        public void Clear()
        {
            m_list.Clear();
        }

        public bool Contains( TItem item )
        {
            return IndexOf( item ) >= 0;
        }

        public void CopyTo( TItem[] array, int arrayIndex )
        {
            m_list.CopyTo( array, arrayIndex );
        }

        public int Count
        {
            get { return m_list.Count; }
        }

        public bool IsReadOnly
        {
            //get { return m_isReadOnly; }
            get { return false; }
        }

        public bool Remove( TItem item )
        {
            int idx = IndexOf( item );
            if( idx < 0 )
                return false;

            RemoveAt( idx );
            return true;
        }

        public IEnumerator<TItem> GetEnumerator()
        {
            return m_list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }


#if DEBUG
        private class Thing : IComparable< Thing >, IEquatable< Thing >
        {
            public int Priority;
            public string Blah;

            public Thing( int priority, string blah )
            {
                Priority = priority;
                Blah = blah;
            }

            // "Loose" comparer
            public int CompareTo( Thing other )
            {
                return Priority.CompareTo( other.Priority );
            }

            // "Stricter" equality.
            public bool Equals( Thing other )
            {
                return (0 == CompareTo( other )) &&
                       (0 == StringComparer.OrdinalIgnoreCase.Compare( Blah, other.Blah ));
            }
        }

        private void _VerifySorted()
        {
            for( int i = 0; i < m_list.Count; i++ )
            {
                if( i < (m_list.Count - 1) )
                {
                    int compareResult = m_sortComparer.Compare( m_list[ i ], m_list[ i + 1 ] );
                    if( compareResult > 0 )
                        throw new Exception( Util.Sprintf( "Looks unsorted at index {0}", i ) );
                }
            }
        }

        public static void SelfTest()
        {
            SortedList< Thing > sl = new SortedList<Thing>( Comparer<Thing>.Default, EqualityComparer<Thing>.Default );
            sl._VerifySorted();

            var thing1 = new Thing( 1, "hi" );
            sl.Add( thing1 );
            sl._VerifySorted();
            if( 0 != sl.IndexOf( thing1 ) )
                throw new Exception( "can't find thing1" );

            var thing2 = new Thing( 1, "hi again" );
            sl.Add( thing2 );
            sl._VerifySorted();
            if( 0 != sl.IndexOf( thing1 ) )
                throw new Exception( "can't find thing1" );
            if( 1 != sl.IndexOf( thing2 ) )
                throw new Exception( "can't find thing2" );

            var thing3 = new Thing( 0, "whatever" );
            sl.Add( thing3 );
            sl._VerifySorted();
            if( 0 != sl.IndexOf( thing3 ) )
                throw new Exception( "can't find thing3" );
            if( 1 != sl.IndexOf( thing1 ) )
                throw new Exception( "can't find thing1" );
            if( 2 != sl.IndexOf( thing2 ) )
                throw new Exception( "can't find thing2" );

            var thing4 = new Thing( 0, "whatever 2" );
            sl.Add( thing4 );
            sl._VerifySorted();
            if( 0 != sl.IndexOf( thing3 ) )
                throw new Exception( "can't find thing3" );
            if( 1 != sl.IndexOf( thing4 ) )
                throw new Exception( "can't find thing4" );
            if( 2 != sl.IndexOf( thing1 ) )
                throw new Exception( "can't find thing1" );
            if( 3 != sl.IndexOf( thing2 ) )
                throw new Exception( "can't find thing2" );

            // TODO: I should probably add more, better tests.
        }
#endif
    } // end class SortedList< TItem >
}

