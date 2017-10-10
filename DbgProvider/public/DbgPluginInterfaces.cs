using System;
using System.Collections.Generic;

namespace MS.Dbg
{
    /// <summary>
    ///    Allows custom logic for determining if two types are related.
    /// </summary>
    public interface IDbgDerivedTypeDetectionPlugin
    {
        string Name { get; }

        /// <summary>
        ///    Attempts to finds the offset of the base type relative to the derived type.
        /// </summary>
        /// <remarks>
        ///    For instance, say derivedType is CFoo, and baseType is IFoo. CFoo doesn't
        ///    only implement IFoo, however; it also derives from some other classes, so
        ///    it has a vtable for IFoo at offset N. In that case, this method should
        ///    return -N.
        ///
        ///    Note that firstSlotPtr is sort of redundant, since you can get it by
        ///    reading the first pointer at vtableAddr. But I'll already have done that,
        ///    so I might as well save the plugin a trip.
        /// </remarks>
        bool TryFindPossibleOffsetFromDerivedClass( DbgEngDebugger debugger,
                                                    ILogger logger,
                                                    ulong vtableAddr,
                                                    ulong firstSlotPtr,
                                                    DbgUdtTypeInfo derivedType,
                                                    DbgUdtTypeInfo baseType,
                                                    out int offset );
    } // end interface IDbgDerivedTypeDetectionPlugin
}

