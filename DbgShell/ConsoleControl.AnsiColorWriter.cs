using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using MS.Dbg;

using ConsoleHandle = Microsoft.Win32.SafeHandles.SafeFileHandle;

namespace MS.DbgShell
{
    internal static partial class ConsoleControl
    {
        // This class interprets ANSI escape sequences to alter the color of console output. Only
        // a limited set of control sequences are supported, namely a subset of SGR (Select
        // Graphics Rendition) commands.
        //
        // See:
        //
        //     http://bjh21.me.uk/all-escapes/all-escapes.txt
        //     http://en.wikipedia.org/wiki/ISO/IEC_6429
        //     http://en.wikipedia.org/wiki/ISO_6429
        //     http://invisible-island.net/xterm/ctlseqs/ctlseqs.html
        //
        internal class AnsiColorWriter
        {
            private const char CSI = '\x9b';  // "Control Sequence Initiator" (single character, as opposed to '\x1b' + '[')
            private const string CSI_str = "\x9b"; //String version for printing (was sticking the char in a stackalloc span, but hit https://github.com/dotnet/roslyn/issues/35874 )

            private readonly ConsoleColor DefaultForeground;
            private readonly ConsoleColor DefaultBackground;
            private ControlSequenceParseState m_state;

            
            public bool VirtualTerminalSupported { get; set; }

            // We support control sequences being broken across calls to Write methods. This
            // struct keeps track of state in between calls.
            private struct ControlSequenceParseState
            {
                private int m_accum;
                private bool m_isParsingParam;
                private List< int > m_list;

                // True if we are in the middle of parsing a sequence. Useful for when sequences
                // are broken across multiple calls.
                public bool IsParsing
                {
                    get
                    {
                        return null != CurrentCommandHandler;
                    }
                }

                private readonly Func< char, bool > m_commandTreeRootDelegate;

                public ControlSequenceParseState( Func< char, bool > rootCommands )
                {
                    m_commandTreeRootDelegate = rootCommands;
                    CurrentCommandHandler = null;
                    m_accum = 0;
                    m_isParsingParam = false;
                    m_list = null;
                }

                public Func< char, bool > CurrentCommandHandler;

                // This can tolerate multiple Begin calls without destroying state.
                public void Begin()
                {
                    if( !IsParsing )
                    {
                        CurrentCommandHandler = m_commandTreeRootDelegate;
                        m_list = new List< int >();
                        m_isParsingParam = false;
                    }
                }

                public void AccumParamDigit( int digit )
                {
                    m_accum *= 10;
                    m_accum += digit;
                    m_isParsingParam = true;
                }

                public void EnterParam()
                {
                    Util.Assert( m_isParsingParam );
                    m_list.Add( m_accum );
                    m_isParsingParam = false;
                    m_accum = 0;
                }

                public List< int > FinishParsingParams()
                {
                    if( m_isParsingParam )
                        EnterParam();

                    CurrentCommandHandler = null;
                    List< int > list = m_list;
                    m_list = null;
                    return list;
                }

                /// <summary>
                ///    To recover from bad input.
                /// </summary>
                public void Reset()
                {
                    CurrentCommandHandler = null;
                    m_isParsingParam = false;
                    m_list = null;
                    m_accum = 0;
                }
            } // end class ControlSequenceParseState


            public AnsiColorWriter()
            {
                DefaultForeground = ConsoleControl.ForegroundColor;
                DefaultBackground = ConsoleControl.BackgroundColor;

                m_state = new ControlSequenceParseState( CommandTreeRootStateHandler );
            } // end constructor

            private bool CommandTreeRootStateHandler( char commandChar )
            {
                switch( commandChar )
                {
                    case 'm':
                        _SelectGraphicsRendition( m_state.FinishParsingParams() );
                        return true;
                    case '#':
                        m_state.CurrentCommandHandler = HashCommandStateHandler;
                        return false;
                    default:
                        throw new NotSupportedException( String.Format( "The command code '{0}' (0x{1:x}) is not supported.", commandChar, (int) commandChar ) );
                }
            }

            private bool HashCommandStateHandler( char commandChar )
            {
                // TROUBLE: The current definition of XTPUSHSGR and XTPOPSGR use curly
                // brackets, which turns out to conflict badly with C# string formatting.
                // For example, this:
                //
                //
                //    $csFmt = (New-ColorString).AppendPushFg( 'Cyan' ).Append( 'this should all be cyan: {0}' )
                //    [string]::format( $csFmt.ToString( $true ), 'blah' )
                //
                // will blow up.
                //
                // For now, I'm going to switch to some other characters while we see if
                // we get can something worked out with xterm.
                //{ '{', () => _PushSgr },
                //{ '}', () => _PopSgr },
                switch( commandChar )
                {
                    case 'p':
                        _PushSgr( m_state.FinishParsingParams() );
                        return true;
                    case 'q':
                        _PopSgr( m_state.FinishParsingParams() );
                        return true;
                    default:
                        throw new NotSupportedException( String.Format( "The command code '{0}' (0x{1:x}) is not supported.", commandChar, (int) commandChar ) );
                }
            }


            private char[] m_outputCharBuffer = new char[ 16384 ];
            private int m_outputBufferPos = 0;

            public void Write( ConsoleHandle handle, ReadOnlySpan< char > s, bool appendNewline )
            {
                m_outputBufferPos = 0;

                if( m_state.IsParsing )
                {
                    // Need to continue.
                    int startIndex = _HandleControlSequence( s, -1 );
                    s = s.Slice( startIndex );
                }

                int escIndex = s.IndexOf( CSI );

                while( escIndex >= 0 )
                {
                    var chunk = s.Slice( 0, escIndex );
                    WriteConsoleWrapper( handle, chunk );
                    int startIndex = _HandleControlSequence( s, escIndex );
                    s = s.Slice( startIndex );
                    escIndex = s.IndexOf( CSI );
                }

                if( !s.IsEmpty )
                {
                    WriteConsoleWrapper( handle, s );
                }

                if( appendNewline )
                {
                    WriteConsoleWrapper( handle, ColorHostUserInterface.Crlf.AsSpan() );
                }
                FinalizeWrite( handle );
            } // end Write()

            private void WriteConsoleWrapper( ConsoleHandle handle, ReadOnlySpan<char> value )
            {
                if( VirtualTerminalSupported )
                {
                    AppendToOutputBuffer( value );
                }
                else
                {
                    // [zhent] the PowerShell source claims that WriteConsole breaks down past 64KB. But it also seems to think `char` is/can be 4 bytes, so I'm skeptical.
                    // In my tests on Windows 10, it can handle at least up to 4GB (with enough patience), so I'll leave the chunking for versions that aren't recent enough to support VT
                    while( value.Length > 16383 )
                    {
                        _RealWriteConsole( handle, value.Slice( 0, 16383 ) );
                        value = value.Slice( 16383 );
                    }

                    _RealWriteConsole( handle, value );
                }
            }

            private void AppendToOutputBuffer( ReadOnlySpan<char> value )
            {
                var destSpan = m_outputCharBuffer.AsSpan( m_outputBufferPos );
                while( value.Length > destSpan.Length )
                {
                    var newBuff = new char[ m_outputCharBuffer.Length * 2 ];
                    m_outputCharBuffer.AsSpan( 0, m_outputBufferPos ).CopyTo( newBuff.AsSpan() );
                    m_outputCharBuffer = newBuff;
                    destSpan = m_outputCharBuffer.AsSpan( m_outputBufferPos );
                }
                value.CopyTo( destSpan );
                m_outputBufferPos += value.Length;
            }

            // Commented out due to https://github.com/dotnet/roslyn/issues/35874
            //private void AppendToOutputBuffer( char value )
            //{
            //    Span< char > tempBuff = stackalloc char[ 1 ];
            //    tempBuff[ 0 ] = value;
            //    AppendToOutputBuffer( tempBuff );
            //}


            private void FinalizeWrite( ConsoleHandle handle )
            {
                if( VirtualTerminalSupported )
                {
                    _RealWriteConsole( handle, m_outputCharBuffer.AsSpan( 0, m_outputBufferPos ) );
                }
            }


            /// <summary>
            ///    Returns the index of the first character past the control sequence (which
            ///    could be past the end of the string, or could be the start of another
            ///    control sequence).
            /// </summary>
            private int _HandleControlSequence( ReadOnlySpan< char > s, int escIndex )
            {
                m_state.Begin(); // we may actually be continuing...

                int curIndex = escIndex;
                while( ++curIndex < s.Length )
                {
                    char c = s[ curIndex ];
                    if( (c >= '0') &&  (c <= '9') )
                    {
                        m_state.AccumParamDigit( ((int) c) - 0x30 );
                        continue;
                    }
                    else if( ';' == c )
                    {
                        m_state.EnterParam();
                        continue;
                    }
                    else if( ((c >= '@') && (c <= '~')) || (c == '#') )
                    {
                        if( m_state.CurrentCommandHandler( c ) )
                        {
                            return curIndex + 1;
                        }
                    }
                    else
                    {
                        // You're supposed to be able to have another character, like a space
                        // (0x20) before the command code character, but I'm not going to do that.

                        // TODO: Unfortunately, we can get into this scenario. Say somebody wrote a string to the
                        // output stream, and that string had control sequences. And say then somebody tried to process
                        // that string in a pipeline, for instance tried to find some property on it, that didn't
                        // exist, causing an error to be written, where the "target object" is the string... and stuff
                        // maybe gets truncated or ellipsis-ized... and then we're hosed. What should I do about this?
                        // Try to sanitize higher-level output? Seems daunting... Just reset state and ignore it? Hm.
                        m_state.Reset();
                        throw new ArgumentException( String.Format( "Invalid command sequence character at position {0} (0x{1:x}).", curIndex, (int) c ) );
                    }
                }
                // Finished parsing the whole string--the control sequence must be broken across
                // strings.
                return curIndex;
            } // end _HandleControlSequence()


            // Info sources:
            //     http://bjh21.me.uk/all-escapes/all-escapes.txt
            //     http://en.wikipedia.org/wiki/ISO/IEC_6429
            //     http://en.wikipedia.org/wiki/ISO_6429
            //     http://invisible-island.net/xterm/ctlseqs/ctlseqs.html
            private static ConsoleColor[] AnsiNormalColorMap =
            {
                                               // Foreground   Background
                ConsoleColor.Black,            //     30           40
                ConsoleColor.DarkRed,          //     31           41
                ConsoleColor.DarkGreen,        //     32           42
                ConsoleColor.DarkYellow,       //     33           43
                ConsoleColor.DarkBlue,         //     34           44
                ConsoleColor.DarkMagenta,      //     35           45
                ConsoleColor.DarkCyan,         //     36           46
                ConsoleColor.Gray              //     37           47
            };

            private static ConsoleColor[] AnsiBrightColorMap =
            {                                 // Foreground   Background
                ConsoleColor.DarkGray,        //     90          100
                ConsoleColor.Red,             //     91          101
                ConsoleColor.Green,           //     92          102
                ConsoleColor.Yellow,          //     93          103
                ConsoleColor.Blue,            //     94          104
                ConsoleColor.Magenta,         //     95          105
                ConsoleColor.Cyan,            //     96          106
                ConsoleColor.White            //     97          107
            };

            // In addition to the color codes, I've added support for two additional
            // (non-standard) codes:
            //
            //   56: Push fg/bg color pair
            //   57: Pop fg/bg color pair
            //
            // However, THESE ARE DEPRECATED. Use XTPUSHSGR/XTPOPSGR instead.
            //

            private void _SelectGraphicsRendition( List< int > args )
            {
                if( 0 == args.Count )
                    args.Add( 0 ); // no args counts as a reset

                int index = 0;
                while( index < args.Count )
                {
                    _ProcessSgrCode( args, ref index );
                }
            } // end _SelectGraphicsRendition()


            private void _PushSgr( List< int > args )
            {
                if( (args != null) && (args.Count != 0) )
                {
                    throw new NotSupportedException( "Optional arguments to the XTPUSHSGR command are not currently implemented." );
                }

                m_colorCodeStack.Push( new ColorCodePair( ForegroundCode, BackgroundCode ) );
            } // end _PushSgr()


            private void _PopSgr( List< int > args )
            {
                if( (args != null) && (args.Count != 0) )
                {
                    throw new InvalidOperationException( "The XTPOPSGR command does not accept arguments." );
                }

                var pair = m_colorCodeStack.Pop();
                ForegroundCode = pair.Foreground;
                BackgroundCode = pair.Background;
            } // end _PopSgr()
            
            
            private struct VtColorCode
            {
                public VtColorCode( int commandCode ) : this()
                {
                    CommandCode = (byte)commandCode;
                }

                public VtColorCode( byte commandCode, byte r, byte g, byte b )
                {
                    CommandCode = commandCode;
                    R = r;
                    G = g;
                    B = b;
                }

                public byte CommandCode { get; }
                public byte R { get; }
                public byte G { get; }
                public byte B { get; }
            }

            private struct ColorCodePair
            {
                public VtColorCode Foreground { get; }
                public VtColorCode Background { get; }
                public ColorCodePair( VtColorCode foreground, VtColorCode background )
                {
                    Foreground = foreground;
                    Background = background;
                }
            }

            private readonly Stack<ColorCodePair> m_colorCodeStack = new Stack<ColorCodePair>();


            private VtColorCode m_activeForegroundCode = new VtColorCode( 39 );
            private VtColorCode m_activeBackgroundCode = new VtColorCode( 49 );

            private VtColorCode ForegroundCode
            {
                get
                {
                    return m_activeForegroundCode;
                }
                set
                {
                    m_activeForegroundCode = value;
                    if( VirtualTerminalSupported )
                    {
                        _WriteCodeToBuffer( value );
                    }
                    else
                    {
                        if( value.CommandCode == 0 || value.CommandCode == 39 || value.CommandCode == 38 ) //Treat RGB codes as default since we can't render them correctly
                        {
                            ConsoleControl.ForegroundColor = DefaultForeground;
                        }
                        else if( (value.CommandCode <= 37) && (value.CommandCode >= 30) )
                        {
                            ConsoleControl.ForegroundColor = AnsiNormalColorMap[ (value.CommandCode - 30) ];
                        }
                        else if( (value.CommandCode <= 97) && (value.CommandCode >= 90) )
                        {
                            ConsoleControl.ForegroundColor = AnsiBrightColorMap[ (value.CommandCode - 90) ];
                        }
                        else
                        {
                            throw new InvalidOperationException();
                        }
                    }
                }
            }

            private VtColorCode BackgroundCode
            {
                get
                {
                    return m_activeBackgroundCode;
                }
                set
                {
                    m_activeBackgroundCode = value;
                    if( VirtualTerminalSupported )
                    {
                        _WriteCodeToBuffer( value );
                    }
                    else
                    {
                        if( value.CommandCode == 0 || value.CommandCode == 49 || value.CommandCode == 48 ) //Treat RGB codes as default since we can't render them correctly
                        {
                            ConsoleControl.BackgroundColor = DefaultBackground;
                        }
                        else if( (value.CommandCode <= 47) && (value.CommandCode >= 40) )
                        {
                            ConsoleControl.BackgroundColor = AnsiNormalColorMap[ (value.CommandCode - 40) ];
                        }
                        else if( (value.CommandCode <= 107) && (value.CommandCode >= 100) )
                        {
                            ConsoleControl.BackgroundColor = AnsiBrightColorMap[ (value.CommandCode - 100) ];
                        }
                        else
                        {
                            throw new InvalidOperationException();
                        }
                    }
                }
            }

            private void _WriteCodeToBuffer( VtColorCode code )
            {
                AppendToOutputBuffer( CSI_str.AsSpan() );
                AppendToOutputBuffer( sm_byteStrings[ code.CommandCode ].AsSpan() );
                if( code.CommandCode == 38 || code.CommandCode == 48 )
                {
                    AppendToOutputBuffer( ";2;".AsSpan() );
                    AppendToOutputBuffer( sm_byteStrings[ code.R ].AsSpan() );
                    AppendToOutputBuffer( ";".AsSpan() );
                    AppendToOutputBuffer( sm_byteStrings[ code.G ].AsSpan() );
                    AppendToOutputBuffer( ";".AsSpan() );
                    AppendToOutputBuffer( sm_byteStrings[ code.B ].AsSpan() );
                }
                AppendToOutputBuffer( "m".AsSpan() );

            }

            private void _ProcessSgrCode( List< int > codes, ref int index )
            {
                var code = codes[ index ];
                index++;
                if( 0 == code )
                {
                    //using default foreground/background codes instead of full reset so that the colorcodestack can maintain either one as default independently
                    ForegroundCode = new VtColorCode( 39 );
                    BackgroundCode = new VtColorCode( 49 );
                }
                else if( ((code <= 37) && (code >= 30)) || ((code <= 97) && (code >= 90)) || code == 39 )
                {
                    ForegroundCode = new VtColorCode( code );
                }
                else if( ((code <= 47) && (code >= 40)) || ((code <= 107) && (code >= 100)) || code == 49 )
                {
                    BackgroundCode = new VtColorCode( code );
                }
                else if( code == 38 )
                {
                    ForegroundCode = _ProcessTrueColorSgrCode( code, codes, ref index );
                }
                else if( code == 48 )
                {
                    BackgroundCode = _ProcessTrueColorSgrCode( code, codes, ref index );
                }
                else if( 56 == code ) // NON-STANDARD (I made this one up)
                {
                    Util.Fail( "The 56/57 SGR codes (non-standard push/pop) are deprecated. Use XTPUSHSGR/XTPOPSGR instead." );
                    m_colorCodeStack.Push( new ColorCodePair( ForegroundCode, BackgroundCode ) );
                }
                else if( 57 == code ) // NON-STANDARD (I made this one up)
                {
                    Util.Fail( "The 56/57 SGR codes (non-standard push/pop) are deprecated. Use XTPUSHSGR/XTPOPSGR instead." );
                    var pair = m_colorCodeStack.Pop();
                    ForegroundCode = pair.Foreground;
                    BackgroundCode = pair.Background;
                }
                else
                {
                    throw new NotSupportedException( String.Format( "SGR code '{0}' not supported.", code ) );
                }
            } // end _ProcessSgrCode()

            private static VtColorCode _ProcessTrueColorSgrCode( int command,  List< int > codes, ref int index )
            {
                if( codes.Count - index < 4 ) { throw new ArgumentException( "Extended SGR sequence does not have enough arguments for RGB colors" ); }

                if( codes[index] != 2 ) { throw new NotSupportedException( $"Extended SGR mode '{codes[ index + 1 ]}' not supported." ); }
                index++;
                return new VtColorCode( (byte)command, (byte) codes[ index++ ], (byte) codes[ index++ ], (byte) codes[ index++ ] );
            }

            //[zhent] Because the cost of .ToString() is just too much to bear.
            private static readonly string[] sm_byteStrings =
            { "0","1","2","3","4","5","6","7","8","9","10","11","12","13","14","15","16","17","18","19","20","21","22","23","24","25","26","27","28","29","30","31",
              "32","33","34","35","36","37","38","39","40","41","42","43","44","45","46","47","48","49","50","51","52","53","54","55","56","57","58","59","60","61","62","63",
              "64","65","66","67","68","69","70","71","72","73","74","75","76","77","78","79","80","81","82","83","84","85","86","87","88","89","90","91","92","93","94","95",
              "96","97","98","99","100","101","102","103","104","105","106","107","108","109","110","111","112","113","114","115","116","117","118","119","120","121","122","123","124","125","126","127",
              "128","129","130","131","132","133","134","135","136","137","138","139","140","141","142","143","144","145","146","147","148","149","150","151","152","153","154","155","156","157","158","159",
              "160","161","162","163","164","165","166","167","168","169","170","171","172","173","174","175","176","177","178","179","180","181","182","183","184","185","186","187","188","189","190","191",
              "192","193","194","195","196","197","198","199","200","201","202","203","204","205","206","207","208","209","210","211","212","213","214","215","216","217","218","219","220","221","222","223",
              "224","225","226","227","228","229","230","231","232","233","234","235","236","237","238","239","240","241","242","243","244","245","246","247","248","249","250","251","252","253","254","255"};
        } // end class AnsiColorWriter
    } // class ConsoleControl
} // namespace

