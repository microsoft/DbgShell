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
                                        bool preserveHeaderContext )
            : this( script,
                    produceGroupByHeader,
                    groupBy,
                    end,
                    preserveHeaderContext,
                    (bool) false )
        {
        }

        public AltCustomViewDefinition( ScriptBlock script,
                                        ScriptBlock produceGroupByHeader,
                                        object groupBy,
                                        ScriptBlock end,
                                        bool preserveHeaderContext,
                                        bool preserveScriptContext )
        {
            Script = script ?? throw new ArgumentNullException( nameof(script) );
            ProduceGroupByHeader = produceGroupByHeader;
            GroupBy = groupBy;
            End = end;
            PreserveHeaderContext = preserveHeaderContext;
            PreserveScriptContext = preserveScriptContext;
        } // end constructor


        bool IFormatInfo.LooksLikeExistingFromPropertyDefinition( IFormatInfo other )
        {
            Util.Fail( "nobody should be calling this..." );
            return false;
        }
    } // end class AltCustomViewDefinition
}

