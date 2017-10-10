using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents a base class type (normal or virtual).
    /// </summary>
    /// <remarks>
    ///    BaseClass objects are a little confusing. They are just a superset of the UDT
    ///    that defines the base class, plus a few extra things (like offset). So the
    ///    children of a BaseClass will be the exact same IDs as the children of the UDT
    ///    that the BaseClass represents, etc.
    /// </remarks>
    [DebuggerDisplay( "BaseClass: Id {TypeId}: {Name}" )]
    public abstract class DbgBaseClassTypeInfoBase : DbgUdtTypeInfo
    {
        public readonly bool IsVirtualBaseClass;
        public readonly bool IsIndirectVirtualBaseClass;

        /// <summary>
        ///    The TypeId of the "plain" UDT type represented by this base class.
        /// </summary>
        /// <remarks>
        ///    Base class type objects are very much like normal UDT types, but they have
        ///    some additional info (like an offset), as well as their own distinct type
        ///    ID.
        ///
        ///    But for the purposes of comparing types, sometimes you just want to pretend
        ///    that base classes are "the same as" normal UDTs (like you would in source).
        ///    This property lets you do that.
        ///
        ///    Example:
        ///
        ///    Suppose "foo!blah" is a UDT that also has a derived class "foo!blah2".
        ///
        ///    If you do "dt foo!blah", you'll get a DbgUdtTypeInfo object, with TypeId=X.
        ///
        ///    If you do "(dt foo!blah2).BaseClasses[0]", you'll get a
        ///    DbgBaseClassTypeInfo object, with TypeId=Y, and BaseClassTypeId=X.
        /// </remarks>
        public readonly uint BaseClassTypeId;


        public static DbgBaseClassTypeInfoBase GetBaseClassTypeInfo( DbgEngDebugger debugger,
                                                                     DbgModuleInfo module,
                                                                     uint typeId )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            if( null == module )
                throw new ArgumentNullException( "module" );

            RawBaseClassInfo rbci = DbgHelp.GetBaseClassInfo( debugger.DebuggerInterface, module.BaseAddress, typeId );
            if( rbci.IsVirtualBaseClass )
            {
                return new DbgVirtualBaseClassTypeInfo( debugger,
                                                        module,
                                                        typeId,
                                                        rbci.UdtKind,
                                                        rbci.BaseClassTypeName,
                                                        rbci.BaseClassSize,
                                                        rbci.ChildrenCount,
                                                        rbci.ClassParentId,
                                                        rbci.VirtualTableShapeId,
                                                        rbci.IsVirtualBaseClass,
                                                        rbci.IsIndirectVirtualBaseClass,
                                                        rbci.BaseClassTypeId,
                                                        rbci.VirtualBaseDispIndex,
                                                        rbci.VirtualBasePointerOffset );
            }
            else
            {
                return new DbgBaseClassTypeInfo( debugger,
                                                 module,
                                                 typeId,
                                                 rbci.UdtKind,
                                                 rbci.BaseClassTypeName,
                                                 rbci.BaseClassSize,
                                                 rbci.ChildrenCount,
                                                 rbci.ClassParentId,
                                                 rbci.VirtualTableShapeId,
                                                 rbci.IsVirtualBaseClass,
                                                 rbci.IsIndirectVirtualBaseClass,
                                                 rbci.BaseClassTypeId,
                                                 rbci.Offset );
            }
        } // end GetBaseClassTypeInfo()


        internal abstract void AggregateMembers( IList<DbgDataMemberTypeInfo> members,
                                                 IList<DbgDataStaticMemberTypeInfo> statics );

        protected DbgBaseClassTypeInfoBase( DbgEngDebugger debugger,
                                            ulong moduleBase,
                                            uint typeId,
                                            UdtKind kind,
                                            string name,
                                            ulong size,
                                            uint numChildren,
                                            uint classParentId,   // could be 0?
                                            uint vtableShapeId,   // could be 0?
                                            bool isVirtBaseClass,
                                            bool isIndirectVirtBaseClass,
                                            uint baseClassTypeId,
                                            DbgTarget target )
            : base( debugger,
                    moduleBase,
                    typeId,
                    kind,
                    name,
                    size,
                    numChildren,
                    classParentId,
                    vtableShapeId,
                    target )
        {
            IsVirtualBaseClass = isVirtBaseClass;
            IsIndirectVirtualBaseClass = isIndirectVirtBaseClass;
            BaseClassTypeId = baseClassTypeId;
        } // end constructor

        protected DbgBaseClassTypeInfoBase( DbgEngDebugger debugger,
                                            DbgModuleInfo module,
                                            uint typeId,
                                            UdtKind kind,
                                            string name,
                                            ulong size,
                                            uint numChildren,
                                            uint classParentId,   // could be 0?
                                            uint vtableShapeId,   // could be 0?
                                            bool isVirtBaseClass,
                                            bool isIndirectVirtBaseClass,
                                            uint baseClassTypeId )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    kind,
                    name,
                    size,
                    numChildren,
                    classParentId,
                    vtableShapeId,
                    isVirtBaseClass,
                    isIndirectVirtBaseClass,
                    baseClassTypeId,
                    module.Target )
        {
            __mod = module;
        } // end constructor
    } // end class DbgBaseClassTypeInfoBase
}

