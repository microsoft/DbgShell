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


    public class PropertyListItem : ListItem
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
    } // end class PropertyListItem


    public class ScriptListItem : ListItem
    {
        private string m_label;
        public override string Label { get { return m_label; } }
        public ScriptBlock Script { get; private set; }

        internal readonly PsContext Context;

        public ScriptListItem( string label, ScriptBlock script, bool captureContext )
        {
            if( String.IsNullOrEmpty( label ) )
                throw new ArgumentException( "You must supply a label.", "label" );

            if( null == script )
                throw new ArgumentNullException( "script" );

            m_label = label;
            Script = script;
            if( captureContext )
                Context = DbgProvider.CapturePsContext();
            else
                Context = new PsContext();
        } // end constructor
    } // end class ScriptListItem


    public class AltListViewDefinition : IFormatInfo
    {
        string IFormatInfo.FormatCommand { get { return "Format-AltList"; } }
        string IFormatInfo.Module { get { return "Debugger"; } }

        public ScriptBlock ProduceGroupByHeader { get; private set; }
        public object GroupBy { get; private set; }

        public IReadOnlyList< ListItem > ListItems { get; private set; }

        internal readonly PsContext Context;
        internal readonly bool PreserveHeaderContext;

        public AltListViewDefinition( IReadOnlyList< ListItem > listItems )
            : this( listItems, null, null )
        {
        }

        public AltListViewDefinition( IReadOnlyList< ListItem > listItems,
                                      ScriptBlock produceGroupByHeader,
                                      object groupBy )
            : this( listItems, produceGroupByHeader, groupBy, false, false )
        {
        }

        public AltListViewDefinition( IReadOnlyList< ListItem > listItems,
                                      ScriptBlock produceGroupByHeader,
                                      object groupBy,
                                      bool captureContext,
                                      bool preserveHeaderContext )
        {
            if( null == listItems )
                throw new ArgumentNullException( "listItems" );

            ListItems = listItems;
            ProduceGroupByHeader = produceGroupByHeader;
            GroupBy = groupBy;
            PreserveHeaderContext = preserveHeaderContext;
            if( captureContext )
                Context = DbgProvider.CapturePsContext();
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
    } // end class AltListViewDefinition
}

