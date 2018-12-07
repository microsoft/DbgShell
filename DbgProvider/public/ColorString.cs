using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
using System.Text;

namespace MS.Dbg
{
    public interface ISupportColor
    {
        ColorString ToColorString();
    } // end interface ISupportColor


    // Q: Who should check if the host implements color: individual types, the [alternate]
    //    formatting engine, or both?
    //
    // A: The formatting engine (NOT the format view definitions), when it is rendering
    //    ColorString objects. (both alternate and traditional formatting engines; the
    //    traditional formatting engine can use a custom format view to call
    //    ColorString.ToString( bool ))
    //
    // Q: Who should be exposing/returning ColorString objects?
    //
    // A: ColorStrings are a UI thing, so objects that represent structured data should
    //    avoid them, and the "marking up" of structured data into UI display should be
    //    done by formatting definitions as much as possible. However, sometimes such
    //    objects do have properties specifically for UI use, such as
    //    DbgEventArgs.Message; in those cases, ColorStrings make sense.


    //public class ColorString : IComparable, IConvertible, IComparable< string >, IEnumerable< char >, IEnumerable, IEquatable< string >
    /// <summary>
    ///    Represents a string with ISO/IEC 6429 color markup.
    /// </summary>
    /// <remarks>
    ///    Note that this has implicit conversions from and to System.String, as well as
    ///    certain fields to make it "look like" System.String. This is to aid
    ///    script use of ColorString objects; the script is only interested in the
    ///    content, and the color markup is only for display by a capable host.
    /// </remarks>
    public class ColorString : IConvertible, ISupportColor
    {
        private int m_apparentLength;
        private string m_noColorCache;
        private string m_colorCache;
        private bool m_readOnly;

        public static readonly ColorString Empty = new ColorString().MakeReadOnly();


        //
        // Constructors
        //

        public ColorString()
        {
        }

        public ColorString( ConsoleColor foreground )
        {
            AppendPushFg( foreground );
        }

        public ColorString( ColorString other )
        {
            Append( other );
        }

        // This constructor is private, used only by the string -> ColorString implicit conversion
        // because if it is public, the C# compiler will favor it over the FormattableString constructor
        // for interpolated strings
        private ColorString(string content)
        {
            // The content might not be "pure"; it might be pre-rendered colorized text.
            var noColorLength = CaStringUtil.Length( content );
            if( noColorLength == content.Length )
            {
                m_elements.Add( new ContentElement( content ) );
            }
            else
            {
                // TODO: Might be handy to be able to decompose a pre-rendered color
                // string. For now we'll just dump it in.
                m_elements.Add( new ContentElement( content ) );
            }
            m_apparentLength = noColorLength;
        }

        public ColorString(FormattableString content)
        {
            Append(content);
        }


        public ColorString( ConsoleColor foreground, string content )
        {
            AppendPushFg( foreground );
            Append( content );
            AppendPop();
        }

        public ColorString( string content, ConsoleColor background )
        {
            AppendPushBg( background );
            Append( content );
            AppendPop();
        }

        public ColorString( ConsoleColor foreground, ConsoleColor background, string content )
        {
            AppendPushFgBg( foreground, background );
            Append( content );
            AppendPop();
        }

        private string _GatherAllElements(bool withColor)
        {
            StringBuilder sb = new StringBuilder( m_apparentLength + (withColor ? 40 : 1) );
            return AppendTo( sb, withColor ).ToString();
        } // end _GatherAllElements()

        private StringBuilder AppendTo(StringBuilder sb, bool withColor )
        {
            foreach (ColorStringElement cse in m_elements)
            {
                cse.AppendTo(sb, withColor);
            }
            return sb;
        } // end AppendTo


        public override string ToString()
        {
            return ToString( false );
        } // end ToString()


        public string ToString( bool includeColorMarkup )
        {
            if( includeColorMarkup )
            {
                if( null == m_colorCache )
                    m_colorCache = _GatherAllElements( true );

                return m_colorCache;
            }
            else
            {
                if( null == m_noColorCache )
                    m_noColorCache = _GatherAllElements( false );

                return m_noColorCache;
            }
        } // end ToString( bool )


        public static implicit operator String( ColorString cs )
        {
            if( null == cs )
                return null;

            return cs.ToString();
        }

        public static implicit operator ColorString( string s )
        {
            if( null == s )
                return null;

            return new ColorString( s );
        }

        // This conversion is pretty pointless and not actually intended to be used
        // It exists only because otherwise the compiler considers the choice between
        // the string -> ColorString conversion and FormattableString to be ambiguous
        // for interpolated strings.
        public static implicit operator ColorString( FormattableString arg )
        {
            return $"{arg}";
        }

        public bool IsReadOnly { get { return m_readOnly; } }

        public ColorString MakeReadOnly()
        {
            m_readOnly = true;
            return this;
        }

        private void _CheckReadOnly( bool modifyingContent )
        {
            if( m_readOnly )
                throw new InvalidOperationException( "This object is read-only." );

            m_colorCache = null;

            if( modifyingContent )
                m_noColorCache = null;
        }

        private List< ColorStringElement > m_elements = new List< ColorStringElement >();


        public ColorString AppendLine( FormattableString content )
        {
            Append( content );
            return AppendLine();
        }

        public ColorString AppendLine()
        {
            return Append( Environment.NewLine );
        }

        private const int PUSH = 56;
        private const int POP = 57;
        private static readonly int[] PopArray = { POP };

        public ColorString AppendPush()
        {
            _CheckReadOnly( false );
            m_elements.Add( new SgrControlSequence( new int[] { PUSH } ) );
            return this;
        }

        public ColorString AppendPushFg( ConsoleColor foreground )
        {
            _CheckReadOnly( false );
            m_elements.Add( new SgrControlSequence( new int[] { PUSH,
                                                                CaStringUtil.ForegroundColorMap[ foreground ] } ) );
            return this;
        }

        public ColorString AppendPushBg( ConsoleColor background )
        {
            _CheckReadOnly( false );
            m_elements.Add( new SgrControlSequence( new int[] { PUSH,
                                                                CaStringUtil.BackgroundColorMap[ background ] } ) );
            return this;
        }


        public ColorString AppendPushFgBg( ConsoleColor foreground, ConsoleColor background )
        {
            _CheckReadOnly( false );
            m_elements.Add( new SgrControlSequence( new int[] { PUSH,
                                                                CaStringUtil.ForegroundColorMap[ foreground ],
                                                                CaStringUtil.BackgroundColorMap[ background ] } ) );
            return this;
        }

        public ColorString AppendPop()
        {
            _CheckReadOnly( false );
            m_elements.Add( new SgrControlSequence( PopArray ) );
            return this;
        }

        public ColorString AppendPushPopFg( ConsoleColor foreground, string content )
        {
            _CheckReadOnly( true );
            m_elements.Add( new SgrControlSequence( new int[] { PUSH,
                                                                CaStringUtil.ForegroundColorMap[ foreground ] } ) );
            m_elements.Add( new ContentElement( content ) );
            // The content might not be "pure"; it might be pre-rendered colorized text.
            m_apparentLength += CaStringUtil.Length( content );
            m_elements.Add( new SgrControlSequence( PopArray ) );
            return this;
        }

        public ColorString AppendPushPopFgBg( ConsoleColor foreground, ConsoleColor background, string content )
        {
            _CheckReadOnly( true );
            m_elements.Add( new SgrControlSequence( new int[] { PUSH,
                                                                CaStringUtil.ForegroundColorMap[ foreground ],
                                                                CaStringUtil.BackgroundColorMap[ background ] } ) );
            m_elements.Add( new ContentElement( content ) );
            // The content might not be "pure"; it might be pre-rendered colorized text.
            m_apparentLength += CaStringUtil.Length( content );
            m_elements.Add( new SgrControlSequence( PopArray ) );
            return this;
        }

        public ColorString AppendFg( ConsoleColor foreground )
        {
            _CheckReadOnly( false );
            m_elements.Add( new SgrControlSequence( new int[] { CaStringUtil.ForegroundColorMap[ foreground ] } ) );
            return this;
        }

        public ColorString AppendFgBg( ConsoleColor foreground, ConsoleColor background )
        {
            _CheckReadOnly( false );
            m_elements.Add( new SgrControlSequence( new int[] { CaStringUtil.ForegroundColorMap[ foreground ],
                                                                CaStringUtil.BackgroundColorMap[ background ] } ) );
            return this;
        }

        public ColorString AppendBg( ConsoleColor background )
        {
            _CheckReadOnly( false );
            m_elements.Add( new SgrControlSequence( new int[] { CaStringUtil.BackgroundColorMap[ background ] } ) );
            return this;
        }


        public ColorString Append( ColorString other )
        {
            if( null == other )
                return this;

            _CheckReadOnly( true );
            m_elements.AddRange( other.m_elements );
            m_apparentLength += other.m_apparentLength;
            return this;
        }

        public ColorString AppendLine( ColorString other )
        {
            Append( other );
            return AppendLine();
        }


        public ColorString AppendFormat( string format, params object[] args )
        {
            _CheckReadOnly( true );
            m_elements.Add( new FormatStringElement( format, args ) );
            return this;
        }

        public ColorString Append( FormattableString formatString )
        {
            return AppendFormat( formatString.Format, formatString.GetArguments() );
        }

        public static ColorString operator +( ColorString a, ColorString b )
        {
            var cs = new ColorString( a );
            return cs.Append( b );
        }


        /// <summary>
        ///    Creates a new ColorString object if truncation is necessary.
        /// </summary>
        public static ColorString Truncate( ColorString cs, int maxApparentWidth )
        {
            return ColorString.Truncate( cs, maxApparentWidth, true );
        } // end Truncate()


        /// <summary>
        ///    Creates a new ColorString object if truncation is necessary.
        /// </summary>
        public static ColorString Truncate( ColorString cs, int maxApparentWidth, bool useEllipsis )
        {
            if( cs.Length <= maxApparentWidth )
                return cs;

            // Would it be better to go through all the elements?
            return CaStringUtil.Truncate( cs.ToString( true ), maxApparentWidth, useEllipsis );
        } // end Truncate()


        /// <summary>
        ///    Creates a new ColorString object if truncation is necessary.
        /// </summary>
        public static ColorString Truncate( ColorString cs,
                                            int maxApparentWidth,
                                            bool useEllipsis,
                                            TrimLocation trimLocation )
        {
            if( cs.Length <= maxApparentWidth )
                return cs;

            // Would it be better to go through all the elements?
            return CaStringUtil.Truncate( cs.ToString( true ),
                                          maxApparentWidth,
                                          useEllipsis,
                                          trimLocation );
        } // end Truncate()


        /// <summary>
        ///    Creates a new ColorString object if any change is necessary.
        ///    TODO: Perhaps it should /always/ create a new object?
        /// </summary>
        public static ColorString MakeFixedWidth( ColorString cs,
                                                  int maxApparentWidth )
        {
            return MakeFixedWidth( cs, maxApparentWidth, true );
        }

        /// <summary>
        ///    Creates a new ColorString object if any change is necessary.
        ///    TODO: Perhaps it should /always/ create a new object?
        /// </summary>
        public static ColorString MakeFixedWidth( ColorString cs,
                                                  int maxApparentWidth,
                                                  bool useEllipsis )
        {
            return MakeFixedWidth( cs, maxApparentWidth, useEllipsis, Alignment.Left );
        }

        /// <summary>
        ///    Creates a new ColorString object if any change is necessary.
        ///    TODO: Perhaps it should /always/ create a new object?
        /// </summary>
        public static ColorString MakeFixedWidth( ColorString cs,
                                                  int maxApparentWidth,
                                                  Alignment contentAlignment )
        {
            return MakeFixedWidth( cs, maxApparentWidth, true, contentAlignment );
        }

        /// <summary>
        ///    Creates a new ColorString object if any change is necessary.
        ///    TODO: Perhaps it should /always/ create a new object?
        /// </summary>
        public static ColorString MakeFixedWidth( ColorString cs,
                                                  int maxApparentWidth,
                                                  bool useEllipsis,
                                                  Alignment contentAlignment )
        {
            var trimLocation = TrimLocation.Right;
            if( Alignment.Right == contentAlignment )
                trimLocation = TrimLocation.Left;

            return MakeFixedWidth( cs,
                                   maxApparentWidth,
                                   useEllipsis,
                                   contentAlignment,
                                   trimLocation );
        }

        /// <summary>
        ///    Creates a new ColorString object if any change is necessary.
        ///    TODO: Perhaps it should /always/ create a new object?
        /// </summary>
        /// <remarks>
        ///    Note that not all combinations of useEllipsis, contentAlignment, and
        ///    trimLocation are valid.
        /// </remarks>
        public static ColorString MakeFixedWidth( ColorString cs,
                                                  int maxApparentWidth,
                                                  bool useEllipsis,
                                                  Alignment contentAlignment,
                                                  TrimLocation trimLocation )
        {
            if( cs.Length > maxApparentWidth )
            {
                return Truncate( cs, maxApparentWidth, useEllipsis, trimLocation );
            }
            else if( cs.Length == maxApparentWidth )
            {
                return cs;
            }

            int diff = maxApparentWidth - cs.Length;
            switch( contentAlignment )
            {
                case Alignment.Undefined:
                case Alignment.Left:
                    // Pad right:
                    return new ColorString( cs ).Append( new String( ' ', diff ) );

                case Alignment.Right:
                    // Pad left:
                    return new ColorString( new String( ' ', diff ) ).Append( cs );

                case Alignment.Center:
                    int diff2 = diff / 2;
                    diff = diff - diff2;
                    return new ColorString( new String( ' ', diff2 ) )
                                .Append( cs )
                                .Append( new String( ' ', diff ) );

                default:
                    throw new Exception( "Unexpected alignment value." );
            }
        } // end MakeFixedWidth()


        public static string StripPrerenderedColor( string prerendered )
        {
            return CaStringUtil.StripControlSequences( prerendered );
        } // end StripPrerenderedColor()


        private abstract class ColorStringElement
        {
            public abstract StringBuilder AppendTo( StringBuilder sb, bool withColor );
        } // end class ColorStringElement

        [DebuggerDisplay( "ContentElement: {Content}" )]
        private class ContentElement : ColorStringElement
        {
            public readonly string Content;

            public ContentElement( string content )
            {
                Content = content ?? throw new ArgumentNullException( nameof(content) );
            } // end constructor

            public override StringBuilder AppendTo( StringBuilder sb, bool _ )
            {
                return sb.Append( Content );
            } // end AppendTo();
        } // end class ContentElement

        private class SgrControlSequence : ColorStringElement
        {
            public readonly IReadOnlyList< int > Commands;

            public SgrControlSequence( IReadOnlyList< int > commands )
            {
                Commands = commands ?? throw new ArgumentNullException( nameof(commands) );
            } // end constructor

            private const char CSI = '\x9b';  // "Control Sequence Initiator"
            private const char SGR = 'm';     // "Select Graphics Rendition"

            public override StringBuilder AppendTo( StringBuilder sb, bool withColor )
            {
                return withColor ? AppendCommands(sb, Commands) : sb;
            } // end AppendTo();

            public static StringBuilder AppendCommands(StringBuilder sb, IReadOnlyList<int> commands)
            {
                sb.Append( CSI );
                for( int i = 0; i < commands.Count; i++ )
                {
                    sb.Append( commands[ i ].ToString( CultureInfo.InvariantCulture ) );
                    if( i != (commands.Count - 1) )
                        sb.Append( ';' );
                }

                return sb.Append( SGR );
            } // end AppendCommands();

        } // end class SgrControlSequence

        private class FormatStringElement : ColorStringElement
        {
            private readonly string m_formatString;
            private readonly object[] m_args;
            private static readonly ColorStringFormatProvider sm_colorProvider = new ColorStringFormatProvider( true );
            private static readonly ColorStringFormatProvider sm_noColorProvider = new ColorStringFormatProvider( false );

            public FormatStringElement( string formatString, object[] args )
            {
                m_formatString = formatString ?? throw new ArgumentNullException( nameof( formatString ) );
                m_args = args ?? throw new ArgumentNullException( nameof( args ) );
            } // end FormatStringElement()

            public override StringBuilder AppendTo( StringBuilder sb, bool withColor )
            {
                return sb.AppendFormat( withColor ? sm_colorProvider : sm_noColorProvider, m_formatString, m_args );
            } // end AppendTo()

            private static string ArgumentToString( object theArg, string theFormat, bool withColor )
            {
                switch( theArg )
                {
                    case ISupportColor colorSupporter:
                        return colorSupporter.ToColorString().ToString( withColor );
                    case IFormattable formattable:
                        return formattable.ToString( theFormat, null );
                    default:
                        return theArg.ToString();
                }
            } // end ArgumentToString()

            private class ColorStringFormatProvider : IFormatProvider
            {
                private readonly ICustomFormatter m_formatter;

                public ColorStringFormatProvider( bool withColor )
                {
                    m_formatter = withColor ? (ICustomFormatter) new ColorStringFormatter() : new NoColorStringFormatter();
                }

                public object GetFormat( Type formatType )
                {
                    return typeof( ICustomFormatter ).IsAssignableFrom( formatType ) ? m_formatter : null;
                }
            } // end class ColorStringFormatProvider

            private class ColorStringFormatter : ICustomFormatter
            {
                public string Format( string format, object arg, IFormatProvider formatProvider )
                {
                    if( arg == null )
                        return String.Empty;

                    string[] formatPieces = format?.Split( ',' ) ?? new[] { String.Empty };

                    bool hasFg = Enum.TryParse<ConsoleColor>( formatPieces.ElementAtOrDefault( 1 ), out var fgColor );
                    bool hasBg = Enum.TryParse<ConsoleColor>( formatPieces.ElementAtOrDefault( 2 ), out var bgColor );

                    if( hasFg || hasBg )
                    {
                        var sb = new StringBuilder();
                        var commands = new List<int> { PUSH };
                        if( hasFg ) { commands.Add( CaStringUtil.ForegroundColorMap[ fgColor ] ); }
                        if( hasBg ) { commands.Add( CaStringUtil.BackgroundColorMap[ bgColor ] ); }
                        SgrControlSequence.AppendCommands( sb, commands );

                        if( arg is ISupportColor asColor )
                        {
                            asColor.ToColorString().AppendTo( sb, true );
                        }
                        else
                        {
                            sb.Append( ArgumentToString( arg, formatPieces[ 0 ], true ) );
                        }

                        SgrControlSequence.AppendCommands( sb, PopArray );
                        return sb.ToString();
                    }

                    return ArgumentToString( arg, formatPieces[ 0 ], true );
                } // end Format()
            } // end class ColorStringFormatter

            private class NoColorStringFormatter : ICustomFormatter
            {
                public string Format( string format, object arg, IFormatProvider formatProvider )
                {
                    if( arg == null )
                        return String.Empty;

                    var formatPieces = format?.Split( ',' ) ?? new[] { String.Empty };

                    return ArgumentToString( arg, formatPieces[ 0 ], false );
                }
            } // end class NoColorStringFormatter
        }  // end class FormatStringElement


#region String stuff

        public char this[ int index ]
        {
            get { return this.ToString()[ index ]; }
        }

        public int Length
        {
            get { return m_apparentLength; }
        }

     // public override bool Equals( object obj )
     // {
     //     return this.ToString().Equals( obj );
     // }

     // public bool Equals( string value )
     // {
     //     return this.ToString().Equals( value );
     // }

     // public bool Equals( string value, StringComparison comparisonType )
     // {
     //     return this.ToString().Equals( value, comparisonType );
     // }

     // public static bool Equals( ColorString a, ColorString b )
     // {
     //     return String.Equals( a.ToString(), b.ToString() );
     // }

     // public static bool operator ==( ColorString a, ColorString b )
     // {
     //     if( null == a )
     //     {
     //         return b == null;
     //     }
     //     else
     //     {
     //         return a.ToString() == b.ToString();
     //     }
     // }

     // public static bool operator !=( ColorString a, ColorString b )
     // {
     //     if( null == a )
     //     {
     //         return b != null;
     //     }
     //     else
     //     {
     //         return a.ToString() != b.ToString();
     //     }
     // }

     // public void CopyTo( int sourceIndex, char[] destination, int destinationIndex, int count )
     // {
     //     this.ToString().CopyTo( sourceIndex, destination, destinationIndex, count );
     // }

     // public char[] ToCharArray()
     // {
     //     return this.ToString().ToCharArray();
     // }

     // public char[] ToCharArray( int startIndex, int length )
     // {
     //     return this.ToString().ToCharArray( startIndex, length );
     // }

     // public override int GetHashCode()
     // {
     //     return this.ToString().GetHashCode();
     // }

        public string[] Split( params char[] separator )
        {
            return this.ToString().Split( separator );
        }

        public string[] Split( char[] separator, int count )
        {
            return this.ToString().Split( separator, count );
        }

        public string[] Split( char[] separator, StringSplitOptions options )
        {
            return this.ToString().Split( separator, options );
        }

        public string[] Split( char[] separator, int count, StringSplitOptions options )
        {
            return this.ToString().Split( separator, count, options );
        }

        public string[] Split( string[] separator, StringSplitOptions options )
        {
            return this.ToString().Split( separator, options );
        }

        public string[] Split( string[] separator, int count, StringSplitOptions options )
        {
            return this.ToString().Split( separator, count, options );
        }

        public string Substring( int startIndex )
        {
            return this.ToString().Substring( startIndex );
        }

        public string Substring( int startIndex, int length )
        {
            return this.ToString().Substring( startIndex, length );
        }

        public string Trim( params char[] trimChars )
        {
            return this.ToString().Trim( trimChars );
        }

        public string TrimStart( params char[] trimChars )
        {
            return this.ToString().TrimStart( trimChars );
        }

        public string TrimEnd( params char[] trimChars )
        {
            return this.ToString().TrimEnd( trimChars );
        }

     // public bool IsNormalized()
     // {
     //     return this.ToString().IsNormalized();
     // }

     // public bool IsNormalized( NormalizationForm normalizationForm )
     // {
     //     return this.ToString().IsNormalized( normalizationForm );
     // }

     // public string Normalize()
     // {
     //     return this.ToString().Normalize();
     // }

     // public string Normalize( NormalizationForm normalizationForm )
     // {
     //     return this.ToString().Normalize( normalizationForm );
     // }

     // public int CompareTo( object value )
     // {
     //     return this.ToString().CompareTo( value );
     // }

     // public int CompareTo( string strB )
     // {
     //     return this.ToString().CompareTo( strB );
     // }

        public bool Contains( string value )
        {
            return this.ToString().Contains( value );
        }

        public bool EndsWith( string value )
        {
            return this.ToString().EndsWith( value );
        }

        public bool EndsWith( string value, StringComparison comparisonType )
        {
            return this.ToString().EndsWith( value, comparisonType );
        }

        public bool EndsWith( string value, bool ignoreCase, CultureInfo culture )
        {
            return this.ToString().EndsWith( value, ignoreCase, culture );
        }

        public int IndexOf( char value )
        {
            return this.ToString().IndexOf( value );
        }

        public int IndexOf( char value, int startIndex )
        {
            return this.ToString().IndexOf( value, startIndex );
        }

        public int IndexOf( char value, int startIndex, int count )
        {
            return this.ToString().IndexOf( value, startIndex, count );
        }

        public int IndexOfAny( char[] anyOf )
        {
            return this.ToString().IndexOfAny( anyOf );
        }

        public int IndexOfAny( char[] anyOf, int startIndex )
        {
            return this.ToString().IndexOfAny( anyOf, startIndex );
        }

        public int IndexOfAny( char[] anyOf, int startIndex, int count )
        {
            return this.ToString().IndexOfAny( anyOf, startIndex, count );
        }

        public int IndexOf( string value )
        {
            return this.ToString().IndexOf( value );
        }

        public int IndexOf( string value, int startIndex )
        {
            return this.ToString().IndexOf( value, startIndex );
        }

        public int IndexOf( string value, int startIndex, int count )
        {
            return this.ToString().IndexOf( value, startIndex, count );
        }

        public int IndexOf( string value, StringComparison comparisonType )
        {
            return this.ToString().IndexOf( value, comparisonType );
        }

        public int IndexOf( string value, int startIndex, StringComparison comparisonType )
        {
            return this.ToString().IndexOf( value, startIndex, comparisonType );
        }

        public int IndexOf( string value, int startIndex, int count, StringComparison comparisonType )
        {
            return this.ToString().IndexOf( value, startIndex, count, comparisonType );
        }

        public int LastIndexOf( char value )
        {
            return this.ToString().LastIndexOf( value );
        }

        public int LastIndexOf( char value, int startIndex )
        {
            return this.ToString().LastIndexOf( value, startIndex );
        }

        public int LastIndexOf( char value, int startIndex, int count )
        {
            return this.ToString().LastIndexOf( value, startIndex, count );
        }

        public int LastIndexOfAny( char[] anyOf )
        {
            return this.ToString().LastIndexOfAny( anyOf );
        }

        public int LastIndexOfAny( char[] anyOf, int startIndex )
        {
            return this.ToString().LastIndexOfAny( anyOf, startIndex );
        }

        public int LastIndexOfAny( char[] anyOf, int startIndex, int count )
        {
            return this.ToString().LastIndexOfAny( anyOf, startIndex, count );
        }

        public int LastIndexOf( string value )
        {
            return this.ToString().LastIndexOf( value );
        }

        public int LastIndexOf( string value, int startIndex )
        {
            return this.ToString().LastIndexOf( value, startIndex );
        }

        public int LastIndexOf( string value, int startIndex, int count )
        {
            return this.ToString().LastIndexOf( value, startIndex, count );
        }

        public int LastIndexOf( string value, StringComparison comparisonType )
        {
            return this.ToString().LastIndexOf( value, comparisonType );
        }

        public int LastIndexOf( string value, int startIndex, StringComparison comparisonType )
        {
            return this.ToString().LastIndexOf( value, startIndex, comparisonType );
        }

        public int LastIndexOf( string value, int startIndex, int count, StringComparison comparisonType )
        {
            return this.ToString().LastIndexOf( value, startIndex, count, comparisonType );
        }

     // public string PadLeft( int totalWidth )
     // {
     //     return this.ToString().PadLeft( totalWidth );
     // }

     // public string PadLeft( int totalWidth, char paddingChar )
     // {
     //     return this.ToString().PadLeft( totalWidth, paddingChar );
     // }

     // public string PadRight( int totalWidth )
     // {
     //     return this.ToString().PadRight( totalWidth );
     // }

     // public string PadRight( int totalWidth, char paddingChar )
     // {
     //     return this.ToString().PadRight( totalWidth, paddingChar );
     // }

        public bool StartsWith( string value )
        {
            return this.ToString().StartsWith( value );
        }

        public bool StartsWith( string value, StringComparison comparisonType )
        {
            return this.ToString().StartsWith( value, comparisonType );
        }

        public bool StartsWith( string value, bool ignoreCase, CultureInfo culture )
        {
            return this.ToString().StartsWith( value, ignoreCase, culture );
        }

        public string ToLower()
        {
            return this.ToString().ToLower();
        }

        public string ToLower( CultureInfo culture )
        {
            return this.ToString().ToLower( culture );
        }

        public string ToLowerInvariant()
        {
            return this.ToString().ToLowerInvariant();
        }

        public string ToUpper()
        {
            return this.ToString().ToUpper();
        }

        public string ToUpper( CultureInfo culture )
        {
            return this.ToString().ToUpper( culture );
        }

        public string ToUpperInvariant()
        {
            return this.ToString().ToUpperInvariant();
        }

        public string Trim()
        {
            return this.ToString().Trim();
        }

     // public string Insert( int startIndex, string value )
     // {
     //     return this.ToString().Insert( startIndex, value );
     // }

        public string Replace( char oldChar, char newChar )
        {
            return this.ToString().Replace( oldChar, newChar );
        }

        public string Replace( string oldValue, string newValue )
        {
            return this.ToString().Replace( oldValue, newValue );
        }

        public string Remove( int startIndex, int count )
        {
            return this.ToString().Remove( startIndex, count );
        }

        public string Remove( int startIndex )
        {
            return this.ToString().Remove( startIndex );
        }

    /// public static string Format( string format, object arg0 )
    /// {
    ///     return this.ToString().string Format( format, arg0 );
    /// }

    /// public static string Format( string format, object arg0, object arg1 )
    /// {
    ///     return this.ToString().string Format( format, arg0, arg1 );
    /// }

    /// public static string Format( string format, object arg0, object arg1, object arg2 )
    /// {
    ///     return this.ToString().string Format( format, arg0, arg1, arg2 );
    /// }

    /// public static string Format( string format, params object[] args )
    /// {
    ///     return this.ToString().string Format( format, args );
    /// }

    /// public static string Format( IFormatProvider provider, string format, params object[] args )
    /// {
    ///     return this.ToString().string Format( provider, format, args );
    /// }

        public TypeCode GetTypeCode()
        {
            return this.ToString().GetTypeCode();
        }

        bool IConvertible.ToBoolean( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToBoolean( provider );
        }

        char IConvertible.ToChar( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToChar( provider );
        }

        sbyte IConvertible.ToSByte( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToSByte( provider );
        }

        byte IConvertible.ToByte( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToByte( provider );
        }

        short IConvertible.ToInt16( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToInt16( provider );
        }

        ushort IConvertible.ToUInt16( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToUInt16( provider );
        }

        int IConvertible.ToInt32( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToInt32( provider );
        }

        uint IConvertible.ToUInt32( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToUInt32( provider );
        }

        long IConvertible.ToInt64( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToInt64( provider );
        }

        ulong IConvertible.ToUInt64( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToUInt64( provider );
        }

        float IConvertible.ToSingle( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToSingle( provider );
        }

        double IConvertible.ToDouble( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToDouble( provider );
        }

        decimal IConvertible.ToDecimal( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToDecimal( provider );
        }

        DateTime IConvertible.ToDateTime( IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToDateTime( provider );
        }

        object IConvertible.ToType( Type type, IFormatProvider provider )
        {
            return ((IConvertible) this.ToString()).ToType( type, provider );
        }

        string IConvertible.ToString( IFormatProvider provider )
        {
            return this.ToString();
        }

     // public CharEnumerator GetEnumerator()
     // {
     //     return this.ToString().GetEnumerator();
     // }

     // IEnumerator<char> IEnumerable<char>.GetEnumerator()
     // {
     //     return ((IEnumerable<char>) this.ToString()).GetEnumerator();
     // }

     // IEnumerator IEnumerable.GetEnumerator()
     // {
     //     return ((IEnumerable) this.ToString()).GetEnumerator();
     // }

#endregion


        ColorString ISupportColor.ToColorString()
        {
            return this;
        }
    } // end class ColorString
}

