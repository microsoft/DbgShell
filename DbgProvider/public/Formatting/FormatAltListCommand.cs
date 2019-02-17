using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Text;

namespace MS.Dbg.Formatting.Commands
{
    [Cmdlet( VerbsCommon.Format, "AltList" )]
    public class FormatAltListCommand : FormatBaseCommand< AltListViewDefinition >
    {

        // The problem with using a plain PSVariable with the 'readonly' option is that it
        // can't be removed. You can remove this one, though.
        private class ReadOnlyPSVariable< T > : PSVariable
        {
            private T m_t;

            public ReadOnlyPSVariable( string variableName, T t )
                : base( variableName )
            {
                m_t = t;
            }

            public override object Value
            {
                get
                {
                    return m_t;
                }
                set
                {
                    throw new PSInvalidOperationException( "You cannot modify this value." );
                }
            }

            public override ScopedItemOptions Options
            {
                get
                {
                    return ScopedItemOptions.ReadOnly;
                }
                set
                {
                    throw new PSInvalidOperationException( "You cannot modify this value." );
                }
            }
        } // end class ReadOnlyPSVariable


        private static ColorString sm_labelColors = new ColorString()
            .AppendPushFg( ConsoleColor.Cyan ).MakeReadOnly();

        private static ColorString sm_pop = new ColorString().AppendPop().MakeReadOnly();

        private static char[] sm_newlineDelim = new char[] { '\n' };


        protected override string ViewFromPropertyCommandName => "New-AltListViewDefinition";


        protected override AltListViewDefinition GenerateView()
        {
            Util.Assert( null != InputObject );
            Util.Assert( (null == Property) || (0 == Property.Length)); // -Property should be handled by different code path

            var items = new List< ListItem >();

            foreach( var pi in InputObject.Properties )
            {
                if( pi.IsGettable )
                    items.Add( new PropertyListItem( pi.Name ) );
            }

            return new AltListViewDefinition( items.AsReadOnly() );
        } // end GenerateView()


        protected override void ApplyViewToInputObject()
        {
            for( int idx = 0; idx < m_view.ListItems.Count; idx++ )
            {
                if( Stopping )
                    break;

                ColorString listItem = new ColorString( sm_labelColors );

                ListItem li = m_view.ListItems[ idx ];
                listItem.Append( PadAndAlign( li.Label,
                                              m_view.MaxLabelLength + 1,
                                              ColumnAlignment.Left ) + ": " );

                listItem.Append( sm_pop.ToString( DbgProvider.HostSupportsColor ) );

                string val;
                if( li is PropertyListItem )
                {
                    var pli = (PropertyListItem) li;
                    val = RenderPropertyValue( InputObject,
                                               pli.PropertyName,
                                               pli.FormatString,
                                               dontGroupMultipleResults: false,
                                               allowMultipleLines: true ); // <-- N.B. special for Format-List compat
                }
                else
                {
                    var sli = (ScriptListItem) li;
                    val = RenderScriptValue( InputObject, sli.Script );
                    if( null == val )
                        val = String.Empty;
                }

                listItem.Append( _Indent( val ) );
                SafeWriteObject( listItem );
            }
            // N.B. Using String.Empty here used to cause 3 blank lines instead of one.
            // I don't understand precisely why, but the crux of the problem is that
            //
            //     a) System.String has a custom view definition (which is to get around
            //        PowerShell's reticence to properly display strings if they have
            //        other stuff in their TypeNames) (see commit 4bc7d1c76f97d0)
            //     b) When we write the string here, for some reason it causes a
            //        transition between steppable pipelines, and the PS default
            //        formatter wants to put in an extra newline at the format start and
            //        another at the format end.
            //
            // Fortunately, it seems easy enough to workaround by sending a different type
            // of object down the pipeline.
            //
            // (I wonder if it might have been specific to the particular commands I was
            // using to test, like "uf blah!blah | fl", or symbols | fl, because of how
            // their formatting was done.)
            //
            //SafeWriteObject( String.Empty ); // to get a blank line
            SafeWriteObject( ColorString.Empty ); // to get a blank line
        } // end ApplyViewToInputObject()


        protected override void ResetState( bool newViewChosen )
        {
            // nothing to do
        } // end ResetState()


        protected override ScriptBlock GetCustomWriteGroupByGroupHeaderScript( out bool preserveHeaderContext )
        {
            Util.Assert( null != m_view );
            preserveHeaderContext = m_view.PreserveHeaderContext;
            return m_view.ProduceGroupByHeader;
        }

        protected override object GetViewDefinedGroupBy()
        {
            Util.Assert( null != m_view );
            return m_view.GroupBy;
        }

        private string _Indent( string val )
        {
            if( val.IndexOfAny( sm_newlineDelim ) < 0 )
                return val;

            StringBuilder sb = new StringBuilder( val.Length * 2 );
            string[] lines = val.Split( sm_newlineDelim );
            sb.Append( lines[ 0 ] );
            for( int i = 1; i < lines.Length; i++ )
            {
                sb.AppendLine();
                sb.Append( ' ', m_view.MaxLabelLength + 3 ); // +3 for " : "
                sb.Append( lines[ i ] );
            }
            return sb.ToString();
        } // end _Indent()
    } // end class FormatAltListCommand
}

