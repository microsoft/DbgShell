using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Management.Automation;

namespace MS.Dbg
{
    [System.Diagnostics.DebuggerDisplay( "Converter: {TypeName}" )]
    public class DbgValueScriptConverter : IDbgValueConverter
    {
        public readonly string TypeName;

        public ScriptBlock Script { get; private set; }

        internal PsContext Context { get; private set; }

        public DbgValueScriptConverter( string typeName, ScriptBlock script, bool captureContext )
        {
            if( String.IsNullOrEmpty( typeName ) )
                throw new ArgumentException( "You must supply a type name.", "typeName" );

            if( null == script )
                throw new ArgumentNullException( "script" );

            TypeName = typeName;
            Script = script;
            if( captureContext )
                Context = DbgProvider.CapturePsContext();
            else
                Context = new PsContext();
        } // end constructor


        // To deal with re-entrant conversion.
        private HashSet< ulong > m_currentlyProcessingAddresses;



        public object Convert( DbgSymbol symbol ) // TODO: plumb a CancellationToken through here.
        {
            if( null == m_currentlyProcessingAddresses )
                m_currentlyProcessingAddresses = new HashSet< ulong >();

            if( !m_currentlyProcessingAddresses.Add( symbol.Address ) ) // TODO: Do we need to worry about enregistered things?
            {
                LogManager.Trace( "Detected re-entrant conversion of type {0}; bailing out.", TypeName );
                return null;
            }

            PowerShell shell;
            var lease = DbgProvider.LeaseShell( out shell );
            try
            {
                Util.Assert( null != Context );

                Context.Vars[ "_" ] =      new PSVariable( "_",      symbol );
                Context.Vars[ "PSItem" ] = new PSVariable( "PSItem", symbol );

                // Note that StrictMode will be enforced for value converters, because
                // they execute in the scope of Debugger.psm1, which sets StrictMode.
                //
                // The problem with just calling Script.InvokeWithContext directly is that
                // non-terminating errors are hidden from us.
                shell.AddScript( @"$args[ 0 ].InvokeWithContext( $args[ 1 ].Funcs, $args[ 1 ].VarList )",
                                 true )
                     .AddArgument( Script )
                     .AddArgument( Context );

                Collection< PSObject > results = shell.Invoke();

                //  // For some reason, sometimes shell.HadErrors returns true when
                //  // shell.Streams.Error.Count is 0, when the pipeline is stopping.
                //  if( Stopping )
                //      return;

                // INT2d518c4b: shell.HadErrors is clueless.
                //if( shell.HadErrors )
                if( shell.Streams.Error.Count > 0 )
                {
                    // TODO TODO: handle more than one
                    // if( 1 == shell.Streams.Error.Count )
                    // {
                    var e = shell.Streams.Error[ 0 ];
                    // TODO: tailored exception
                    throw new DbgProviderException( Util.Sprintf( "Symbol value conversion for type name {0} failed: {1}",
                                                                  TypeName,
                                                                  Util.GetExceptionMessages( e.Exception ) ),
                                                    e );
                    // }
                    // else
                    // {
                    // }
                }

                if( 0 == results.Count )
                {
                    return null; // I guess it didn't work.
                }
                if( 1 == results.Count )
                {
                    return results[ 0 ];
                }
                else
                {
                    // TODO: Hmm... not sure what's the best thing to do here. Return just the
                    // last thing? For now I'll return the collection.
                    LogManager.Trace( "Warning: Symbol value conversion for type name {0} yielded multiple results (symbol {1}).", TypeName, symbol );
                    return results;
                }
            }
            finally
            {
                m_currentlyProcessingAddresses.Remove( symbol.Address );

                if( null != lease )
                    lease.Dispose();

                // Let's not keep the input object rooted.
                Context.Vars.Remove( "_" );
                Context.Vars.Remove( "PSItem" );
            }
        } // end Convert()
    } // end class DbgValueScriptConverter
}

