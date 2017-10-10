using System.Collections.Generic;
using System.Diagnostics;

namespace MS.Dbg
{
    // BaseClass objects are a little confusing. They are just a superset of the UDT that
    // defines the base class, plus a few extra things (like offset). So the children of a
    // BaseClass will be the exact same IDs as the children of the UDT that the BaseClass
    // represents, etc.

    [DebuggerDisplay( "BaseClass: Id {TypeId}: {Name}" )]
    public class DbgBaseClassTypeInfo : DbgBaseClassTypeInfoBase
    {
        public readonly uint Offset;

        /// <summary>
        ///    Adds members (including statics) from the current type to the specified
        ///    lists (as DbgDataInheritedMemberTypeInfo objects (for members)).
        /// </summary>
        internal override void AggregateMembers( IList<DbgDataMemberTypeInfo> members,
                                                 IList<DbgDataStaticMemberTypeInfo> statics )
        {
            foreach( var member in Members )
            {
                members.Add( new DbgDataInheritedMemberTypeInfo( member, Offset ) );
            }

            foreach( var staticMember in StaticMembers )
            {
                statics.Add( staticMember );
            }
        } // end AggregateMembers()


        public DbgBaseClassTypeInfo( DbgEngDebugger debugger,
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
                                     uint offset,
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
                    isVirtBaseClass,
                    isIndirectVirtBaseClass,
                    baseClassTypeId,
                    target )
        {
            Offset = offset;
        } // end constructor

        public DbgBaseClassTypeInfo( DbgEngDebugger debugger,
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
                                     uint baseClassTypeId,
                                     uint offset )
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
                    offset,
                    module.Target )
        {
            __mod = module;
        } // end constructor
    } // end class DbgBaseClassTypeInfo
}

