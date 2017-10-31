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
    public class ListIndexableDictionary< TKey, TValue > : IReadOnlyDictionary< TKey, IReadOnlyList< TValue > >,
                                                           IEnumerable< KeyValuePair< TKey, IReadOnlyList< TValue > > >,
                                                           IEnumerable< KeyValuePair< TKey, TValue > >
    {
        /// <summary>
        ///    Adapter to allow accessing ListIndexableDictionary items by index (instead
        ///    of by key). Also allows "unrolling" duplicate keys and value lists.
        /// </summary>
        private class IndexAdapter< T > : IReadOnlyList< T >
        {
            private List< KeyValuePair< TKey, TValue > > m_indexList;
            private Func< KeyValuePair< TKey, TValue >, T > m_adapter;
            private Func< IEnumerator< T > > m_getEnumerator;

            public IndexAdapter( List< KeyValuePair< TKey, TValue > > indexList,
                                 Func< KeyValuePair< TKey, TValue >, T > adapter,
                                 Func< IEnumerator< T > > getEnumerator )
            {
                m_indexList = indexList;
                m_adapter = adapter;
                m_getEnumerator = getEnumerator;
            }

            // Allows PowerShell-style negative indexes.
            private int _FixIndex( int idx )
            {
                if( idx >= 0 )
                    return idx;
                else
                    return Count + idx;
            }


            public IEnumerable< TKey > Keys
            {
                get
                {
                    foreach( var kvp in m_indexList )
                    {
                        yield return kvp.Key;
                    }
                }
            }

            public IEnumerable< TValue > Values
            {
                get
                {
                    foreach( var kvp in m_indexList )
                    {
                        yield return kvp.Value;
                    }
                }
            }

            public IEnumerable< KeyValuePair< TKey, TValue > > KeyValuePairs
            {
                get
                {
                    foreach( var kvp in m_indexList )
                    {
                        yield return kvp;
                    }
                }
            }

            public T this[ int idx ]
            {
                get
                {
                    return m_adapter( m_indexList[ _FixIndex( idx ) ] );
                }
            }

            public int Count
            {
                get { return m_indexList.Count; }
            }

            public IEnumerator< T > GetEnumerator()
            {
                return m_getEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        } // end class IndexAdapter< T >


        private List< KeyValuePair< TKey, TValue > > m_indexList;
        private Dictionary< TKey, List< TValue > > m_dict;
        //private Dictionary< string, List< T > > m_stringDict;
        private bool m_isReadOnly;


        public ListIndexableDictionary()
        {
            m_indexList = new List< KeyValuePair< TKey, TValue > >();
            m_dict = new Dictionary< TKey, List< TValue > >();
            //m_stringDict = new Dictionary< string, List< T > >( StringComparer.OrdinalIgnoreCase );
        } // end constructor

        public ListIndexableDictionary( int capacity )
        {
            m_indexList = new List< KeyValuePair< TKey, TValue > >( capacity );
            m_dict = new Dictionary< TKey, List< TValue > >( capacity );
            //m_stringDict = new Dictionary< string, List< T > >( capacity, StringComparer.OrdinalIgnoreCase );
        }

        public ListIndexableDictionary( int capacity, IEqualityComparer< TKey > keyComparer )
        {
            m_indexList = new List< KeyValuePair< TKey, TValue > >( capacity );
            m_dict = new Dictionary< TKey, List< TValue > >( capacity, keyComparer );
            //m_stringDict = new Dictionary< string, List< T > >( capacity, StringComparer.OrdinalIgnoreCase );
        }

        public ListIndexableDictionary( IEqualityComparer< TKey > keyComparer )
        {
            m_indexList = new List< KeyValuePair< TKey, TValue > >();
            m_dict = new Dictionary< TKey, List< TValue > >( keyComparer );
            //m_stringDict = new Dictionary< string, List< T > >( capacity, StringComparer.OrdinalIgnoreCase );
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

     // private static string _CreateStringKey( T psoKey )
     // {
     //     string stringKey = FormatAltSingleLineCommand.FormatSingleLineDirect( psoKey );
     //     stringKey = CaStringUtil.StripControlSequences( stringKey );

     //     // For string objects, Format-AltSingleLine will yield something with quotes
     //     // around it. Let's take them off. Perhaps we should check if psoKey's
     //     // BaseObject is a string first?
     //     if( (stringKey.Length > 0) &&
     //         ('"' == stringKey[ 0 ]) &&
     //         ('"' == stringKey[ stringKey.Length - 1 ]) )
     //     {
     //         stringKey = stringKey.Substring( 1, stringKey.Length -2 );
     //     }
     //     return stringKey;
     // } // end _CreateStringKey()


        public void Add( TKey key, TValue value )
        {
            _CheckReadOnly();
            m_indexList.Add( new KeyValuePair< TKey, TValue >( key, value ) );
            List< TValue > list;

            if( !m_dict.TryGetValue( key, out list ) )
            {
                list = new List< TValue >();
                m_dict.Add( key, list );
            }
            list.Add( value );

         // string stringKey = _CreateStringKey( key );

         // if( !m_stringDict.TryGetValue( stringKey, out list ) )
         // {
         //     list = new List< T >();
         //     m_stringDict.Add( stringKey, list );
         // }
         // list.Add( value );
        } // end Add

        public bool ContainsKey( TKey key )
        {
            return m_dict.ContainsKey( key );
        }

     // public bool ContainsKey( string stringKey )
     // {
     //     return m_stringDict.ContainsKey( stringKey );
     // }

        public IEnumerable< TKey > Keys
        {
            get { return m_dict.Keys; }
        }

     // public ICollection< String > StringKeys
     // {
     //     get { return m_stringDict.Keys; }
     // }

        #region ICollection< KeyValuePair< TKey, TValue > > Members

        // N.B. This returns the "rolled-up" count: if there are duplicate keys, this
        // number will be smaller than m_indexList.Count.
        public int Count
        {
            get { return m_dict.Count; }
        }

        // For things with duplicate keys, this includes duplicates.
        public int UncollapsedCount
        {
            get { return m_indexList.Count; }
        }

        public bool IsReadOnly
        {
            get { return m_isReadOnly; }
        }

        #endregion

        #region IEnumerable< KeyValuePair< TKey, TValue > > Members

        public IEnumerator< KeyValuePair< TKey, TValue > > GetIndividualItemEnumerator()
        {
            return m_indexList.GetEnumerator();
        }

        IEnumerator< KeyValuePair< TKey, TValue > >
        IEnumerable< KeyValuePair< TKey, TValue > >.GetEnumerator()
        {
            return GetIndividualItemEnumerator();
        }

        #endregion

        #region IEnumerable Members

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable< KeyValuePair< TKey, IReadOnlyList< TValue > > >) this).GetEnumerator();
        }

        #endregion

        public bool TryGetValue( TKey key, out IReadOnlyList< TValue > value )
        {
            List< TValue > list;
            if( m_dict.TryGetValue( key, out list ) )
            {
                value = new ReadOnlyCollection< TValue >( list );
                return true;
            }
            value = null;
            return false;
        }

     // public bool TryGetValue( string stringKey, out IReadOnlyList< T > value )
     // {
     //     List< T > list;
     //     if( m_stringDict.TryGetValue( stringKey, out list ) )
     //     {
     //         value = new ReadOnlyCollection< T >( list );
     //         return true;
     //     }
     //     value = null;
     //     return false;
     // }

        public IEnumerable< IReadOnlyList< TValue > > Values
        {
            get
            {
                foreach( var val in m_dict.Values )
                {
                    yield return new ReadOnlyCollection< TValue >( val );
                }
            }
        }

        public IReadOnlyList< TValue > this[ TKey key ]
        {
            get
            {
                return new ReadOnlyCollection< TValue >( m_dict[ key ] );
            }
        }

     // public IReadOnlyList< T > this[ string stringKey ]
     // {
     //     get
     //     {
     //         return new ReadOnlyCollection< T >( m_stringDict[ stringKey ] );
     //     }
     // }

        #region IEnumerable< KeyValuePair< T, IReadOnlyList< T > > > Members

        IEnumerator< KeyValuePair< TKey, IReadOnlyList< TValue > > >
        IEnumerable< KeyValuePair< TKey, IReadOnlyList< TValue > > >.GetEnumerator()
        {
            foreach( var kvp in m_dict )
            {
                yield return new KeyValuePair< TKey, IReadOnlyList< TValue > >(
                    kvp.Key, new ReadOnlyCollection< TValue >( kvp.Value ) );
            }
        }

        #endregion


        private IndexAdapter< TValue > m_valByIndexAdapter;

        public IReadOnlyList< TValue > ValuesByIndex
        {
            get
            {
                if( null == m_valByIndexAdapter )
                {
                    IEnumerable< TValue > enumVals()
                    {
                        foreach( var kvp in m_indexList )
                        {
                            yield return kvp.Value;
                        }
                    }

                    m_valByIndexAdapter = new IndexAdapter< TValue >( m_indexList,
                                                                      ( kvp ) => kvp.Value,
                                                                      enumVals().GetEnumerator );
                }
                return m_valByIndexAdapter;
            }
        }

        private IndexAdapter< TKey > m_keyByIndexAdapter;

        public IReadOnlyList< TKey > KeysByIndex
        {
            get
            {
                if( null == m_keyByIndexAdapter )
                {
                    IEnumerable< TKey > enumKeys()
                    {
                        foreach( var kvp in m_indexList )
                        {
                            yield return kvp.Key;
                        }
                    }

                    m_keyByIndexAdapter = new IndexAdapter< TKey >( m_indexList,
                                                                    ( kvp ) => kvp.Key,
                                                                    enumKeys().GetEnumerator );
                }
                return m_keyByIndexAdapter;
            }
        }

        private IndexAdapter< KeyValuePair< TKey, TValue > > m_kvpByIndex;

        public IReadOnlyList< KeyValuePair< TKey, TValue > > KvpsByIndex
        {
            get
            {
                if( null == m_kvpByIndex )
                {
                    m_kvpByIndex = new IndexAdapter< KeyValuePair< TKey, TValue > >( m_indexList,
                                                                                     ( kvp ) => kvp,
                                                                                     () => m_indexList.GetEnumerator() );
                }
                return m_kvpByIndex;
            }
        }
    } // end class ListIndexableDictionary
}

