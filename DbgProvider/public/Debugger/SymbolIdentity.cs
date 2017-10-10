using System;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    /// <summary>
    ///    Identifies a symbol. Note that it is NOT unique within a symbol group, since
    ///    multiple things could point to the same symbol.
    /// </summary>
    [DebuggerDisplay( "SymIdentity: {Name}: {\"0x\" + ModuleBase.ToString(\"x\") + \" +0x\" + Offset.ToString(\"x\")}" )]
    public class SymbolIdentity : IEquatable< SymbolIdentity >
    {
        private const uint DEBUG_ANY_ID = unchecked( 0xffffffff );

        public string Name { get; private set; }

        /// <summary>
        ///    The module that the symbol is in. Note that this can be 0 for
        ///    compiler-/debugger-generated types.
        /// </summary>
        public ulong ModuleBase { get; private set; }
        public ulong Offset { get; private set; }
        public DbgNamedTypeInfo Type { get; private set; }
        public DbgEngContext ProcessContext { get; private set; }

        internal SymbolIdentity( DbgSymbol sgi )
        {
            Name = sgi.Name;
            ModuleBase = null != sgi.Module ? sgi.Module.BaseAddress : 0;
            if( sgi.IsValueUnavailable )
                Offset = DebuggerObject.InvalidAddress;
            else
                Offset = sgi.Address;

            Type = sgi.Type;

            _SetContext( sgi.Target.Context );
        } // end constructor


        private void _SetContext( DbgEngContext context )
        {
            if( null == context )
                throw new ArgumentNullException( "context" );

            //ProcessContext = context.AsProcessContext();
            // TODO: for symbols for a user-mode module in a kernel mode target,
            // do I want to keep the process portion of the context?

            //ProcessContext = context.AsTargetContext();
            ProcessContext = context.WithoutThreadOrFrameIndex();
        } // _SetContext()


        public SymbolIdentity( string name,
                               ulong moduleBase,
                               ulong offset,
                               DbgNamedTypeInfo type,
                               DbgEngContext processContext )
        {
            Name = name;
            ModuleBase = moduleBase;
            Offset = offset;
            Type = type;
            _SetContext( processContext );
        } // end constructor

        public SymbolIdentity( string name,
                               DEBUG_SYMBOL_PARAMETERS nativeParams,
                               DEBUG_SYMBOL_ENTRY? dse,
                               DbgEngContext processContext )
        {
            Name = name;
            ModuleBase = nativeParams.Module;
            if( null == dse )
                Offset = DebuggerObject.InvalidAddress;
            else
                Offset = ((DEBUG_SYMBOL_ENTRY) dse).Offset;

            _SetContext( processContext );
        } // end constructor

        #region IEquatable< SymbolIdentity > Stuff

        public bool Equals( SymbolIdentity other )
        {
            if( null == other )
                return false;

            return (ModuleBase == other.ModuleBase) &&
                   (Offset == other.Offset) &&
                   (0 == Util.Strcmp_OI( Name, other.Name )) &&
                   (Type == other.Type) &&
                   (ProcessContext == other.ProcessContext);
        } // end Equals()

        public override bool Equals( object obj )
        {
            return Equals( obj as SymbolIdentity );
        }

        public override int GetHashCode()
        {
            return Name.ToLowerInvariant().GetHashCode() +
                   ModuleBase.GetHashCode() +
                   Offset.GetHashCode() +
                   Type.GetHashCode() +
                   ProcessContext.GetHashCode();
        }

        public static bool operator ==( SymbolIdentity s1, SymbolIdentity s2 )
        {
            if( null == (object) s1 )
                return (null == (object) s2);

            return s1.Equals( s2 );
        }

        public static bool operator !=( SymbolIdentity s1, SymbolIdentity s2 )
        {
            return !(s1 == s2);
        }
        #endregion
    } // end class SymbolIdentity
}

