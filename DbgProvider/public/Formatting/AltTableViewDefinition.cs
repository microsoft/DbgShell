using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Linq;

namespace MS.Dbg.Formatting
{
    // TODO: replace with S.M.A.Alignment?
    public enum ColumnAlignment
    {
        Default = 0,
        Left,
        Center,
        Right
    }

    public abstract class Column
    {
        public abstract string Label { get; }
        public ColumnAlignment Alignment { get; private set; }
        public int Width { get; internal set; }
        public string Tag { get; private set; }
        // Answers the question "if the content is to big to fit, do you want to have
        // "lorem..." (default) or "...ipsum" (TrimLocation.Left) or "lore...psum"
        // (TrimLocation.Center)?"
        public TrimLocation TrimLocation { get; private set; }

        private int m_calculatedWidth;
        internal int CalculatedWidth
        {
            get { return m_calculatedWidth == 0 ? Width : m_calculatedWidth; }
            set { m_calculatedWidth = value; }
        }

        private ColumnAlignment m_calculatedAlignment;
        internal ColumnAlignment CalculatedAlignment
        {
            get { return m_calculatedAlignment == ColumnAlignment.Default ? Alignment : m_calculatedAlignment; }
            set { m_calculatedAlignment = value; }
        }

        protected Column( ColumnAlignment alignment, int width, string tag, TrimLocation trimLocation )
        {
            // We used to make sure that your specified column width was big enough to
            // handle truncation. But that precluded having very narrow colums, which is
            // occasionally handy for squishing in lots of small, flag-type data.
            //
            // The trick is that if you pass a width of less than 4, you'd better be sure
            // that your data won't ever get truncated, because the truncation code will
            // have a fit if it can't indicate truncation by sticking an ellipsis onto at
            // least one character.
            //
            // If we ever want to go back to enforcing column width of at least 4, then
            // the workaround for narrow columns would be to combine columns (the header
            // would be the original column headers separated by a space, and manually
            // format the column value to include both "virtual" columns).
         // if( (width < 4) && (width > 0) )
         //     throw new ArgumentOutOfRangeException( "width" );

            Alignment = alignment;
            Width = width;
            Tag = tag;
            TrimLocation = trimLocation;
        } // end constructor
    } // end class Column


    public class PropertyColumn : Column
    {
        private string m_label;
        public override string Label
        {
            get
            {
                return String.IsNullOrEmpty( m_label ) ? PropertyName : m_label;
            }
        }

        public string PropertyName { get; private set; }
        public ColorString FormatString { get; private set; }

        public PropertyColumn( string propertyName, ColumnAlignment alignment, int width )
            : this( propertyName, null, alignment, width )
        {
        }

        public PropertyColumn( string propertyName,
                               ColorString formatString,
                               ColumnAlignment alignment,
                               int width )
            : this( propertyName, formatString, null, alignment, width )
        {
        }

        public PropertyColumn( string propertyName,
                               ColorString formatString,
                               string label,
                               ColumnAlignment alignment,
                               int width )
            : this( propertyName, formatString, label, alignment, width, null )
        {
        }

        public PropertyColumn( string propertyName,
                               ColorString formatString,
                               string label,
                               ColumnAlignment alignment,
                               int width,
                               string tag )
            : this( propertyName, formatString, label, alignment, width, tag, TrimLocation.Right )
        {
        }

        public PropertyColumn( string propertyName,
                               ColorString formatString,
                               string label,
                               ColumnAlignment alignment,
                               int width,
                               string tag,
                               TrimLocation trimLocation )
            : base( alignment, width, tag, trimLocation )
        {
            if( String.IsNullOrEmpty( propertyName ) )
                throw new ArgumentException( "You must supply a property name.", "propertyname" );

            PropertyName = propertyName;
            FormatString = formatString;
            m_label = label;
        } // end constructor


        /// <summary>
        ///    For use by -Property machinery; just compares PropertyName, Label, and
        ///    FormatString.
        /// </summary>
        public bool LooksSimilar( PropertyColumn other )
        {
            if( other == null )
                return false;

            return (PropertyName == other.PropertyName) &&
                   (FormatString?.ToString( true ) == other.FormatString?.ToString( true )) &&
                   (m_label == other.m_label);
        } // end LooksSimilar()
    } // end class PropertyColumn


    public class ScriptColumn : Column
    {
        public override string Label { get; }
        public ScriptBlock Script { get; }

        public ScriptColumn( string label,
                             ScriptBlock script,
                             ColumnAlignment alignment,
                             int width )
            : this( label, script, alignment, width, null )
        {
        }

        public ScriptColumn( string label,
                             ScriptBlock script,
                             ColumnAlignment alignment,
                             int width,
                             string tag )
            : this( label, script, alignment, width, tag, TrimLocation.Right )
        {
        }

        public ScriptColumn( string label,
                             ScriptBlock script,
                             ColumnAlignment alignment,
                             int width,
                             string tag,
                             TrimLocation trimLocation )
            : base( alignment, width, tag, trimLocation )
        {
            if( String.IsNullOrEmpty( label ) )
                throw new ArgumentException( "You must supply a column label.", nameof(label) );

            Label = label;
            Script = script ?? throw new ArgumentNullException( nameof(script) );
        } // end constructor

        /// <summary>
        ///    For use by -Property machinery; just compares the Label and Script.
        /// </summary>
        public bool LooksSimilar( ScriptColumn other )
        {
            if( other == null )
                return false;

            return (Script.ToString() == other.Script.ToString()) &&
                   (Label == other.Label);
        } // end LooksSimilar()
    } // end class ScriptColumn


    public class Footer
    {
        public readonly ColumnAlignment Alignment;
        public readonly ScriptBlock Script;

        public Footer( ColumnAlignment alignment, ScriptBlock script )
        {
            Alignment = alignment;
            Script = script ?? throw new ArgumentNullException( nameof(script) );
        }
    } // end class Footer


    public class AltTableViewDefinition : IFormatInfo
    {
        string IFormatInfo.FormatCommand { get { return "Format-AltTable"; } }
        string IFormatInfo.Module { get { return "Debugger"; } }

        public bool ShowIndex { get; private set; }
        public IReadOnlyList< Column > Columns { get { return m_roColumns; } }
        public readonly Footer Footer;

        public ScriptBlock ProduceGroupByHeader { get; private set; }
        internal readonly bool PreserveHeaderContext;
        public object GroupBy { get; private set; }

        private int m_accountedWidth;
        private Column[] m_columns;
        private IReadOnlyList< Column > m_roColumns;
        private List< Column > m_autoSizeColumns;
        private int m_lastUsedBufferWidth;

        private void _ResetWidthCalculations()
        {
            m_lastUsedBufferWidth = 0;
            m_accountedWidth = 0;
            m_autoSizeColumns = null;
            foreach( var c in Columns )
            {
                c.CalculatedWidth = 0;
            }
        } // end _ResetWidthCalculations()


        private void _AnalyzeColumns()
        {
            m_autoSizeColumns = new List< Column >();
            foreach( var c in Columns )
            {
                if( c.Width > 0 )
                    m_accountedWidth += c.Width;
                else
                    m_autoSizeColumns.Add( c );
            }
        } // end _AnalyzeColumns()

        internal int AccountedWidth
        {
            get
            {
                if( null == m_autoSizeColumns )
                    _AnalyzeColumns();

                return m_accountedWidth;
            }
        } // end property AccountedWidth

        internal IReadOnlyList< Column > AutoSizeColumns
        {
            get
            {
                if( null == m_autoSizeColumns )
                    _AnalyzeColumns();

                return m_autoSizeColumns;
            }
        } // end property AutoSizeColumns


        internal void CalculateWidthsAndAlignments( int bufferWidth, PSObject exampleObj )
        {
            if( bufferWidth <= 0 )
                throw new ArgumentOutOfRangeException( "bufferWidth" );

            if( m_lastUsedBufferWidth == bufferWidth )
                return;

            int availableWidth = bufferWidth - AccountedWidth - 1; // -1 for newline which takes a column

            if( availableWidth < (4 * AutoSizeColumns.Count) )
            {
                // TODO: proper error. Or... what?
                throw new ArgumentException( "View is too wide." );
            }

            m_lastUsedBufferWidth = bufferWidth;

            if( AutoSizeColumns.Count > 0 )
            {
                int totalSeparatorWidth = Columns.Count - 1;
                availableWidth -= totalSeparatorWidth;
                // Note that availableWidth (and therefore perCol) could be negative.
                int perCol = availableWidth / AutoSizeColumns.Count;
                int remainder = availableWidth - (perCol * AutoSizeColumns.Count);
                foreach( var c in AutoSizeColumns )
                {
                    int fieldWidth = 0;
                    // TODO: Maybe it would be better to just get the values for the first
                    // line of the table and use that...
                    if( c is PropertyColumn )
                    {
                        // This returns 0 if it doesn't know.
                        try
                        {
                            fieldWidth = GetWidthForProperty( exampleObj, c.Label );
                        }
                        catch( RuntimeException rte )
                        {
                            LogManager.Trace( "Cannot attempt to determine width of property {0} on object of type {1}: {2}",
                                              c.Label,
                                              Util.GetGenericTypeName( exampleObj.BaseObject ),
                                              Util.GetExceptionMessages( rte ) );
                        }
                        if( 0 == fieldWidth )
                        {
                            fieldWidth = perCol;
                            if( remainder > 0 )
                            {
                                remainder--;
                                fieldWidth++;
                            }
                        }
                    }
                    else
                    {
                        fieldWidth = perCol;
                        if( remainder > 0 )
                        {
                            remainder--;
                            fieldWidth++;
                        }
                    }

                    c.CalculatedWidth = Math.Max( CaStringUtil.Length( c.Label ), fieldWidth );
                }
            } // end if( we have auto-size columns )

            for( int colIdx = 0; colIdx < Columns.Count; colIdx++ )
            {
                var c = Columns[ colIdx ];
                if( ColumnAlignment.Default == c.Alignment )
                {
                    if( 0 == colIdx )
                        c.CalculatedAlignment = ColumnAlignment.Right;
                    else if( (Columns.Count - 1) == colIdx )
                        c.CalculatedAlignment = ColumnAlignment.Left;
                    else
                        c.CalculatedAlignment = ColumnAlignment.Center;
                }
            }
        } // end CalculateWidthsAndAlignments()


        /// <summary>
        ///    Checks the type of the specified property and returns the column
        ///    width for certain known types. Can throw whatever accessing the
        ///    property might throw. Returns 0 if we don't know how wide the
        ///    column should be.
        /// </summary>
        internal static int GetWidthForProperty( PSObject obj, string fieldOrPropertyName )
        {
            Util.Assert( null != obj );

            var prop = obj.Properties[ fieldOrPropertyName ];
            if( null == prop )
                return 0; // We'll let the error show up in the rows, so you can at least get partial output (for columns that aren't bogus).

            object fieldOrPropVal = prop.Value;
            if( null == fieldOrPropVal )
                return 0;

            Type t = fieldOrPropVal.GetType();
            if( typeof( int ) == t )
            {
                return 11;
            }
            else if( typeof( uint ) == t )
            {
                return 8;
            }
            else if( typeof( long ) == t )
            {
                return 20;
            }
            else if( typeof( ulong ) == t )
            {
                return 16;
            }
            else if( (typeof( byte ) == t) || (typeof( sbyte ) == t) )
            {
                return 4;
            }
            else if( typeof( short ) == t )
            {
                return 6;
            }
            else if( typeof( ushort ) == t )
            {
                return 4;
            }
            else if( typeof( Guid ) == t )
            {
                return 36;
            }

            return 0;
        } // end _GetWidthForProperty()


        public void SetColumnWidth( int colIdx, int newWidth )
        {
            Column c = Columns[ colIdx ];
            if( (newWidth == c.Width) || (newWidth == c.CalculatedWidth) )
                return; // no change

            c.Width = newWidth;
            _ResetWidthCalculations();
        } // end SetColumnWidth()


        public AltTableViewDefinition WithGroupBy( object groupBy,
                                                   ScriptBlock produceGroupByHeader )
        {
            return new AltTableViewDefinition( m_columns,
                                               Footer,
                                               produceGroupByHeader,
                                               groupBy,
                                               PreserveHeaderContext );
        }


        public AltTableViewDefinition( bool showIndex, IReadOnlyList< Column > columns )
            : this( showIndex, columns, null )
        {
        }

        public AltTableViewDefinition( bool showIndex,
                                       IReadOnlyList< Column > columns,
                                       Footer footer )
            : this( showIndex, columns, footer, null, null )
        {
        }

        public AltTableViewDefinition( bool showIndex,
                                       IReadOnlyList< Column > columns,
                                       Footer footer,
                                       ScriptBlock produceGroupByHeader,
                                       object groupBy )
            : this( showIndex, columns, footer, produceGroupByHeader, groupBy, false )
        {
        }


        private static void _ValidateColumns( IReadOnlyList< Column > columns )
        {
            if( null == columns )
                throw new ArgumentNullException( "columns" );

            for( int i = 0; i < columns.Count; i++ )
            {
                if( null == columns[ i ] )
                    throw new ArgumentException( Util.Sprintf( "Column at index {0} is null.", i ) );
            }
        } // end _ValidateColumns()


        private static Column[] _ProduceColumns( bool showIndex,
                                                 IReadOnlyList< Column > columns )
        {
            _ValidateColumns( columns );

            if( showIndex )
            {
                Column[] withIndex = new Column[ columns.Count + 1 ];
                withIndex[ 0 ] = new ScriptColumn( "Index",
                                                   ScriptBlock.Create( "$PipeOutputIndex.ToString()" ),
                                                   ColumnAlignment.Right,
                                                   5 );
                for( int i = 0; i < columns.Count; i++ )
                {
                    withIndex[ i + 1 ] = columns[ i ];
                }
                return withIndex;
            }
            else
            {
                return columns.ToArray();
            }
        }


        public AltTableViewDefinition( bool showIndex,
                                       IReadOnlyList< Column > columns,
                                       Footer footer,
                                       ScriptBlock produceGroupByHeader,
                                       object groupBy,
                                       bool preserveHeaderContext )
            : this( _ProduceColumns( showIndex, columns ),
                    footer,
                    produceGroupByHeader,
                    groupBy,
                    preserveHeaderContext )
        {
        } // end constructor


        /// <summary>
        ///    Pure assignment constructor--does not copy columns.
        /// </summary>
        internal AltTableViewDefinition( Column[] columns,
                                         Footer footer,
                                         ScriptBlock produceGroupByHeader,
                                         object groupBy,
                                         bool preserveHeaderContext )
        {
            m_columns = columns;
            m_roColumns = new ReadOnlyCollection< Column >( m_columns );
            Footer = footer;
            ProduceGroupByHeader = produceGroupByHeader;
            GroupBy = groupBy;
            PreserveHeaderContext = preserveHeaderContext;
        } // end constructor


        /// <summary>
        ///    Not a true, complete comparison; just enough to handle the needs of dealing
        ///    with calculated properties.
        /// </summary>
        public bool LooksLikeExistingFromPropertyDefinition( IFormatInfo otherBase )
        {
            if( !(otherBase is AltTableViewDefinition other) )
                return false;

            if( m_columns.Length != other.m_columns.Length )
                return false;

            for( int i = 0; i < m_columns.Length; i++ )
            {
                if( m_columns[ i ] is PropertyColumn pc1 )
                {
                    if( other.m_columns[ i ] is PropertyColumn pc2 )
                    {
                        if( !pc1.LooksSimilar( pc2 ) )
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
                else if( m_columns[ i ] is ScriptColumn sc1 )
                {
                    if( other.m_columns[ i ] is ScriptColumn sc2 )
                    {
                        if( !sc1.LooksSimilar( sc2 ) )
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            // For the purposes of -Property stuff, we shouldn't need to care about other
            // stuff. If any of these fire... somebody is using this method that probably
            // shouldn't.
            Util.Assert( null == GroupBy );
            Util.Assert( null == ProduceGroupByHeader );

            return true;
        } // end LooksLikeExistingFromPropertyDefinition()
    } // end class AltTableViewDefinition
}

