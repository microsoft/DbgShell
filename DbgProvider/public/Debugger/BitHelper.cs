using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MS.Dbg
{
    internal static class BitHelper
    {
        // This looks like an interview question... probably a better (fancy math) way, but
        // it's midnight so I'm coding this up the first way it pops into my head.
        public static IEnumerable<object> EnumBitsThatAreSet( uint ui )
        {
            uint mask = 0x00000001;
            while( (mask != 0x80000000) && (mask <= ui) )
            {
                if( 0 != (mask & ui) )
                    yield return mask;

                mask <<= 1;
            }
        } // end EnumBitsThatAreSet( uint )

        public static IEnumerable<object> EnumBitsThatAreSet( int i )
        {
            foreach( uint ui in EnumBitsThatAreSet( (uint) i ) )
                yield return (int) ui;
        } // end EnumBitsThatAreSet( int )

        public static IEnumerable<object> EnumBitsThatAreSet( ushort us )
        {
            ushort mask = 0x0001;
            while( (mask != 0x8000) && (mask <= us) )
            {
                if( 0 != (mask & us) )
                    yield return mask;

                mask <<= 1;
            }
        } // end EnumBitsThatAreSet( ushort )

        public static IEnumerable<object> EnumBitsThatAreSet( short s )
        {
            foreach( ushort us in EnumBitsThatAreSet( (ushort) s ) )
                yield return (short) us;
        } // end EnumBitsThatAreSet( short )

        public static IEnumerable<object> EnumBitsThatAreSet( ulong ul )
        {
            ulong mask = 0x0000000000000001;
            while( (mask != 0x8000000000000000) && (mask <= ul) )
            {
                if( 0 != (mask & ul) )
                    yield return mask;

                mask <<= 1;
            }
        } // end EnumBitsThatAreSet( ulong )

        public static IEnumerable<object> EnumBitsThatAreSet( long lVal )
        {
            foreach( ulong ul in EnumBitsThatAreSet( (ulong) lVal ) )
                yield return (long) ul;
        } // end EnumBitsThatAreSet( long )

        public static IEnumerable<object> EnumBitsThatAreSet( byte b )
        {
            byte mask = 0x01;
            while( (mask != 0x80) && (mask <= b) )
            {
                if( 0 != (mask & b) )
                    yield return mask;

                mask <<= 1;
            }
        } // end EnumBitsThatAreSet( byte )

        public static IEnumerable<object> EnumBitsThatAreSet( sbyte sb )
        {
            foreach( byte b in EnumBitsThatAreSet( (byte) sb ) )
                yield return (sbyte) b;
        } // end EnumBitsThatAreSet( sbyte )


        // I don't actually know if any compiler will generate bitfields with all these
        // different types.

        public static sbyte ExtractBitfield( sbyte val, uint bitfieldPos, uint bitfieldLen )
        {
            Util.Assert( (bitfieldPos + bitfieldLen) <= (Marshal.SizeOf( typeof( sbyte ) ) * 8) );
            return (sbyte) ExtractBitfield( (ulong) val, bitfieldPos, bitfieldLen );
        }

        public static byte ExtractBitfield( byte val, uint bitfieldPos, uint bitfieldLen )
        {
            Util.Assert( (bitfieldPos + bitfieldLen) <= (Marshal.SizeOf( typeof( byte ) ) * 8) );
            return (byte) ExtractBitfield( (ulong) val, bitfieldPos, bitfieldLen );
        }

        public static short ExtractBitfield( short val, uint bitfieldPos, uint bitfieldLen )
        {
            Util.Assert( (bitfieldPos + bitfieldLen) <= (Marshal.SizeOf( typeof( short ) ) * 8) );
            return (short) ExtractBitfield( (ulong) val, bitfieldPos, bitfieldLen );
        }

        public static ushort ExtractBitfield( ushort val, uint bitfieldPos, uint bitfieldLen )
        {
            Util.Assert( (bitfieldPos + bitfieldLen) <= (Marshal.SizeOf( typeof( ushort ) ) * 8) );
            return (ushort) ExtractBitfield( (ulong) val, bitfieldPos, bitfieldLen );
        }

        public static int ExtractBitfield( int val, uint bitfieldPos, uint bitfieldLen )
        {
            Util.Assert( (bitfieldPos + bitfieldLen) <= (Marshal.SizeOf( typeof( int ) ) * 8) );
            return (int) ExtractBitfield( (ulong) val, bitfieldPos, bitfieldLen );
        }

        public static uint ExtractBitfield( uint val, uint bitfieldPos, uint bitfieldLen )
        {
            Util.Assert( (bitfieldPos + bitfieldLen) <= (Marshal.SizeOf( typeof( uint ) ) * 8) );
            return (uint) ExtractBitfield( (ulong) val, bitfieldPos, bitfieldLen );
        }

        public static long ExtractBitfield( long val, uint bitfieldPos, uint bitfieldLen )
        {
            Util.Assert( (bitfieldPos + bitfieldLen) <= (Marshal.SizeOf( typeof( long ) ) * 8) );
            return (long) ExtractBitfield( (ulong) val, bitfieldPos, bitfieldLen );
        }

        public static ulong ExtractBitfield( ulong val, uint bitfieldPos, uint bitfieldLen )
        {
            Util.Assert( (bitfieldPos + bitfieldLen) <= (Marshal.SizeOf( typeof( ulong ) ) * 8) );

            int leftShift = 64 - (int) (bitfieldPos + bitfieldLen);
            Util.Assert( leftShift >= 0 );
            val = val << leftShift;
            val = val >> leftShift + (int) bitfieldPos;
            return val;
        }

    } // end class BitHelper
}

