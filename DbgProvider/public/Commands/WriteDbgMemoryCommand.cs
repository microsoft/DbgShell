using System;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommunications.Write, "DbgMemory", DefaultParameterSetName = "DWordsParamSet" )]
    public class WriteDbgMemoryCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true,
                    Position = 0 )]
        [AddressTransformation( FailGracefully = true )] // FailGracefully to allow ValueFromPipelineByPropertyName to work
        public ulong Address { get; set; }


        [Parameter( Mandatory = true,
                    Position = 1,
                    ValueFromPipeline = true,
                    ParameterSetName = "BytesParamSet" )]
        [ValidateNotNull]
        public byte[] Bytes { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
                    ValueFromPipeline = true,
                    ParameterSetName = "DWordsParamSet" )]
        [ValidateNotNull]
        public uint[] DWords { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
                    ValueFromPipeline = true,
                    ParameterSetName = "QWordsParamSet" )]
        [ValidateNotNull]
        public ulong[] QWords { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
                    ValueFromPipeline = true,
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
            uint writeSize = 0;
            if( null != Bytes )
            {
                Debugger.WriteMem( Address, Bytes );
                writeSize = (uint) Bytes.Length;
            }
            else if( null != DWords )
            {
                Debugger.WriteMem( Address, DWords );
                writeSize = (uint) DWords.Length * 4;
            }
            else if( null != QWords )
            {
                Debugger.WriteMem( Address, QWords );
                writeSize = (uint) QWords.Length * 8;
            }
            else
            {
                Util.Assert( null != Memory );
                // TODO: use some fancy new C# features to get a span or array view or
                // something so I don't have to have this crummy internal accessor in
                // order to avoid a copy.
                var bytes = Memory._GetBackingBytes();
                Debugger.WriteMem( Address, bytes );
                writeSize = (uint) bytes.Length;
            }

            // TODO: the pipelining is quite terrible: if you have a byte[] of a hundred
            // bytes, and pipe it in, PS will slice the array into a hundred different
            // byte arrays of a single byte each, and then call ProcessRecord a hundred
            // times. Oy.
            Address += writeSize; // For the pipelining case
        }
    }
}
