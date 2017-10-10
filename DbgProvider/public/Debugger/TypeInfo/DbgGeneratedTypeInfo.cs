using System;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    /// <summary>
    ///    Sometimes the debugger has to make up some type information. This class
    ///    represents such a type. Unfortunately it's pretty bare at the moment because
    ///    there is no way to get information about the type that dbgeng made up.
    /// </summary>
    [DebuggerDisplay( "GeneratedType: Id {TypeId}" )]
    public class DbgGeneratedTypeInfo : DbgNamedTypeInfo
    {
        public override string ToString()
        {
            return Util.Sprintf( "Debugger-generated type: 0x{0:x}", TypeId );
        }

        protected override string GetName()
        {
            return Util.Sprintf( "Debugger-generated type (id 0x{0:x}): Please ask the debugger team to provide a way to get this information.",
                                 TypeId );
        }

        protected override ColorString GetColorName()
        {
            return GetName();
        }

        protected override ulong GetSize()
        {
            // You'd think we could just call GetTypeSize. But apparently it won't work
            // for dbgeng-generated types. :/
         // uint size;
         // CheckHr( ((WDebugSymbols) Debugger.DebuggerInterface).GetTypeSize( Module.BaseAddress, TypeId, out size ) );
         // return size;

            // Another possibility is that we could allow the caller to set some of the
            // information: if the caller has a DEBUG_SYMBOL_ENTRY, that contains some
            // rudimentary type information (size and tag).
            return 0xdeadbeef;
        }


        public DbgGeneratedTypeInfo( DbgEngDebugger debugger,
                                     ulong moduleBase,
                                     uint typeId,
                                     DbgTarget target )
            : this( debugger, moduleBase, typeId, SymTag.Null, target )
        {
        }

        public DbgGeneratedTypeInfo( DbgEngDebugger debugger,
                                     ulong moduleBase,
                                     uint typeId,
                                     SymTag symTag,
                                     DbgTarget target )
            : base( debugger, moduleBase, typeId, symTag, target )
        {
            if( !IsDbgGeneratedType( typeId ) )
            {
                throw new ArgumentException( "The typeId does not look like a debugger-generated typeId to me.",
                                             "typeId" );
            }

            //
            // TODO: Dbgeng internally stores info about generated types in a
            // DBG_GENERATED_TYPE structure. Unfortunately, there is no way to get at that
            // information, so until they provide an API, this class is just a dummy
            // placeholder.
            //
        } // end constructor


        public DbgGeneratedTypeInfo( DbgEngDebugger debugger,
                                     DbgModuleInfo module,
                                     uint typeId )
            : this( debugger, module, typeId, SymTag.Null )
        {
        }

        public DbgGeneratedTypeInfo( DbgEngDebugger debugger,
                                     DbgModuleInfo module,
                                     uint typeId,
                                     SymTag symTag )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    symTag,
                    module.Target )
        {
            __mod = module;
        } // end constructor
    } // end class DbgGeneratedTypeInfo
}

