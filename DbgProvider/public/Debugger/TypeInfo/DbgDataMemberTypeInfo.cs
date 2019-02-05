using System.Diagnostics;

namespace MS.Dbg
{
    [DebuggerDisplay( "Data Member: Id {TypeId}: {Name}" )]
    public class DbgDataMemberTypeInfo : DbgDataMemberTypeInfoBase
    {
        public readonly uint Offset;
        public readonly uint BitfieldLength;    // 0 if not a bitfield
        public readonly uint BitfieldPosition;

        public bool IsBitfield { get { return 0 != BitfieldLength; } }

        public override bool IsStatic
        {
            get { return false; }
        }

        public DbgDataMemberTypeInfo( DbgEngDebugger debugger,
                                      ulong moduleBase,
                                      uint typeId,
                                      RawDataInfo rdi,
                                      DbgTarget target )
            : base( debugger, moduleBase, typeId, rdi, target )
        {
            Offset = rdi.Offset;
            BitfieldLength = rdi.BitfieldLength;
            BitfieldPosition = rdi.BitPosition;

            // Don't know if this is possible, so I'll throw in an assert to help me find
            // out:
            Util.Assert( null == rdi.Value,
                         "Oh hey, a non-static data member with a constant value!" );
        } // end constructor

        public DbgDataMemberTypeInfo( DbgEngDebugger debugger,
                                      DbgModuleInfo module,
                                      uint typeId,
                                      RawDataInfo rdi )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    rdi,
                    module.Target )
        {
            __mod = module;
        } // end constructor

        protected DbgDataMemberTypeInfo( DbgEngDebugger debugger,
                                         DbgModuleInfo module,
                                         uint typeId,
                                         string name,
                                         DataKind kind,
                                         uint memberTypeId,
                                         uint owningTypeId,
                                         uint offset,
                                         uint bitfieldLength,
                                         uint bitfieldPosition )
            : base( debugger,
                    module,
                    typeId,
                    name,
                    kind,
                    memberTypeId,
                    owningTypeId )
        {
            Offset = offset;
            BitfieldLength = bitfieldLength;
            BitfieldPosition = bitfieldPosition;
        } // end constructor
    } // end class DbgDataMemberTypeInfo
}

