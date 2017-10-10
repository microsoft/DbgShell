using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using MS.Dbg.Formatting.Commands;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents a collection of PSObject which can be indexed by a PSObject key, a
    ///    string key, or by numeric index (by order added). Suitable for representing
    ///    std::map or std::multimap.
    /// </summary>
    public class PsIndexedDictionary : IReadOnlyDictionary< PSObject, IReadOnlyList< PSObject > >,
                                       IEnumerable<KeyValuePair<PSObject,PSObject>>
    {
        private class PSObjectComparer : IEqualityComparer< PSObject >, IComparer< PSObject >
        {
            public static readonly PSObjectComparer Instance = new PSObjectComparer();

            private PSObjectComparer() { }

            // IEqualityComparer< PSObject > Members

            public bool Equals( PSObject x, PSObject y )
            {
                return 0 == Compare( x, y );
            }

            public int GetHashCode( PSObject obj )
            {
                if( obj.BaseObject is string )
                {
                    return StringComparer.InvariantCultureIgnoreCase.GetHashCode( ((string) obj.BaseObject) );
                }
                return obj.GetHashCode();
            }

            // IComparer< PSObject > Members

            public int Compare( PSObject x, PSObject y )
            {
                return LanguagePrimitives.Compare( x, y, ignoreCase: true );
            }
        } // end class PSObjectComparer


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
        private class IndexAdapter : IReadOnlyList< PSObject >
        {
            private List< KeyValuePair< PSObject, PSObject > > m_indexList;
            private IndexAdapterReturns m_returns;

            public IndexAdapter( List< KeyValuePair< PSObject, PSObject > > indexList,
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


            public IEnumerable< PSObject > Keys
            {
                get
                {
                    foreach( var kvp in m_indexList )
                    {
                        yield return kvp.Key;
                    }
                }
            }

            public IEnumerable< PSObject > Values
            {
                get
                {
                    foreach( var kvp in m_indexList )
                    {
                        yield return kvp.Value;
                    }
                }
            }

            public IEnumerable< PSObject > KeyValuePairs
            {
                get
                {
                    foreach( var kvp in m_indexList )
                    {
                        yield return new PSObject( kvp );
                    }
                }
            }

            public PSObject this[ int key ]
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
                            return new PSObject( m_indexList[ _FixIndex( key ) ] );
                        default:
                            throw new Exception( "unexpected enum value" );
                    }
                }
            }

            public int Count
            {
                get { return m_indexList.Count; }
            }

            public IEnumerator<PSObject> GetEnumerator()
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


        private List< KeyValuePair< PSObject, PSObject > > m_indexList;
        private Dictionary< PSObject, List< PSObject > > m_dict;
        private Dictionary< string, List< PSObject > > m_stringDict;
        private bool m_isReadOnly;


        public PsIndexedDictionary()
        {
            m_indexList = new List< KeyValuePair< PSObject, PSObject > >();
            m_dict = new Dictionary< PSObject, List< PSObject > >( PSObjectComparer.Instance );
            m_stringDict = new Dictionary< string, List< PSObject > >( StringComparer.OrdinalIgnoreCase );
        } // end constructor

        public PsIndexedDictionary( int capacity )
        {
            m_indexList = new List< KeyValuePair< PSObject, PSObject > >( capacity );
            m_dict = new Dictionary< PSObject, List< PSObject > >( capacity, PSObjectComparer.Instance );
            m_stringDict = new Dictionary< string, List< PSObject > >( capacity, StringComparer.OrdinalIgnoreCase );
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

        private static string _CreateStringKey( PSObject psoKey )
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


        public void Add( PSObject key, PSObject value )
        {
            _CheckReadOnly();
            m_indexList.Add( new KeyValuePair< PSObject, PSObject >( key, value ) );
            List< PSObject > list;

            if( !m_dict.TryGetValue( key, out list ) )
            {
                list = new List< PSObject >();
                m_dict.Add( key, list );
            }
            list.Add( value );

            string stringKey = _CreateStringKey( key );

            if( !m_stringDict.TryGetValue( stringKey, out list ) )
            {
                list = new List< PSObject >();
                m_stringDict.Add( stringKey, list );
            }
            list.Add( value );
        } // end Add

        public bool ContainsKey( PSObject key )
        {
            return m_dict.ContainsKey( key );
        }

        public bool ContainsKey( string stringKey )
        {
            return m_stringDict.ContainsKey( stringKey );
        }

        public IEnumerable< PSObject > Keys
        {
            get { return m_dict.Keys; }
        }

        public ICollection< String > StringKeys
        {
            get { return m_stringDict.Keys; }
        }

        #region ICollection< KeyValuePair< PSObject, PSObject > > Members

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

        #region IEnumerable< KeyValuePair< PSObject, PSObject > > Members

        public IEnumerator< KeyValuePair< PSObject, PSObject > > GetIndividualItemEnumerator()
        {
            return m_indexList.GetEnumerator();
        }

        IEnumerator< KeyValuePair< PSObject, PSObject > >
        IEnumerable< KeyValuePair< PSObject, PSObject > >.GetEnumerator()
        {
            return GetIndividualItemEnumerator();
        }

        #endregion

        #region IEnumerable Members

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable< KeyValuePair< PSObject, IReadOnlyList< PSObject > > >) this).GetEnumerator();
        }

        #endregion

        public bool TryGetValue( PSObject key, out IReadOnlyList< PSObject > value )
        {
            List< PSObject > list;
            if( m_dict.TryGetValue( key, out list ) )
            {
                value = new ReadOnlyCollection< PSObject >( list );
                return true;
            }
            value = null;
            return false;
        }

        public bool TryGetValue( string stringKey, out IReadOnlyList< PSObject > value )
        {
            List< PSObject > list;
            if( m_stringDict.TryGetValue( stringKey, out list ) )
            {
                value = new ReadOnlyCollection< PSObject >( list );
                return true;
            }
            value = null;
            return false;
        }

        public IEnumerable< IReadOnlyList< PSObject > > Values
        {
            get
            {
                foreach( var val in m_dict.Values )
                {
                    yield return new ReadOnlyCollection< PSObject >( val );
                }
            }
        }

        public IReadOnlyList< PSObject > this[ PSObject key ]
        {
            get
            {
                return new ReadOnlyCollection< PSObject >( m_dict[ key ] );
            }
        }

        public IReadOnlyList< PSObject > this[ string stringKey ]
        {
            get
            {
                return new ReadOnlyCollection< PSObject >( m_stringDict[ stringKey ] );
            }
        }

        #region IEnumerable< KeyValuePair< PSObject, IReadOnlyList< PSObject > > > Members

        IEnumerator< KeyValuePair< PSObject, IReadOnlyList< PSObject > > >
        IEnumerable< KeyValuePair< PSObject, IReadOnlyList< PSObject > > >.GetEnumerator()
        {
            foreach( var kvp in m_dict )
            {
                yield return new KeyValuePair< PSObject, IReadOnlyList< PSObject > >(
                    kvp.Key, new ReadOnlyCollection< PSObject >( kvp.Value ) );
            }
        }

        #endregion


        private IndexAdapter m_valByIndexAdapter;

        public IReadOnlyList< PSObject > ValueByIndex
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

        public IReadOnlyList< PSObject > KeyByIndex
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

        public IReadOnlyList< PSObject > KvpByIndex
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

