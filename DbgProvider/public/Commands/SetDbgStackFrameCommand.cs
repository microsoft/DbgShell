using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Set,
             "DbgStackFrame", // Or maybe "DbgStackFrameContext"?
             DefaultParameterSetName = c_FrameNumParamSet )]
    public class SetDbgStackFrameCommand : DbgBaseCommand
    {
        private const string c_FrameNumParamSet = "FrameNumParamSet";
        private const string c_IncrementParamSet = "IncrementNumParamSet";
        private const string c_DecrementParamSet = "DecrementNumParamSet";

        [Parameter( Mandatory = true,
                    Position = 0,
                    ParameterSetName = c_FrameNumParamSet,
                    ValueFromPipeline = true )]
        // It's not an address, but we want it to be in hex, and also recognize "0n".
        //
        // Unfortunately, without a true "hex input mode", this still doesn't work very
        // well--".frame 10" will be interpreted as 0n10.
        //
        // Using this transformation seems to work out okay even though we want a uint,
        // not a ulong.
        [AddressTransformation( SkipGlobalSymbolTest = true )]
        public uint FrameNumber { get; set; }

        [Parameter]
        public SwitchParameter DontChangeRegisterContext { get; set; }


        // TODO: These params are pretty hokey. Should I remove them? I've left them in
        // for now for discoverability. (who would guess ".f+"?)
        [Parameter( Mandatory = true, ParameterSetName = c_IncrementParamSet )]
        public SwitchParameter Increment { get; set; }

        [Parameter( Mandatory = true, ParameterSetName = c_DecrementParamSet )]
        public SwitchParameter Decrement { get; set; }


        private void _Worker( uint frameNum, string errorId )
        {
            if( ((int) frameNum) < 0 )
            {
                throw new DbgProviderException( "Current frame index underflow.",
                                                "CannotDecrementPastBottomOfStack",
                                                ErrorCategory.InvalidOperation );
            }

            if( !Debugger.SetCurrentScopeFrame( frameNum ) )
            {
                throw new DbgProviderException( Util.Sprintf( "Cannot find frame 0x{0:x}, previous scope unchanged.",
                                                              frameNum ),
                                                errorId,
                                                ErrorCategory.InvalidOperation );
            }
        }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            try
            {
                if( Increment )
                {
                    var ctx = Debugger.GetCurrentDbgEngContext();
                    _Worker( ctx.FrameIndex + 1, "CannotIncrementPastTopOfStack" );
                    DbgProvider.SetAutoRepeatCommand( ".f+" );
                }
                else if( Decrement )
                {
                    var ctx = Debugger.GetCurrentDbgEngContext();
                    _Worker( ctx.FrameIndex - 1, "CannotDecrementPastBottomOfStack" );
                    DbgProvider.SetAutoRepeatCommand( ".f-" );
                }
                else
                {
                    _Worker( FrameNumber, "StackFrameOutsideOfActualStack" );
                }
            }
            catch( DbgProviderException dpe )
            {
                WriteError( dpe );
            }

            DbgProvider.SetLocationByDebuggerContext();
        } // end ProcessRecord()
    } // end class SetDbgStackFrameCommand
}

