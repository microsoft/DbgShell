using System;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace v4Lib
{
    internal static class NativeMethods
    {

        /*
    HRESULT CallCoCreateInstance( _In_   REFCLSID rclsid,
                                  _In_   LPUNKNOWN pUnkOuter,
                                  _In_   DWORD dwClsContext,
                                  _In_   REFIID riid,
                                  _Out_  LPVOID *ppv )
                                  */
        [DllImport( "CoCreateShim.dll", EntryPoint = "CallCoCreateInstance" )]
        private unsafe static extern int native_CallCoCreateInstance( Guid* rclsid,
                                                                      [MarshalAs( UnmanagedType.IUnknown )]
                                                                      object pUnkOuter,
                                                                      uint dwClsContext,
                                                                      Guid* riid,
                                                                      IntPtr* ppv );

        public unsafe static int CallCoCreateInstance( Guid clsid,
                                                       object pUnkOuter,
                                                       uint dwClsContext,
                                                       Guid iid,
                                                       out dynamic obj )
        {
            obj = null;
            Guid* rclsid = &clsid;
            Guid* riid = &iid;
            IntPtr p = IntPtr.Zero;

            int hr = native_CallCoCreateInstance( rclsid,
                                                  pUnkOuter,
                                                  dwClsContext,
                                                  riid,
                                                  &p );

            if( 0 == hr )
            {
                obj = Marshal.GetObjectForIUnknown( p );
            }

            return hr;
        } // end CallCoCreateInstance()


        // TODO: SafeHandle-ify
        [DllImport( "kernel32.dll", CharSet = CharSet.Unicode )]
        public static extern IntPtr CreateActCtx( NativeActivationContext actCtx );


        [DllImport( "kernel32.dll" )]
        [return: MarshalAs( UnmanagedType.Bool )]
        public static unsafe extern bool ActivateActCtx( IntPtr hActCtx, UIntPtr* lpCookie );

        /*
BOOL DeactivateActCtx(
  _In_  DWORD dwFlags,
  _In_  ULONG_PTR ulCookie
); */
        [DllImport( "kernel32.dll" )]
        [return: MarshalAs( UnmanagedType.Bool )]
        public static extern bool DeactivateActCtx( uint dwFlags, UIntPtr ulCookie );

        [DllImport( "kernel32.dll" )]
        public static extern void ReleaseActCtx( IntPtr actCtx );

    }


    internal enum ActivationContextFlags
    {
        PROCESSOR_ARCHITECTURE_VALID = 0x00000001,
        LANGID_VALID                 = 0x00000002,
        ASSEMBLY_DIRECTORY_VALID     = 0x00000004,
        RESOURCE_NAME_VALID          = 0x00000008,
        SET_PROCESS_DEFAULT          = 0x00000010,
        APPLICATION_NAME_VALID       = 0x00000020,
        SOURCE_IS_ASSEMBLYREF        = 0x00000040,
        HMODULE_VALID                = 0x00000080,
    } // end enum ActivationContextFlags


    [StructLayout( LayoutKind.Sequential )]
    internal class NativeActivationContext
    {
        private uint cbSize = (uint) Marshal.SizeOf( typeof( NativeActivationContext ) );

        public ActivationContextFlags Flags;

        [MarshalAs( UnmanagedType.LPWStr )]
        public String Source;

        public UInt16 ProcessorArchitecture;

        public UInt16 LangId;

        [MarshalAs( UnmanagedType.LPWStr )]
        public String AssemblyDirectory;

        // This has to be marshaled as an IntPtr instead of a string, because we have to
        // be able to put a resource ID here, too (as an integer).
        public IntPtr ResourceName;

        [MarshalAs( UnmanagedType.LPWStr )]
        public String ApplicationName;

        public IntPtr HModule;
    } // end class NativeActivationContext
}
