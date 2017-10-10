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
    } // end class ReadDbgMemoryCommand
}
