using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace MS.Dbg
{
    [NsContainer( NameProperty = "TargetFriendlyName" )]
    [DebuggerDisplay( "DbgUModeProcess: id {DbgEngProcessId}: {TargetFriendlyName}" )]
    public class DbgUModeProcess : DbgTarget
    {
        // N.B. This assumes that the current debugger context is what is being represented.
     // internal DbgUModeProcess( DbgEngDebugger debugger )
     //     : this( debugger, debugger.GetCurrentDbgEngContext() )
     // {
     // }

        internal DbgUModeProcess( DbgEngDebugger debugger, DbgEngContext context )
            : this( debugger, context, DEBUG_ANY_ID )
        {
        }

        internal DbgUModeProcess( DbgEngDebugger debugger,
                                  DbgEngContext context,
                                  uint osProcessId )
            : this( debugger, context, osProcessId, null )
        {
        }

        internal DbgUModeProcess( DbgEngDebugger debugger,
                                  DbgEngContext context,
                                  uint osProcessId,
                                  string targetFriendlyName )
            : base( debugger, context, targetFriendlyName )
        {
            m_osProcessId = osProcessId;
        } // end constructor


        [NsLeafItem]
        public uint DbgEngProcessId
        {
            get
            {
                Util.Assert( 0 == (Context.ProcessIndexOrAddress & 0xffffffff00000000) );
                return (uint) Context.ProcessIndexOrAddress;
            }
        }


        private uint m_osProcessId;
        [NsLeafItem]
        public uint OsProcessId
        {
            get
            {
                if( DEBUG_ANY_ID == m_osProcessId )
                {
                    using( new DbgEngContextSaver( Debugger, Context ) )
                    {
                        m_osProcessId = Debugger.GetProcessSystemId();
                    }
                }
                return m_osProcessId;
            }
        } // end OsProcessId


        [NsContainer( "Threads" )]
        public IEnumerable<DbgUModeThreadInfo> EnumerateThreads()
        {
            using( new DbgEngContextSaver( Debugger, Context ) )
            {
                return Debugger.EnumerateThreads();
            }
        }

        public IEnumerable< object > TestStuff()
        {
            return Debugger.ExecuteOnDbgEngThread( () =>
            {
                Target.SymbolLocator.SymbolPath = Debugger.SymbolPath;
                string dacPath = Target.SymbolLocator.FindBinary( Target.ClrVersions[ 0 ].DacInfo );
                var rt = Target.ClrVersions[ 0 ].CreateRuntime( dacPath );
                Console.WriteLine( "{0} threads", rt.Threads.Count );
                List< object > list = new List< object >();
                foreach( var t in rt.Threads )
                {
                    Console.WriteLine( "Thread {0} ({1}):", t.ManagedThreadId, t.OSThreadId.ToString( "x" ) );
                    foreach( var f in t.StackTrace )
                    {
                        Console.WriteLine( "   {0}", f.DisplayString );
                        list.Add( f );
                    }
                }
                return list;
            } );
        }
    } // end class DbgUModeProcess
}

