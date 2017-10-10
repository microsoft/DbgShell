/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Management.Automation;
using System.Management.Automation.Host;
using Microsoft.Win32;
using MS.Dbg;


namespace MS.DbgShell
{
    // Taken from engine\Utils.cs
    internal class Utils
    {
        // From System.Web.Util.HashCodeCombiner
        internal static int CombineHashCodes(int h1, int h2)
        {
            return unchecked(((h1 << 5) + h1) ^ h2);
        }

        internal static int CombineHashCodes(int h1, int h2, int h3)
        {
            return CombineHashCodes(CombineHashCodes(h1, h2), h3);
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4)
        {
            return CombineHashCodes(CombineHashCodes(h1, h2), CombineHashCodes(h3, h4));
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5)
        {
            return CombineHashCodes(CombineHashCodes(h1, h2, h3, h4), h5);
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5, int h6)
        {
            return CombineHashCodes(CombineHashCodes(h1, h2, h3, h4), CombineHashCodes(h5, h6));
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5, int h6, int h7)
        {
            return CombineHashCodes(CombineHashCodes(h1, h2, h3, h4), CombineHashCodes(h5, h6, h7));
        }

        internal static int CombineHashCodes(int h1, int h2, int h3, int h4, int h5, int h6, int h7, int h8)
        {
            return CombineHashCodes(CombineHashCodes(h1, h2, h3, h4), CombineHashCodes(h5, h6, h7, h8));
        }


        /// <summary>
        /// Gets the application base for current monad version
        /// </summary>
        /// <returns>
        /// applicationbase path for current monad version installation
        /// </returns>
        /// <exception cref="SecurityException">
        /// if caller doesn't have permission to read the key
        /// </exception>
        internal static string GetApplicationBase(string shellId)
        {
#if CORECLR 
        //  // Use the location of SMA.dll as the application base
        //  // Assembly.GetEntryAssembly and GAC are not in CoreCLR.
        //  Assembly assembly = typeof(PSObject).GetTypeInfo().Assembly;
        //  return Path.GetDirectoryName(assembly.Location);
#else
        //  // This code path applies to Windows FullCLR inbox deployments. All CoreCLR 
        //  // implementations should use the location of SMA.dll since it must reside in PSHOME.
        //  //
        //  // try to get the path from the registry first
        //  string result = GetApplicationBaseFromRegistry(shellId);
        //  if (result != null)
        //  {
        //      return result;
        //  }
            
            // The default keys aren't installed, so try and use the entry assembly to
            // get the application base. This works for managed apps like minishells...
            Assembly assem = Assembly.GetEntryAssembly();
            if (assem != null)
            {
                // For minishells, we just return the executable path. 
                return Path.GetDirectoryName(assem.Location);
            }

            // For unmanaged host apps, look for the SMA dll, if it's not GAC'ed then
            // use it's location as the application base...
            assem = typeof(PSObject).GetTypeInfo().Assembly;
            string gacRootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.Net\\assembly");
            if (!assem.Location.StartsWith(gacRootPath, StringComparison.OrdinalIgnoreCase))
            {
                // For other hosts. 
                return Path.GetDirectoryName(assem.Location);
            }

            // otherwise, just give up...
            return "";
#endif
        }

        /// <summary>
        /// String representing the Default shellID.
        /// </summary>
        internal static string DefaultPowerShellShellID = "Microsoft.PowerShell";
        /// <summary>
        /// String used to control directory location for PowerShell
        /// </summary>
        /// <remarks>
        /// Profile uses this to control profile loading.
        /// </remarks>
        internal const string ProductNameForDirectory = "WindowsPowerShell";

        internal static string GetRegistryConfigurationPrefix()
        {
            // For 3.0 PowerShell, we still use "1" as the registry version key for 
            // Snapin and Custom shell lookup/discovery.
            // For 3.0 PowerShell, we use "3" as the registry version key only for Engine
            // related data like ApplicationBase etc.
            return "SOFTWARE\\Microsoft\\PowerShell\\" + PSVersionInfo.RegistryVersion1Key + "\\ShellIds";
        }

        internal static string GetRegistryConfigurationPath(string shellID)
        {
            return GetRegistryConfigurationPrefix() + "\\" + shellID;
        }


        internal static class Separators
        {
            internal static readonly char[] Backslash = new char[] { '\\' };
            internal static readonly char[] Directory = new char[] { '\\', '/' };
            internal static readonly char[] DirectoryOrDrive = new char[] { '\\', '/', ':' };

            internal static readonly char[] Colon = new char[] { ':' };
            internal static readonly char[] Dot = new char[] { '.' };
            internal static readonly char[] Pipe = new char[] { '|' };
            internal static readonly char[] Comma = new char[] { ',' };
            internal static readonly char[] Semicolon = new char[] { ';' };
            internal static readonly char[] StarOrQuestion = new char[] { '*', '?' };
            internal static readonly char[] ColonOrBackslash = new char[] { '\\', ':' };
            internal static readonly char[] PathSeparator = new char[] { Path.PathSeparator };

            internal static readonly char[] QuoteChars = new char[] { '\'', '"' };
            internal static readonly char[] Space = new char[] { ' ' };
            internal static readonly char[] QuotesSpaceOrTab = new char[] { ' ', '\t', '\'', '"' };
            internal static readonly char[] SpaceOrTab = new char[] { ' ', '\t' };
            internal static readonly char[] Newline = new char[] { '\n' };
            internal static readonly char[] CrLf = new char[] { '\r', '\n' };

            // (Copied from System.IO.Path so we can call TrimEnd in the same way that Directory.EnumerateFiles would on the search patterns).
            // Trim trailing white spaces, tabs etc but don't be aggressive in removing everything that has UnicodeCategory of trailing space.
            // String.WhitespaceChars will trim aggressively than what the underlying FS does (for ex, NTFS, FAT).    
            internal static readonly char[] PathSearchTrimEnd = { (char)0x9, (char)0xA, (char)0xB, (char)0xC, (char)0xD, (char)0x20, (char)0x85, (char)0xA0 };
        }


        // Taken from StringUtils:
        internal static string TruncateToBufferCellWidth(
            PSHostRawUserInterface rawUI,
            string toTruncate,
            int maxWidthInBufferCells)
        {
            Util.Assert(rawUI != null, "need a reference");
            Util.Assert(maxWidthInBufferCells >= 0, "maxWidthInBufferCells must be positive");

            string result;
            int i = Math.Min(toTruncate.Length, maxWidthInBufferCells);

            do
            {
                result = toTruncate.Substring(0, i);
                int cellCount = rawUI.LengthInBufferCells(result);
                if (cellCount <= maxWidthInBufferCells)
                {
                    // the segment from start..i fits

                    break;
                }
                else
                {
                    // The segment does not fit, back off a tad until it does
                    // We need to back off 1 by 1 because there could theoretically 
                    // be characters taking more 2 buffer cells
                    --i;
                }
            } while (true);

            return result;
        }
    }
}   // namespace

