using System;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommunications.Write, "DbgMemory", DefaultParameterSetName = "DWordsParamSet" )]
    public class WriteDbgMemoryCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true,
                    Position = 0 )]
        [AddressTransformation]
        public ulong Address { get; set; }

        //
        // No pipelining of input data:
        //
        // This cmdlet accepts a byte[] as input. Suppose we allowed piping it in:
        //
        //    $myBytes | Write-DbgMemory -Address 0x12345678
        //
        // Seems like it would be nice, right? However, due to the nature of how PS does
        // pipelining, it's a bad idea: PS "unrolls" the input array, and would call
        // Write-DbgMemory N times with a 1-element array each time (resulting in N calls
        // to WriteMemory), where N is the size of $myBytes. This would be terrible, so
        // we're just not going to allow pipelining the content at all.
        //

        [Parameter( Mandatory = true,
                    Position = 1,
                    ValueFromPipeline = false, // NO pipelining: see comment above
                    ParameterSetName = "BytesParamSet" )]
        [ValidateNotNull]
        public byte[] Bytes { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
                    ValueFromPipeline = false, // NO pipelining: see comment above
                    ParameterSetName = "DWordsParamSet" )]
        [ValidateNotNull]
        public uint[] DWords { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
                    ValueFromPipeline = false, // NO pipelining: see comment above
                    ParameterSetName = "QWordsParamSet" )]
        [ValidateNotNull]
        public ulong[] QWords { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
                    ValueFromPipeline = false, // NO pipelining: see comment above
                    ParameterSetName = "DbgMemoryParamSet" )]
        [ValidateNotNull]
        public DbgMemory Memory { get; set; }


        //
        // It is tempting to add other parameter set overloads for signed types (Int32,
        // Int64, etc.), but don't do it--it will confuse parameter set resolution.
        //
        // (e.g. "Write-DbgMemory $rsp $stuff" will no longer work, because PS won't be
        // able to figure out which parameter set to use--the signed or unsigned variant)
        //


        protected override void ProcessRecord()
        {
            if( null != Bytes )
                Debugger.WriteMem( Address, Bytes );
            else if( null != DWords )
                Debugger.WriteMem( Address, DWords );
            else if( null != QWords )
                Debugger.WriteMem( Address, QWords );
            else
            {
                Util.Assert( null != Memory );
                Debugger.WriteMem( Address, Memory.GetMemory() );
            }
        }
    }
}
