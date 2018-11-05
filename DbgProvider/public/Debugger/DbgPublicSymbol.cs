using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;
using DbgEngWrapper;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents a public symbol (could be global, OR local).
    /// </summary>
    /// <remarks>
    ///    Normally we use a DbgLocalSymbol to represent a local symbol, but the dbgeng
    ///    routines that enumerate global symbols can also enumerate locals. So whether or
    ///    not you have a DbgLocalSymbol or a DbgPublicSymbol is more a question of how
    ///    you generated it, not whether it is a local symbol or not.
    /// </remarks>
    [DebuggerDisplay( "PublicSym: {Name}" )]
    public class DbgPublicSymbol : DbgSymbol
    {
        private WDebugSymbols m_debugSymbols;
        private SymbolInfo m_symInfo;
        private DbgModuleInfo m_mod;
        private DbgNamedTypeInfo m_type;

        private DEBUG_MODULE_AND_ID m_dmai;

        private DEBUG_SYMBOL_ENTRY? m_dse;
        public unsafe DEBUG_SYMBOL_ENTRY? DSE
        {
            get
            {
                if( null == m_dse )
                {
                    m_dse = Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        var dmai = m_dmai;
                        DEBUG_SYMBOL_ENTRY dse;
                        CheckHr( m_debugSymbols.GetSymbolEntryInformation( &dmai, out dse ) );
                        //m_dmai = dmai;
                        return dse;
                    } );
                }
                return m_dse;
            }
        } // end property DSE


        public override ulong Address {
            //[DebuggerStepThrough]
            get
            {
                if( 0 != (int) (m_symInfo.Flags & SymbolInfoFlags.RegRel) )
                {
                    return m_symInfo.Address + _RegisterInternal.ValueAsPointer;
                }
                else if( IsThreadLocal )
                {
                    ulong tlsBase = Module.GetImplicitThreadLocalStorageForThread( Debugger.GetCurrentThread() );

                    if( 0 == tlsBase )
                        return 0;
                    else
                        return tlsBase + m_symInfo.Address;
                }

                return m_symInfo.Address;
            }
        }

        public override bool IsValueInRegister
        {
            get
            {
                Util.Assert( IsLocalOrParam || (0 == m_symInfo.Register) );
                return null != Register;
            }
        }


        private DbgRegisterInfo m_reg;

        // This is set for both Register and RegRel values. We don't want to expose a
        // register for RegRel values, though, so this property is internal.
        private DbgRegisterInfoBase _RegisterInternal
        {
            get
            {
                if( 0 == m_symInfo.Register )
                    return null;

                Util.Assert( 0 != (int) (m_symInfo.Flags & (SymbolInfoFlags.Register | SymbolInfoFlags.RegRel)) );

                if( null == m_reg )
                {
                    Debugger.ExecuteOnDbgEngThread( () =>
                        {
                            // TODO TODO: Shoot... aren't we going to need to ensure we're in the proper context?
                            m_reg = DbgRegisterInfo.GetDbgRegisterInfoFor_CV_HREG_e( Debugger, m_symInfo.Register );
                        } );
                }
                return m_reg;
            }
        }

        public override DbgRegisterInfoBase Register
        {
            get
            {
                if( (0 == m_symInfo.Register) ||
                    (0 != (int) (m_symInfo.Flags & SymbolInfoFlags.RegRel)) )
                {
                    // We'll return null for register-relative things because the value is
                    // not in a register.
                    return null;
                }

                return _RegisterInternal;
            }
        } // end Register property

        public override DbgNamedTypeInfo Type { get { return m_type; } }

        public override DbgModuleInfo Module
        {
            get
            {
                if( null == m_mod )
                {
                    m_mod = new DbgModuleInfo( Debugger, SymbolInfo.ModBase, Target );
                }
                return m_mod;
            }
        } // end property Module


        public bool IsLocalOrParam
        {
            get
            {
                return 0 != (int) (m_symInfo.Flags & (SymbolInfoFlags.Local | SymbolInfoFlags.Parameter));
            }
        }


        public override bool IsConstant
        {
            get
            {
                return 0 != (m_symInfo.Flags & SymbolInfoFlags.Constant);
            }
        }


        internal override object GetConstantValue()
        {
            if( !IsConstant )
                return base.GetConstantValue(); // will throw

            object rawVal = DbgHelp.SymGetTypeInfo( Debugger.DebuggerInterface,
                                                    Module.BaseAddress,
                                                    m_symInfo.Index,
                                                    IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VALUE );

            // The raw value might be stored more efficiently than the actual type--for
            // instance, a DWORD constant might be stored (and returned from
            // SymGetTypeInfo) as a USHORT. So we might need to extend the value to get
            // fidelity with the true type of the constant.

            Debug.Assert( (Type is DbgBaseTypeInfo) || (Type is DbgEnumTypeInfo) );

            DbgBaseTypeInfo bti;
            bti = Type as DbgBaseTypeInfo;
            if( null == bti )
            {
                DbgEnumTypeInfo eti = Type as DbgEnumTypeInfo;
                if( null != eti )
                {
                    bti = eti.BaseType;
                }
                else
                {
                    throw new DbgProviderException(
                        Util.Sprintf( "I don't know how to handle constants with Type: {0}",
                                      Util.GetGenericTypeName( Type ) ),
                        "DontKnowHowToHandleConstantType",
                        System.Management.Automation.ErrorCategory.NotImplemented );
                }
            }
            return DbgHelp.ReconstituteConstant( rawVal, bti );
        }


        public override bool IsInlineCallsite
        {
            get { return m_symInfo.Tag == SymTag.InlineSite; }
        }


        public override bool IsThreadLocal
        {
            get { return 0 != (m_symInfo.Flags & SymbolInfoFlags.TlsRel); }
        }


        private static string _VerifySymInfo( SymbolInfo symbolInfo )
        {
            if( null == symbolInfo )
                throw new ArgumentNullException( "symbolInfo" );

            return symbolInfo.Name;
        }


        public SymbolInfo SymbolInfo { get { return m_symInfo; } }


        internal DbgPublicSymbol( DbgEngDebugger debugger,
                                  SymbolInfo symbolInfo,
                                  DbgTarget target )
            : base( debugger, _VerifySymInfo( symbolInfo ), target )
        {
            m_debugSymbols = (WDebugSymbols) debugger.DebuggerInterface;
            m_symInfo = symbolInfo;
            var typeSymTag = DbgHelp.GetSymTag( debugger.DebuggerInterface,
                                                symbolInfo.ModBase,
                                                symbolInfo.TypeIndex );

            m_type = (DbgNamedTypeInfo) DbgTypeInfo.GetTypeInfo( debugger,
                                                                 symbolInfo.ModBase,
                                                                 symbolInfo.TypeIndex,
                                                                 typeSymTag,
                                                                 target );

            m_dmai = new DEBUG_MODULE_AND_ID( symbolInfo.ModBase, symbolInfo.Index );
        } // end constructor
    } // end class DbgPublicSymbol
}

