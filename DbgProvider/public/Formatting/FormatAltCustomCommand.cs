using System;
using System.Collections.ObjectModel;
using System.Management.Automation;

namespace MS.Dbg.Formatting.Commands
{
    [Cmdlet( VerbsCommon.Format, "AltCustom" )]
    public class FormatAltCustomCommand : FormatBaseCommand< AltCustomViewDefinition >
    {
        // We need to store this separately--we don't want it to get mixed in with the
        // context stored by m_view.Context, because we only want this context preserved
        // across a single invocation of Format-AltCustom, not every subsequent
        // invocation.
        private PsContext m_preservedScriptContext;

        protected override void ApplyViewToInputObject()
        {
            string val;
            using( var pch = new PsContextHelper( m_view.Script,
                                                  m_view.Context + m_preservedScriptContext,
                                                  m_view.PreserveScriptContext ) )
            {
                val = RenderScriptValue( InputObject, pch.AdjustedScriptBlock, true, pch.WithContext );
                if( m_view.PreserveScriptContext )
                    m_preservedScriptContext += pch.SavedContext;
            }

            if( null != val )
                WriteObject( val );
        } // end ProcessRecord()


        protected override void ResetState()
        {
            // nothing to do
        } // end ResetState()


        protected override ScriptBlock GetCustomWriteGroupByGroupHeaderScript(
            out PsContext context,
            out bool preserveHeaderContext )
        {
            Util.Assert( null != m_view );
            context = m_view.Context;
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
                                                false,
                                                m_view.Context + m_preservedScriptContext );

                if( null != val )
                    WriteObject( val );
            }
            WriteObject( String.Empty ); // to get a blank line
        }
    } // end class FormatAltCustomCommand
}


