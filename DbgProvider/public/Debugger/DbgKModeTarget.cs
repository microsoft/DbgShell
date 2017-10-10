using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace MS.Dbg
{
    [NsContainer( NameProperty = "TargetFriendlyName" )]
    [DebuggerDisplay( "DbgKModeTarget: id {DbgEngSystemId}: {TargetFriendlyName}" )]
//  public class DbgKModeTarget : DebuggerObject,
//                                ICanSetContext,
//                                IEquatable< DbgKModeTarget >,
//                                IComparable< DbgKModeTarget >
    public class DbgKModeTarget : DbgTarget
    {
        // N.B. This assumes that the current debugger context is what is being represented.
     // internal DbgKModeTarget( DbgEngDebugger debugger )
     //     : this( debugger, debugger.GetCurrentDbgEngContext() )
     // {
     // }

        internal DbgKModeTarget( DbgEngDebugger debugger, DbgEngContext context )
            : this( debugger, context, null )
        {
        }

        internal DbgKModeTarget( DbgEngDebugger debugger,
                                 DbgEngContext context,
                                 string targetFriendlyName )
            : base( debugger, context, targetFriendlyName )
        {
        } // end constructor


        [NsContainer( "Processes" )]
        public IEnumerable< DbgKmProcessInfo > EnumerateProcesses()
        {
            using( new DbgEngContextSaver( Debugger, Context ) )
            {
                IEnumerator< DbgKmProcessInfo > iter = null;
                bool error = false;

                try
                {
                    iter = Debugger.KmEnumerateProcesses().GetEnumerator();
                }
                catch( DbgProviderException dpe )
                {
                    error = true;
                    LogManager.Trace( "KmEnumerateProcesses failed (initializing enumeration): {0}",
                                      Util.GetExceptionMessages( dpe ) );
                }

                if( null != iter )
                {
                    while( iter.MoveNext() )
                    {
                        DbgKmProcessInfo kmp = null;
                        try
                        {
                            kmp = iter.Current;
                        }
                        catch( DbgProviderException dpe )
                        {
                            error = true;
                            LogManager.Trace( "KmEnumerateProcesses failed: {0}",
                                              Util.GetExceptionMessages( dpe ) );
                            break;
                        }

                        yield return kmp;
                    }
                }

                if( !error )
                    yield break;

                //
                // We might not have enough memory in the dump to enumerate all processes
                // (we might not even have nt!PsActiveProcessHead). In that case, we'll
                // just return the one process we (hopefully) know about: the current one.
                //

                ulong addr = Debugger.GetImplicitProcessDataOffset();

                yield return new DbgKmProcessInfo( Debugger, this, addr );
            } // end using( DbgEngContextSaver )
        } // end EnumerateProcesses()
    } // end class DbgKModeTarget
}

