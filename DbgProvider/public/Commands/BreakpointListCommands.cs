using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    public class BreakpointListCommandBase : DbgBaseCommand
    {
        protected const string c_ByIdParamSetName = "ByIdParamSet";
        protected const string c_WildcardParamSetName = "WildcardParamSet";

        protected virtual bool NoIdMeansAll { get { return false; } }


        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true,
                    ParameterSetName = c_ByIdParamSetName )]
        public uint[] Id { get; set; }

        [Parameter( Mandatory = true,
                    Position = 0,
                    ParameterSetName = c_WildcardParamSetName )]
        [SupportsWildcards]
        [ValidateSet( "*" )]
        public string Star { get; set; }


        protected IEnumerable< DbgBreakpointInfo > _EnumBreakpointsToOperateOn()
        {
            if( !String.IsNullOrEmpty( Star ) ||
                (NoIdMeansAll && (null == Id)) )
            {
                foreach( var bp in Debugger.GetBreakpoints().Values )
                    yield return bp;
            }
            else
            {
                if( null != Id )
                {
                    foreach( var bpid in Id )
                    {
                        var bp = Debugger.TryGetBreakpointById( bpid );
                        if( null == bp )
                            SafeWriteWarning( "No such breakpoint: {0}", bpid );
                        else
                            yield return bp;
                    }
                }
                else
                {
                    //
                    // Ideally this would not even be accepted as a command, but
                    //
                    //   a) then I couldn't share the Id parameter in the base class, and
                    //   b) behavior would not match windbg (well, windbg doesn't emit a
                    //      warning, but it doesn't generate an error, either).
                    //
                    WriteWarning( "No breakpoint Ids specified." );
                }
            }
        } // end _EnumBreakpointsToOperateOn()


        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            EnsureTargetCanStep( Debugger );
        }
    } // end class BreakpointListCommandBase


    [Cmdlet( VerbsCommon.Get, "DbgBreakpoint",
             DefaultParameterSetName = c_ByIdParamSetName )]
    [OutputType( typeof( DbgBreakpointInfo ) )]
    public class GetDbgBreakpointCommand : BreakpointListCommandBase
    {
        protected override bool NoIdMeansAll { get { return true; } }

        [Parameter( Mandatory = false, ParameterSetName = "ByGuidParamSet" )]
        public Guid Guid { get; set; }

        protected override void ProcessRecord()
        {
            if( Guid != Guid.Empty )
            {
                foreach( var bp in Debugger.GetBreakpoints().Values )
                {
                    if( bp.Guid == Guid )
                    {
                        WriteObject( bp );
                        break;
                    }
                }
            }
            else
            {
                foreach( var bp in _EnumBreakpointsToOperateOn() )
                {
                    WriteObject( bp );
                }
            }
        } // end ProcessRecord()
    } // end class GetDbgBreakpointCommand


    [Cmdlet( VerbsCommon.Remove, "DbgBreakpoint",
             DefaultParameterSetName = c_ByIdParamSetName )]
    public class RemoveDbgBreakpointCommand : BreakpointListCommandBase
    {
        protected override void ProcessRecord()
        {
            foreach( var bp in _EnumBreakpointsToOperateOn() )
            {
                DbgBreakpointInfo tmpBp = bp; // because we can't pass a "foreach variable" as "ref"
                SafeWriteVerbose( "Deleting breakpoint {0} ({1}).",
                                  tmpBp.Id,
                                  tmpBp.SymbolicName );

                Debugger.RemoveBreakpoint( ref tmpBp );
            }
        } // end ProcessRecord()
    } // end class RemoveDbgBreakpointCommand


    public abstract class EnableDisableDbgBreakpointCommandBase : BreakpointListCommandBase
    {
        protected abstract bool Enable { get; }
        protected abstract string ActionWord { get; }


        [Parameter( Mandatory = false )]
        public SwitchParameter PassThru { get; set; }


        protected override void ProcessRecord()
        {
            foreach( var bp in _EnumBreakpointsToOperateOn() )
            {
                SafeWriteVerbose( "{0} breakpoint {1} ({2}).",
                                  ActionWord,
                                  bp.Id,
                                  bp.SymbolicName );

                bp.IsEnabled = Enable;

                if( PassThru )
                    SafeWriteObject( bp );
            } // end foreach( bp )
        } // end ProcessRecord()
    } // end class EnableDisableDbgBreakpointCommandBase


    [Cmdlet( VerbsLifecycle.Disable, "DbgBreakpoint",
             DefaultParameterSetName = c_ByIdParamSetName )]
    [OutputType( typeof( DbgBreakpointInfo ) )]
    public class DisableDbgBreakpointCommand : EnableDisableDbgBreakpointCommandBase
    {
        protected override bool Enable { get { return false; } }
        protected override string ActionWord { get { return "Disabling"; } }
    } // end class DisableDbgBreakpointCommand


    [Cmdlet( VerbsLifecycle.Enable, "DbgBreakpoint",
             DefaultParameterSetName = c_ByIdParamSetName )]
    [OutputType( typeof( DbgBreakpointInfo ) )]
    public class EnableDbgBreakpointCommand : EnableDisableDbgBreakpointCommandBase
    {
        protected override bool Enable { get { return true; } }
        protected override string ActionWord { get { return "Enabling"; } }
    } // end class EnableDbgBreakpointCommand
}
