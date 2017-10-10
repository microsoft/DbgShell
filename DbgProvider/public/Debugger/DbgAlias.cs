using System;

namespace MS.Dbg
{
    /// <summary>
    ///    Information about a dbgeng alias, or "text replacement".
    /// </summary>
    public class DbgAlias
    {
        /// <summary>
        ///    The name of the alias, including the '$'.
        /// </summary>
        public readonly string Name;

        public readonly string Value;

        /// <summary>
        ///    Note that the index is not stable: if you retrieve an alias by index, be
        ///    sure to check that the name matches what you expect.
        /// </summary>
        public readonly uint Index;


        public DbgAlias( string name,
                         string value,
                         uint index )
        {
            Name = name;
            Value = value;
            Index = index;
        }
    } // end class DbgAlias
}

