using System;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgEngineEventFilter" )]
    [OutputType( typeof( DbgEngineEventFilter ) )]
    public class GetDbgEngineEventFilterCommand : DbgBaseCommand
    {
        // TODO: add filtering, etc.

        protected override void ProcessRecord()
        {
            // TODO: rename "specific" event filters to "engine" event filters?
            foreach( var sef in Debugger.GetSpecificEventFilters() )
                WriteObject( sef );
        } // end ProcessRecord()
    } // end class GetDbgEngineEventFilterCommand

    [Cmdlet( VerbsCommon.Get, "DbgExceptionEventFilter" )]
    [OutputType( typeof( DbgExceptionEventFilter ) )]
    public class GetDbgExceptionEventFilterCommand : DbgBaseCommand
    {
        // TODO: add filtering, etc.

        protected override void ProcessRecord()
        {
            foreach( var eef in Debugger.GetExceptionEventFilters() )
                WriteObject( eef );
        } // end ProcessRecord()
    } // end class GetDbgExceptionEventFilterCommand
}
