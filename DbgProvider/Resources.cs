//
//
//
//    IMPORTANT: This is generated code. Do not edit.
//
//
//
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.ComponentModel;
using System.Diagnostics;
using System.Resources;

namespace MS.Dbg
{

    internal class Resources
    {
        private ResourceManager m_resMan;

        private CultureInfo m_culture;

        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources()
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources( CultureInfo ci )
        {
            m_culture = ci;
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Advanced)]
        internal ResourceManager ResourceManager
        {
            get
            {
                if( null == m_resMan )
                {
                    m_resMan = new ResourceManager( "MS.Dbg.Resources", typeof( Resources ).Assembly );
                }
                return m_resMan;
            }
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Advanced)]
        internal CultureInfo Culture
        {
            get { return m_culture; }
            set { m_culture = value; }
        }

        private ConcurrentDictionary< ResourceId, string > m_resources = new ConcurrentDictionary< ResourceId, string >();
        public string this[ ResourceId index ]
        {
            get
            {
                string resource;
                if( !m_resources.TryGetValue( index, out resource ) )
                {
                    resource = ResourceManager.GetString( index.ToString(), m_culture );
                    m_resources.TryAdd( index, resource );
                }
                if( null == resource )
                {
                    System.Diagnostics.Debug.Fail( String.Format( Culture, "No resource for resource id {0}. You probably need to either rebuild or get matching binaries.", index ) );
                    throw new ArgumentException( String.Format( Culture, "No such resource id {0}.", index ), "index" );
                }
                return resource;
            }
        }

        public static Resources Loc = new Resources( CultureInfo.CurrentUICulture );
        public static Resources Neu = new Resources( CultureInfo.InvariantCulture );


        public static CultureMap CultureMap = new CultureMap();

        
        public static MulticulturalString WarningBadActivityIdFmt
        {
            get
            {
                return new MulticulturalString( (ci) => CultureMap[ ci ][ ResourceId.WarningBadActivityIdFmt ] );
            }
        }

        public static MulticulturalString ErrMsgSeeErrorRecord
        {
            get
            {
                return new MulticulturalString( (ci) => CultureMap[ ci ][ ResourceId.ErrMsgSeeErrorRecord ] );
            }
        }

        public static MulticulturalString ErrMsgCouldNotStartLoggingFmt
        {
            get
            {
                return new MulticulturalString( (ci) => CultureMap[ ci ][ ResourceId.ErrMsgCouldNotStartLoggingFmt ] );
            }
        }

        public static MulticulturalString ErrMsgTraceFailureFmt
        {
            get
            {
                return new MulticulturalString( (ci) => CultureMap[ ci ][ ResourceId.ErrMsgTraceFailureFmt ] );
            }
        }

        public static MulticulturalString ErrMsgTracingFailed
        {
            get
            {
                return new MulticulturalString( (ci) => CultureMap[ ci ][ ResourceId.ErrMsgTracingFailed ] );
            }
        }

        public static MulticulturalString ErrMsgCouldNotSaveTraceFmt
        {
            get
            {
                return new MulticulturalString( (ci) => CultureMap[ ci ][ ResourceId.ErrMsgCouldNotSaveTraceFmt ] );
            }
        }

        public static MulticulturalString ErrMsgTraceFileAlreadyExistsFmt
        {
            get
            {
                return new MulticulturalString( (ci) => CultureMap[ ci ][ ResourceId.ErrMsgTraceFileAlreadyExistsFmt ] );
            }
        }

        public static MulticulturalString ErrMsgCannotCreateSubkeyFmt
        {
            get
            {
                return new MulticulturalString( (ci) => CultureMap[ ci ][ ResourceId.ErrMsgCannotCreateSubkeyFmt ] );
            }
        }

        public static MulticulturalString ErrMsgCreateFileFailedFmt
        {
            get
            {
                return new MulticulturalString( (ci) => CultureMap[ ci ][ ResourceId.ErrMsgCreateFileFailedFmt ] );
            }
        }

        public static MulticulturalString ErrMsgUnhandledExceptionFmt
        {
            get
            {
                return new MulticulturalString( (ci) => CultureMap[ ci ][ ResourceId.ErrMsgUnhandledExceptionFmt ] );
            }
        }

        public static MulticulturalString ExMsgEndOfInnerExceptionStack
        {
            get
            {
                return new MulticulturalString( (ci) => CultureMap[ ci ][ ResourceId.ExMsgEndOfInnerExceptionStack ] );
            }
        }

        public static MulticulturalString ExMsgInvalidRegistryDataFmt
        {
            get
            {
                return new MulticulturalString( (ci) => CultureMap[ ci ][ ResourceId.ExMsgInvalidRegistryDataFmt ] );
            }
        }

        public static MulticulturalString ExMsgCallbackDisposedFmt
        {
            get
            {
                return new MulticulturalString( (ci) => CultureMap[ ci ][ ResourceId.ExMsgCallbackDisposedFmt ] );
            }
        }

    } // end class Resources


    class CultureMap
    {
        private static ConcurrentDictionary< CultureInfo, Resources > sm_cultureMap = new ConcurrentDictionary< CultureInfo, Resources >();

        static CultureMap()
        {
            sm_cultureMap.TryAdd( CultureInfo.CurrentUICulture, Resources.Loc );
            sm_cultureMap.TryAdd( CultureInfo.InvariantCulture, Resources.Neu );
        }

        public Resources this[ CultureInfo ci ]
        {
            get
            {
                Resources r;
                if( !sm_cultureMap.TryGetValue( ci, out r ) )
                {
                    r = new Resources( ci );
                    sm_cultureMap.TryAdd( ci, r );
                }
                return r;
            }
        }
    }


    internal enum ResourceId
    {
        WarningBadActivityIdFmt,
        ErrMsgSeeErrorRecord,
        ErrMsgCouldNotStartLoggingFmt,
        ErrMsgTraceFailureFmt,
        ErrMsgTracingFailed,
        ErrMsgCouldNotSaveTraceFmt,
        ErrMsgTraceFileAlreadyExistsFmt,
        ErrMsgCannotCreateSubkeyFmt,
        ErrMsgCreateFileFailedFmt,
        ErrMsgUnhandledExceptionFmt,
        ExMsgEndOfInnerExceptionStack,
        ExMsgInvalidRegistryDataFmt,
        ExMsgCallbackDisposedFmt,
        
    }
}

