using System;
using System.Collections.Generic;
using DbgEngWrapper;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents a module (loaded or unloaded) in the target.
    /// </summary>
    /// <remarks>
    ///    The IComparable implementation sorts on BaseAddress.
    /// </remarks>
    public class DbgModuleInfo : DebuggerObject,
                                 IEquatable< DbgModuleInfo >,
                                 IComparable< DbgModuleInfo >,
                                 ISupportColor
    {
        private WDebugSymbols m_ds;

        private WDebugSymbols _DS
        {
            get
            {
                if( null == m_ds )
                {
                    Debugger.ExecuteOnDbgEngThread( () =>
                        {
                            m_ds = (WDebugSymbols) Debugger.DebuggerInterface;
                        } );
                }
                return m_ds;
            }
        } // end property _DS


        private string _GetName( DEBUG_MODNAME which )
        {
            uint nameSizeHint;
            switch( which )
            {
                case DEBUG_MODNAME.IMAGE:
                    nameSizeHint = NativeParams.ImageNameSize;
                    break;
                case DEBUG_MODNAME.LOADED_IMAGE:
                    nameSizeHint = NativeParams.LoadedImageNameSize;
                    break;
                case DEBUG_MODNAME.MAPPED_IMAGE:
                    nameSizeHint = NativeParams.MappedImageNameSize;
                    break;
                case DEBUG_MODNAME.MODULE:
                    nameSizeHint = NativeParams.ModuleNameSize;
                    break;
                case DEBUG_MODNAME.SYMBOL_FILE:
                    nameSizeHint = NativeParams.SymbolFileNameSize;
                    break;
                default:
                    var msg = Util.Sprintf( "Unknown DEBUG_MODNAME value: {0}", which );
                    Util.Fail( msg );
                    throw new NotSupportedException( msg );
            }
            if( 0 == nameSizeHint )
            {
                // Sometimes this happens, like for unloaded modules. (I don't know why,
                // since unloaded modules have names too...)
                nameSizeHint = 0;
            }
            return Debugger.ExecuteOnDbgEngThread( () =>
                {
                    _BailIfMissingProcessContext();
                    using( new DbgEngContextSaver( Debugger, Target.Context ) )
                    {
                        string name;
                        CheckHr( _DS.GetModuleNameStringWide( which,
                                                              DEBUG_ANY_ID,
                                                              NativeParams.Base,
                                                              nameSizeHint,
                                                              out name ) );
                        return name;
                    }
                } );
        } // end _GetName()


        private string m_name;
        public string Name
        {
            get
            {
                if( null == m_name )
                {
                    m_name = _GetName( DEBUG_MODNAME.MODULE );
                }
                if( String.IsNullOrEmpty( m_name ) )
                {
                    m_name = Util.Sprintf( "<unknown_{0}>", DbgProvider.FormatAddress( BaseAddress, Debugger.TargetIs32Bit, false ) );
                }
                return m_name;
            }
        } // end property Name

        private ulong m_baseAddr;
        public ulong BaseAddress
        {
            get
            {
                if( 0 == m_baseAddr )
                {
                    m_baseAddr = NativeParams.Base;
                }
                return m_baseAddr;
            }
        } // end property BaseAddress

        private uint m_size;
        public uint Size
        {
            get
            {
                if( 0 == m_size )
                {
                    m_size = NativeParams.Size;
                }
                return m_size;
            }
        } // end property Size

        private string m_imgName;
        public string ImageName
        {
            get
            {
                if( null == m_imgName )
                {
                    m_imgName = _GetName( DEBUG_MODNAME.IMAGE );
                }
                return m_imgName;
            }
        } // end property ImageName

        private string m_loadedImgName;
        public string LoadedImageName
        {
            get
            {
                if( null == m_loadedImgName )
                {
                    m_loadedImgName = _GetName( DEBUG_MODNAME.LOADED_IMAGE );
                }
                return m_loadedImgName;
            }
        } // end property LoadedImageName

        private string m_mappedImgName;
        public string MappedImageName
        {
            get
            {
                if( null == m_mappedImgName )
                {
                    m_mappedImgName = _GetName( DEBUG_MODNAME.MAPPED_IMAGE );
                }
                return m_mappedImgName;
            }
        } // end property MappedImageName

        private string m_symbolFileName;
        public string SymbolFileName
        {
            get
            {
                if( null == m_symbolFileName )
                {
                    m_symbolFileName = _GetName( DEBUG_MODNAME.SYMBOL_FILE );
                }
                return m_symbolFileName;
            }
        } // end property SymbolFileName

        private uint m_checkSum;
        public uint CheckSum
        {
            get
            {
                if( 0 == m_checkSum )
                {
                    m_checkSum = NativeParams.Checksum;
                }
                return m_checkSum;
            }
        } // end property CheckSum

        public DEBUG_MODULE Flags { get { return NativeParams.Flags; } }

        public DEBUG_SYMTYPE SymbolType { get { return NativeParams.SymbolType; } }


        /// <summary>
        ///    Incremented whenever the symbol status changes.
        /// </summary>
        /// <remarks>
        ///    If you get symbol information (such as type information / type ids) when
        ///    the SymbolCookie is 3, then later you can check the SymbolCookie, and if
        ///    it isn't 3 anymore, you know you need to throw away your information.
        /// </remarks>
        public int SymbolCookie
        {
            get
            {
                // Anybody can new up a DbgModuleInfo object based on the module base
                // address, so rather than try to keep this bit of info on the
                // DbgModuleInfo and keep all of them up-to-date, we'll store it on the
                // target object.
                return Target.GetSymbolCookie( BaseAddress );
            }
        }


        // It's a UNIX timestamp (time_t): a 32-bit number representing seconds since
        // January 1, 1970 (UTC).
        private uint m_timeDateStamp;
        public uint TimeDateStampRaw
        {
            get
            {
                if( 0 == m_timeDateStamp )
                {
                    m_timeDateStamp = NativeParams.TimeDateStamp;
                }
                return m_timeDateStamp;
            }
        } // end property TimeDateStamp

        public DateTime TimeDateStamp
        {
            get
            {
                return Util.DateTimeFrom_time_t( TimeDateStampRaw );
            }
        }


        private void _BailIfMissingProcessContext()
        {
            if( null == Target )
            {
                throw new DbgProviderException( "Unfortunately we don't have enough information to get that information--we're missing the target context. One thing you can try complaining about to the debugger team is lack of context attached to events like \"module loaded\".",
                                                "DbgEngProblem_NoTargetContext",
                                                System.Management.Automation.ErrorCategory.NotEnabled,
                                                this );
            }
        }

        private DEBUG_MODULE_PARAMETERS? m_params;
        public DEBUG_MODULE_PARAMETERS NativeParams
        {
            get
            {
                if( null == m_params )
                {
                    Util.Assert( 0 != m_baseAddr );
                    _BailIfMissingProcessContext();
                    m_params = _GetModParamsByAddress( Debugger, Target.Context, _DS, m_baseAddr );
                }
                return (DEBUG_MODULE_PARAMETERS) m_params;
            }
        }

        public DbgTarget Target { get; private set; }

     // private static DbgTarget _GetCurrentTarget( DbgEngDebugger debugger )
     // {
     //     if( null == debugger )
     //         throw new ArgumentNullException( "debugger" );

     //     return debugger.GetCurrentTarget();
     // } // end _GetCurrentTarget()


        private ModuleVersionInfo m_mvi;

        /// <summary>
        ///    Represents version information. Should never be null, though sub-fields
        ///    (such as fixedFileInfo) could be.
        /// </summary>
        public ModuleVersionInfo VersionInfo
        {
            get
            {
                if( null == m_mvi )
                {
                    m_mvi = Debugger.CreateModuleVersionInfo( BaseAddress );
                }
                return m_mvi;
            }
        } // end property VersionInfo


        // Answers the question: which pointer in the thread's TLS block is for this DLL?
        //
        // N.B. This is NOT an index into the TlsSlots array!
        //
        // See the comments at DbgUModeThreadInfo.ImplicitTlsStorage for more info.
        private int m_implicitThreadLocalStorageIndex = -1;

        internal ulong GetImplicitThreadLocalStorageForThread( DbgUModeThreadInfo thread )
        {
            ulong tlsPointer = thread.ImplicitTlsStorage;

            if( 0 == tlsPointer )
                return 0;

            if( -1 == m_implicitThreadLocalStorageIndex )
            {
                // In the TEB is a block of pointers to per-DLL TLS storage. Which pointer
                // is the one for our DLL? To answer that, we look in the PE header.

                var ntHeaders = Debugger.ReadImageNtHeaders( BaseAddress );

                if( ntHeaders.OptionalHeader.NumberOfRvaAndSizes <= (uint) ImageDirectoryEntry.TLS /* 9 */ )
                {
                    return 0;
                }

                uint sizeOfTlsDir;
                ulong addrOfTlsDir;

                // TODO: The names of the data directories could be improved...
                addrOfTlsDir = BaseAddress + ntHeaders.OptionalHeader.DataDirectory9.VirtualAddress;
                sizeOfTlsDir = ntHeaders.OptionalHeader.DataDirectory9.Size;

                if( sizeOfTlsDir < (3 * Debugger.PointerSize) )
                {
                    LogManager.Trace( "Unexpected TLS directory size: {0:x}",
                                      ntHeaders.OptionalHeader.DataDirectory9.Size );

                    return 0;
                }
                else if( 0 == ntHeaders.OptionalHeader.DataDirectory9.VirtualAddress )
                {
                    LogManager.Trace( "Unexpected TLS VA (0)." );

                    return 0;
                }

                // The /address/ of the index is stored in the third pointer in the "TLS
                // directory" (at addrOfTlsDir) (from the PE/COFF spec).

                //  Offset   Size      Field       Description
                //  (PE32/  (PE32/
                //   PE32+)  PE32+)
                //
                //  0        4/8      Raw Data     The starting address of the TLS template. The template is a
                //                     Start VA    block of data that is used to initialize TLS data. The
                //                                 system copies all of this data each time a thread is
                //                                 created, so it must not be corrupted. Note that this address
                //                                 is not an RVA; it is an address for which there should be a
                //                                 base relocation in the .reloc section.
                //
                //  4/8      4/8      Raw Data     The address of the last byte of the TLS, except for the zero
                //                     End VA      fill. As with the Raw Data Start VA field, this is a VA, not
                //                                 an RVA.
                //
                //  8/16     4/8      Address of   The location to receive the TLS index, which the loader
                //                     Index       assigns. This location is in an ordinary data section, so it
                //                                 can be given a symbolic name that is accessible to the
                //                                 program.
                //

                ulong addrOfThisModsTlsIndex = Debugger.ReadMemSlotInPointerArray( addrOfTlsDir, 2 );

                // I don't know if the index itself is also pointer-sized... I'll just
                // read a DWORD's-worth.

                m_implicitThreadLocalStorageIndex = Debugger.ReadMemAs_Int32( addrOfThisModsTlsIndex );
            }

            return Debugger.ReadMemSlotInPointerArray( tlsPointer,
                                                       (uint) m_implicitThreadLocalStorageIndex );
        } // end GetImplicitThreadLocalStorageForThread()


        internal DbgModuleInfo( DbgEngDebugger debugger,
                                DEBUG_MODULE_PARAMETERS nativeParams,
                                DbgTarget target )
            : base( debugger )
        {
            if( null == target )
                throw new ArgumentNullException( "target" );

            Target = target;

            m_params = nativeParams;
            if( 0 == nativeParams.Base )
                throw new ArgumentException( "Invalid DEBUG_MODULE_PARAMETERS: no base address.", "nativeParams" );

            if( UInt64.MaxValue == nativeParams.Base ) // (DEBUG_INVALID_OFFSET)
                throw new ArgumentException( "Invalid DEBUG_MODULE_PARAMETERS: base address indicates the structure is empty. Guess: Don't pass a base address buffer to GetModuleParameters.", "nativeParams" );

            // TODO: more validation?
        } // end constructor


        private DEBUG_MODULE_PARAMETERS _GetModParamsByAddress( ulong addressInModule )
        {
            _BailIfMissingProcessContext();
            return _GetModParamsByAddress( Debugger, Target.Context, _DS, addressInModule );
        }

        private static DEBUG_MODULE_PARAMETERS _GetModParamsByAddress( DbgEngDebugger debugger,
                                                                       DbgEngContext ctx,
                                                                       WDebugSymbols ds,
                                                                       ulong addressInModule )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            if( 0 == addressInModule )
                throw new ArgumentException( "You must supply a non-zero address.", "addressInModule" );

            return debugger.ExecuteOnDbgEngThread( () =>
                {
                    using( new DbgEngContextSaver( debugger, ctx ) )
                    {
                        if( null == ds )
                            ds = (WDebugSymbols) debugger.DebuggerInterface;

                        uint index;
                        ulong baseAddr;

                        // TODO: this might not work for a managed module with public dbgeng...
                        // could we query clrmd or something? This is the actual point of
                        // failure, c.f. 8b09cd08-2fb1-4f51-902f-bdd6f967b105
                        StaticCheckHr( ds.GetModuleByOffset2( addressInModule,
                                                              0, // start index
                                                              DEBUG_GETMOD.DEFAULT,
                                                              out index,
                                                              out baseAddr ) );
                        DEBUG_MODULE_PARAMETERS[] modParams;
                        StaticCheckHr( ds.GetModuleParameters( 1, null, index, out modParams ) );
                        Util.Assert( 1 == modParams.Length );
                        return modParams[ 0 ];
                    }
                } );
        } // end _GetModParamsByAddress()


        internal DbgModuleInfo( DbgEngDebugger debugger,
                                ulong addressInModule,
                                DbgTarget target )
            : this( debugger,
                    _GetModParamsByAddress( debugger, target.Context, null, addressInModule ),
                    target )
        {
        }


        /// <summary>
        ///    This constructor is used by the modload event.
        /// </summary>
        internal DbgModuleInfo( DbgEngDebugger debugger,
                                WDebugSymbols ds,
                              //ulong imageFileHandle, <-- is there any [straightforward] way to get this besides the modload event? Is it useful?
                                ulong baseOffset,
                                uint moduleSize,
                                string moduleName,
                                string imageName,
                                uint checkSum,
                                uint timeDateStamp,
                                DbgTarget target )
            : base( debugger )
        {
            Util.Assert( 0 != baseOffset );
            m_ds = ds;
            m_baseAddr = baseOffset;
            m_size = moduleSize;
            m_name = moduleName;
            m_imgName = imageName;
            m_checkSum = checkSum;
            m_timeDateStamp = timeDateStamp;

            // We can't always get the current target context--like when we are trying to raise
            // a "module loaded" event, which happens from a dbgeng callback, where we don't know
            // the current context.
       //   if( null == target )
       //       throw new ArgumentNullException( "target" );

            Target = target;
        } // end constructor


        /// <summary>
        ///    Throw away cached state.
        /// </summary>
        public void Refresh()
        {
            // Save the base address if we haven't pulled it out yet. (Else we'll be
            // stranded with no way to re-load m_params.)
            if( 0 == m_baseAddr )
            {
                Util.Assert( null != m_params );
                m_baseAddr = m_params.Value.Base;
            }

            m_params = null;
            m_symbolFileName = null;
            // Could anything else change?
        } // end Refresh()


        public ColorString SymbolStatus
        {
            get { return _GetSymbolTypeString(); }
        }

        public bool IsUnloaded
        {
            get { return this.NativeParams.Flags.HasFlag( DEBUG_MODULE.UNLOADED ); }
        }

        #region IEquatable<DbgModuleInfo> Stuff

        public bool Equals( DbgModuleInfo other )
        {
            if( null == other )
                return false;

            // Is this sufficient? (are there weird scenarios with loaded versus unloaded modules or anything like that?)
            return (BaseAddress == other.BaseAddress) &&
                   (0 == Util.Strcmp_OI( Name, other.Name ));
        } // end Equals()

        public override bool Equals( object obj )
        {
            return Equals( obj as DbgModuleInfo );
        }

        public override int GetHashCode()
        {
            return BaseAddress.GetHashCode() + Name.GetHashCode();
        }

        public static bool operator ==( DbgModuleInfo m1, DbgModuleInfo m2 )
        {
            if( null == (object) m1 )
                return (null == (object) m2);

            return m1.Equals( m2 );
        }

        public static bool operator !=( DbgModuleInfo m1, DbgModuleInfo m2 )
        {
            return !(m1 == m2);
        }

        #endregion

        private static Dictionary< DEBUG_SYMTYPE, ColorString > sm_symStatusStrings = new Dictionary< DEBUG_SYMTYPE, ColorString >()
        {
            { DEBUG_SYMTYPE.CODEVIEW, new ColorString(                        "(codeview symbols)" )   .MakeReadOnly() },
            { DEBUG_SYMTYPE.COFF,     new ColorString(                        "(COFF symbols)" )       .MakeReadOnly() },
            { DEBUG_SYMTYPE.DEFERRED, new ColorString( ConsoleColor.DarkGray, "(deferred)" )           .MakeReadOnly() },
            { DEBUG_SYMTYPE.DIA,      new ColorString(                        "(DIA symbols)" )        .MakeReadOnly() },
            { DEBUG_SYMTYPE.EXPORT,   new ColorString( ConsoleColor.Yellow,   "(export symbols)" )     .MakeReadOnly() },
            // "no symbols" can happen when you're looking at a dump and you don't even
            // have the original image file to get exports from.
            { DEBUG_SYMTYPE.NONE,     new ColorString( ConsoleColor.Red,      "(NO SYMBOLS)" )         .MakeReadOnly() },
            { DEBUG_SYMTYPE.PDB,      new ColorString( ConsoleColor.Green,    "(private pdb symbols)" ).MakeReadOnly() },
            { DEBUG_SYMTYPE.SYM,      new ColorString(                        "('sym' symbols)" )      .MakeReadOnly() },
        };

        private ColorString _GetSymbolTypeString()
        {
            ColorString cs;
            if( !sm_symStatusStrings.TryGetValue( SymbolType, out cs ) )
            {
                throw new NotSupportedException( Util.Sprintf( "Unknown symbol type: {0}", SymbolType ) );
            }
            return cs;
        } // end _GetSymbolTypeString()

        public override string ToString()
        {
            return ToColorString().ToString();
        }

        public ColorString ToColorString()
        {
         // return DbgProvider.FormatAddress( BaseAddress,
         //                                   Debugger.TargetIs32Bit,
         //                                   true )
         //             .Append( " " )
         //             .Append( DbgProvider.FormatAddress( BaseAddress + Size,
         //                                                 Debugger.TargetIs32Bit,
         //                                                 true ) )
         //             .Append( " " )
         //             .AppendPushPopFgBg( ConsoleColor.Black,
         //                                 ConsoleColor.DarkYellow,
         //                                 Name )
         //             .Append( Name.Length < 20 ? new String( ' ', 20 - Name.Length ) : String.Empty )
         //             .Append( SymbolStatus );

            return DbgProvider.ColorizeModuleName( Name )
                        .Append( "  " )
                        .Append( DbgProvider.FormatAddress( BaseAddress, Debugger.TargetIs32Bit,
                                                            true ) )
                        .Append( " " )
                        .Append( DbgProvider.FormatAddress( BaseAddress + Size,
                                                            Debugger.TargetIs32Bit,
                                                            true ) );
        } // end ToColorString()

        #region IComparable<DbgModuleInfo> Members

        public int CompareTo( DbgModuleInfo other )
        {
            // TODO: Take Target into account?
            return BaseAddress.CompareTo( other.BaseAddress );
        }

        #endregion

        internal ModuleInfo ToClrMdModuleInfo( IDataReader dataReader )
        {
            var mi = new ModuleInfo( dataReader );
            // Is this the right name?
            //mi.FileName = Name;
            mi.FileName = LoadedImageName;
            if( String.IsNullOrEmpty( mi.FileName ) )
            {
                mi.FileName = this.MappedImageName;
                if( String.IsNullOrEmpty( mi.FileName ) )
                {
                    mi.FileName = Name;
                }
            }
            Util.Assert( !String.IsNullOrEmpty( mi.FileName ) );
            mi.FileSize = Size;
            mi.ImageBase = BaseAddress;
            // We can leave mi.Pdb null--ClrMd will pull info it needs directly from the
            // image, and only uses it for source file/line info.
            mi.TimeStamp = TimeDateStampRaw;

            if( null != VersionInfo.FixedFileInfo )
                mi.Version = VersionInfo.FixedFileInfo.ToClrMdVersionInfo();

            if( !String.IsNullOrEmpty( Environment.GetEnvironmentVariable( "_TEST_DEMO_PS_NULLREF" ) ) )
            {
                string s = null;
                Console.WriteLine( s.Length ); // <-- boom
            }

            return mi;
        } // end ToClrMdModuleInfo()
    } // end class DbgModuleInfo
}
