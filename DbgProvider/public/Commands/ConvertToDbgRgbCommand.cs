using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading.Tasks;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsData.ConvertTo, "DbgRgb" )]
    [OutputType( typeof( ColorString ) )]
    public class ConvertToDbgRgbCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true,
            Position = 0,
            ValueFromPipeline = true)]
        public DbgMemory Memory { get; set; }

        [Parameter( Mandatory = false )]
        public uint Columns { get; set; }

        [Parameter( Mandatory = false )]
        public SwitchParameter Alpha { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            WriteObject( FormatRGB( Columns, Alpha ) );
        }

        private static readonly string[] AlphaChars = { "░", "▒", "▓", "█" };

        private ColorString FormatRGB( uint numColumns, bool withAlpha )
        {
            var bytes = Memory.Bytes;
            bool is32Bit = Debugger.TargetIs32Bit;
            ulong startAddress = Memory.StartAddress;
            if( 0 == numColumns )
                numColumns = 64;

            ColorString cs = new ColorString();

            var bytesPerCharacter = withAlpha ? 4 : 3;
            int bytesPerRow = (int) numColumns * bytesPerCharacter + 3 & ~(3); //round up 

            for( int rowStart = 0; rowStart + bytesPerCharacter < bytes.Count; rowStart += bytesPerRow )
            {
                if( rowStart != 0 )
                {
                    cs.AppendLine();
                }
                cs.Append( DbgProvider.FormatAddress( startAddress + (uint) rowStart, is32Bit, true, true ) ).Append( "  " );

                var rowLen = Math.Min( bytes.Count - rowStart, bytesPerRow );

                for( int colOffset = 0; colOffset + bytesPerCharacter < rowLen; colOffset += bytesPerCharacter )
                {
                    byte b = bytes[ rowStart + colOffset + 0 ];
                    byte g = bytes[ rowStart + colOffset + 1 ];
                    byte r = bytes[ rowStart + colOffset + 2 ];
                    string ch = "█";
                    if( withAlpha )
                    {
                        ch = AlphaChars[ bytes[ rowStart + colOffset + 3 ] >> 6 ];
                    }
                    cs.AppendFgRgb( r, g, b, ch );
                }
            }

            return cs.MakeReadOnly();

        }
    }
}
