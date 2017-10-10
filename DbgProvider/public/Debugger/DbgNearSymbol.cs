using System;

namespace MS.Dbg
{
    public sealed class DbgNearSymbol
    {
        public readonly ulong BaseAddress;
        public readonly long Displacement;
        public readonly DbgSymbol Symbol;
        // Caused by BBT:
        public readonly bool DoesNotMakeMathematicalSense;


        /// <summary>
        ///    A shortcut for Displacement == 0.
        /// </summary>
        public bool IsExactMatch { get { return 0 == Displacement; } }


        public DbgNearSymbol( ulong baseAddress,
                              long displacement,
                              DbgSymbol symbol )
        {
            if( null == symbol )
                throw new ArgumentNullException( "symbol" );

            BaseAddress = baseAddress;
            Displacement = displacement;
            Symbol = symbol;

            if( (ulong) ((long) baseAddress + displacement) != symbol.Address )
            {
                // This can be caused by optimization tools (like BBT) which operate on
                // already-built binaries, because they might move stuff around, but
                // offsets in the PDB are not adjusted.
                DoesNotMakeMathematicalSense = true;
             // if( (ulong) ((long) baseAddress - displacement) == symbol.Address )
             //     Util.Fail( "We got the displacement backwards!" );
            }
        } // end constructor
    } // end class DbgNearSymbol
}

