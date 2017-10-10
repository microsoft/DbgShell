using System;
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
            string val = RenderScriptValue( InputObject, m_view.Script, false, m_view.Context );
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


        protected override void ResetState()
        {
            // nothing to do
        } // end ResetState()


        internal static string FormatSingleLineDirect( object obj )
        {
            if( null == obj )
                return String.Empty;

            PSObject pso = obj as PSObject;
            if( null == pso )
                pso = new PSObject( obj );

            var view = AltFormattingManager.ChooseFormatInfoForPSObject< AltSingleLineViewDefinition >( pso );

            ScriptBlock script;
            PsContext ctx;
            if( null != view )
            {
                script = view.Script;
                ctx = view.Context;
            }
            else
            {
                script = sm_DefaultScript;
                ctx = null;
            }

            return FormatSingleLineDirect( pso, script, ctx );
        }

        internal static string FormatSingleLineDirect( PSObject obj,
                                                       ScriptBlock script,
                                                       PsContext ctx )
        {
            string val;
            using( FormatAltSingleLineCommand cmd = new FormatAltSingleLineCommand() )
            {
                val = cmd.RenderScriptValue( obj, script, false, ctx );
            }
            // TODO: What to do if it spans more than a line? Just truncate? Issue a
            // warning? Add "..."? Also, consider that the view definition might have been
            // generated.
            if( null == val )
            {
                return String.Empty;
            }
            else
            {
                int idx = CaStringUtil.ApparentIndexOf( val, '\n' );
                if( idx < 0 )
                {
                    return val;
                }
                else
                {
                    return CaStringUtil.Truncate( val, idx );
                }
            }
        } // end FormatSingleLineDirect
    } // end class FormatAltSingleLineCommand
}

