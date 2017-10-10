using System;
using System.Globalization;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [Flags]
    public enum FixedFileInfoFlags
    {
        None            = 0,
        Debug           = 0x00000001,
        Prerelease      = 0x00000002,
        Patched         = 0x00000004,
        PrivateBuild    = 0x00000008,
        InfoInferred    = 0x00000010,
        SpecialBuild    = 0x00000020,
    }

    [Flags]
    public enum FixedFileInfoOs
    {
        Unknown           = 0x00000000,
		Dos               = 0x00010000,
		OS216             = 0x00020000,
		OS232             = 0x00030000,
		NT                = 0x00040000,
		WinCE             = 0x00050000,

		Windows16         = 0x00000001,
		PM16              = 0x00000002,
		PM32              = 0x00000003,
		Windows32         = 0x00000004,
    }

    public enum FixedFileInfoFileType
    {
        Unknown           = 0x00000000,
        App               = 0x00000001,
        Dll               = 0x00000002,
        Drv               = 0x00000003,
        Font              = 0x00000004,
        Vxd               = 0x00000005,
        StaticLib         = 0x00000007,
    }


    /// <summary>
    ///    Nice managed counterpart to VS_FIXEDFILEINFO.
    /// </summary>
    public class FixedFileInfo
    {
        public readonly uint Signature;
        public readonly Version StructureVersion;
        public readonly Version FileVersion;
        public readonly Version ProductVersion;
        public readonly FixedFileInfoFlags Flags;
        public readonly FixedFileInfoOs OS;
        public readonly FixedFileInfoFileType FileType;
        public readonly uint FileSubType;
        public readonly DateTime FileDate;

        public FixedFileInfo( VS_FIXEDFILEINFO nativeFfi )
        {
            Signature = nativeFfi.dwSignature;
            uint major = (nativeFfi.dwStrucVersion & 0xffff0000) >> 16;
            uint minor = (nativeFfi.dwStrucVersion & 0x0000ffff);
            StructureVersion = new Version( (int) major, (int) minor, 0, 0 );

            uint build, rev;
            major = (nativeFfi.dwFileVersionMS & 0xffff0000) >> 16;
            minor = (nativeFfi.dwFileVersionMS & 0x0000ffff);
            build = (nativeFfi.dwFileVersionLS & 0xffff0000) >> 16;
            rev   = (nativeFfi.dwFileVersionLS & 0x0000ffff);

            FileVersion = new Version( (int) major,
                                       (int) minor,
                                       (int) build,
                                       (int) rev );

            major = (nativeFfi.dwProductVersionMS & 0xffff0000) >> 16;
            minor = (nativeFfi.dwProductVersionMS & 0x0000ffff);
            build = (nativeFfi.dwProductVersionLS & 0xffff0000) >> 16;
            rev   = (nativeFfi.dwProductVersionLS & 0x0000ffff);

            ProductVersion = new Version( (int) major,
                                          (int) minor,
                                          (int) build,
                                          (int) rev );

            Flags = (FixedFileInfoFlags) (nativeFfi.dwFileFlagsMask & (uint) nativeFfi.dwFileFlags);
            OS = (FixedFileInfoOs) nativeFfi.dwFileOS;
            FileType = (FixedFileInfoFileType) nativeFfi.dwFileType;
            FileSubType = nativeFfi.dwFileSubtype;

            long filetime = nativeFfi.dwFileDateLS + (((long) nativeFfi.dwFileDateMS) << 32);
            FileDate = DateTime.FromFileTime( filetime );
        } // end constructor


        internal Microsoft.Diagnostics.Runtime.VersionInfo ToClrMdVersionInfo()
        {
            var vi = new Microsoft.Diagnostics.Runtime.VersionInfo();
            vi.Major = FileVersion.Major;
            vi.Minor = FileVersion.Minor;
            vi.Revision = FileVersion.Build;
            vi.Patch = FileVersion.Revision;
            return vi;
        } // end ToClrMdVersionInfo()
    } // end class FixedFileInfo


    public class ModuleVersionInfo
    {
        // TODO: Should I provide access to other lang-codepage pairs?
        public readonly FixedFileInfo FixedFileInfo;
        public readonly CultureInfo Language;
        public readonly Encoding CharSet;
        public readonly string Comments;
        public readonly string InternalName;
        public readonly string ProductName;
        public readonly string CompanyName;
        public readonly string LegalCopyright;
        public readonly string ProductVersion;
        public readonly string FileDescription;
        public readonly string LegalTrademarks;
        public readonly string PrivateBuild;
        public readonly string FileVersion;
        public readonly string OriginalFilename;
        public readonly string SpecialBuild;

        public ModuleVersionInfo( FixedFileInfo fixedFileInfo,
                                  CultureInfo language,
                                  Encoding charSet,
                                  string comments,
                                  string internalName,
                                  string productName,
                                  string companyName,
                                  string legalCopyright,
                                  string productVersion,
                                  string fileDescription,
                                  string legalTrademarks,
                                  string privateBuild,
                                  string fileVersion,
                                  string originalFilename,
                                  string specialBuild )
        {
            FixedFileInfo = fixedFileInfo;
            Language = language;
            CharSet = charSet;
            Comments = comments;
            InternalName = internalName;
            ProductName = productName;
            CompanyName = companyName;
            LegalCopyright = legalCopyright;
            ProductVersion = productVersion;
            FileDescription = fileDescription;
            LegalTrademarks = legalTrademarks;
            PrivateBuild = privateBuild;
            FileVersion = fileVersion;
            OriginalFilename = originalFilename;
            SpecialBuild = specialBuild;
        } // end constructor

        public override string ToString()
        {
            if( !String.IsNullOrEmpty( FileVersion ) )
                return FileVersion;
            else if( null != FixedFileInfo )
                return FixedFileInfo.FileVersion.ToString();
            else
                return String.Empty;
        } // end ToString()
    } // end class ModuleVersionInfo
}

