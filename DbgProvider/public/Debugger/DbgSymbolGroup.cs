using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interop;
using DbgEngWrapper;

namespace MS.Dbg
{
    // I think in the long run I'd like to not have this be public, but I need it for now in
    // order to experiment with stuff.
    public class DbgSymbolGroup : DebuggerObject
    {
        internal DbgLocalSymbol[] m_items;
        public IList< DbgLocalSymbol > Items
        {
            get
            {
                if( null == m_items )
                {
                    m_items = _LoadItems( null );
                }
                return m_items; // TODO: need to expose read-only
            }
        } // end property Items

        private DbgLocalSymbol[] _LoadItems( DbgLocalSymbol[] existingItems )
        {
            using( new DbgEngContextSaver( Debugger, Context ) )
            {
                return Debugger.ExecuteOnDbgEngThread( () =>
                {
                    uint numSyms;
                    CheckHr( m_symGroup.GetNumberSymbols( out numSyms ) );
                    LogManager.Trace( "There are {0} symbols in the group.", numSyms );

                    if( 0 == numSyms )
                    {
                        Util.Assert( (null == existingItems) || (0 == existingItems.Length) );
                        return new DbgLocalSymbol[ 0 ];
                    }

                    int numExisting = existingItems == null ? 0 : existingItems.Length;
                    var map = new Dictionary<SymbolIdentity, Queue<DbgLocalSymbol>>( numExisting );
                    if( null != existingItems )
                    {
                        Queue< DbgLocalSymbol > q;
                        foreach( var sym in existingItems )
                        {
                            if( !map.TryGetValue( sym.Identity, out q ) )
                            {
                                q = new Queue<DbgLocalSymbol>( 1 );
                                map.Add( sym.Identity, q );
                            }
                            q.Enqueue( sym );
                        }
                    }

                    DEBUG_SYMBOL_PARAMETERS[] symParams;
                    CheckHr( m_symGroup.GetSymbolParameters( 0, numSyms, out symParams ) );

                    try
                    {
                        DbgLocalSymbol[] symbols = new DbgLocalSymbol[ numSyms ];
                        for( uint i = 0; i < numSyms; i++ )
                        {
                            string name;
                            CheckHr( m_symGroup.GetSymbolNameWide( i, out name ) );

                            DEBUG_SYMBOL_ENTRY dse;
                            int hr = m_symGroup.GetSymbolEntryInformation( i, out dse );
                            const int E_NOINTERFACE = unchecked( (int) 0x80004002 );
                            DEBUG_SYMBOL_ENTRY? dseNullable;
                            if( E_NOINTERFACE == hr )
                            {
                                // This means "<value unavailable>".
                                hr = 0;
                                dseNullable = null;
                            }
                            else
                            {
                                dseNullable = dse;
                            }
                            CheckHr( hr );

                            var identity = new SymbolIdentity( name,
                                                               symParams[ i ],
                                                               dseNullable,
                                                               Target.Context );

                            Queue< DbgLocalSymbol > q;
                            if( map.TryGetValue( identity, out q ) )
                            {
                                var existingSym = q.Dequeue();
                                existingSym.Refresh( i, symParams[ i ], dseNullable );
                                symbols[ i ] = existingSym;
                                if( 0 == q.Count )
                                {
                                    map.Remove( identity );
                                }
                            }
                            else
                            {
                                symbols[ i ] = new DbgLocalSymbol( this,
                                                                   i,
                                                                   name,
                                                                   symParams[ i ],
                                                                   dseNullable,
                                                                   Target );
                            }
                        } // end for( each index )

                        //
                        // NOTE: There can be duplicates. In other words, if there two symbols
                        // representing pointers to the same object, and both are expanded, the
                        // 'symbols' collection will contain multiple entries for that
                        // object--dbgeng the only difference will be that they have a different
                        // parent (or "path").
                        //

                        // We should have found all the pre-existing symbols.
                        Util.Assert( 0 == map.Count );

                        DbgLocalSymbol.WireUpChildren( symbols );
                        return symbols;
                    }
                    catch( Exception e )
                    {
                        // TODO: once we get into here, we're probably in a bad state, because
                        // we may have updated some of the SymbolGroupItems, but haven't finished.
                        // Let's try to avoid further problems by tossing the bad state out.
                        m_items = null;
                        LogManager.Trace( "Error while loading symbol group items: {0}", e );
                        System.Diagnostics.Debugger.Break(); // TODO: does this work?
                        throw;
                    }
                } );
            } // end using( context )
        } // end _LoadItems()

        internal WDebugSymbolGroup m_symGroup;

        public DbgTarget Target { get; private set; }

        public DbgEngContext Context { get; private set; }

        public DbgStackFrameInfo Frame { get; private set; }


    //  internal DbgSymbolGroup( DbgEngDebugger debugger,
    //                           WDebugSymbolGroup symGroup,
    //                           DbgTarget target )
    //  context
    //      : base( debugger )
    //  {
    //      if( null == symGroup )
    //          throw new ArgumentNullException( "symGroup" );

    //      if( null == target )
    //          throw new ArgumentNullException( "target" );

    //      m_symGroup = symGroup;
    //      Target = target;
    //  } // end constructor

        public DbgSymbolGroup( DbgEngDebugger debugger,
                               DEBUG_SCOPE_GROUP scope )
            : this( debugger, scope, null, null )
        {
        }

        public DbgSymbolGroup( DbgEngDebugger debugger,
                               DEBUG_SCOPE_GROUP scope,
                               DbgStackFrameInfo frame,
                               DbgEngContext context )
            : base( debugger )
        {
            if( null == context )
                context = debugger.GetCurrentDbgEngContext();

            if( null == frame )
                frame = debugger.GetCurrentScopeFrame();

            Context = context;
            Frame = frame;

            using( new DbgEngContextSaver( debugger, context ) )
            {
                debugger.ExecuteOnDbgEngThread( () =>
                    {
                        WDebugSymbols ds5 = (WDebugSymbols) debugger.DebuggerInterface;
                        WDebugSymbolGroup symGroup;
                        CheckHr( ds5.GetScopeSymbolGroup2( scope, null, out symGroup ) );
                        m_symGroup = symGroup;
                        Target = debugger.GetCurrentTarget();
                    } );
            }
        } // end constructor

    //  /// <summary>
    //  ///    Creates an empty symbol group.
    //  /// </summary>
    //  internal DbgSymbolGroup( DbgEngDebugger debugger )
    //  context
    //      : base( debugger )
    //  {
    //      debugger.ExecuteOnDbgEngThread( () =>
    //          {
    //              WDebugSymbols ds = (WDebugSymbols) debugger.DebuggerInterface;
    //              CheckHr( ds.CreateSymbolGroup2( out m_symGroup ) );
    //              Target = debugger.GetCurrentTarget();
    //          } );
    //  } // end constructor


     // public uint AddSymbol( DbgLocalSymbol sym )
     // {
     //     return Debugger.ExecuteOnDbgEngThread( () =>
     //         {
     //             uint idx = 0;
     //             if( null != m_items )
     //                 idx = (uint) m_items.Length;

     //             int hr = m_symGroup.AddSymbolWide( sym.Name, ref idx );
     //             if( S_FALSE == hr )
     //             {
     //                 // TODO: seems like a dbgeng bug...
     //                 //   a) it should return an error, not a success code
     //                 //   b) it is returning this for things that should succeed (kernel32!CreateFileW)
     //                 //
     //                 // From my limited experience, it looks like it returns S_FALSE for things
     //                 // that are actually forwarded to another DLL--for instance,
     //                 // kernel32!CreateFileW just forwards to kernelbase!CreateFileW, and
     //                 // adding /that/ symbol to a symbol group works fine. If I knew that was
     //                 // the only case that could cause this S_FALSE, then I could have some
     //                 // sort of better error or behavior.
     //                 throw new DbgEngException( E_FAIL,
     //                                            Util.Sprintf( "Could not add symbol '{0}' to the symbol group.",
     //                                                          sym.Name ) );
     //             }
     //             CheckHr( hr );

     //             if( null == m_items )
     //             {
     //                 m_items = new DbgLocalSymbol[ 1 ] { sym };
     //             }
     //             else
     //             {
     //                 Util.Assert( idx == m_items.Length );
     //                 Array.Resize( ref m_items, (int) (idx + 1) );
     //                 m_items[ idx ] = sym;
     //             }
     //             return idx;
     //         } );
     // } // end AddSymbol()

        internal void Refresh()
        {
            m_items = _LoadItems( m_items );
        }
    } // end class DbgSymbolGroup
}

