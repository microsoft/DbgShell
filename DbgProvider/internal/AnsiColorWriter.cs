using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MS.Dbg
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
    internal class AnsiColorWriter : TextWriter
    {
        private const char CSI = '\x9b';  // "Control Sequence Initiator"

        private ConsoleColor DefaultForeground;
        private ConsoleColor DefaultBackground;
        private Dictionary< char, Action< List< int > > > m_commands;
        private ControlSequenceParseState m_state;


        // We support control sequences being broken across calls to Write methods. This
        // struct keeps track of state in between calls.
        private struct ControlSequenceParseState
        {
            private int m_accum;
            private List< int > m_list;

            // True if we are in the middle of parsing a sequence. Useful for when sequences
            // are broken across multiple calls.
            public bool Parsing { get; private set; }

            // This can tolerate multiple Begin calls without destroying state.
            public void Begin()
            {
                if( !Parsing )
                {
                    Parsing = true;
                    m_list = new List< int >();
                }
            }

            public void Accum( int digit )
            {
                m_accum *= 10;
                m_accum += digit;
            }

            public void Enter()
            {
                m_list.Add( m_accum );
                m_accum = 0;
            }

            public List< int > Finish()
            {
                Enter();
                Parsing = false;
                List< int > list = m_list;
                m_list = null;
                return list;
            }
        } // end class ControlSequenceParseState


        public AnsiColorWriter()
        {
            DefaultForeground = Console.ForegroundColor;
            DefaultBackground = Console.BackgroundColor;

            m_commands = new Dictionary< char, Action< List< int > > >()
            {
                { 'm', _SelectGraphicsRendition },
            };
        } // end constructor


        public override Encoding Encoding { get { return Console.Out.Encoding; } }


        public override void Write( char[] charBuffer, int index, int count)
        {
            Write( new String( charBuffer, index, count ) );
        }

        public override void Write( char c )
        {
            Write( c.ToString() );
        }

        public override void Write( string s )
        {
            int startIndex = 0;

            if( m_state.Parsing )
            {
                // Need to continue.
                startIndex = _HandleControlSequence( s, -1 );
            }

            int escIndex = s.IndexOf( CSI, startIndex );

            while( escIndex >= 0 )
            {
                //Tool.WL( "escIndex: {0}, startIndex: {1}", escIndex, startIndex );
                string chunk = s.Substring( startIndex, escIndex - startIndex );
                Console.Write( chunk );
                startIndex = _HandleControlSequence( s, escIndex );
                escIndex = s.IndexOf( CSI, startIndex );
            }

            if( !(startIndex >= s.Length) )
            {
                Console.Write( s.Substring( startIndex ) );
            }
        } // end Write()


        private int _HandleControlSequence( string s, int escIndex )
        {
            m_state.Begin(); // we may actually be continuing...

            char c;
            int curIndex = escIndex;
            while( ++curIndex < s.Length )
            {
                c = s[ curIndex ];
                if( (c >= '0') &&  (c <= '9') )
                {
                    m_state.Accum( ((int) c) - 0x30 );
                    continue;
                }
                else if( ';' == c )
                {
                    m_state.Enter();
                    continue;
                }
                else if( (c >= '@') && (c <= '~') )
                {
                    List< int > args = m_state.Finish();
                    _ProcessCommand( c, args );
                    return curIndex + 1;
                }
                else
                {
                    // You're supposed to be able to have anonther character, like a space
                    // (0x20) before the command code character, but I'm not going to do that.
                    throw new ArgumentException( String.Format( "Invalid command sequence character at position {0} (0x{1:x}).", curIndex, (int) c ) );
                }
            }
            // Finished parsing the whole string--the control sequence must be broken across
            // strings.
            return curIndex;
        } // end _HandleControlSequence()


        private void _ProcessCommand( char commandChar, List< int > args )
        {
            Action< List< int > > action;
            if( !m_commands.TryGetValue( commandChar, out action ) )
            {
                throw new NotSupportedException( String.Format( "The command code '{0}' (0x{1:x}) is not supported.", commandChar, (int) commandChar ) );
            }

            action( args );
        } // end _ProcessCommand()


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

        private void _SelectGraphicsRendition( List< int > args )
        {
            if( 0 == args.Count )
                args.Add( 0 ); // no args counts as a reset

            foreach( int arg in args )
            {
                _ProcessSgrCode( arg );
            }
        } // end _SelectGraphicsRendition()


        private class ColorPair
        {
            public ConsoleColor Foreground { get; private set; }
            public ConsoleColor Background { get; private set; }
            public ColorPair( ConsoleColor foreground, ConsoleColor background )
            {
                Foreground = foreground;
                Background = background;
            }
        }

        private Stack< ColorPair > m_colorStack = new Stack< ColorPair >();

        private void _ProcessSgrCode( int code )
        {
            if( 0 == code )
            {
                Console.ForegroundColor = DefaultForeground;
                Console.BackgroundColor = DefaultBackground;
            }
            else if( (code <= 37) && (code >= 30) )
            {
                Console.ForegroundColor = AnsiNormalColorMap[ (code - 30) ];
            }
            else if( (code <= 47) && (code >= 40) )
            {
                Console.BackgroundColor = AnsiNormalColorMap[ (code - 40) ];
            }
            else if( (code <= 97) && (code >= 90) )
            {
                Console.ForegroundColor = AnsiBrightColorMap[ (code - 90) ];
            }
            else if( (code <= 107) && (code >= 100) )
            {
                Console.BackgroundColor = AnsiBrightColorMap[ (code - 100) ];
            }
            else if( 56 == code ) // NON-STANDARD (I made this one up)
            {
                m_colorStack.Push( new ColorPair( Console.ForegroundColor, Console.BackgroundColor ) );
            }
            else if( 57 == code ) // NON-STANDARD (I made this one up)
            {
                ColorPair cp = m_colorStack.Pop();
                Console.ForegroundColor = cp.Foreground;
                Console.BackgroundColor = cp.Background;
            }
            else
            {
                throw new NotSupportedException( String.Format( "SGR code '{0}' not supported.", code ) );
            }
        } // end _ProcessSgrCode()

        public static void Test()
        {
            AnsiColorWriter colorWriter = new AnsiColorWriter();
            colorWriter.Write( "\u009b91mThis should be red.\u009bm This should not.\r\n" );
            colorWriter.Write( "\u009b91;41mThis should be red on red.\u009bm This should not.\r\n" );
            colorWriter.Write( "\u009b91;100mThis should be red on gray.\u009b" );
            colorWriter.Write( "m This should not.\r\n" );

            colorWriter.Write( "\u009b" );
            colorWriter.Write( "9" );
            colorWriter.Write( "1;10" );
            colorWriter.Write( "6mThis should be red on cyan.\u009b" );
            colorWriter.Write( "m This should \u009bm\u009bmnot.\r\n" );
        } // end Test

    } // end class AnsiColorWriter
}
