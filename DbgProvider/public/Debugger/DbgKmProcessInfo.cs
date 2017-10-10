using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [NsContainer( NameProperty = "Name" )]
    [DebuggerDisplay( "DbgKmProcessInfo: (0x{Address.ToString(\"x\")})" )]
    public class DbgKmProcessInfo : DebuggerObject, IEquatable< DbgKmProcessInfo >//, IHaveName
    {
        private string __name;

        public string Name
        {
            get
            {
                if( null == __name )
                {
                    // TODO: try to get a nicer name (like "notepad (66a)")
                    __name = Address.ToString( "x16" );
                }
                return __name;
            }
        }

        private bool? __memoryUnavailable;

        private bool _IsMemoryUnavailable
        {
            get
            {
                if( null == __memoryUnavailable )
                {
                    // We don't necessarily know the size of an _EPROCESS; let's just read
                    // a single byte.
                    uint readSize = 1;

                    byte[] memDontCare;
                    __memoryUnavailable = !Debugger.TryReadMem( Address,
                                                                readSize,
                                                                true,
                                                                out memDontCare );
                }
                return (bool) __memoryUnavailable;
            }
        }

        public bool IsValueUnavailable { get { return _IsMemoryUnavailable; } }


    //  private DbgEngContext m_context;


        private DbgKModeTarget m_target;


        // TODO: This is also called "offset" in a lot of places. I think 'Address' is
        // a lot more user-friendly, but is it worth breaking with tradition?
        // This will throw if the value is unavailable...
        [NsLeafItem]
        public readonly ulong Address;

        //public DbgKmProcessInfo Parent { get; protected set; }

        public DbgKmProcessInfo( DbgEngDebugger debugger,
                                 DbgKModeTarget target,
                                 ulong address )
            : base( debugger )
        {
            if( null == target )
                throw new ArgumentNullException( "target" );

            Address = address;
            m_target = target;
        } // end constructor




        private void _CheckValueAvailable()
        {
            if( IsValueUnavailable )
                // TODO: value unavailable exception?
                throw new InvalidOperationException( "The value is unavailable." );
        }


        #region IEquatable< DbgKmProcessInfo > Stuff

        public bool Equals( DbgKmProcessInfo other )
        {
            if( null == other )
                return false;

            return Address == other.Address;
        } // end Equals()

        public override bool Equals( object obj )
        {
            return Equals( obj as DbgKmProcessInfo );
        }

        public override int GetHashCode()
        {
            return Address.GetHashCode();
        }

        public static bool operator ==( DbgKmProcessInfo s1, DbgKmProcessInfo s2 )
        {
            if( null == (object) s1 )
                return (null == (object) s2);

            return s1.Equals( s2 );
        }

        public static bool operator !=( DbgKmProcessInfo s1, DbgKmProcessInfo s2 )
        {
            return !(s1 == s2);
        }

        #endregion
    } // end class DbgKmProcessInfo
}

