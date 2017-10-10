using System;

namespace MS.Dbg
{
    [Flags]
    internal enum ValueOptions
    {
        Raw                      = 0,
        DoDerivedTypeDetection   = 1,  // "stock" value
        DoSymbolValueConversion  = 2,
        DoBoth                   = 3,  // default
    }

    internal class DbgValueCache< TValue >
    {
        private readonly TValue[] m_cache = new TValue[ 4 ]; // one for each possible ValueOption


        internal static ValueOptions ComputeValueOptions( bool skipConversion,
                                                          bool skipDerivedTypeDetection )
        {
            ValueOptions valOpts = ValueOptions.Raw;

            if( !skipConversion )
                valOpts |= ValueOptions.DoSymbolValueConversion;

            if( !skipDerivedTypeDetection )
                valOpts |= ValueOptions.DoDerivedTypeDetection;

            return valOpts;
        } // end _ComputeValueOptions()


        public TValue this[ bool skipConversion, bool skipDerivedTypeDetection ]
        {
            get
            {
                ValueOptions valOpts = ComputeValueOptions( skipConversion,
                                                            skipDerivedTypeDetection );

                return m_cache[ (int) valOpts ];
            }

            set
            {
                ValueOptions valOpts = ComputeValueOptions( skipConversion,
                                                            skipDerivedTypeDetection );

                m_cache[ (int) valOpts ] = value;
            }
        } // end indexer


        /// <summary>
        ///    Internal ref-getter (which allows the caller to set the value).
        /// </summary>
        internal ref TValue this[ ValueOptions valOpts ]
        {
            get
            {
                return ref m_cache[ (int) valOpts ];
            }
        } // end ref-getter indexer


        public void Clear()
        {
            m_cache[ 0 ] = default( TValue );
            m_cache[ 1 ] = default( TValue );
            m_cache[ 2 ] = default( TValue );
            m_cache[ 3 ] = default( TValue );
        }
    } // end class DbgValueCache
}

