using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using MS.Dbg;

using ConsoleHandle = Microsoft.Win32.SafeHandles.SafeFileHandle;

namespace MS.DbgShell
{
// (unindented a level to simplify the diff with other similar copies of this code)
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

        private ConsoleColor DefaultForeground;
        private ConsoleColor DefaultBackground;
        private readonly IReadOnlyDictionary< char, Func< Action< List< int > > > > m_commandTreeRoot;
        private readonly IReadOnlyDictionary< char, Func< Action< List< int > > > > m_hashCommands;
        private ControlSequenceParseState m_state;


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
                    return null != CurrentCommands;
                }
            }

            private IReadOnlyDictionary< char, Func< Action< List< int > > > > m_commandTreeRoot;

            public ControlSequenceParseState( IReadOnlyDictionary< char, Func< Action< List< int > > > > rootCommands )
            {
                m_commandTreeRoot = rootCommands;
                CurrentCommands = null;
                m_accum = 0;
                m_isParsingParam = false;
                m_list = null;
            }

            public IReadOnlyDictionary< char, Func< Action< List< int > > > > CurrentCommands;

            // This can tolerate multiple Begin calls without destroying state.
            public void Begin()
            {
                if( !IsParsing )
                {
                    CurrentCommands = m_commandTreeRoot;
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

                CurrentCommands = null;
                List< int > list = m_list;
                m_list = null;
                return list;
            }

            /// <summary>
            ///    To recover from bad input.
            /// </summary>
            public void Reset()
            {
                CurrentCommands = null;
                m_isParsingParam = false;
                m_list = null;
                m_accum = 0;
            }
        } // end class ControlSequenceParseState


        public AnsiColorWriter()
        {
            DefaultForeground = ConsoleControl.ForegroundColor;
            DefaultBackground = ConsoleControl.BackgroundColor;

            m_commandTreeRoot = new Dictionary< char, Func< Action< List< int > > > >()
            {
                { 'm', () => _SelectGraphicsRendition },
                { '#', _ProcessHashCommand },
            };

            m_hashCommands = new Dictionary< char, Func< Action< List< int > > > >()
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
                { 'p', () => _PushSgr },
                { 'q', () => _PopSgr },
            };

            m_state = new ControlSequenceParseState( m_commandTreeRoot );
        } // end constructor


        public void Write( ConsoleHandle handle, ReadOnlySpan< char > s, bool appendNewline )
        {
            if( m_state.IsParsing )
            {
                // Need to continue.
                int startIndex = _HandleControlSequence( s, -1 );
                s = s.Slice( startIndex );
            }

            int escIndex = s.IndexOf( CSI );

            while( escIndex >= 0 )
            {
                //Tool.WL( "escIndex: {0}, startIndex: {1}", escIndex, startIndex );
                var chunk = s.Slice( 0, escIndex );
                ConsoleControl._RealWriteConsole( handle, chunk );
                int startIndex = _HandleControlSequence( s, escIndex );
                s = s.Slice( startIndex );
                escIndex = s.IndexOf( CSI );
            }

            if( !s.IsEmpty )
            {
                ConsoleControl._RealWriteConsole( handle, s );
            }

            if( appendNewline )
            {
                ConsoleControl._RealWriteConsole( handle, ColorHostUserInterface.Crlf.AsSpan() );
            }
        } // end Write()


        /// <summary>
        ///    Returns the index of the first character past the control sequence (which
        ///    could be past the end of the string, or could be the start of another
        ///    control sequence).
        /// </summary>
        private int _HandleControlSequence( ReadOnlySpan< char > s, int escIndex )
        {
            m_state.Begin(); // we may actually be continuing...

            char c;
            int curIndex = escIndex;
            while( ++curIndex < s.Length )
            {
                c = s[ curIndex ];
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
                    if( _FindCommand( c, out Action< List< int > > command ) )
                    {
                        command( m_state.FinishParsingParams() );
                        return curIndex + 1;
                    }
                }
                else
                {
                    // You're supposed to be able to have anonther character, like a space
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


        /// <summary>
        ///    Returns true if it successfully found a command to execute; false if we
        ///    need to read more characters (some commands are expressed with multiple
        ///    characters).
        /// </summary>
        private bool _FindCommand( char commandChar, out Action< List< int > > command )
        {
            Func< Action< List< int > > > commandSearcher;
            if( !m_state.CurrentCommands.TryGetValue( commandChar, out commandSearcher ) )
            {
                throw new NotSupportedException( String.Format( "The command code '{0}' (0x{1:x}) is not supported.", commandChar, (int) commandChar ) );
            }

            command = commandSearcher();

            return command != null;
        } // end _FindCommand()


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

            foreach( int arg in args )
            {
                _ProcessSgrCode( arg );
            }
        } // end _SelectGraphicsRendition()


        private void _PushSgr( List< int > args )
        {
            if( (args != null) && (args.Count != 0) )
            {
                throw new NotSupportedException( "Optional arguments to the XTPUSHSGR command are not currently implemented." );
            }

            m_colorStack.Push( new ColorPair( Console.ForegroundColor, Console.BackgroundColor ) );
        } // end _PushSgr()


        private void _PopSgr( List< int > args )
        {
            if( (args != null) && (args.Count != 0) )
            {
                throw new InvalidOperationException( "The XTPOPSGR command does not accept arguments." );
            }

            ColorPair cp = m_colorStack.Pop();
            Console.ForegroundColor = cp.Foreground;
            Console.BackgroundColor = cp.Background;
        } // end _PopSgr()


        private Action< List< int > > _ProcessHashCommand()
        {
            m_state.CurrentCommands = m_hashCommands;

            return null;
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
                ConsoleControl.ForegroundColor = DefaultForeground;
                ConsoleControl.BackgroundColor = DefaultBackground;
            }
            else if( (code <= 37) && (code >= 30) )
            {
                ConsoleControl.ForegroundColor = AnsiNormalColorMap[ (code - 30) ];
            }
            else if( (code <= 47) && (code >= 40) )
            {
                ConsoleControl.BackgroundColor = AnsiNormalColorMap[ (code - 40) ];
            }
            else if( (code <= 97) && (code >= 90) )
            {
                ConsoleControl.ForegroundColor = AnsiBrightColorMap[ (code - 90) ];
            }
            else if( (code <= 107) && (code >= 100) )
            {
                ConsoleControl.BackgroundColor = AnsiBrightColorMap[ (code - 100) ];
            }
            else if( 56 == code ) // NON-STANDARD (I made this one up)
            {
                Util.Fail( "The 56/57 SGR codes (non-standard push/pop) are deprecated. Use XTPUSHSGR/XTPOPSGR instead." );
                m_colorStack.Push( new ColorPair( ConsoleControl.ForegroundColor, ConsoleControl.BackgroundColor ) );
            }
            else if( 57 == code ) // NON-STANDARD (I made this one up)
            {
                Util.Fail( "The 56/57 SGR codes (non-standard push/pop) are deprecated. Use XTPUSHSGR/XTPOPSGR instead." );
                ColorPair cp = m_colorStack.Pop();
                ConsoleControl.ForegroundColor = cp.Foreground;
                ConsoleControl.BackgroundColor = cp.Background;
            }
            else
            {
                throw new NotSupportedException( String.Format( "SGR code '{0}' not supported.", code ) );
            }
        } // end _ProcessSgrCode()
    } // end class AnsiColorWriter
} // class ConsoleControl
} // namespace

