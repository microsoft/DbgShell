// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

#pragma warning disable 1591

namespace Microsoft.Diagnostics.Runtime.Interop
{

    public enum IoctlCode : ushort
    {
        DumpSymbolInfo = 22,          // IG_DUMP_SYMBOL_INFO
    }


    // Corresponds to DEBUG_DUMP_XXX defines.
    [Flags]
    public enum DbgDump : uint
    {
        NO_INDENT              = 0x00000001, // Fields are not indented if this is set
        NO_OFFSET              = 0x00000002, // Offsets are not printed if this is set
        VERBOSE                = 0x00000004, // Verbose output
        CALL_FOR_EACH          = 0x00000008, // Callback is done for each of fields
        LIST                   = 0x00000020, // A list of type is dumped, listLink should have info about next element pointer
        NO_PRINT               = 0x00000040, // Nothing is printed if this is set (only callbacks and data copies done)
        GET_SIZE_ONLY          = 0x00000080, // Ioctl returns the size as usual, but will not do field prints/callbacks if this is set
        COMPACT_OUT            = 0x00002000, // No newlines are printed after each field
        ARRAY                  = 0x00008000, // An array of type is dumped, number of elements can be specified in listLink->size
        ADDRESS_OF_FIELD       = 0x00010000, // The specified addr value is actually the address of field listLink->fName
        ADDRESS_AT_END         = 0x00020000, // The specified addr value is actually the adress at the end of type
        COPY_TYPE_DATA         = 0x00040000, // This could be used to copy only the primitive types like ULONG, PVOID etc. - will not work with structures/unions
        READ_PHYSICAL          = 0x00080000, // Flag to allow read directly from physical memory
        FUNCTION_FORMAT        = 0x00100000, // This causes a function type to be dumped in format function(arg1, arg2, ...)
        BLOCK_RECURSE          = 0x00200000, // This recurses on a struct but doesn't expand pointers
        MATCH_SIZE             = 0x00400000, // Match the type size to resolve ambiguity in case multiple matches with same name are available
    } // end enum DbgDump



    public class WdbgExts
    {
        private IDebugControl6 m_debugControl;

        public WdbgExts( IDebugControl6 debugControl )
        {
            if( null == debugControl )
                throw new ArgumentNullException( "debugControl" );

            m_debugControl = debugControl;
        } // end constructor


        public IList< FieldInfo > GetSymbolFields( uint typeId, ulong modBase )
        {
            SYM_DUMP_PARAM sdp = new SYM_DUMP_PARAM();
            sdp.size = (uint) Marshal.SizeOf( sdp );
            sdp.Options = DbgDump.NO_PRINT | DbgDump.CALL_FOR_EACH;
            sdp.ModBase = modBase; // presumably this makes it so we don't have to search all modules? (it's doc'ed as an 'out' param)

            List< FieldInfo > fields = new List< FieldInfo >();
            GCHandle gchFields = GCHandle.Alloc( fields );
            SYM_DUMP_FIELD_CALLBACK callback = new SYM_DUMP_FIELD_CALLBACK(_My_SYM_DUMP_FIELD_CALLBACK);
            sdp.Context = GCHandle.ToIntPtr( gchFields );
            sdp.CallbackRoutine = Marshal.GetFunctionPointerForDelegate( callback );
            sdp.TypeId = typeId;

            try
            {
                int err = Ioctl( IoctlCode.DumpSymbolInfo, sdp );
                if( 0 != err )
                {
                    // TODO: proper error
                    throw new Exception( String.Format( "Ioctl for DumpSymbolInfo returned: {0}", err ) );
                }
                DebugModuleAndId dmai = new DebugModuleAndId( modBase, typeId );
                foreach( var field in fields )
                {
                    field.OwningType = dmai;
                }
                return fields.AsReadOnly();
            }
            finally
            {
                gchFields.Free();
                GC.KeepAlive(callback);
            }
        } // end GetSymbolFields


        private int _My_SYM_DUMP_FIELD_CALLBACK( FIELD_INFO pField, IntPtr userContext )
        {
            GCHandle gchFields = GCHandle.FromIntPtr( userContext );
            List< FieldInfo > fields = (List< FieldInfo >) gchFields.Target;
            fields.Add( new FieldInfo( fields.Count, pField ) );
            return 0;
        } // end _My_SYM_DUMP_FIELD_CALLBACK()


        private WINDBG_EXTENSION_APIS? m_extApis;

        private WINDBG_EXTENSION_APIS _ExtensionApis
        {
            get
            {
                if( null == m_extApis )
                {
                    // TODO: does this depend on the debugger bitness or the target bitness? I'm
                    // guessing debugger bitness.
                    WINDBG_EXTENSION_APIS wdbgExts = new WINDBG_EXTENSION_APIS();
                    wdbgExts.nSize = (uint) Marshal.SizeOf( wdbgExts );
                    int hr;
                    if( Is32Bit )
                        hr = m_debugControl.GetWindbgExtensionApis32( ref wdbgExts );
                    else
                        hr = m_debugControl.GetWindbgExtensionApis64( ref wdbgExts );

                    if( 0 != hr )
                        // TODO: proper error
                        throw new Exception( String.Format( "Couldn't get wdbgexts API: 0x{0:x8}", hr ) );

                    m_extApis = wdbgExts;
                }
                return (WINDBG_EXTENSION_APIS) m_extApis;
            }
        } // end property _ExtensionApis


        private IoctlDelegate m_ioctl;

        private IoctlDelegate _NativeIoctl
        {
            get
            {
                if( null == m_ioctl )
                {
                    m_ioctl = (IoctlDelegate) Marshal.GetDelegateForFunctionPointer( _ExtensionApis.lpIoctlRoutine,
                                                                                     typeof( IoctlDelegate ) );
                }
                return m_ioctl;
            }
        } // end property _NativeIoctl

        public int Ioctl( IoctlCode ioctlType, object data )
        {
            uint size = (uint) Marshal.SizeOf( data );
            GCHandle gch = GCHandle.Alloc( data, GCHandleType.Pinned );
            int err = -1;
            try
            {
                IntPtr ptr = gch.AddrOfPinnedObject();
                err = _NativeIoctl( (ushort) ioctlType, ptr, size );
            }
            finally
            {
                gch.Free();
            }
            return err;
        } // end Ioctl


        private bool? m_is32bit;
        public bool Is32Bit
        {
            get
            {
                if( null == m_is32bit )
                {
                    int hr = m_debugControl.IsPointer64Bit();
                    if( 0 == hr )
                        m_is32bit = false;
                    else if( 1 == hr )
                        m_is32bit = true;
                    else
                        // TODO: proper error
                        throw new Exception( String.Format( "IsPointer64Bit failed: {0:x8}", hr ) );
                }
                return (bool) m_is32bit;
            }
        }
    } // end class WdbgExts


    [StructLayout( LayoutKind.Sequential )]
    public class FIELD_INFO
    {
        [MarshalAs( UnmanagedType.LPStr )]
        public string       fName;          // Name of the field
        [MarshalAs( UnmanagedType.LPStr )]
        public string       printName;      // Name to be printed at dump
        public uint         size;           // Size of the field
        public uint         fOptions;       // Dump Options for the field
        public ulong        address;        // address of the field
        public IntPtr       pBuffer;
      //union {
      //   PVOID    fieldCallBack;  // Return info or callBack routine for the field
      //   PVOID    pBuffer;        // the type data is copied into this
      //};
        public uint         TypeId;         // OUT Type index of the field
        public uint         FieldOffset;    // OUT Offset of field inside struct
        public uint         BufferSize;     // size of buffer used with DBG_DUMP_FIELD_COPY_FIELD_DATA
        public uint         BitField;
      //struct _BitField {
      //   USHORT Position;         // OUT set to start position for bitfield
      //   USHORT Size;             // OUT set to size for bitfields
      //} BitField;

        public uint         Flags;
      //uint         fPointer:2;     // OUT set to 1 for pointers, 3 for 64bit pointers
      //uint         fArray:1;       // OUT set to 1 for array types
      //uint         fStruct:1;      // OUT set to 1 for struct/class tyoes
      //uint         fConstant:1;    // OUT set to 1 for constants (enumerate as fields)
      //uint         fStatic:1;      // OUT set to 1 for statics (class/struct static members)
      //uint         Reserved:26;    // unused
    } // end class FIELD_INFO

    public delegate int SYM_DUMP_FIELD_CALLBACK( FIELD_INFO pField, IntPtr userContext );
    public delegate int IoctlDelegate( ushort IoctlType, IntPtr lpvData, uint cbSizeOfContext );

    [StructLayout( LayoutKind.Sequential )]
    public struct SYM_DUMP_PARAM
    {
        public uint                size;          // size of this struct
      // Need to declare as IntPtr so that we can pin this structure.
      //[MarshalAs( UnmanagedType.LPStr )]
      //public string              sName;         // type name
        public IntPtr              sName;         // type name
        public DbgDump             Options;       // Dump options
        public ulong               addr;          // Address to take data for type
        public IntPtr              listLink;      // fName here would be used to do list dump   // PFIELD_INFO
        public IntPtr              Context;
      // union {
      // PVOID           Context;       // Usercontext passed to CallbackRoutine
      // PVOID           pBuffer;       // the type data is copied into this
      //};

        // The CallbackRoutine member cannot be a delegate type because a delegate is not
        // a blittable data type--if we included a non-blittable data type in this
        // structure, then we wouldn't be able to pin it. So we'll make it an IntPtr
        // instead and use Marshal.GetFunctionPointerForDelegate.
        //public SYM_DUMP_FIELD_CALLBACK CallbackRoutine; // Routine called back
        public IntPtr              CallbackRoutine; // Routine called back
        public uint                nFields;       // # elements in Fields
        public IntPtr              Fields;        // Used to return information about field // PFIELD_INFO
        public ulong               ModBase;       // OUT Module base address containing type
        public uint                TypeId;        // OUT Type index of the symbol
        public uint                TypeSize;      // OUT Size of type
        public uint                BufferSize;    // IN size of buffer (used with DBG_DUMP_COPY_TYPE_DATA)
        public uint                Flags;
      //uint                fPointer:2;    // OUT set to 1 for pointers, 3 for 64bit pointers
      //uint                fArray:1;      // OUT set to 1 for array types
      //uint                fStruct:1;     // OUT set to 1 for struct/class tyoes
      //uint                fConstant:1;   // OUT set to 1 for constant types (unused)
      //uint                Reserved:27;   // unused
    } // end struct SYM_DUMP_PARAM


    public enum PointerFlag
    {
        None = 0,
        Pointer = 1,
        Pointer64 = 3
    }

    public class FieldInfo : IEquatable< FieldInfo >
    {
        public int Index { get; private set; }
        public string Name { get; private set; }
        public string PrintName { get; private set; }
        public uint Size { get; private set; }
        //public uint fOptions { get; private set; } // TODO: enum?
        //public ulong Address { get; private set; }
        // TODO: what to do about the pBuffer?

        // TODO: should I put these 'out' fields in a separate structure
        public uint TypeId { get; private set; }
        public uint FieldOffset { get; private set; }

        public PointerFlag PointerFlag { get; private set; }
        public bool IsArray { get; private set; }
        public bool IsStruct { get; private set; }
        public bool IsConstant { get; private set; }
        public bool IsStatic { get; private set; }

        public DebugModuleAndId OwningType { get; internal set; } // gets set post-construction

        internal FieldInfo( int index, FIELD_INFO nativeFi )
        {
            Index = index;
            Name = nativeFi.fName;
            PrintName = nativeFi.printName;
            Size = nativeFi.size;
            //fOptions = nativeFi.fOptions;
            //Address = nativeFi.address;
            TypeId = nativeFi.TypeId;
            // The nativeFi.FieldOffset field is 0. What is it for? I don't know. Perhaps
            // there is a different usage of the FIELD_INFO struct.
            // According to comments in wdbgexts.h, if we had supplied an address in the
            // SYM_DUMP_PARAM structure, then address would have the address of the field,
            // (address in SYM_DUMP_PARAM plus field offset), but we pass 0, so address is
            // just the field offset.
            FieldOffset = (uint) nativeFi.address;
            PointerFlag = (PointerFlag) (nativeFi.Flags & 0x0003);
            IsArray = 0 != (nativeFi.Flags & 0x0004);
            IsStruct = 0 != (nativeFi.Flags & 0x0008);
            IsConstant = 0 != (nativeFi.Flags & 0x0010);
            IsStatic = 0 != (nativeFi.Flags & 0x0020);
        } // end constructor


        #region IEquatable< FieldInfo > Stuff

        public bool Equals( FieldInfo other )
        {
            if( null == other )
                return false;

            return (TypeId == other.TypeId) &&
                   (FieldOffset == other.FieldOffset) &&
                   (Index == other.Index) &&
                   (OwningType == other.OwningType);
        } // end Equals()

        public override bool Equals( object obj )
        {
            return Equals( obj as FieldInfo );
        }

        public override int GetHashCode()
        {
            return TypeId.GetHashCode() +
                   FieldOffset.GetHashCode() +
                   Index.GetHashCode() +
                   OwningType.GetHashCode();
        }

        public static bool operator ==( FieldInfo f1, FieldInfo f2 )
        {
            if( null == (object) f1 )
                return (null == (object) f2);

            return f1.Equals( f2 );
        }

        public static bool operator !=( FieldInfo f1, FieldInfo f2 )
        {
            return !(f1 == f2);
        }
        #endregion
    } // end class FieldInfo


    public class SymDumpParam
    {
        public string Name { get; private set; }
        public ulong Address { get; private set; }
        public ulong ModBase { get; private set; }
        public uint TypeId { get; private set; }
        public uint TypeSize { get; private set; }

        public PointerFlag PointerFlag { get; private set; }
        public bool IsArray { get; private set; }
        public bool IsStruct { get; private set; }
        public bool IsConstant { get; private set; }

        public SymDumpParam( SYM_DUMP_PARAM nativeSdp )
        {
            Name = Marshal.PtrToStringAnsi( nativeSdp.sName );
            Address = nativeSdp.addr;
            ModBase = nativeSdp.ModBase;
            TypeId = nativeSdp.TypeId;
            TypeSize = nativeSdp.TypeSize;
            PointerFlag = (PointerFlag) (nativeSdp.Flags & 0x0003);
            IsArray = 0 != (nativeSdp.Flags & 0x0004);
            IsStruct = 0 != (nativeSdp.Flags & 0x0008);
            IsConstant = 0 != (nativeSdp.Flags & 0x0010);
        } // end constructor
    } // end class SymDumpParam

}

