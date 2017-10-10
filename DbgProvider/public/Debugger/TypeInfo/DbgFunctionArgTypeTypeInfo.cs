using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [DebuggerDisplay( "FunctionArg: {Type.Name}" )]
    public class DbgFunctionArgTypeTypeInfo : DbgTypeInfo
    {
        private readonly uint m_argTypeId;

        private DbgNamedTypeInfo m_argType;
        public DbgNamedTypeInfo ArgType
        {
            get
            {
                if( null == m_argType )
                {
                    _EnsureValid();
                    m_argType = DbgTypeInfo.GetNamedTypeInfo( Debugger,
                                                              Module,
                                                              m_argTypeId );
                }
                return m_argType;
            }
        }

        // TODO: There is no static factory method. Should I provide one? We'd only need
        // it if we want to provide a way to get one of these by itself; not via a
        // FunctionType thing (which already gets the necessary information).


        public DbgFunctionArgTypeTypeInfo( DbgEngDebugger debugger,
                                           ulong moduleBase,
                                           uint typeId,
                                           uint argTypeId,
                                           DbgTarget target )
            : base( debugger, moduleBase, typeId, SymTag.FunctionArgType, target )
        {
            m_argTypeId = argTypeId;
        } // end constructor

        public DbgFunctionArgTypeTypeInfo( DbgEngDebugger debugger,
                                           DbgModuleInfo module,
                                           uint typeId,
                                           uint argTypeId )
            : this( debugger, GetModBase( module ), typeId, argTypeId, module.Target )
        {
            __mod = module;
        } // end constructor
    } // end class DbgFunctionArgTypeTypeInfo
}

