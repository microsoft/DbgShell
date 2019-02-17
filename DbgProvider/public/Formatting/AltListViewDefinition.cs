using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace MS.Dbg.Formatting
{

    public abstract class ListItem
    {
        // TODO: Should I colorize the labels?
        public abstract string Label { get; }
    } // end class ListItem


    public class PropertyListItem : ListItem, IEquatable< PropertyListItem >
    {
        public readonly string PropertyName;
        public readonly ColorString FormatString;

        private string m_label;
        public override string Label
        {
            get
            {
                return String.IsNullOrEmpty( m_label ) ? PropertyName : m_label;
            }
        }

        public PropertyListItem( string propertyName )
            : this( propertyName, null, null )
        {
        }

        public PropertyListItem( string propertyName, ColorString formatString )
            : this( propertyName, formatString, null )
        {
        }

        public PropertyListItem( string propertyName, ColorString formatString, string label )
        {
            if( String.IsNullOrEmpty( propertyName ) )
                throw new ArgumentException( "You must supply a property name.", "propertyName" );

            PropertyName = propertyName;
            FormatString = formatString;
            m_label = label;
        } // end constructor


        //
        // IEquatable-related stuff
        //

        public bool Equals( PropertyListItem other )
        {
            if( other == null )
                return false;

            return (PropertyName == other.PropertyName) &&
                   (FormatString?.ToString( true ) == other.FormatString?.ToString( true )) &&
                   (m_label == other.m_label);
        }

        public override bool Equals( object obj )
        {
            if( obj is PropertyListItem pli )
            {
                return Equals( pli );
            }
            return false;
        }

        public override int GetHashCode()
        {
            int hash = PropertyName.GetHashCode();
            if( null != FormatString )
                hash += FormatString.ToString( includeColorMarkup: true ).GetHashCode();

            if( null != m_label )
                hash ^= m_label.GetHashCode();

            return hash;
        }

        public static bool operator==( PropertyListItem pli1, PropertyListItem pli2 )
        {
            if( ReferenceEquals( pli1, null ) )
            {
                return ReferenceEquals( pli2, null );
            }
            else
            {
                return pli1.Equals( pli2 );
            }
        }

        public static bool operator!=( PropertyListItem pli1, PropertyListItem pli2 )
        {
            return !(pli1 == pli2);
        }
    } // end class PropertyListItem


    public class ScriptListItem : ListItem//, IEquatable< ScriptListItem >
    {
        private string m_label;
        public override string Label { get { return m_label; } }
        public ScriptBlock Script { get; private set; }

        public ScriptListItem( string label, ScriptBlock script )
        {
            if( String.IsNullOrEmpty( label ) )
                throw new ArgumentException( "You must supply a label.", nameof(label) );

            m_label = label;
            Script = script ?? throw new ArgumentNullException( nameof(script) );
        } // end constructor


        /// <summary>
        ///    For use by -Property machinery; just compares the Label and Script.
        /// </summary>
        public bool LooksSimilar( ScriptListItem other )
        {
            if( other == null )
                return false;

            return (Script.ToString() == other.Script.ToString()) &&
                   (m_label == other.m_label);
        } // end LooksSimilar()


        //
        // In the code below, the Context is not taken into account, so I'm not going to
        // add it until we manage to get rid of the Context.
        //
     // public bool Equals( ScriptListItem other )
     // {
     //     if( other == null )
     //         return false;

     //     return (m_label == other.m_label) &&
     //            (Script.ToString() == other.Script.ToString());
     // }

     // public override bool Equals( object obj )
     // {
     //     if( obj is ScriptListItem pli )
     //     {
     //         return Equals( pli );
     //     }
     //     return false;
     // }

     // public override int GetHashCode()
     // {
     //     return m_label.GetHashCode() ^ Script.ToString().GetHashCode();
     // }

     // public static bool operator==( ScriptListItem sli1, ScriptListItem sli2 )
     // {
     //     if( null == sli1 )
     //     {
     //         return (null == sli2);
     //     }
     //     else
     //     {
     //         return sli1.Equals( sli2 );
     //     }
     // }

     // public static bool operator!=( ScriptListItem sli1, ScriptListItem sli2 )
     // {
     //     return !(sli1 == sli2);
     // }
    } // end class ScriptListItem


    public class AltListViewDefinition : IFormatInfo
    {
        string IFormatInfo.FormatCommand { get { return "Format-AltList"; } }
        string IFormatInfo.Module { get { return "Debugger"; } }

        public ScriptBlock ProduceGroupByHeader { get; private set; }
        public object GroupBy { get; private set; }

        public IReadOnlyList< ListItem > ListItems { get; private set; }
        
        internal readonly bool PreserveHeaderContext;

        public AltListViewDefinition( IReadOnlyList< ListItem > listItems )
            : this( listItems, null, null )
        {
        }

        public AltListViewDefinition( IReadOnlyList< ListItem > listItems,
                                      ScriptBlock produceGroupByHeader,
                                      object groupBy )
            : this( listItems, produceGroupByHeader, groupBy, false )
        {
        }

        public AltListViewDefinition( IReadOnlyList< ListItem > listItems,
                                      ScriptBlock produceGroupByHeader,
                                      object groupBy,
                                      bool preserveHeaderContext )
        {
            ListItems = listItems ?? throw new ArgumentNullException( nameof(listItems) );
            ProduceGroupByHeader = produceGroupByHeader;
            GroupBy = groupBy;
            PreserveHeaderContext = preserveHeaderContext;
        } // end constructor


        private int m_maxLabelLength;

        internal int MaxLabelLength
        {
            get
            {
                if( 0 == m_maxLabelLength )
                {
                    m_maxLabelLength = ListItems.Max( (li) => CaStringUtil.Length( li.Label ) );
                }
                return m_maxLabelLength;
            }
        }


        /// <summary>
        ///    Not a true, complete comparison; just enough to handle the needs of dealing
        ///    with calculated properties.
        /// </summary>
        public bool LooksLikeExistingFromPropertyDefinition( IFormatInfo otherBase )
        {
            if( !(otherBase is AltListViewDefinition other) )
                return false;

            if( ListItems.Count != other.ListItems.Count )
                return false;

            for( int i = 0; i < ListItems.Count; i++ )
            {
                if( ListItems[ i ] is PropertyListItem pli1 )
                {
                    if( other.ListItems[ i ] is PropertyListItem pli2 )
                    {
                        if( pli1 != pli2 )
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
                else if( ListItems[ i ] is ScriptListItem sli1 )
                {
                    if( other.ListItems[ i ] is ScriptListItem sli2 )
                    {
                        if( !sli1.LooksSimilar( sli2 ) )
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
    } // end class AltListViewDefinition
}

