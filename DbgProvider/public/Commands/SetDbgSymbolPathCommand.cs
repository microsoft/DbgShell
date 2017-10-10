using System;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Set, "DbgSymbolPath" )]
    public class SetSymbolPathCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true )]
        [AllowEmptyString] // Sometimes you want to zap the symbol path; handy for testing (because you can force symbol errors).
        public string Path { get; set; }

        [Parameter( Mandatory = false )]
        public SwitchParameter PassThru { get; set; }


        protected override bool TrySetDebuggerContext { get { return false; } }

        // TODO: Perhaps we should do the "<location> not accessible" check and warning
        // ourselves. But we also can't get the "expanded" symbol path any other way, so
        // we still need this.
        private void _HandleDbgEngOutput( string s )
        {
            if( s.StartsWith( "WARNING", StringComparison.OrdinalIgnoreCase ) )
            {
                if( s.Length > 7 )
                    s = s.Substring( 7 ); // strip off "WARNING"

                if( s.StartsWith( " : " ) && (s.Length > 3) )
                    s = s.Substring( 3 );
                else if( s.StartsWith( ": " ) && (s.Length > 2) )
                    s = s.Substring( 2 );

                SafeWriteWarning( s );
            }
            else
            {
                if( s.StartsWith( "VERBOSE", StringComparison.OrdinalIgnoreCase ) )
                {
                    if( s.Length > 7 )
                        s = s.Substring( 7 ); // strip off "VERBOSE"

                    if( s.StartsWith( " : " ) && (s.Length > 3) )
                        s = s.Substring( 3 );
                    else if( s.StartsWith( ": " ) && (s.Length > 2) )
                        s = s.Substring( 2 );
                }
                SafeWriteVerbose( s );
            }
        } // end _HandleDbgEngOutput()


        private static string _FixPsPathsInSymbolPath( string inputPath, Func< string, string > resolvePath )
        {
            // This seems a little fragile... maybe we should just screen path elements
            // that are passed to the ".sympath+" function there, and call it good? That
            // wouldn't cover calls to Set-DbgSymbolPath directly, but it might be "good
            // enough".
            var pathElems = inputPath.Split( ';' );
            for( int i = 0; i < pathElems.Length; i++ )
            {
                var pathElemElems = pathElems[ i ].Split( '*' );
                for( int j = 0; j < pathElemElems.Length; j++ )
                {
                    if( !pathElemElems[ j ].StartsWith( "http:", StringComparison.OrdinalIgnoreCase ) &&
                        !pathElemElems[ j ].StartsWith( "https:", StringComparison.OrdinalIgnoreCase ) &&
                        !pathElemElems[ j ].EndsWith( ".dll", StringComparison.OrdinalIgnoreCase ) && // symbol server DLL
                        !(pathElemElems[ j ].Length == 0) && // the part after "SRV*" (implicit http://symbols or whatever it is)
                        !(0 == Util.Strcmp_OI( "SRV",    pathElemElems[ j ] )) &&
                        !(0 == Util.Strcmp_OI( "symsrv", pathElemElems[ j ] )) &&
                        !(0 == Util.Strcmp_OI( "cache",  pathElemElems[ j ] )) )
                    {
                        pathElemElems[ j ] = resolvePath( pathElemElems[ j ] );
                    }
                }
                pathElems[ i ] = String.Join( "*", pathElemElems );
            }

            return String.Join( ";", pathElems );
        } // end _FixPsPathsInSymbolPath()


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( null == Path )
                Path = String.Empty;

            //
            // Fix PS-specific paths.
            //
            var fixedPath = _FixPsPathsInSymbolPath( Path, GetUnresolvedProviderPathFromPSPath );

            if( 0 == fixedPath.Length )
                WriteWarning( "The symbol path is empty." );

            Debugger.SymbolPath = fixedPath;
            if( PassThru )
                WriteObject( Debugger.SymbolPath );

            MsgLoop.Prepare();

            // If the specified symbol path is inaccessible, the SetSymbolPathWide call
            // will generate some warning dbgeng output. But we'd also like to be able to
            // get output about the "expanded" symbol path. Unfortunately, there is no way
            // to get that, except to parse ".sympath" output. Since executing ".sympath"
            // will also repeat the warning about the inaccessible symbol path, we won't
            // start displaying dbgeng output until now.
            using( Debugger.HandleDbgEngOutput( _HandleDbgEngOutput ) )
            {
                // TODO: Feature request: no way to get expanded symbol path except to parse ".sympath" output.
                var t = Debugger.InvokeDbgEngCommandAsync( ".sympath", false );

                t.ContinueWith( (x) => MsgLoop.SignalDone() );

                MsgLoop.Run();

                Util.Await( t ); // in case it threw
            }
        } // end ProcessRecord()
    } // end class SetSymbolPathCommand
}

