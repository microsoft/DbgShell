using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using MS.Dbg.Formatting.Commands;

namespace MS.Dbg
{
    /// <summary>
    /// </summary>
    public class Bucketizer< T > : IReadOnlyDictionary< T, IReadOnlyList< T > >,
                                   IEnumerable< KeyValuePair< T, T> >
    {
        private enum IndexAdapterReturns
        {
            Keys,
            Values,
            Both
        }

        /// <summary>
        ///    Adapter to allow accessing PsIndexedDictionary items by index (instead of
        ///    by key). Also allows "unrolling" duplicate keys and value lists.
        /// </summary>
        private class IndexAdapter : IReadOnlyList< T >
        {
            private List< KeyValuePair< T, T > > m_indexList;
            private IndexAdapterReturns m_returns;

            public IndexAdapter( List< KeyValuePair< T, T > > indexList,
                                 IndexAdapterReturns returns )
            {
                m_indexList = indexList;
                m_returns = returns;
            }

            // Allows PowerShell-style negative indexes.
            private int _FixIndex( int idx )
            {
                if( idx >= 0 )
                    return idx;
                else
                    return Count + idx;
            }


            public IEnumerable< T > Keys
            {
                get
                {
                    foreach( var kvp in m_indexList )
                    {
                        yield return kvp.Key;
                    }
                }
            }

            public IEnumerable< T > Values
            {
                get
                {
                    foreach( var kvp in m_indexList )
                    {
                        yield return kvp.Value;
                    }
                }
            }

            public IEnumerable< T > KeyValuePairs
            {
                get
                {
                    foreach( var kvp in m_indexList )
                    {
                        yield return new T( kvp );
                    }
                }
            }

            public T this[ int key ]
            {
                get
                {
                    switch( m_returns )
                    {
                        case IndexAdapterReturns.Keys:
                            return m_indexList[ _FixIndex( key ) ].Key;
                        case IndexAdapterReturns.Values:
                            return m_indexList[ _FixIndex( key ) ].Value;
                        case IndexAdapterReturns.Both:
                            return new T( m_indexList[ _FixIndex( key ) ] );
                        default:
                            throw new Exception( "unexpected enum value" );
                    }
                }
            }

            public int Count
            {
                get { return m_indexList.Count; }
            }

            public IEnumerator<T> GetEnumerator()
            {
                switch( m_returns )
                {
                    case IndexAdapterReturns.Keys:
                        return Keys.GetEnumerator();
                    case IndexAdapterReturns.Values:
                        return Values.GetEnumerator();
                    case IndexAdapterReturns.Both:
                        return KeyValuePairs.GetEnumerator();
                    default:
                        throw new Exception( "unexpected enum value" );
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        } // end class IndexAdapter


        private List< KeyValuePair< T, T > > m_indexList;
        private Dictionary< T, List< T > > m_dict;
        private Dictionary< string, List< T > > m_stringDict;
        private bool m_isReadOnly;


        public PsIndexedDictionary()
        {
            m_indexList = new List< KeyValuePair< T, T > >();
            m_dict = new Dictionary< T, List< T > >( T.Instance );
            m_stringDict = new Dictionary< string, List< T > >( StringComparer.OrdinalIgnoreCase );
        } // end constructor

        public PsIndexedDictionary( int capacity )
        {
            m_indexList = new List< KeyValuePair< T, T > >( capacity );
            m_dict = new Dictionary< T, List< T > >( capacity, T.Instance );
            m_stringDict = new Dictionary< string, List< T > >( capacity, StringComparer.OrdinalIgnoreCase );
        }


        private void _CheckReadOnly()
        {
            if( m_isReadOnly )
                throw new InvalidOperationException( "This object has been made read-only." );
        }

        public void Freeze()
        {
            m_isReadOnly = true;
        }

        private static string _CreateStringKey( T psoKey )
        {
            string stringKey = FormatAltSingleLineCommand.FormatSingleLineDirect( psoKey );
            stringKey = CaStringUtil.StripControlSequences( stringKey );

            // For string objects, Format-AltSingleLine will yield something with quotes
            // around it. Let's take them off. Perhaps we should check if psoKey's
            // BaseObject is a string first?
            if( (stringKey.Length > 0) &&
                ('"' == stringKey[ 0 ]) &&
                ('"' == stringKey[ stringKey.Length - 1 ]) )
            {
                stringKey = stringKey.Substring( 1, stringKey.Length -2 );
            }
            return stringKey;
        } // end _CreateStringKey()


        public void Add( T key, T value )
        {
            _CheckReadOnly();
            m_indexList.Add( new KeyValuePair< T, T >( key, value ) );
            List< T > list;

            if( !m_dict.TryGetValue( key, out list ) )
            {
                list = new List< T >();
                m_dict.Add( key, list );
            }
            list.Add( value );

            string stringKey = _CreateStringKey( key );

            if( !m_stringDict.TryGetValue( stringKey, out list ) )
            {
                list = new List< T >();
                m_stringDict.Add( stringKey, list );
            }
            list.Add( value );
        } // end Add

        public bool ContainsKey( T key )
        {
            return m_dict.ContainsKey( key );
        }

        public bool ContainsKey( string stringKey )
        {
            return m_stringDict.ContainsKey( stringKey );
        }

        public IEnumerable< T > Keys
        {
            get { return m_dict.Keys; }
        }

        public ICollection< String > StringKeys
        {
            get { return m_stringDict.Keys; }
        }

        #region ICollection< KeyValuePair< T, T > > Members

        // N.B. This returns the "rolled-up" count: if there are duplicate keys, this
        // number will be smaller than m_indexList.Count.
        public int Count
        {
            get { return m_dict.Count; }
        }

        // For things with duplicate keys (like multimap), this includes duplicates.
        public int UncollapsedCount
        {
            get { return m_indexList.Count; }
        }

        public bool IsReadOnly
        {
            get { return m_isReadOnly; }
        }

        #endregion

        #region IEnumerable< KeyValuePair< T, T > > Members

        public IEnumerator< KeyValuePair< T, T > > GetIndividualItemEnumerator()
        {
            return m_indexList.GetEnumerator();
        }

        IEnumerator< KeyValuePair< T, T > >
        IEnumerable< KeyValuePair< T, T > >.GetEnumerator()
        {
            return GetIndividualItemEnumerator();
        }

        #endregion

        #region IEnumerable Members

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable< KeyValuePair< T, IReadOnlyList< T > > >) this).GetEnumerator();
        }

        #endregion

        public bool TryGetValue( T key, out IReadOnlyList< T > value )
        {
            List< T > list;
            if( m_dict.TryGetValue( key, out list ) )
            {
                value = new ReadOnlyCollection< T >( list );
                return true;
            }
            value = null;
            return false;
        }

        public bool TryGetValue( string stringKey, out IReadOnlyList< T > value )
        {
            List< T > list;
            if( m_stringDict.TryGetValue( stringKey, out list ) )
            {
                value = new ReadOnlyCollection< T >( list );
                return true;
            }
            value = null;
            return false;
        }

        public IEnumerable< IReadOnlyList< T > > Values
        {
            get
            {
                foreach( var val in m_dict.Values )
                {
                    yield return new ReadOnlyCollection< T >( val );
                }
            }
        }

        public IReadOnlyList< T > this[ T key ]
        {
            get
            {
                return new ReadOnlyCollection< T >( m_dict[ key ] );
            }
        }

        public IReadOnlyList< T > this[ string stringKey ]
        {
            get
            {
                return new ReadOnlyCollection< T >( m_stringDict[ stringKey ] );
            }
        }

        #region IEnumerable< KeyValuePair< T, IReadOnlyList< T > > > Members

        IEnumerator< KeyValuePair< T, IReadOnlyList< T > > >
        IEnumerable< KeyValuePair< T, IReadOnlyList< T > > >.GetEnumerator()
        {
            foreach( var kvp in m_dict )
            {
                yield return new KeyValuePair< T, IReadOnlyList< T > >(
                    kvp.Key, new ReadOnlyCollection< T >( kvp.Value ) );
            }
        }

        #endregion


        private IndexAdapter m_valByIndexAdapter;

        public IReadOnlyList< T > ValueByIndex
        {
            get
            {
                if( null == m_valByIndexAdapter )
                {
                    m_valByIndexAdapter = new IndexAdapter( m_indexList, IndexAdapterReturns.Values );
                }
                return m_valByIndexAdapter;
            }
        }

        private IndexAdapter m_keyByIndexAdapter;

        public IReadOnlyList< T > KeyByIndex
        {
            get
            {
                if( null == m_keyByIndexAdapter )
                {
                    m_keyByIndexAdapter = new IndexAdapter( m_indexList, IndexAdapterReturns.Keys );
                }
                return m_keyByIndexAdapter;
            }
        }

        private IndexAdapter m_kvpByIndex;

        public IReadOnlyList< T > KvpByIndex
        {
            get
            {
                if( null == m_kvpByIndex )
                {
                    m_kvpByIndex = new IndexAdapter( m_indexList, IndexAdapterReturns.Both );
                }
                return m_kvpByIndex;
            }
        }
    } // end class PsIndexedDictionary
}

