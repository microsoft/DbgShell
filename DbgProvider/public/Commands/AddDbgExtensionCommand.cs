using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Add, "DbgExtension" )]
    public class AddDbgExtensionCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true )]
        // aliases?
        public string PathOrName { get; set; }


        protected override bool TrySetDebuggerContext { get { return false; } }

        private void _ConsumeLine( string line )
        {
            SafeWriteObject( line, true ); // this will get sent to the pipeline thread
        }


        private static readonly char[] sm_dirSeparators = new char[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar };

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            using( var disposer = new ExceptionGuard( Debugger.HandleDbgEngOutput( _ConsumeLine ) ) )
            {
                // Failure to load an extension should be a terminating error.
                //
                // Because the only reason to load an extension is so that you can call
                // commands in it. And if you didn't load it, then none of those commands
                // will work.

                string providerPath = PathOrName;

                if( providerPath.IndexOfAny( sm_dirSeparators ) >= 0 )
                {
                    // We support PS paths.
                    //
                    // (we don't want to call GetUnresolvedProviderPathFromPSPath if it's
                    // just a bare name, else PS will interpret that as a relative path,
                    // which would be wrong)
                    providerPath = GetUnresolvedProviderPathFromPSPath( PathOrName );
                }

                MsgLoop.Prepare();

                var t = Debugger.AddExtensionAsync( providerPath );
                t.ContinueWith( ( x ) => { disposer.Dispose(); SignalDone(); } );

                MsgLoop.Run();

                var elr = Util.Await( t ); // in case it threw
                if( !string.IsNullOrEmpty( elr.LoadOutput ) )
                {
                    SafeWriteObject( elr.LoadOutput );
                }
                SafeWriteVerbose( "Loaded extension: {0}", PathOrName );
            } // end using( disposer )
        } // end ProcessRecord()
    } // end class AddDbgExtensionCommand
}

