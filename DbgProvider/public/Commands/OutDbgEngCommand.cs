using System;
using System.Threading.Tasks;
using System.Management.Automation;
using Microsoft.Diagnostics.Runtime.Interop;
using MS.Dbg.Commands;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsData.Out, "DbgEng", DefaultParameterSetName = "OtherClientParamSet" )]
    public class OutDbgEngCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true, Position = 0, ValueFromPipeline = true )]
        public object[] InputObject { get; set; }

        [Parameter( Mandatory = false, ParameterSetName = "ThisClientParamSet" )]
        public SwitchParameter ThisClientOnly { get; set; }

        [Parameter( Mandatory = false, ParameterSetName = "OtherClientParamSet" )]
        public SwitchParameter OtherClientsOnly { get; set; }

        [Parameter( Mandatory = false, ParameterSetName = "LogOnlyClientParamSet" )]
        public SwitchParameter LogOnly { get; set; }

        // I don't think we need this--if you want it ignored, don't call Out-DbgEng?
     // [Parameter( Mandatory = false, ParameterSetName = "IgnoreClientParamSet" )]
     // public SwitchParameter Ignore { get; set; }

        [Parameter( Mandatory = false )]
        public SwitchParameter NotLogged { get; set; }

        [Parameter( Mandatory = false )]
        public SwitchParameter Override { get; set; }

        [Parameter( Mandatory = false )]
        public SwitchParameter Dml { get; set; }

        [Parameter( Mandatory = false )]
        public DEBUG_OUTPUT OutputType { get; set; }

        [Parameter( Mandatory = false )]
        public SwitchParameter NoNewLine { get; set; }


        public OutDbgEngCommand()
        {
            OutputType = DEBUG_OUTPUT.NORMAL;
        }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            DEBUG_OUTCTL ctl;

            if( ThisClientOnly )
                ctl = DEBUG_OUTCTL.THIS_CLIENT;
            else if( OtherClientsOnly )
                ctl = DEBUG_OUTCTL.ALL_OTHER_CLIENTS;
         // else if( Ignore )
         //     ctl = DEBUG_OUTCTL.IGNORE;
            else if( LogOnly )
                ctl = DEBUG_OUTCTL.LOG_ONLY;
            else
                ctl = DEBUG_OUTCTL.ALL_CLIENTS;

            if( NotLogged )
                ctl |= DEBUG_OUTCTL.NOT_LOGGED;

            if( Override )
                ctl |= DEBUG_OUTCTL.OVERRIDE_MASK;

            if( Dml )
                ctl |= DEBUG_OUTCTL.DML;

            foreach( var io in InputObject )
            {
                string s = io.ToString();
                if( !s.EndsWith( "\n" ) && !NoNewLine )
                {
                    s += Environment.NewLine;
                }


                Task[] tasks = new Task[ 3 ];
                try
                {
                    // These tasks need to happen strictly in sequence, of course. We are
                    // relying on the fact that they all get queued to the DbgEngThread,
                    // which will execute them in the order received.
                    tasks[ 0 ] = Debugger.CallExtensionAsync( 0, "internal_SwapMask", null );
                    tasks[ 1 ] = Debugger.ControlledOutputAsync( ctl, OutputType, s );
                }
                finally
                {
                    tasks[ 2 ] = Debugger.CallExtensionAsync( 0, "internal_SwapMask", null );
                }
                Util.Await( Task.WhenAll( tasks ) );
            }
        } // end ProcessRecord()

        protected override bool TrySetDebuggerContext { get { return false; } }
    } // end class OutDbgEngCommand
}

