using Microsoft.Diagnostics.Runtime;

namespace MS.Dbg
{
    public class ClrObject
    {
        public ClrObject( ulong address, ClrType clrType )
        {
            Address = address;
            ClrType = clrType;
        }

        public readonly ulong Address;
        public readonly ClrType ClrType;
    }
}
