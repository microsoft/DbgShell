using System;
using System.Management.Automation;

namespace MS.Dbg.Formatting
{
    public class AltCustomViewDefinition : IFormatInfo
    {
        string IFormatInfo.FormatCommand { get { return "Format-AltCustom"; } }
        string IFormatInfo.Module { get { return "Debugger"; } }

        public ScriptBlock Script { get; private set; }

        public ScriptBlock End { get; private set; }

        public ScriptBlock ProduceGroupByHeader { get; private set; }

        public object GroupBy { get; private set; }

        internal readonly PsContext Context;
        // If true, preserve context created by the ProduceGroupByHeader script, to be
        // used with Script (when processing each item).
        internal readonly bool PreserveHeaderContext;
        // If true, preserve context created by the Script script, to be used with
        // subsequent invocations of Script, and for the End script.
        internal readonly bool PreserveScriptContext;

        public AltCustomViewDefinition( ScriptBlock script )
            : this( script, null, null )
        {
        }

        public AltCustomViewDefinition( ScriptBlock script, ScriptBlock produceGroupByHeader )
            : this( script, produceGroupByHeader, null )
        {
        }

        public AltCustomViewDefinition( ScriptBlock script,
                                        ScriptBlock produceGroupByHeader,
                                        object groupBy )
            : this( script, produceGroupByHeader, groupBy, null )
        {
        }

        public AltCustomViewDefinition( ScriptBlock script,
                                        ScriptBlock produceGroupByHeader,
                                        object groupBy,
                                        ScriptBlock end )
            : this( script, produceGroupByHeader, groupBy, end, false )
        {
        }

        public AltCustomViewDefinition( ScriptBlock script,
                                        ScriptBlock produceGroupByHeader,
                                        object groupBy,
                                        ScriptBlock end,
                                        bool captureContext )
            : this( script, produceGroupByHeader, groupBy, end, captureContext, false )
        {
        }

        public AltCustomViewDefinition( ScriptBlock script,
                                        ScriptBlock produceGroupByHeader,
                                        object groupBy,
                                        ScriptBlock end,
                                        bool captureContext,
                                        bool preserveHeaderContext )
            : this( script,
                    produceGroupByHeader,
                    groupBy,
                    end,
                    captureContext,
                    preserveHeaderContext,
                    false )
        {
        }

        public AltCustomViewDefinition( ScriptBlock script,
                                        ScriptBlock produceGroupByHeader,
                                        object groupBy,
                                        ScriptBlock end,
                                        bool captureContext,
                                        bool preserveHeaderContext,
                                        bool preserveScriptContext )
        {
            if( null == script )
                throw new ArgumentNullException( "script" );

            Script = script;
            ProduceGroupByHeader = produceGroupByHeader;
            GroupBy = groupBy;
            End = end;
            PreserveHeaderContext = preserveHeaderContext;
            PreserveScriptContext = preserveScriptContext;
            if( captureContext )
                Context = DbgProvider.CapturePsContext();
        } // end constructor
    } // end class AltCustomViewDefinition
}

