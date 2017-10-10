using System.Diagnostics;

namespace MS.Dbg
{
    [DebuggerDisplay( "Data Static Member: Id {TypeId}: {Name}" )]
    public class DbgDataStaticMemberTypeInfo : DbgDataMemberTypeInfoBase
    {
        public readonly ulong Address;

        public readonly uint AddressOffset;


        public override bool IsStatic
        {
            get { return true; }
        }

        public DbgDataStaticMemberTypeInfo( DbgEngDebugger debugger,
                                            ulong moduleBase,
                                            uint typeId,
                                            RawDataInfo rdi,
                                            DbgTarget target )
            : base( debugger, moduleBase, typeId, rdi, target )
        {
            Address = rdi.Address;
            AddressOffset = rdi.AddressOffset;
        } // end constructor

        public DbgDataStaticMemberTypeInfo( DbgEngDebugger debugger,
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
    } // end class DbgDataStaticMemberTypeInfo
}

