using System;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommunications.Write, "DbgMemory", DefaultParameterSetName = "DWordsParamSet" )]
    public class WriteDbgMemoryCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true )]
        [AddressTransformation]
        public ulong Address { get; set; }


        [Parameter( Mandatory = true,
                    Position = 1,
                    ParameterSetName = "BytesParamSet" )]
        [ValidateNotNull]
        public byte[] Bytes { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
                    ParameterSetName = "DWordsParamSet" )]
        [ValidateNotNull]
        public uint[] DWords { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
                    ParameterSetName = "QWordsParamSet" )]
        [ValidateNotNull]
        public ulong[] QWords { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
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
                Debugger.WriteMem( Address, Memory._GetBackingBytes() );
            }
        }
    }
}
