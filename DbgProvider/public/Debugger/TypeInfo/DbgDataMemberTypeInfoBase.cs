using System.Diagnostics;

namespace MS.Dbg
{
    [DebuggerDisplay( "Data Member: Id {TypeId}: {Name}" )]
    public abstract class DbgDataMemberTypeInfoBase : DbgDataTypeInfo
    {
        internal readonly uint m_owningTypeId;
        // TODO: Can we scope this to a UDT?
        private DbgTypeInfo m_owningType;
        public DbgTypeInfo OwningType
        {
            get
            {
                if( (null == m_owningType) && (0 != m_owningTypeId) )
                {
                    _EnsureValid();
                    m_owningType = DbgTypeInfo.GetTypeInfo( Debugger, Module, m_owningTypeId );
                }
                return m_owningType;
            }
        }


        public abstract bool IsStatic { get; }

        protected DbgDataMemberTypeInfoBase( DbgEngDebugger debugger,
                                             ulong moduleBase,
                                             uint typeId,
                                             RawDataInfo rdi,
                                             DbgTarget target )
            : base( debugger, moduleBase, typeId, rdi, target )
        {
            m_owningTypeId = rdi.ClassParentId;
        } // end constructor

        protected DbgDataMemberTypeInfoBase( DbgEngDebugger debugger,
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

        protected DbgDataMemberTypeInfoBase( DbgEngDebugger debugger,
                                             DbgModuleInfo module,
                                             uint typeId,
                                             string name,
                                             DataKind kind,
                                             uint memberTypeId,
                                             uint owningTypeId )
            : base( debugger, module, typeId, name, kind, memberTypeId )
        {
            m_owningTypeId = owningTypeId;
        } // end constructor
    } // end class DbgDataMemberTypeInfoBase
}

