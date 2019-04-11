using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Dbg
{
    // TODO: Maybe this should be named more generically, since it should support command
    // sequences beside just color.

    // I wonder if at some point it would be better to introduce a whole new string-like class.

    /// <summary>
    ///    Indicates where an ellipsis is put in a truncated string.
    /// </summary>
    public enum TrimLocation
    {
        Right = 0,
        Center,
        Left
    }

    /// <summary>
    ///    Color-Aware String Utilities for strings with ISO 6429 strings. "CA" == "Color
    ///    Aware".
    /// </summary>
    /// <remarks>
    /// <para>
    ///    This class does not store any state between calls--strings with ISO command
    ///    sequences must contain the full command sequence (do not split command
    ///    sequences across strings).
    /// </para>
    /// <para>
    ///    See:
    ///
    ///        http://bjh21.me.uk/all-escapes/all-escapes.txt
    ///        http://en.wikipedia.org/wiki/ISO/IEC_6429
    ///        http://en.wikipedia.org/wiki/ISO_6429
    ///        http://invisible-island.net/xterm/ctlseqs/ctlseqs.html
    /// </para>
    /// </remarks>
    static class CaStringUtil
    {
        private const char CSI = '\x9b';  // "Control Sequence Initiator"
        private const string c_ResetColor = "\u009b0m"; // Resets background and foreground color to default.
        private const char c_ellipsis = (char) 0x2026;


        /// <summary>
        ///    Like String.Length, but ISO command sequences are zero-width.
        /// </summary>
        public static int Length( string s )
        {
            return Length( s, 0 );
        }

        public static int Length( string s, int startIndex )
        {
            if( null == s )
                throw new ArgumentNullException( "s" );

            int escIndex = s.IndexOf( CSI, startIndex );
            if( escIndex < 0 )
                return s.Length - startIndex; // TODO: String caches its length, right?

            int length = escIndex - startIndex; // not including command sequence stuff.
            int curIndex = escIndex;
            char c;
            while( ++curIndex < s.Length )
            {
                c = s[ curIndex ];
                if( (c >= '0') &&  (c <= '9') )
                {
                    continue;
                }
                else if( ';' == c )
                {
                    continue;
                }
                else if( (c >= '@') && (c <= '~') ) // The command code character.
                {
                    if( s.Length == (curIndex + 1) )
                        return length; // the command sequence is at the very end of the string.

                    return length + Length( s, curIndex + 1 );
                }
                else
                {
                    // You're supposed to be able to have anonther character, like a space
                    // (0x20) before the command code character, but I'm not going to do that.
                    throw new ArgumentException( String.Format( "Invalid command sequence character at position {0} (0x{1:x}).", curIndex, (int) c ) );
                }
            }
            // Finished parsing the whole string, without seeing the end of the control
            // sequence--the control sequence must be broken across strings.
            throw _CreateBrokenSequenceException();
        } // end Length()


        private static Exception _CreateBrokenSequenceException()
        {
            return new ArgumentException( "CaStringUtil methods cannot support strings with command sequences broken across string boundaries.", "s" );
        }


        private static bool _IsDigitOrSemi( char c )
        {
            if( (c >= '0') &&  (c <= '9') )
            {
                return true;
            }
            else if( ';' == c )
            {
                return true;
            }
            return false;
        } // end _IsDigitOrSemi()


        /// <summary>
        ///    Returns the index of the character after the control sequence (which could
        ///    be past the end of the string, or could be another control sequence).
        /// </summary>
        private static int _SkipControlSequence( string s, int startIdx )
        {
            if( CSI != s[ startIdx ] )
            {
                Util.Fail( "_SkipControlSequence called, but startIdx does not point to a control sequence." );
                throw new ArgumentException( "startIdx does not point to a control sequence.", "startIdx" );
            }

            int curIdx = startIdx;
            char c;
            while( ++curIdx < s.Length )
            {
                c = s[ curIdx ];
                if( _IsDigitOrSemi( c ) )
                    continue;

                if( (c >= '@') && (c <= '~') ) // The command code character.
                {
                    return curIdx + 1; // note that this could be just past the end of the string.
                }
                else
                {
                    // You're supposed to be able to have anonther character, like a space
                    // (0x20) before the command code character, but I'm not going to do that.
                    var ae = new ArgumentException( String.Format( "Invalid command sequence character at position {0} (0x{1:x}).",
                                                                   curIdx,
                                                                   (int) c ) );
                    ae.Data[ "s" ] = s;
                    ae.Data[ "startIdx" ] = startIdx;
                    ae.Data[ "s_munged" ] = s.Replace( new String( new char[] { CSI } ), "<CSI>" );
                    throw ae;
                }
            }
            // Finished parsing the whole string, without seeing the end of the control
            // sequence--the control sequence must be broken across strings, which we
            // don't support.
            throw _CreateBrokenSequenceException();
        } // end _SkipControlSequence()


     // /// <summary>
     // ///    Translates an "apparent" index into an "actual" index, where the "apparent"
     // ///    index treats control sequences as zero width.
     // /// </summary>
     // private static int _TranslateApparentIndexToActual( string s, int apparentIdx )
     // {
     //     if( null == s )
     //         throw new ArgumentNullException( "s" );

     //     int apparentSlotsLeftToConsume = apparentIdx;
     //     int actualIdx = 0;

     //     while( (apparentSlotsLeftToConsume > 0) && (actualIdx < s.Length) )
     //     {
     //         int escIndex = s.IndexOf( CSI, actualIdx );
     //         int slotsToConsume = Math.Min( apparentSlotsLeftToConsume, (escIndex - actualIdx) );
     //         actualIdx += slotsToConsume;
     //         apparentSlotsLeftToConsume -= slotsToConsume;

     //         Util.Assert( actualIdx < s.Length );

     //         if( CSI == s[ actualIdx ] )
     //         {
     //             actualIdx = _SkipControlSequence( s, actualIdx );
     //         }

     //         if( 0 == apparentSlotsLeftToConsume )
     //             return actualIdx;
     //     }
     //     Util.Assert( actualIdx >= s.Length );
     //     // TODO: The apparent index is past the end of the string. What to do here?
     //     // Fail? Or just return one past the end, since that is a valid when a control
     //     // sequence is at the end of the string?
     //     return s.Length;
     // } // end _TranslateApparentIndexToActual()

    //  /// <summary>
    //  ///    Translates an "apparent" index into an "actual" index, where the "apparent"
    //  ///    index treats control sequences as zero width.
    //  /// </summary>
    //  public static int TranslateApparentIndexToActual( string s, int apparentIdx )
    //  {
    //      return _TranslateApparentSubstringLengthToActual( s, 0, apparentIdx );
    //  }

        /// <summary>
        ///    Returns the actual length of the substring starting at actual index
        ///    actualStartIdx and extending for apparentLength characters,(where control
        ///    sequences are counted as zero-width. Consumption of control characters can
        ///    be specified as "greedy" at the end of the string; that is, if we want to
        ///    get two apparent characters from a string that consists of two content
        ///    characters followed by 100 characters of control codes, this will return a
        ///    string of length 102. That way, if that's the entire input string, we take
        ///    it all without losing anything.
        ///
        ///    Suppose you are trying to grab some substring out of a larger string. When
        ///    should you use greedy, and when not?
        ///
        ///    Well now, that's a bit of a puzzle. If there is a control sequence
        ///    immediately after where the content of your substring would end, you might
        ///    think it would be better to leave it off, because what if it is some
        ///    formatting stuff that is intended for the /following/ bit of content (the
        ///    content following the control sequence, which is past your substring).
        ///    True... but what if the control sequence is a POP sequence, that turns off
        ///    some formatting introduced in or before your substring? In that case, it
        ///    might be nice to have it...
        ///
        ///    So in general, my advice is to use the default of greedy = true, except
        ///    perhaps in the case where you are splitting a string, and not just carving
        ///    a substring out, because then you know that the following control
        ///    sequence(s) will be taken care of. One nice thing about greedy = true is
        ///    that if the substring happens to be the entire string, then you will pick
        ///    up all formatting.
        /// </summary>
        private static int _TranslateApparentSubstringLengthToActual( string s,
                                                                      int actualStartIdx,
                                                                      int apparentLength,
                                                                      bool greedy = true )
        {
            if( null == s )
                throw new ArgumentNullException( "s" );

            int apparentSlotsLeftToConsume = apparentLength;
            int actualEndIdx = actualStartIdx;

            while( (apparentSlotsLeftToConsume > 0) && (actualEndIdx < s.Length) )
            {
                int escIndex = s.IndexOf( CSI, actualEndIdx );
                if( escIndex < 0 )
                {
                    actualEndIdx += apparentSlotsLeftToConsume;
                    return actualEndIdx - actualStartIdx;
                }
                int consumed = Math.Min( apparentSlotsLeftToConsume, (escIndex - actualEndIdx) );
                actualEndIdx += consumed;
                apparentSlotsLeftToConsume -= consumed;

                Util.Assert( actualEndIdx < s.Length );

                while( (actualEndIdx < s.Length) &&
                       (CSI == s[ actualEndIdx ]) &&
                       (greedy || (apparentSlotsLeftToConsume > 0)) )
                {
                    actualEndIdx = _SkipControlSequence( s, actualEndIdx );
                }

                if( 0 == apparentSlotsLeftToConsume )
                    return actualEndIdx - actualStartIdx;
            }
            Util.Assert( actualEndIdx >= s.Length );
            return s.Length;
        } // end _TranslateApparentSubstringLengthToActual()



        // Nobody seems to be using these...
        // Would it be more useful to have a version that took an apparentStartIdx?
     // public static string Substring( string s, int actualStartIdx )
     // {
     //     return s.Substring( actualStartIdx );
     // } // end Substring()

        // Leaving this one uncommented but private because it functions as a test case
        // for _TranslateApparentSubstringLengthToActual.
        private static string Substring( string s, int actualStartIdx, int apparentLength )
        {
            int actualLength = _TranslateApparentSubstringLengthToActual( s,
                                                                          actualStartIdx,
                                                                          apparentLength,
                                                                          greedy: true );
            return s.Substring( actualStartIdx, actualLength );
        } // end Substring()


        /// <summary>
        ///    Strips all content, leaving only control sequences. Used for truncation
        ///    scenarios, where we need to preserve control sequences in truncated areas.
        /// </summary>
        private static void _StripContent( StringBuilder destSb,
                                           string s,
                                           int realStartIdx,
                                           int realEndIdx )
        {
            bool inControlSeq = false;
            for( int i = realStartIdx; i <= realEndIdx; i++ )
            {
                if( inControlSeq )
                {
                    destSb.Append( s[ i ] );
                    if( !_IsDigitOrSemi( s[ i ] ) )
                        inControlSeq = false; // we just appended the command code character.
                }
                else
                {
                    if( s[ i ] == CSI )
                    {
                        inControlSeq = true;
                        destSb.Append( s[ i ] );
                    }
                }
            } // end for( each char in range )
        } // end _StripContent()


        public static string Truncate( string s, int maxLen )
        {
            return Truncate( s, maxLen, true );
        }

        public static string Truncate( string s, int maxLen, bool useEllipsis )
        {
            return Truncate( s, maxLen, useEllipsis, false );
        }

        public static string Truncate( string s, int maxLen, bool useEllipsis, bool trimLeft )
        {
            return Truncate( s, maxLen, useEllipsis, trimLeft ? TrimLocation.Left : TrimLocation.Right );
        }

        public static string Truncate( string s,
                                       int maxLen,
                                       bool useEllipsis,
                                       TrimLocation trimLocation )
        {
            if( null == s )
                throw new ArgumentNullException( "s" );

            if( (trimLocation == TrimLocation.Center) && !useEllipsis )
                throw new ArgumentException( "You must use an ellipsis when trimming from the center.", "useEllipsis" );

            int minimumRequiredMaxLen = 1;
            if( useEllipsis )
            {
                if( TrimLocation.Center == trimLocation )
                    minimumRequiredMaxLen = 3; // a char of content on each side, plus the ellipsis
                else
                    minimumRequiredMaxLen = 2; // a char of content plus the ellipsis
            }

            if( maxLen < minimumRequiredMaxLen )
            {
                throw new ArgumentOutOfRangeException( "maxLen",
                                                       maxLen,
                                                       Util.Sprintf( "The maximum length must be > {0} for the specified format (useEllipsis: {1}, {2}).",
                                                                     minimumRequiredMaxLen - 1,
                                                                     useEllipsis,
                                                                     trimLocation ) );
            }

            int originalApparentLength = Length( s );
            if( originalApparentLength <= maxLen )
                return s;

            StringBuilder sb = new StringBuilder( maxLen + 16 );
            _TruncateWorker( s,
                             originalApparentLength,
                             maxLen,
                             useEllipsis,
                             trimLocation,
                             -1,
                             sb );
            return sb.ToString();
        }

        private static void _TruncateWorker( string s,
                                             int originalApparentLength,
                                             int maxApparentLength,
                                             bool useEllipsis,
                                             TrimLocation trimLocation,
                                             // Tricksy: the meaning of stripContentBoundary depends
                                             // on if we are trimming right or trimming left.
                                             int stripContentBoundary,
                                             StringBuilder dest )
        {
            if( TrimLocation.Center == trimLocation )
            {
                if( !useEllipsis )
                {
                    throw new ArgumentException( "Doesn't seem like a good idea to trim from the center without using an ellipsis." );
                }

                // Trimming from the center looks just like trimming from the right
                // concatenated with trimming from the left.
                //
                // We only need one character for the ellipsis, so if we have an odd
                // maxApparentLength, the number of content chars shown for each side will
                // be equal. If the maxApparentLength is even, I'm going to bias toward
                // showing a little more content on the left.
                int rightLen = maxApparentLength / 2;
                int leftLen = maxApparentLength - rightLen;

                if( leftLen == rightLen )
                {
                    // Even maxApparentLength.
                    rightLen -= 1; // We'll carve room for the ellipsis from the right side
                    leftLen += 1;  // (because we pass useEllipsis: true for the right side).
                }
                else
                {
                    // Odd maxApparentLength.
                    Util.Assert( leftLen > rightLen );
                }

                _TruncateWorker( s,
                                 originalApparentLength,
                                 leftLen,
                                 true,               // useEllipsis
                                 TrimLocation.Right,
                                 0,                  // stripContentBoundary
                                 dest );
                _TruncateWorker( s,
                                 originalApparentLength,
                                 rightLen,
                                 false,              // useEllipsis
                                 TrimLocation.Left,
                                 dest.Length - 1,    // stripContentBoundary
                                 dest );
                return;
            }

            bool trimLeft = trimLocation == TrimLocation.Left;
            bool trimRight = !trimLeft;
            int desiredApparentLength = useEllipsis ? maxApparentLength - 1 : maxApparentLength;
            int realStartIdx = 0; // start of content

            if( trimLeft )
            {
                int apparentDiff = originalApparentLength - desiredApparentLength;
                Util.Assert( apparentDiff > 0 );
                realStartIdx = _TranslateApparentSubstringLengthToActual( s, 0, apparentDiff, greedy: true );
            }
            int realLength = _TranslateApparentSubstringLengthToActual( s, realStartIdx, desiredApparentLength, greedy: true );

            // We can't just chop it at realLength--we need to also get any remaining (or
            // preceding) control sequences (in case there are pops, etc.).

            if( trimLeft )
            {
                if( useEllipsis )
                    dest.Append( c_ellipsis );

                if( -1 == stripContentBoundary )
                    stripContentBoundary = 0;

                _StripContent( dest, s, stripContentBoundary, realStartIdx - 1 );
            }

            for( int i = realStartIdx; i < (realStartIdx + realLength); i++ )
            {
                dest.Append( s[ i ] );
            }

            if( trimRight )
            {
                if( 0 != stripContentBoundary )
                    _StripContent( dest, s, realStartIdx + realLength, s.Length - 1 );

                if( useEllipsis )
                    dest.Append( c_ellipsis );
            }
        } // end Truncate()


        public static string StripControlSequences( string s )
        {
            if( String.IsNullOrWhiteSpace( s ) )
                return s;

            StringBuilder sb = new StringBuilder( s.Length );
            for( int i = 0; i < s.Length; i++ )
            {
                if( s[ i ] == CSI )
                {
                    i = _SkipControlSequence( s, i ) - 1; // -1 because the for loop will increment it
                }
                else
                {
                    sb.Append( s[ i ] );
                }
            } // end for( each char )
            return sb.ToString();
        } // end StripControlSequences()


        public static int ApparentIndexOf( string s, char c )
        {
            s = StripControlSequences( s );
            return s.IndexOf( c );
        } // end ApparentIndexOf()


        internal static Dictionary< ConsoleColor, int > ForegroundColorMap = new Dictionary< ConsoleColor, int >()
        {
            // ANSI "normal" colors
            { ConsoleColor.Black,       30 },
            { ConsoleColor.DarkRed,     31 },
            { ConsoleColor.DarkGreen,   32 },
            { ConsoleColor.DarkYellow,  33 },
            { ConsoleColor.DarkBlue,    34 },
            { ConsoleColor.DarkMagenta, 35 },
            { ConsoleColor.DarkCyan,    36 },
            { ConsoleColor.Gray,        37 },

            // ANSI "bright" colors
            { ConsoleColor.DarkGray,    90 },
            { ConsoleColor.Red,         91 },
            { ConsoleColor.Green,       92 },
            { ConsoleColor.Yellow,      93 },
            { ConsoleColor.Blue,        94 },
            { ConsoleColor.Magenta,     95 },
            { ConsoleColor.Cyan,        96 },
            { ConsoleColor.White,       97 },
        };

        internal static Dictionary< ConsoleColor, int > BackgroundColorMap = new Dictionary< ConsoleColor, int >()
        {
            // ANSI "normal" colors
            { ConsoleColor.Black,       40 },
            { ConsoleColor.DarkRed,     41 },
            { ConsoleColor.DarkGreen,   42 },
            { ConsoleColor.DarkYellow,  43 },
            { ConsoleColor.DarkBlue,    44 },
            { ConsoleColor.DarkMagenta, 45 },
            { ConsoleColor.DarkCyan,    46 },
            { ConsoleColor.Gray,        47 },

            // ANSI "bright" colors
            { ConsoleColor.DarkGray,    100 },
            { ConsoleColor.Red,         101 },
            { ConsoleColor.Green,       102 },
            { ConsoleColor.Yellow,      103 },
            { ConsoleColor.Blue,        104 },
            { ConsoleColor.Magenta,     105 },
            { ConsoleColor.Cyan,        106 },
            { ConsoleColor.White,       107 },
        };


        internal const string SGR = "m"; // SGR: "Select Graphics Rendition"
        internal const string PUSH = "56";
        internal const string POP = "57";

     // public static string FG( ConsoleColor foreground )
     // {
     //     return CSI + ForegroundColorMap[ foreground ] + SGR;
     // }

     // public static string FGBG( ConsoleColor foreground, ConsoleColor background )
     // {
     //     StringBuilder sb = new StringBuilder( 10 );
     //     sb.Append( CSI );
     //     sb.Append( ForegroundColorMap[ foreground ].ToString( CultureInfo.InvariantCulture ) );
     //     sb.Append( ';' );
     //     sb.Append( BackgroundColorMap[ background ].ToString( CultureInfo.InvariantCulture ) );
     //     sb.Append( SGR );
     //     return sb.ToString();
     // }

     // public static string PushFG( ConsoleColor foreground )
     // {
     //     StringBuilder sb = new StringBuilder( 10 );
     //     sb.Append( CSI );
     //     sb.Append( PUSH );
     //     sb.Append( ';' );
     //     sb.Append( ForegroundColorMap[ foreground ].ToString( CultureInfo.InvariantCulture ) );
     //     sb.Append( SGR );
     //     return sb.ToString();
     // }

     // public static string PushFGBG( ConsoleColor foreground, ConsoleColor background )
     // {
     //     StringBuilder sb = new StringBuilder( 14 );
     //     sb.Append( CSI );
     //     sb.Append( PUSH );
     //     sb.Append( ';' );
     //     sb.Append( ForegroundColorMap[ foreground ].ToString( CultureInfo.InvariantCulture ) );
     //     sb.Append( ';' );
     //     sb.Append( BackgroundColorMap[ background ].ToString( CultureInfo.InvariantCulture ) );
     //     sb.Append( SGR );
     //     return sb.ToString();
     // }

     // public static string Pop()
     // {
     //     return CSI + POP + SGR;
     // }

        public static StringBuilder FG( StringBuilder sb, ConsoleColor foreground )
        {
            if( !DbgProvider.HostSupportsColor )
                return sb;

            sb.Append( CSI );
            sb.Append( ForegroundColorMap[ foreground ] );
            return sb.Append( SGR );
        }

        public static StringBuilder FGBG( StringBuilder sb, ConsoleColor foreground, ConsoleColor background )
        {
            if( !DbgProvider.HostSupportsColor )
                return sb;

            sb.Append( CSI );
            sb.Append( ForegroundColorMap[ foreground ].ToString( CultureInfo.InvariantCulture ) );
            sb.Append( ';' );
            sb.Append( BackgroundColorMap[ background ].ToString( CultureInfo.InvariantCulture ) );
            return sb.Append( SGR );
        }

        public static StringBuilder PushFG( StringBuilder sb, ConsoleColor foreground )
        {
            if( !DbgProvider.HostSupportsColor )
                return sb;

            sb.Append( CSI );
            sb.Append( PUSH );
            sb.Append( ';' );
            sb.Append( ForegroundColorMap[ foreground ].ToString( CultureInfo.InvariantCulture ) );
            return sb.Append( SGR );
        }

        public static StringBuilder PushFGBG( StringBuilder sb, ConsoleColor foreground, ConsoleColor background )
        {
            if( !DbgProvider.HostSupportsColor )
                return sb;

            sb.Append( CSI );
            sb.Append( PUSH );
            sb.Append( ';' );
            sb.Append( ForegroundColorMap[ foreground ].ToString( CultureInfo.InvariantCulture ) );
            sb.Append( ';' );
            sb.Append( BackgroundColorMap[ background ].ToString( CultureInfo.InvariantCulture ) );
            return sb.Append( SGR );
        }

        public static StringBuilder Pop( StringBuilder sb )
        {
            if( !DbgProvider.HostSupportsColor )
                return sb;

            sb.Append( CSI );
            sb.Append( POP );
            return sb.Append( SGR );
        }



        public static string PushFGPop( ConsoleColor foreground, string content )
        {
            if( !DbgProvider.HostSupportsColor )
                return content;

            var sb = new StringBuilder( content.Length + 20 );
            PushFG( sb, foreground );
            sb.Append( content );
            Pop( sb );
            return sb.ToString();
        }

        [Flags]
        public enum IndentAndWrapOptions
        {
            Default                                      = 0,
            NoWordBreaking                               = 0x01, // No characters (incl. spaces) are replaced with newlines
            TruncateInsteadOfWrap                        = 0x02,
            FirstLineAlreadyIndented                     = 0x04,
            DoNotIndentContinuationLines                 = 0x08, // Could also be named DoNotIndentWrapLines. Takes
                                                                 // precedence over AddLineLeadingSpaceToAddtlContinuationIndent.
            AddLineLeadingSpaceToAddtlContinuationIndent = 0x10, // TODO: could this result in a pathological situation if
                                                                 // leading space is longer than the entire outputWidth
        }

        private const string c_PushAndReset  = "\u009b56;0m";
        private const string c_StandalonePop = "\u009b57m";

        public static string IndentAndWrap( string str,
                                            int outputWidth,
                                            IndentAndWrapOptions options,
                                            int indent,
                                            int addtlContinuationIndent )
        {
            bool truncate = 0 != (options & IndentAndWrapOptions.TruncateInsteadOfWrap);

            int minContent = 2; // 1 char of content, and a newline char

            if( truncate )
            {
                minContent = 3; // 1 char of content, 1 ellipsis char, and a newline char

                if( addtlContinuationIndent != 0 )
                {
                    throw new ArgumentException( "The combination of TruncateInsteadOfWrap and non-zero addtlContinuationIndent makes no sense." );
                }
            }

            if( (indent + addtlContinuationIndent) > (outputWidth - minContent) )
            {
                throw new ArgumentOutOfRangeException(
                    Util.Sprintf( "The outputWidth ({0}) should be somewhat larger than " +
                                      "the max possible indent ({1} + {2}).",
                                  outputWidth,
                                  indent,
                                  addtlContinuationIndent ),
                    innerException: null );
            }

            if( null == str )
                throw new ArgumentNullException( nameof( str ) );

            StringBuilder sb = new StringBuilder( str.Length * 2 ); // just an estimate

            int spaceToUse = outputWidth - indent - 1; // "- 1" because the newline actually uses a spot.

            int srcIdx = 0;

            int lastSpaceOrTabSrcIdx = -1;
            int lastSpaceOrTabDstIdx = -1;

            // We may need to replace the last char of content (space or not) when
            // truncating (to put an ellipsis in its place).
            int lastContentDstIdx = -1;

            bool inLeadingSpace = true;
            int leadingSpaceLen = 0;

            // So that we don't need to inserts push/pops for plain text.
            bool haveSeenControlSequence = false;

            bool pushInserted = false;

            void _insertIndent( int numSpaces )
            {
                sb.Append( ' ', numSpaces );
            }

            void _rememberLastSpaceOrTabIndexes()
            {
                lastSpaceOrTabSrcIdx = srcIdx;
                lastSpaceOrTabDstIdx = sb.Length;
            }

            void _rememberLastContentIndex()
            {
                lastContentDstIdx = sb.Length;
            }

            bool _weHaveConsumedAllAvailableOutputWidth()
            {
                return spaceToUse == 0;
            }

            bool _backtrackToLastSpaceOrTab()
            {
                bool backtrackingAllowed = 0 == (options & IndentAndWrapOptions.NoWordBreaking);

                if( backtrackingAllowed &&
                    (lastSpaceOrTabSrcIdx > 0) ) // can't be 0, because we don't count leading space
                {
                    Util.Assert( lastSpaceOrTabDstIdx >= 0 );
                    srcIdx = lastSpaceOrTabSrcIdx;
                    sb.Length = lastSpaceOrTabDstIdx;

                    return true;
                }

                return false;
            }

            bool _stillInBounds()
            {
                return srcIdx < str.Length;
            }

            // Returns true if srcIdx is still within the bounds of str.
            bool _consumeControlSequences()
            {
                while( _stillInBounds() && (str[ srcIdx ] == CSI) )
                {
                    haveSeenControlSequence = true;
                    int pastSeq = _SkipControlSequence( str, srcIdx ); // TODO: make _SkipControlSequence not assert if first char isn't CSI?
                    sb.Append( str, srcIdx, pastSeq - srcIdx );
                    srcIdx = pastSeq;
                }
                return _stillInBounds();
            }

            void _saveAndResetSgrState()
            {
                if( haveSeenControlSequence && !pushInserted )
                {
                    sb.Append( c_PushAndReset );
                    pushInserted = true;
                }
            }

            void _restoreSgrState()
            {
                if( haveSeenControlSequence )
                {
                    Util.Assert( pushInserted );

                    sb.Append( c_StandalonePop );
                    pushInserted = false;
                }
            }

            void _completeLineAndIndent( int numSpaces, bool isWrap )
            {
                // Save the current SGR (color) state and (temporarily) reset to default,
                // if necessary.
                _saveAndResetSgrState();

                sb.Append( '\n' );
                _insertIndent( numSpaces );

                // Restore the SGR state.
                _restoreSgrState();

                // Need to reset various counters/state:

                spaceToUse = outputWidth - numSpaces - 1; // "- 1" because the newline actually uses a spot.

                Util.Assert( !pushInserted );

                lastSpaceOrTabSrcIdx = -1;
                lastSpaceOrTabDstIdx = -1;

                // This should not be needed... but I'm doing it defensively.
                lastContentDstIdx = -1;

                if( !isWrap )
                {
                    // We're actually on the next line of input.
                    inLeadingSpace = true;
                    leadingSpaceLen = 0;
                }
            }

            void _appendContentChar( char c )
            {
                _rememberLastContentIndex();

                if( Char.IsWhiteSpace( c ) )
                {
                    if( !inLeadingSpace )
                    {
                        _rememberLastSpaceOrTabIndexes();
                    }
                    else
                    {
                        leadingSpaceLen++;
                    }
                }
                else
                {
                    inLeadingSpace = false;
                }

                sb.Append( c );
                spaceToUse--;
            }

            // Returns 'true' if we actually found a newline; false if we hit the end of
            // the string first. Will preserve control sequences.
            bool _seekToNextLine()
            {
                while( _consumeControlSequences() )
                {
                    char c = str[ srcIdx ];

                    if( c == '\r' )
                    {
                        // ignore it
                    }
                    else if( c == '\n' )
                    {
                        return true;
                    }

                    srcIdx++;
                }
                return false;
            }

            if( (options & IndentAndWrapOptions.FirstLineAlreadyIndented) == 0 )
            {
                _insertIndent( indent );
            }

            while( _consumeControlSequences() )
            {
                // We consume control sequences in the "while" condition, so the
                // characters we consider here are purely content.

                char c = str[ srcIdx ];

                if( c == '\r' )
                {
                    // ignore it
                }
                else if( c == '\n' )
                {
                    // We consider the case of a newline char before the
                    // _weHaveConsumedAllAvailableOutputWidth case, because the newline
                    // does not consume available space (but rather resets it).

                    _completeLineAndIndent( indent, isWrap: false );
                }
                else if( _weHaveConsumedAllAvailableOutputWidth() )
                {
                    // Need to decide where to put a newline... was there a space where we
                    // could break up the line, or do we have to just chop right where we
                    // are?

                    int totalIndent = indent;
                    bool backtracked = false;

                    if( Char.IsWhiteSpace( c ) )
                    {
                        // So we can "backtrack" to it.
                        _rememberLastSpaceOrTabIndexes();
                    }

                    bool moreContentAfterTruncation = false;

                    if( truncate )
                    {
                        if( lastContentDstIdx == -1 )
                            throw new Exception( "unexpected" );

                        // Note that we put the ellipsis /after/ resetting SGR (color)
                        // state. This is a stylistic choice.

                        sb.Remove( lastContentDstIdx, 1 ); // make way for the ellipsis.

                        moreContentAfterTruncation = _seekToNextLine();

                        _saveAndResetSgrState();

                        sb.Append( c_ellipsis );
                    }
                    else
                    {
                        bool doNotIndentContinuation = 0 != (options & IndentAndWrapOptions.DoNotIndentContinuationLines);

                        if( doNotIndentContinuation )
                            totalIndent = 0;
                        else
                            totalIndent += addtlContinuationIndent;

                        // Could be a backtrack of 0 chars if the current char is a space.
                        backtracked = _backtrackToLastSpaceOrTab();

                        if( !doNotIndentContinuation &&
                            (options & IndentAndWrapOptions.AddLineLeadingSpaceToAddtlContinuationIndent) != 0 )
                        {
                            totalIndent += leadingSpaceLen;
                        }
                    }

                    if( !truncate || moreContentAfterTruncation )
                    {
                        _completeLineAndIndent( totalIndent, isWrap: !truncate );
                    }
                    else
                    {
                        // Don't forget the last POP.
                        _restoreSgrState();
                    }

                    if( !truncate && !backtracked )
                    {
                        // If we didn't backtrack, then we haven't yet accounted for the
                        // current character--don't lose it!
                        _appendContentChar( c );
                    }
                }
                else
                {
                    _appendContentChar( c );
                }

                srcIdx++;
            } // end while( still more str )

            return sb.ToString();
        } // end _WordWrap()


        // TODO: theme support

#if DEBUG
        private class CaStringUtilLengthTestCase
        {
            public readonly string Input;
            public readonly int ExpectedLength;
            public readonly Type ExpectedExceptionType;

            public CaStringUtilLengthTestCase( string input, int expectedLength )
            {
                Input = input;
                ExpectedLength = expectedLength;
            }

            public CaStringUtilLengthTestCase( string input, Type expectedExceptionType )
            {
                Input = input;
                ExpectedExceptionType = expectedExceptionType;
            }
        } // end class CaStringUtilLengthTestCase


        private class CaStringUtilSubstringTestCase
        {
            public readonly string Input;
            public readonly int StartIdx;
            public readonly int ApparentLength;
            public readonly string ExpectedOutput;
            public readonly Type ExpectedExceptionType;

            private CaStringUtilSubstringTestCase( string input,
                                                   int startIdx,
                                                   int apparentLength )
            {
                Input = input;
                StartIdx = startIdx;
                ApparentLength = apparentLength;
            }

            public CaStringUtilSubstringTestCase( string input,
                                                  int startIdx,
                                                  int apparentLength,
                                                  string expectedOutput )
                : this( input, startIdx, apparentLength )
            {
                ExpectedOutput = expectedOutput;
            }

            public CaStringUtilSubstringTestCase( string input,
                                                  int startIdx,
                                                  int apparentLength,
                                                  Type expectedExceptionType )
                : this( input, startIdx, apparentLength )
            {
                ExpectedExceptionType = expectedExceptionType;
            }
        } // end class CaStringUtilSubstringTestCase


        private class CaStringUtilTruncateTestCase
        {
            public readonly string Input;
            public readonly int MaxApparentLength;
            public readonly bool UseEllipsis;
            public readonly TrimLocation TrimLocation;
            public readonly string ExpectedOutput;
            public readonly Type ExpectedExceptionType;

            private CaStringUtilTruncateTestCase( string input,
                                                  int maxApparentLength,
                                                  bool useEllipsis,
                                                  TrimLocation trimLocation )
            {
                Input = input;
                MaxApparentLength = maxApparentLength;
                UseEllipsis = useEllipsis;
                TrimLocation = trimLocation;
            }

            private CaStringUtilTruncateTestCase( string input,
                                                  int maxApparentLength,
                                                  bool useEllipsis,
                                                  bool trimLeft )
                : this( input,
                        maxApparentLength,
                        useEllipsis,
                        trimLeft ? TrimLocation.Left : TrimLocation.Right )
            {
            }

            public CaStringUtilTruncateTestCase( string input,
                                                 int maxApparentLength,
                                                 string expectedOutput )
                : this( input, maxApparentLength, true, expectedOutput )
            {
            }

            public CaStringUtilTruncateTestCase( string input,
                                                 int maxApparentLength,
                                                 bool useEllipsis,
                                                 string expectedOutput )
                : this( input, maxApparentLength, useEllipsis, false, expectedOutput )
            {
            }

            public CaStringUtilTruncateTestCase( string input,
                                                 int maxApparentLength,
                                                 bool useEllipsis,
                                                 bool trimLeft,
                                                 string expectedOutput )
                : this( input, maxApparentLength, useEllipsis, trimLeft )
            {
                ExpectedOutput = expectedOutput;
            }

            public CaStringUtilTruncateTestCase( string input,
                                                 int maxApparentLength,
                                                 bool useEllipsis,
                                                 TrimLocation trimLocation,
                                                 string expectedOutput )
                : this( input, maxApparentLength, useEllipsis, trimLocation )
            {
                ExpectedOutput = expectedOutput;
            }

            public CaStringUtilTruncateTestCase( string input,
                                                 int maxApparentLength,
                                                 Type expectedExceptionType )
                : this( input, maxApparentLength, true, expectedExceptionType )
            {
                ExpectedExceptionType = expectedExceptionType;
            }

            public CaStringUtilTruncateTestCase( string input,
                                                 int maxApparentLength,
                                                 bool useEllipsis,
                                                 Type expectedExceptionType )
                : this( input, maxApparentLength, useEllipsis, false )
            {
                ExpectedExceptionType = expectedExceptionType;
            }
        } // end class CaStringUtilTruncateTestCase


        private class CaStringUtilStripTestCase
        {
            public readonly string Input;
            public readonly string ExpectedOutput;
            public readonly Type ExpectedExceptionType;

            private CaStringUtilStripTestCase( string input )
            {
                Input = input;
            }

            public CaStringUtilStripTestCase( string input,
                                              string expectedOutput )
                : this( input )
            {
                ExpectedOutput = expectedOutput;
            }

            public CaStringUtilStripTestCase( string input,
                                              Type expectedExceptionType )
                : this( input )
            {
                ExpectedExceptionType = expectedExceptionType;
            }
        } // end class CaStringUtilStripTestCase


        private class CaStringUtilIndentAndWrapTestCase
        {
            public readonly string Input;
            public readonly int OutputWidth;
            public readonly IndentAndWrapOptions Options;
            public readonly int Indent;
            public readonly int AddtlContinuationIndent;

            public readonly String ExpectedOutput;
            public readonly Type ExpectedExceptionType;

            private CaStringUtilIndentAndWrapTestCase( string input,
                                                       int outputWidth )
            {
                Input = input;
                OutputWidth = outputWidth;
            }

            public CaStringUtilIndentAndWrapTestCase( string input,
                                                      int outputWidth,
                                                      string expectedOutput )
                : this( input, outputWidth )
            {
                ExpectedOutput = expectedOutput;
            }

            public CaStringUtilIndentAndWrapTestCase( string input,
                                                      int outputWidth,
                                                      Type expectedExceptionType )
                : this( input, outputWidth )
            {
                ExpectedExceptionType = expectedExceptionType;
            }

        /* public static string IndentAndWrap( string str,
                                               int outputWidth,
                                               IndentAndWrapOptions options,
                                               int indent,
                                               int addtlContinuationIndent ) */

            public CaStringUtilIndentAndWrapTestCase( string input,
                                                      int outputWidth,
                                                      IndentAndWrapOptions options,
                                                      int indent,
                                                      int addtlContinuationIndent,
                                                      string expectedOutput )
                : this( input, outputWidth, expectedOutput )
            {
                Options = options;
                Indent = indent;
                AddtlContinuationIndent = addtlContinuationIndent;
            }
        } // end class CaStringUtilIndentAndWrapTestCase



        private static List< CaStringUtilLengthTestCase > sm_lengthTests = new List< CaStringUtilLengthTestCase >()
        {
            new CaStringUtilLengthTestCase( "\u009b91mThis should be red.\u009bm This should not.\r\n",
                                            38 ),
            new CaStringUtilLengthTestCase( String.Empty,
                                            0 ),
            new CaStringUtilLengthTestCase( "hi",
                                            2 ),
            new CaStringUtilLengthTestCase( "\u009b91m",
                                            0 ),
            new CaStringUtilLengthTestCase( "\u009bm",
                                            0 ),
            new CaStringUtilLengthTestCase( "\u009bm123",
                                            3 ),
            new CaStringUtilLengthTestCase( "123\u009bm123",
                                            6 ),
            new CaStringUtilLengthTestCase( "123\u009bm",
                                            3 ),
            new CaStringUtilLengthTestCase( "\u009b42;53m123\u009bm",
                                            3 ),
            new CaStringUtilLengthTestCase( "\u009b",
                                            typeof( ArgumentException ) ),
            new CaStringUtilLengthTestCase( "\u009b42;",
                                            _CreateBrokenSequenceException().GetType() ),
            new CaStringUtilLengthTestCase( "123\u009b42;",
                                            _CreateBrokenSequenceException().GetType() ),
            new CaStringUtilLengthTestCase( "\u009b42;56 m123",
                                            typeof( ArgumentException ) ),
        };


        private static List< CaStringUtilSubstringTestCase > sm_substringTests = new List< CaStringUtilSubstringTestCase >()
        {
 /*  0 */   new CaStringUtilSubstringTestCase( String.Empty,
                                               0,
                                               0,
                                               String.Empty ),
 /*  1 */   new CaStringUtilSubstringTestCase( "1",
                                               0,
                                               1,
                                               "1" ),
 /*  2 */   new CaStringUtilSubstringTestCase( "12",
                                               0,
                                               1,
                                               "1" ),
 /*  3 */   new CaStringUtilSubstringTestCase( "12",
                                               1,
                                               1,
                                               "2" ),
 /*  4 */   new CaStringUtilSubstringTestCase( "123",
                                               0,
                                               2,
                                               "12" ),
 /*  5 */   new CaStringUtilSubstringTestCase( "123",
                                               1,
                                               2,
                                               "23" ),
 /*  6 */   new CaStringUtilSubstringTestCase( "1234",
                                               1,
                                               2,
                                               "23" ),
 /*  7 */   new CaStringUtilSubstringTestCase( "1234\u009bm",
                                               1,
                                               2,
                                               "23" ),
 /*  8 */   new CaStringUtilSubstringTestCase( "\u009bm1234",
                                               0,
                                               2,
                                               "\u009bm12" ),
 /*  9 */   new CaStringUtilSubstringTestCase( "1\u009bm234",
                                               3,
                                               2,
                                               "23" ),
 /* 10 */   new CaStringUtilSubstringTestCase( "123\u009bm4",
                                               1,
                                               2,
                                               "23\u009bm" ),
 /* 11 */   new CaStringUtilSubstringTestCase( "123\u009bm\u009bm4",
                                               startIdx:       1,
                                               apparentLength: 2,
                                               expectedOutput: "23\u009bm\u009bm" ), // we should pick up both control sequences (greedy)
 /* 12 */   new CaStringUtilSubstringTestCase( "123\u009b",
                                               1,
                                               2,
                                               _CreateBrokenSequenceException().GetType() ),
        };


        private static List< CaStringUtilTruncateTestCase > sm_truncateTests = new List< CaStringUtilTruncateTestCase >()
        {
 /*  0 */   new CaStringUtilTruncateTestCase( String.Empty,
                                              4,
                                              String.Empty ),
 /*  1 */   new CaStringUtilTruncateTestCase( "1",
                                              4,
                                              "1" ),
 /*  2 */   new CaStringUtilTruncateTestCase( "12",
                                              4,
                                              "12" ),
 /*  3 */   new CaStringUtilTruncateTestCase( "123",
                                              4,
                                              "123" ),
 /*  4 */   new CaStringUtilTruncateTestCase( "1234",
                                              4,
                                              "1234" ),
 /*  5 */   new CaStringUtilTruncateTestCase( "1234\u009bm",
                                               4,
                                               "1234\u009bm" ),
 /*  6 */   new CaStringUtilTruncateTestCase( "\u009bm1234",
                                               4,
                                               "\u009bm1234" ),
 /*  7 */   new CaStringUtilTruncateTestCase( "1\u009bm234",
                                               4,
                                               "1\u009bm234" ),
 /*  8 */   new CaStringUtilTruncateTestCase( "123\u009bm4",
                                              4,
                                              "123\u009bm4" ),
 /*  9 */   new CaStringUtilTruncateTestCase( "123\u009b",
                                               4,
                                               _CreateBrokenSequenceException().GetType() ),
 /* 10 */   new CaStringUtilTruncateTestCase( "12345",
                                              4,
                                              "123…" ),
 /* 11 */   new CaStringUtilTruncateTestCase( "12\u009b101;32m345",
                                              4,
                                              "12\u009b101;32m3…" ),
 /* 12 */   new CaStringUtilTruncateTestCase( "\u009bm12\u009bm345",
                                              4,
                                              "\u009bm12\u009bm3…" ),
 /* 13 */   new CaStringUtilTruncateTestCase( "\u009bm12\u009bm345",
                                              5,
                                              "\u009bm12\u009bm345" ),
 /* 14 */   new CaStringUtilTruncateTestCase( "\u009bm12\u009bm34567",
                                              6,
                                              "\u009bm12\u009bm345…" ),
 /* 15 */   new CaStringUtilTruncateTestCase( "\u009bm1234567\u009bm",
                                              6,
                                              "\u009bm12345\u009bm…" ),
 /* 16 */   new CaStringUtilTruncateTestCase( "",
                                              1,
                                              false,
                                              "" ),
 /* 17 */   new CaStringUtilTruncateTestCase( "",
                                              1,
                                              true,
                                              new ArgumentOutOfRangeException().GetType() ),
 /* 18 */   new CaStringUtilTruncateTestCase( "a",
                                              1,
                                              false,
                                              "a" ),
 /* 19 */   new CaStringUtilTruncateTestCase( "ab",
                                              1,
                                              false,
                                              "a" ),
 /* 20 */   new CaStringUtilTruncateTestCase( "\u009bm",
                                              1,
                                              false,
                                              "\u009bm" ),
 /* 21 */   new CaStringUtilTruncateTestCase( "\u009bma",
                                              1,
                                              false,
                                              "\u009bma" ),
// TrimLeft variations

 /* 22 */   new CaStringUtilTruncateTestCase( String.Empty,
                                              4,
                                              useEllipsis: true,
                                              trimLeft: true,
                                              expectedOutput: String.Empty ),
 /* 23 */   new CaStringUtilTruncateTestCase( "1",
                                              4,
                                              useEllipsis: true,
                                              trimLeft: true,
                                              expectedOutput: "1" ),
 /* 24 */   new CaStringUtilTruncateTestCase( "12",
                                              4,
                                              useEllipsis: true,
                                              trimLeft: true,
                                              expectedOutput: "12" ),
 /* 25 */   new CaStringUtilTruncateTestCase( "123",
                                              4,
                                              useEllipsis: true,
                                              trimLeft: true,
                                              expectedOutput: "123" ),
 /* 26 */   new CaStringUtilTruncateTestCase( "1234",
                                              4,
                                              useEllipsis: true,
                                              trimLeft: true,
                                              expectedOutput: "1234" ),
 /* 27 */   new CaStringUtilTruncateTestCase( "1234\u009bm",
                                               4,
                                               useEllipsis: true,
                                               trimLeft: true,
                                               expectedOutput: "1234\u009bm" ),
 /* 28 */   new CaStringUtilTruncateTestCase( "\u009bm1234",
                                               4,
                                               useEllipsis: true,
                                               trimLeft: true,
                                               expectedOutput: "\u009bm1234" ),
 /* 29 */   new CaStringUtilTruncateTestCase( "1\u009bm234",
                                               4,
                                               useEllipsis: true,
                                               trimLeft: true,
                                               expectedOutput: "1\u009bm234" ),
 /* 30 */   new CaStringUtilTruncateTestCase( "123\u009bm4",
                                              4,
                                              useEllipsis: true,
                                              trimLeft: true,
                                              expectedOutput: "123\u009bm4" ),
 /* 31 */   new CaStringUtilTruncateTestCase( "12345",
                                              4,
                                              useEllipsis: true,
                                              trimLeft: true,
                                              expectedOutput: "…345" ),
 /* 32 */   new CaStringUtilTruncateTestCase( "12\u009b101;32m345",
                                              4,
                                              useEllipsis: true,
                                              trimLeft: true,
                                              expectedOutput: "…\u009b101;32m345" ),
 /* 33 */   new CaStringUtilTruncateTestCase( "\u009bm12\u009bm345",
                                              4,
                                              useEllipsis: true,
                                              trimLeft: true,
                                              expectedOutput: "…\u009bm\u009bm345" ),
 /* 34 */   new CaStringUtilTruncateTestCase( "\u009bm12\u009bm345\u009bm",
                                              4,
                                              useEllipsis: true,
                                              trimLeft: true,
                                              expectedOutput: "…\u009bm\u009bm345\u009bm" ),
 /* 35 */   new CaStringUtilTruncateTestCase( "\u009bm12\u009bm345",
                                              5,
                                              useEllipsis: true,
                                              trimLeft: true,
                                              expectedOutput: "\u009bm12\u009bm345" ),
 /* 36 */   new CaStringUtilTruncateTestCase( "\u009bm12\u009bm34567",
                                              6,
                                              useEllipsis: true,
                                              trimLeft: true,
                                              expectedOutput: "…\u009bm\u009bm34567" ),
 /* 37 */   new CaStringUtilTruncateTestCase( "\u009bm1234567\u009bm",
                                              6,
                                              useEllipsis: true,
                                              trimLeft: true,
                                              expectedOutput: "…\u009bm34567\u009bm" ),
 /* 38 */   new CaStringUtilTruncateTestCase( "",
                                              1,
                                              useEllipsis: false,
                                              trimLeft: true,
                                              expectedOutput: "" ),
 /* 39 */   new CaStringUtilTruncateTestCase( "a",
                                              1,
                                              useEllipsis: false,
                                              trimLeft: true,
                                              expectedOutput: "a" ),
 /* 40 */   new CaStringUtilTruncateTestCase( "ab",
                                              1,
                                              useEllipsis: false,
                                              trimLeft: true,
                                              expectedOutput: "b" ),
 /* 41 */   new CaStringUtilTruncateTestCase( "\u009bm",
                                              1,
                                              useEllipsis: false,
                                              trimLeft: true,
                                              expectedOutput: "\u009bm" ),
 /* 42 */   new CaStringUtilTruncateTestCase( "\u009bma",
                                              1,
                                              useEllipsis: false,
                                              trimLeft: true,
                                              expectedOutput: "\u009bma" ),

// TrimLocation.Center variations

 /* 43 */   new CaStringUtilTruncateTestCase( String.Empty,
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: String.Empty ),
 /* 44 */   new CaStringUtilTruncateTestCase( "1",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "1" ),
 /* 45 */   new CaStringUtilTruncateTestCase( "12",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "12" ),
 /* 46 */   new CaStringUtilTruncateTestCase( "123",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "123" ),
 /* 47 */   new CaStringUtilTruncateTestCase( "1234",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "1234" ),
 /* 48 */   new CaStringUtilTruncateTestCase( "1234\u009bm",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "1234\u009bm" ),
 /* 49 */   new CaStringUtilTruncateTestCase( "\u009bm1234",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "\u009bm1234" ),
 /* 50 */   new CaStringUtilTruncateTestCase( "1\u009bm234",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "1\u009bm234" ),
 /* 51 */   new CaStringUtilTruncateTestCase( "123\u009bm4",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "123\u009bm4" ),

 /* 52 */   new CaStringUtilTruncateTestCase( "12345",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "12345" ),
 /* 53 */   new CaStringUtilTruncateTestCase( "12345\u009bm",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "12345\u009bm" ),
 /* 54 */   new CaStringUtilTruncateTestCase( "\u009bm12345",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "\u009bm12345" ),
 /* 55 */   new CaStringUtilTruncateTestCase( "1\u009bm2345",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "1\u009bm2345" ),
 /* 56 */   new CaStringUtilTruncateTestCase( "123\u009bm45",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "123\u009bm45" ),


 /* 57 */   new CaStringUtilTruncateTestCase( "123456",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "12…56" ),
 /* 58 */   new CaStringUtilTruncateTestCase( "12\u009b101;32m3456",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "12\u009b101;32m…56" ),
 /* 59 */   new CaStringUtilTruncateTestCase( "\u009bm12\u009bm3456",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "\u009bm12\u009bm…56" ),
 /* 60 */   new CaStringUtilTruncateTestCase( "\u009bm12\u009bm3456\u009bm",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "\u009bm12\u009bm…56\u009bm" ),

 /* 61 */   new CaStringUtilTruncateTestCase( "\u009bm12\u009bm345",
                                              5,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "\u009bm12\u009bm345" ),
 /* 62 */   new CaStringUtilTruncateTestCase( "\u009bm12\u009bm34567",
                                              6,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "\u009bm12\u009bm3…67" ),
 /* 63 */   new CaStringUtilTruncateTestCase( "\u009bm1234567\u009bm",
                                              6,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "\u009bm123…67\u009bm" ),

 /* 64 */   new CaStringUtilTruncateTestCase( "\u009bm123456789abcdefghijk\u009bm",
                                              6,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "\u009bm123…jk\u009bm" ),
 /* 65 */   new CaStringUtilTruncateTestCase( "\u009bm12345678\u009bm9ab\u009bmcdefghijk\u009bm",
                                              6,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "\u009bm123…\u009bm\u009bmjk\u009bm" ),
 /* 66 */   new CaStringUtilTruncateTestCase( "123456789abcdefghijk",
                                              6,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "123…jk" ),
 /* 67 */   new CaStringUtilTruncateTestCase( "123456789abcdefghijk",
                                              7,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "123…ijk" ),

                                              // The control sequence shows up with the first half here because
                                              // of how _TranslateApparentSubstringLengthToActual works: if it
                                              // ends up on a CSI character, it has to consume the rest of the
                                              // control sequence ("greedy").
 /* 68 */   new CaStringUtilTruncateTestCase( "123\u009bm56789abcdefghijk",
                                              7,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "123\u009bm…ijk" ),
                                              // ... but it only has to consume one:
 /* 69 */   new CaStringUtilTruncateTestCase( "12\u009bm\u009bm3456789abcdefghijk",
                                              7,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "12\u009bm\u009bm3…ijk" ),
 /* 70 */   new CaStringUtilTruncateTestCase( "123\u009bm\u009bm456789abcdefghijk",
                                              7,
                                              useEllipsis: true,
                                              trimLocation: TrimLocation.Center,
                                              expectedOutput: "123\u009bm\u009bm…ijk" ),
        };


        private static List< CaStringUtilStripTestCase > sm_stripTests = new List< CaStringUtilStripTestCase >()
        {
 /*  0 */   new CaStringUtilStripTestCase( null,
                                           (string) null ),
 /*  1 */   new CaStringUtilStripTestCase( String.Empty,
                                           String.Empty ),
 /*  2 */   new CaStringUtilStripTestCase( "1",
                                           "1" ),
 /*  3 */   new CaStringUtilStripTestCase( "12",
                                           "12" ),
 /*  4 */   new CaStringUtilStripTestCase( "1234\u009bm",
                                           "1234" ),
 /*  5 */   new CaStringUtilStripTestCase( "\u009bm1234",
                                           "1234" ),
 /*  6 */   new CaStringUtilStripTestCase( "1\u009bm234",
                                           "1234" ),
 /*  7 */   new CaStringUtilStripTestCase( "123\u009bm4",
                                           "1234" ),
 /*  8 */   new CaStringUtilStripTestCase( "123\u009b",
                                            _CreateBrokenSequenceException().GetType() ),
 /*  9 */   new CaStringUtilStripTestCase( "\u009bm12\u009bm345",
                                           "12345" ),
 /* 10 */   new CaStringUtilStripTestCase( "\u009bm12\u009bm345\u009b42;35m",
                                           "12345" ),
 /* 11 */   new CaStringUtilStripTestCase( "\u009bm",
                                           String.Empty ),
 /* 12 */   new CaStringUtilStripTestCase( "\u009b32;m\u009bm",
                                           String.Empty ),
        };


        private static List<CaStringUtilIndentAndWrapTestCase> sm_indentAndWrapTests = new List<CaStringUtilIndentAndWrapTestCase>()
        {
            /*
            */
            new CaStringUtilIndentAndWrapTestCase( null,
                                                   outputWidth: 4,
                                                   typeof( ArgumentNullException ) ),

            new CaStringUtilIndentAndWrapTestCase( "",
                                                   outputWidth: 4,
                                                   expectedOutput: "" ),

            new CaStringUtilIndentAndWrapTestCase( "1 2 3 4 5 6 7 8 9",
                                                   outputWidth: 4,
                                                   expectedOutput: "1 2\n3 4\n5 6\n7 8\n9" ),

            new CaStringUtilIndentAndWrapTestCase( " 1 2 3 4 5 6 7 8 9",
                                                   outputWidth: 4,
                                                   expectedOutput: " 1\n2 3\n4 5\n6 7\n8 9" ),

            new CaStringUtilIndentAndWrapTestCase( "12 34 56 78 90",
                                                   outputWidth: 4,
                                                   expectedOutput: "12\n34\n56\n78\n90" ),

            new CaStringUtilIndentAndWrapTestCase( " 12 34 56 78 90",
                                                   outputWidth: 4,
                                                   expectedOutput: " 12\n34\n56\n78\n90" ),

            new CaStringUtilIndentAndWrapTestCase( "123 456 789 0ab",
                                                   outputWidth: 4,
                                                   expectedOutput: "123\n456\n789\n0ab" ),

            new CaStringUtilIndentAndWrapTestCase( "1234 5678 90ab",
                                                   outputWidth: 4,
                                                   expectedOutput: "123\n4\n567\n8\n90a\nb" ),

            new CaStringUtilIndentAndWrapTestCase( " 1 2 3 4 5 6 7 8 9",
                                                   outputWidth: 4,
                                                   options: IndentAndWrapOptions.AddLineLeadingSpaceToAddtlContinuationIndent,
                                                   indent: 1,
                                                   addtlContinuationIndent: 0,
                                                   expectedOutput: "  1\n  2\n  3\n  4\n  5\n  6\n  7\n  8\n  9" ),

            new CaStringUtilIndentAndWrapTestCase( "1 2 3 4 5 6 7 8 9",
                                                   outputWidth: 4,
                                                   options: IndentAndWrapOptions.Default,
                                                   indent: 2,
                                                   addtlContinuationIndent: 0,
                                                   expectedOutput: "  1\n  2\n  3\n  4\n  5\n  6\n  7\n  8\n  9" ),

            new CaStringUtilIndentAndWrapTestCase( " 1 2 3 4 5 6 7 8 9",
                                                   outputWidth: 4,
                                                   options: IndentAndWrapOptions.Default,
                                                   indent: 2,
                                                   addtlContinuationIndent: 0,
                                                   expectedOutput: "   \n  1\n  2\n  3\n  4\n  5\n  6\n  7\n  8\n  9" ),

            new CaStringUtilIndentAndWrapTestCase( " 1 2 3 4 5 6 7 8 9",
                                                   outputWidth: 4,
                                                   options: IndentAndWrapOptions.NoWordBreaking,
                                                   indent: 2,
                                                   addtlContinuationIndent: 0,
                                                   expectedOutput: "   \n  1\n   \n  2\n   \n  3\n   \n  4\n   \n  5\n   \n  6\n   \n  7\n   \n  8\n   \n  9" ),

            new CaStringUtilIndentAndWrapTestCase( "1 2 3 4 5 6 7 8 9",
                                                   outputWidth: 4,
                                                   options: IndentAndWrapOptions.FirstLineAlreadyIndented,
                                                   indent: 2,
                                                   addtlContinuationIndent: 0,
                                                   expectedOutput: "1\n  2\n  3\n  4\n  5\n  6\n  7\n  8\n  9" ),

            new CaStringUtilIndentAndWrapTestCase( "1 2\n3 4\n5 6\n7 8\n9",
                                                   outputWidth: 4,
                                                   options: IndentAndWrapOptions.Default,
                                                   indent: 0,
                                                   addtlContinuationIndent: 0,
                                                   expectedOutput: "1 2\n3 4\n5 6\n7 8\n9" ),

            new CaStringUtilIndentAndWrapTestCase( "1 2\n3 4\n5 6\n7 8\n9",
                                                   outputWidth: 4,
                                                   options: IndentAndWrapOptions.Default,
                                                   indent: 1,
                                                   addtlContinuationIndent: 0,
                                                   expectedOutput: " 1\n 2\n 3\n 4\n 5\n 6\n 7\n 8\n 9" ),

            new CaStringUtilIndentAndWrapTestCase( "1 2\n3 4\n5 6\n7 8\n9",
                                                   outputWidth: 4,
                                                   options: IndentAndWrapOptions.DoNotIndentContinuationLines,
                                                   indent: 1,
                                                   addtlContinuationIndent: 0,
                                                   expectedOutput: " 1\n2\n 3\n4\n 5\n6\n 7\n8\n 9" ),

            new CaStringUtilIndentAndWrapTestCase( " 1 2\n3 4\n5 6\n7 8\n9",
                                                   outputWidth: 4,
                                                   options: IndentAndWrapOptions.DoNotIndentContinuationLines |
                                                            IndentAndWrapOptions.AddLineLeadingSpaceToAddtlContinuationIndent,
                                                   indent: 1,
                                                   addtlContinuationIndent: 0,
                                                   expectedOutput: "  1\n2\n 3\n4\n 5\n6\n 7\n8\n 9" ),

            new CaStringUtilIndentAndWrapTestCase( "1 2\n3 4\n5 6\n7 8\n9",
                                                   outputWidth: 4,
                                                   options: IndentAndWrapOptions.TruncateInsteadOfWrap,
                                                   indent: 1,
                                                   addtlContinuationIndent: 0,
                                                   expectedOutput: " 1…\n 3…\n 5…\n 7…\n 9" ),

            new CaStringUtilIndentAndWrapTestCase( "1 2\n\u009b91m3 4\n5 6\n7 8\u009bm\n9",
                                                   outputWidth: 4,
                                                   options: IndentAndWrapOptions.TruncateInsteadOfWrap,
                                                   indent: 1,
                                                   addtlContinuationIndent: 0,
                                                   expectedOutput: $" 1…\n \u009b91m3{c_PushAndReset}…\n {c_StandalonePop}5{c_PushAndReset}…\n {c_StandalonePop}7\u009bm{c_PushAndReset}…\n {c_StandalonePop}9" ),
        };


        private static string _EscapeStringForDisplay( string s )
        {
            StringBuilder sb = new StringBuilder( s.Length * 2 );

            foreach( char c in s )
            {
                if( c == CSI )
                    sb.Append( @"\" ).Append( "u009b" );
                else if( c == '\r' )
                    sb.Append( @"\" ).Append( 'r' );
                else if( c == '\n' )
                    sb.Append( @"\" ).Append( 'n' );
                else
                    sb.Append( c );
            }

            return sb.ToString();
        }


        public static void SelfTest()
        {
            int failures = 0;
            for( int i = 0; i < sm_lengthTests.Count; i++ )
            {
                var testCase = sm_lengthTests[ i ];
                try
                {
                    int actual = Length( testCase.Input );
                    if( actual != testCase.ExpectedLength )
                    {
                        failures++;
                      //Util.Fail( Util.Sprintf( "Test case {0} failed. Expected: {1}, Actual: {2}.",
                      //                         i,
                      //                         testCase.ExpectedLength,
                      //                         actual ) );
                        Console.WriteLine( "Length test case {0} failed. Expected: {1}, Actual: {2}.",
                                           i,
                                           testCase.ExpectedLength,
                                           actual );
                    }
                }
                catch( Exception e )
                {
                    if( e.GetType() != testCase.ExpectedExceptionType )
                    {
                        failures++;
                        Console.WriteLine( "Test case {0} failed. Expected exception type: {1}, Actual: {2}.",
                                           i,
                                           null == testCase.ExpectedExceptionType ?
                                                   "<none>" :
                                                   testCase.ExpectedExceptionType.Name,
                                           e.GetType().Name );
                    }
                }
            } // end foreach( testCase )

            if( 0 == failures )
            {
                Console.WriteLine( "CaStringUtil Length tests passed." );
            }
            else
            {
                Console.WriteLine( "CaStringUtil Length tests failed ({0} failures).", failures );
            }

            int substringFailures = 0;
            for( int i = 0; i < sm_substringTests.Count; i++ )
            {
                var testCase = sm_substringTests[ i ];
                try
                {
                    string output = Substring( testCase.Input, testCase.StartIdx, testCase.ApparentLength );
                    if( 0 != String.CompareOrdinal( output, testCase.ExpectedOutput ) )
                    {
                        substringFailures++;
                        Console.WriteLine( "Substring test case {0} failed. Expected: {1}, Actual: {2}.",
                                           i,
                                           testCase.ExpectedOutput,
                                           output );
                    }
                }
                catch( Exception e )
                {
                    if( e.GetType() != testCase.ExpectedExceptionType )
                    {
                        substringFailures++;
                        Console.WriteLine( "Substring Test case {0} failed. Expected exception type: {1}, Actual: {2}.",
                                           i,
                                           null == testCase.ExpectedExceptionType ?
                                                   "<none>" :
                                                   testCase.ExpectedExceptionType.Name,
                                           e.GetType().Name );
                    }
                }
            } // end foreach( testCase )

            if( 0 == substringFailures )
            {
                Console.WriteLine( "CaStringUtil Substring tests passed." );
            }
            else
            {
                Console.WriteLine( "CaStringUtil Substring tests failed ({0} failures).", substringFailures );
            }

            failures += substringFailures;


            int truncateFailures = 0;
            for( int i = 0; i < sm_truncateTests.Count; i++ )
            {
                var testCase = sm_truncateTests[ i ];
                try
                {
                    string output = Truncate( testCase.Input,
                                              testCase.MaxApparentLength,
                                              testCase.UseEllipsis,
                                              testCase.TrimLocation );
                    if( 0 != String.CompareOrdinal( output, testCase.ExpectedOutput ) )
                    {
                        truncateFailures++;
                        Console.WriteLine( "Truncate test case {0} failed.\n   Expected: {1}\n     Actual: {2}.",
                                           i,
                                           _EscapeStringForDisplay( testCase.ExpectedOutput ),
                                           _EscapeStringForDisplay( output ) );
                    }
                }
                catch( Exception e )
                {
                    if( e.GetType() != testCase.ExpectedExceptionType )
                    {
                        truncateFailures++;
                        Console.WriteLine( "Truncate Test case {0} failed. Expected exception type: {1}, Actual: {2}.",
                                           i,
                                           null == testCase.ExpectedExceptionType ?
                                                   "<none>" :
                                                   testCase.ExpectedExceptionType.Name,
                                           e.GetType().Name );
                    }
                }
            } // end foreach( testCase )

            if( 0 == truncateFailures )
            {
                Console.WriteLine( "CaStringUtil Truncate tests passed." );
            }
            else
            {
                Console.WriteLine( "CaStringUtil Truncate tests failed ({0} failures).", truncateFailures );
            }

            failures += truncateFailures;

            int stripFailures = 0;
            for( int i = 0; i < sm_stripTests.Count; i++ )
            {
                var testCase = sm_stripTests[ i ];
                try
                {
                    string output = StripControlSequences( testCase.Input );
                    if( 0 != String.CompareOrdinal( output, testCase.ExpectedOutput ) )
                    {
                        stripFailures++;
                        Console.WriteLine( "StripControlSequences test case {0} failed. Expected: {1}, Actual: {2}.",
                                           i,
                                           testCase.ExpectedOutput,
                                           output );
                    }
                }
                catch( Exception e )
                {
                    if( e.GetType() != testCase.ExpectedExceptionType )
                    {
                        stripFailures++;
                        Console.WriteLine( "StripControlSequences Test case {0} failed. Expected exception type: {1}, Actual: {2}.",
                                           i,
                                           null == testCase.ExpectedExceptionType ?
                                                   "<none>" :
                                                   testCase.ExpectedExceptionType.Name,
                                           e.GetType().Name );
                    }
                }
            } // end foreach( testCase )


            if( 0 == stripFailures )
            {
                Console.WriteLine( "CaStringUtil StripControlSequences tests passed." );
            }
            else
            {
                Console.WriteLine( "CaStringUtil StripControlSequences tests failed ({0} failures).", stripFailures );
            }

            failures += stripFailures;


            int indentAndWrapFailures = 0;
            for( int i = 0; i < sm_indentAndWrapTests.Count; i++ )
            {
                var testCase = sm_indentAndWrapTests[ i ];
                try
                {
                    string output = IndentAndWrap( testCase.Input,
                                                   testCase.OutputWidth,
                                                   testCase.Options,
                                                   testCase.Indent,
                                                   testCase.AddtlContinuationIndent );

                    if( 0 != String.CompareOrdinal( output, testCase.ExpectedOutput ) )
                    {
                        indentAndWrapFailures++;
                        Console.WriteLine( "indentAndWrap test case {0} failed.\n   Expected: {1}\n     Actual: {2}\n",
                                           i,
                                           _EscapeStringForDisplay( testCase.ExpectedOutput ),
                                           _EscapeStringForDisplay( output ) );
                    }
                }
                catch( Exception e )
                {
                    if( e.GetType() != testCase.ExpectedExceptionType )
                    {
                        indentAndWrapFailures++;
                        Console.WriteLine( "IndentAndWrap Test case {0} failed. Expected exception type: {1}, Actual: {2}.",
                                           i,
                                           null == testCase.ExpectedExceptionType ?
                                                   "<none>" :
                                                   testCase.ExpectedExceptionType.Name,
                                           e.GetType().Name );
                    }
                }
            } // end foreach( testCase )

            if( 0 == indentAndWrapFailures )
            {
                Console.WriteLine( "CaStringUtil IndentAndWrap tests passed." );
            }
            else
            {
                Console.WriteLine( "CaStringUtil IndentAndWrap tests failed ({0} failures).", indentAndWrapFailures );
            }

            failures += indentAndWrapFailures;


            if( 0 == failures )
            {
                Console.WriteLine( "All CaStringUtil tests passed." );
            }
            else
            {
                Console.WriteLine( "Some CaStringUtil tests failed (total: {0} failures).", failures );
            }
        } // end SelfTest()
#endif
    } // end class CaStringUtil
}

