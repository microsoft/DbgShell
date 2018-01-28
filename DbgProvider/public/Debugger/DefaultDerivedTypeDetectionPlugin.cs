using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MS.Dbg
{
    /// <summary>
    ///    The default DTD plugin attempts to find a symbolic relationship between the two
    ///    types. For most situations, this should be sufficient.
    /// </summary>
    internal class DefaultDerivedTypeDetectionPlugin : IDbgDerivedTypeDetectionPlugin
    {
        public string Name { get { return "(built-in default)";  } }

        public bool TryFindPossibleOffsetFromDerivedClass( DbgEngDebugger debugger,
                                                           ILogger logger,
                                                           ulong vtableAddr,
                                                           ulong firstSlotPtr,
                                                           DbgUdtTypeInfo derivedType,  // AKA the implementing type
                                                           DbgUdtTypeInfo baseType,     // AKA the declaredType
                                                           out int offset )
        {
            offset = 0;

            List< int > possibleOffsets = derivedType.FindPossibleOffsetsFromDerivedClass( baseType );
            if( 0 == possibleOffsets.Count )
            {
                //
                // Special case: if we are crossing module boundaries, but the type names
                // are the same, one module may have just a "stub" type definition
                // (vtables but no other members). In that case, we'll assume that the
                // offset is 0.
                //

                if( 0 == Util.Strcmp_OI( derivedType.Name, baseType.Name ) )
                {
                    Util.Assert( derivedType.Module != baseType.Module );

                    LogManager.Trace( "Type match between modules: {0} -> {1}",
                                      baseType.Module.Name,
                                      derivedType.Module.Name );

                    offset = 0;
                    return true;
                }

                LogManager.Trace( "Could not find a relationship between {0} (declared) and {1} (alleged concrete/derived).",
                                  baseType.Name,
                                  derivedType.Name );
                // It could just be because we're looking at garbage data.
                return false;
            }
            else if( 1 == possibleOffsets.Count )
            {
                offset = possibleOffsets[ 0 ];
                return true;
            }
            else
            {
                LogManager.Trace( "Thre are {0} possible offsets: {1}",
                                  possibleOffsets.Count,
                                  String.Join( ", ", possibleOffsets ) );
                // This can happen, for instance, in the "manual IUnknown" case. We could
                // have multiple positions where IUnknown shows up, and we're not sure
                // which one we have:
                //
                //    > $t
                //    ieframe!CTabProcessReference (size 0x48)
                // -->   +0x000 CRefThread: VTable for IUnknown (rel. offset 0, 3 slots)
                //       +0x004 _pcRef              : Int4B*
                //       +0x008 _idThread           : UInt4B
                //       +0x00c _fProcessReference  : bool
                //       +0x010 _cDestructs         : Int4B
                // -->   +0x014 IWaitOnThreadsBeforeExit: VTable for IUnknown (rel. offset 0, 3 slots)
                //       +0x018 _dsaThreadsToWaitOn : CDSA<void *>
                //       +0x020 _tLastPrune         : UInt8B
                //       +0x028 _fInDestructor      : bool
                //       +0x02c _csDSA              : _RTL_CRITICAL_SECTION
                //
                // If there are multiple possibilities, some of them will point to
                // "adjustor thunks" (see
                // http://blogs.msdn.com/b/oldnewthing/archive/2004/02/06/68695.aspx).
                // There isn't any symbolic way to get the info, so we fall back once
                // again to parsing it out of a symbol name.
                //
                if( _TryDiscernOffsetFromAdjustorThunk( debugger, firstSlotPtr, out offset ) )
                {
                    Util.Assert( possibleOffsets.Contains( offset ) );
                    return true;
                }
                LogManager.Trace( "_TryDetectDerivedType: Multiple possible offsets; couldn't pick one, between {0} (declared) and {1} (alleged concrete/derived).",
                                  baseType.Name,
                                  derivedType.Name );
                return false;
            } // end multiple possible offsets
        } // end TryFindPossibleOffsetFromDerivedClass()


        private static Regex sm_adjustorThunkRegex = new Regex( @".*`adjustor\{(?<decimalOffset>\d+)\}'$",
                                                                RegexOptions.Compiled );

        /// <summary>
        ///    Give a vtable pointer, we get the symbolic name for the first slot. If it
        ///    looks like an adjustor thunk, we return the adjustment offset; else 0.
        /// </summary>
        private static bool _TryDiscernOffsetFromAdjustorThunk( DbgEngDebugger debugger,
                                                                ulong firstSlotPtr,
                                                                out int offset )
        {
            offset = 0;
            try
            {
                ulong disp;
                // There should be at least one slot... else why have a vtable?
                ulong firstSlotAddr = debugger.ReadMemAs_pointer( firstSlotPtr );
                string slotSymName = debugger.GetNameByOffset( firstSlotAddr, out disp );

                if( 0 != disp )
                {
                    LogManager.Trace( "Warning: could not get exact symbolic name for slot in vftable." );
                    return false;
                }

                var match = sm_adjustorThunkRegex.Match( slotSymName );
                if( !match.Success )
                {
                    LogManager.Trace( "_TryDiscernOffsetFromAdjustorThunk: Hm, this doesn't look like an adjustor thunk: {0}",
                                       slotSymName );
                    // No adjustor thunk? I guess that means the offset should be 0.
                    return true;
                }
                if( !Int32.TryParse( match.Groups[ "decimalOffset" ].Value, out offset ) )
                {
                    Util.Fail( "Int32.TryParse should not have failed, because the regex succeeded." );
                    return false;
                }
                offset = -offset; // it's expressed opposite of how we want it
                LogManager.Trace( "Found offset based on adjustor thunk: {0}", offset );
                return true;
            }
            catch( DbgProviderException dpe )
            {
                LogManager.Trace( "Warning: _TryDiscernOffsetFromAdjustorThunk: failed reading memory or getting symbolic name. Probably bad data: {0}",
                                  Util.GetExceptionMessages( dpe ) );
                return false;
            }
        } // end _TryDiscernOffsetFromAdjustorThunk
    } // end class DefaultDerivedTypeDetectionPlugin
}

