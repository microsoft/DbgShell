using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    // A "long double" is a 10-byte (80-bit) floating-point value. It is mostly out of
    // common use, but it still shows up in some constants in places (like ntdll).
    public class LongDouble
    {
        private readonly string m_str;
        public readonly byte[] RawBytes;
        public override string ToString()
        {
            return m_str;
        }

        internal LongDouble( byte[] rawBytes )
        {
            RawBytes = rawBytes;
            m_str = NativeMethods._uldtoa( rawBytes );
        }
    } // end class LongDouble


    [DebuggerDisplay( "BaseType: {BaseType} {Size}B, Id {TypeId}" )]
    public class DbgBaseTypeInfo : DbgNamedTypeInfo
    {
        public readonly BasicType BaseType;


        public bool TryConvertToPrimitive( DbgSymbol symbol, out object primitive, out bool isBitfield )
        {
            if( null == symbol )
                throw new ArgumentNullException( "symbol" );

            isBitfield = false;
            primitive = null;

            if( symbol.IsConstant )
            {
                // Is this really the right place for this code?

                primitive = symbol.GetConstantValue();
            }
            else
            {
                switch( BaseType )
                {
                    case BasicType.btChar:
                        Util.Assert( 1 == Size );
                        primitive = symbol.ReadAs_sbyte();
                        break;

                    case BasicType.btWChar:
                        Util.Assert( 2 == Size );
                        primitive = symbol.ReadAs_WCHAR();
                        break;

                    case BasicType.btInt:
                    case BasicType.btLong:
                        switch( Size )
                        {
                            case 1: primitive = symbol.ReadAs_sbyte(); break;
                            case 2: primitive = symbol.ReadAs_short(); break;
                            case 4: primitive = symbol.ReadAs_Int32(); break;
                            case 8: primitive = symbol.ReadAs_Int64(); break;
                            default: Util.Fail( "strangely-sized int" ); return false;
                        }
                        break;

                    case BasicType.btUInt:
                    case BasicType.btULong:
                        switch( Size )
                        {
                            case 1: primitive = symbol.ReadAs_byte(); break;
                            case 2: primitive = symbol.ReadAs_ushort(); break;
                            case 4: primitive = symbol.ReadAs_UInt32(); break;
                            case 8: primitive = symbol.ReadAs_UInt64(); break;
                            default: Util.Fail( "strangely-sized uint" ); return false;
                        }
                        break;

                    case BasicType.btBool:
                        switch( Size )
                        {
                            case 1: primitive = symbol.ReadAs_CPlusPlusBool();
                                break;
                            case 2: Util.Fail( "Does this happen?" ); primitive = symbol.ReadAs_short() == 0 ? false : true;
                                break;
                            case 4: Util.Fail( "Does this happen?" ); primitive = symbol.ReadAs_Int32() == 0 ? false : true;
                                break;
                            case 8: Util.Fail( "Does this happen?" ); primitive = symbol.ReadAs_Int64() == 0 ? false : true;
                                break;
                            default: Util.Fail( "strangely-sized bool" );
                                return false;
                        }
                        break;

                    case BasicType.btFloat:
                        switch( Size )
                        {
                            case 4: primitive = symbol.ReadAs_float(); break;
                            case 8: primitive = symbol.ReadAs_double(); break;
                            case 10: primitive = new LongDouble( Debugger.ReadMem( symbol.Address, 10 ) ); break;
                            default: Util.Fail( "strangely-sized float" ); return false;
                        }
                        break;

                    case BasicType.btHresult:
                        Util.Assert( 4 == Size );
                        primitive = symbol.ReadAs_UInt32();
                        break;

                        // TODO
                //  case BasicType.btBSTR:

                        // TODO
                //  case BasicType.btVariant

                        // TODO: other types
                } // end switch( BaseType )
            }

            DbgMemberSymbol memberSym = symbol as DbgMemberSymbol;
            if( null != memberSym )
            {
                if( memberSym.MemberInfo.IsBitfield )
                {
                    isBitfield = true;
                    Util.Assert( BaseType == BasicType.btInt   ||
                                 BaseType == BasicType.btLong  ||
                                 BaseType == BasicType.btUInt  ||
                                 BaseType == BasicType.btULong ||
                                 BaseType == BasicType.btBool );

                    // We'll use 'dynamic' to get runtime dynamic dispatch (so the correct
                    // overload will get chosen at runtime).
                    try
                    {
                        if( BasicType.btBool == BaseType )
                        {
                            Util.Assert( 1 == Size );
                            // Oops; we've already converted to true/false. Need to read
                            // the full byte value again.
                            primitive = BitHelper.ExtractBitfield( (dynamic) symbol.ReadAs_byte(),
                                                                   memberSym.MemberInfo.BitfieldPosition,
                                                                   memberSym.MemberInfo.BitfieldLength );
                            primitive = ((byte) primitive) != 0;
                        }
                        else
                        {
                            primitive = BitHelper.ExtractBitfield( (dynamic) primitive,
                                                                   memberSym.MemberInfo.BitfieldPosition,
                                                                   memberSym.MemberInfo.BitfieldLength );
                        }
                    }
                    catch( Exception e )
                    {
                        LogManager.Trace( "Failure trying to extract bitfield from a {0}: {1}",
                                          primitive.GetType().FullName,
                                          Util.GetExceptionMessages( e ) );
                        throw;
                    }
                } // end if( isBitfield )
            } // end if( null != memberSym )

            return null != primitive;
        } // end TryConvertToPrimitive()


        public bool IsSigned
        {
            get
            {
                switch( BaseType )
                {
                    case BasicType.btNoType:
                        return false;

                    case BasicType.btVoid:
                        return false;

                    case BasicType.btChar:
                        return true; // well now we're making judgement calls (can be wrong if compiled with /J)

                    case BasicType.btWChar:
                        return false; // well now we're making judgement calls (can be wrong if compiled with /J)

                    case BasicType.btInt:
                        return true;

                    case BasicType.btUInt:
                        return false;

                    case BasicType.btFloat:
                        return true;

                    case BasicType.btBCD:
                        Util.Fail( "Help me; what is this btBCD thing?" );
                        return true; // no idea what this actually is

                    case BasicType.btBool:
                        return false;

                    case BasicType.btLong:
                        return true;

                    case BasicType.btULong:
                        return false;

                    case BasicType.btCurrency:
                        return true;

                    case BasicType.btDate:
                        return false;

                    case BasicType.btVariant:
                        return false;

                    case BasicType.btComplex:
                        return true;

                    case BasicType.btBit:
                        return false;

                    case BasicType.btBSTR:
                        return false;

                    case BasicType.btHresult:
                        return true;

                    case BasicType.btChar16:
                    case BasicType.btChar32:
                        return false;

                    default:
                        Util.Fail( Util.Sprintf( "Unexpected BasicType: {0}", BaseType ) );
                        return false;
                }
            }
        } // end IsSigned


        public DbgBaseTypeInfo AsDifferentSize( ulong newSize )
        {
            // Should I check if the size makes any sense?

            return new DbgBaseTypeInfo( Debugger, Module, TypeId, BaseType, newSize );
        }


        // Indexed by BasicType
        private static readonly string[] sm_btNames = new string[]
        {
            "NOTYPE",                      // btNoType   = 0,
            "Void",                        // btVoid     = 1,
            "Char",                        // btChar     = 2,
            "WChar",                       // btWChar    = 3,
            "<shouldn't see this>",        //             (4)
            "<shouldn't see this>",        //             (5)
            "Int",                         // btInt      = 6,
            "UInt",                        // btUInt     = 7,
            "Float",                       // btFloat    = 8,
            "BCD",                         // btBCD      = 9,     // TODO: <-- what's that?
            "Bool",                        // btBool     = 10,
            "<shouldn't see this>",        //             (11)
            "<shouldn't see this>",        //             (12)
            "Int",                         // btLong     = 13,  <-- note that I just call these "int" as well, for simplicity
            "UInt",                        // btULong    = 14,  <-- note that I just call these "uint" as well, for simplicity
            "<shouldn't see this>",        //             (15)
            "<shouldn't see this>",        //             (16)
            "<shouldn't see this>",        //             (17)
            "<shouldn't see this>",        //             (18)
            "<shouldn't see this>",        //             (19)
            "<shouldn't see this>",        //             (20)
            "<shouldn't see this>",        //             (21)
            "<shouldn't see this>",        //             (22)
            "<shouldn't see this>",        //             (23)
            "<shouldn't see this>",        //             (24)
            "Currency",                    // btCurrency = 25,
            "Date",                        // btDate     = 26,
            "Variant",                     // btVariant  = 27,
            "Complex",                     // btComplex  = 28,
            "Bit",                         // btBit      = 29,    // TODO: what's the meaning of the "size" of this?
            "BStr",                        // btBSTR     = 30,
            "HResult",                     // btHresult  = 31,
            "char16_t",                    // btChar16   = 32,
            "char32_t",                    // btChar16   = 33,
        };

        private static readonly int[] sm_shouldNotSeeThese = new int[]
        {
            4, 5, 11, 12, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24
        };


        private static string _GetDefaultName( int idx, ulong size )
        {
            return Util.Sprintf( "{0}{1}B", sm_btNames[ idx ], size );
        } // end _GetDefaultName()


        private static string _GetTypeNameForBaseType( DbgEngDebugger debugger,
                                                       BasicType baseType,
                                                       ulong size )
        {
            int idx = (int) baseType;
            if( (idx < 0) || (idx > (sm_btNames.Length - 1)) )
                throw new ArgumentOutOfRangeException( Util.Sprintf( "Invalid BasicType value: {0}", baseType ),
                                                       "baseType" );

            Util.Assert( !sm_shouldNotSeeThese.Contains( idx ) );

            switch( baseType )
            {
                case BasicType.btUInt:
                case BasicType.btULong:
                    if( 1 == size ) return "byte";
                    if( 2 == size ) return "ushort";
                    return _GetDefaultName( idx, size );

                case BasicType.btInt:
                case BasicType.btLong:
                    if( 1 == size ) return "sbyte";
                    if( 2 == size ) return "short";
                    return _GetDefaultName( idx, size );

                case BasicType.btHresult:
                    Util.Assert( 4 == size );
                    return "HRESULT";

                case BasicType.btWChar:
                    Util.Assert( 2 == size );
                    return "WCHAR"; // TODO: or should this correspond to wchar_t?

                case BasicType.btChar:
                    Util.Assert( 1 == size );
                    return "char";

                case BasicType.btBool:
                    if( 1 == size ) return "bool";
                    if( 4 == size ) return "BOOL";
                    Util.Fail( "Oddly-sized bool." );
                    return _GetDefaultName( idx, size );

                case BasicType.btBSTR:
                    Util.Assert( GetTargetPointerSize( debugger ) == size );
                    return "BSTR";

                case BasicType.btVariant:
                    Util.Assert( 24 == size ); // right?
                    return "VARIANT";

                case BasicType.btVoid:
                    Util.Assert( 0 == size ); // we never know how big it is, right?
                    return "Void";

                case BasicType.btChar16:
                    Util.Assert( 2 == size );
                    return "char16_t";

                case BasicType.btChar32:
                    Util.Assert( 4 == size );
                    return "char32_t";

                default:
                    return _GetDefaultName( idx, size );
            }
        } // end _GetTypeNameForBaseType()


        public DbgBaseTypeInfo( DbgEngDebugger debugger,
                                ulong moduleBase,
                                uint typeId,
                                BasicType baseType,
                                ulong size,
                                DbgTarget target )
            : base( debugger,
                    moduleBase,
                    typeId,
                    SymTag.BaseType,
                    _GetTypeNameForBaseType( debugger, baseType, size ),
                    size,
                    target )
        {
            BaseType = baseType;
        } // end constructor

        public DbgBaseTypeInfo( DbgEngDebugger debugger,
                                DbgModuleInfo module,
                                uint typeId,
                                BasicType baseType,
                                ulong size )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    baseType,
                    size,
                    module.Target )
        {
            __mod = module;
        } // end constructor


        public static DbgBaseTypeInfo GetBaseTypeInfo( DbgEngDebugger debugger,
                                                       DbgModuleInfo module,
                                                       uint typeId )
        {
            BasicType baseType;
            ulong size;
            DbgHelp.GetBaseTypeInfo( debugger.DebuggerInterface,
                                     module.BaseAddress,
                                     typeId,
                                     out baseType,
                                     out size );
            return new DbgBaseTypeInfo( debugger, module, typeId, baseType, size );
        }
    } // end class DbgBaseTypeInfo
}

