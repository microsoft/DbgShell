using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MS.Dbg
{
    public interface IHaveName
    {
        string Name { get; }
    }

    /// <summary>
    ///    Like a List&lt; T &gt;, but elements can be addressed by name as well
    ///    as numeric index, and is freezable.
    /// </summary>
    /// <remarks>
    ///    In C#, there's less need for this sort of thing, because you have LINQ. In
    ///    PowerShell, you certainly have powerful scripting abilities, but it's still not
    ///    nearly as nice as just having an indexer that takes a string.
    ///
    ///    (compare "$sym.Children[ 'm_foo' ].Whatever" to "($sym.Children | where Name -eq 'm_foo' | select -first 1).Whatever").
    /// </remarks>
    public class NameIndexableList< T > : IList< T >, IReadOnlyList< T >
        where T : IHaveName
    {
        private List< T > m_list;
        private bool m_readOnly;

        public NameIndexableList()
        {
            m_list = new List< T >();
        }

        public NameIndexableList( int initialCapacity )
        {
            m_list = new List< T >( initialCapacity );
        }

        private void _CheckReadOnly()
        {
            if( m_readOnly )
                throw new InvalidOperationException( "This collection is read-only." );
        }


        public void Freeze()
        {
            m_readOnly = true;
        }

        /// <summary>
        ///    Note that if there are duplicate names, this only returns the first item
        ///    with the specified name. If there is no item with that name, it will throw.
        /// </summary>
        public T this[ string name ]
        {
            get
            {
                // Note: This will throw if there is no such item. Just like using an
                // integer index that was out of range would throw. You can use
                // HasItemNamed if you want to peek first.
                return m_list.First( ( x ) => 0 == Util.Strcmp_OI( x.Name, name ) );
            }
        }

        public bool HasItemNamed( string name )
        {
            return null != m_list.FirstOrDefault( ( x ) => 0 == Util.Strcmp_OI( x.Name, name ) );
        }

        // Perhaps a signature like this could be used if we wanted to be able to address
        // into collections with duplicate names.
     // public T this[ string name, int plus ]
     // {
     // }


        //
        // IList< T > stuff
        //

        public int IndexOf( T item )
        {
            return m_list.IndexOf( item );
        }

        public void Insert( int index, T item )
        {
            _CheckReadOnly();
            m_list.Insert( index, item );
        }

        public void RemoveAt( int index )
        {
            _CheckReadOnly();
            m_list.RemoveAt( index );
        }

        public T this[ int index ]
        {
            get
            {
                return m_list[ index ];
            }
            set
            {
                _CheckReadOnly();
                m_list[ index ] = value;
            }
        }

        public void Add( T item )
        {
            _CheckReadOnly();
            m_list.Add( item );
        }

        public void Clear()
        {
            _CheckReadOnly();
            m_list.Clear();
        }

        public bool Contains( T item )
        {
            return m_list.Contains( item );
        }

        public void CopyTo( T[] array, int arrayIndex )
        {
            m_list.CopyTo( array, arrayIndex );
        }

        public int Count
        {
            get { return m_list.Count; }
        }

        public bool IsReadOnly
        {
            get { return m_readOnly; }
        }

        public bool Remove( T item )
        {
            _CheckReadOnly();
            return m_list.Remove( item );
        }

        public IEnumerator< T > GetEnumerator()
        {
            return m_list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    } // end class NameIndexableList
}

