using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MS.Dbg
{
    /// <summary>
    ///    Like BitArray, but can be made readonly, and provides some additional methods.
    /// </summary>
    public class FreezableBitArray : ICollection, IEnumerable< bool >, ICloneable
    {
        private BitArray m_ba;
        private bool m_isReadOnly;


        public FreezableBitArray( int length )
        {
            m_ba = new BitArray( length );
        }

        private void _Init( int length, int[] raw )
        {
            if( null == raw )
                throw new ArgumentNullException( "raw" );

            if( length > (raw.Length * 32) )
            {
                throw new ArgumentOutOfRangeException( "length",
                                                       length,
                                                       "The length parameter does not fit with the raw data provided (too large)." );
            }

            if( (0 != raw.Length) &&
                (length < (((raw.Length - 1) * 32) + 1)) )
            {
                throw new ArgumentOutOfRangeException( "length",
                                                       length,
                                                       "The length parameter does not fit with the raw data provided (too small)." );
            }

            m_ba = new BitArray( raw );

            // Unfortunately, there is no BitArray constructor that lets us both pass the
            // raw bits AND say how many of those bits are valid. I'm gonna hack it.
            // TODO: Feature request.
            m_ba.GetType().GetField( "m_length", BindingFlags.Instance | BindingFlags.NonPublic )
                          .SetValue( m_ba, length );
        } // end _Init()

        public FreezableBitArray( int length, IList< int > raw )
        {
            if( raw is int[] )
                _Init( length, ((int[]) raw) );
            else
                _Init( length, raw.ToArray() );
        }


        public FreezableBitArray( int length, int[] raw )
        {
            _Init( length, raw );
        } // end constructor

        private FreezableBitArray( FreezableBitArray other )
        {
            m_ba = new BitArray( other.m_ba );
            // TODO: Perhaps I should let you make a non-readonly copy.
            m_isReadOnly = other.m_isReadOnly;
        } // end copy constructor


        private void _CheckReadonly()
        {
            if( m_isReadOnly )
                throw new InvalidOperationException( "This object is read-only." );
        }

        public void Freeze()
        {
            m_isReadOnly = true;
        }

        //
        // BitArray stuff
        //

        public bool this[ int index ]
        {
            get
            {
                return m_ba[ index ];
            }
            set
            {
                _CheckReadonly();
                m_ba[ index ] = value;
            }
        }

        public FreezableBitArray And( FreezableBitArray other )
        {
            return And( other.m_ba );
        }

        public FreezableBitArray And( BitArray other )
		{
            _CheckReadonly();
            m_ba.And( other );
            return this;
		}

		public FreezableBitArray Or( FreezableBitArray other )
        {
            return Or( other.m_ba );
        }

		public FreezableBitArray Or( BitArray other )
		{
            _CheckReadonly();
            m_ba.Or( other );
            return this;
		}

		public FreezableBitArray Xor( FreezableBitArray other )
        {
            return Xor( other.m_ba );
        }

		public FreezableBitArray Xor( BitArray other )
		{
            _CheckReadonly();
            m_ba.Xor( other );
            return this;
		}

		public FreezableBitArray Not()
		{
            _CheckReadonly();
            m_ba.Not();
            return this;
		}

        public void SetAll( bool value )
        {
            m_ba.SetAll( value );
        }

        //
        // Extra stuff
        //

        public IEnumerable< int > GetTrueIndices()
        {
            for( int i = 0; i < Count; i++ )
            {
                if( this[ i ] )
                    yield return i;
            }
        }

        public IEnumerable< int > GetFalseIndices()
        {
            for( int i = 0; i < Count; i++ )
            {
                if( !this[ i ] )
                    yield return i;
            }
        }

        public bool GetIsAllTrue()
        {
            for( int i = 0; i < Count; i++ )
            {
                if( !this[ i ] )
                    return false;
            }
            return true;
        }

        public bool GetIsAllFalse()
        {
            for( int i = 0; i < Count; i++ )
            {
                if( this[ i ] )
                    return false;
            }
            return true;
        }

        // In case you need an actual BitArray for some interop scenario. Note that this
        // makes a copy.
        public BitArray ToBitArray()
        {
            return new BitArray( m_ba );
        }

        //
        // ICollection, IEnumerable< bool >, ICloneable stuff.
        //

        public void CopyTo( bool[] array, int arrayIndex )
        {
            CopyTo( ((Array) array), arrayIndex );
        }

        public int Count
        {
            get { return m_ba.Count; }
        }

        public bool IsReadOnly
        {
            get { return m_isReadOnly; }
        }

        public IEnumerator< bool > GetEnumerator()
        {
            // This approach involves lots of boxing.
         // foreach( var b in m_ba )
         // {
         //     yield return (bool) b;
         // }
            for( int i = 0; i < Count; i++ )
            {
                yield return m_ba[ i ];
            }
        } // end GetEnumerator()

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public object Clone()
        {
            return new FreezableBitArray( this );
        }

        public void CopyTo( Array array, int index )
        {
            m_ba.CopyTo( array, index );
        }

        public bool IsSynchronized
        {
            get { return m_ba.IsSynchronized; }
        }

        public object SyncRoot
        {
            get { return m_ba.SyncRoot; }
        }
    } // end class FreezableBitArray
}

