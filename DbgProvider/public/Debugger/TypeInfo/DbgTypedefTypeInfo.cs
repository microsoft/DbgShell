using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [DebuggerDisplay( "Typedef: {Name}: Id {TypeId} -> {m_representedTypeId}" )]
    public class DbgTypedefTypeInfo : DbgNamedTypeInfo
    {
        private readonly uint m_representedTypeId;
        private DbgNamedTypeInfo m_representedType;


        public DbgNamedTypeInfo RepresentedType
        {
            get
            {
                if( null == m_representedType )
                {
                    _EnsureValid();
                    m_representedType = DbgNamedTypeInfo.GetNamedTypeInfo( Debugger,
                                                                           Module,
                                                                           m_representedTypeId );
                }
                return m_representedType;
            }
        }


        public static DbgTypedefTypeInfo GetTypedefTypeInfo( DbgEngDebugger debugger,
                                                             DbgModuleInfo module,
                                                             uint typeId )
        {
            uint representedTypeId;
            string typedefName;
            DbgHelp.GetTypedefTypeInfo( debugger.DebuggerInterface,
                                        module.BaseAddress,
                                        typeId,
                                        out representedTypeId,
                                        out typedefName );
            return new DbgTypedefTypeInfo( debugger,
                                           module,
                                           typeId,
                                           representedTypeId,
                                           typedefName );
        } // end GetTypedefTypeInfo()


        public DbgTypedefTypeInfo( DbgEngDebugger debugger,
                                   ulong moduleBase,
                                   uint typeId,
                                   uint representedTypeId,
                                   string typedefName,
                                   DbgTarget target )
            : base( debugger, moduleBase, typeId, SymTag.Typedef, typedefName, target )
        {
            m_representedTypeId = representedTypeId;
        } // end constructor

        public DbgTypedefTypeInfo( DbgEngDebugger debugger,
                                   DbgModuleInfo module,
                                   uint typeId,
                                   uint representedTypeId,
                                   string typedefName )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    representedTypeId,
                    typedefName,
                    module.Target )
        {
            __mod = module;
        } // end constructor
    } // end class DbgTypedefTypeInfo
}

