using System;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [DebuggerDisplay( "Data: Id {TypeId}: {Name}" )]
    public class DbgDataTypeInfo : DbgNamedTypeInfo
    {
        public readonly DataKind DataKind;

        internal readonly uint m_memberTypeId;
        private DbgNamedTypeInfo m_dataType;
        public DbgNamedTypeInfo DataType
        {
            get
            {
                if( null == m_dataType )
                {
                    _EnsureValid();
                    m_dataType = DbgTypeInfo.GetNamedTypeInfo( Debugger, Module, m_memberTypeId );
                }
                return m_dataType;
            }
        }

        protected override ulong GetSize() { return DataType.Size; }


        public static DbgDataTypeInfo GetDataTypeInfo( DbgEngDebugger debugger,
                                                       DbgModuleInfo module,
                                                       uint typeId )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            RawDataInfo rdi = DbgHelp.GetDataInfo( debugger.DebuggerInterface, module.BaseAddress, typeId );

            switch( rdi.DataKind )
            {
                case DataKind.Member:
                    return new DbgDataMemberTypeInfo( debugger, module, typeId, rdi );
                case DataKind.StaticMember:
                    return new DbgDataStaticMemberTypeInfo( debugger, module, typeId, rdi );
                    // TODO: more specialized types
                default:
                    return new DbgDataTypeInfo( debugger, module, typeId, rdi );
            }
        } // end GetDataTypeInfo()
                                               

        protected DbgDataTypeInfo( DbgEngDebugger debugger,
                                   ulong moduleBase,
                                   uint typeId,
                                   RawDataInfo rdi,
                                   DbgTarget target )
            : base( debugger, moduleBase, typeId, SymTag.Data, rdi.SymName, target )
        {
            DataKind = rdi.DataKind;
            m_memberTypeId = rdi.TypeId;
        } // end constructor

        protected DbgDataTypeInfo( DbgEngDebugger debugger,
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

        protected DbgDataTypeInfo( DbgEngDebugger debugger,
                                   DbgModuleInfo module,
                                   uint typeId,
                                   string name,
                                   DataKind kind,
                                   uint memberTypeId )
            : base( debugger,
                    GetModBase( module ),
                    typeId,
                    SymTag.Data,
                    name,
                    module.Target )
        {
            DataKind = kind;
            m_memberTypeId = memberTypeId;
            __mod = module;
        } // end constructor
    } // end class DbgDataTypeInfo
}

