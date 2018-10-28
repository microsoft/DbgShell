using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using DbgEngWrapper;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [System.Diagnostics.DebuggerDisplay( "@{Name}: {DEBUG_VALUE.I64}" )]
    public abstract class DbgRegisterInfoBase : DebuggerObject
    {
        public string Name { get; private set; }

        public DEBUG_VALUE DEBUG_VALUE { get; private set; }

        public abstract object Value
        {
            get;
        } // end property Value

        public readonly uint DbgEngIndex;


        protected object GetValueFromRawValue()
        {
            switch( DEBUG_VALUE.Type )
            {
                case DEBUG_VALUE_TYPE.INVALID:
                    Util.Fail( "I don't think we should actually see DEBUG_VALUE_TYPE.INVALID." );
                    return null;
                case DEBUG_VALUE_TYPE.INT8:
                    return DEBUG_VALUE.I8;
                case DEBUG_VALUE_TYPE.INT16:
                    return DEBUG_VALUE.I16;
                case DEBUG_VALUE_TYPE.INT32:
                    return DEBUG_VALUE.I32;
                case DEBUG_VALUE_TYPE.INT64:
                    return DEBUG_VALUE.I64;
                case DEBUG_VALUE_TYPE.FLOAT32:
                    return DEBUG_VALUE.F32;
                case DEBUG_VALUE_TYPE.FLOAT64:
                    return DEBUG_VALUE.F64;
                case DEBUG_VALUE_TYPE.FLOAT80:
                    // TODO: Figure out how to get the data into a Decimal?
                    throw new NotImplementedException( "Haven't dealt with weird precision types yet." );
                case DEBUG_VALUE_TYPE.FLOAT82:
                    // TODO: Figure out how to get the data into a Decimal?
                    throw new NotImplementedException( "Haven't dealt with weird precision types yet." );
                case DEBUG_VALUE_TYPE.FLOAT128:
                    // TODO: Figure out how to get the data into a Decimal?
                    throw new NotImplementedException( "Haven't dealt with weird precision types yet." );
                case DEBUG_VALUE_TYPE.VECTOR64:
                    unsafe
                    {
                        DEBUG_VALUE dv = DEBUG_VALUE; // have to copy this to a local var to access the pointer
                        IntPtr src = new IntPtr( dv.VI8 );
                        byte[] dest = new byte[ 8 ];
                        Marshal.Copy( src, dest, 0, dest.Length );
                        return dest;
                    }
                case DEBUG_VALUE_TYPE.VECTOR128:
                    unsafe
                    {
                        DEBUG_VALUE dv = DEBUG_VALUE; // have to copy this to a local var to access the pointer
                        IntPtr src = new IntPtr( dv.VI8 );
                        byte[] dest = new byte[ 16 ];
                        Marshal.Copy( src, dest, 0, dest.Length );
                        return dest;
                    }
                default:
                    var msg = Util.Sprintf( "Unexpected DEBUG_VALUE_TYPE: {0}.", DEBUG_VALUE.Type );
                    Util.Fail( msg );
                    throw new NotSupportedException( msg );
            }
        } // end GetValueFromRawValue()


        /// <summary>
        ///    Retrieves the value of the register as a ulong. Note that this will throw if
        ///    the register does not represent a pointer (uint on 32-bit, ulong on 64).
        /// </summary>
        public ulong ValueAsPointer
        {
            get
            {
                // TODO: I think this does not work for some pseudo registers (like $peb),
                // but should...

                object val = Value;
                if( val is uint )
                    return (ulong) (uint) val;
                else
                    return (ulong) val;
            }
        } // end proprety ValueAsPointer


        public string GetValueString()
        {
            object valObj = Value;
            if( valObj is byte )
            {
                return Util.Sprintf( "{0:x2}", valObj );
            }
            else if( valObj is short )
            {
                return Util.Sprintf( "{0:x4}", valObj );
            }
            else if( valObj is ushort )
            {
                return Util.Sprintf( "{0:x4}", valObj );
            }
            else if( valObj is int )
            {
                return Util.Sprintf( "{0:x8}", valObj );
            }
            else if( valObj is uint )
            {
                return Util.Sprintf( "{0:x8}", valObj );
            }
            else if( valObj is long )
            {
                return Util.Sprintf( "{0:x16}", valObj );
            }
            else if( valObj is ulong )
            {
                return Util.Sprintf( "{0:x16}", valObj );
            }
            else if( valObj is byte[] )
            {
                return Util.FormatBytes( valObj as byte[] );
            }
            else // float, double, what else?
            {
                if( null == valObj )
                    return "<value type not supported yet>";
                else
                    return valObj.ToString();
                    //return Util.GetGenericTypeName( valObj );
            }
        } // end GetValueString()


        public ColorString GetColorizedValueString( ConsoleColor color )
        {
            object valObj = Value;
            if( valObj is byte[] )
            {
                // TODO: add another Util.FormatBytes overload?
                byte[] bytes = valObj as byte[];
                StringBuilder sb = new StringBuilder( bytes.Length * 3 );
                bool prependSpace = false;
                foreach( byte b in bytes )
                {
                    if( prependSpace )
                        sb.Append( ' ' );
                    else
                        prependSpace = true;

                    sb.AppendFormat( "{0:x2}", b );
                }
                return new ColorString().AppendPushPopFg( color, sb.ToString() );
            }

            string noLeadingZeroes;
            try
            {
                noLeadingZeroes = Util.Sprintf( "{0:x}", valObj );
            }
            catch( FormatException )
            {
                Util.Fail( "I guess we need to handle this case..." );
                // float, double, what else?

                if( null == valObj )
                    return new ColorString().AppendPushPopFg( color, "<value type not supported yet>" );
                else
                    //return Util.GetGenericTypeName( valObj );
                    return new ColorString().AppendPushPopFg( color, valObj.ToString() );
            }

            int desiredWidth = 0;
            if( valObj is byte )
            {
                desiredWidth = 2;
            }
            else if( valObj is short )
            {
                desiredWidth = 4;
            }
            else if( valObj is ushort )
            {
                desiredWidth = 4;
            }
            else if( valObj is int )
            {
                desiredWidth = 8;
            }
            else if( valObj is uint )
            {
                desiredWidth = 8;
            }
            else if( valObj is long )
            {
                desiredWidth = 16;
            }
            else if( valObj is ulong )
            {
                desiredWidth = 16;
            }
            else // float, double, what else?
            {
                Util.Fail( "missed something" );
                if( null == valObj )
                    return new ColorString().AppendPushPopFg( color, "<value type not supported yet>" );
                else
                    //return Util.GetGenericTypeName( valObj );
                    return new ColorString().AppendPushPopFg( color, valObj.ToString() );
            }

            if( noLeadingZeroes.Length == desiredWidth )
                return new ColorString().AppendPushPopFg( color, noLeadingZeroes );

            Util.Assert( desiredWidth > noLeadingZeroes.Length );
            ColorString cs = new ColorString();

            ConsoleColor darkColor = color;
            int iDarkColor = ((int) color) - 8;
            if( iDarkColor > 0 )
                darkColor = (ConsoleColor) iDarkColor;

            cs.AppendPushPopFg( darkColor, new String( '0', desiredWidth - noLeadingZeroes.Length ) );
            cs.AppendPushPopFg( color, noLeadingZeroes );
            return cs;
        } // end GetColorizedValueString()


        protected DbgRegisterInfoBase( DbgEngDebugger debugger,
                                       string name,
                                       DEBUG_VALUE rawVal,
                                       uint dbgEngIndex )
            : base( debugger )
        {
            if( String.IsNullOrEmpty( name ) )
                throw new ArgumentException( "You must supply a register name.", "name" );

            Name = name;
            DEBUG_VALUE = rawVal;
            DbgEngIndex = dbgEngIndex;
        } // end constructor
    } // end class DbgRegisterInfoBase


    public class DbgRegisterInfo : DbgRegisterInfoBase
    {
        public bool IsSubregister
        {
            get
            {
                return 0 != (Description.Flags & DEBUG_REGISTER.SUB_REGISTER);
            }
        }

        public DEBUG_REGISTER_DESCRIPTION Description { get; private set; }

        public override object Value
        {
            get
            {
                return GetValueFromRawValue();
            }
        } // end property Value


        public DbgRegisterInfo( DbgEngDebugger debugger,
                                string name,
                                DEBUG_VALUE rawVal,
                                DEBUG_REGISTER_DESCRIPTION description,
                                uint dbgEngIndex )
            : base( debugger, name, rawVal, dbgEngIndex )
        {
            Description = description;
        } // end constructor


        public static DbgRegisterInfo GetDbgRegisterInfoForIndex( DbgEngDebugger debugger,
                                                                  uint registerIndex )
        {
            return debugger.ExecuteOnDbgEngThread( () =>
                {
                    WDebugRegisters dr = (WDebugRegisters) debugger.DebuggerInterface;
                    DEBUG_REGISTER_DESCRIPTION desc;

                    string name;
                    StaticCheckHr( dr.GetDescriptionWide( registerIndex,
                                                          out name,
                                                          out desc ) );

                    // Don't use dr.GetValue, because it won't respect the context.
                    DEBUG_VALUE[] dvs;
                    StaticCheckHr( dr.GetValues2( DEBUG_REGSRC.FRAME,
                                                  new uint[] { registerIndex },
                                                  out dvs ) );
                    return new DbgRegisterInfo( debugger, name, dvs[ 0 ], desc, registerIndex );
                } );
        } // end factory


        private const uint CV_ALLREG_VFRAME = 30006;


        /// <summary>
        ///    Gets a DbgRegisterInfo object for a CV_HREG_e value (such as you might find
        ///    in a SYMBOL_INFO structure).
        /// </summary>
        public static DbgRegisterInfo GetDbgRegisterInfoFor_CV_HREG_e( DbgEngDebugger debugger,
                                                                       uint CV_HREG_e_val )
        {
            return debugger.ExecuteOnDbgEngThread( () =>
                {
                    var dc = (WDebugControl) debugger.DebuggerInterface;
                    IMAGE_FILE_MACHINE effmach;
                    StaticCheckHr( dc.GetEffectiveProcessorType( out effmach ) );
                    if( CV_ALLREG_VFRAME == CV_HREG_e_val )
                    {
                        // See MachineInfo::CvRegToMachine.
                        if( IMAGE_FILE_MACHINE.AMD64 == effmach )
                        {
                            CV_HREG_e_val = 335; // CV_AMD64_RSP
                        }
                        else if( IMAGE_FILE_MACHINE.I386 == effmach )
                        {
                            // TODO: Drat... It /might/ be EBP, but likely not--it's likely some
                            // magical "virtual frame" value that I don't know how to get. I'll
                            // have to ask the debugger team for a way to get that.
                            //CV_HREG_e_val = 22; // CV_REG_EBP
                            throw new DbgProviderException( "On x86, I don't know how to find the \"virtual frame\" value that corresponds to CV_ALLREG_VFRAME. Sorry. Use Get-DbgLocalSymbol instead of Get-Symbol.",
                                                            "CV_ALLREG_VFRAME_on_x86_notSupported",
                                                            System.Management.Automation.ErrorCategory.NotImplemented,
                                                            CV_HREG_e_val );
                        }
                        else
                        {
                            throw new NotSupportedException( Util.Sprintf( "CV_ALLREG_VFRAME not supported for platform {0}",
                                                                           effmach ) );
                        }
                    }

                    uint dbgengRegIdx = CvRegMap.TryMapCvToDbgEngIndex( debugger,
                                                                        effmach,
                                                                        CV_HREG_e_val );

                    if( dbgengRegIdx == DEBUG_ANY_ID )
                    {
                        throw new NotSupportedException( Util.Sprintf( "I don't know how to map this CV_HREG_e value '{0}' for platform '{1}'.",
                                                                       (CV_HREG_e) CV_HREG_e_val,
                                                                       effmach ) );
                    }

                    return GetDbgRegisterInfoForIndex( debugger, dbgengRegIdx );
                } );
        } // end factory
    } // end class DbgRegisterInfo


    public class DbgPseudoRegisterInfo : DbgRegisterInfoBase
    {
        public ulong TypeModule { get; private set; }
        public uint TypeId { get; private set; }


        public override object Value
        {
            get
            {
                if( (0 == TypeModule) && (0 == TypeId) )
                {
                    return GetValueFromRawValue();
                }

                // The debugger assigns generated types to these registers, which we
                // can't easily support well (because dbgeng won't let us get at the
                // details of the generated types). Additionally, the generated "type"
                // does not necessarily make any sense (for instance, the generated type
                // for $argreg is UInt8*, but it contains a DBG_STATUS_XXX value (like
                // DBG_STATUS_CONTROL_C, DBG_STATUS_SYSRQ, DBG_STATUS_BUGCHECK_FIRST,
                // DBG_STATUS_FATAL, etc.) (not to be confused with DEBUG_STATUS_XXX
                // values).
                //
                // So we'll hardcode some things.

                if( (0 == Util.Strcmp_OI( Name, "$ra" )) ||
                    (0 == Util.Strcmp_OI( Name, "$thread" )) ||
                    (0 == Util.Strcmp_OI( Name, "$proc" )) ||
                    (0 == Util.Strcmp_OI( Name, "$retreg" )) ||
                    (0 == Util.Strcmp_OI( Name, "$argreg" )) ||
                    (0 == Util.Strcmp_OI( Name, "$retreg64" )) ||
                    (0 == Util.Strcmp_OI( Name, "$ip" )) ||
                    (0 == Util.Strcmp_OI( Name, "$eventip" )) ||
                    (0 == Util.Strcmp_OI( Name, "$previp" )) ||
                    (0 == Util.Strcmp_OI( Name, "$exentry" )) ||
                    (0 == Util.Strcmp_OI( Name, "$scopeip" )) ||
                    (0 == Util.Strcmp_OI( Name, "$csp" )) )
                {
                    return GetValueFromRawValue();
                }

                if( 0 == Util.Strcmp_OI( Name, "$peb" ) )
                {
                    var si = DbgHelp.EnumTypesByName( Debugger.DebuggerInterface,
                                                      0,
                                                      "ntdll!_PEB",
                                                      System.Threading.CancellationToken.None ).FirstOrDefault();
                    if( null == si )
                    {
                        throw new DbgProviderException( "Can't find type ntdll!_PEB. No symbols?",
                                                        "NoPebType",
                                                        System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                        this );
                    }

                    var type = DbgTypeInfo.GetNamedTypeInfo( Debugger,
                                                             si.ModBase,
                                                             si.TypeIndex,
                                                             si.Tag,
                                                             Debugger.GetCurrentTarget() );
                    return new DbgSimpleSymbol( Debugger,
                                                Name,
                                                type,
                                                DEBUG_VALUE.I64 ).Value;
                }

                if( 0 == Util.Strcmp_OI( Name, "$teb" ) )
                {
                    var si = DbgHelp.EnumTypesByName( Debugger.DebuggerInterface,
                                                      0,
                                                      "ntdll!_TEB",
                                                      System.Threading.CancellationToken.None ).FirstOrDefault();
                    if( null == si )
                    {
                        throw new DbgProviderException( "Can't find type ntdll!_TEB. No symbols?",
                                                        "NoTebType",
                                                        System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                        this );
                    }

                    var type = DbgTypeInfo.GetNamedTypeInfo( Debugger,
                                                             si.ModBase,
                                                             si.TypeIndex,
                                                             si.Tag,
                                                             Debugger.GetCurrentTarget() );

                    return new DbgSimpleSymbol( Debugger,
                                                Name,
                                                type,
                                                DEBUG_VALUE.I64 ).Value;
                }

                if( 0 == Util.Strcmp_OI( Name, "$dbgtime" ) )
                {
                    return DateTime.FromFileTime( (long) DEBUG_VALUE.I64 );
                }

                DbgNamedTypeInfo dnti;
                try
                {
                    dnti = DbgTypeInfo.GetNamedTypeInfo( Debugger,
                                                         TypeModule,
                                                         TypeId,
                                                         Debugger.GetCurrentTarget() );
                }
                catch( Exception e )
                {
                    Util.Fail( Util.Sprintf( "Error getting type info for pseudo-register: {0}",
                                             Util.GetExceptionMessages( e ) ) );
                    return GetValueFromRawValue();
                }
                return new DbgSimpleSymbol( Debugger,
                                            Name,
                                            dnti,
                                            this ).Value;
            }
        } // end property Value


        public DbgPseudoRegisterInfo( DbgEngDebugger debugger,
                                      string name,
                                      DEBUG_VALUE rawVal,
                                      ulong typeMod,
                                      uint typeId,
                                      uint dbgEngIndex )
            : base( debugger, name, rawVal, dbgEngIndex )
        {
            TypeModule = typeMod;
            TypeId = typeId;
        } // end constructor


        public static DbgPseudoRegisterInfo GetDbgPsedoRegisterInfo( DbgEngDebugger debugger,
                                                                     string name )
        {
            return debugger.ExecuteOnDbgEngThread( () =>
                {
                    WDebugRegisters dr = (WDebugRegisters) debugger.DebuggerInterface;

                    uint idx;
                    StaticCheckHr( dr.GetPseudoIndexByNameWide( name, out idx ) );

                    string tmpName;
                    ulong typeMod;
                    uint typeId;
                    StaticCheckHr( dr.GetPseudoDescriptionWide( idx,
                                                                out tmpName,
                                                                out typeMod,
                                                                out typeId ) );

                    DEBUG_VALUE[] dvs;
                    StaticCheckHr( dr.GetPseudoValues( DEBUG_REGSRC.FRAME,
                                                       1,   // count
                                                       idx, // start
                                                       out dvs ) );

                    return new DbgPseudoRegisterInfo( debugger,
                                                      tmpName,
                                                      dvs[ 0 ],
                                                      typeMod,
                                                      typeId,
                                                      DEBUG_ANY_ID );
                } );
        } // end factory
    } // end class DbgPseudoRegisterInfo
}
