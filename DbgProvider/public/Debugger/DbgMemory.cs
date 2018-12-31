using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace MS.Dbg
{
    //
    // Two possible approaches to Read-DbgMemory:
    //
    //    1. Memory region approach: Read-DbgMemory always succeeds. It returns a list of
    //       regions, some may be valid, some may not be.
    //
    //    2. Valid-only approach: Read-DbgMemory only reads valid memory. If a read
    //       request straddles a valid/invalid boundary, you get a warning, and a
    //       DbgMemory object that is smaller than you thought. If a read request is in
    //       invalid memory, you get an error.
    //
    // A disadvantage of #2 is that you can't easily get an output experience like windbg.
    // But it's easier to code. And probably easier to script against for common
    // scenarios. So I'll start with that.
    //

    public enum DbgMemoryDisplayFormat
    {
        AsciiOnly,
        Bytes,
        UnicodeOnly,
        Words,
        WordsWithAscii,
        DWords,
        DWordsWithAscii,
        DWordsWithBits,
        QWords,
        QWordsWithAscii,
        Pointers,
        PointersWithSymbols,
        PointersWithAscii,
        PointersWithSymbolsAndAscii
    }

    public class DbgMemory : ISupportColor, ICloneable
    {
        private readonly byte[] m_bytes;
        private bool m_is32Bit;
        private Func< ulong, ColorString > m_lookupSymbol;

        public readonly ulong StartAddress;

        public uint Length { get { return (uint) m_bytes.Length; } }

        public bool IsBigEndian { get; private set; }

        public DbgMemoryDisplayFormat DefaultDisplayFormat { get; set; }

        public uint DefaultDisplayColumns { get; set; }


        public DbgMemory( byte[] bytes )
            : this( 0, bytes, false, false, ( x ) => { return new ColorString( ConsoleColor.DarkGray, "<symbol lookup function not provided>" ); } )
        {
        }

        public DbgMemory( byte[] bytes, DbgEngDebugger debugger )
            : this( 0, bytes, false, debugger )
        {
        }

        public DbgMemory( ulong address,
                          byte[] bytes,
                          bool is32bit,
                          DbgEngDebugger debugger )
            : this( address, bytes, is32bit, false, (x) => _DefaultSymLookup( debugger, x ) )
        {
        }


        public DbgMemory( ulong address,
                          byte[] bytes,
                          bool is32bit,
                          bool isBigEndian,
                          DbgEngDebugger debugger )
            : this( address, bytes, is32bit, isBigEndian, (x) => _DefaultSymLookup( debugger, x ) )
        {
        }

        public DbgMemory( ulong address,
                          byte[] bytes,
                          bool is32bit,
                          bool isBigEndian,
                          Func< ulong, ColorString > lookupSymbol )
        {
            if( null == bytes )
                throw new ArgumentNullException( "bytes" );

            if( 0 == bytes.Length )
                throw new ArgumentOutOfRangeException( "bytes.Length",
                                                       bytes.Length,
                                                       "You must give at least one byte." );

            StartAddress = address;
            m_bytes = bytes;
            IsBigEndian = isBigEndian;
            m_is32Bit = is32bit;
            m_lookupSymbol = lookupSymbol;

            if( isBigEndian )
                throw new NotSupportedException( "No support for big-endian data yet." );

            DefaultDisplayFormat = DbgMemoryDisplayFormat.Bytes;
        } // end constructor


        // When I first tried out the Span/Memory stuff, I originally planned to expose
        // the Bytes only as a ReadOnlyMemory< byte >, but ran into some complications.
        //
        // 1. I had hoped to also expose the other properties as ReadOnlyMemory< T > views
        //    of the same underlying byte[] (without a copy), but couldn't figure out a
        //    way to do it: MemoryMarshal.Cast only works to give you a Span, and Spans
        //    aren't good enough for me, because I often need to pass stuff to the dbgeng
        //    thread in a lambda (and you can't do that with a Span). So that left the
        //    Bytes property having a different type than the other properties
        //    (ReadOnlyMemory< T > versus IReadOnlyList< T >
        //
        // 2. The Memory/Span stuff does not work very well in PowerShell script. Span< T
        //    > does not implement IEnumerable< T >, so PowerShell does not unroll it,
        //    even though there is a GetEnumerator() method. Secondly, trying to directly
        //    access elements of a span ($memoryThing.Span[123]) yields a
        //    VerificationException saying "Operation could destabilize the runtime."
        //    Yikes!
        //
        // So for now, Memory/Span stuff needs to only be used internally, or if exposed
        // (such as in a C# method signature), you can't expect it to be usable from
        // script.

        // I'll leave this as public for now as an easy demo of the it-doesn't-work-in-
        // script situation.
        public ReadOnlyMemory< byte > GetMemory() { return m_bytes; }


        private ReadOnlyCollection< byte > m_bytesRoCollection;

        public IReadOnlyList< byte > Bytes
        {
            get
            {
                if( null == m_bytesRoCollection )
                {
                    m_bytesRoCollection = new ReadOnlyCollection< byte >( m_bytes );
                }

                return m_bytesRoCollection;
            }
        }

        private ReadOnlyCollection< ushort > m_words;

        public IReadOnlyList< ushort > Words
        {
            get
            {
                if( null == m_words )
                {
                    const int elemSize = 2;
                    int numDWords = m_bytes.Length / elemSize; // Note that we may be missing the last few bytes...
                    ushort[] words = new ushort[ numDWords ];
                    for( int i = 0; i < numDWords; i++ )
                    {
                        int byteIdx = i * elemSize;
                        words[ i ] = (ushort) (((ushort) m_bytes[ byteIdx ]) +
                                              (((ushort) m_bytes[ byteIdx + 1 ]) << 8));
                    } // end for( each dword )
                    m_words = new ReadOnlyCollection< ushort >( words );
                }
                return m_words;
            }
        } // end property Words


        private ReadOnlyCollection< uint > m_dwords;

        public IReadOnlyList< UInt32 > DWords
        {
            get
            {
                if( null == m_dwords )
                {
                    const int elemSize = 4;
                    int numDWords = m_bytes.Length / elemSize; // Note that we may be missing the last few bytes...
                    uint[] dwords = new uint[ numDWords ];
                    for( int i = 0; i < numDWords; i++ )
                    {
                        // TODO: Why did I do it this way instead of Buffer.BlockCopy?
                        int byteIdx = i * elemSize;
                        dwords[ i ] =  ((uint) m_bytes[ byteIdx ]) +
                                      (((uint) m_bytes[ byteIdx + 1 ]) << 8) +
                                      (((uint) m_bytes[ byteIdx + 2 ]) << 16) +
                                      (((uint) m_bytes[ byteIdx + 3 ]) << 24);
                    } // end for( each dword )
                    m_dwords = new ReadOnlyCollection< uint >( dwords );
                }
                return m_dwords;
            }
        } // end property DWords


        private ReadOnlyCollection< ulong > m_qwords;

        public IReadOnlyList< UInt64 > QWords
        {
            get
            {
                if( null == m_qwords )
                {
                    const int elemSize = 8;
                    int numQWords = m_bytes.Length / elemSize; // Note that we may be missing the last few bytes...
                    ulong[] qwords = new ulong[ numQWords ];
                    for( int i = 0; i < numQWords; i++ )
                    {
                        int byteIdx = i * elemSize;
                        qwords[ i ] =  ((uint)  m_bytes[ byteIdx ]) +
                                      (((uint)  m_bytes[ byteIdx + 1 ]) << 8) +
                                      (((uint)  m_bytes[ byteIdx + 2 ]) << 16) +
                                      (((uint)  m_bytes[ byteIdx + 3 ]) << 24) +
                                      (((ulong) m_bytes[ byteIdx + 4 ]) << 32) +
                                      (((ulong) m_bytes[ byteIdx + 5 ]) << 40) +
                                      (((ulong) m_bytes[ byteIdx + 6 ]) << 48) +
                                      (((ulong) m_bytes[ byteIdx + 7 ]) << 56);
                    } // end for( each qword )
                    m_qwords = new ReadOnlyCollection< ulong >( qwords );
                }
                return m_qwords;
            }
        } // end property QWords


        private IReadOnlyList< ulong > m_pointers;

        public IReadOnlyList< UInt64 > Pointers
        {
            get
            {
                if( null == m_pointers )
                {
                    if( !m_is32Bit )
                    {
                        m_pointers = QWords;
                    }
                    else
                    {
                        ulong[] pointers = new ulong[ DWords.Count ];
                        for( int i = 0; i < DWords.Count; i++ )
                        {
                            pointers[ i ] = DWords[ i ];
                        }
                        m_pointers = new ReadOnlyCollection< ulong >( pointers );
                    }
                }
                return m_pointers;
            }
        } // end property Pointers


        public dynamic this[ int index ]
        {
            get
            {
                switch( DefaultDisplayFormat )
                {
                    case DbgMemoryDisplayFormat.AsciiOnly: // or should I expose a char array?
                    case DbgMemoryDisplayFormat.Bytes:
                        return Bytes[ index ];
                    case DbgMemoryDisplayFormat.UnicodeOnly:
                    case DbgMemoryDisplayFormat.Words:
                    case DbgMemoryDisplayFormat.WordsWithAscii:
                        return Words[ index ];
                    case DbgMemoryDisplayFormat.DWords:
                    case DbgMemoryDisplayFormat.DWordsWithAscii:
                    case DbgMemoryDisplayFormat.DWordsWithBits:
                        return DWords[index];
                    case DbgMemoryDisplayFormat.QWords:
                    case DbgMemoryDisplayFormat.QWordsWithAscii:
                        return QWords[ index ];
                    case DbgMemoryDisplayFormat.Pointers:
                    case DbgMemoryDisplayFormat.PointersWithSymbols:
                    case DbgMemoryDisplayFormat.PointersWithAscii:
                    case DbgMemoryDisplayFormat.PointersWithSymbolsAndAscii:
                        if( m_is32Bit )
                            return DWords[ index ];
                        else
                            return QWords[ index ];
                    default:
                        throw new NotImplementedException();
                }
            }
        } // end default indexer


        public int Count
        {
            get
            {
                switch( DefaultDisplayFormat )
                {
                    case DbgMemoryDisplayFormat.AsciiOnly:
                    case DbgMemoryDisplayFormat.Bytes:
                        return Bytes.Count;
                    case DbgMemoryDisplayFormat.UnicodeOnly:
                    case DbgMemoryDisplayFormat.Words:
                    case DbgMemoryDisplayFormat.WordsWithAscii:
                        return Words.Count;
                    case DbgMemoryDisplayFormat.DWords:
                    case DbgMemoryDisplayFormat.DWordsWithAscii:
                    case DbgMemoryDisplayFormat.DWordsWithBits:
                        return DWords.Count;
                    case DbgMemoryDisplayFormat.QWords:
                    case DbgMemoryDisplayFormat.QWordsWithAscii:
                        return QWords.Count;
                    case DbgMemoryDisplayFormat.Pointers:
                    case DbgMemoryDisplayFormat.PointersWithSymbols:
                    case DbgMemoryDisplayFormat.PointersWithAscii:
                    case DbgMemoryDisplayFormat.PointersWithSymbolsAndAscii:
                        if( m_is32Bit )
                            return DWords.Count;
                        else
                            return QWords.Count;
                    default:
                        throw new NotImplementedException();
                }
            }
        } // end property Count


        [Flags]
        private enum AddtlInfo
        {
            None         = 0,
            Ascii        = 0x01,
            Symbols      = 0x02,
        }

        public ColorString ToColorString()
        {
            return ToColorString( DefaultDisplayFormat );
        }

        public ColorString ToColorString( DbgMemoryDisplayFormat format )
        {
            return ToColorString( format, DefaultDisplayColumns );
        }

        public ColorString ToColorString( DbgMemoryDisplayFormat format, uint numColumns )
        {
            // This is a little awkward, but we do it like this (temporarily swapping out
            // the DefaultDisplayFormat) because the formatting routines access the
            // Count and indexer properties, which depend on that.
            DbgMemoryDisplayFormat originalDisplayFormat = DefaultDisplayFormat;
            DefaultDisplayFormat = format;

            try
            {
                switch( format )
                {
                    case DbgMemoryDisplayFormat.AsciiOnly:
                        return _FormatCharsOnly( numColumns, ( c ) =>
                        {
                            if( (c >= 32) && (c < 127) )
                                return c;
                            else
                                return '.';
                        } );
                    case DbgMemoryDisplayFormat.Bytes:
                        return _FormatBlocks( 1, 3, numColumns, AddtlInfo.Ascii );
                    case DbgMemoryDisplayFormat.UnicodeOnly:
                        return _FormatCharsOnly( numColumns, ( c ) =>
                        {
                            // This seems somewhat arbitrary... what about surrogate
                            // pairs, for instance? Will the console ever be able to
                            // display such things?
                            //
                            // The dbgeng "du" command uses "iswprint"... but I don't know
                            // of an easy .NET equivalent. I'll err on the side of trying
                            // to print it; worst case is we get a weird question box
                            // instead of a dot.
                            if( !Char.IsControl( c ) &&
                                !Char.IsSeparator( c ) &&
                                !(Char.GetUnicodeCategory( c ) == UnicodeCategory.PrivateUse) &&
                                !(Char.GetUnicodeCategory( c ) == UnicodeCategory.Format) &&
                                !(Char.GetUnicodeCategory( c ) == UnicodeCategory.OtherNotAssigned) )
                            {
                                return c;
                            }
                            else
                            {
                                return '.';
                            }
                        } );
                    case DbgMemoryDisplayFormat.Words:
                        return _FormatBlocks( 2, 5, numColumns );
                    case DbgMemoryDisplayFormat.WordsWithAscii:
                        return _FormatBlocks( 2, 5, numColumns, AddtlInfo.Ascii );
                    case DbgMemoryDisplayFormat.DWords:
                        return _FormatBlocks( 4, 9, numColumns );
                    case DbgMemoryDisplayFormat.DWordsWithAscii:
                        return _FormatBlocks( 4, 9, numColumns, AddtlInfo.Ascii );
                    case DbgMemoryDisplayFormat.DWordsWithBits:
                        return _FormatBits();
                    case DbgMemoryDisplayFormat.QWords:
                        return _FormatBlocks( 8, 18, numColumns );
                    case DbgMemoryDisplayFormat.QWordsWithAscii:
                        return _FormatBlocks( 8, 18, numColumns, AddtlInfo.Ascii );
                    case DbgMemoryDisplayFormat.Pointers:
                        if( m_is32Bit )
                            return _FormatBlocks( 4, 9, numColumns );
                        else
                            return _FormatBlocks( 8, 18, numColumns );
                    case DbgMemoryDisplayFormat.PointersWithSymbols:
                        // I could complain if numColumns is set... but I'll just ignore it instead.
                        if( m_is32Bit )
                            return _FormatBlocks( 4, 9, 1, AddtlInfo.Symbols );
                        else
                            return _FormatBlocks( 8, 18, 1, AddtlInfo.Symbols );
                    case DbgMemoryDisplayFormat.PointersWithAscii:
                        // I could complain if numColumns is set... but I'll just ignore it instead.
                        if( m_is32Bit )
                            return _FormatBlocks( 4, 9, numColumns, AddtlInfo.Ascii );
                        else
                            return _FormatBlocks( 8, 18, numColumns, AddtlInfo.Ascii );
                    case DbgMemoryDisplayFormat.PointersWithSymbolsAndAscii:
                        // I could complain if numColumns is set... but I'll just ignore it instead.
                        if( m_is32Bit )
                            return _FormatBlocks( 4, 9, 1, AddtlInfo.Symbols | AddtlInfo.Ascii );
                        else
                            return _FormatBlocks( 8, 18, 1, AddtlInfo.Symbols | AddtlInfo.Ascii );
                    default:
                        throw new NotImplementedException();
                }
            }
            finally
            {
                DefaultDisplayFormat = originalDisplayFormat;
            }
        } // end ToColorString()


        private void _AppendChars( StringBuilder sb, int idx, int len )
        {
            while( len > 0 )
            {
                byte b = m_bytes[ idx ];
                if( (b >= 32) && (b < 127) )// 32 = ' ', 127 = DEL
                    sb.Append( (char) b );
                else
                    sb.Append( '.' );

                idx++;
                len--;
            } // end while( len )
        } // end _AppendChars()


        private ColorString _FormatBlocks( int elemSize,
                                           int charsPerBlock,
                                           uint numColumns )
        {
            return _FormatBlocks( elemSize, charsPerBlock, numColumns, AddtlInfo.None );
        }


        /// <summary>
        ///    Zero-pads the given value to the specified [string] length. Highlights the
        ///    non-zero portion in Green.
        /// </summary>
        private static ColorString _ZeroPad( ulong val, int desiredLen )
        {
            ColorString cs = null;

            // We need a special case for ulongs to put the ` in.
            if( 16 == desiredLen )
            {
                ulong hi = (val & 0xffffffff00000000) >> 32;
                ulong lo =  val & 0x00000000ffffffff;

                cs = _ZeroPad( hi, 8 );

                // If 'hi' is non-zero, force the bottom half to also be highlighted, even
                // if the bottom half is all zeroes.
                if( 0 != hi )
                    cs.AppendPushFg( ConsoleColor.Green );

                cs.Append( "`" );
                cs.Append( _ZeroPad( lo, 8 ) );

                if( 0 != hi )
                    cs.AppendPop();

                return cs;
            }

            if( 0 == val )
                return new String( '0', desiredLen );

            string noLeadingZeroes = val.ToString( "x" );
            if( noLeadingZeroes.Length == desiredLen )
                return new ColorString( ConsoleColor.Green, noLeadingZeroes );

            Util.Assert( desiredLen > noLeadingZeroes.Length );

            cs = new ColorString( new String( '0', desiredLen - noLeadingZeroes.Length ) );
            cs.AppendPushPopFg( ConsoleColor.Green, noLeadingZeroes );
            return cs;
        } // end _ZeroPad()


        private ColorString _FormatBlocks( int elemSize,
                                           int charsPerBlock,
                                           uint numColumns,
                                           AddtlInfo addtlInfo )
        {
            if( 0 == numColumns )
                numColumns = (uint) (16 / elemSize);

            if( addtlInfo.HasFlag( AddtlInfo.Symbols ) )
            {
                numColumns = 1;
            }

            int desiredLen = elemSize * 2;
            ColorString cs = new ColorString();
            StringBuilder sbChars = new StringBuilder( 20 );
            ulong addr = StartAddress;
            cs.AppendPushFg( ConsoleColor.DarkGreen );

            for( int idx = 0; idx < Count; idx++ )
            {
                if( 0 == (idx % numColumns) )
                {
                    // This is the beginning of a new line.

                    // First finish off the last line if necessary:
                    if( addtlInfo.HasFlag( AddtlInfo.Ascii ) )
                    {
                        if( sbChars.Length > 2 )
                        {
                            cs.AppendPushPopFg( ConsoleColor.Cyan, sbChars.ToString() );
                        }

                        sbChars.Clear();
                        sbChars.Append( "  " );
                    }

                    if( 0 != idx )
                    {
                        cs.AppendLine();
                    }

                    cs.Append( DbgProvider.FormatAddress( addr, m_is32Bit, true, true ) ).Append( "  " );
                    addr += (ulong) (numColumns * elemSize);
                } // end start of new line
                else
                {
                    cs.Append( " " );
                }

                // Widen to ulong to accommodate largest possible item.
                ulong val = (ulong) this[ idx ];

                // This highlights the non-zero portion in [bright] green.
                cs.Append( _ZeroPad( val, desiredLen ) );

                if( addtlInfo.HasFlag( AddtlInfo.Symbols ) )
                {
                    ColorString csSym = ColorString.Empty;
                    if( val > 4096 ) // don't even bother trying if it's too low.
                    {
                        csSym = m_lookupSymbol( val );
                        if( csSym.Length > 0 )
                            cs.Append( " " ).Append( csSym );
                    }
                    if( addtlInfo.HasFlag( AddtlInfo.Ascii ) )
                    {
                        if( 0 == csSym.Length )
                        {
                            cs.Append( "                                          " );
                            _AppendChars( sbChars, idx * elemSize, elemSize );
                        }
                    }
                } // end if( symbols )
                else if( addtlInfo.HasFlag( AddtlInfo.Ascii ) )
                {
                    _AppendChars( sbChars, idx * elemSize, elemSize );
                } // end else if( Ascii )
            } // end for( idx = 0 .. length )

            // Finish off last line.
            if( sbChars.Length > 2 )
            {
                // It could be a partial line, so we may need to adjust for that.
                int numMissing = (int) numColumns - ((sbChars.Length - 2) / elemSize);
                Util.Assert( numMissing >= 0 );
                if( numMissing > 0 )
                {
                    cs.Append( new String( ' ', numMissing * charsPerBlock ) );
                }
                cs.AppendPushPopFg( ConsoleColor.Cyan, sbChars.ToString() );
            }

            cs.AppendPop(); // pop DarkGreen

            return cs.MakeReadOnly();
        } // end _FormatBlocks()


        private ColorString _FormatCharsOnly( uint numColumns, Func< char, char > toDisplay )
        {
            if( 0 == numColumns )
                numColumns = 32; // documentation says it should be 48, but windbg seems to do 32 instead.

            ColorString cs = new ColorString();
            StringBuilder sbChars = new StringBuilder( (int) numColumns + 8 );
            ulong addr = StartAddress;

            for( int idx = 0; idx < Count; idx++ )
            {
                if( 0 == (idx % numColumns) )
                {
                    // This is the beginning of a new line.

                    // First finish off the last line if necessary:
                    if( sbChars.Length > 1 )
                    {
                        sbChars.Append( '"' );
                        cs.AppendPushPopFg( ConsoleColor.Cyan, sbChars.ToString() );
                    }

                    sbChars.Clear();
                    sbChars.Append( '"' );

                    if( 0 != idx )
                    {
                        cs.AppendLine();
                    }

                    cs.Append( DbgProvider.FormatAddress( addr, m_is32Bit, true, true ) ).Append( "  " );
                    addr += (ulong) numColumns;
                } // end start of new line

                // Widen to ulong to accommodate largest possible item.
                ulong val = (ulong) this[ idx ];

                if( 0 == val )
                    break;

                sbChars.Append( toDisplay( (char) val ) );
            } // end for( idx = 0 .. length )

            // Finish off last line.
            if( sbChars.Length > 1 )
            {
                sbChars.Append( '"' );
                cs.AppendPushPopFg( ConsoleColor.Cyan, sbChars.ToString() );
            }

            return cs.MakeReadOnly();
        } // end _FormatCharsOnly()

        private ColorString _FormatBits()
        {
            ColorString cs = new ColorString();
            uint bytesPerValue = sizeof( uint );
            uint bitsPerValue = bytesPerValue * 8;
            ulong addr = StartAddress;

            for( int idx = 0; idx < Count; idx++ )
            {
                if( 0 != idx )
                {
                    cs.AppendLine();
                }

                cs.Append( DbgProvider.FormatAddress( addr, m_is32Bit, true, true ) ).Append( " " );
                addr += (ulong)bytesPerValue;

                uint val = this[idx];
                for( int i = 0; i < bitsPerValue; i++ )
                {
                    if( i % 8 == 0 )
                    {
                        cs.Append( " " );
                    }
                    uint mask = 0x80000000 >> i;
                    if( (mask & val) == mask )
                    {
                        cs.AppendPushPopFg( ConsoleColor.Green, "1" );
                    }
                    else
                    {
                        cs.AppendPushPopFg( ConsoleColor.DarkGreen, "0" );
                    }
                }

                cs.Append( "  " + val.ToString("x").PadLeft( 8, '0' ) );
            } // end for( idx = 0 .. length )

            return cs.MakeReadOnly();
        } // end _FormatBits()


        private static ColorString _DefaultSymLookup( DbgEngDebugger debugger, ulong addr )
        {
            ulong disp;
            string symName;
            if( debugger.TryGetNameByOffset( addr, out symName, out disp ) )
            {
                ColorString cs = DbgProvider.ColorizeSymbol( symName );
                if( disp != 0 )
                    cs.AppendPushPopFg( ConsoleColor.Gray, "+" + disp.ToString( "x" ) );

                return cs;
            }
            return ColorString.Empty;
        } // end _DefaultSymLookup()


        public object Clone()
        {
            var clone = new DbgMemory( StartAddress, m_bytes, m_is32Bit, IsBigEndian, m_lookupSymbol );
            clone.DefaultDisplayFormat = DefaultDisplayFormat;
            clone.DefaultDisplayColumns = DefaultDisplayColumns;
            return clone;
        }
    } // end class DbgMemory
}

