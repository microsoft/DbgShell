using System;

namespace MS.Dbg
{
    public class DbgSourceLineInfo
    {
        public readonly ulong Address;
        public readonly String File;
        public readonly uint Line;

        // The number of bytes from the first address that is also considered to be on the
        // same line number in the same file.
        public readonly ulong Displacement;

        public DbgSourceLineInfo( ulong address,
                                  string file,
                                  uint line,
                                  ulong displacement )
        {
            Address = address;
            File = file;
            Line = line;
            Displacement = displacement;
        }

        public override string ToString()
        {
            return File + ":" + Line.ToString();
        }
    } // end class DbgSourceLineInfo
}

