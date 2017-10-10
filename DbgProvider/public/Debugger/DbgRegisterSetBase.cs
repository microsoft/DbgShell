using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interop;
using DbgEngWrapper;

namespace MS.Dbg
{
    // TODO: This class currently assumes that the register values are static. Need to handle
    // changing register values.

    /// <summary>
    ///    Represents a set of registers.
    /// </summary>
    public abstract class DbgRegisterSetBase : DebuggerObject, ISupportColor
    {
        public DbgStackFrameInfo StackFrame { get; private set; }

        private WDebugSymbols m_debugSymbols;
        private WDebugRegisters m_debugRegisters;
        private WDebugControl m_debugControl;

        // This is used when creating the colorized string: changed registers (as
        // compared to Baseline) will be red; unchanged registers will be cyan.
        protected DbgRegisterSetBase Baseline { get; private set; }

        private List< DbgRegisterInfo > m_registers;
        private Dictionary< string, DbgRegisterInfo > m_registerIndex;


        protected DbgRegisterSetBase( DbgEngDebugger debugger,
                                      DbgStackFrameInfo stackFrame,
                                      DbgRegisterSetBase baseline )
            : base( debugger )
        {
            if( null == stackFrame )
                throw new ArgumentNullException( "stackFrame" );

            StackFrame = stackFrame;

            Debugger.ExecuteOnDbgEngThread( () =>
                {
                    m_debugSymbols = (WDebugSymbols) Debugger.DebuggerInterface;
                    m_debugRegisters = (WDebugRegisters) Debugger.DebuggerInterface;
                    m_debugControl = (WDebugControl) Debugger.DebuggerInterface;
                } );

            Baseline = baseline;
            // We used to lazily retrieve values, but that's no good when the context
            // changes (such as when stepping through code).
            _GetRegisterInfo();
        } // end constructor


        private void _GetRegisterInfo()
        {
            Debugger.ExecuteOnDbgEngThread( () =>
                {
                    m_registers = new List<DbgRegisterInfo>( _EnumerateRegisters() );
                    m_registerIndex = new Dictionary<string, DbgRegisterInfo>( StringComparer.OrdinalIgnoreCase );
                    foreach( DbgRegisterInfo ri in m_registers )
                    {
                        m_registerIndex.Add( ri.Name, ri );
                    }
                } );
        } // end _GetRegisterInfo()


        public IEnumerable<DbgRegisterInfo> EnumerateRegisters()
        {
            if( null == m_registers )
            {
                _GetRegisterInfo();
            }
            return m_registers;
        } // end EnumerateRegisters()


        public DbgRegisterInfo this[ string registerName ]
        {
            get
            {
                if( String.IsNullOrEmpty( registerName ) )
                    throw new ArgumentException( "You must supply a register name.", "registerName" );

                if( '$' == registerName[ 0 ] )
                    throw new InvalidOperationException( "Please access pseudo-registers via the Pseudo property." );
                else
                    return _GetRegister( registerName );
            }
        } // end indexer


        private DbgRegisterInfo _GetRegister( string registerName )
        {
            if( null == m_registers )
            {
                _GetRegisterInfo();
            }
            DbgRegisterInfo reg;
            if( !m_registerIndex.TryGetValue( registerName, out reg ) )
            {
                throw new DbgProviderException( Util.Sprintf( "Register '{0}' not available.",
                                                              registerName ),
                                                "NoSuchRegister",
                                                System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                registerName );
            }
            return reg;
        }



        private IEnumerable< DbgRegisterInfo > _EnumerateRegisters()
        {
            using( new DbgEngContextSaver( Debugger, StackFrame.Context ) )
            {
                uint numRegs;
                CheckHr( m_debugRegisters.GetNumberRegisters( out numRegs ) );

                DEBUG_VALUE[] regVals;

                CheckHr( m_debugRegisters.GetValues2( DEBUG_REGSRC.FRAME, numRegs, 0, out regVals ) );

                // INT460e158e: dbgeng gives us "xmm7/3" again in place of "xmm8/3".
                bool sawXmm73Already = false;
                for( uint i = 0; i < regVals.Length; i++ )
                {
                    string name;
                    DEBUG_REGISTER_DESCRIPTION desc;
                    CheckHr( m_debugRegisters.GetDescriptionWide( i, out name, out desc ) );
                    if( 0 == Util.Strcmp_OI( "xmm7/3", name ) )
                    {
                        if( !sawXmm73Already )
                        {
                            sawXmm73Already = true;
                        }
                        else
                        {
                            // INT460e158e: dbgeng meant to say "xmm8/3".
                            name = "xmm8/3";
                        }
                    }
                    yield return new DbgRegisterInfo( Debugger, name, regVals[ i ], desc, i );
                }
            } // end using( context saver )
        } // end _EnumerateRegisters()


        private IEnumerable< DbgPseudoRegisterInfo > _EnumeratePseudoRegisters()
        {
            using( new DbgEngContextSaver( Debugger, StackFrame.Context ) )
            {
                uint numRegs;
                CheckHr( m_debugRegisters.GetNumberPseudoRegisters( out numRegs ) );

                DEBUG_VALUE[] regVals;

                int hr = m_debugRegisters.GetPseudoValues( DEBUG_REGSRC.FRAME, numRegs, 0, out regVals );
                // Unfortunately, hr here does not mean much: if dbgeng couldn't get the
                // value for a single register out of 100, it will return that error code.
                // So we just ignore it.

                for( uint i = 0; i < regVals.Length; i++ )
                {
                 // if( regVals[ i ].Type == DEBUG_VALUE_TYPE.INVALID )
                 //     continue;

                    string name;
                    ulong typeMod;
                    uint typeId;
                    hr = m_debugRegisters.GetPseudoDescriptionWide( i, out name, out typeMod, out typeId );
                    if( 0 == hr )
                    {
                        Util.Assert( regVals[ i ].Type != DEBUG_VALUE_TYPE.INVALID );
                        yield return new DbgPseudoRegisterInfo( Debugger, name, regVals[ i ], typeMod, typeId, i );
                    }
                    else
                    {
                        if( i != 0x4f )
                        {
                            Util.Assert( regVals[ i ].Type == DEBUG_VALUE_TYPE.INVALID,
                                         Util.Sprintf( "Unexpected error trying to GetPsuedoDescriptionWide: hr={0}, i={1}, type={2}",
                                                       Util.FormatErrorCode( hr ),
                                                       i,
                                                       regVals[ i ].Type ) );
                        }
                        else
                        {
                            // INTae976bd6
                            yield return new DbgPseudoRegisterInfo( Debugger, "$fp", regVals[ i ], 0, 0, i );
                        }
                    }
                }
            } // end using( context saver )
        } // end _EnumeratePseudoRegisters()


        /// <summary>
        ///    Gets the platform-specific RegisterSet object for the current stack frame.
        /// </summary>
        public static DbgRegisterSetBase GetRegisterSet( DbgEngDebugger debugger )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            return GetRegisterSet( debugger, debugger.GetCurrentScopeFrame(), null );
        }

        /// <summary>
        ///    Gets the platform-specific RegisterSet object for the specified stack frame.
        /// </summary>
        public static DbgRegisterSetBase GetRegisterSet( DbgEngDebugger debugger,
                                                         DbgStackFrameInfo stackFrame )
        {
            return GetRegisterSet( debugger, stackFrame, null );
        }

        /// <summary>
        ///    Gets the platform-specific RegisterSet object for the specified stack frame,
        ///    using the baseline RegisterSet to compare to when creatin a colorized string.
        /// </summary>
        public static DbgRegisterSetBase GetRegisterSet( DbgEngDebugger debugger,
                                                         DbgStackFrameInfo stackFrame,
                                                         DbgRegisterSetBase baseline )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            var procType = debugger.GetEffectiveProcessorType();
            if( debugger.TargetIs32Bit )
            {
                if( IMAGE_FILE_MACHINE.THUMB2 == procType )
                {
                    return new Arm32RegisterSet( debugger, stackFrame, baseline );
                }
                else if( IMAGE_FILE_MACHINE.I386 == procType )
                {
                    return new x86RegisterSet( debugger, stackFrame, baseline );
                }
                else
                {
                    throw new NotImplementedException( Util.Sprintf( "Need to support effective processor type: {0} (32-bit)", procType ) );
                }
            }
            else
            {
                if( IMAGE_FILE_MACHINE.AMD64 == procType )
                {
                    return new x64RegisterSet( debugger, stackFrame, baseline );
                }
                else if( IMAGE_FILE_MACHINE.I386 == procType )
                {
                    // 64-bit dump of a 32-bit process.
                    return new x86RegisterSet( debugger, stackFrame, baseline );
                }
                else
                {
                    throw new NotImplementedException( Util.Sprintf( "Need to support effective processor type: {0} (64-bit)", procType ) );
                }
            }
        } // end GetRegisterSet()


        public abstract ColorString ToColorString();

        public override string ToString()
        {
            return ToColorString().ToString( false );
        }


        protected ColorString Disasm( ulong addr )
        {
            try
            {
                string s = Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        string disasm;
                        ulong endAddr;
                        CheckHr( m_debugControl.DisassembleWide( addr,
                                                                 (DEBUG_DISASM) 0,
                                                                 out disasm,
                                                                 out endAddr ) );
                        return disasm;
                    } );
                return Commands.ReadDbgDisassemblyCommand._ParseDisassembly( addr,
                                                                             s,
                                                                             null,
                                                                             true ).ToColorString();
            }
            catch( DbgProviderException dpe )
            {
                return new ColorString( ConsoleColor.Red, Util.Sprintf( "??? {0}", dpe.Message ) );
            }
        }


        protected ConsoleColor GetColorForDiffAgainstBaseline( string registerName )
        {
            if( null != Baseline )
            {
                if( !this[ registerName ].Value.Equals( Baseline[ registerName ].Value ) )
                {
                    if( this[ registerName ].DEBUG_VALUE.I64 == 0 )
                        return ConsoleColor.DarkRed;
                    else
                        return ConsoleColor.Red;
                }
            }
            if( this[ registerName ].DEBUG_VALUE.I64 == 0 )
                return ConsoleColor.DarkCyan;
            else
                return ConsoleColor.Cyan;
        } // end GetColorForDiffAgainstBaseline()


        // TODO: I'm not sure that I like this mutation... On the other hand,
        // register values on live targets will be mutable...
        internal DbgRegisterSetBase WithBaseline( DbgRegisterSetBase baseline )
        {
            Baseline = baseline;
            return this;
        }


        private PseudoRegisters m_pseudo;
        public PseudoRegisters Pseudo
        {
            get
            {
                if( null == m_pseudo )
                {
                    m_pseudo = new PseudoRegisters( this );
                }

                return m_pseudo;
            }
        } // end property Pseudo


        public class PseudoRegisters
        {
            private DbgRegisterSetBase m_rsb;
            private List< DbgPseudoRegisterInfo > m_pseudoRegisters;
            private Dictionary< string, DbgPseudoRegisterInfo > m_pseudoRegisterIndex;

            internal PseudoRegisters( DbgRegisterSetBase rsb )
            {
                m_rsb = rsb;
            }

            private void _GetPseudoRegisterInfo()
            {
                m_rsb.Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        m_pseudoRegisters = new List< DbgPseudoRegisterInfo>( m_rsb._EnumeratePseudoRegisters() );
                        m_pseudoRegisterIndex = new Dictionary<string, DbgPseudoRegisterInfo>( StringComparer.OrdinalIgnoreCase );
                        foreach( var ri in m_pseudoRegisters )
                        {
                            m_pseudoRegisterIndex.Add( ri.Name, ri );
                        }
                    } );
            } // end _GetPseudoRegisterInfo()

            public IEnumerable<DbgPseudoRegisterInfo> EnumeratePseudoRegisters()
            {
                if( null == m_pseudoRegisters )
                {
                    _GetPseudoRegisterInfo();
                }
                return m_pseudoRegisters;
            } // end EnumeratePseudoRegisters()


            // Expects the pseudo-register name, including the '$'.
            private DbgPseudoRegisterInfo _GetPseudoRegister( string registerName )
            {
                if( null == m_pseudoRegisters )
                {
                    _GetPseudoRegisterInfo();
                }
                DbgPseudoRegisterInfo reg;
                if( !m_pseudoRegisterIndex.TryGetValue( registerName, out reg ) )
                {
                    throw new DbgProviderException( Util.Sprintf( "Pseudo-register '{0}' not available.",
                                                                  registerName ),
                                                    "NoSuchPseudoRegister",
                                                    System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                    registerName );
                }
                return reg;
            } // end _GetPseudoRegister

            public DbgPseudoRegisterInfo this[ string registerName ]
            {
                get
                {
                    if( String.IsNullOrEmpty( registerName ) )
                        throw new ArgumentException( "You must supply a register name.", "registerName" );

                    if( '$' != registerName[ 0 ] )
                        registerName = "$" + registerName;

                    return _GetPseudoRegister( registerName );
                }
            } // end indexer
        } // end class PseudoRegisters
    } // end class DbgRegisterSetBase


    public class x64RegisterSet : DbgRegisterSetBase
    {
        public x64RegisterSet( DbgEngDebugger debugger,
                               DbgStackFrameInfo stackFrame,
                               DbgRegisterSetBase baseline )
            : base( debugger, stackFrame, baseline )
        {
        } // end constructor

        public DbgRegisterInfo Rax  { get { return this[ "Rax"  ]; } }
        public DbgRegisterInfo Rbx  { get { return this[ "Rbx"  ]; } }
        public DbgRegisterInfo Rcx  { get { return this[ "Rcx"  ]; } }
        public DbgRegisterInfo Rdx  { get { return this[ "Rdx"  ]; } }
        public DbgRegisterInfo Rsi  { get { return this[ "Rsi"  ]; } }
        public DbgRegisterInfo Rdi  { get { return this[ "Rdi"  ]; } }
        public DbgRegisterInfo Rip  { get { return this[ "Rip"  ]; } }
        public DbgRegisterInfo Rsp  { get { return this[ "Rsp"  ]; } }
        public DbgRegisterInfo Rbp  { get { return this[ "Rbp"  ]; } }
        public DbgRegisterInfo R8   { get { return this[ "R8"   ]; } }
        public DbgRegisterInfo R9   { get { return this[ "R9"   ]; } }
        public DbgRegisterInfo R10  { get { return this[ "R10"  ]; } }
        public DbgRegisterInfo R11  { get { return this[ "R11"  ]; } }
        public DbgRegisterInfo R12  { get { return this[ "R12"  ]; } }
        public DbgRegisterInfo R13  { get { return this[ "R13"  ]; } }
        public DbgRegisterInfo R14  { get { return this[ "R14"  ]; } }
        public DbgRegisterInfo R15  { get { return this[ "R15"  ]; } }
        public DbgRegisterInfo Iopl { get { return this[ "Iopl" ]; } }
        public DbgRegisterInfo Cs   { get { return this[ "Cs"   ]; } }
        public DbgRegisterInfo Ss   { get { return this[ "Ss"   ]; } }
        public DbgRegisterInfo Ds   { get { return this[ "Ds"   ]; } }
        public DbgRegisterInfo Es   { get { return this[ "Es"   ]; } }
        public DbgRegisterInfo Fs   { get { return this[ "Fs"   ]; } }
        public DbgRegisterInfo Gs   { get { return this[ "Gs"   ]; } }
        public DbgRegisterInfo Efl  { get { return this[ "Efl"  ]; } }

        private ColorString m_colorString;
        // Example:
        //
        // rax=000007f7d5e4e000 rbx=0000000000000000 rcx=0000000000000000
        // rdx=000007fcac628f98 rsi=0000000000000000 rdi=0000000000000000
        // rip=000007fcac571e00 rsp=0000000b9879fb78 rbp=0000000000000000
        //  r8=0000000000000000  r9=000007fcac628f98 r10=0000000000000000
        // r11=0000000000000000 r12=0000000000000000 r13=0000000000000000
        // r14=0000000000000000 r15=0000000000000000
        // iopl=0         nv up ei pl zr na po nc
        // cs=0033  ss=002b  ds=002b  es=002b  fs=0053  gs=002b             efl=00000246
        //   ntdll!DbgBreakPoint:
        //
        public override ColorString ToColorString()
        {
            if( null == m_colorString )
            {
                ConsoleColor color;
                ColorString cs = new ColorString( "rax=" );
                color = GetColorForDiffAgainstBaseline( "rax" );
                cs.Append( Rax.GetColorizedValueString( color ) );
                cs.Append( " rbx=" );
                color = GetColorForDiffAgainstBaseline( "rbx" );
                cs.Append( Rbx.GetColorizedValueString( color ) );
                cs.Append( " rcx=" );
                color = GetColorForDiffAgainstBaseline( "rcx" );
                cs.Append( Rcx.GetColorizedValueString( color ) );
                cs.AppendLine();

                cs.Append( "rdx=" );
                color = GetColorForDiffAgainstBaseline( "rdx" );
                cs.Append( Rdx.GetColorizedValueString( color ) );
                cs.Append( " rsi=" );
                color = GetColorForDiffAgainstBaseline( "rsi" );
                cs.Append( Rsi.GetColorizedValueString( color ) );
                cs.Append( " rdi=" );
                color = GetColorForDiffAgainstBaseline( "rdi" );
                cs.Append( Rdi.GetColorizedValueString( color ) );
                cs.AppendLine();

                cs.Append( "rip=" );
                color = GetColorForDiffAgainstBaseline( "rip" );
                cs.Append( Rip.GetColorizedValueString( color ) );
                cs.Append( " rsp=" );
                color = GetColorForDiffAgainstBaseline( "rsp" );
                cs.Append( Rsp.GetColorizedValueString( color ) );
                cs.Append( " rbp=" );
                color = GetColorForDiffAgainstBaseline( "rbp" );
                cs.Append( Rbp.GetColorizedValueString( color ) );
                cs.AppendLine();

                cs.Append( " r8=" );
                color = GetColorForDiffAgainstBaseline( "r8" );
                cs.Append( R8.GetColorizedValueString( color ) );
                cs.Append( "  r9=" );
                color = GetColorForDiffAgainstBaseline( "r9" );
                cs.Append( R9.GetColorizedValueString( color ) );
                cs.Append( " r10=" );
                color = GetColorForDiffAgainstBaseline( "r10" );
                cs.Append( R10.GetColorizedValueString( color ) );
                cs.AppendLine();

                cs.Append( "r11=" );
                color = GetColorForDiffAgainstBaseline( "r11" );
                cs.Append( R11.GetColorizedValueString( color ) );
                cs.Append( " r12=" );
                color = GetColorForDiffAgainstBaseline( "r12" );
                cs.Append( R12.GetColorizedValueString( color ) );
                cs.Append( " r13=" );
                color = GetColorForDiffAgainstBaseline( "r13" );
                cs.Append( R13.GetColorizedValueString( color ) );
                cs.AppendLine();

                cs.Append( "r14=" );
                color = GetColorForDiffAgainstBaseline( "r14" );
                cs.Append( R14.GetColorizedValueString( color ) );
                cs.Append( " r15=" );
                color = GetColorForDiffAgainstBaseline( "r15" );
                cs.Append( R15.GetColorizedValueString( color ) );
                cs.AppendLine();

                cs.Append( "iopl=" );
                color = GetColorForDiffAgainstBaseline( "iopl" );
                cs.AppendPushPopFg( color, ((ulong) Iopl.Value).ToString( "x" ) );
                // TODO:
                cs.AppendLine( "   TBD: flags" );

                cs.Append( "cs=" );
                color = GetColorForDiffAgainstBaseline( "cs" );
                cs.AppendPushPopFg( color, ((ushort) Cs.Value).ToString( "x4" ) );
                cs.Append( "  ss=" );
                color = GetColorForDiffAgainstBaseline( "ss" );
                cs.AppendPushPopFg( color, ((ushort) Ss.Value).ToString( "x4" ) );
                cs.Append( "  ds=" );
                color = GetColorForDiffAgainstBaseline( "ds" );
                cs.AppendPushPopFg( color, ((ushort) Ds.Value).ToString( "x4" ) );
                cs.Append( "  es=" );
                color = GetColorForDiffAgainstBaseline( "es" );
                cs.AppendPushPopFg( color, ((ushort) Es.Value).ToString( "x4" ) );
                cs.Append( "  fs=" );
                color = GetColorForDiffAgainstBaseline( "fs" );
                cs.AppendPushPopFg( color, ((ushort) Fs.Value).ToString( "x4" ) );
                cs.Append( "  gs=" );
                color = GetColorForDiffAgainstBaseline( "gs" );
                cs.AppendPushPopFg( color, ((ushort) Gs.Value).ToString( "x4" ) );
                cs.Append( "             efl=" );
                color = GetColorForDiffAgainstBaseline( "efl" );
                cs.Append( Efl.GetColorizedValueString( color ) );
                cs.AppendLine();

                cs.Append( DbgProvider.ColorizeSymbol( StackFrame.SymbolName ) );
                if( 0 != StackFrame.Displacement )
                {
                    cs.Append( "+0x" );
                    cs.Append( StackFrame.Displacement.ToString( "x" ) );
                }
                cs.AppendLine( ":" );
                cs.Append( Disasm( Rip.ValueAsPointer ) );

                m_colorString = cs;
            }
            return m_colorString;
        } // end ToString()
    } // end class x64RegisterSet


    public class x86RegisterSet : DbgRegisterSetBase
    {
        public x86RegisterSet( DbgEngDebugger debugger,
                               DbgStackFrameInfo stackFrame,
                               DbgRegisterSetBase baseline )
            : base( debugger, stackFrame, baseline )
        {
        } // end constructor


        public DbgRegisterInfo Eax  { get { return this[ "Eax"  ]; } }
        public DbgRegisterInfo Ebx  { get { return this[ "Ebx"  ]; } }
        public DbgRegisterInfo Ecx  { get { return this[ "Ecx"  ]; } }
        public DbgRegisterInfo Edx  { get { return this[ "Edx"  ]; } }
        public DbgRegisterInfo Esi  { get { return this[ "Esi"  ]; } }
        public DbgRegisterInfo Edi  { get { return this[ "Edi"  ]; } }
        public DbgRegisterInfo Eip  { get { return this[ "Eip"  ]; } }
        public DbgRegisterInfo Esp  { get { return this[ "Esp"  ]; } }
        public DbgRegisterInfo Ebp  { get { return this[ "Ebp"  ]; } }
        public DbgRegisterInfo Iopl { get { return this[ "Iopl" ]; } }
        public DbgRegisterInfo Cs   { get { return this[ "Cs"   ]; } }
        public DbgRegisterInfo Ss   { get { return this[ "Ss"   ]; } }
        public DbgRegisterInfo Ds   { get { return this[ "Ds"   ]; } }
        public DbgRegisterInfo Es   { get { return this[ "Es"   ]; } }
        public DbgRegisterInfo Fs   { get { return this[ "Fs"   ]; } }
        public DbgRegisterInfo Gs   { get { return this[ "Gs"   ]; } }
        public DbgRegisterInfo Efl  { get { return this[ "Efl"  ]; } }


        private ColorString m_colorString;
        // Example:
        //
        //   eax=7ffdb000 ebx=00000000 ecx=00000000 edx=7785f17d esi=00000000 edi=00000000
        //   eip=777f410c esp=040ef770 ebp=040ef79c iopl=0         nv up ei pl zr na pe nc
        //   cs=001b  ss=0023  ds=0023  es=0023  fs=003b  gs=0000             efl=00000246
        //   ntdll!DbgBreakPoint:
        //
        public override ColorString ToColorString()
        {
            if( null == m_colorString )
            {
                ConsoleColor color;
                ColorString cs = new ColorString( "eax=" );
                color = GetColorForDiffAgainstBaseline( "eax" );
                cs.Append( Eax.GetColorizedValueString( color ) );
                cs.Append( " ebx=" );
                color = GetColorForDiffAgainstBaseline( "ebx" );
                cs.Append( Ebx.GetColorizedValueString( color ) );
                cs.Append( " ecx=" );
                color = GetColorForDiffAgainstBaseline( "ecx" );
                cs.Append( Ecx.GetColorizedValueString( color ) );
                cs.Append( " edx=" );
                color = GetColorForDiffAgainstBaseline( "edx" );
                cs.Append( Edx.GetColorizedValueString( color ) );
                cs.Append( " esi=" );
                color = GetColorForDiffAgainstBaseline( "esi" );
                cs.Append( Esi.GetColorizedValueString( color ) );
                cs.Append( " edi=" );
                color = GetColorForDiffAgainstBaseline( "edi" );
                cs.Append( Edi.GetColorizedValueString( color ) );
                cs.AppendLine();

                cs.Append( "eip=" );
                color = GetColorForDiffAgainstBaseline( "eip" );
                cs.Append( Eip.GetColorizedValueString( color ) );
                cs.Append( " esp=" );
                color = GetColorForDiffAgainstBaseline( "esp" );
                cs.Append( Esp.GetColorizedValueString( color ) );
                cs.Append( " ebp=" );
                color = GetColorForDiffAgainstBaseline( "ebp" );
                cs.Append( Ebp.GetColorizedValueString( color ) );
                cs.Append( " iopl=" );
                color = GetColorForDiffAgainstBaseline( "iopl" );
                cs.AppendPushPopFg( color, ((uint) Iopl.Value).ToString( "x" ) );
                // TODO:
                cs.AppendLine( "   TBD: flags" );

                cs.Append( "cs=" );
                color = GetColorForDiffAgainstBaseline( "cs" );
                cs.AppendPushPopFg( color, ((uint) Cs.Value).ToString( "x4" ) );
                cs.Append( "  ss=" );
                color = GetColorForDiffAgainstBaseline( "ss" );
                cs.AppendPushPopFg( color, ((uint) Ss.Value).ToString( "x4" ) );
                cs.Append( "  ds=" );
                color = GetColorForDiffAgainstBaseline( "ds" );
                cs.AppendPushPopFg( color, ((uint) Ds.Value).ToString( "x4" ) );
                cs.Append( "  es=" );
                color = GetColorForDiffAgainstBaseline( "es" );
                cs.AppendPushPopFg( color, ((uint) Es.Value).ToString( "x4" ) );
                cs.Append( "  fs=" );
                color = GetColorForDiffAgainstBaseline( "fs" );
                cs.AppendPushPopFg( color, ((uint) Fs.Value).ToString( "x4" ) );
                cs.Append( "  gs=" );
                color = GetColorForDiffAgainstBaseline( "gs" );
                cs.AppendPushPopFg( color, ((uint) Gs.Value).ToString( "x4" ) );
                cs.Append( "             efl=" );
                color = GetColorForDiffAgainstBaseline( "efl" );
                cs.Append( Efl.GetColorizedValueString( color ) );
                cs.AppendLine();

                cs.Append( DbgProvider.ColorizeSymbol( StackFrame.SymbolName ) );
                if( 0 != StackFrame.Displacement )
                {
                    cs.Append( "+0x" );
                    cs.Append( StackFrame.Displacement.ToString( "x" ) );
                }
                cs.AppendLine( ":" );
                cs.Append( Disasm( Eip.ValueAsPointer ) );

                m_colorString = cs;
            }
            return m_colorString;
        } // end ToString()
    } // end class x86RegisterSet


    public class Arm32RegisterSet : DbgRegisterSetBase
    {
        public Arm32RegisterSet( DbgEngDebugger debugger,
                                 DbgStackFrameInfo stackFrame,
                               DbgRegisterSetBase baseline )
            : base( debugger, stackFrame, baseline )
        {
        } // end constructor

        public DbgRegisterInfo R0  { get { return this[ "R0"  ]; } }
        public DbgRegisterInfo R1  { get { return this[ "R1"  ]; } }
        public DbgRegisterInfo R2  { get { return this[ "R2"  ]; } }
        public DbgRegisterInfo R3  { get { return this[ "R3"  ]; } }
        public DbgRegisterInfo R4  { get { return this[ "R4"  ]; } }
        public DbgRegisterInfo R5  { get { return this[ "R5"  ]; } }
        public DbgRegisterInfo R6  { get { return this[ "R6"  ]; } }
        public DbgRegisterInfo R7  { get { return this[ "R7"  ]; } }
        public DbgRegisterInfo R8  { get { return this[ "R8"  ]; } }
        public DbgRegisterInfo R9  { get { return this[ "R9"  ]; } }
        public DbgRegisterInfo R10 { get { return this[ "R10" ]; } }
        public DbgRegisterInfo R11 { get { return this[ "R11" ]; } }
        public DbgRegisterInfo R12 { get { return this[ "R12" ]; } }

        public DbgRegisterInfo Sp  { get { return this[ "Sp"  ]; } }
        public DbgRegisterInfo Lr  { get { return this[ "Lr"  ]; } }
        public DbgRegisterInfo Pc  { get { return this[ "Pc"  ]; } }
        public DbgRegisterInfo Psr { get { return this[ "Psr" ]; } }

        public DbgRegisterInfo Nf  { get { return this[ "Nf"  ]; } }
        public DbgRegisterInfo Zf  { get { return this[ "Zf"  ]; } }
        public DbgRegisterInfo Cf  { get { return this[ "Cf"  ]; } }
        public DbgRegisterInfo Vf  { get { return this[ "Vf"  ]; } }
        public DbgRegisterInfo Qf  { get { return this[ "Qf"  ]; } }
        public DbgRegisterInfo If  { get { return this[ "If"  ]; } }
        public DbgRegisterInfo Ff  { get { return this[ "Ff"  ]; } }
        public DbgRegisterInfo Tf  { get { return this[ "Tf"  ]; } }

        public DbgRegisterInfo Mode { get { return this[ "Mode" ]; } }


        private ColorString m_colorString;
        // Example:
        //
        //    0:009> r
        //     r0=00000000  r1=00000000  r2=00000000  r3=76fdcf09  r4=00000000  r5=028df6e0
        //     r6=028df730  r7=00000000  r8=00000001  r9=01507858 r10=015156a8 r11=028df8e8
        //    r12=00000000  sp=028df6c8  lr=00000000  pc=76ccce24 psr=600f0030 -ZC-- Thumb
        //    KERNELBASE!RaiseFailFastException+0x60:
        //    76ccce24 f000f846 bl          KERNELBASE!SignalStartWerSvc (76ccceb4)
        //
        public override ColorString ToColorString()
        {
            if( null == m_colorString )
            {
                ConsoleColor color;
                ColorString cs = new ColorString( " r0=" );
                color = GetColorForDiffAgainstBaseline( "r0" );
                cs.Append( R0.GetColorizedValueString( color ) );
                cs.Append( "  r1=" );
                color = GetColorForDiffAgainstBaseline( "r1" );
                cs.Append( R1.GetColorizedValueString( color ) );
                cs.Append( "  r2=" );
                color = GetColorForDiffAgainstBaseline( "r2" );
                cs.Append( R2.GetColorizedValueString( color ) );
                cs.Append( "  r3=" );
                color = GetColorForDiffAgainstBaseline( "r3" );
                cs.Append( R3.GetColorizedValueString( color ) );
                cs.Append( "  r4=" );
                color = GetColorForDiffAgainstBaseline( "r4" );
                cs.Append( R4.GetColorizedValueString( color ) );
                cs.Append( "  r5=" );
                color = GetColorForDiffAgainstBaseline( "r5" );
                cs.Append( R5.GetColorizedValueString( color ) );
                cs.AppendLine();

                cs.Append( " r6=" );
                color = GetColorForDiffAgainstBaseline( "r6" );
                cs.Append( R6.GetColorizedValueString( color ) );
                cs.Append( "  r7=" );
                color = GetColorForDiffAgainstBaseline( "r7" );
                cs.Append( R7.GetColorizedValueString( color ) );
                cs.Append( "  r8=" );
                color = GetColorForDiffAgainstBaseline( "r8" );
                cs.Append( R8.GetColorizedValueString( color ) );
                cs.Append( "  r9=" );
                color = GetColorForDiffAgainstBaseline( "r9" );
                cs.Append( R9.GetColorizedValueString( color ) );
                cs.Append( " r10=" );
                color = GetColorForDiffAgainstBaseline( "r10" );
                cs.Append( R10.GetColorizedValueString( color ) );
                cs.Append( " r11=" );
                color = GetColorForDiffAgainstBaseline( "r11" );
                cs.Append( R11.GetColorizedValueString( color ) );
                cs.AppendLine();

                cs.Append( "r12=" );
                color = GetColorForDiffAgainstBaseline( "r12" );
                cs.Append( R12.GetColorizedValueString( color ) );
                cs.Append( "  sp=" );
                color = GetColorForDiffAgainstBaseline( "sp" );
                cs.Append( Sp.GetColorizedValueString( color ) );
                cs.Append( "  lr=" );
                color = GetColorForDiffAgainstBaseline( "lr" );
                cs.Append( Lr.GetColorizedValueString( color ) );
                cs.Append( "  pc=" );
                color = GetColorForDiffAgainstBaseline( "pc" );
                cs.Append( Pc.GetColorizedValueString( color ) );
                cs.Append( " psr=" );
                color = GetColorForDiffAgainstBaseline( "psr" );
                cs.Append( Psr.GetColorizedValueString( color ) );

                // TODO:
                cs.AppendLine( " TBD: flags and mode" );

                cs.Append( DbgProvider.ColorizeSymbol( StackFrame.SymbolName ) );
                if( 0 != StackFrame.Displacement )
                {
                    cs.Append( "+0x" );
                    cs.Append( StackFrame.Displacement.ToString( "x" ) );
                }
                cs.AppendLine( ":" );
                cs.Append( Disasm( Pc.ValueAsPointer ) );

                m_colorString = cs;
            }
            return m_colorString;
        } // end ToString()
    } // end class Arm32RegisterSet
}
