using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MS.Dbg
{
    // BaseClass objects are a little confusing. They are just a superset of the UDT that
    // defines the base class, plus a few extra things (like offset). So the children of a
    // BaseClass will be the exact same IDs as the children of the UDT that the BaseClass
    // represents, etc.

    [DebuggerDisplay( "VirtualBaseClass: Id {TypeId}: {Name}" )]
    public class DbgVirtualBaseClassTypeInfo : DbgBaseClassTypeInfoBase
    {
        public readonly uint VirtualBaseDispIndex;
        public readonly uint VirtualBasePointerOffset;

        internal override void AggregateMembers( IList<DbgDataMemberTypeInfo> members,
                                                 IList<DbgDataStaticMemberTypeInfo> statics )
        {
            throw new NotImplementedException();
         // foreach( var member in Members )
         // {
         //     members.Add( new DbgDataInheritedMemberTypeInfo( member, Offset ) );
         // }

         // foreach( var staticMember in StaticMembers )
         // {
         //     statics.Add( staticMember );
         // }
        } // end AggregateMembers()


        public DbgVirtualBaseClassTypeInfo( DbgEngDebugger debugger,
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
                                            uint virtualBaseDispIndex,
                                            uint virtualBasePointerOffset,
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
            VirtualBaseDispIndex = virtualBaseDispIndex;
            VirtualBasePointerOffset = virtualBasePointerOffset;
        } // end constructor

        public DbgVirtualBaseClassTypeInfo( DbgEngDebugger debugger,
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
                                            uint virtualBaseDispIndex,
                                            uint virtualBasePointerOffset )
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
                    virtualBaseDispIndex,
                    virtualBasePointerOffset,
                    module.Target )
        {
            __mod = module;
        } // end constructor
    } // end class DbgVirtualBaseClassTypeInfo
}

