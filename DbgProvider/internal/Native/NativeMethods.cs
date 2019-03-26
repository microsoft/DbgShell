//-----------------------------------------------------------------------
// <copyright file="NativeMethods.cs" company="Microsoft">
//    Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MS.Dbg
{
    internal static class ExternDll
    {
        public const string Advapi32 = "advapi32.dll";
        public const string Kernel32 = "kernel32.dll";
        public const string DbgNativeUtil = "DbgNativeUtil.dll";
    }


    internal static partial class NativeMethods
    {
        // This is a win32 error code from winerror.h; not a quantity of minutes or
        // something like that.
        public const int WAIT_TIMEOUT = 258;

        public const int ERROR_INVALID_DATA = 13;
        public const int ERROR_INVALID_PARAMETER = 87;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const int ERROR_ALREADY_EXISTS = 183;
        public const int ERROR_MORE_DATA = 234;

        public const int RPC_S_SERVER_UNAVAILABLE = 1722;
        public const int RPC_S_CALL_FAILED = 1726;
        public const int RPC_S_CALL_FAILED_DNE = 1727;

        [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr StrFormatByteSizeW(
                long size, 
                [MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer, 
                int bufferSize);


        [DllImport( ExternDll.Kernel32, CharSet = CharSet.Unicode, SetLastError = true )]
        internal static extern IntPtr LoadLibrary( string lpFileName );

        [DllImport( ExternDll.Kernel32, CharSet = CharSet.Unicode, SetLastError = true )]
        internal static extern IntPtr LoadLibraryEx( string lpFileName,
                                                     IntPtr hFile_Reserved,
                                                     LoadLibraryExFlags flags );



        [DllImport( ExternDll.Kernel32, SetLastError = true )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static extern bool FreeLibrary( IntPtr hModule );


        [DllImport( ExternDll.Kernel32,
                    CharSet = CharSet.Ansi,
                    ExactSpelling = true,
                    SetLastError = true,
                    BestFitMapping = false,
                    ThrowOnUnmappableChar = true )]
        internal static extern IntPtr GetProcAddress( IntPtr hModule, [MarshalAs( UnmanagedType.LPStr )] string procName );


        [DllImport( ExternDll.Kernel32 )]
        internal static extern uint GetTickCount();


        [DllImport( ExternDll.Kernel32 )]
        internal static extern void DebugBreak();


        internal const int LOGON32_PROVIDER_DEFAULT = 0;

        internal const int LOGON32_LOGON_INTERACTIVE       = 2;
        internal const int LOGON32_LOGON_NETWORK           = 3;
        internal const int LOGON32_LOGON_BATCH             = 4;
        internal const int LOGON32_LOGON_SERVICE           = 5;
        internal const int LOGON32_LOGON_NETWORK_CLEARTEXT = 8;
        internal const int LOGON32_LOGON_NEW_CREDENTIALS   = 9;

        [DllImport( ExternDll.Advapi32, SetLastError = true, CharSet = CharSet.Unicode )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static extern bool LogonUser( [MarshalAs(UnmanagedType.LPWStr)] string lpszUsername,
                                               [MarshalAs(UnmanagedType.LPWStr)] string lpszDomain,
                                               IntPtr lpszPassword,
                                               int dwLogonType,
                                               int dwLogonProvider,
                                               out SafeUserTokenHandle phToken );

        [DllImport( ExternDll.Kernel32, SetLastError = true )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static extern bool CloseHandle( IntPtr handle );


        [DllImport( ExternDll.Advapi32, CharSet = CharSet.Unicode )]
        internal static extern int InitiateShutdown( [MarshalAs(UnmanagedType.LPWStr)] string lpMachineName, // null for localhost
                                                     [MarshalAs(UnmanagedType.LPWStr)] string lpMessage,
                                                     uint dwGracePeriod, // seconds
                                                     ShutdownFlags dwShutdownFlags,
                                                     uint dwReason );
        
        [DllImport( "netapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "NetLocalGroupAddMembers" )]
        internal static extern int native_NetLocalGroupAddMembers( string serverName,
                                                                   string groupName,
                                                                   int level,
                                                                   string[] newMembers,
                                                                   int numNewMembers );

        internal static void NetLocalGroupAddMembers( string serverName, string groupName, params string[] newMembers )
        {
            int err = native_NetLocalGroupAddMembers( serverName, groupName, 3, newMembers, newMembers.Length );
            if( 0 != err )
                throw new Win32Exception( err );
        }

        [DllImport( "netapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "NetLocalGroupDelMembers" )]
        internal static extern int native_NetLocalGroupDelMembers( string serverName,
                                                                   string groupName,
                                                                   int level,
                                                                   string[] removeUs,
                                                                   int numRemoveUs );
        
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "NetValidateName")]
        internal static extern int native_NetValidateName(  string lpServer,
                                                            string lpName,
                                                            string lpAccount,
                                                            string lpPassword,
                                                            int nameType);

        internal static void NetLocalGroupDelMembers( string serverName, string groupName, params string[] removeUs )
        {
            int err = native_NetLocalGroupDelMembers( serverName, groupName, 3, removeUs, removeUs.Length );
            if( 0 != err )
                throw new Win32Exception( err );
        }

        // This only checks for enabled privileges.
        [DllImport( ExternDll.Advapi32, CharSet = CharSet.Unicode, SetLastError = true )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static extern bool PrivilegeCheck( SafeUserTokenHandle token,
                                                    [In] [Out] PRIVILEGE_SET requiredPrivileges,
                                                    out bool result );

        [DllImport( ExternDll.Advapi32, CharSet = CharSet.Unicode, SetLastError = true )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static extern bool GetTokenInformation( SafeUserTokenHandle tokenHandle,
                                                         TokenInformationClass infoClass,
                                                         [Out] byte[] tokenInformation,
                                                         int cbTokenInformation,
                                                         out int returnLength );

        [DllImport( ExternDll.Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs( UnmanagedType.Bool )]
        [ReliabilityContract( Consistency.WillNotCorruptState, Cer.MayFail )]
        internal static extern bool LookupPrivilegeName( string systemName,
                                                         [In] ref LUID pLuid,
                                                         [Out] StringBuilder name,
                                                         ref int cchName );

        [DllImport( ExternDll.Kernel32, SetLastError = true )]
        [ReliabilityContract( Consistency.WillNotCorruptState, Cer.MayFail )]
        internal static extern IntPtr GetCurrentProcess();


        [DllImport( ExternDll.Kernel32, SetLastError = true)]
        [ReliabilityContract( Consistency.WillNotCorruptState, Cer.MayFail )]
        public static extern int GetProcessId( IntPtr hProcess );


        [DllImport( ExternDll.Advapi32, CharSet = CharSet.Unicode, SetLastError = true )]
        [ReliabilityContract( Consistency.WillNotCorruptState, Cer.MayFail )]
        [return: MarshalAs( UnmanagedType.Bool )]
        internal static extern bool OpenProcessToken( IntPtr hProcess,
                                                      TokenAccessLevels DesiredAccess,
                                                      out SafeUserTokenHandle TokenHandle );

        [DllImport( ExternDll.Kernel32 )]
        internal static extern int GetCurrentProcessId();

        // PInvoking to CreateFile allows us to open NTFS alternate streams.
        [DllImport( ExternDll.Kernel32, CharSet = CharSet.Unicode, SetLastError = true )]
        internal static extern SafeFileHandle CreateFile( string lpFileName,
                                                          uint dwDesiredAccess,
                                                          FileShare dwShareMode,
                                                          IntPtr securityAttrs,
                                                          FileMode dwCreationDisposition,
                                                          int dwFlagsAndAttributes,
                                                          IntPtr hTemplateFile );

        [return: MarshalAs( UnmanagedType.Bool )]
        [DllImport( ExternDll.Kernel32, SetLastError = true )]
        internal static extern bool SetConsoleCtrlHandler( HandlerRoutine handler,
                                                           [MarshalAs( UnmanagedType.Bool )] bool add );


        [return: MarshalAs( UnmanagedType.Bool )]
        [DllImport( ExternDll.Kernel32, SetLastError = true )]
        internal static extern bool SetHandleInformation( SafeHandle handle,
                                                          HandleFlag dwMask,
                                                          HandleFlag dwFlags );


        [StructLayout( LayoutKind.Sequential )]
        private unsafe struct ULDOUBLE
        {
            public fixed byte Bytes[ 10 ];
            public unsafe ULDOUBLE( byte[] bytes )
            {
                if( null == bytes )
                    throw new ArgumentNullException( "bytes" );

                if( 10 != bytes.Length )
                    throw new ArgumentException( "The bytes argument should be 10 bytes.", "bytes" );

                fixed( byte* pb = Bytes )
                {
                    Marshal.Copy( bytes, 0, new IntPtr( pb ), 10 );
                }
            } // end constructor
        } // end struct ULDOUBLE

     // // The native function actually returns a PTSTR, but we declare the PInvoke
     // // signature as returning a plain IntPtr, because otherwise the CLR marshaler will
     // // mess things up by trying to CoTaskMemFree the native pointer after marshaling
     // // to a managed System.String, which is Bad, so we don't want it to do that.
     // // Fortunately, the returned PTSTR is the same as the buffer passed in, so we can
     // // just use that.
     // [DllImport( ExternDll.DbgNativeUtil,
     //             CharSet = CharSet.Unicode,
     //             EntryPoint = "_uldtoa",
     //             CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl )]
     // private static extern unsafe IntPtr native_uldtoa( [In] ULDOUBLE* px,
     //                                                    [In] int maxchars,
     //                                                    [In][Out] [MarshalAs( UnmanagedType.LPWStr )] StringBuilder ldtext );

        internal unsafe static string _uldtoa( byte[] bytes )
        {
         // ULDOUBLE x = new ULDOUBLE( bytes );
         // StringBuilder sb = new StringBuilder( 30 );
         // native_uldtoa( &x, sb.Capacity, sb );
         // return sb.ToString();

            throw new NotImplementedException();
        }
    } // end partial class NativeMethods


    [return: MarshalAs( UnmanagedType.Bool )]
    internal delegate bool HandlerRoutine( ConsoleBreakSignal ctrlType );


    internal enum HandleFlag
    {
        None = 0,
        Inherit = 1,
        ProtectFromClose = 2
    } // end enum HandleFlag


    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Unicode )]
    internal struct LUID
    {
        public uint LowPart;
        public uint HighPart;
    }

    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Unicode )]
    internal struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Unicode )]
    internal class PRIVILEGE_SET
    {
        public uint PrivilegeCount;
        public uint Control;
        public LUID_AND_ATTRIBUTES Privilege; // In native code, this would actually be an array. But we only need room for one, so that makes it simple.
    }


    internal enum TokenInformationClass
    {
        User = 1,
        Groups,
        Privileges,
        Owner,
        PrimaryGroup,
        DefaultDacl,
        Source,
        Type,
        ImpersonationLevel,
        Statistics,
        RestrictedSids,
        SessionId,
        GroupsAndPrivileges,
        SessionReference,
        SandBoxInert,
        AuditPolicy,
        Origin,
        ElevationType,
        LinkedToken,
        Elevation,
        HasRestrictions,
        AccessInformation,
        VirtualizationAllowed,
        VirtualizationEnabled,
        IntegrityLevel,
        UIAccess,
        MandatoryPolicy,
        LogonSid,
        IsAppContainer,
        Capabilities,
        AppContainerSid,
        AppContainerNumber,
        UserClaimAttributes,
        DeviceClaimAttributes,
        RestrictedUserClaimAttributes,
        RestrictedDeviceClaimAttributes,
        DeviceGroups,
        RestrictedDeviceGroups,
        SecurityAttributes,
        IsRestricted,
    } // end enum TokenInformationClass


    // A managed rendition of the LUID_AND_ATTRIBUTES structures contained as a part of a
    // TOKEN_PRIVILEGES structure.
    internal class TokenPrivilege
    {
        public Privilege Privilege { get; private set; }
        public PrivilegeAttribute Attributes { get; private set; }

        public TokenPrivilege( Privilege privilege, PrivilegeAttribute attributes )
        {
            Privilege = privilege;
            Attributes = attributes;
        } // end constructor

        public TokenPrivilege( LUID_AND_ATTRIBUTES laa )
        {
            Privilege = _GetPrivilegeFromLuid( laa.Luid, out m_name );
            Attributes = (PrivilegeAttribute) laa.Attributes;
        } // end constructor


        private string m_name;

        public string PrivilegeName
        {
            get
            {
                if( null == m_name )
                {
                    if( (int) Privilege >= sm_privNames.Length )
                        m_name = sm_privNames[ 0 ]; // "unknown"
                    else
                        m_name = sm_privNames[ (int) Privilege ];
                }
                return m_name;
            }
        }

        public override string ToString()
        {
            return Util.Sprintf( "{0} ({1})", PrivilegeName, Attributes );
        }

        private static Privilege _GetPrivilegeFromLuid( LUID luid, out string name )
        {
            name = null;
            int cchSb = 240;
            StringBuilder sb = new StringBuilder( cchSb );

            if( !NativeMethods.LookupPrivilegeName( null, ref luid, sb, ref cchSb ) )
            {
                throw new Win32Exception(); // automatically uses last win32 error
            }

            name = sb.ToString();
            for( int idx = 0; idx < sm_privNames.Length; idx++ )
            {
                if( 0 == Util.Strcmp_OI( name, sm_privNames[ idx ] ) )
                {
                    return (Privilege) idx;
                }
            }
            return Privilege.Unknown;
        } // end _GetPrivilegeFromLuid()


        private static readonly string[] sm_privNames =
        {
            "<unknown privilege>",
            "SeCreateTokenPrivilege",
            "SeAssignPrimaryTokenPrivilege",
            "SeLockMemoryPrivilege",
            "SeIncreaseQuotaPrivilege",
            "SeUnsolicitedInputPrivilege",
            "SeMachineAccountPrivilege",
            "SeTcbPrivilege",
            "SeSecurityPrivilege",
            "SeTakeOwnershipPrivilege",
            "SeLoadDriverPrivilege",
            "SeSystemProfilePrivilege",
            "SeSystemtimePrivilege",
            "SeProfileSingleProcessPrivilege",
            "SeIncreaseBasePriorityPrivilege",
            "SeCreatePagefilePrivilege",
            "SeCreatePermanentPrivilege",
            "SeBackupPrivilege",
            "SeRestorePrivilege",
            "SeShutdownPrivilege",
            "SeDebugPrivilege",
            "SeAuditPrivilege",
            "SeSystemEnvironmentPrivilege",
            "SeChangeNotifyPrivilege",
            "SeRemoteShutdownPrivilege",
            "SeUndockPrivilege",
            "SeSyncAgentPrivilege",
            "SeEnableDelegationPrivilege",
            "SeManageVolumePrivilege",
            "SeImpersonatePrivilege",
            "SeCreateGlobalPrivilege",
            "SeTrustedCredManAccessPrivilege",
            "SeRelabelPrivilege",
            "SeIncreaseWorkingSetPrivilege",
            "SeTimeZonePrivilege",
            "SeCreateSymbolicLinkPrivilege"
        };
    } // end class TokenPrivilege


    internal enum Privilege
    {
        // IMPORTANT: Each value is used as an index into TokenPrivilege.sm_privNames.
        Unknown = 0,
        CreateToken,
        AssignPrimaryToken,
        LockMemory,
        IncreaseQuota,
        UnsolicitedInput,
        MachineAccount,
        Tcb,
        Security,
        TakeOwnership,
        LoadDriver,
        SystemProfile,
        Systemtime,
        ProfileSingleProcess,
        IncreaseBasePriority,
        CreatePagefile,
        CreatePermanent,
        Backup,
        Restore,
        Shutdown,
        Debug,
        Audit,
        SystemEnvironment,
        ChangeNotify,
        RemoteShutdown,
        Undock,
        SyncAgent,
        EnableDelegation,
        ManageVolume,
        Impersonate,
        CreateGlobal,
        TrustedCredManAccess,
        Relabel,
        IncreaseWorkingSet,
        TimeZone,
        CreateSymbolicLink,
    } // end Privilege enum


    [Flags]
    internal enum PrivilegeAttribute : uint
    {
        Disabled         = 0x00000000,
        EnabledByDefault = 0x00000001,
        Enabled          = 0x00000002,
        Removed          = 0X00000004,
        UsedForAccess    = 0x80000000,
    } // end enum PrivilegeAttribute


    [Flags]
    internal enum ShutdownFlags
    {
        ForceOthers         = 0x00000001,
        ForceSelf           = 0x00000002,
        Restart             = 0x00000004,
        PowerOff            = 0x00000008,
        NoReboot            = 0x00000010,
        GraceOverride       = 0x00000020,
        InstallUpdates      = 0x00000040,
        RestartApps         = 0x00000080,
        SkipSvcPreshutdown  = 0x00000100,
        Full                = 0x00000200,
    } // end enum ShutdownFlags


    [Flags]
    internal enum LoadLibraryExFlags : uint
    {
        DONT_RESOLVE_DLL_REFERENCES   = 0x00000001,
        AS_DATAFILE                   = 0x00000002,
        // reserved for internal LOAD_PACKAGED_LIBRARY: 0x00000004
        LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008,
        LOAD_IGNORE_CODE_AUTHZ_LEVEL  = 0x00000010,
        AS_IMAGE_RESOURCE             = 0x00000020,
        AS_DATAFILE_EXCLUSIVE         = 0x00000040,
        REQUIRE_SIGNED_TARGET         = 0x00000080,
        SEARCH_DLL_LOAD_DIR           = 0x00000100,
        SEARCH_APPLICATION_DIR        = 0x00000200,
        SEARCH_USER_DIRS              = 0x00000400,
        SEARCH_SYSTEM32               = 0x00000800,
        SEARCH_DEFAULT_DIRS           = 0x00001000,
    } // end enum LoadLibraryExFlags


    internal sealed class SafeUserTokenHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeUserTokenHandle()
            : base( true )
        {
        }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.CloseHandle( base.handle );
        }
    } // end class SafeUserTokenHandle
}

