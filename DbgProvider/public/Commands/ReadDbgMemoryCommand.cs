using System;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommunications.Read, "DbgMemory" )]
    [OutputType( typeof( DbgMemory ) )]
    public class ReadDbgMemoryCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true )]
        [AddressTransformation]
        public ulong Address { get; set; }

        [Parameter( Mandatory = false, Position = 1 )]
        public uint LengthInBytes { get; set; }

        [Parameter( Mandatory = false )]
        public DbgMemoryDisplayFormat DefaultDisplayFormat { get; set; }

        [Parameter( Mandatory = false )]
        [Alias( "Columns" )]
        public uint DefaultDisplayColumns { get; set; }


        static ReadDbgMemoryCommand()
        {
            NextDefaultFormat = DbgMemoryDisplayFormat.PointersWithAscii;
        }

        // Normally you can't have [non-const] static members in PowerShell stuff, thanks
        // to runspaces, but dbgeng (and therefore dbgshell) doesn't support more than
        // one instance per process.
        internal static ulong NextAddressToDump { get; set; }
        internal static DbgMemoryDisplayFormat NextDefaultFormat { get; set; }
        // Windbg doesn't store the columns or length, but we're more awesome.
        internal static uint NextDefaultLengthInBytes { get; set; }
        internal static uint NextDefaultNumColumns { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( 0 == Address )
            {
                Address = NextAddressToDump;
            }

            if( !MyInvocation.BoundParameters.ContainsKey( "DefaultDisplayFormat" ) )
            {
                DefaultDisplayFormat = NextDefaultFormat;
            }

            if( !MyInvocation.BoundParameters.ContainsKey( "LengthInBytes" ) )
            {
                if( DefaultDisplayFormat != NextDefaultFormat )
                {
                    // We're switching formats; may need to also switch the default size.
                    LengthInBytes = _GetDefaultReadSize( DefaultDisplayFormat );
                }
                else
                {
                    LengthInBytes = NextDefaultLengthInBytes;
                }
            }

            if( 0 == LengthInBytes )
            {
                LengthInBytes = _GetDefaultReadSize( DefaultDisplayFormat );
            }

            // Okay, we've got a length + display format, but one or the other may be
            // defaulted. In that case, we want to make sure that the LengthInBytes is
            // compatible with the display format. (And actually, even if they were both
            // explicitly specified, we /still/ want to make sure they are compatible.)
            //
            // For instance, if the LengthInBytes was explicitly specified as 4, and the
            // display format got defaulted to PointersWithAscii, on a 64-bit target, we
            // are not going to be able to display anything (because we won't have read
            // even a single full pointer)--we need to downgrade the display format to fit
            // the size requested, if possible.

            if( !_IsDisplayFormatCompatibleWithReadSize( LengthInBytes, DefaultDisplayFormat, Debugger.PointerSize ) )
            {
                var compatibleDisplayFormat = _GetDefaultDisplayFormatForSize( LengthInBytes, Debugger.PointerSize );

                if( MyInvocation.BoundParameters.ContainsKey( "DefaultDisplayFormat" ) )
                {
                    SafeWriteWarning( "The specified display format ({0}) is not compatible with the byte length requested ({1}). The {2} format will be used instead.",
                                      DefaultDisplayFormat,
                                      LengthInBytes,
                                      compatibleDisplayFormat );
                }
                // else just silently fix it up.

                DefaultDisplayFormat = compatibleDisplayFormat;
            }

            if( !MyInvocation.BoundParameters.ContainsKey( "DefaultDisplayColumns" ) )
            {
                DefaultDisplayColumns = NextDefaultNumColumns;
            }

            var raw = Debugger.ReadMem( Address, LengthInBytes, false );
            if( raw.Length != LengthInBytes )
            {
                WriteWarning( Util.Sprintf( "Actual memory read (0x{0:x} bytes) is less than requested (0x{1:x} bytes).",
                                            raw.Length,
                                            LengthInBytes ) );
            }
            var mem = new DbgMemory( Address,
                                     raw,
                                     Debugger.TargetIs32Bit,
                                     false, // TODO: actually determine endianness
                                     Debugger );
            mem.DefaultDisplayFormat = DefaultDisplayFormat;
            mem.DefaultDisplayColumns = DefaultDisplayColumns;

            NextAddressToDump = Address + (ulong) raw.Length;
            NextDefaultFormat = DefaultDisplayFormat;
            NextDefaultLengthInBytes = LengthInBytes;
            NextDefaultNumColumns = DefaultDisplayColumns;

            WriteObject( mem );

            DbgProvider.SetAutoRepeatCommand( Util.Sprintf( "{0}", MyInvocation.InvocationName ) );
        } // end ProcessRecord()


        private static uint _GetDefaultReadSize( DbgMemoryDisplayFormat fmt )
        {
            if( (fmt == DbgMemoryDisplayFormat.AsciiOnly) ||
                (fmt == DbgMemoryDisplayFormat.UnicodeOnly) )
            {
                return 704; // This is an oddball default... but it seems to be what windbg does.
            }
            else
            {
                return 128;
            }
        } // end _GetDefaultReadSize()


        private static DbgMemoryDisplayFormat _GetDefaultDisplayFormatForSize( uint readSize, uint pointerSize )
        {
            if( 0 == (readSize % pointerSize) )
            {
                return DbgMemoryDisplayFormat.PointersWithAscii;
            }
            else if( 0 == (readSize % 4) )
            {
                return DbgMemoryDisplayFormat.DWordsWithAscii;
            }
            else
            {
                return DbgMemoryDisplayFormat.Bytes;
            }
        }


        private static uint _ElementSizeForDisplayFormat( DbgMemoryDisplayFormat format, uint pointerSize )
        {
            switch( format )
            {
                case DbgMemoryDisplayFormat.AsciiOnly:
                case DbgMemoryDisplayFormat.Bytes:
                    return 1;
                case DbgMemoryDisplayFormat.UnicodeOnly:
                case DbgMemoryDisplayFormat.Words:
                case DbgMemoryDisplayFormat.WordsWithAscii:
                    return 2;
                case DbgMemoryDisplayFormat.RGB:
                    return 1; // Technically should be 3 but want to allow flexibility for line padding
                case DbgMemoryDisplayFormat.DWords:
                case DbgMemoryDisplayFormat.DWordsWithAscii:
                case DbgMemoryDisplayFormat.DWordsWithBits:
                case DbgMemoryDisplayFormat.RGBA:
                    return 4;
                case DbgMemoryDisplayFormat.QWords:
                case DbgMemoryDisplayFormat.QWordsWithAscii:
                    return 8;
                case DbgMemoryDisplayFormat.Pointers:
                case DbgMemoryDisplayFormat.PointersWithSymbols:
                case DbgMemoryDisplayFormat.PointersWithAscii:
                case DbgMemoryDisplayFormat.PointersWithSymbolsAndAscii:
                    return pointerSize;
                default:
                    throw new NotImplementedException();
            }
        }


        /// <summary>
        ///    Returns "true" if the number of bytes read (readSize) is evenly divisible
        ///    by the element size implied by the display format.
        /// </summary>
        private static bool _IsDisplayFormatCompatibleWithReadSize( uint readSize,
                                                                    DbgMemoryDisplayFormat format,
                                                                    uint pointerSize )
        {
            uint elemSize = _ElementSizeForDisplayFormat( format, pointerSize );

            return 0 == (readSize % elemSize);
        }
    } // end class ReadDbgMemoryCommand
}
