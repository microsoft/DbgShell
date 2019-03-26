//-----------------------------------------------------------------------
// <copyright file="DbgHelp.cs" company="Microsoft">
//    Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using DbgEngWrapper;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    using ModBaseTypeIdMap = Dictionary< ulong, /*TypeIdMap*/ Dictionary< uint, Dictionary< IMAGEHLP_SYMBOL_TYPE_INFO, object > > > ;
    using SIZE_T = IntPtr;
    using TypeIdMap = Dictionary< uint, /*TypeInfoDict*/ Dictionary< IMAGEHLP_SYMBOL_TYPE_INFO, object > >;
    using TypeInfoDict = Dictionary< IMAGEHLP_SYMBOL_TYPE_INFO, object >;

    [return: MarshalAs( UnmanagedType.Bool )]
    internal delegate bool native_SYM_ENUMERATESYMBOLS_CALLBACK( /*SYMBOL_INFO*/ IntPtr symInfo,
                                                                                 uint symbolSize,
                                                                                 IntPtr pUserContext );

    [Flags]
    public enum SymEnumOptions
    {
        Default = 0x01,
        Inline  = 0x02,   // includes inline symbols

        All = (Default | Inline)
    }

    [Flags]
    public enum SymSearchOptions
    {
        MaskObjs    = 0x01,   // used internally to implement other APIs
        Recurse     = 0x02,   // recurse scopes
        GlobalsOnly = 0x04,   // search only for global symbols
        AllItems    = 0x08,   // search for everything in the pdb, not just normal scoped symbols
    }


    internal enum ImageDirectoryEntry : ushort
    {
        EXPORT         =  0,  // Export Directory
        IMPORT         =  1,  // Import Directory
        RESOURCE       =  2,  // Resource Directory
        EXCEPTION      =  3,  // Exception Directory
        SECURITY       =  4,  // Security Directory
        BASERELOC      =  5,  // Base Relocation Table
        DEBUG          =  6,  // Debug Directory
     // COPYRIGHT      =  7,  // (X86 usage)
        ARCHITECTURE   =  7,  // Architecture Specific Data
        GLOBALPTR      =  8,  // RVA of GP
        TLS            =  9,  // TLS Directory
        LOAD_CONFIG    = 10,  // Load Configuration Directory
        BOUND_IMPORT   = 11,  // Bound Import Directory in headers
        IAT            = 12,  // Import Address Table
        DELAY_IMPORT   = 13,  // Delay Load Import Descriptors
        COM_DESCRIPTOR = 14   // COM Runtime descriptor
    } // end enum ImageDirectoryEntry


    internal static partial class NativeMethods
    {
        //
        // These methods (or rather, the underlying functions) are not threadsafe. But
        // that shouldn't be a problem, because we also have to always be sure to call
        // them on the dbgeng thread.
        //

        //
        // The [DefaultDllImportSearchPaths] attribute is needed for the hosted case (when
        // the CLR is being hosted by DbgShellExt, loaded into windbg). The problem was
        // that the CLR was deciding to load a second copy of dbghelp.dll, the one right
        // next to DbgProvider.dll, even though there was already one loaded (windbg's).
        // That attribute perturbs the CLR's PInvoke LoadLibrary behavior, and this
        // setting seems to work (it makes it not try to use a fully-qualified path to
        // dbghelp.dll).
        //
        // Things still work in the standalone case because dbghelp.dll happens to get
        // loaded by something else first in that case, too (DbgEngWrapper.dll).
        //

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static unsafe extern bool SymGetTypeInfo( IntPtr hProcess,
                                                           ulong modBase,
                                                           uint typeId,
                                                           IMAGEHLP_SYMBOL_TYPE_INFO getType,
                                                           void* pInfo );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
                    SetLastError = true,
                    CharSet = CharSet.Unicode,
                    EntryPoint = "SymEnumTypesByNameW" )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static extern bool SymEnumTypesByName( IntPtr hProcess,
                                                        ulong modBase,
                                                        string mask,
                                                        native_SYM_ENUMERATESYMBOLS_CALLBACK callback,
                                                        IntPtr pUserContext );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
                    SetLastError = true,
                    CharSet = CharSet.Unicode,
                    EntryPoint = "SymEnumSymbolsW" )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static extern bool SymEnumSymbols( IntPtr hProcess,
                                                    ulong modBase,
                                                    string mask,
                                                    native_SYM_ENUMERATESYMBOLS_CALLBACK callback,
                                                    IntPtr pUserContext );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
                    SetLastError = true,
                    CharSet = CharSet.Unicode,
                    EntryPoint = "SymEnumSymbolsExW" )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static extern bool SymEnumSymbolsEx( IntPtr hProcess,
                                                      ulong modBase,
                                                      string mask,
                                                      native_SYM_ENUMERATESYMBOLS_CALLBACK callback,
                                                      IntPtr pUserContext,
                                                      SymEnumOptions options );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
                    SetLastError = true,
                    CharSet = CharSet.Unicode,
                    EntryPoint = "SymEnumSymbolsForAddrW" )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static extern bool SymEnumSymbolsForAddr( IntPtr hProcess,
                                                           ulong address,
                                                           native_SYM_ENUMERATESYMBOLS_CALLBACK callback,
                                                           IntPtr pUserContext );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
                    SetLastError = true,
                    CharSet = CharSet.Unicode )]
        internal static extern ulong SymGetModuleBase64( IntPtr hProcess,
                                                         ulong address );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
                    SetLastError = true,
                    CharSet = CharSet.Unicode,
                    EntryPoint = "SymGetModuleInfoW64" )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static unsafe extern bool SymGetModuleInfo64( IntPtr hProcess,
                                                               ulong address,
                                                               IMAGEHLP_MODULEW64* pModInfo );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
                    SetLastError = true,
                    CharSet = CharSet.Unicode )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static extern bool SymGetTypeInfoEx( IntPtr hProcess,
                                                      ulong modBase,
                                                      IMAGEHLP_GET_TYPE_INFO_PARAMS @params );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
                    SetLastError = true,
                    CharSet = CharSet.Unicode )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static extern bool SymGetTypeFromName( IntPtr hProcess,
                                                        ulong modBase,
                                                        string typeName,
                                                        //[In] [Out] SymbolInfo symInfo );
                                                         /*SYMBOL_INFO**/ IntPtr pSymbolInfo );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
                    SetLastError = true,
                    CharSet = CharSet.Unicode,
                    EntryPoint = "SymFromIndexW" )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static unsafe extern bool SymFromIndex( IntPtr hProcess,
                                                         ulong modBase,
                                                         uint index,
                                                         /*SYMBOL_INFO**/ IntPtr pSymbolInfo );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
                    SetLastError = true,
                    CharSet = CharSet.Unicode,
                    EntryPoint = "SymFromTokenW" )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static unsafe extern bool SymFromToken( IntPtr hProcess,
                                                         ulong modBase,
                                                         uint token,
                                                         /*SYMBOL_INFO**/ IntPtr pSymbolInfo );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
                    SetLastError = true,
                    CharSet = CharSet.Unicode,
                    EntryPoint = "SymFromAddrW" )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static unsafe extern bool SymFromAddr( IntPtr hProcess,
                                                        ulong address,
                                                        out ulong displacement,
                                                        /*SYMBOL_INFO**/ IntPtr pSymbolInfo );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
                    SetLastError = true,
                    CharSet = CharSet.Unicode,
                    EntryPoint = "SymFromInlineContextW" )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static unsafe extern bool SymFromInlineContext( IntPtr hProcess,
                                                                 ulong address,
                                                                 uint inlineContext,
                                                                 out ulong displacement,
                                                                 /*SYMBOL_INFO**/ IntPtr pSymbolInfo );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
                    SetLastError = true,
                    CharSet = CharSet.Unicode,
                    EntryPoint = "SymSearchW" )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static extern bool SymSearch( IntPtr hProcess,
                                               ulong modBase,
                                               uint index,       // optional
                                               SymTag symtag,    // optional
                                               string mask,      // optional
                                               ulong address,    // optional
                                               native_SYM_ENUMERATESYMBOLS_CALLBACK callback,
                                               IntPtr pUserContext,
                                               SymSearchOptions options );

        [DefaultDllImportSearchPaths( DllImportSearchPath.LegacyBehavior )]
        [DllImport( "dbghelp.dll",
            SetLastError = true,
            CharSet = CharSet.Unicode,
            EntryPoint = "SymLoadModuleExW" )]
        internal static extern ulong SymLoadModuleEx( IntPtr hProcess,
                                                      IntPtr hFile,
                                                      string ImageName,
                                                      string ModuleName,
                                                      ulong BaseOfDll,
                                                      uint DllSize,
                                     /*MODLOAD_DATA*/ IntPtr Data,
                                                      uint Flags );
        
    } // end partial class NativeMethods


    /// <summary>
    ///    Represents an element of an enum.
    /// </summary>
    [DebuggerDisplay( "{Name} (0x{Value.ToString(\"x\")})" )]
    public class Enumerand : IEquatable< Enumerand >,
        // I don't think it's a good idea to have these other equatable overloads, because
        // it doesn't provide a "complete" story--"$myEnumerand -eq 0x08" will work, but
        // not "0x08 -eq $myEnumerand".
                          // IEquatable< ulong >,
                          // IEquatable< long >,
                          // IEquatable< uint >,
                          // IEquatable< int >,
                          // IEquatable< ushort >,
                          // IEquatable< short >,
                          // IEquatable< byte >,
                          // IEquatable< sbyte >,
                             IEquatable< System.Management.Automation.PSObject >
    {
        public readonly string Name;
        // An int should be suitable for 99% of enums, but there are 8-byte enums (for
        // instance, the WEBPLATFORM_VERSION enum, which I found in an iexplore.exe dump).
        public readonly ulong Value;
        public Enumerand( string name, ulong value )
        {
            if( String.IsNullOrEmpty( name ) )
                throw new ArgumentException( "You must supply a name.", "name" );

            Name = name;
            Value = value;
        } // end constructor

        #region IEquatable stuff

        public bool Equals( Enumerand other )
        {
            if( null == other )
                return false;

            return (Value == other.Value) &&
                   (0 == String.CompareOrdinal( Name, other.Name ));
        }

        public override bool Equals( object obj )
        {
            if( null == obj )
                return false;

            // It would be nice to do dynamic dispatch here, but since the signature of
            // this is "object", we'd get stack overflow if somebody passed some bogus
            // value like a string.

            if( obj is Enumerand )
                return Equals( obj as Enumerand );
     //     else if( obj is ulong )
     //         return Equals( (ulong) obj );
     //     else if( obj is long )
     //         return Equals( (long) obj );
     //     else if( obj is uint )
     //         return Equals( (uint) obj );
     //     else if( obj is int )
     //         return Equals( (int) obj );
     //     else if( obj is ushort )
     //         return Equals( (ushort) obj );
     //     else if( obj is short )
     //         return Equals( (short) obj );
     //     else if( obj is byte )
     //         return Equals( (byte) obj );
     //     else if( obj is sbyte )
     //         return Equals( (sbyte) obj );
            else if( obj is System.Management.Automation.PSObject )
                return Equals( (System.Management.Automation.PSObject) obj );

            return false;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode() ^ Value.GetHashCode();
        }

        #endregion IEquatable stuff

     // public bool Equals( ulong other )
     // {
     //     return other == Value;
     // }

     // public bool Equals( long other )
     // {
     //     return unchecked( Equals( (ulong) other ) );
     // }

     // //
     // // TODO: What to do about sign extension?
     // //

     // public bool Equals( int other )
     // {
     //     return unchecked( Equals( (ulong) other ) );
     // }

     // public bool Equals( uint other )
     // {
     //     return unchecked( Equals( (ulong) other ) );
     // }

     // public bool Equals( short other )
     // {
     //     return unchecked( Equals( (ulong) other ) );
     // }

     // public bool Equals( ushort other )
     // {
     //     return unchecked( Equals( (ulong) other ) );
     // }

     // public bool Equals( sbyte other )
     // {
     //     return unchecked( Equals( (ulong) other ) );
     // }

     // public bool Equals( byte other )
     // {
     //     return unchecked( Equals( (ulong) other ) );
     // }

        public bool Equals( System.Management.Automation.PSObject other )
        {
            return Equals( (dynamic) other.BaseObject );
        }
    } // end struct Enumerand


    internal static class DbgHelp
    {
        // DbgHelp.dll supports adding "synthetic symbols" (SymAddSymbol), but not synthetic types.
        // In order to allow certain sorts of casting (like reinterpreting a pointer as an array),
        // we will layer our own synthetic type layer on top.
        private const uint sm_syntheticTypeIdBase = 0x70000000;
        private const uint sm_syntheticTypeIdMax  = 0x7fffffff;
        private static int sm_lastSyntheticTypeId = (int) sm_syntheticTypeIdBase;

                  //  hProc               modBase            typeId            data
        //Dictionary< IntPtr, Dictionary< ulong, Dictionary< uint, Dictionary< IMAGEHLP_SYMBOL_TYPE_INFO, object > > > > sm_syntheticTypes
        static Dictionary< IntPtr, ModBaseTypeIdMap > sm_syntheticTypes = new Dictionary< IntPtr, ModBaseTypeIdMap >();


        // This seems a bit fragile and scary.
        // It's a reverse lookup: the type id key is actually the pointee type id.
        // TODO: I guess the end key could just be the type id; doesn't need to be the whole TypeInfoDict.
        static Dictionary< IntPtr, ModBaseTypeIdMap > sm_reverseSynthPointerTypes = new Dictionary< IntPtr, ModBaseTypeIdMap >();



        private static TypeInfoDict _TryGetSyntheticTypeInfoDict( Dictionary< IntPtr, ModBaseTypeIdMap > procMap,
                                                                  IntPtr hProcess,
                                                                  ulong modBase,
                                                                  uint typeId,
                                                                  out string elementNotFoundName,
                                                                  out string elementNotFoundValue )
        {
            elementNotFoundName = null;
            elementNotFoundValue = null;

            ModBaseTypeIdMap modBaseMap;
            if( !procMap.TryGetValue( hProcess, out modBaseMap ) )
            {
                elementNotFoundName = "hProcess";
                elementNotFoundValue = Util.FormatQWord( hProcess );
                return null;
            }

            TypeIdMap typeIdMap;
            if( !modBaseMap.TryGetValue( modBase, out typeIdMap ) )
            {
                elementNotFoundName = "modBase";
                elementNotFoundValue = Util.FormatQWord( modBase );
                return null;
            }

            TypeInfoDict typeInfoDict;
            if( !typeIdMap.TryGetValue( typeId, out typeInfoDict ) )
            {
                elementNotFoundName = "typeId";
                elementNotFoundValue = typeId.ToString();
                return null;
            }

            return typeInfoDict;
        } // end _TryGetSyntheticTypeInfoDict()


        private static TypeInfoDict _GetSyntheticTypeInfoDict( IntPtr hProcess,
                                                               ulong modBase,
                                                               uint typeId )
        {
            string elementNotFoundName, elementNotFoundValue;
            TypeInfoDict typeInfoDict = _TryGetSyntheticTypeInfoDict( sm_syntheticTypes,
                                                                      hProcess,
                                                                      modBase,
                                                                      typeId,
                                                                      out elementNotFoundName,
                                                                      out elementNotFoundValue );
            if( null == typeInfoDict )
            {
                throw new ArgumentException( Util.Sprintf( "Invalid {0}: {1}",
                                                           elementNotFoundName,
                                                           elementNotFoundValue ),
                                             elementNotFoundName );
            }

            return typeInfoDict;
        } // end _GetSyntheticTypeInfoDict()


        // Throws if any of the elements (hProcess, modBase, typeId) are not found;
        // returns false if the info type is not found for that typeId.
        private static bool _TryGetSyntheticTypeInfo( IntPtr hProcess,
                                                      ulong modBase,
                                                      uint typeId,
                                                      IMAGEHLP_SYMBOL_TYPE_INFO getType,
                                                      out object info )
        {
            info = null;
            TypeInfoDict typeInfoDict = _GetSyntheticTypeInfoDict( hProcess, modBase, typeId );

            return typeInfoDict.TryGetValue( getType, out info );
        } // end _TryGetSyntheticTypeInfo()


        private static object _GetSyntheticTypeInfo( TypeInfoDict typeInfoDict, IMAGEHLP_SYMBOL_TYPE_INFO ti )
        {
            object obj;
            typeInfoDict.TryGetValue( ti, out obj );
            // If the data isn't there, perhaps it is optional. We'll just return null.
            return obj;
        } // end _GetSyntheticTypeInfo()

        private static void _VerifySyntheticTypeTag( TypeInfoDict typeInfoDict,
                                                     SymTag expectedTag,
                                                     uint typeId )
        {
            object tagObj = _GetSyntheticTypeInfo( typeInfoDict, IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG );
            Util.Assert( null != tagObj, "It should not be possible to register any synthetic types without at least a symtag." );
            if( (null == tagObj) ||
                (expectedTag != (SymTag) tagObj) )
            {
                throw new ArgumentException( Util.Sprintf( "Wrong SYMTAG: typeId {0} is a {1}, not a {2}.",
                                                           typeId,
                                                           tagObj,
                                                           expectedTag ),
                                             "typeId" );
            }
        } // end _VerifySyntheticTypeTag()


        //
        // TODO TODO TODO
        // BUG BUGBUG
        //
        // We can add synthetic types... but we never clear them out.
        // We should clear them when their module is unloaded or when their target disappears.
        // I probably want to address the context callbacks before I deal with that.
        //

        private static void _AddSyntheticTypeInfo( Dictionary< IntPtr, ModBaseTypeIdMap > procMap,
                                                   IntPtr hProcess,
                                                   ulong modBase,
                                                   uint typeId,
                                                   TypeInfoDict typeInfoData )
        {
            ModBaseTypeIdMap modBaseMap;
            if( !procMap.TryGetValue( hProcess, out modBaseMap ) )
            {
                modBaseMap = new ModBaseTypeIdMap();
                procMap.Add( hProcess, modBaseMap );
            }

            TypeIdMap typeIdMap;
            if( !modBaseMap.TryGetValue( modBase, out typeIdMap ) )
            {
                typeIdMap = new TypeIdMap();
                modBaseMap.Add( modBase, typeIdMap );
            }

            typeIdMap.Add( typeId, typeInfoData );
        } // end _AddSyntheticTypeInfo()


        private static void _AddSyntheticTypeInfo( IntPtr hProcess,
                                                   ulong modBase,
                                                   uint typeId,
                                                   TypeInfoDict typeInfoData )
        {
            _AddSyntheticTypeInfo( sm_syntheticTypes, hProcess, modBase, typeId, typeInfoData );
        } // end _AddSyntheticTypeInfo()


        internal static bool PeekSyntheticTypeExists( WDebugClient debugClient,
                                                      ulong modBase,
                                                      uint typeId )
        {
            IntPtr hProcess = _GetHProcForDebugClient( debugClient );

            ModBaseTypeIdMap modBaseMap;
            if( !sm_syntheticTypes.TryGetValue( hProcess, out modBaseMap ) )
            {
                return false;
            }

            TypeIdMap typeIdMap;
            if( !modBaseMap.TryGetValue( modBase, out typeIdMap ) )
            {
                return false;
            }

            return typeIdMap.ContainsKey( typeId );
        } // end PeekSyntheticTypeExists()


        public static uint AddSyntheticArrayTypeInfo( WDebugClient debugClient,
                                                      ulong modBase,
                                                      uint arrayElementTypeId,
                                                      uint count,
                                                      ulong size )
        {
            uint newTypeId = (uint) Interlocked.Increment( ref sm_lastSyntheticTypeId );
            TypeInfoDict typeInfoDict = new TypeInfoDict()
            {
                { IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG, SymTag.ArrayType },
                { IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID, arrayElementTypeId },
                { IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_COUNT,  count },
                { IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LENGTH, size }
            };
            _AddSyntheticTypeInfo( _GetHProcForDebugClient( debugClient ),
                                   modBase,
                                   newTypeId,
                                   typeInfoDict );
            return newTypeId;
        } // end AddSyntheticArrayTypeInfo()


        /// <summary>
        ///    Adds a synthetic pointer type. Before calling this, you should either use
        ///    PeekSyntheticTypeExists (if you already have the typeId in hand, such as
        ///    for DbgEng-generated types) or TryGetSynthPointerTypeIdByPointeeTypeId to
        ///    see if the type already exists.
        /// </summary>
        public static uint AddSyntheticPointerTypeInfo( WDebugClient debugClient,
                                                        ulong modBase,
                                                        uint pointeeTypeId,
                                                        ulong size,
                                                        uint typeId ) // the typeId of the new type--0 if you don't have one.
        {
            uint newTypeId;
            if( 0 == typeId )
            {
                newTypeId = (uint) Interlocked.Increment( ref sm_lastSyntheticTypeId );
            }
            else
            {
                if( !DbgTypeInfo.IsDbgGeneratedType( typeId ) )
                {
                    throw new ArgumentException( Util.Sprintf( "I can't create a synthetic pointer type with an ID that doesn't look like a dbgeng-generated type (0x{0:x}).",
                                                               typeId ) );
                }
                newTypeId = typeId;
            }

            TypeInfoDict typeInfoDict = new TypeInfoDict()
            {
                // Note that we need to store the newTypeId in the TypeInfoDict because we
                // want to do a reverse lookup via pointeeTypeId.
                // TODO: Although maybe the reverse lookup should just be uint -> uint?
                { IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG, SymTag.PointerType },
                { IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMINDEX, newTypeId },
                { IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID, pointeeTypeId },
                { IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LENGTH, size },
                { IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_IS_REFERENCE, false }
            };

            var hProc = _GetHProcForDebugClient( debugClient );
            _AddSyntheticTypeInfo( hProc,
                                   modBase,
                                   newTypeId,
                                   typeInfoDict );

            _AddSyntheticTypeInfo( sm_reverseSynthPointerTypes,
                                   hProc,
                                   modBase,
                                   pointeeTypeId, // <-- the pointee type id is the key
                                   typeInfoDict );
            return newTypeId;
        } // end AddSyntheticPointerTypeInfo()


        internal static uint TryGetSynthPointerTypeIdByPointeeTypeId( WDebugClient debugClient,
                                                                      ulong modBase,
                                                                      uint pointeeTypeId )
        {
            var hProc = _GetHProcForDebugClient( debugClient );

            string dontCare1, dontCare2;
            TypeInfoDict tid = _TryGetSyntheticTypeInfoDict( sm_reverseSynthPointerTypes,
                                                             hProc,
                                                             modBase,
                                                             pointeeTypeId,
                                                             out dontCare1,
                                                             out dontCare2 );
            if( null == tid )
                return 0;

            return (uint) tid[ IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMINDEX ];
        } // end TryGetSynthPointerTypeIdByPointeeTypeId


        private static IntPtr _GetHProcForDebugClient( WDebugClient debugClient )
        {
            if( null == debugClient )
                throw new ArgumentNullException( "debugClient" );

            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                {
                    ulong hProc;
                    WDebugSystemObjects dso = (WDebugSystemObjects) debugClient;
                    int hr = dso.GetCurrentProcessHandle( out hProc );
                    if( 0 != hr )
                        throw new DbgEngException( hr );

                    return new IntPtr( (long) hProc );
                } );
        } // end _GetHProcForDebugClient()


        private static bool _IsSyntheticTypeId( uint typeId )
        {
            return ((typeId > sm_syntheticTypeIdBase) &&
                    (typeId < sm_syntheticTypeIdMax)) ||
                   DbgTypeInfo.IsDbgGeneratedType( typeId );
        }


        /// <summary>
        ///    Clears any synthetic type information for the specified module. If modBase
        ///    is 0, clears it for the whole debuggee (represented by debugClient).
        /// </summary>
        /// <remarks>
        ///    We need to dump synthetic type information when symbols get reloaded,
        ///    because although we control / "own" the synthetic type ids, they end up
        ///    referring to real type ids, which become invalid.
        /// </remarks>
        internal static void DumpSyntheticTypeInfoForModule( //WDebugClient debugClient,
                                                             ulong modBase )
        {
            //
            // This gets called during an event callback, so we must not call into dbgeng.
            //
            // That's gives us a sad; we'll have to dump synthetic type info for all
            // processes.
            //
            //var hProc = _GetHProcForDebugClient( debugClient );

            _DumpSyntheticTypeInfoForModuleWorker( sm_syntheticTypes, /*hProc,*/ modBase );
            _DumpSyntheticTypeInfoForModuleWorker( sm_reverseSynthPointerTypes, /*hProc,*/ modBase );
        } // end DumpSyntheticTypeInfoForModule()


        private static void _DumpSyntheticTypeInfoForModuleWorker( Dictionary< IntPtr, ModBaseTypeIdMap > procMap,
                                                                   //IntPtr hProcess,
                                                                   ulong modBase )
        {
            // We don't know what hProcess this is actually for, so we'll just have to do
            // this for all of them.
            foreach( var hProcess in procMap.Keys )
            {
                ModBaseTypeIdMap modBaseMap;
                if( !procMap.TryGetValue( hProcess, out modBaseMap ) )
                {
                    //return;
                    continue;
                }

                if( 0 == modBase )
                {
                    modBaseMap.Clear();
                }
                else
                {
                    modBaseMap.Remove( modBase );
                }
            }
        }


        private static unsafe void _SymGetTypeInfo( IntPtr hProcess,
                                                    ulong modBase,
                                                    uint typeId,
                                                    IMAGEHLP_SYMBOL_TYPE_INFO getType,
                                                    void* pInfo )
        {
            DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                _SymGetTypeInfo_naked( hProcess,
                                       modBase,
                                       typeId,
                                       getType,
                                       pInfo ) );
        }

        private static unsafe void _SymGetTypeInfo_naked( IntPtr hProcess,
                                                          ulong modBase,
                                                          uint typeId,
                                                          IMAGEHLP_SYMBOL_TYPE_INFO getType,
                                                          void* pInfo )
        {
            if( _IsSyntheticTypeId( typeId ) )
            {
                object obj;
                if( _TryGetSyntheticTypeInfo( hProcess, modBase, typeId, getType, out obj ) )
                {
                    string str = obj as string;

                    if( null != str )
                    {
                        // Need to give the caller something to free.
                        obj = Marshal.StringToHGlobalUni( str );
                    }
                    // Crud, I wish there was a more generic way to do this. I can't use
                    // Marshal.StructureToPtr: "System.ArgumentException: The specified
                    // structure must be blittable or have layout information."
                    if( obj is IntPtr )
                    {
                        IntPtr* _pInfo = (IntPtr*) pInfo;
                        *_pInfo = (IntPtr) obj;
                    }
                    else if( (obj is uint) || (obj is SymTag) )
                    {
                        uint* _pInfo = (uint*) pInfo;
                        *_pInfo = (uint) obj;
                    }
                    else if( obj is ulong )
                    {
                        ulong* _pInfo = (ulong*) pInfo;
                        *_pInfo = (ulong) obj;
                    }
                    else
                    {
                        throw new NotImplementedException( Util.Sprintf( "Need to implement memcpy for type {0}.",
                                                                         Util.GetGenericTypeName( obj ) ) );
                    }

                    return;
                }

               // N.B. Keep this error behavior consistent with how we fail when the real
               // API fails (when it returns S_FALSE).
               throw new DbgEngException( (int) DebuggerObject.S_FALSE );
            }

            bool itWorked = NativeMethods.SymGetTypeInfo( hProcess,
                                                          modBase,
                                                          typeId,
                                                          getType,
                                                          pInfo );
            if( !itWorked )
                // TODO: Should I have a separate DbgHelpException class?
                throw new DbgEngException( Marshal.GetLastWin32Error() );
        } // end _SymGetTypeInfo_naked()


        // It would be nice, in a way, to have a _SymGetTypeInfoEx wrapper similar to our
        // _SymGetTypeInfo wrapper, but besides being a lot of work, it would also force us
        // to go through all our complex re-implementation of SymGetTypeInfoEx so that we
        // could check each typeId to see if it was fake. This seems too much. So, for now
        // at least, I'm just going to require that "if( _IsSyntheticTypeId( typeId ) )"
        // checks be sprinkled about all over the place.
     // private static unsafe void _SymGetTypeInfoEx( IntPtr hProcess,
     //                                               ulong modBase,
     //                                               IMAGEHLP_GET_TYPE_INFO_PARAMS tiParams )
     // {
     //     if( null == tiParams.Buffer )
     //     {
     //         // In this mode of operation, caller is only asking about one typeId,
     //         // and the offsets are the actual addresses of the desired info.
     //         if( 1 != tiParams.NumIds )
     //         {
     //             throw new ArgumentException( Util.Sprintf( "If Buffer is null, you can only ask about one typeId, not {0}.",
     //                                                        tiParams.NumIds ),
     //                                          "tiParams.NumIds" );
     //         }
     //         if( IntPtr.Zero != tiParams.BufferSize )
     //         {
     //             throw new ArgumentException( Util.Sprintf( "You said the BufferSize is {0}, but the Buffer is null.",
     //                                                        tiParams.BufferSize ),
     //                                          "tiParams.BufferSize" );
     //         }
     //         uint typeId = tiParams.TypeIds[ 0 ];
     //     }
     //     else
     //     {
     //     }
     // } // end _SymGetTypeInfoEx()


        // Returns a uint[] where the "children" ids are starting at index 2 (the first two
        // elements are the elements of the TI_FINDCHILDREN_PARAMS structure). This "raw"
        // version allows us to avoid copying the array in cases where we need to do further
        // processing on the children anyway.
        private static unsafe uint[] _FindTypeChildrenRaw( IntPtr hProcess,
                                                           ulong modBase,
                                                           uint typeId )
        {
            uint numChildren = 0;

            _SymGetTypeInfo( hProcess,
                             modBase,
                             typeId,
                             IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CHILDRENCOUNT,
                             &numChildren );

            return _FindTypeChildrenRaw( hProcess, modBase, typeId, numChildren );
        }

        private static unsafe uint[] _FindTypeChildrenRaw( IntPtr hProcess,
                                                           ulong modBase,
                                                           uint typeId,
                                                           uint numChildren )
        {
            uint[] buf = new uint[ 2 + numChildren ]; // TI_FINDCHILDREN_PARAMS structure
            buf[ 0 ] = numChildren; // the "count" field of TI_FINDCHILDREN_PARAMS

            fixed( uint* pBuf = buf )
            {
                _SymGetTypeInfo( hProcess,
                                 modBase,
                                 typeId,
                                 IMAGEHLP_SYMBOL_TYPE_INFO.TI_FINDCHILDREN,
                                 pBuf );
            }

            return buf;
        } // end _FindTypeChildrenRaw()


        public static unsafe uint[] FindTypeChildren( WDebugClient debugClient,
                                                      ulong modBase,
                                                      uint typeId )
        {
            return FindTypeChildren( _GetHProcForDebugClient( debugClient ), modBase, typeId );
        }

        public static unsafe uint[] FindTypeChildren( WDebugClient debugClient,
                                                      ulong modBase,
                                                      uint typeId,
                                                      uint numChildren )
        {
            return FindTypeChildren( _GetHProcForDebugClient( debugClient ), modBase, typeId, numChildren );
        }

        public static unsafe uint[] FindTypeChildren( IntPtr hProcess,
                                                      ulong modBase,
                                                      uint typeId )
        {
            var buf = _FindTypeChildrenRaw( hProcess, modBase, typeId );
            uint[] children = new uint[ buf.Length - 2 ];
            Array.Copy( buf, 2, children, 0, buf.Length - 2 );
            return children;
        } // end FindTypeChildren()


        public static unsafe uint[] FindTypeChildren( IntPtr hProcess,
                                                      ulong modBase,
                                                      uint typeId,
                                                      uint numChildren )
        {
            var buf = _FindTypeChildrenRaw( hProcess, modBase, typeId, numChildren );
            uint[] children = new uint[ buf.Length - 2 ];
            Array.Copy( buf, 2, children, 0, buf.Length - 2 );
            return children;
        } // end FindTypeChildren()


        public static unsafe void GetBaseTypeInfo( WDebugClient debugClient,
                                                   ulong modBase,
                                                   uint typeId,
                                                   out BasicType baseType,
                                                   out ulong size )
        {
            BasicType tmpBaseType = 0;
            ulong tmpSize = 0;
            DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                {
                    GetBaseTypeInfo_naked( debugClient,
                                           modBase,
                                           typeId,
                                           out tmpBaseType,
                                           out tmpSize );
                } );
            baseType = tmpBaseType;
            size = tmpSize;
        }


        private static unsafe void GetBaseTypeInfo_naked( WDebugClient debugClient,
                                                          ulong modBase,
                                                          uint typeId,
                                                          out BasicType baseType,
                                                          out ulong size )
        {
            SymTag tag = 0;
            baseType = 0;
            size = 0;

            if( _IsSyntheticTypeId( typeId ) )
            {
                throw new ArgumentException( Util.Sprintf( "There is currently no way to register a synthetic base type, so this can't be right. (typeId {0})",
                                                           typeId ),
                                             "typeId" );
            }

            IntPtr hProcess = _GetHProcForDebugClient( debugClient );

            IMAGEHLP_SYMBOL_TYPE_INFO[] reqKinds = new IMAGEHLP_SYMBOL_TYPE_INFO[]
            {
                IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,
                IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_BASETYPE,
                IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LENGTH,
            };

            BasicType _baseType = 0;
            ulong _size = 0;

            // Since we're only getting info about one id, we use the alternate mode of
            // operation where the offsets are the actual addresses we want the info
            // stored at.
            IntPtr[] reqOffsets = new SIZE_T[]
            {
                (IntPtr) (&tag),
                (IntPtr) (&_baseType),
                (IntPtr) (&_size),
            };

            uint[] reqSizes = new uint[]
            {
                (uint) 4,  // Tag
                (uint) 4,  // BaseType
                (uint) 8,  // Length
            };

            var gtip = new IMAGEHLP_GET_TYPE_INFO_PARAMS();

            ulong reqsValid = 0;

            fixed( uint* pReqSizes = reqSizes )
            fixed( SIZE_T* pReqOffsets = reqOffsets )
            fixed( IMAGEHLP_SYMBOL_TYPE_INFO* pReqKinds = reqKinds )
            {
                gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.UNCACHED;
                gtip.NumIds = 1;
                gtip.TypeIds = &typeId;
                gtip.TagFilter = (ulong) (1L << (int) SymTag.SymTagMax) - 1;
                gtip.NumReqs = (uint) reqKinds.Length;
                gtip.ReqKinds = pReqKinds;
                gtip.ReqOffsets = pReqOffsets;
                gtip.ReqSizes = pReqSizes;
                gtip.ReqStride = IntPtr.Zero;
                gtip.BufferSize = new IntPtr( -1 );
                gtip.Buffer = null;
                gtip.NumReqsValid = 1;
                gtip.ReqsValid = &reqsValid;

                bool itWorked = NativeMethods.SymGetTypeInfoEx( hProcess,
                                                                modBase,
                                                                gtip );
                if( !itWorked )
                    throw new DbgEngException( Marshal.GetLastWin32Error() );

                // The bits in reqsValid tell us which requests were valid. We should always be able
                // to get at least the tag, if it's a valid type ID at all. (if it's not,
                // SymGetTypeInfoEx should have failed)
                Util.Assert( 0 != (reqsValid & 0x01) );
                if( SymTag.BaseType != tag )
                    throw new ArgumentException( Util.Sprintf( "TypeId {0} is not a BaseType (it's a {1}).",
                                                               typeId,
                                                               tag ) );

                Util.Assert( reqsValid == 7 ); // we should always be able to get these items for a BaseType
                baseType = _baseType;
                size = _size;
            } // end fixed()
        } // end GetBaseTypeInfo_naked()


        private static readonly IMAGEHLP_SYMBOL_TYPE_INFO[] sm_pointerTypeReqKinds = new IMAGEHLP_SYMBOL_TYPE_INFO[]
        {                                                    // ReqsValid bits:
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,         // 0x01
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID,         // 0x02
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LENGTH,         // 0x04
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_IS_REFERENCE,   // 0x08
        };

        private static readonly uint[] sm_pointerTypeReqSizes = new uint[]
        {
            (uint) 4,  // Tag
            (uint) 4,  // TypeId
            (uint) 8,  // Length
            (uint) 4,  // IsReference
        };

        public static unsafe void GetPointerTypeInfo( WDebugClient debugClient,
                                                      ulong modBase,
                                                      uint typeId,
                                                      out uint pointeeTypeId,
                                                      out ulong size,
                                                      out bool isReference )
        {
            uint tmpPointeeTypeId = 0;
            ulong tmpSize = 0;
            bool tmpIsReference = false;

            DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                GetPointerTypeInfo_naked( debugClient,
                                          modBase,
                                          typeId,
                                          out tmpPointeeTypeId,
                                          out tmpSize,
                                          out tmpIsReference ) );

            pointeeTypeId = tmpPointeeTypeId;
            size = tmpSize;
            isReference = tmpIsReference;
        }


        private static unsafe void GetPointerTypeInfo_naked( WDebugClient debugClient,
                                                             ulong modBase,
                                                             uint typeId,
                                                             out uint pointeeTypeId,
                                                             out ulong size,
                                                             out bool isReference )
        {
            pointeeTypeId = 0;
            size = 0;
            isReference = false;

            IntPtr hProcess = _GetHProcForDebugClient( debugClient );

            if( _IsSyntheticTypeId( typeId ) )
            {
                var typeInfoDict = _GetSyntheticTypeInfoDict( hProcess, modBase, typeId );
                _VerifySyntheticTypeTag( typeInfoDict, SymTag.PointerType, typeId );
                pointeeTypeId = (uint) typeInfoDict[ IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID ];
                size = (ulong) typeInfoDict[ IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LENGTH ];
                isReference = (bool) typeInfoDict[ IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_IS_REFERENCE ];
                return;
            }

            SymTag tag = 0;
            uint _pointeeTypeId = 0;
            ulong _size = 0;
            uint dwIsReference = 0;

            // Since we're only getting info about one id, we use the alternate mode of
            // operation where the offsets are the actual addresses we want the info
            // stored at.
            IntPtr[] reqOffsets = new SIZE_T[]
            {
                (IntPtr) (&tag),
                (IntPtr) (&_pointeeTypeId),
                (IntPtr) (&_size),
                (IntPtr) (&dwIsReference),
            };

            var gtip = new IMAGEHLP_GET_TYPE_INFO_PARAMS();

            ulong reqsValid = 0;

            fixed( uint* pReqSizes = sm_pointerTypeReqSizes )
            fixed( SIZE_T* pReqOffsets = reqOffsets )
            fixed( IMAGEHLP_SYMBOL_TYPE_INFO* pReqKinds = sm_pointerTypeReqKinds )
            {
                gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.UNCACHED;
                gtip.NumIds = 1;
                gtip.TypeIds = &typeId;
                gtip.TagFilter = (ulong) (1L << (int) SymTag.SymTagMax) - 1;
                gtip.NumReqs = (uint) sm_pointerTypeReqKinds.Length;
                gtip.ReqKinds = pReqKinds;
                gtip.ReqOffsets = pReqOffsets;
                gtip.ReqSizes = pReqSizes;
                gtip.ReqStride = IntPtr.Zero;
                gtip.BufferSize = new IntPtr( -1 );
                gtip.Buffer = null;
                gtip.NumReqsValid = 1;
                gtip.ReqsValid = &reqsValid;

                bool itWorked = NativeMethods.SymGetTypeInfoEx( hProcess,
                                                                modBase,
                                                                gtip );
                if( !itWorked )
                    throw new DbgEngException( Marshal.GetLastWin32Error() );

                // The bits in reqsValid tell us which requests were valid. We should always be able
                // to get at least the tag, if it's a valid type ID at all. (if it's not,
                // SymGetTypeInfoEx should have failed)
                Util.Assert( 0 != (reqsValid & 0x01) );
                if( SymTag.PointerType != tag )
                    throw new ArgumentException( Util.Sprintf( "TypeId {0} is not a PointerType (it's a {1}).",
                                                               typeId,
                                                               tag ) );

                Util.Assert( reqsValid == 0xf ); // we should always be able to get these items for a PointerType
                pointeeTypeId = _pointeeTypeId;
                size = _size;
                isReference = 0 != dwIsReference;
            } // end fixed()
        } // end GetPointerTypeInfo_naked()


        private static readonly IMAGEHLP_SYMBOL_TYPE_INFO[] sm_typedefTypeReqKinds = new IMAGEHLP_SYMBOL_TYPE_INFO[]
        {                                                    // ReqsValid bits:
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,         // 0x001
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID,         // 0x002
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMNAME,        // 0x004
        };

        private static readonly uint[] sm_typedefTypeReqSizes = new uint[]
        {
            (uint) 4,           // Tag
            (uint) 4,           // TypeId
            (uint) IntPtr.Size, // SymName
        };

        public static unsafe void GetTypedefTypeInfo( WDebugClient debugClient,
                                                      ulong modBase,
                                                      uint typeId,
                                                      out uint representedTypeId,
                                                      out string typedefName )
        {
            uint tmpRepresentedTypeId = 0;
            string tmpTypedefName = null;

            DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                GetTypedefTypeInfo_naked( debugClient,
                                          modBase,
                                          typeId,
                                          out tmpRepresentedTypeId,
                                          out tmpTypedefName ) );

            representedTypeId = tmpRepresentedTypeId;
            typedefName = tmpTypedefName;
        }

        private static unsafe void GetTypedefTypeInfo_naked( WDebugClient debugClient,
                                                             ulong modBase,
                                                             uint typeId,
                                                             out uint representedTypeId,
                                                             out string typedefName )
        {
            representedTypeId = 0;
            typedefName = null;

            if( _IsSyntheticTypeId( typeId ) )
            {
                throw new ArgumentException( Util.Sprintf( "There is currently no way to register a synthetic typedef type, so this can't be right. (typeId {0})",
                                                           typeId ),
                                             "typeId" );
            }

            IntPtr hProcess = _GetHProcForDebugClient( debugClient );

            SymTag tag = 0;
            uint _representedTypeId = 0;
            IntPtr pSymName = IntPtr.Zero;

            // Since we're only getting info about one id, we use the alternate mode of
            // operation where the offsets are the actual addresses we want the info
            // stored at.
            IntPtr[] reqOffsets = new SIZE_T[]
            {
                (IntPtr) (&tag),
                (IntPtr) (&_representedTypeId),
                (IntPtr) (&pSymName),
            };

            var gtip = new IMAGEHLP_GET_TYPE_INFO_PARAMS();

            ulong reqsValid = 0;

            fixed( uint* pReqSizes = sm_typedefTypeReqSizes )
            fixed( SIZE_T* pReqOffsets = reqOffsets )
            fixed( IMAGEHLP_SYMBOL_TYPE_INFO* pReqKinds = sm_typedefTypeReqKinds )
            {
                gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.UNCACHED;
                gtip.NumIds = 1;
                gtip.TypeIds = &typeId;
                gtip.TagFilter = (ulong) (1L << (int) SymTag.SymTagMax) - 1;
                gtip.NumReqs = (uint) sm_typedefTypeReqKinds.Length;
                gtip.ReqKinds = pReqKinds;
                gtip.ReqOffsets = pReqOffsets;
                gtip.ReqSizes = pReqSizes;
                gtip.ReqStride = IntPtr.Zero;
                gtip.BufferSize = new IntPtr( -1 );
                gtip.Buffer = null;
                gtip.NumReqsValid = 1;
                gtip.ReqsValid = &reqsValid;

                try
                {
                    bool itWorked = NativeMethods.SymGetTypeInfoEx( hProcess,
                                                                    modBase,
                                                                    gtip );
                    if( !itWorked )
                        throw new DbgEngException( Marshal.GetLastWin32Error() );

                    // The bits in reqsValid tell us which requests were valid. We should always be able
                    // to get at least the tag, if it's a valid type ID at all. (if it's not,
                    // SymGetTypeInfoEx should have failed)
                    Util.Assert( 0 != (reqsValid & 0x01) );
                    if( SymTag.Typedef != tag )
                        throw new ArgumentException( Util.Sprintf( "TypeId {0} is not a Typedef (it's a {1}).",
                                                                   typeId,
                                                                   tag ) );

                    Util.Assert( reqsValid == 0x7 ); // we should always be able to get these items for a Typedef
                    representedTypeId = _representedTypeId;
                    typedefName = Marshal.PtrToStringUni( pSymName );
                }
                finally
                {
                    if( IntPtr.Zero != pSymName )
                        Marshal.FreeHGlobal( pSymName );
                }
            } // end fixed()
        } // end GetTypedefTypeInfo_naked()


        private static readonly IMAGEHLP_SYMBOL_TYPE_INFO[] sm_arrayTypeReqKinds = new IMAGEHLP_SYMBOL_TYPE_INFO[]
        {                                                    // ReqsValid bits:
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,         // 0x01
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID,         // 0x02
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_COUNT,          // 0x04
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LENGTH,         // 0x08
            // TODO: is IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_ARRAYINDEXTYPEID of any use?
        };

        private static readonly uint[] sm_arrayTypeReqSizes = new uint[]
        {
            (uint) 4,  // Tag
            (uint) 4,  // TypeId
            (uint) 4,  // Count
            (uint) 8,  // Length
        };

        public static unsafe void GetArrayTypeInfo( WDebugClient debugClient,
                                                    ulong modBase,
                                                    uint typeId,
                                                    out uint arrayTypeId,
                                                    out uint count,
                                                    out ulong size )
        {
            uint tmpArrayTypeId = 0;
            uint tmpCount = 0;
            ulong tmpSize = 0;

            DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                GetArrayTypeInfo_naked( debugClient,
                                        modBase,
                                        typeId,
                                        out tmpArrayTypeId,
                                        out tmpCount,
                                        out tmpSize ) );

            arrayTypeId = tmpArrayTypeId;
            count = tmpCount;
            size = tmpSize;
        }

        private static unsafe void GetArrayTypeInfo_naked( WDebugClient debugClient,
                                                           ulong modBase,
                                                           uint typeId,
                                                           out uint arrayTypeId,
                                                           out uint count,
                                                           out ulong size )
        {
            SymTag tag = 0;
            arrayTypeId = 0;
            count = 0;
            size = 0;

            IntPtr hProcess = _GetHProcForDebugClient( debugClient );

            if( _IsSyntheticTypeId( typeId ) )
            {
                var typeInfoDict = _GetSyntheticTypeInfoDict( hProcess, modBase, typeId );
                _VerifySyntheticTypeTag( typeInfoDict, SymTag.ArrayType, typeId );
                arrayTypeId = (uint) typeInfoDict[ IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID ];
                count = (uint) typeInfoDict[ IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_COUNT ];
                size = (ulong) typeInfoDict[ IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LENGTH ];
                return;
            }

            uint _arrayTypeId = 0;
            uint _count = 0;
            ulong _size = 0;

            // Since we're only getting info about one id, we use the alternate mode of
            // operation where the offsets are the actual addresses we want the info
            // stored at.
            IntPtr[] reqOffsets = new SIZE_T[]
            {
                (IntPtr) (&tag),
                (IntPtr) (&_arrayTypeId),
                (IntPtr) (&_count),
                (IntPtr) (&_size),
            };

            var gtip = new IMAGEHLP_GET_TYPE_INFO_PARAMS();

            ulong reqsValid = 0;

            fixed( uint* pReqSizes = sm_arrayTypeReqSizes )
            fixed( SIZE_T* pReqOffsets = reqOffsets )
            fixed( IMAGEHLP_SYMBOL_TYPE_INFO* pReqKinds = sm_arrayTypeReqKinds )
            {
                gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.UNCACHED;
                gtip.NumIds = 1;
                gtip.TypeIds = &typeId;
                gtip.TagFilter = (ulong) (1L << (int) SymTag.SymTagMax) - 1;
                gtip.NumReqs = (uint) sm_arrayTypeReqKinds.Length;
                gtip.ReqKinds = pReqKinds;
                gtip.ReqOffsets = pReqOffsets;
                gtip.ReqSizes = pReqSizes;
                gtip.ReqStride = IntPtr.Zero;
                gtip.BufferSize = new IntPtr( -1 );
                gtip.Buffer = null;
                gtip.NumReqsValid = 1;
                gtip.ReqsValid = &reqsValid;

                bool itWorked = NativeMethods.SymGetTypeInfoEx( hProcess,
                                                                modBase,
                                                                gtip );
                if( !itWorked )
                    throw new DbgEngException( Marshal.GetLastWin32Error() );

                // The bits in reqsValid tell us which requests were valid. We should always be able
                // to get at least the tag, if it's a valid type ID at all. (if it's not,
                // SymGetTypeInfoEx should have failed)
                Util.Assert( 0 != (reqsValid & 0x01) );
                if( SymTag.ArrayType != tag )
                    throw new ArgumentException( Util.Sprintf( "TypeId {0} is not an ArrayType (it's a {1}).",
                                                               typeId,
                                                               tag ) );

                Util.Assert( reqsValid == 0xf ); // we should always be able to get these items for an ArrayType
                arrayTypeId = _arrayTypeId;
                count = _count;
                size = _size;
            } // end fixed()
        } // end GetArrayTypeInfo_naked()


        private static readonly IMAGEHLP_SYMBOL_TYPE_INFO[] sm_udtInfoReqKinds = new IMAGEHLP_SYMBOL_TYPE_INFO[]
        {                                                          // ReqsValid bits
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,               // 0x01
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_UDTKIND,              // 0x02
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LENGTH,               // 0x04
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CHILDRENCOUNT,        // 0x08
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CLASSPARENTID,        // 0x10
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VIRTUALTABLESHAPEID,  // 0x20
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMNAME,              // 0x40
        };

        [StructLayout( LayoutKind.Sequential )]
        internal struct UdtInfoRequest
        {
            public SymTag SymTag;
            public UdtKind UdtKind;
            public ulong Size;
            public uint ChildrenCount;
            public uint ClassParentId;
            public uint VirtualTableShapeId;
            public IntPtr SymName;
        }


        private static readonly uint[] sm_udtInfoReqSizes = new uint[]
        {
            (uint) 4,  // Tag
            (uint) 4,  // UdtKind
            (uint) 8,  // Length
            (uint) 4,  // ChildrenCount
            (uint) 4,  // ClassParentId
            (uint) 4,  // VirtualTableShapeId
            (uint) IntPtr.Size, // SymName
        };


        public static unsafe RawUdtInfo GetUdtInfo( WDebugClient debugClient,
                                                    ulong modBase,
                                                    uint typeId )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                GetUdtInfo_naked( debugClient,
                                  modBase,
                                  typeId ) );
        }

        private static unsafe RawUdtInfo GetUdtInfo_naked( WDebugClient debugClient,
                                                           ulong modBase,
                                                           uint typeId )
        {
            if( _IsSyntheticTypeId( typeId ) )
            {
                throw new ArgumentException( Util.Sprintf( "There is currently no way to register a synthetic UDT type, so this can't be right. (typeId {0})",
                                                           typeId ),
                                             "typeId" );
            }

            IntPtr hProcess = _GetHProcForDebugClient( debugClient );

            UdtInfoRequest uir;

            // Since we're only getting info about one id, we use the alternate mode of
            // operation where the offsets are the actual addresses we want the info
            // stored at.
            IntPtr[] reqOffsets = new IntPtr[]
            {
                (IntPtr) (&uir.SymTag),
                (IntPtr) (&uir.UdtKind),
                (IntPtr) (&uir.Size),
                (IntPtr) (&uir.ChildrenCount),
                (IntPtr) (&uir.ClassParentId),
                (IntPtr) (&uir.VirtualTableShapeId),
                (IntPtr) (&uir.SymName),
            };

            var gtip = new IMAGEHLP_GET_TYPE_INFO_PARAMS();

            ulong reqsValid = 0;

            fixed( uint* pReqSizes = sm_udtInfoReqSizes )
            fixed( IntPtr* pReqOffsets = reqOffsets )
            fixed( IMAGEHLP_SYMBOL_TYPE_INFO* pReqKinds = sm_udtInfoReqKinds )
            {
                //gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.UNCACHED;
                gtip.NumIds = 1;
                gtip.TypeIds = &typeId;
                //gtip.TagFilter = (ulong) (1L << (int) SymTag.BaseType);
                gtip.TagFilter = (ulong) (1L << (int) SymTag.SymTagMax) - 1;
                gtip.NumReqs = (uint) sm_udtInfoReqKinds.Length;
                gtip.ReqKinds = pReqKinds;
                gtip.ReqOffsets = pReqOffsets;
                gtip.ReqSizes = pReqSizes;
                gtip.ReqStride = IntPtr.Zero;
                gtip.BufferSize = new IntPtr( -1 );
                gtip.Buffer = null;
                gtip.NumReqsValid = 1;
                gtip.ReqsValid = &reqsValid;

                try
                {
                    bool itWorked = NativeMethods.SymGetTypeInfoEx( hProcess,
                                                                    modBase,
                                                                    gtip );
                    if( !itWorked )
                        throw new DbgEngException( Marshal.GetLastWin32Error() );

                    // The bits in reqsValid tell us which requests were valid. We should always be able
                    // to get at least the tag, if it's a valid type ID at all. (if it's not,
                    // SymGetTypeInfoEx should have failed)
                    Util.Assert( 0 != (reqsValid & 0x01) );
                    if( SymTag.UDT != uir.SymTag )
                        throw new ArgumentException( Util.Sprintf( "TypeId {0} is not a UDT (it's a {1}).",
                                                                   typeId,
                                                                   uir.SymTag ) );

                    Util.Assert( 0x4f == (reqsValid & 0x4f) ); // we should always be able to get these items for a UDT
                    // If we couldn't get the other properties, their default values of 0 will be fine.

                    return new RawUdtInfo( uir );
                }
                finally
                {
                    if( IntPtr.Zero != uir.SymName )
                        Marshal.FreeHGlobal( uir.SymName );
                }
            } // end fixed()
        } // end GetUdtInfo_naked()


        // Not all of these will be valid, depending on the data kind.
        private static readonly IMAGEHLP_SYMBOL_TYPE_INFO[] sm_dataInfoReqKinds = new IMAGEHLP_SYMBOL_TYPE_INFO[]
        {                                                          // ReqsValid bits
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,               // 0x001
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_DATAKIND,             // 0x002
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID,               // 0x004
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CLASSPARENTID,        // 0x008
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_OFFSET,               // 0x010
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_ADDRESSOFFSET,        // 0x020
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_BITPOSITION,          // 0x040  only valid for bitfields
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LENGTH,               // 0x080  only valid for bitfields
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_ADDRESS,              // 0x100
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VALUE,                // 0x200  only valid for e.g. constant static members
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMNAME,              // 0x400
        };

        [StructLayout( LayoutKind.Sequential )]
        internal unsafe struct DataInfoRequest
        {
            public SymTag SymTag;
            public DataKind DataKind;
            public uint TypeId;
            public uint ClassParentId;
            public uint Offset;
            public uint AddressOffset;
            public uint BitPosition;       // only valid for bitfields
            public ulong Length;           // only valid for bitfields
            public ulong Address;
            public fixed byte Value[ 24 ]; // only valid for e.g. constant static members
            public IntPtr SymName;
        }

        private static readonly uint[] sm_dataInfoReqSizes = new uint[]
        {
            (uint) 4,  // Tag
            (uint) 4,  // DataKind
            (uint) 4,  // TypeId
            (uint) 4,  // ClassParentId
            (uint) 4,  // Offset
            (uint) 4,  // AddressOfset
            (uint) 4,  // BitPosition
            (uint) 8,  // Length
            (uint) 8,  // Address
            (uint) 24, // Value (VARIANT)
            (uint) IntPtr.Size, // SymName
        };


        public static unsafe RawDataInfo GetDataInfo( WDebugClient debugClient,
                                                      ulong modBase,
                                                      uint typeId )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                GetDataInfo_naked( debugClient,
                                   modBase,
                                   typeId ) );
        }

        private static unsafe RawDataInfo GetDataInfo_naked( WDebugClient debugClient,
                                                             ulong modBase,
                                                             uint typeId )
        {
            if( _IsSyntheticTypeId( typeId ) )
            {
                throw new ArgumentException( Util.Sprintf( "There is currently no way to register a synthetic data type, so this can't be right. (typeId {0})",
                                                           typeId ),
                                             "typeId" );
            }

            IntPtr hProcess = _GetHProcForDebugClient( debugClient );

            DataInfoRequest dir;

            IntPtr pvtIntPtr = new IntPtr( dir.Value );
            Marshal.GetNativeVariantForObject( null, pvtIntPtr ); // I'm hoping this gets me something like VT_EMPTY

            // Since we're only getting info about one id, we use the alternate mode of
            // operation where the offsets are the actual addresses we want the info
            // stored at.
            IntPtr[] reqOffsets = new IntPtr[]
            {
                (IntPtr) (&dir.SymTag),
                (IntPtr) (&dir.DataKind),
                (IntPtr) (&dir.TypeId),
                (IntPtr) (&dir.ClassParentId),
                (IntPtr) (&dir.Offset),
                (IntPtr) (&dir.AddressOffset),
                (IntPtr) (&dir.BitPosition),
                (IntPtr) (&dir.Length),
                (IntPtr) (&dir.Address),
                (IntPtr) (dir.Value), // <-- already a pointer!
                (IntPtr) (&dir.SymName),
            };

            var gtip = new IMAGEHLP_GET_TYPE_INFO_PARAMS();

            ulong reqsValid = 0;

            fixed( uint* pReqSizes = sm_dataInfoReqSizes )
            fixed( IntPtr* pReqOffsets = reqOffsets )
            fixed( IMAGEHLP_SYMBOL_TYPE_INFO* pReqKinds = sm_dataInfoReqKinds )
            {
                //gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.UNCACHED;
                gtip.NumIds = 1;
                gtip.TypeIds = &typeId;
                gtip.TagFilter = (ulong) (1L << (int) SymTag.SymTagMax) - 1;
                gtip.NumReqs = (uint) sm_dataInfoReqKinds.Length;
                gtip.ReqKinds = pReqKinds;
                gtip.ReqOffsets = pReqOffsets;
                gtip.ReqSizes = pReqSizes;
                gtip.ReqStride = IntPtr.Zero;
                gtip.BufferSize = new IntPtr( -1 );
                gtip.Buffer = null;
                gtip.NumReqsValid = 1;
                gtip.ReqsValid = &reqsValid;

                try
                {
                    bool itWorked = NativeMethods.SymGetTypeInfoEx( hProcess,
                                                                    modBase,
                                                                    gtip );
                    if( !itWorked )
                        throw new DbgEngException( Marshal.GetLastWin32Error() );

                    // The bits in reqsValid tell us which requests were valid. We should always be able
                    // to get at least the tag, if it's a valid type ID at all. (if it's not,
                    // SymGetTypeInfoEx should have failed)
                    Util.Assert( 0 != (reqsValid & 0x01) );
                    if( SymTag.Data != dir.SymTag )
                        throw new ArgumentException( Util.Sprintf( "TypeId {0} is not a Data (it's a {1}).",
                                                                   typeId,
                                                                   dir.SymTag ) );

                    //Util.Assert( 0x87 == (reqsValid & 0x87) ); // we should always be able to get these items for a Data, no matter the kind
                    //Util.Assert( 0x337 == (reqsValid & 0x337) ); // we should always be able to get these items for a Data, no matter the kind
                    //Util.Assert( 0x21f == (reqsValid & 0x21f) ); // we should always be able to get these items for a Data, no matter the kind
                    //Util.Assert( 0x20f == (reqsValid & 0x20f) ); // we should always be able to get these items for a Data, no matter the kind
                    Util.Assert( 0x407 == (reqsValid & 0x407) ); // we should always be able to get these items for a Data, no matter the kind
                    // TODO: Rewrite the assert to check for sets; that is, there are certain sets of
                    // properties that should be available together.
                    // If we couldn't get the other properties, their default values of 0
                    // will be fine.

                    // DbgHelp likes to initialize the Value VARIANT with an int 0 (as
                    // opposed to an empty VARIANT), so we need to use reqsValid to
                    // determine if we should pay attention to the Value or not.
                    return new RawDataInfo( dir, valueFieldIsValid: 0 != (reqsValid & 0x200) );
                }
                finally
                {
                    if( IntPtr.Zero != dir.SymName )
                        Marshal.FreeHGlobal( dir.SymName );
                }
            } // end fixed()
        } // end GetDataInfo_naked()


        // Not all of these will be valid, depending on the function.
        private static readonly IMAGEHLP_SYMBOL_TYPE_INFO[] sm_funcTypeInfoReqKinds = new IMAGEHLP_SYMBOL_TYPE_INFO[]
        {                                                          // ReqsValid bits
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,               // 0x01
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID,               // 0x02
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CHILDRENCOUNT,        // 0x04
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CLASSPARENTID,        // 0x08  (optional)
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_COUNT,                // 0x10
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_THISADJUST,           // 0x20  (optional)
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CALLING_CONVENTION,   // 0x40
        };

        private static readonly uint[] sm_funcTypeInfoReqSizes = new uint[]
        {
            (uint) 4,  // Tag
            (uint) 4,  // TypeId
            (uint) 4,  // ChildrenCount
            (uint) 4,  // ClassParentId
            (uint) 4,  // Count
            (uint) 4,  // ThisAdjust
            (uint) 4,  // CallingConvention
        };

        [StructLayout( LayoutKind.Sequential )]
        internal struct FuncTypeInfoRequest
        {
            public SymTag SymTag;
            public uint ReturnTypeId;
            public uint ChildrenCount;
            public uint ClassParentId;
            public uint Count; // what is this??
            public uint ThisAdjust; // only valid if ClassParentId is not 0
            public CallingConvention CallingConvention;
        }


        private static readonly IMAGEHLP_SYMBOL_TYPE_INFO[] sm_funcArgTypeInfoReqKinds = new IMAGEHLP_SYMBOL_TYPE_INFO[]
        {                                                          // ReqsValid bits
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,               // 0x01
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMINDEX,             // 0x02
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID,               // 0x04
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CHILDRENCOUNT,        // 0x08   # What are the children for? Templates/generics?
        };

        private static readonly uint[] sm_funcArgTypeInfoReqSizes = new uint[]
        {
            (uint) 4,  // Tag
            (uint) 4,  // SymIndex
            (uint) 4,  // TypeId
            (uint) 4,  // ChildrenCount
        };

        private static readonly SIZE_T[] sm_funcArgTypeInfoReqOffsets = new SIZE_T[]
        {
            (SIZE_T) 0x0,  // SymTag
            (SIZE_T) 0x4,  // SymIndex
            (SIZE_T) 0x8,  // TypeId
            (SIZE_T) 0xc,  // ChildrenCount
        };

        [StructLayout( LayoutKind.Sequential )]
        internal struct FuncArgTypeInfoRequest
        {
            public SymTag SymTag;
            public uint SymIndex;
            public uint ArgTypeId;
            public uint ChildrenCount;
        }

        public static unsafe RawFuncTypeInfo GetFuncTypeInfo( WDebugClient debugClient,
                                                              ulong modBase,
                                                              uint typeId )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                GetFuncTypeInfo_naked( debugClient,
                                       modBase,
                                       typeId ) );
        }

        private static unsafe RawFuncTypeInfo GetFuncTypeInfo_naked( WDebugClient debugClient,
                                                                     ulong modBase,
                                                                     uint typeId )
        {
            if( _IsSyntheticTypeId( typeId ) )
            {
                throw new ArgumentException( Util.Sprintf( "There is currently no way to register a synthetic functionType type, so this can't be right. (typeId {0})",
                                                           typeId ),
                                             "typeId" );
            }

            IntPtr hProcess = _GetHProcForDebugClient( debugClient );

            FuncTypeInfoRequest ftir;

            // Since we're only getting info about one id, we use the alternate mode of
            // operation where the offsets are the actual addresses we want the info
            // stored at.
            IntPtr[] reqOffsets = new IntPtr[]
            {
                (IntPtr) (&ftir.SymTag),
                (IntPtr) (&ftir.ReturnTypeId),
                (IntPtr) (&ftir.ChildrenCount),
                (IntPtr) (&ftir.ClassParentId),
                (IntPtr) (&ftir.Count),
                (IntPtr) (&ftir.ThisAdjust),
                (IntPtr) (&ftir.CallingConvention),
            };

            var gtip = new IMAGEHLP_GET_TYPE_INFO_PARAMS();

            ulong reqsValid = 0;

            fixed( uint* pReqSizes = sm_funcTypeInfoReqSizes )
            fixed( IntPtr* pReqOffsets = reqOffsets )
            fixed( IMAGEHLP_SYMBOL_TYPE_INFO* pReqKinds = sm_funcTypeInfoReqKinds )
            {
                //gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.UNCACHED;
                gtip.NumIds = 1;
                gtip.TypeIds = &typeId;
                gtip.TagFilter = (ulong) (1L << (int) SymTag.SymTagMax) - 1;
                gtip.NumReqs = (uint) sm_funcTypeInfoReqKinds.Length;
                gtip.ReqKinds = pReqKinds;
                gtip.ReqOffsets = pReqOffsets;
                gtip.ReqSizes = pReqSizes;
                gtip.ReqStride = IntPtr.Zero;
                gtip.BufferSize = new IntPtr( -1 );
                gtip.Buffer = null;
                gtip.NumReqsValid = 1;
                gtip.ReqsValid = &reqsValid;

                bool itWorked = NativeMethods.SymGetTypeInfoEx( hProcess,
                                                                modBase,
                                                                gtip );
                if( !itWorked )
                    throw new DbgEngException( Marshal.GetLastWin32Error() );

                // The bits in reqsValid tell us which requests were valid. We should always be able
                // to get at least the tag, if it's a valid type ID at all. (if it's not,
                // SymGetTypeInfoEx should have failed)
                Util.Assert( 0 != (reqsValid & 0x01) );
                if( SymTag.FunctionType != ftir.SymTag )
                    throw new ArgumentException( Util.Sprintf( "TypeId {0} is not a FunctionType (it's a {1}).",
                                                               typeId,
                                                               ftir.SymTag ) );

                Util.Assert( 0x57 == (reqsValid & 0x57) ); // we should always be able to get these items for a FunctionType, no matter the kind
                // If we couldn't get the other properties, their default values of 0
                // will be fine.

            } // end fixed()

            //
            // Now we get the children: the function arguments.
            //

            uint reqStride = (uint) Marshal.SizeOf( typeof( FuncArgTypeInfoRequest ) );

            uint bufSize = (uint) (ftir.ChildrenCount * reqStride);
            void* buf = (void*) Marshal.AllocHGlobal( (int) bufSize );

            ulong[] childrenReqsValid = new ulong[ ftir.ChildrenCount ];

            uint[] ids = new uint[] { typeId };

            var args = new List< RawFuncArgTypeInfo >( (int) ftir.ChildrenCount );

            try
            {
                fixed( uint* pIds = ids,
                             pReqSizes = sm_funcArgTypeInfoReqSizes )
                fixed( SIZE_T* pReqOffsets = sm_funcArgTypeInfoReqOffsets )
                fixed( ulong* pReqsValid = childrenReqsValid )
                fixed( IMAGEHLP_SYMBOL_TYPE_INFO* pReqKinds = sm_funcArgTypeInfoReqKinds )
                {
                    //gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.UNCACHED | IMAGEHLP_GET_TYPE_INFO_FLAGS.CHILDREN;
                    gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.CHILDREN;
                    gtip.NumIds = (uint) ids.Length;
                    gtip.TypeIds = pIds;
                    gtip.TagFilter = (ulong) (1L << (int) SymTag.SymTagMax) - 1;
                    gtip.NumReqs = (uint) sm_funcArgTypeInfoReqKinds.Length;
                    gtip.ReqKinds = pReqKinds;
                    gtip.ReqOffsets = pReqOffsets;
                    gtip.ReqSizes = pReqSizes;
                    gtip.ReqStride = (SIZE_T) reqStride;
                    gtip.BufferSize = (SIZE_T) bufSize;
                    gtip.Buffer = buf;

                    gtip.NumReqsValid = (uint) ftir.ChildrenCount;
                    gtip.ReqsValid = pReqsValid;

                    bool itWorked = NativeMethods.SymGetTypeInfoEx( hProcess,
                                                                    modBase,
                                                                    gtip );
                    if( !itWorked )
                        throw new DbgEngException( Marshal.GetLastWin32Error() );

                    if( gtip.EntriesFilled != ftir.ChildrenCount )
                    {
                        Util.Fail( "Couldn't get all the arguments or what?" );
                        throw new DbgEngException( 1 /*S_FALSE*/ ); // TODO: better error
                    }

                    FuncArgTypeInfoRequest* pArgs = (FuncArgTypeInfoRequest*) buf;
                    for( int i = 0; i < gtip.EntriesFilled; i++ )
                    {
                        // We should have been able to get all the requested data, and
                        // they should all be FunctionArgTypes.
                        Util.Assert( 0xf == (0xf & childrenReqsValid[ i ]) );
                        Util.Assert( SymTag.FunctionArgType == pArgs->SymTag );

                        args.Add( new RawFuncArgTypeInfo( *pArgs ) );
                        pArgs++;
                    }
                } // end fixed( stuff )

                return new RawFuncTypeInfo( ftir, args.AsReadOnly() );
            }
            finally
            {
                Marshal.FreeHGlobal( (IntPtr) buf );
            }
        } // end GetFuncTypeInfo_naked()


        private static readonly IMAGEHLP_SYMBOL_TYPE_INFO[] sm_baseClassInfoReqKinds = new IMAGEHLP_SYMBOL_TYPE_INFO[]
        {                                                              // ReqsValid bits
                                                                       //        Regular  Virtual
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,                   // 0x0001      x   x
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID,                   // 0x0002      x   x
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LENGTH,                   // 0x0004      x   x
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_OFFSET,                   // 0x0008      x       <
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_UDTKIND,                  // 0x0010      x   x
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CHILDRENCOUNT,            // 0x0020      x   x
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_NESTED,                   // 0x0040      x   x
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CLASSPARENTID,            // 0x0080      x   x
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VIRTUALBASECLASS,         // 0x0100      x   x
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VIRTUALTABLESHAPEID,      // 0x0200      x   x
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VIRTUALBASEDISPINDEX,     // 0x0400          x     >
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VIRTUALBASEOFFSET,        // 0x0800               o
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VIRTUALBASEPOINTEROFFSET, // 0x1000          x     >
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_INDIRECTVIRTUALBASECLASS, // 0x2000      x   x
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMNAME,                  // 0x4000      x   x
        };

        private static readonly uint[] sm_baseClassInfoReqSizes = new uint[]
        {
            (uint) 4,  // Tag
            (uint) 4,  // TypeId
            (uint) 8,  // Length
            (uint) 4,  // Offset
            (uint) 4,  // UdtKind
            (uint) 4,  // ChildrenCount
            (uint) 4,  // Nested
            (uint) 4,  // ClassParentId
            (uint) 4,  // VirtualBaseClass
            (uint) 4,  // VirtualTableShapeId
            (uint) 4,  // VirtualBaseDispIndex
            (uint) 4,  // VirtualBaseOffset
            (uint) 4,  // VirtualBasePointerOffset
            (uint) 4,  // IndirectVirtualBaseClass
            (uint) IntPtr.Size, // SymName
        };

        // A BaseClass is kind of funny--it looks just like a UDT, but with a few extra
        // properties (like offset, etc.), and with its own type ID.
        [StructLayout( LayoutKind.Sequential )]
        internal struct BaseClassInfoRequest
        {
            public SymTag SymTag;
            public uint BaseTypeId;     // TypeId of the UDT that represents the actual base class
            public ulong Size;
            public uint Offset;
            public UdtKind UdtKind;
            public uint ChildrenCount;
            public uint IsNested;
            public uint ClassParentId; // This seems to be the derived class that this is a base for?
            public uint IsVirtualBaseClass;
            public uint VirtualTableShapeId;
            public uint VirtualBaseDispIndex;
            public uint VirtualBaseOffset;
            public uint VirtualBasePointerOffset;
            public uint IsIndirectVirtualBaseClass;
            public IntPtr SymName; // The name of the base class
        }


        public static unsafe RawBaseClassInfo GetBaseClassInfo( WDebugClient debugClient,
                                                                ulong modBase,
                                                                uint typeId )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                GetBaseClassInfo_naked( debugClient,
                                        modBase,
                                        typeId ) );
        }

        private static unsafe RawBaseClassInfo GetBaseClassInfo_naked( WDebugClient debugClient,
                                                                       ulong modBase,
                                                                       uint typeId )
        {
            if( _IsSyntheticTypeId( typeId ) )
            {
                throw new ArgumentException( Util.Sprintf( "There is currently no way to register a synthetic baseClass type, so this can't be right. (typeId {0})",
                                                           typeId ),
                                             "typeId" );
            }

            IntPtr hProcess = _GetHProcForDebugClient( debugClient );

            BaseClassInfoRequest bcir;

            // Since we're only getting info about one id, we use the alternate mode of
            // operation where the offsets are the actual addresses we want the info
            // stored at.
            IntPtr[] reqOffsets = new IntPtr[]
            {
                (IntPtr) (&bcir.SymTag),
                (IntPtr) (&bcir.BaseTypeId),
                (IntPtr) (&bcir.Size),
                (IntPtr) (&bcir.Offset),
                (IntPtr) (&bcir.UdtKind),
                (IntPtr) (&bcir.ChildrenCount),
                (IntPtr) (&bcir.IsNested),
                (IntPtr) (&bcir.ClassParentId),
                (IntPtr) (&bcir.IsVirtualBaseClass),
                (IntPtr) (&bcir.VirtualTableShapeId),
                (IntPtr) (&bcir.VirtualBaseDispIndex),
                (IntPtr) (&bcir.VirtualBaseOffset),
                (IntPtr) (&bcir.VirtualBasePointerOffset),
                (IntPtr) (&bcir.IsIndirectVirtualBaseClass),
                (IntPtr) (&bcir.SymName),
            };

            var gtip = new IMAGEHLP_GET_TYPE_INFO_PARAMS();

            ulong reqsValid = 0;

            fixed( uint* pReqSizes = sm_baseClassInfoReqSizes )
            fixed( IntPtr* pReqOffsets = reqOffsets )
            fixed( IMAGEHLP_SYMBOL_TYPE_INFO* pReqKinds = sm_baseClassInfoReqKinds )
            {
                //gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.UNCACHED;
                gtip.NumIds = 1;
                gtip.TypeIds = &typeId;
                gtip.TagFilter = (ulong) (1L << (int) SymTag.SymTagMax) - 1;
                gtip.NumReqs = (uint) sm_baseClassInfoReqKinds.Length;
                gtip.ReqKinds = pReqKinds;
                gtip.ReqOffsets = pReqOffsets;
                gtip.ReqSizes = pReqSizes;
                gtip.ReqStride = IntPtr.Zero;
                gtip.BufferSize = new IntPtr( -1 );
                gtip.Buffer = null;
                gtip.NumReqsValid = 1;
                gtip.ReqsValid = &reqsValid;

                try
                {
                    bool itWorked = NativeMethods.SymGetTypeInfoEx( hProcess,
                                                                    modBase,
                                                                    gtip );
                    if( !itWorked )
                        throw new DbgEngException( Marshal.GetLastWin32Error() );

                    // The bits in reqsValid tell us which requests were valid. We should always be able
                    // to get at least the tag, if it's a valid type ID at all. (if it's not,
                    // SymGetTypeInfoEx should have failed)
                    Util.Assert( 0 != (reqsValid & 0x01) );
                    if( SymTag.BaseClass != bcir.SymTag )
                        throw new ArgumentException( Util.Sprintf( "TypeId {0} is not a BaseClass (it's a {1}).",
                                                                   typeId,
                                                                   bcir.SymTag ) );

                    Util.Assert( (0x63ff == reqsValid) || (0x77f7 == reqsValid) );

                    if( 0x63ff == reqsValid )
                    {
                        Util.Assert( 0 == bcir.IsVirtualBaseClass );
                    }
                    else
                    {
                        Util.Assert( 0 != bcir.IsVirtualBaseClass );
                    }

                    Util.Assert( 0 == bcir.VirtualBaseOffset ); // Not an invariant; I just want to see if it ever happens.

                    return new RawBaseClassInfo( bcir );
                }
                finally
                {
                    if( IntPtr.Zero != bcir.SymName )
                        Marshal.FreeHGlobal( bcir.SymName );
                }
            } // end fixed()
        } // end GetBaseClassInfo_naked()


        private static readonly IMAGEHLP_SYMBOL_TYPE_INFO[] sm_funcInfoReqKinds = new IMAGEHLP_SYMBOL_TYPE_INFO[]
        {                                                          // ReqsValid bits
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,               // 0x001
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID,               // 0x002
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CHILDRENCOUNT,        // 0x004
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CLASSPARENTID,        // 0x008
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_ADDRESS,              // 0x010  // might not exist, like for pure virtual functions
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LENGTH,               // 0x020  // might not exist, like for pure virtual functions
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VIRTUALBASEOFFSET,    // 0x040
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMINDEX,             // 0x080 // I don't know what this is.
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMNAME,              // 0x100
        };

        [StructLayout( LayoutKind.Sequential )]
        internal struct FuncInfoRequest
        {
            public SymTag SymTag;
            public uint FuncTypeTypeId;
            public uint ChildrenCount;
            public uint ClassParentId;
            public ulong Address;
            public ulong Length;
            public uint VirtualBaseOffset;
            public uint SymIndex;
            public IntPtr SymName;
        }


        private static readonly uint[] sm_funcInfoReqSizes = new uint[]
        {
            (uint) 4,  // Tag
            (uint) 4,  // TypeId
            (uint) 4,  // ChildrenCount
            (uint) 4,  // ClassParentId
            (uint) 8,  // Address
            (uint) 8,  // Length
            (uint) 4,  // VirtualBaseOffset
            (uint) 4,  // SymIndex
            (uint) IntPtr.Size, // SymName
        };


        public static unsafe RawFuncInfo GetFuncInfo( WDebugClient debugClient,
                                                      ulong modBase,
                                                      uint typeId )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                GetFuncInfo_naked( debugClient,
                                   modBase,
                                   typeId ) );
        }

        private static unsafe RawFuncInfo GetFuncInfo_naked( WDebugClient debugClient,
                                                             ulong modBase,
                                                             uint typeId )
        {
            if( _IsSyntheticTypeId( typeId ) )
            {
                throw new ArgumentException( Util.Sprintf( "There is currently no way to register a synthetic Function, so this can't be right. (typeId {0})",
                                                           typeId ),
                                             "typeId" );
            }

            IntPtr hProcess = _GetHProcForDebugClient( debugClient );

            FuncInfoRequest fir;

            // Since we're only getting info about one id, we use the alternate mode of
            // operation where the offsets are the actual addresses we want the info
            // stored at.
            IntPtr[] reqOffsets = new IntPtr[]
            {
                (IntPtr) (&fir.SymTag),
                (IntPtr) (&fir.FuncTypeTypeId),
                (IntPtr) (&fir.ChildrenCount),
                (IntPtr) (&fir.ClassParentId),
                (IntPtr) (&fir.Address),
                (IntPtr) (&fir.Length),
                (IntPtr) (&fir.VirtualBaseOffset),
                (IntPtr) (&fir.SymIndex),
                (IntPtr) (&fir.SymName),
            };

            var gtip = new IMAGEHLP_GET_TYPE_INFO_PARAMS();

            ulong reqsValid = 0;

            fixed( uint* pReqSizes = sm_funcInfoReqSizes )
            fixed( IntPtr* pReqOffsets = reqOffsets )
            fixed( IMAGEHLP_SYMBOL_TYPE_INFO* pReqKinds = sm_funcInfoReqKinds )
            {
                //gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.UNCACHED;
                gtip.NumIds = 1;
                gtip.TypeIds = &typeId;
                //gtip.TagFilter = (ulong) (1L << (int) SymTag.BaseType);
                gtip.TagFilter = (ulong) (1L << (int) SymTag.SymTagMax) - 1;
                gtip.NumReqs = (uint) sm_funcInfoReqKinds.Length;
                gtip.ReqKinds = pReqKinds;
                gtip.ReqOffsets = pReqOffsets;
                gtip.ReqSizes = pReqSizes;
                gtip.ReqStride = IntPtr.Zero;
                gtip.BufferSize = new IntPtr( -1 );
                gtip.Buffer = null;
                gtip.NumReqsValid = 1;
                gtip.ReqsValid = &reqsValid;

                try
                {
                    bool itWorked = NativeMethods.SymGetTypeInfoEx( hProcess,
                                                                    modBase,
                                                                    gtip );
                    if( !itWorked )
                        throw new DbgEngException( Marshal.GetLastWin32Error() );

                    // The bits in reqsValid tell us which requests were valid. We should always be able
                    // to get at least the tag, if it's a valid type ID at all. (if it's not,
                    // SymGetTypeInfoEx should have failed)
                    Util.Assert( 0 != (reqsValid & 0x01) );
                    if( SymTag.Function != fir.SymTag )
                        throw new ArgumentException( Util.Sprintf( "TypeId {0} is not a Function (it's a {1}).",
                                                                   typeId,
                                                                   fir.SymTag ) );

                    Util.Assert( 0x1cf == (reqsValid & 0x1cf) ); // we should always be able to get these items for a Function
                    // If we couldn't get the other properties, their default values of 0 will be fine.

                    return new RawFuncInfo( typeId, fir );
                }
                finally
                {
                    if( IntPtr.Zero != fir.SymName )
                        Marshal.FreeHGlobal( fir.SymName );
                }
            } // end fixed()
        } // end GetFuncInfo_naked()


        private static readonly IMAGEHLP_SYMBOL_TYPE_INFO[] sm_vtableInfoReqKinds = new IMAGEHLP_SYMBOL_TYPE_INFO[]
        {                                                          // ReqsValid bits
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,               // 0x01
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID,               // 0x02
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CHILDRENCOUNT,        // 0x04
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CLASSPARENTID,        // 0x08
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_OFFSET,               // 0x10
        };

        [StructLayout( LayoutKind.Sequential )]
        internal struct VTableInfoRequest
        {
            public SymTag SymTag;
            public uint PointerTypeId;
            public uint ChildrenCount;
            public uint ClassParentId;
            public uint Offset;
        }


        private static readonly uint[] sm_vtableInfoReqSizes = new uint[]
        {
            (uint) 4,  // Tag
            (uint) 4,  // TypeId
            (uint) 4,  // ChildrenCount
            (uint) 4,  // ClassParentId
            (uint) 4,  // Offset
        };


        public static unsafe RawVTableInfo GetVTableInfo( WDebugClient debugClient,
                                                          ulong modBase,
                                                          uint typeId )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                GetVTableInfo_naked( debugClient,
                                     modBase,
                                     typeId ) );
        }

        private static unsafe RawVTableInfo GetVTableInfo_naked( WDebugClient debugClient,
                                                                 ulong modBase,
                                                                 uint typeId )
        {
            if( _IsSyntheticTypeId( typeId ) )
            {
                throw new ArgumentException( Util.Sprintf( "There is currently no way to register a synthetic VTable, so this can't be right. (typeId {0})",
                                                           typeId ),
                                             "typeId" );
            }

            IntPtr hProcess = _GetHProcForDebugClient( debugClient );

            VTableInfoRequest vir;

            // Since we're only getting info about one id, we use the alternate mode of
            // operation where the offsets are the actual addresses we want the info
            // stored at.
            IntPtr[] reqOffsets = new IntPtr[]
            {
                (IntPtr) (&vir.SymTag),
                (IntPtr) (&vir.PointerTypeId),
                (IntPtr) (&vir.ChildrenCount),
                (IntPtr) (&vir.ClassParentId),
                (IntPtr) (&vir.Offset)
            };

            var gtip = new IMAGEHLP_GET_TYPE_INFO_PARAMS();

            ulong reqsValid = 0;

            fixed( uint* pReqSizes = sm_vtableInfoReqSizes )
            fixed( IntPtr* pReqOffsets = reqOffsets )
            fixed( IMAGEHLP_SYMBOL_TYPE_INFO* pReqKinds = sm_vtableInfoReqKinds )
            {
                //gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.UNCACHED;
                gtip.NumIds = 1;
                gtip.TypeIds = &typeId;
                //gtip.TagFilter = (ulong) (1L << (int) SymTag.BaseType);
                gtip.TagFilter = (ulong) (1L << (int) SymTag.SymTagMax) - 1;
                gtip.NumReqs = (uint) sm_vtableInfoReqKinds.Length;
                gtip.ReqKinds = pReqKinds;
                gtip.ReqOffsets = pReqOffsets;
                gtip.ReqSizes = pReqSizes;
                gtip.ReqStride = IntPtr.Zero;
                gtip.BufferSize = new IntPtr( -1 );
                gtip.Buffer = null;
                gtip.NumReqsValid = 1;
                gtip.ReqsValid = &reqsValid;

                try
                {
                    bool itWorked = NativeMethods.SymGetTypeInfoEx( hProcess,
                                                                    modBase,
                                                                    gtip );
                    if( !itWorked )
                        throw new DbgEngException( Marshal.GetLastWin32Error() );

                    // The bits in reqsValid tell us which requests were valid. We should always be able
                    // to get at least the tag, if it's a valid type ID at all. (if it's not,
                    // SymGetTypeInfoEx should have failed)
                    Util.Assert( 0 != (reqsValid & 0x01) );
                    if( SymTag.VTable != vir.SymTag )
                        throw new ArgumentException( Util.Sprintf( "TypeId {0} is not a VTable (it's a {1}).",
                                                                   typeId,
                                                                   vir.SymTag ) );

                    Util.Assert( 0x1f == (reqsValid & 0x1f) ); // we should always be able to get these items for a VTable
                    // If we couldn't get the other properties, their default values of 0 will be fine.

                    return new RawVTableInfo( typeId, vir );
                }
                finally
                {
                }
            } // end fixed()
        } // end GetVTableInfo_naked()


        private static readonly IMAGEHLP_SYMBOL_TYPE_INFO[] sm_vtableShapeInfoReqKinds = new IMAGEHLP_SYMBOL_TYPE_INFO[]
        {                                                          // ReqsValid bits
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,               // 0x01
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_COUNT,                // 0x02
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CHILDRENCOUNT,        // 0x04
        };

        [StructLayout( LayoutKind.Sequential )]
        internal struct VTableShapeInfoRequest
        {
            public SymTag SymTag;
            public uint Count;
            public uint ChildrenCount;
        }


        private static readonly uint[] sm_vtableShapeInfoReqSizes = new uint[]
        {
            (uint) 4,  // Tag
            (uint) 4,  // Count
            (uint) 4,  // ChildrenCount
        };


        public static unsafe RawVTableShapeInfo GetVTableShapeInfo( WDebugClient debugClient,
                                                                    ulong modBase,
                                                                    uint typeId )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                GetVTableShapeInfo_naked( debugClient,
                                          modBase,
                                          typeId ) );
        }

        private static unsafe RawVTableShapeInfo GetVTableShapeInfo_naked( WDebugClient debugClient,
                                                                           ulong modBase,
                                                                           uint typeId )
        {
            if( _IsSyntheticTypeId( typeId ) )
            {
                throw new ArgumentException( Util.Sprintf( "There is currently no way to register a synthetic VTableShape, so this can't be right. (typeId {0})",
                                                           typeId ),
                                             "typeId" );
            }

            IntPtr hProcess = _GetHProcForDebugClient( debugClient );

            VTableShapeInfoRequest vsir;

            // Since we're only getting info about one id, we use the alternate mode of
            // operation where the offsets are the actual addresses we want the info
            // stored at.
            IntPtr[] reqOffsets = new IntPtr[]
            {
                (IntPtr) (&vsir.SymTag),
                (IntPtr) (&vsir.Count),
                (IntPtr) (&vsir.ChildrenCount),
            };

            var gtip = new IMAGEHLP_GET_TYPE_INFO_PARAMS();

            ulong reqsValid = 0;

            fixed( uint* pReqSizes = sm_vtableShapeInfoReqSizes )
            fixed( IntPtr* pReqOffsets = reqOffsets )
            fixed( IMAGEHLP_SYMBOL_TYPE_INFO* pReqKinds = sm_vtableShapeInfoReqKinds )
            {
                //gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.UNCACHED;
                gtip.NumIds = 1;
                gtip.TypeIds = &typeId;
                //gtip.TagFilter = (ulong) (1L << (int) SymTag.BaseType);
                gtip.TagFilter = (ulong) (1L << (int) SymTag.SymTagMax) - 1;
                gtip.NumReqs = (uint) sm_vtableShapeInfoReqKinds.Length;
                gtip.ReqKinds = pReqKinds;
                gtip.ReqOffsets = pReqOffsets;
                gtip.ReqSizes = pReqSizes;
                gtip.ReqStride = IntPtr.Zero;
                gtip.BufferSize = new IntPtr( -1 );
                gtip.Buffer = null;
                gtip.NumReqsValid = 1;
                gtip.ReqsValid = &reqsValid;

                try
                {
                    bool itWorked = NativeMethods.SymGetTypeInfoEx( hProcess,
                                                                    modBase,
                                                                    gtip );
                    if( !itWorked )
                        throw new DbgEngException( Marshal.GetLastWin32Error() );

                    // The bits in reqsValid tell us which requests were valid. We should always be able
                    // to get at least the tag, if it's a valid type ID at all. (if it's not,
                    // SymGetTypeInfoEx should have failed)
                    Util.Assert( 0 != (reqsValid & 0x01) );
                    if( SymTag.VTableShape != vsir.SymTag )
                        throw new ArgumentException( Util.Sprintf( "TypeId {0} is not a VTableShape (it's a {1}).",
                                                                   typeId,
                                                                   vsir.SymTag ) );

                    Util.Assert( 0x07 == (reqsValid & 0x07) ); // we should always be able to get these items for a VTable
                    // If we couldn't get the other properties, their default values of 0 will be fine.

                    return new RawVTableShapeInfo( typeId, vsir );
                }
                finally
                {
                }
            } // end fixed()
        } // end GetVTableShapeInfo_naked()


        private static readonly IMAGEHLP_SYMBOL_TYPE_INFO[] sm_enumInfoReqKinds = new IMAGEHLP_SYMBOL_TYPE_INFO[]
        {                                                          // ReqsValid bits
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,               // 0x001
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CHILDRENCOUNT,        // 0x002
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LENGTH,               // 0x004
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID,               // 0x008
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_BASETYPE,             // 0x010
            IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMNAME,              // 0x020
        };

        [StructLayout( LayoutKind.Sequential )]
        internal struct EnumInfoRequest
        {
            public SymTag SymTag;
            public uint ChildrenCount;
            public ulong Length;
            public uint BaseTypeId;
            public uint BaseType;
            public IntPtr SymName;
        }


        private static readonly uint[] sm_enumInfoReqSizes = new uint[]
        {
            (uint) 4,  // Tag
            (uint) 4,  // ChildrenCount
            (uint) 8,  // Length
            (uint) 4,  // BaseTypeId
            (uint) 4,  // BaseType
            (uint) IntPtr.Size, // SymName
        };


        public static unsafe RawEnumInfo GetEnumInfo( WDebugClient debugClient,
                                                      ulong modBase,
                                                      uint typeId )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                GetEnumInfo_naked( debugClient,
                                   modBase,
                                   typeId ) );
        }

        private static unsafe RawEnumInfo GetEnumInfo_naked( WDebugClient debugClient,
                                                             ulong modBase,
                                                             uint typeId )
        {
            if( _IsSyntheticTypeId( typeId ) )
            {
                throw new ArgumentException( Util.Sprintf( "There is currently no way to register a synthetic enum, so this can't be right. (typeId {0})",
                                                           typeId ),
                                             "typeId" );
            }

            IntPtr hProcess = _GetHProcForDebugClient( debugClient );

            EnumInfoRequest eir;

            // Since we're only getting info about one id, we use the alternate mode of
            // operation where the offsets are the actual addresses we want the info
            // stored at.
            IntPtr[] reqOffsets = new IntPtr[]
            {
                (IntPtr) (&eir.SymTag),
                (IntPtr) (&eir.ChildrenCount),
                (IntPtr) (&eir.Length),
                (IntPtr) (&eir.BaseTypeId),
                (IntPtr) (&eir.BaseType),
                (IntPtr) (&eir.SymName),
            };

            var gtip = new IMAGEHLP_GET_TYPE_INFO_PARAMS();

            ulong reqsValid = 0;

            fixed( uint* pReqSizes = sm_enumInfoReqSizes )
            fixed( IntPtr* pReqOffsets = reqOffsets )
            fixed( IMAGEHLP_SYMBOL_TYPE_INFO* pReqKinds = sm_enumInfoReqKinds )
            {
                //gtip.Flags = IMAGEHLP_GET_TYPE_INFO_FLAGS.UNCACHED;
                gtip.NumIds = 1;
                gtip.TypeIds = &typeId;
                //gtip.TagFilter = (ulong) (1L << (int) SymTag.BaseType);
                gtip.TagFilter = (ulong) (1L << (int) SymTag.SymTagMax) - 1;
                gtip.NumReqs = (uint) sm_enumInfoReqKinds.Length;
                gtip.ReqKinds = pReqKinds;
                gtip.ReqOffsets = pReqOffsets;
                gtip.ReqSizes = pReqSizes;
                gtip.ReqStride = IntPtr.Zero;
                gtip.BufferSize = new IntPtr( -1 );
                gtip.Buffer = null;
                gtip.NumReqsValid = 1;
                gtip.ReqsValid = &reqsValid;

                try
                {
                    bool itWorked = NativeMethods.SymGetTypeInfoEx( hProcess,
                                                                    modBase,
                                                                    gtip );
                    if( !itWorked )
                        throw new DbgEngException( Marshal.GetLastWin32Error() );

                    // The bits in reqsValid tell us which requests were valid. We should always be able
                    // to get at least the tag, if it's a valid type ID at all. (if it's not,
                    // SymGetTypeInfoEx should have failed)
                    Util.Assert( 0 != (reqsValid & 0x01) );
                    if( SymTag.Enum != eir.SymTag )
                        throw new ArgumentException( Util.Sprintf( "TypeId {0} is not an Enum (it's a {1}).",
                                                                   typeId,
                                                                   eir.SymTag ) );

                    Util.Assert( 0x3f == (reqsValid & 0x3f) ); // we should always be able to get these items for an Enum
                    // If we couldn't get the other properties, their default values of 0 will be fine.

                    return new RawEnumInfo( typeId, eir );
                }
                finally
                {
                    if( IntPtr.Zero != eir.SymName )
                        Marshal.FreeHGlobal( eir.SymName );
                }
            } // end fixed()
        } // end GetEnumInfo_naked()


        public static unsafe Enumerand[] GetEnumerands( WDebugClient debugClient,
                                                        ulong modBase,
                                                        uint typeId,
                                                        ulong size )
        {
            return GetEnumerands( _GetHProcForDebugClient( debugClient ),
                                  modBase,
                                  typeId,
                                  size,
                                  0 ); // numEnumerands
        }

        public static unsafe Enumerand[] GetEnumerands( WDebugClient debugClient,
                                                        ulong modBase,
                                                        uint typeId,
                                                        ulong size,
                                                        uint numEnumerands )
        {
            return GetEnumerands( _GetHProcForDebugClient( debugClient ),
                                  modBase,
                                  typeId,
                                  size,
                                  numEnumerands );
        }


        public static unsafe Enumerand[] GetEnumerands( IntPtr hProcess,
                                                        ulong modBase,
                                                        uint typeId,
                                                        ulong size,
                                                        uint numEnumerands ) // pass 0 for "don't know"
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                GetEnumerands_naked( hProcess,
                                     modBase,
                                     typeId,
                                     size,
                                     numEnumerands ) );
        }

        private static unsafe Enumerand[] GetEnumerands_naked( IntPtr hProcess,
                                                               ulong modBase,
                                                               uint typeId,
                                                               ulong size,
                                                               uint numEnumerands ) // pass 0 for "don't know"
        {
            // TODO: If I wanted, I could do this all in at most two shots: one to get the
            // number of children, and one to get all the info for all the children at the
            // same type (via SymGetTypeInfoEx).

            uint[] children;

            if( 0 == numEnumerands )
                children = _FindTypeChildrenRaw( hProcess, modBase, typeId );
            else
                children = _FindTypeChildrenRaw( hProcess, modBase, typeId, numEnumerands );

            int numChildren = children.Length - 2;

            Util.Assert( (0 == numEnumerands) || (numEnumerands == numChildren) );

            Enumerand[] enumerands = new Enumerand[ numChildren ];
            for( int i = 0; i < numChildren; i++ )
            {
                uint childId = children[ 2 + i ]; // +2: skip over TI_FINDCHILDREN_PARAMS header
                string name;
                object value;
                GetConstantNameAndValue( hProcess,
                                         modBase,
                                         childId,
                                         out name,
                                         out value );

                // For some reason, this usually returns 2-byte values, even though native
                // enums are usually backed by "int", so I'll just convert it. (Perhaps
                // it's done to save room in the PDB?)
                //
                // We have to be careful about sign extensions.
                //
                // And in fact, I'm not 100% sure the correct way to do this (as evidenced
                // by the many tweaks to this code!).
                //
                // For an example, let's take the "CoreMessaging!System::Object::Flags::E"
                // enum type.
                //
                // In source code, it is declared as using "uint32" storage. It has an
                // enumerand called "HandleMask", which has a value (in the source code)
                // of 0xfffffc00.
                //
                // When we ask dbghelp about this enum, it claims the base type is a
                // /signed/, 4-byte integer. And for the value of the enumerand, it gives
                // us back a /2-byte/, /signed/ integer value of 0xfc00. Windbg will
                // display this as "0n-1024", which, IF you sign-extend to 4 bytes,
                // happens to be correct.
                //
                // If I add another enumerand value, which (in source) is actually
                // declared as 0x0000fc00, windbg still manages to get it right. In this
                // case, I believe dbghelp is returning an /unsigned/ two-byte value.
                //
                // So, based on that, here's my current theory on what to do:
                //
                // If the size of the enumerand value is smaller than the size of the enum
                // backing type, then we need to extend it.
                //
                //    * If the enumerand value returned from dbghelp is signed: sign-extend
                //      it,
                //    * else extend without sign.
                //
                // Since we'd like to further expand all enumerands to ulongs in order to
                // deal with them uniformly, when we expand from there to ulong, we
                // /won't/ use sign extension.
                //

                value = _ExtendToSize( (dynamic) value, size );

                ulong u = Util.ConvertToUlongWithoutSignExtension( (dynamic) value );
                enumerands[ i ] = new Enumerand( name, u );
            }

            return enumerands;
        } // end GetEnumerands_naked()


        internal static object ReconstituteConstant( object rawVal, DbgBaseTypeInfo targetType )
        {
            object resizedVal = _ExtendToSize( (dynamic) rawVal, targetType.Size );

            // Now, is it possible that DbgHelp gave us a signed raw value, but the actual
            // target type is not signed? (or vice versa) Don't know. But we should handle
            // it.

            return _EnsureSignedness( (dynamic) resizedVal, targetType.IsSigned );
        }


        //
        // _ExtendToSize: if the source value is signed, sign-extend; else extend without
        // sign.
        //

        private static object _ExtendToSize( sbyte value, ulong size )
        {
            switch( size )
            {
                case 1:
                    return value;
                case 2:
                    return (short) value;
                case 4:
                    return (int) value;
                case 8:
                    return (long) value;
                default:
                    throw new Exception( "Unexpected." );
            }
        }

        private static object _ExtendToSize( byte value, ulong size )
        {
            switch( size )
            {
                case 1:
                    return value;
                case 2:
                    return (ushort) value;
                case 4:
                    return (uint) value;
                case 8:
                    return (ulong) value;
                default:
                    throw new Exception( "Unexpected." );
            }
        }

        private static object _ExtendToSize( short value, ulong size )
        {
            switch( size )
            {
                case 1:
                    if( 0 != (0xff00 & value) )
                    {
                        throw new Exception( Util.Sprintf( "Unexpected: enum value is two bytes (signed), with a non-zero high byte, but the the enum type is only 1 byte (0x{0:x})",
                                                           value ) );
                    }
                    // I've seen this with a D3D11 enum (EDDIVersion).
                    return (sbyte) value;
                case 2:
                    return value;
                case 4:
                    return (int) value;
                case 8:
                    return (long) value;
                default:
                    throw new Exception( "Unexpected." );
            }
        }

        private static object _ExtendToSize( ushort value, ulong size )
        {
            switch( size )
            {
                case 1:
                    if( 0 != (0xff00 & value) )
                    {
                        throw new Exception( Util.Sprintf( "Unexpected: enum value is two bytes, with a non-zero high byte, but the the enum type is only 1 byte (0x{0:x})",
                                                           value ) );
                    }
                    return (byte) value;
                case 2:
                    return value;
                case 4:
                    return (uint) value;
                case 8:
                    return (ulong) value;
                default:
                    throw new Exception( "Unexpected." );
            }
        }

        private static object _ExtendToSize( int value, ulong size )
        {
            switch( size )
            {
                case 4:
                    return value;
                case 8:
                    return (long) value;
                default:
                    throw new Exception( "Unexpected." );
            }
        }

        private static object _ExtendToSize( uint value, ulong size )
        {
            switch( size )
            {
                case 4:
                    return value;
                case 8:
                    return (ulong) value;
                default:
                    throw new Exception( "Unexpected." );
            }
        }

        private static object _ExtendToSize( long value, ulong size )
        {
            switch( size )
            {
                case 8:
                    return value;
                default:
                    throw new Exception( "Unexpected." );
            }
        }

        private static object _ExtendToSize( ulong value, ulong size )
        {
            switch( size )
            {
                case 8:
                    return value;
                default:
                    throw new Exception( "Unexpected." );
            }
        }


        //
        // _EnsureSignedness: make sure the value is signed/unsigned, as requested.
        //

        private static object _EnsureSignedness( sbyte value, bool signed )
        {
            if( signed )
                return value;
            else
                return (byte) value;
        }

        private static object _EnsureSignedness( byte value, bool signed )
        {
            if( signed )
                return (sbyte) value;
            else
                return value;
        }

        private static object _EnsureSignedness( short value, bool signed )
        {
            if( signed )
                return value;
            else
                return (ushort) value;
        }

        private static object _EnsureSignedness( ushort value, bool signed )
        {
            if( signed )
                return (short) value;
            else
                return value;
        }

        private static object _EnsureSignedness( int value, bool signed )
        {
            if( signed )
                return value;
            else
                return (uint) value;
        }

        private static object _EnsureSignedness( uint value, bool signed )
        {
            if( signed )
                return (int) value;
            else
                return value;
        }

        private static object _EnsureSignedness( long value, bool signed )
        {
            if( signed )
                return value;
            else
                return (ulong) value;
        }

        private static object _EnsureSignedness( ulong value, bool signed )
        {
            if( signed )
                return (long) value;
            else
                return value;
        }


        public static unsafe void GetConstantNameAndValue( IntPtr hProcess,
                                                           ulong modBase,
                                                           uint typeId,
                                                           out string name,
                                                           out object value )
        {
            name = null;
            value = null;
            IntPtr ptr = IntPtr.Zero;

            try
            {
                _SymGetTypeInfo( hProcess,
                                 modBase,
                                 typeId,
                                 IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMNAME,
                                 &ptr );

                name = Marshal.PtrToStringUni( ptr );
            }
            finally
            {
                if( IntPtr.Zero != ptr )
                    Marshal.FreeHGlobal( ptr );
            }

            byte* pvt = stackalloc byte[ 24 ];
            IntPtr pvtIntPtr = new IntPtr( pvt );
            Marshal.GetNativeVariantForObject( null, pvtIntPtr ); // I'm hoping this gets me something like VT_EMPTY

            _SymGetTypeInfo( hProcess,
                             modBase,
                             typeId,
                             IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VALUE,
                             pvt );

            // TODO: Uh-oh, these VARIANT-related APIs are obsolete...
            value = Marshal.GetObjectForNativeVariant( pvtIntPtr );
        } // end GetConstantNameAndValue()


        public static unsafe BasicType GetBaseType( WDebugClient debugClient,
                                                    ulong modBase,
                                                    uint typeId )
        {
            return GetBaseType( _GetHProcForDebugClient( debugClient ), modBase, typeId );
        }

        public static unsafe BasicType GetBaseType( IntPtr hProcess,
                                                    ulong modBase,
                                                    uint typeId )
        {
            BasicType baseType = 0;
            _SymGetTypeInfo( hProcess,
                             modBase,
                             typeId,
                             IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_BASETYPE,
                             &baseType );

            return baseType;
        } // end GetBaseType()


        public static unsafe SymTag GetSymTag( WDebugClient debugClient,
                                               ulong modBase,
                                               uint typeId )
        {
            return GetSymTag( _GetHProcForDebugClient( debugClient ), modBase, typeId );
        }


        public static unsafe SymTag GetSymTag( IntPtr hProcess,
                                               ulong modBase,
                                               uint typeId )
        {
            // 0 is apparently a special case. Sometimes, we'll have a symtag already in hand
            // for typeId 0 (SymTag.Null), such as when getting info about children. But if we
            // ask for the SymTag of typeId 0, it will throw. So we'll just assume that 0 always
            // means SymTag.Null.
            if( 0 == typeId )
                return SymTag.Null;

            SymTag symTag = 0;
            // TODO: SymTags should always be available for type info symbols. But if it's
            // some other sort of symbol, allegedly this is supposed to return S_FALSE.
            // Myabe we should have a special exception for "property not available" or
            // something?
            _SymGetTypeInfo( hProcess,
                             modBase,
                             typeId,
                             IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG,
                             &symTag );

            return symTag;
        } // end GetSymTag()


        public static IEnumerable< SymbolInfo > EnumTypesByName( WDebugClient debugClient,
                                                                 ulong modBase,
                                                                 string mask,
                                                                 CancellationToken cancelToken )
        {
            return EnumTypesByName( _GetHProcForDebugClient( debugClient ), modBase, mask, cancelToken );
        } // end EnumTypesByName()


        // Note that this does not include synthetic types.
        public static IEnumerable< SymbolInfo > EnumTypesByName( IntPtr hProcess,
                                                                 ulong modBase,
                                                                 string mask,
                                                                 CancellationToken cancelToken )
        {
            if( String.IsNullOrEmpty( mask ) )
                throw new ArgumentException( "You must supply a mask", "mask" );

            using( var bcHolder = new BlockingCollectionHolder< SymbolInfo >( cancelToken ) )
            {
                DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                    {
                        _EnumSymsWorker(
                            (callback) => NativeMethods.SymEnumTypesByName( hProcess,
                                                                            modBase,
                                                                            mask,
                                                                            callback,
                                                                            pUserContext: hProcess ),
                            bcHolder );
                    } );
                foreach( var si in bcHolder.GetConsumingEnumerable() )
                {
                    // Note that an exception could come flying out of the following line.
                    // Not because the cast will throw, but because the iterator state
                    // machine generated by the compiler means that external code gets
                    // executed "here".
                    yield return si;
                }
            }
        } // end EnumTypesByName()


        public static SymbolInfo TryGetTypeFromName( WDebugClient debugClient,
                                                     ulong modBase,
                                                     string typeName )
        {
            return TryGetTypeFromName( _GetHProcForDebugClient( debugClient ),
                                       modBase,
                                       typeName );
        } // end TryGetTypeFromName()


        // Note that this does not include synthetic types.
        public unsafe static SymbolInfo TryGetTypeFromName( IntPtr hProcess,
                                                            ulong modBase,
                                                            string typeName )
        {
            if( String.IsNullOrEmpty( typeName ) )
                throw new ArgumentException( "You must supply a type name", "typeName" );

            int maxNameLen = 2000; // MAX_SYM_NAME (characters, not bytes)
            IntPtr buf = Marshal.AllocHGlobal( Marshal.SizeOf( typeof( SYMBOL_INFO ) ) + (maxNameLen * 2) );
            try
            {
                SYMBOL_INFO* pNative = (SYMBOL_INFO*) buf;
                pNative->SizeOfStruct = (uint) Marshal.SizeOf( typeof( SYMBOL_INFO ) );
                pNative->MaxNameLen = (uint) maxNameLen; // Characters, not bytes!
                bool itWorked = NativeMethods.SymGetTypeFromName( hProcess, modBase, typeName, buf );
                if( !itWorked )
                {
                    //throw new DbgEngException( Marshal.GetLastWin32Error() );
                    return null;
                }

                return new SymbolInfo( hProcess, buf );
            }
            finally
            {
                Marshal.FreeHGlobal( buf );
            }
        } // end TryGetTypeFromName()


        //
        // SymSearch seems to work well for global-type stuff. It can return things that
        // SymEnumSymbols does not, such as constants. But it doesn't do so well for
        // locals--if you ask for SymSearchOptions.AllItems and give the name of a local,
        // it returns several results with the same name, but without an address/location.
        //
        // Not that you'd really want to use SymEnumSymbols for locals anyway. But another
        // place where SymEnumSymbols seems better than SymSearch is looking for "near"
        // symbols. So it seems one is not strictly a superset of the other, nor "better".
        //


        public static IEnumerable< SymbolInfo > SymSearch( WDebugClient debugClient,
                                                           ulong modBase,
                                                           string mask,
                                                           CancellationToken cancelToken )
        {
            return SymSearch( debugClient, modBase, mask, cancelToken, SymSearchOptions.GlobalsOnly );
        }

        public static IEnumerable< SymbolInfo > SymSearch( WDebugClient debugClient,
                                                           ulong modBase,
                                                           string mask,
                                                           CancellationToken cancelToken,
                                                           SymSearchOptions symSearchOptions )
        {
            return SymSearch( _GetHProcForDebugClient( debugClient ),
                              modBase,
                              mask,
                              cancelToken,
                              symSearchOptions );
        }

        public static IEnumerable< SymbolInfo > SymSearch( IntPtr hProcess,
                                                           ulong modBase,
                                                           string mask,
                                                           CancellationToken cancelToken )
        {
            return SymSearch( hProcess, modBase, mask, cancelToken, SymSearchOptions.GlobalsOnly );
        }

        public static IEnumerable< SymbolInfo > SymSearch( IntPtr hProcess,
                                                           ulong modBase,
                                                           string mask,
                                                           CancellationToken cancelToken,
                                                           SymSearchOptions symSearchOptions )
        {
            if( String.IsNullOrEmpty( mask ) )
                throw new ArgumentException( "You must supply a mask", "mask" );

            using( var bcHolder = new BlockingCollectionHolder< SymbolInfo >( cancelToken ) )
            {
                DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                    {
                        _EnumSymsWorker(
                            (callback) => NativeMethods.SymSearch( hProcess,
                                                                   modBase,
                                                                   0,            // index
                                                                   SymTag.Null,
                                                                   mask,
                                                                   0,            // address
                                                                   callback,
                                                                   hProcess,     // pUserContext
                                                                   symSearchOptions ),
                            bcHolder );
                    } );
                foreach( var si in bcHolder.GetConsumingEnumerable() )
                {
                    // Note that an exception could come flying out of the following line.
                    // Not because the cast will throw, but because the iterator state
                    // machine generated by the compiler means that external code gets
                    // executed "here".
                    yield return si;
                }
            }
        } // end SymSearch()


        public static IEnumerable< SymbolInfo > EnumSymbols( WDebugClient debugClient,
                                                             ulong modBase,
                                                             string mask,
                                                             CancellationToken cancelToken )
        {
            return EnumSymbols( debugClient, modBase, mask, cancelToken, SymEnumOptions.All );
        }

        public static IEnumerable< SymbolInfo > EnumSymbols( WDebugClient debugClient,
                                                             ulong modBase,
                                                             string mask,
                                                             CancellationToken cancelToken,
                                                             SymEnumOptions symEnumOptions )
        {
            return EnumSymbols( _GetHProcForDebugClient( debugClient ),
                                modBase,
                                mask,
                                cancelToken,
                                symEnumOptions );
        }

        public static IEnumerable< SymbolInfo > EnumSymbols( IntPtr hProcess,
                                                             ulong modBase,
                                                             string mask,
                                                             CancellationToken cancelToken )
        {
            return EnumSymbols( hProcess, modBase, mask, cancelToken, SymEnumOptions.All );
        }

        public static IEnumerable< SymbolInfo > EnumSymbols( IntPtr hProcess,
                                                             ulong modBase,
                                                             string mask,
                                                             CancellationToken cancelToken,
                                                             SymEnumOptions symEnumOptions )
        {
            if( String.IsNullOrEmpty( mask ) )
                throw new ArgumentException( "You must supply a mask", "mask" );

            using( var bcHolder = new BlockingCollectionHolder< SymbolInfo >( cancelToken ) )
            {
                DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                    {
                        _EnumSymsWorker(
                            (callback) => NativeMethods.SymEnumSymbolsEx( hProcess,
                                                                          modBase,
                                                                          mask,
                                                                          callback,
                                                                          hProcess, // pUserContext
                                                                          symEnumOptions ),
                            bcHolder );
                    } );
                foreach( var si in bcHolder.GetConsumingEnumerable() )
                {
                    // Note that an exception could come flying out of the following line.
                    // Not because the cast will throw, but because the iterator state
                    // machine generated by the compiler means that external code gets
                    // executed "here".
                    yield return si;
                }
            }
        } // end EnumSymbols()


        public static IEnumerable< SymbolInfo > EnumSymbolsForAddr( WDebugClient debugClient,
                                                                    ulong address,
                                                                    CancellationToken cancelToken )
        {
            return EnumSymbolsForAddr( _GetHProcForDebugClient( debugClient ),
                                       address,
                                       cancelToken );
        } // end EnumSymbols()


        public static IEnumerable< SymbolInfo > EnumSymbolsForAddr( IntPtr hProcess,
                                                                    ulong address,
                                                                    CancellationToken cancelToken )
        {
            using( var bcHolder = new BlockingCollectionHolder< SymbolInfo >( cancelToken ) )
            {
                DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                    {
                        _EnumSymsWorker(
                            (callback) => NativeMethods.SymEnumSymbolsForAddr( hProcess,
                                                                               address,
                                                                               callback,
                                                                               pUserContext: hProcess ),
                            bcHolder );
                    } );
                foreach( var si in bcHolder.GetConsumingEnumerable() )
                {
                    // Note that an exception could come flying out of the following line.
                    // Not because the cast will throw, but because the iterator state
                    // machine generated by the compiler means that external code gets
                    // executed "here".
                    yield return si;
                }
            }
        } // end EnumSymbolsForAddr()


        private static void _EnumSymsWorker( Func< native_SYM_ENUMERATESYMBOLS_CALLBACK, bool > nativeMethod,
                                             BlockingCollectionHolder< SymbolInfo > bcHolder )
        {
            var simh = new SYMBOL_INFO_MarshalHelper(
                        ( si ) =>
                        {
                            if( bcHolder.CancelToken.IsCancellationRequested )
                                return false;

                            bcHolder.Add( si );
                            return true;
                        } );
            bool itWorked = nativeMethod( simh.RealCallback );
            if( !itWorked && !bcHolder.CancelToken.IsCancellationRequested )
            {
                int err = Marshal.GetLastWin32Error();
                if( 0 == err )
                {
                    // This seems like it just means the enumeration is done. But
                    // it's strange, right? TODO: Perhaps I should file a bug. Repro:
                    //
                    //    New-TestApp -TestApp TestNativeConsoleApp -Attach -TargetName testApp
                    //    Get-DbgTypeInfo wchar_t
                    //
                    // Maybe my enumeration callback is supposed to set the last error
                    // before returning false?
                    bcHolder.CompleteAdding();
                }
                else
                {
                    if( err == NativeMethods.ERROR_INVALID_PARAMETER )
                    {
                        // A common reason for this is that we don't have symbols loaded.
                        // Unfortunately I don't have a good general way to check for
                        // that right here; we'd need to plumb through more data (namely
                        // what module we are operating against).
                    }

                    var e = new DbgEngException( err );
                    try { throw e; }
                    catch( Exception ) { }  // give it a stack
                    // This will stop an outstanding consuming enumerator.
                    bcHolder.SetException( e );
                }
            }
            else
            {
                bcHolder.CompleteAdding();
            }
            GC.KeepAlive( simh );
        } // end _EnumSymsWorker()


        // Return false to stop the enumeration.
        private delegate bool SymEnumerateSymbolsCallback( SymbolInfo symInfo );

        private class SYMBOL_INFO_MarshalHelper
        {
            public readonly native_SYM_ENUMERATESYMBOLS_CALLBACK RealCallback;
            private SymEnumerateSymbolsCallback m_userCallback;

            public SYMBOL_INFO_MarshalHelper( SymEnumerateSymbolsCallback callback )
            {
                if( null == callback )
                    throw new ArgumentNullException( "callback" );

                m_userCallback = callback;
                RealCallback = new native_SYM_ENUMERATESYMBOLS_CALLBACK( _RealCallback );
            } // end constructor

            private bool _RealCallback( /*SYMBOL_INFO*/ IntPtr symInfo,
                                                        uint symbolSize,
                                                        IntPtr pUserContext )
            {
                Util.Assert( IntPtr.Zero != pUserContext ); // should be the hProcess
                try
                {
                    return m_userCallback( new SymbolInfo( pUserContext, symInfo ) );
                }
                catch( Exception e )
                {
                    Util.FailFast( Util.Sprintf( "Unhandled exception from user SymEnumerateSymbolsCallback: {0}",
                                                 Util.GetExceptionMessages( e ) ),
                                   e );
                    return false; // should not execute; to satisfy the compiler
                }
            } // end _RealCallback()
        } // end class SYMBOL_INFO_MarshalHelper


        public static unsafe object SymGetTypeInfo( WDebugClient debugClient,
                                                    ulong modBase,
                                                    uint typeId,
                                                    IMAGEHLP_SYMBOL_TYPE_INFO getType )
        {
            IntPtr hProc = _GetHProcForDebugClient( debugClient );
            switch( getType )
            {
                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMINDEX:
                    uint symIndex = 0;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &symIndex );

                    return symIndex;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMNAME:
                    IntPtr psz = IntPtr.Zero;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &psz );

                    string s = Marshal.PtrToStringUni( psz );
                    Marshal.FreeHGlobal( psz );
                    return s;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMTAG:
                    SymTag tag;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &tag );
                    return tag;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_OFFSET:
                    uint offset;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &offset );
                    return offset;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LENGTH:
                    ulong len;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &len );
                    return len;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPE:
                    uint type;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &type );
                    return type;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_TYPEID:
                    uint tid;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &tid );
                    return tid;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_UDTKIND:
                    uint udtKind;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &udtKind );
                    return (UdtKind) udtKind;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_DATAKIND:
                    DataKind dataKind;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &dataKind );
                    return dataKind;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_COUNT:
                    uint count;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &count );
                    return count;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_LEXICALPARENT:
                    uint lexParent;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &lexParent );
                    return lexParent;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_ARRAYINDEXTYPEID:
                    uint arrayIdxTid;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &arrayIdxTid );
                    return arrayIdxTid;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_BASETYPE:
                    BasicType baseType;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &baseType );
                    return baseType;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CHILDRENCOUNT:
                    uint numChildren;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &numChildren );
                    return numChildren;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CLASSPARENTID:
                    uint classParentId;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &classParentId );
                    return classParentId;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_IS_REFERENCE:
                    // TODO: I have no idea if this is the right size; MSDN says "Boolean", not "BOOL"...
                    bool isRef;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &isRef );
                    return isRef;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_NESTED:
                    uint nested;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &nested );
                    return nested;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_FINDCHILDREN:
                    return FindTypeChildren( hProc, modBase, typeId );


                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_ADDRESS:
                    ulong addr;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &addr );
                    return addr;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_ADDRESSOFFSET:
                    uint addrOffset;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &addrOffset );
                    return addrOffset;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_BITPOSITION:
                    uint bitPos;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &bitPos );
                    return bitPos;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_CALLING_CONVENTION:
                    CallingConvention callConv;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &callConv );
                    return callConv;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_INDIRECTVIRTUALBASECLASS:
                    uint isIndirVirtBase;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &isIndirVirtBase );
                    return isIndirVirtBase != 0;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_THISADJUST:
                    uint thisAdjust;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &thisAdjust );
                    return thisAdjust;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VIRTUALBASECLASS:
                    uint virtBaseClass;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &virtBaseClass );
                    return virtBaseClass != 0;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VIRTUALBASEDISPINDEX:
                    uint virtBaseDispIndex;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &virtBaseDispIndex );
                    return virtBaseDispIndex;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VIRTUALBASEOFFSET:
                    uint virtBaseOffset;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &virtBaseOffset );
                    return virtBaseOffset;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VIRTUALBASEPOINTEROFFSET:
                    uint virtBasePointerOffset;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &virtBasePointerOffset );
                    return virtBasePointerOffset;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VIRTUALTABLESHAPEID:
                    uint shapeId;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &shapeId );
                    return shapeId;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GTIEX_REQS_VALID:
                    ulong gtiexReqsValid;
                    _SymGetTypeInfo( hProc, modBase, typeId, getType, (void*) &gtiexReqsValid );
                    return gtiexReqsValid;

                case IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_VALUE:
                    byte* pvt = stackalloc byte[ 24 ];
                    IntPtr pvtIntPtr = new IntPtr( pvt );
                    Marshal.GetNativeVariantForObject( null, pvtIntPtr ); // I'm hoping this gets me something like VT_EMPTY

                    _SymGetTypeInfo( hProc, modBase, typeId, getType, pvt );

                    return Marshal.GetObjectForNativeVariant( pvtIntPtr );

                default:
                    throw new NotSupportedException( Util.Sprintf( "Not yet supported: {0}", getType ) );
            }
        } // end SymGetTypeInfo()


        /// <summary>
        ///    Checks if two types are equivalent. Unfortunately not very useful. Here is
        ///    a comment from the dbghelp code explaining:
        ///
        ///       XXX ----- - Right now symsAreEquiv apparently takes
        ///       some context information into account so types which
        ///       really are equivalent end up looking different.
        ///       ------ has said he's going to fix this, but for now
        ///       allow TI_IS_CLOSE_EQUIV_TO which does a name comparison
        ///       to achieve a similar effect.
        /// </summary>
        public static unsafe bool SymGetTypeIsEquivTo( WDebugClient debugClient,
                                                       ulong modBase,
                                                       uint typeId,
                                                       uint otherTypeId )
        {
            IntPtr hProc = _GetHProcForDebugClient( debugClient );

            if( _IsSyntheticTypeId( typeId ) || _IsSyntheticTypeId( otherTypeId ) )
            {
                Util.Fail( "Do we need to handle this?" );
                return false;
            }

            void* pInfo = &otherTypeId;
            bool bResult = DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                {
                    return NativeMethods.SymGetTypeInfo( hProc,
                                                         modBase,
                                                         typeId,
                                                         IMAGEHLP_SYMBOL_TYPE_INFO.TI_IS_EQUIV_TO,
                                                         pInfo );
                } );

            // This is confusing, because the documentation for TI_IS_EQUIV_TO says that
            // the return value is S_OK (0) if the types are equivalent, and S_FALSE (1)
            // if not. But the dbghelp wrapper around the DIA call returns "err==S_OK",
            // which flips the meaning (not to mention hides errors).
            return bResult;
        } // end SymGetTypeIsEquivTo()


        public static unsafe SymbolInfo SymFromIndex( WDebugClient debugClient,
                                                      ulong modBase,
                                                      uint symIndex )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                SymFromIndex_naked( debugClient,
                                    modBase,
                                    symIndex ) );
        }

        private static unsafe SymbolInfo SymFromIndex_naked( WDebugClient debugClient,
                                                             ulong modBase,
                                                             uint symIndex )
        {
            int maxNameLen = 2000; // MAX_SYM_NAME (characters, not bytes)
            IntPtr hProc = _GetHProcForDebugClient( debugClient );
            IntPtr buf = Marshal.AllocHGlobal( Marshal.SizeOf( typeof( SYMBOL_INFO ) ) + (maxNameLen * 2) );
            try
            {
                SYMBOL_INFO* pNative = (SYMBOL_INFO*) buf;
                pNative->SizeOfStruct = (uint) Marshal.SizeOf( typeof( SYMBOL_INFO ) );
                pNative->MaxNameLen = (uint) maxNameLen; // Characters, not bytes!
                bool itWorked = NativeMethods.SymFromIndex( hProc, modBase, symIndex, buf );
                if( !itWorked )
                    throw new DbgEngException( Marshal.GetLastWin32Error() );

                return new SymbolInfo( hProc, buf );
            }
            finally
            {
                Marshal.FreeHGlobal( buf );
            }
        } // end SymFromIndex_naked()


        public static unsafe SymbolInfo SymFromToken( WDebugClient debugClient,
                                                      ulong modBase,
                                                      uint token )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                SymFromToken_naked( debugClient,
                                    modBase,
                                    token ) );
        }

        private static unsafe SymbolInfo SymFromToken_naked( WDebugClient debugClient,
                                                             ulong modBase,
                                                             uint token )
        {
            int maxNameLen = 2000; // MAX_SYM_NAME (characters, not bytes)
            IntPtr hProc = _GetHProcForDebugClient( debugClient );
            IntPtr buf = Marshal.AllocHGlobal( Marshal.SizeOf( typeof( SYMBOL_INFO ) ) + (maxNameLen * 2) );
            try
            {
                SYMBOL_INFO* pNative = (SYMBOL_INFO*) buf;
                pNative->SizeOfStruct = (uint) Marshal.SizeOf( typeof( SYMBOL_INFO ) );
                pNative->MaxNameLen = (uint) maxNameLen; // Characters, not bytes!
                bool itWorked = NativeMethods.SymFromToken( hProc, modBase, token, buf );
                if( !itWorked )
                    throw new DbgEngException( Marshal.GetLastWin32Error() );

                return new SymbolInfo( hProc, buf );
            }
            finally
            {
                Marshal.FreeHGlobal( buf );
            }
        } // end SymFromToken_naked()


        public static unsafe SymbolInfo SymFromAddr( WDebugClient debugClient,
                                                     ulong address,
                                                     out ulong displacement )
        {
            ulong tmpDisplacement = 0;

            var si = DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                SymFromAddr_naked( debugClient,
                                   address,
                                   out tmpDisplacement ) );

            displacement = tmpDisplacement;
            return si;
        }

        private static unsafe SymbolInfo SymFromAddr_naked( WDebugClient debugClient,
                                                            ulong address,
                                                            out ulong displacement )
        {
            int maxNameLen = 2000; // MAX_SYM_NAME (characters, not bytes)
            IntPtr hProc = _GetHProcForDebugClient( debugClient );
            IntPtr buf = Marshal.AllocHGlobal( Marshal.SizeOf( typeof( SYMBOL_INFO ) ) + (maxNameLen * 2) );
            try
            {
                SYMBOL_INFO* pNative = (SYMBOL_INFO*) buf;
                pNative->SizeOfStruct = (uint) Marshal.SizeOf( typeof( SYMBOL_INFO ) );
                pNative->MaxNameLen = (uint) maxNameLen; // Characters, not bytes!
                bool itWorked = NativeMethods.SymFromAddr( hProc, address, out displacement, buf );
                if( !itWorked )
                    throw new DbgEngException( Marshal.GetLastWin32Error() );

                return new SymbolInfo( hProc, buf );
            }
            finally
            {
                Marshal.FreeHGlobal( buf );
            }
        } // end SymFromAddr_naked()


        public static unsafe SymbolInfo SymFromInlineContext( WDebugClient debugClient,
                                                              ulong address,
                                                              uint inlineContext,
                                                              out ulong displacement )
        {
            ulong tmpDisplacement = 0;
            var ret = DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                {
                    return SymFromInlineContext_naked( debugClient,
                                                       address,
                                                       inlineContext,
                                                       out tmpDisplacement );
                } );
            displacement = tmpDisplacement;
            return ret;
        }

        private static unsafe SymbolInfo SymFromInlineContext_naked( WDebugClient debugClient,
                                                                     ulong address,
                                                                     uint inlineContext,
                                                                     out ulong displacement )
        {
            int maxNameLen = 2000; // MAX_SYM_NAME (characters, not bytes)
            IntPtr hProc = _GetHProcForDebugClient( debugClient );
            IntPtr buf = Marshal.AllocHGlobal( Marshal.SizeOf( typeof( SYMBOL_INFO ) ) + (maxNameLen * 2) );
            try
            {
                SYMBOL_INFO* pNative = (SYMBOL_INFO*) buf;
                pNative->SizeOfStruct = (uint) Marshal.SizeOf( typeof( SYMBOL_INFO ) );
                pNative->MaxNameLen = (uint) maxNameLen; // Characters, not bytes!
                bool itWorked = NativeMethods.SymFromInlineContext( hProc,
                                                                    address,
                                                                    inlineContext,
                                                                    out displacement,
                                                                    buf );
                if( !itWorked )
                    throw new DbgEngException( Marshal.GetLastWin32Error() );

                return new SymbolInfo( hProc, buf );
            }
            finally
            {
                Marshal.FreeHGlobal( buf );
            }
        } // end SymFromInlineContext_naked()

        public static int SymLoadModule( WDebugClient debugClient, ulong moduleBaseAddress )
        {
            IntPtr hProc = _GetHProcForDebugClient( debugClient );
            if( 0 == NativeMethods.SymLoadModuleEx( hProc, default, null, null, moduleBaseAddress, 0, default, 0 ) )
            {
                // 0 means no new module was loaded... but you don't know if that's because it was already loaded without checking the last error.
                var err = Marshal.GetLastWin32Error();
                if( err != 0 )
                {
                    return (err | unchecked( (int) 0x80070000 ));
                }
            }
            return 0;
        }
    } // end class DbgHelp


    // TODO: Rationalize with DEBUG_SYMTYPE in ClrMd?
    internal enum SYM_TYPE : uint
    {
        None     = 0,
        Coff     = 1,
        Cv       = 2,
        Pdb      = 3,
        Export   = 4,
        Deferred = 5,
        Sym      = 6,
        Dia      = 7,
        Virtual  = 8,
    }

    // TODO: Rationalize with IMAGEHLP_MODULE64 in ClrMd
    [StructLayout( LayoutKind.Sequential )]
    internal unsafe struct IMAGEHLP_MODULEW64
    {
        public         uint SizeOfStruct;           // set to sizeof(IMAGEHLP_MODULE64)
        public        ulong BaseOfImage;            // base load address of module
        public         uint ImageSize;              // virtual size of the loaded module
        public         uint TimeDateStamp;          // date/time stamp from pe header
        public         uint CheckSum;               // checksum from the pe header
        public         uint NumSyms;                // number of symbols in the symbol table
        public     SYM_TYPE SymType;                // type of symbols loaded
        public fixed ushort ModuleName[ 32 ];       // module name
        public fixed ushort ImageName[ 256 ];       // image name
        public fixed ushort LoadedImageName[ 256 ]; // symbol file name
        public fixed ushort LoadedPdbName[ 256 ];   // pdb file name
        public         uint CVSig;                  // Signature of the CV record in the debug directories
        public fixed ushort CVData[ /*MAX_PATH*/ 260 * 3 ]; // Contents of the CV record
        public         uint PdbSig;                 // Signature of PDB
        //public        Guid PdbSig70;               // Signature of PDB (VC 7 and up)
        public   fixed byte PdbSig70[ 16 ];         // Signature of PDB (VC 7 and up)
        public         uint PdbAge;                 // DBI age of pdb
        public /*BOOL*/ int PdbUnmatched;           // loaded an unmatched pdb
        public /*BOOL*/ int DbgUnmatched;           // loaded an unmatched dbg
        public /*BOOL*/ int LineNumbers;            // we have line number information
        public /*BOOL*/ int GlobalSymbols;          // we have internal symbol information
        public /*BOOL*/ int TypeInfo;               // we have type information
        public /*BOOL*/ int SourceIndexed;          // pdb supports source server
        public /*BOOL*/ int Publics;                // contains public symbols
        public         uint MachineType;            // IMAGE_FILE_MACHINE_XXX from ntimage.h and winnt.h
        public         uint Reserved;               // Padding - don't remove.
    } // end struct IMAGEHLP_MODULEW64


    //internal enum IMAGEHLP_SYMBOL_TYPE_INFO : uint
    public enum IMAGEHLP_SYMBOL_TYPE_INFO : uint
    {
        /// <summary>
        ///    The symbol tag. The data type is DWORD.
        /// </summary>
        TI_GET_SYMTAG,
        /// <summary>
        ///    The symbol name. The data type is WCHAR*. The caller must free the buffer.
        /// </summary>
        TI_GET_SYMNAME,
        /// <summary>
        ///    The length of the type. The data type is ULONG64.
        /// </summary>
        TI_GET_LENGTH,
        /// <summary>
        ///    The type. The data type is DWORD.
        /// </summary>
        TI_GET_TYPE,
        /// <summary>
        ///    The type index. The data type is DWORD.
        /// </summary>
        TI_GET_TYPEID,
        /// <summary>
        ///    The base type for the type index. The data type is DWORD.
        /// </summary>
        TI_GET_BASETYPE,
        /// <summary>
        ///    The type index for index of an array type. The data type is DWORD.
        /// </summary>
        TI_GET_ARRAYINDEXTYPEID,
        /// <summary>
        ///    The type index of all children. The data type is a pointer to a
        ///    TI_FINDCHILDREN_PARAMS structure. The Count member should be initialized
        ///    with the number of children.
        /// </summary>
        TI_FINDCHILDREN,
        /// <summary>
        ///    The data kind. The data type is DWORD.
        /// </summary>
        TI_GET_DATAKIND,
        /// <summary>
        ///    The address offset. The data type is DWORD.
        /// </summary>
        TI_GET_ADDRESSOFFSET,
        /// <summary>
        ///    The offset of the type in the parent. Members can use this to get their
        ///    offset in a structure. The data type is DWORD.
        /// </summary>
        TI_GET_OFFSET,
        /// <summary>
        ///    The value of a constant or enumeration value. The data type is VARIANT.
        /// </summary>
        TI_GET_VALUE,
        /// <summary>
        ///    The count of array elements. The data type is DWORD.
        /// </summary>
        TI_GET_COUNT,
        /// <summary>
        ///    The number of children. The data type is DWORD.
        /// </summary>
        TI_GET_CHILDRENCOUNT,
        /// <summary>
        ///    The bit position of a bitfield. The data type is DWORD.
        /// </summary>
        TI_GET_BITPOSITION,
        /// <summary>
        ///    A value that indicates whether the base class is virtually inherited. The
        ///    data type is BOOL.
        /// </summary>
        TI_GET_VIRTUALBASECLASS,
        /// <summary>
        ///    The symbol interface of the type of virtual table, for a user-defined type. The data type is DWORD.
        /// </summary>
        TI_GET_VIRTUALTABLESHAPEID,
        /// <summary>
        ///    The offset of the virtual base pointer. The data type is DWORD.
        /// </summary>
        TI_GET_VIRTUALBASEPOINTEROFFSET,
        /// <summary>
        ///    The type index of the class parent. The data type is DWORD.
        /// </summary>
        TI_GET_CLASSPARENTID,
        /// <summary>
        ///    A value that indicates whether the type index is nested. The data type is
        ///    DWORD.
        /// </summary>
        TI_GET_NESTED,
        /// <summary>
        ///    The symbol index for a type. The data type is DWORD.
        /// </summary>
        TI_GET_SYMINDEX,
        /// <summary>
        ///    The lexical parent of the type. The data type is DWORD.
        /// </summary>
        TI_GET_LEXICALPARENT,
        /// <summary>
        ///    The index address. The data type is ULONG64.
        /// </summary>
        TI_GET_ADDRESS,
        /// <summary>
        ///    The offset from the this pointer to its actual value. The data type is
        ///    DWORD.
        /// </summary>
        TI_GET_THISADJUST,
        /// <summary>
        ///    The UDT kind. The data type is DWORD.
        /// </summary>
        TI_GET_UDTKIND,
        /// <summary>
        ///    The equivalency of two types. The data type is DWORD. The value is S_OK is
        ///    the two types are equivalent, and S_FALSE otherwise.
        /// </summary>
        TI_IS_EQUIV_TO,
        /// <summary>
        ///    The calling convention. The data type is DWORD.
        /// </summary>
        TI_GET_CALLING_CONVENTION,
        /// <summary>
        ///    The equivalency of two symbols. This is not guaranteed to be accurate. The
        ///    data type is DWORD. The value is S_OK is the two types are equivalent, and
        ///    S_FALSE otherwise.
        /// </summary>
        TI_IS_CLOSE_EQUIV_TO,
        /// <summary>
        ///    The element where the valid request bitfield should be stored. The data
        ///    type is ULONG64.
        ///
        ///    This value is only used with the SymGetTypeInfoEx function.
        /// </summary>
        TI_GTIEX_REQS_VALID,
        /// <summary>
        ///    The offset in the virtual function table of a virtual function. The data
        ///    type is DWORD.
        /// </summary>
        TI_GET_VIRTUALBASEOFFSET,
        /// <summary>
        ///    The index into the virtual base displacement table. The data type is DWORD.
        /// </summary>
        TI_GET_VIRTUALBASEDISPINDEX,
        /// <summary>
        ///    Indicates whether a pointer type is a reference. The data type is Boolean.
        /// </summary>
        TI_GET_IS_REFERENCE,
        /// <summary>
        ///    Indicates whether the user-defined data type is an indirect virtual base.
        ///    The data type is BOOL.
        /// </summary>
        TI_GET_INDIRECTVIRTUALBASECLASS,
        IMAGEHLP_SYMBOL_TYPE_INFO_MAX,
    } // end enum IMAGEHLP_SYMBOL_TYPE_INFO



    // Defined in cvconst.h (https://github.com/Microsoft/microsoft-pdb/blob/master/include/cvconst.h)
    public enum BasicType
    {
        btNoType = 0,
        btVoid = 1,
        btChar = 2,
        btWChar = 3,
        btInt = 6,
        btUInt = 7,
        btFloat = 8,
        btBCD = 9,
        btBool = 10,
        btLong = 13,
        btULong = 14,
        btCurrency = 25,
        btDate = 26,
        btVariant = 27,
        btComplex = 28,
        btBit = 29,
        btBSTR = 30,
        btHresult = 31,
        btChar16 = 32,  // char16_t
        btChar32 = 33   // char32_t
    }


    [StructLayout( LayoutKind.Sequential )]
    internal unsafe struct SYMBOL_INFO
    {
        public        uint SizeOfStruct;
        public        uint TypeIndex;       // Type Index of symbol
        public fixed ulong Reserved[ 2 ];
        public        uint Index;
        public        uint Size;
        public       ulong ModBase;         // Base Address of module comtaining this symbol
        public        uint Flags;
        public       ulong Value;           // Value of symbol, ValuePresent should be 1
        public       ulong Address;         // Address of symbol including base address of module
        public        uint Register;        // register holding value or pointer to value
        public        uint Scope;           // scope of the symbol
        public        uint Tag;             // pdb classification
        public        uint NameLen;         // Actual length of name
        public        uint MaxNameLen;
        public fixed  char Name[ 1 ];       // Name of symbol
    } // end struct SYMBOL_INFO


    // Managed version of SYMFLAG_* values
    [Flags]
    //internal enum SymbolInfoFlags : uint
    public enum SymbolInfoFlags : uint
    {
        ValuePresent      = 0x00000001,
        Register          = 0x00000008,
        RegRel            = 0x00000010,
        FrameRel          = 0x00000020,
        Parameter         = 0x00000040,
        Local             = 0x00000080,
        Constant          = 0x00000100,
        Export            = 0x00000200,
        Forwarder         = 0x00000400,
        Function          = 0x00000800,
        Virtual           = 0x00001000,
        Thunk             = 0x00002000,
        TlsRel            = 0x00004000,
        Slot              = 0x00008000,
        ILRel             = 0x00010000,
        Metadata          = 0x00020000,
        ClrToken          = 0x00040000,
        NULL              = 0x00080000,
        FuncNoReturn      = 0x00100000,
        SyntheticZerobase = 0x00200000,
        PublicCode        = 0x00400000,
    } // end enum SymbolInfoFlags


    // Managed version of SYMBOL_INFO
    //internal class SymbolInfo
    public class SymbolInfo
    {
        public readonly            uint  TypeIndex;   // Type Index of symbol
        public readonly            uint  Index;
        public readonly            uint  Size;
        public readonly           ulong  ModBase;     // Base Address of module containing this symbol
        public readonly SymbolInfoFlags  Flags;
        public readonly           ulong  Value;       // Value of symbol, ValuePresent should be 1
        /// <summary>
        /// Address of symbol including base address of module
        /// </summary>
        public readonly           ulong  Address;
        public readonly            uint  Register;    // register holding value or pointer to value
        public readonly            uint  Scope;       // scope of the symbol
        public readonly          SymTag  Tag;         // pdb classification
        public readonly          string  Name;

        // This should be constructed on the dbgeng thread, because it might call back
        // into dbghelp.
        internal SymbolInfo( IntPtr hProcess, IntPtr pNative )
        {
            SYMBOL_INFO native = (SYMBOL_INFO) Marshal.PtrToStructure( pNative, typeof( SYMBOL_INFO ) );

            if( (native.ModBase == 0) && (native.Address != 0) )
            {
                // This is a workaround for when we're handed a SYMBOL_INFO with a null
                // ModBase but a non-zero Address--we'll call back in to
                // SymGetModuleBase64 to fill in the ModBase. I've seen this happen in
                // no-symbol situations.
                LogManager.Trace( "*grumble* Fixing up a SYMBOL_INFO that has an Address but no ModBase. ({0:x})",
                                  native.Address );

                ulong actualModBase = NativeMethods.SymGetModuleBase64( hProcess,
                                                                        native.Address );

                if( 0 == actualModBase )
                {
                    LogManager.Trace( "Huh... couldn't get the modbase; error {0}",
                                      Util.FormatErrorCode( Marshal.GetLastWin32Error() ) );
                }
                else
                {
                    LogManager.Trace( "Actual modBase: {0:x}", actualModBase );
                }

                native.ModBase = actualModBase;
            }

            TypeIndex = native.TypeIndex;
            Index     = native.Index;
            Size      = native.Size;
            ModBase   = native.ModBase;
            Flags     = (SymbolInfoFlags) native.Flags;
            Value     = native.Value;
            Address   = native.Address;
            Register  = native.Register;
            Scope     = native.Scope;
            Tag       = (SymTag) native.Tag;

            Util.Assert( native.NameLen > 0 ); // all symbols have names... right?

            pNative += 0x54;
            while( (native.NameLen > 0) &&
                   (0 == Marshal.ReadInt16( pNative + ((int) (native.NameLen - 1) * sizeof( char )) )) )
            {
                native.NameLen = native.NameLen - 1; // don't pull in trailing NULLs
            }

            Name = Marshal.PtrToStringUni( pNative, (int) native.NameLen );
        } // end constructor
    } // end class SymbolInfo


    internal enum IMAGEHLP_GET_TYPE_INFO_FLAGS : uint
    {
        /// <summary>
        ///    Do not cache data for later retrievals. It is good to use this flag if you will not be
        ///    requesting the information again.
        /// </summary>
        UNCACHED = 0x00000001,
        /// <summary>
        ///    Retrieve information about the children of the specified types, not the types themselves.
        /// </summary>
        CHILDREN = 0x00000002
    }

    [StructLayout( LayoutKind.Sequential )]
    internal unsafe class IMAGEHLP_GET_TYPE_INFO_PARAMS
    {
        public /*IN */ uint    SizeOfStruct;
        public /*IN */ IMAGEHLP_GET_TYPE_INFO_FLAGS Flags;
        /// <summary>
        ///    The number of elements specified in the TypeIds array.
        /// </summary>
        public /*IN */ uint    NumIds;
        /// <summary>
        ///    An array of type indexes.
        /// </summary>
        public /*IN */ uint*   TypeIds;
        /// <summary>
        ///    The filter for return values. For example, set this member to 1 &lt;&lt; SymTagData
        ///    to return only results with a symbol tag of SymTagData. For a list of tags, see the
        ///    SymTagEnum type defined in Dbghelp.h
        /// </summary>
        public /*IN */ ulong   TagFilter;
        /// <summary>
        ///    The number of elements specified in the arrays specified in the ReqKinds, ReqOffsets,
        ///    and ReqSizes members. These arrays must be the same size.
        /// </summary>
        public /*IN */ uint    NumReqs;
        /// <summary>
        ///    An array of information types to be requested. Each element is one of the enumeration
        ///    values in the IMAGEHLP_SYMBOL_TYPE_INFO enumeration type.
        /// </summary>
        public /*IN */ IMAGEHLP_SYMBOL_TYPE_INFO* ReqKinds;
        /// <summary>
        ///    An array of offsets that specify where to store the data for each request within each
        ///    element of Buffer array.
        /// </summary>
        public /*IN */ SIZE_T*  ReqOffsets;
        /// <summary>
        ///    The size of each data request, in bytes. The required sizes are described in the docs
        ///    for IMAGEHLP_SYMBOL_TYPE_INFO.
        /// </summary>
        public /*IN */ uint*   ReqSizes;
        /// <summary>
        ///    The number of bytes for each element in the Buffer array.
        /// </summary>
        public /*IN */ SIZE_T  ReqStride;
        /// <summary>
        ///    The size of the Buffer array, in bytes.
        /// </summary>
        public /*IN */ SIZE_T  BufferSize;

        /// <summary>
        ///    An array of records used for storing query results. Each record is separated by
        ///    ReqStride bytes. Each type for which data is to be retrieved requires one record in
        ///    the array. Within each record, there are NumReqs pieces of data stored as the result
        ///    of individual queries. The data is stored within the record according to the offsets
        ///    specified in ReqOffsets. The format of the data depends on the value of the ReqKinds
        ///    member in use.
        /// </summary>
        public /*OUT*/ void*   Buffer;
        /// <summary>
        ///    The number of type entries that match the filter.
        /// </summary>
        public /*OUT*/ uint    EntriesMatched;
        /// <summary>
        ///    The number of elements in the Buffer array that received results.
        /// </summary>
        public /*OUT*/ uint    EntriesFilled;
        /// <summary>
        ///    A bitmask indicating all tag bits encountered during the search operation.
        /// </summary>
        public /*OUT*/ ulong   TagsFound;
        /// <summary>
        ///    A bitmask indicate the bit-wise AND of all ReqsValid fields.
        /// </summary>
        public /*OUT*/ ulong   AllReqsValid;
        /// <summary>
        ///    The size of ReqsValid, in elements.
        /// </summary>
        public /*IN */ uint    NumReqsValid;
        /// <summary>
        ///    A bitmask indexed by Buffer element index that indicates which request data is valid.
        ///    This member can be NULL.
        /// </summary>
        public /*OUT*/ ulong*  ReqsValid; //OPTIONAL

        public IMAGEHLP_GET_TYPE_INFO_PARAMS()
        {
            SizeOfStruct = (uint) Marshal.SizeOf( typeof( IMAGEHLP_GET_TYPE_INFO_PARAMS ) );
        }
    } // end class IMAGEHLP_GET_TYPE_INFO_PARAMS


    public enum UdtKind : uint
    {
       UdtStruct,
       UdtClass,
       UdtUnion,
       UdtInterface,
    }

    public enum DataKind : uint
    {
        Unknown,
        Local,
        StaticLocal,
        Param,
        ObjectPtr, // e.g. 'this'
        FileStatic,
        Global,
        Member,
        StaticMember,
        Constant
    }

    // from cvconst.h: CV_call_e enumeration
    public enum CallingConvention : uint
    {
        NEAR_C      = 0x00, // near right to left push, caller pops stack
        FAR_C       = 0x01, // far right to left push, caller pops stack
        NEAR_PASCAL = 0x02, // near left to right push, callee pops stack
        FAR_PASCAL  = 0x03, // far left to right push, callee pops stack
        NEAR_FAST   = 0x04, // near left to right push with regs, callee pops stack
        FAR_FAST    = 0x05, // far left to right push with regs, callee pops stack
        SKIPPED     = 0x06, // skipped (unused) call index
        NEAR_STD    = 0x07, // near standard call
        FAR_STD     = 0x08, // far standard call
        NEAR_SYS    = 0x09, // near sys call
        FAR_SYS     = 0x0a, // far sys call
        THISCALL    = 0x0b, // this call (this passed in register)
        MIPSCALL    = 0x0c, // Mips call
        GENERIC     = 0x0d, // Generic call sequence
        ALPHACALL   = 0x0e, // Alpha call
        PPCCALL     = 0x0f, // PPC call
        SHCALL      = 0x10, // Hitachi SuperH call
        ARMCALL     = 0x11, // ARM call
        AM33CALL    = 0x12, // AM33 call
        TRICALL     = 0x13, // TriCore Call
        SH5CALL     = 0x14, // Hitachi SuperH-5 call
        M32RCALL    = 0x15, // M32R Call
        CLRCALL     = 0x16, // clr call
        INLINE      = 0x17, // Marker for routines always inlined and thus lacking a convention
        RESERVED    = 0x18  // first unused call enumeration
    }

    public class RawUdtInfo
    {
        public readonly string SymName;
        public readonly UdtKind UdtKind;
        public readonly ulong Size;
        public readonly uint ChildrenCount;
        public readonly uint ClassParentId;
        public readonly uint VirtualTableShapeId;

        internal RawUdtInfo( DbgHelp.UdtInfoRequest uir )
        {
            SymName             = Marshal.PtrToStringUni( uir.SymName );
            UdtKind             = uir.UdtKind;
            Size                = uir.Size;
            ChildrenCount       = uir.ChildrenCount;
            ClassParentId       = uir.ClassParentId;
            VirtualTableShapeId = uir.VirtualTableShapeId;
        } // end constructor
    } // end class RawUdtInfo


    public class RawDataInfo
    {
        public readonly string SymName;
        public readonly DataKind DataKind;
        public readonly uint TypeId;
        public readonly uint ClassParentId;
        public readonly uint Offset;
        public readonly uint AddressOffset;
        public readonly ulong Address;
        public readonly uint BitfieldLength;  // only valid for bitfields
        public readonly uint BitPosition;     // only valid for bitfields (if BitfieldLength is non-zero)
        public readonly object Value;         // only valid for e.g. static data members

        internal unsafe RawDataInfo( DbgHelp.DataInfoRequest dir, bool valueFieldIsValid )
        {
            SymName        = Marshal.PtrToStringUni( dir.SymName );
            DataKind       = dir.DataKind;
            TypeId         = dir.TypeId;
            ClassParentId  = dir.ClassParentId;
            Offset         = dir.Offset;
            AddressOffset  = dir.AddressOffset;
            Address        = dir.Address;
            BitfieldLength = (uint) dir.Length;
            BitPosition    = dir.BitPosition;

            if( valueFieldIsValid )
            {
                Value      = Marshal.GetObjectForNativeVariant( new IntPtr( dir.Value ) );
            }
        } // end constructor
    } // end class RawDataInfo


    public class RawFuncArgTypeInfo
    {
        public readonly uint TypeId;
        public readonly uint ArgTypeId;
        public readonly uint ChildrenCount; // I don't know what this is for...

        internal RawFuncArgTypeInfo( DbgHelp.FuncArgTypeInfoRequest fatir )
        {
            TypeId = fatir.SymIndex;
            ArgTypeId = fatir.ArgTypeId;
            ChildrenCount = fatir.ChildrenCount;
        } // end constructor
    } // end class RawFuncArgTypeInfo


    public class RawFuncTypeInfo
    {
        public readonly uint ReturnTypeId;
        public readonly uint ChildrenCount;
        public readonly uint ClassParentId;
        public readonly uint Count;
        public readonly uint ThisAdjust; // Only valid if ClassParentId is nonzero
        public readonly CallingConvention CallingConvention;
        public readonly IReadOnlyList< RawFuncArgTypeInfo > Arguments;

        internal RawFuncTypeInfo( DbgHelp.FuncTypeInfoRequest ftir,
                                  IReadOnlyList< RawFuncArgTypeInfo > arguments )
        {
            ReturnTypeId      = ftir.ReturnTypeId;
            ChildrenCount     = ftir.ChildrenCount;
            ClassParentId     = ftir.ClassParentId;
            Count             = ftir.Count;
            ThisAdjust        = ftir.ThisAdjust;
            CallingConvention = ftir.CallingConvention;
            Arguments         = arguments;
        } // end constructor
    } // end class RawFuncTypeInfo


    public class RawBaseClassInfo
    {
        //public readonly uint TypeId;
        public readonly string BaseClassTypeName;
        public readonly uint BaseClassTypeId;
        public readonly ulong BaseClassSize;
        public readonly uint Offset;
        public readonly UdtKind UdtKind;
        public readonly uint ChildrenCount;
        public readonly bool IsNested;
        public readonly uint ClassParentId; // This seems to be the derived class that this is a base for?
        public readonly uint VirtualTableShapeId;
        public readonly uint VirtualBaseDispIndex;
        public readonly uint VirtualBaseOffset;
        public readonly uint VirtualBasePointerOffset;
        public readonly bool IsVirtualBaseClass;
        public readonly bool IsIndirectVirtualBaseClass;

        internal RawBaseClassInfo( /*uint bcTypeId,*/ DbgHelp.BaseClassInfoRequest bcir )
        {
            //TypeId                     = bcTypeId;
            BaseClassTypeName          = Marshal.PtrToStringUni( bcir.SymName );
            BaseClassTypeId            = bcir.BaseTypeId;
            BaseClassSize              = bcir.Size;
            Offset                     = bcir.Offset;
            UdtKind                    = bcir.UdtKind;
            ChildrenCount              = bcir.ChildrenCount;
            IsNested                   = bcir.IsNested != 0;
            ClassParentId              = bcir.ClassParentId;
            VirtualTableShapeId        = bcir.VirtualTableShapeId;
            VirtualBaseDispIndex       = bcir.VirtualBaseDispIndex;
            VirtualBaseOffset          = bcir.VirtualBaseOffset;
            VirtualBasePointerOffset   = bcir.VirtualBasePointerOffset;
            IsVirtualBaseClass         = bcir.IsVirtualBaseClass != 0;
            IsIndirectVirtualBaseClass = bcir.IsIndirectVirtualBaseClass != 0;
        } // end constructor
    } // end RawBaseClassInfo


    // Contrast with RawFuncTypeInfo.
    public class RawFuncInfo
    {
        public readonly uint TypeId;
        public readonly string FuncName;
        public readonly uint FunctionTypeTypeId;
        public readonly uint OwningClassTypeId;
        public readonly uint ChildrenCount; // I don't know what the children are, actually
        public readonly ulong Address;      // Could be 0, like for pure virtual functions.
        public readonly ulong Length;       // Could be 0, like for pure virtual functions.
        public readonly uint VirtualBaseOffset;
        public readonly uint SymIndex; // I don't know what this is! Normally SymIndex is
                                       // the type Id of the thing itself, but it seems in
                                       // this case it is not.

        internal RawFuncInfo( uint typeId, DbgHelp.FuncInfoRequest fir )
        {
            TypeId                     = typeId;
            FuncName                   = Marshal.PtrToStringUni( fir.SymName );
            FunctionTypeTypeId         = fir.FuncTypeTypeId;
            OwningClassTypeId          = fir.ClassParentId;
            ChildrenCount              = fir.ChildrenCount;
            Address                    = fir.Address;
            Length                     = fir.Length;
            VirtualBaseOffset          = fir.VirtualBaseOffset;
            SymIndex                   = fir.SymIndex;
        } // end constructor
    } // end RawFuncInfo


    public class RawVTableInfo
    {
        public readonly uint TypeId;
        public readonly uint PointerTypeId;
        public readonly uint OwningClassTypeId;
        public readonly uint ChildrenCount; // I don't know what the children are, actually
        public readonly uint Offset;

        internal RawVTableInfo( uint typeId, DbgHelp.VTableInfoRequest vir )
        {
            TypeId            = typeId;
            PointerTypeId     = vir.PointerTypeId;
            OwningClassTypeId = vir.ClassParentId;
            ChildrenCount     = vir.ChildrenCount;
            Util.Assert( 0 == vir.ChildrenCount ); // So I can find out what are the children of a VTable.
            Offset            = vir.Offset;
        } // end constructor
    } // end RawVTableInfo


    public class RawVTableShapeInfo
    {
        public readonly uint TypeId;
        public readonly uint NumSlots;

        internal RawVTableShapeInfo( uint typeId, DbgHelp.VTableShapeInfoRequest vsir )
        {
            TypeId   = typeId;
            NumSlots = vsir.Count;
            Util.Assert( 0 == vsir.ChildrenCount ); // If I ever see a VTableShape with
                                                    // children I'd like to know. Maybe
                                                    // they are interesting/useful.
        } // end constructor
    } // end RawVTableShapeInfo


    public class RawEnumInfo
    {
        public readonly uint TypeId;
        public readonly string Name;
        public readonly uint NumEnumerands;
        public readonly ulong Size;
        public readonly uint BaseTypeTypeId;
        public readonly BasicType BaseType;

        internal RawEnumInfo( uint typeId, DbgHelp.EnumInfoRequest eir )
        {
            TypeId                     = typeId;
            Name                       = Marshal.PtrToStringUni( eir.SymName );
            NumEnumerands              = eir.ChildrenCount;
            Size                       = eir.Length;
            BaseTypeTypeId             = eir.BaseTypeId;
            BaseType                   = (BasicType) eir.BaseType;
        } // end constructor
    } // end RawEnumInfo


 // public enum InlineFrameContext : uint
 // {
 //     Init = 0,
 //     Ignore = 0xFFFFFFFF
 // } // end enum InlineFrameContext
}

