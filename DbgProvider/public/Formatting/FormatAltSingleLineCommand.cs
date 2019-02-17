using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace MS.Dbg.Formatting.Commands
{
    [Cmdlet( VerbsCommon.Format, "AltSingleLine" )]
    public class FormatAltSingleLineCommand : FormatBaseCommand< AltSingleLineViewDefinition >
    {
        private static readonly ScriptBlock sm_DefaultScript = ScriptBlock.Create( "$_" );


        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            // We want to do this in BeginProcessing to help catch the case where you
            // accidentally typed "Format-AltSingleLine $blah". (The correct command would
            // be "$blah | Format-AltSingleLine" OR "Format-AltSingleLine -InputObject
            // $blah". (We won't ever make it to ApplyViewToInputObject because there is
            // no input object.)
            if( (null != Property) && (0 != Property.Length) )
            {
                throw new InvalidOperationException( "The Format-AltSingleLine command does not support selecting properties. Did you forget to use -InputObject or use the pipeline?" );
            }
        } // end BeginProcessing()


        protected override AltSingleLineViewDefinition GenerateView()
        {
            Util.Assert( null != InputObject );

            return new AltSingleLineViewDefinition( sm_DefaultScript );
        } // end GenerateView()


        protected override void ApplyViewToInputObject()
        {
            string val = RenderScriptValue( InputObject, m_view.Script, false );
            // TODO: What to do if it spans more than a line? Just truncate? Issue a
            // warning? Add "..."? Also, consider that the view definition might have been
            // generated.
            if( null == val )
            {
                WriteObject( String.Empty );
            }
            else
            {
                //int idx = val.IndexOf( '\n' );
                // TODO: Now we pass 'false' for dontGroupMultipleResults, so I think this
                // won't ever get hit unless we are formatting a string that contains a
                // newline. Perhaps in that case we should escape it.
                int idx = CaStringUtil.ApparentIndexOf( val, '\n' );
                if( idx < 0 )
                {
                    WriteObject( val );
                }
                else
                {
                    WriteObject( CaStringUtil.Truncate( val, idx, false ) );
                }
            }
        } // end ApplyViewToInputObject()


        protected override void ResetState( bool newViewChosen )
        {
            // nothing to do
        } // end ResetState()


        internal static string FormatSingleLineDirect( object obj )
        {
            return FormatSingleLineDirect( obj, allowMultipleLines: false );
        }

        internal static string FormatSingleLineDirect( object obj,
                                                       bool allowMultipleLines )
        {
            if( null == obj )
                return String.Empty;

            PSObject pso = obj as PSObject;
            if( null == pso )
                pso = new PSObject( obj );

            var view = AltFormattingManager.ChooseFormatInfoForPSObject< AltSingleLineViewDefinition >( pso );

            ScriptBlock script;
            if( null != view )
            {
                script = view.Script;
            }
            else
            {
                script = sm_DefaultScript;
            }

            return FormatSingleLineDirect( pso, script, allowMultipleLines );
        }

        internal static string FormatSingleLineDirect( PSObject obj,
                                                       ScriptBlock script,
                                                       bool allowMultipleLines )
        {
#if DEBUG
            sm_renderScriptCallDepth++;
            if( sm_renderScriptCallDepth > 10 )
            {
                // Helps to catch runaway rendering /before/ gobbling tons of memory
                System.Diagnostics.Debugger.Break();
                // Maybe I should just throw?
            }
#endif

            var ctxVars = new List<PSVariable> { new PSVariable( "_", obj ), new PSVariable( "PSItem", obj ) };
            var results = script.InvokeWithContext( null, ctxVars );

#if DEBUG
            sm_renderScriptCallDepth--;
#endif
            string val = null;
            if( results?.Count > 0)
                val = ObjectsToMarkedUpString( results,
                                            "{0}", // <-- IMPORTANT: this prevents infinite recursion via Format-AltSingleLine
                                            null,
                                            false, 4 ).ToString();

            // TODO: What to do if it spans more than a line? Just truncate? Issue a
            // warning? Add "..."? Also, consider that the view definition might have been
            // generated.
            if( null == val )
            {
                return String.Empty;
            }

            // Q. Why might an alleged single-line view generate multiple lines?
            //
            // A. Could be buggy. Could be a generated view, and somebody's ToString()
            //    generates multiple lines. In short: it's not necessarily "weird", or
            //    unusual.
            //
            // Q. Why would we want to allow multiple lines? Doesn't the name of this
            //    class / method say "format SINGLE LINE"?
            //
            // A. Sometimes multi-line views can not only be accommodated, they are
            //    desirable, such as for compatibility with the built-in Format-List
            //    command.

            if( allowMultipleLines )
            {
                return val;
            }

            int idx = CaStringUtil.ApparentIndexOf( val, '\n' );
            if( idx < 0 )
            {
                return val;
            }

            return CaStringUtil.Truncate( val, idx );
        } // end FormatSingleLineDirect
    } // end class FormatAltSingleLineCommand
}

