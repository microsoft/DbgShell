using System;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    // Corresponding to SymTag.Null, DbgNullTypeInfo is sort of a special case,
    // representing no type at all ("void"). For instance, it could be used for the return
    // type of a function (DbgFunctionTypeTypeInfo) that doesn't return anything.
    [DebuggerDisplay( "NullType: Id {TypeId}" )]
    public class DbgNullTypeInfo : DbgNamedTypeInfo
    {
        public static DbgNullTypeInfo GetNullTypeInfo( DbgEngDebugger debugger,
                                                       DbgModuleInfo module,
                                                       uint typeId )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            // This does not need to be an invariant; I'm just curious if it's ever not 0.
            Util.Assert( 0 == typeId );

            return new DbgNullTypeInfo( debugger, module, typeId );
        } // end GetNullTypeInfo()
                                               

        protected DbgNullTypeInfo( DbgEngDebugger debugger,
                                   ulong moduleBase,
                                   uint typeId,
                                   DbgTarget target )
            : base( debugger, moduleBase, typeId, SymTag.Null, "void", 0, target )
        {
        } // end constructor

        protected DbgNullTypeInfo( DbgEngDebugger debugger,
                                   DbgModuleInfo module,
                                   uint typeId )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    module.Target )
        {
            __mod = module;
        } // end constructor
    } // end class DbgNullTypeInfo
}

