using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;

namespace MS.Dbg.Formatting.Commands
{
    // TODO: Holy cow, have you seen what the built-in formatting engine can do when
    // there isn't enough room? Try "get-process s* | ft *"...

    [Cmdlet( VerbsCommon.Format, "AltTable" )]
    public class FormatAltTableCommand : FormatBaseCommand< AltTableViewDefinition >
    {
        [Parameter( Mandatory = false )]
        public SwitchParameter HideTableHeaders { get; set; }

        // TODO: Other parameters mirroring standard Format-Table


        protected override string ViewFromPropertyCommandName => "New-AltTableViewDefinition";


        // TODO: make this configurable, part of the table view definition or something?
        private static ColorString sm_tableHeaderColors = new ColorString()
            .AppendPushFgBg( ConsoleColor.Black, ConsoleColor.Cyan ).MakeReadOnly();

        private static readonly string[] sm_preferredPropNames = new string[]
        {
            "*Name*",
            "*Id",
            "*Key",
            "Identity",
            "Value",
            "Status",
        };

        private static ColorString sm_pop = new ColorString().AppendPop().MakeReadOnly();
        private bool m_headerShown;
        private bool m_calculatedWidths;


        private void _WriteFooterIfNecessary()
        {
            if( m_headerShown && (null != m_view.Footer) && !Stopping )
            {
                string footer = RenderScriptValue( new PSObject( m_view ), m_view.Footer.Script );
                if( null != footer )
                {
                    footer = PadAndAlign( footer,
                                          Host.UI.RawUI.BufferSize.Width - 1,
                                          m_view.Footer.Alignment );
                    SafeWriteObject( String.Empty ); // To get a blank line
                    SafeWriteObject( footer );
                }
            }
        } // end _WriteFooterIfNecessary()


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


        protected override void EndProcessing()
        {
            base.EndProcessing();
            _WriteFooterIfNecessary();
        } // end EndProcessing()


        protected override void ApplyViewToInputObject()
        {
            if( !m_calculatedWidths )
            {
                m_view.CalculateWidthsAndAlignments( Host.UI.RawUI.BufferSize.Width, InputObject );
                m_calculatedWidths = true;
            }

            if( !m_headerShown && !HideTableHeaders )
            {
                _WriteHeaders();
                m_headerShown = true;
            }

            // Write row values:
            ColorString row = new ColorString();
            for( int colIdx = 0; colIdx < m_view.Columns.Count; colIdx++ )
            {
                if( Stopping )
                    break;

                string val;
                Column c = m_view.Columns[ colIdx ];
                if( c is PropertyColumn )
                {
                    PropertyColumn pc = (PropertyColumn) c;
                    val = RenderPropertyValue( InputObject, pc.PropertyName, pc.FormatString );
                }
                else
                {
                    ScriptColumn sc = (ScriptColumn) c;
                    val = RenderScriptValue( InputObject, sc.Script );
                    if( null == val )
                        val = String.Empty;
                }

                try
                {
                    val = PadAndAlign( val,
                                       c.CalculatedWidth,
                                       c.CalculatedAlignment,
                                       c.TrimLocation );
                }
                catch( Exception e )
                {
                    // Debugging aid to help you figure out what blew up:
                    e.Data[ "ColumnLabel" ] = c.Label;
                    e.Data[ "val" ] = val;

                    // Sometimes you have a column that's very small (3 or less cells
                    // wide). If you get an error trying to render the value that goes in
                    // it, it will certainly be too wide, but we can't add an ellipsis
                    // when truncating (not enough room). Rather than blow up the
                    // formatting operation, we'll substitute a shorthand to indicate that
                    // an error occurred.
                    if( (e is ArgumentException) && (c.CalculatedWidth <= 3) )
                    {
                        if( c.CalculatedWidth == 3 )
                            val = new ColorString( ConsoleColor.Red, "ERR" ).ToString( DbgProvider.HostSupportsColor );
                        else if( c.CalculatedWidth == 2 )
                            val = new ColorString( ConsoleColor.Red, "ER" ).ToString( DbgProvider.HostSupportsColor );
                        else if( c.CalculatedWidth == 1 )
                            val = new ColorString( ConsoleColor.Red, "X" ).ToString( DbgProvider.HostSupportsColor );
                        else
                        {
                            var msg = Util.Sprintf( "Calculated width of 0 or less? {0}", c.CalculatedWidth );
                            Util.Fail( msg );
                            throw new Exception( msg, e );
                        }
                    }
                    else
                    {
                        throw;
                    }
                }

                row.Append( val );

                if( colIdx != (m_view.Columns.Count - 1) )
                    row.Append( " " ); // separator
            }
            SafeWriteObject( row );
        } // end ApplyViewToInputObject()


        protected override void ResetState( bool newViewChosen )
        {
            _WriteFooterIfNecessary();
            m_headerShown = false;
            if( newViewChosen )
            {
                m_calculatedWidths = false;
            }
        } // end ResetState()


        private void _WriteHeaders()
        {
            ColorString headers = new ColorString( sm_tableHeaderColors );

            // Write labels:
            for( int colIdx = 0; colIdx < m_view.Columns.Count; colIdx++ )
            {
                Column c = m_view.Columns[ colIdx ];
                headers.Append( PadAndAlign( c.Label,
                                             c.CalculatedWidth,
                                             c.CalculatedAlignment,
                                             c.TrimLocation ) );
                if( colIdx != (m_view.Columns.Count - 1) )
                    headers.Append( " " ); // separator
            }
            // Clear the header color:
            headers.Append( sm_pop );

            headers.AppendLine();

            // Write -------:
            for( int colIdx = 0; colIdx < m_view.Columns.Count; colIdx++ )
            {
                Column c = m_view.Columns[ colIdx ];
                int len = CaStringUtil.Length( c.Label );
                headers.Append( PadAndAlign( new String( '-', Math.Min( len, c.CalculatedWidth ) ),
                                             c.CalculatedWidth,
                                             c.CalculatedAlignment,
                                             c.TrimLocation ) );
                if( colIdx != (m_view.Columns.Count - 1) )
                    headers.Append( " " ); // separator
            }
            SafeWriteObject( headers );
        } // end _WriteHeaders()


        private bool _CanAddColumn( int consumedWidth,
                                    int numUnspecifiedWidth,
                                    Column proposed,
                                    out int estimatedWidth )
        {
            const int maxUnsizedColumns = 6;
            estimatedWidth = proposed.Width;

            if( 0 == proposed.Width )
            {
                if( numUnspecifiedWidth == maxUnsizedColumns )
                    return false;
                else
                    estimatedWidth = 2 * proposed.Label.Length;
            }

            // TODO: What if there is no Host.UI.RawUI?
            int totalAvailable = Host.UI.RawUI.BufferSize.Width - 1;
            totalAvailable -= consumedWidth;
            Util.Assert( totalAvailable >= 0 );

            int proposedWidthAddition = Math.Max( estimatedWidth, proposed.Label.Length );
            return proposedWidthAddition <= totalAvailable;
        } // end _CanAddColumn()


        protected override AltTableViewDefinition GenerateView()
        {
            Util.Assert( null != InputObject );
            Util.Assert( (null == Property) || (0 == Property.Length)); // -Property should be handled by different code path

            var columns = new List< Column >();

            // Table view is constricted... there is only so much horizontal space, so
            // we'll have to limit how many properties we take. We'll try to prioritize
            // some common ones.
            var alreadyConsidered = new HashSet< string >();
            int consumedWidth = 0;
            int numUnspecifiedWidth = 0;
            int estimatedWidth;
            foreach( var propName in ResolvePropertyNames( sm_preferredPropNames, warn: false ) )
            {
                alreadyConsidered.Add( propName );
                try
                {
                    var proposedColumn = new PropertyColumn( propName,
                                                             ColumnAlignment.Default,
                                                             AltTableViewDefinition.GetWidthForProperty( InputObject,
                                                                                                         propName ) );
                    if( _CanAddColumn( consumedWidth, numUnspecifiedWidth, proposedColumn, out estimatedWidth ) )
                    {
                        columns.Add( proposedColumn );
                        consumedWidth += estimatedWidth;
                        if( 0 == proposedColumn.Width )
                            numUnspecifiedWidth++;
                    }
                }
                catch( RuntimeException rte ) // from GetWidthForProperty, which attempt to access the property
                {
                    SafeWriteWarning( Util.Sprintf( "Not considering property {0} for generated view, because it threw: {1}",
                                                    propName,
                                                    Util.GetExceptionMessages( rte ) ) );
                  //LogManager.Trace( "Not considering property {0} for generated view, because it threw: {1}",
                  //                  propName,
                  //                  Util.GetExceptionMessages( rte ) );
                }
            } // end foreach( preferred property )

            foreach( var pi in InputObject.Properties )
            {
                if( alreadyConsidered.Contains( pi.Name ) )
                    continue;

                if( pi.IsGettable )
                {
                    try
                    {
                        var proposedColumn = new PropertyColumn( pi.Name,
                                                                 ColumnAlignment.Default,
                                                                 AltTableViewDefinition.GetWidthForProperty( InputObject,
                                                                                                             pi.Name ) );

                        if( _CanAddColumn( consumedWidth, numUnspecifiedWidth, proposedColumn, out estimatedWidth ) )
                        {
                            columns.Add( proposedColumn );
                            consumedWidth += estimatedWidth;
                            if( 0 == proposedColumn.Width )
                                numUnspecifiedWidth++;
                        }
                    }
                    catch( RuntimeException rte ) // from GetWidthForProperty, which attempt to access the property
                    {
                        SafeWriteWarning( Util.Sprintf( "Not considering property {0} for generated view, because it threw: {1}",
                                                        pi.Name,
                                                        Util.GetExceptionMessages( rte ) ) );
                     // LogManager.Trace( "Not considering property {0} for generated view, because it threw: {1}",
                     //                   pi.Name,
                     //                   Util.GetExceptionMessages( rte ) );
                    }
                }
            } // end foreach( all properties )

            return new AltTableViewDefinition( false, columns );
        } // end GenerateView()
    } // end class FormatAltTableCommand
}

