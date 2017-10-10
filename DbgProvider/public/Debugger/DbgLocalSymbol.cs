using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [DebuggerDisplay( "LocalSym: {PathString}" )]
    public class DbgLocalSymbol : DbgSymbol
    {
        protected DEBUG_SYMBOL_PARAMETERS? m_nativeParams;
        protected DEBUG_SYMBOL_ENTRY? m_dse;
        protected  DbgSymbolGroup m_symGroup; // the group that this symbol is part of
#if DEBUG
        public DbgSymbolGroup SymGroup { get { return m_symGroup; } }
#endif
        protected uint m_idx; // the index of this symbol in the symbol group

        internal DEBUG_SYMBOL_ENTRY _DSE { get { return (DEBUG_SYMBOL_ENTRY) DSE; } }



        internal DEBUG_SYMBOL_PARAMETERS _NSP
        {
            get { return (DEBUG_SYMBOL_PARAMETERS) NativeSymParams; }
        }

        public DEBUG_SYMBOL_ENTRY? DSE { get { return m_dse; } }

        public DEBUG_SYMBOL_PARAMETERS? NativeSymParams { get { return m_nativeParams; } }


        protected DbgNamedTypeInfo m_type;
        public override DbgNamedTypeInfo Type
        {
            get
            {
                if( null == m_type )
                {
                    // Accessing the property will cause us to try and fetch it, so if
                    // it's still null, we couldn't get it.
                    if( null == NativeSymParams )
                        return null;

                    Debugger.ExecuteOnDbgEngThread( () =>
                        {
                            m_type = (DbgNamedTypeInfo) DbgTypeInfo.GetTypeInfo( Debugger,
                                                                                 _NSP.Module,
                                                                                 _NSP.TypeId,
                                                                                 Target );
                        } );
                }
                return m_type;
            }
        } // end property Type

        // This will throw if the value is unavailable...
        public override ulong Address { get { return _DSE.Offset; } }


        public override bool IsValueInRegister { get { return 0UL == Address; } }


        public DEBUG_SYMBOL Flags
        {
            get
            {
                return (_NSP.Flags & ~DEBUG_SYMBOL.EXPANSION_LEVEL_MASK);
            }
        }


        // Alternative way to get this info: from _DSE:

    //  public override bool IsPointer
    //  {
    //      get
    //      {
    //          return (_DSE.Tag == SymTag.PointerType) ||
    //                 (_DSE.Tag == SymTag.FunctionType);
    //      }
    //  }

    //  public override bool IsArray
    //  {
    //      get
    //      {
    //          return _DSE.Tag == SymTag.ArrayType;
    //      }
    //  }

    //  public override bool IsEnum
    //  {
    //      get
    //      {
    //          return _DSE.Tag == SymTag.Enum;
    //      }
    //  }

    //  public override bool IsUdt
    //  {
    //      get
    //      {
    //          return _DSE.Tag == SymTag.UDT;
    //      }
    //  }

        public uint Index { get { return m_idx; } }

        private DbgRegisterInfo m_reg;

        /// <summary>
        ///    The register that contains the symbol's value, or null if the value is not
        ///    in a register.
        /// </summary>
        public override DbgRegisterInfoBase Register
        {
            get
            {
                if( 0UL != _DSE.Offset )
                    return null;

                if( null == m_reg )
                {
                    Debugger.ExecuteOnDbgEngThread( () =>
                        {
                            uint regNum;
                            // TODO: awkward; refactor
                            CheckHr( m_symGroup.m_symGroup.GetSymbolRegister( this.Index, out regNum ) );
                            // TODO TODO: Shoot... aren't we going to need to ensure we're in the proper context?
                            m_reg = DbgRegisterInfo.GetDbgRegisterInfoForIndex( Debugger, regNum );
                        } );
                }
                return m_reg;
            }
        } // end property Register


        private uint m_pointerAdjustment;

        public override uint PointerAdjustment { get { return m_pointerAdjustment; } }


        private DbgModuleInfo m_mod;
        public override DbgModuleInfo Module
        {
            get
            {
                if( (null == m_mod) &&
                    (null != NativeSymParams) &&
                    (0 != _NSP.Module) )
                {
                    m_mod = new DbgModuleInfo( Debugger, _NSP.Module, Target );
                }
                return m_mod;
            }
        } // end property Module


        private static DbgEngDebugger _GetDebugger( DbgSymbolGroup symGroup )
        {
            if( null == symGroup )
                throw new ArgumentNullException( "symGroup" );

            return symGroup.Debugger;
        } // end _GetDebugger()


        private List< DbgSymbol > m_orphanChildren = new List< DbgSymbol >();

        internal void Refresh( uint idx,
                               DEBUG_SYMBOL_PARAMETERS nativeParams,
                               DEBUG_SYMBOL_ENTRY? dse )
        {
            if( null != m_orphanChildren )
                m_orphanChildren.Clear(); // in preparation for WireUpChildren being called again.

            m_idx = idx;
            m_nativeParams = nativeParams;
            m_dse = dse;
        } // end Refresh()


        internal static void WireUpChildren( DbgLocalSymbol[] syms )
        {
            for( uint i = 0; i < syms.Length; i++ )
            {
                if( null == syms[ i ].m_orphanChildren )
                    syms[ i ].m_orphanChildren = new List< DbgSymbol >();

                if( UInt32.MaxValue == syms[ i ]._NSP.ParentSymbol )
                    continue;

                var parent = syms[ syms[ i ]._NSP.ParentSymbol ];
                syms[ i ].Parent = parent;

                if( null == parent.m_orphanChildren )
                    parent.m_orphanChildren = new List< DbgSymbol >();

                // As an aid to debugging the debugger attempts to automatically cast
                // interface pointers down to the appropriate implementation class. It
                // does this by looking up vtable symbols and using the class name in the
                // symbol as the cast, then creates artificial cast members, which are
                // prefixed with "__vtcast_".
                //
                // In some cases (involving multiple vtables), we can end up with multiple
                // identical __vtcast_X members, so we ignore those in the assert.

                Util.Assert( !parent.m_orphanChildren.Contains( syms[ i ] ) ||
                             syms[ i ].Name.StartsWith( "__vtcast_", StringComparison.OrdinalIgnoreCase ) );

                parent.m_orphanChildren.Add( syms[ i ] );
            }
        } // end WireUpChildren()

        /// <summary>
        ///    Creates a new DbgLocalSymbol object.
        /// </summary>
        internal DbgLocalSymbol( DbgSymbolGroup symGroup,
                                 uint idx,
                                 string name,
                                 DEBUG_SYMBOL_PARAMETERS symParams,
                                 DEBUG_SYMBOL_ENTRY? dse,
                                 DbgTarget target )
            : base( _GetDebugger( symGroup ), name, target )
        {
            if( null == symGroup )
                throw new ArgumentNullException( "symGroup" );

            m_symGroup = symGroup;
            m_idx = idx;
            m_nativeParams = symParams;
            m_dse = dse;
            m_metaDataUnavailable = (null == dse);

            if( !m_metaDataUnavailable && DbgTypeInfo.IsDbgGeneratedType( m_dse.Value.TypeId ) )
            {
                // HACK HACKALERT WORKAROUND
                //
                // This is a workaround for the fact that we can't get at the type
                // information that the debugger generates. It tends to do this to create
                // pointer types, pointing to UDTs (not sure why the pointer types don't
                // exist in the PDB).
                //
                // It of course knows what the pointee type is, but it isn't exposed in
                // any way. I'd like to add a new IDebugAdvancedN::Request request to grab
                // it out, but until I get to that, I'll cheat and parse it out of some
                // string output.
                //
                // I don't know how robust this is, because I've rarely seen it (only once
                // with this code).
                //
                _GenerateSyntheticTypeToMatchDbgEngGeneratedType( name, Debugger, m_dse.Value );
            } // end if( it's a debugger-generated type )

            m_pointerAdjustment = 0;
            if( 0 == Util.Strcmp_OI( name, "this" ) )
            {
                try
                {
                    ulong displacementDontCare;
                    var sym = Debugger.GetSymbolByAddress( symGroup.Frame.InstructionPointer, out displacementDontCare );
                    var ftti = sym.Type as DbgFunctionTypeTypeInfo;
                    Util.Assert( null != ftti ); // if we made it this far, we should have the right type
                    m_pointerAdjustment = ftti.ThisAdjust;
                }
                catch( DbgEngException dee )
                {
                    LogManager.Trace( "Could not get thisadjust: {0}", Util.GetExceptionMessages( dee ) );
                }
            }
        } // end constructor


        private static Regex sm_vartypeRegex = new Regex( @"struct (?<typename>.+) \* (?<varname>\w+) = (?<varval>.*)",
                                                          System.Text.RegularExpressions.RegexOptions.Compiled );

        private static void _GenerateSyntheticTypeToMatchDbgEngGeneratedType( string localSymName,
                                                                              DbgEngDebugger debugger,
                                                                              DEBUG_SYMBOL_ENTRY dse )
        {
            if( dse.Tag != SymTag.PointerType )
            {
                LogManager.Trace( "It's a dbgeng-generated type that isn't a pointer! It's a {0} (id 0x{1:x}).",
                                  dse.Tag,
                                  dse.TypeId );
                return;
            }

            if( DbgHelp.PeekSyntheticTypeExists( debugger.DebuggerInterface,
                                                 dse.ModuleBase,
                                                 dse.TypeId ) )
            {
                return;
            }

            // We'll run a command like this:
            //
            //    dv /t "varname"
            //
            // And here's some sample output:
            //
            //    struct Microsoft::CoreUI::Support::BufferInfo * message = 0x0000006f`81edd570
            //

            string output = String.Empty;
            using( debugger.HandleDbgEngOutput(
                (x) =>
                {
                    x = x.Trim();
                    if( !String.IsNullOrEmpty( x ) )
                    {
                        Util.Assert( String.IsNullOrEmpty( output ) );
                        output = x;
                    }
                } ) )
            {
                debugger.InvokeDbgEngCommand( Util.Sprintf( "dv /t \"{0}\"", localSymName ),
                                              DEBUG_OUTCTL.THIS_CLIENT );
            }

            int matchCount = 0;
            foreach( Match match in sm_vartypeRegex.Matches( output ) )
            {
                matchCount++;
                string typename = match.Groups[ "typename" ].Value;
             // Console.WriteLine( "output: {0}", output );
             // Console.WriteLine( "typename: {0}", typename );
             // Console.WriteLine( "varname: {0}", match.Groups[ "varname" ].Value );
                LogManager.Trace( "DbgEng-generated type is a pointer, pointing to type: {0}", typename );

                var mod = debugger.GetModuleByAddress( dse.ModuleBase );

                DbgTypeInfo ti = debugger.GetTypeInfoByName( mod.Name + "!" + typename ).FirstOrDefault();
                if( null != ti )
                {
                    Util.Assert( ti is DbgNamedTypeInfo );
                    var nti = ti as DbgNamedTypeInfo;
                    Util.Assert( nti.Module.BaseAddress == dse.ModuleBase );
                    // We don't need to TryGetSynthPointerTypeIdByPointeeTypeId, because
                    // we already know it's not there (because we did
                    // PeekSyntheticTypeExists earlier).
                    DbgHelp.AddSyntheticPointerTypeInfo( debugger.DebuggerInterface,
                                                         nti.Module.BaseAddress,
                                                         nti.TypeId,
                                                         //debugger.TargetIs32Bit ? 4UL : 8UL,
                                                         dse.Size,
                                                         dse.TypeId );
                } // end if( we found the pointee type )
                else
                {
                    LogManager.Trace( "(but we couldn't find the type!)" );
                }
            } // end foreach( match )

            Util.Assert( matchCount <= 1 );

            if( 0 == matchCount )
            {
                LogManager.Trace( "Failed to parse out type name from: {0}", output );
            }
        } // end _GenerateSyntheticTypeToMatchDbgEngGeneratedType()
    } // end class DbgLocalSymbol
}

