using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents basic id information about a dbgeng.dlll "system".
    /// </summary>
    /// <remarks>
    ///    In the world of dbgeng.dll, a kernel-mode target constitutes a "system", a
    ///    collection of live user-mode processes constitute a "system", and a dump
    ///    constitutes a "system".
    /// </remarks>
    public class DbgSystemInfo
    {
        protected const uint DEBUG_ANY_ID = unchecked( 0xffffffff );

        public readonly uint SystemIndex;

        public readonly bool IsKernelTarget;


        protected DbgSystemInfo( uint systemIndex,
                                 bool isKernelTarget )
        {
            if( DEBUG_ANY_ID == systemIndex )
                throw new ArgumentOutOfRangeException( "systemIndex", "You must specify a valid system index." );

            SystemIndex = systemIndex;
            IsKernelTarget = isKernelTarget;
        } // end constructor

        public static DbgSystemInfo[] BuildSystemTree( DbgEngDebugger debugger )
        {
            uint originalSysId = DEBUG_ANY_ID;
            uint[] sysIds = debugger.GetDbgEngSystemIds();
            DbgSystemInfo[] sysInfos = new DbgSystemInfo[ sysIds.Length ];
            if( 0 != sysIds.Length )
                originalSysId = debugger.GetCurrentDbgEngSystemId();

            for( int i = 0; i < sysIds.Length; i++ )
            {
                debugger.SetCurrentDbgEngSystemId( sysIds[ i ] );
                if( debugger.IsKernelMode )
                {
                    sysInfos[ i ] = new DbgKmSystemInfo( sysIds[ i ] );
                }
                else
                {
                    // User-mode
                    uint[] dbgEngProcIds;
                    uint[] osPids;
                    debugger.GetProcessIds( out dbgEngProcIds, out osPids );
                    sysInfos[ i ] = new DbgUmSystemInfo( sysIds[ i ],
                                                         dbgEngProcIds,
                                                         osPids );
                }
            }

            if( originalSysId != DEBUG_ANY_ID )
                debugger.SetCurrentDbgEngSystemId( originalSysId );

            return sysInfos;
        } // end BuildSystemTree()

        public static void CompareTrees( DbgSystemInfo[] oldTree,
                                         DbgSystemInfo[] newTree,
                                         out List< DbgUmSystemInfo > umProcessesRemoved,
                                         out List< DbgUmSystemInfo > umProcessesAdded,
                                         out List< DbgKmSystemInfo > kmTargetsRemoved,
                                         out List< DbgKmSystemInfo > kmTargetsAdded )
        {
            umProcessesRemoved = new List< DbgUmSystemInfo >();
            umProcessesAdded   = new List< DbgUmSystemInfo >();
            kmTargetsRemoved   = new List< DbgKmSystemInfo >();
            kmTargetsAdded     = new List< DbgKmSystemInfo >();

            List< uint > sysIdsRemoved;
            List< uint > sysIdsAdded;
            List< uint > sysIdsBoth;
            DiffCompactSet( oldTree.Select( (si) => si.SystemIndex ).ToArray(),
                            newTree.Select( (si) => si.SystemIndex ).ToArray(),
                            out sysIdsAdded,
                            out sysIdsRemoved,
                            out sysIdsBoth );

            // This dictionary maps a system id to "before" and "after" DbgSystemInfos.
            // (the values will be an array of 2 DbgSystemInfo objects: [0] is "before"
            // and [1] is "after")
            var both = new Dictionary< uint, DbgSystemInfo[] >();
            const int idxBefore = 0;
            const int idxAfter = 1;

            foreach( var newInfo in newTree )
            {
                if( sysIdsAdded.Contains( newInfo.SystemIndex ) )
                {
                    if( newInfo.IsKernelTarget )
                        kmTargetsAdded.Add( (DbgKmSystemInfo) newInfo );
                    else
                        umProcessesAdded.Add( (DbgUmSystemInfo) newInfo );
                }
                else
                {
                    Util.Assert( sysIdsBoth.Contains( newInfo.SystemIndex ) );
                    var bothArray = new DbgSystemInfo[] { null, newInfo };
                    both.Add( newInfo.SystemIndex, bothArray );
                }
            } // end foreach( newInfo )

            foreach( var oldInfo in oldTree )
            {
                if( sysIdsRemoved.Contains( oldInfo.SystemIndex ) )
                {
                    if( oldInfo.IsKernelTarget )
                        kmTargetsRemoved.Add( (DbgKmSystemInfo) oldInfo );
                    else
                        umProcessesRemoved.Add( (DbgUmSystemInfo) oldInfo );
                }
                else
                {
                    Util.Assert( sysIdsBoth.Contains( oldInfo.SystemIndex ) );
                    both[ oldInfo.SystemIndex ][ idxBefore ] = oldInfo;
                }
            } // end foreach( oldInfo )

            foreach( var kvp in both )
            {
                if( kvp.Value[ idxBefore ].IsKernelTarget )
                {
                    // We don't have to peer into kernel targets to look for added/removed
                    // user-mode processes.
                    Util.Assert( kvp.Value[ idxAfter ].IsKernelTarget );
                    continue;
                }

                DbgUmSystemInfo beforeInfo = (DbgUmSystemInfo) kvp.Value[ idxBefore ];
                DbgUmSystemInfo afterInfo = (DbgUmSystemInfo) kvp.Value[ idxAfter ];

                List< uint > procIdsRemoved;
                List< uint > procIdsAdded;
                List< uint > procIdsBoth;

                DiffCompactSet( beforeInfo.ProcessIds,
                                afterInfo.ProcessIds,
                                out procIdsAdded,
                                out procIdsRemoved,
                                out procIdsBoth );

                if( 0 != procIdsAdded.Count )
                {
                    // TODO: If we have to deal with a lot of processes, this could be
                    // optimized in a few ways: 1) we could take advantage of the fact
                    // that DiffCompactSet returns things in sorted order, so that we
                    // don't have to search the entire OS pid list each time; or 2) we
                    // could just use an array of DEBUG_ANY_ID and get the OS ids
                    // on-demand.
                    //
                    // For the removed processes, though... we might not have any other
                    // way to get the OS pid.
                    uint[] osPids = new uint[ procIdsAdded.Count ];
                    for( int i = 0; i < osPids.Length; i++ )
                    {
                        bool found = false;
                        for( int j = 0; j < afterInfo.ProcessIds.Count; j++ )
                        {
                            if( afterInfo.ProcessIds[ j ] == procIdsAdded[ i ] )
                            {
                                osPids[ i ] = afterInfo.ProcessOsIds[ j ];
                                found = true;
                            }
                        }
                        Util.Assert( found );
                    }

                    umProcessesAdded.Add( new DbgUmSystemInfo( kvp.Key, procIdsAdded, osPids ) );
                }

                if( 0 != procIdsRemoved.Count )
                {
                    uint[] osPids = new uint[ procIdsRemoved.Count ];
                    for( int i = 0; i < osPids.Length; i++ )
                    {
                        bool found = false;
                        for( int j = 0; j < beforeInfo.ProcessIds.Count; j++ )
                        {
                            if( beforeInfo.ProcessIds[ j ] == procIdsRemoved[ i ] )
                            {
                                osPids[ i ] = beforeInfo.ProcessOsIds[ j ];
                                found = true;
                            }
                        }
                        Util.Assert( found );
                    }
                    umProcessesRemoved.Add( new DbgUmSystemInfo( kvp.Key, procIdsRemoved, osPids ) );
                }
            } // end foreach( sameInfo )
        } // end CompareTrees()


        /// <summary>
        ///    Calculates the difference between two "compact sets".
        /// </summary>
        /// <remarks>
        ///    Compact set: a set of numbers that dbgeng.dll would come up with for an engine
        ///    id. Dbgeng.dll id numbers look suspiciously like indexes (always start at 0 and
        ///    grow by 1). When an item (system, process, thread) is removed and the id is
        ///    removed, that index later gets re-used, so the highest id always remains
        ///    relatively low. There are never duplicates.
        /// </remarks>
        internal static void DiffCompactSet( IReadOnlyList< uint > oldSet,
                                             IReadOnlyList< uint > newSet,
                                             out List< uint > added,
                                             out List< uint > removed,
                                             out List< uint > both )
        {
            int maxItems = oldSet.Count + newSet.Count;
            added = new List< uint >( maxItems + 1 );
            removed = new List< uint >( maxItems + 1 );
            both = new List< uint >( maxItems + 1 );

            uint[] diffmap = new uint[ maxItems * 3 ];

            foreach( uint oldItem in oldSet )
            {
                if( oldItem >= diffmap.Length )
                    throw new NotSupportedException( "Index too big: " + oldItem.ToString() );

                if( diffmap[ oldItem ] != 0 )
                    throw new ArgumentException( "Duplicates in oldSet." );

                diffmap[ oldItem ] += 0x01;
            } // end foreach( oldItem )

            foreach( uint newItem in newSet )
            {
                if( newItem >= diffmap.Length )
                    throw new NotSupportedException( "Index too big: " + newItem.ToString() );

                if( 0 != (diffmap[ newItem ] & 0x02) )
                    throw new ArgumentException( "Duplicates in newSet." );

                diffmap[ newItem ] += 0x02;
            } // end foreach( newItem )

            for( int idx = 0; idx < diffmap.Length; idx++ )
            {
                if( 0x01 == diffmap[ idx ] )
                    removed.Add( (uint) idx );
                else if( 0x02 == diffmap[ idx ] )
                    added.Add( (uint) idx );
                else if( 0x03 == diffmap[ idx ] )
                    both.Add( (uint) idx );
                // else it was in neither
            } // end foreach( idx )
        } // end DiffCompactSet()

        // TODO: test code for DiffCompactSet.
        // TODO: test code for DiffCompactSet.
        // TODO: test code for DiffCompactSet.
        // TODO: test code for DiffCompactSet.
        // TODO: test code for DiffCompactSet.
        // TODO: test code for DiffCompactSet.
        // TODO: test code for DiffCompactSet.
    } // end class DbgSystemInfo


    [DebuggerDisplay( "DbgUmSystemInfo: id {SystemIndex} ({ProcessIds.Count} processes)" )]
    public class DbgUmSystemInfo : DbgSystemInfo
    {
        public readonly IReadOnlyList< uint > ProcessIds;
        // These aren't needed; but we'll keep track of them in order to not waste calls
        // into dbgeng, since it gives us engine ids and OS ids at the same time.
        public readonly IReadOnlyList< uint > ProcessOsIds;


        public DbgUmSystemInfo( uint systemIndex,
                                IReadOnlyList< uint > processIds,
                                IReadOnlyList< uint > processOsIds )
            : base( systemIndex, false )
        {
            if( null == processIds )
                throw new ArgumentNullException( "processIds" );

            if( null == processOsIds )
                throw new ArgumentNullException( "processOsIds" );

            foreach( uint procId in processIds )
            {
                if( DEBUG_ANY_ID == procId )
                    throw new ArgumentOutOfRangeException( "processIds", "You must specify valid process indexes." );
            }

            // ProcessOsIds, on the other hand, can be DEBUG_ANY_ID.

            ProcessIds = processIds;
            ProcessOsIds = processOsIds;
        } // end constructor
    } // end class DbgUmSystemInfo


    [DebuggerDisplay( "DbgKmSystemInfo: id {SystemIndex}" )]
    public class DbgKmSystemInfo : DbgSystemInfo
    {
        public DbgKmSystemInfo( uint systemIndex )
            : base( systemIndex, true )
        {
        } // end constructor
    } // end class DbgKmSystemInfo
}
