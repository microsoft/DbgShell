using System;
using System.Collections.ObjectModel;
using System.Management.Automation;

namespace MS.Dbg.Formatting.Commands
{
    [Cmdlet( VerbsCommon.Format, "AltCustom" )]
    public class FormatAltCustomCommand : FormatBaseCommand< AltCustomViewDefinition >
    {
        protected override void ApplyViewToInputObject()
        {
            var script = m_view.Script;
            if( m_view.PreserveScriptContext )
            {
                if( m_preservedScriptContext == null )
                {
                    m_preservedScriptContext = new PSModuleInfo( false );
                    m_preservedScriptContext.Invoke( sm_importModuleScript, script.Module );
                }
            }

            string val = RenderScriptValue( InputObject, script, true );

            if( null != val )
                WriteObject( val );
        } // end ProcessRecord()


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

        protected override void EndProcessing()
        {
            base.EndProcessing();
            if( (null != m_view.End) && !Stopping )
            {
                string val = RenderScriptValue( null,
                                                m_view.End,
                                                false );

                if( null != val )
                    WriteObject( val );
            }
            WriteObject( String.Empty ); // to get a blank line
        }
    } // end class FormatAltCustomCommand
}


