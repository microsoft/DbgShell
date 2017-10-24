using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Set, "DbgBreakpoint",
             DefaultParameterSetName = c_Expression_ScriptBlockCommand_ParamSet )]
    [OutputType( typeof( DbgBreakpointInfo ) )]
    public class SetDbgBreakpointCommand : DbgBaseCommand
    {
        //
        // Having independently variable parameter sets is pretty annoying.
        //
        // We used to only have the "Expression" parameter (no "Address"). But the problem
        // with that is that if you passed a variable that held a ulong, you'd get the
        // .ToString() of it, which would be decimal, but would be interpreted as hex.
        // For example, "bp $rip" might get you a breakpoint at "36009009972003888", when
        // it was really supposed to be at 7fee00f8184430. So now we have a direct
        // "Address" parameter to consume that case.
        //
        // TODO: This seems like the sort of thing that the AddressTransformationAttribute
        // should be used for, but for breakpoints I'd like to always get what was
        // originally typed, to set as the breakpoint's expression.
        //

        private const string c_Expression_ScriptBlockCommand_ParamSet = "Expression_ScriptBlockCommand_ParamSet";
        private const string c_Address_ScriptBlockCommand_ParamSet = "Address_ScriptBlockCommand_ParamSet";
        private const string c_Expression_DbgEngCommand_ParamSet = "Expression_DbgEngCommand_ParamSet";
        private const string c_Address_DbgEngCommand_ParamSet = "Address_DbgEngCommand_ParamSet";


        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ParameterSetName = c_Expression_ScriptBlockCommand_ParamSet )]
        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ParameterSetName = c_Expression_DbgEngCommand_ParamSet )]
        [ValidateNotNullOrEmpty]
        public string Expression { get; set; }


        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ParameterSetName = c_Address_ScriptBlockCommand_ParamSet )]
        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ParameterSetName = c_Address_DbgEngCommand_ParamSet )]
        public ulong Address { get; set; }


        [Parameter( Mandatory = false )]
        [Alias( "1" )]
        public SwitchParameter OneShot { get; set; }

        [Parameter( Mandatory = false )]
        [Alias( "Passes" )]
        public uint PassCount { get; set; }

        [Parameter( Mandatory = false, ParameterSetName = c_Expression_ScriptBlockCommand_ParamSet )]
        [Parameter( Mandatory = false, ParameterSetName = c_Address_ScriptBlockCommand_ParamSet )]
        public ScriptBlock Command { get; set; }

        [Parameter( Mandatory = false, ParameterSetName = c_Expression_DbgEngCommand_ParamSet )]
        [Parameter( Mandatory = false, ParameterSetName = c_Address_DbgEngCommand_ParamSet )]
        public string DbgEngCommand { get; set; }


        [Parameter( Mandatory = false )]
        [ThreadTransformation]
        public DbgUModeThreadInfo MatchThread { get; set; }


        [Parameter( Mandatory = false )]
        public DEBUG_BREAKPOINT_ACCESS_TYPE DataAccessType { get; set; }


        [Parameter( Mandatory = false )]
        public uint DataSize { get; set; }


        // Supresses the warning you get if the Expression matches multiple symbols (such
        // as an inlined function with multiple sites).
        [Parameter( Mandatory = false )]
        public SwitchParameter Multiple { get; set; }


        // Probably not often a great idea, but I /have/ seen cases where a particular
        // function both existed on its own and as inlined callsites, and I only wanted to
        // break on the standalone version (It was a QueryInterface implementation).
        [Parameter( Mandatory = false )]
        public SwitchParameter NoInlineCallsites { get; set; }


        private bool _IsDataBreakpoint
        {
            get
            {
                return 0 != DataSize;
                // We should have already validated that DataAccessType is also set.
            }
        }


        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            EnsureTargetCanStep( Debugger );

            if( ((0 != DataSize) && !MyInvocation.BoundParameters.ContainsKey( "DataAccessType" )) ||
                ((0 == DataSize) && MyInvocation.BoundParameters.ContainsKey( "DataAccessType" )) )
            {
                var ae = new ArgumentException( "Data size and access type must both be specified, or neither." );
                throw ae;
            }
        }


        protected override void ProcessRecord()
        {
            ulong addr;

            if( 0 != Address )
            {
                addr = Address;
                Util.Assert( (c_Address_ScriptBlockCommand_ParamSet == this.ParameterSetName) ||
                             (c_Address_DbgEngCommand_ParamSet == this.ParameterSetName) );
                Expression = DbgProvider.FormatAddress( Address, Debugger.TargetIs32Bit, true ).ToString( false );
            }
            else
            {
                if( DbgProvider.TryParseHexOrDecimalNumber( Expression, out addr ) )
                {
                    // Oh... PowerShell might have interpreted a backtick as an escape...
                    // Let's fix it.
                    Expression = DbgProvider.FormatAddress( addr, Debugger.TargetIs32Bit, true ).ToString( false );
                }
            }

            if( 0 != addr )
            {
                _CreateBp( addr );
                return;
            }

            //
            // We have a symbolic expression. Dbgeng may or may not like it.
            //
            // For instance, if it resolves to a function that got inlined, even if
            // there is only a single match, dbgeng will complain and say "please use
            // bm instead".
            //
            // I hate that, so we're just going to try tohandle the symbol lookup
            // ourselves. Also, it seems there isn't actually a way to do a "bm"
            // breakpoint via the API.
            //
            IList< ulong > addrs = _TryResolveExpression();

            if( null != addrs )
            {
                // We may not actually have been able to resolve the expression, but it
                // wasn't obviously bad, in which case we'll have to set a deferred
                // breakpoint.
                if( 0 == addrs.Count )
                {
                    _CreateDeferredBp();
                }
                else
                {
                    foreach( ulong bpAddr in addrs )
                    {
                        Util.Assert( 0 != bpAddr );
                        _CreateBp( bpAddr );
                    }
                }
            }
        } // end ProcessRecord()


        private IList< ulong > _TryResolveExpression()
        {
            // We need to parse this here for further examination of the modName and
            // so that we'll have the offset (which isn't returned from FindSymbol).
            string modName;
            string bareSym;
            ulong offset;
            DbgProvider.ParseSymbolName( Expression, out modName, out bareSym, out offset );

            if( !WildcardPattern.ContainsWildcardCharacters( modName ) )
            {
                var mods = Debugger.GetModuleByName( modName, CancelTS.Token ).ToArray();
                if( 0 == mods.Length )
                {
                    // The module not loaded yet; it will have to become a deferred
                    // breakpoint if it's not obviously bad.

                    // DbgEng won't like wildcards for a deferred breakpoint.
                    if( WildcardPattern.ContainsWildcardCharacters( Expression ) )
                    {
                        var dpe = new DbgProviderException( Util.Sprintf( "Can't use a wildcard for a deferred bp: {0}",
                                                                          Expression ),
                                                            "BpNoWildcardsHere",
                                                            ErrorCategory.InvalidOperation,
                                                            Expression );
                        try { throw dpe; } catch { } // give it a stack
                        SafeWriteError( dpe );
                        return null;
                    }

                    return new ulong[ 0 ]; // signals that we need a deferred breakpoint
                }
            }

            GlobalSymbolCategory category;
            if( _IsDataBreakpoint )
                category = GlobalSymbolCategory.All;
            else
                category = GlobalSymbolCategory.Function;

            var syms = Debugger.FindSymbol_Enum( Expression,
                                                 category,
                                                 CancelTS.Token ).ToArray();

            int originalCount = syms.Length;

            if( NoInlineCallsites )
            {
                syms = syms.Where( ( x ) => !x.IsInlineCallsite ).ToArray();

                int diff = originalCount - syms.Length;

                if( diff != 0 )
                {
                    LogManager.Trace( "Filtered out {0} inline sites (leaving {1}).",
                                      diff,
                                      syms.Length );
                }
            }

            if( 0 == syms.Length )
            {
                DbgProviderException dpe;

                if( 0 != originalCount )
                {
                    dpe = new DbgProviderException( Util.Sprintf( "No non-inline sites found for expression: {0}",
                                                                  Expression ),
                                                    "BpCouldNotFindSymbol_NoInline",
                                                    ErrorCategory.ObjectNotFound,
                                                    Expression );
                }
                else
                {
                    dpe = new DbgProviderException( Util.Sprintf( "Could not resolve expression: {0}",
                                                                  Expression ),
                                                    "BpCouldNotFindSymbol",
                                                    ErrorCategory.ObjectNotFound,
                                                    Expression );
                }

                try { throw dpe; } catch { } // give it a stack
                SafeWriteError( dpe );
                return null;
            }

            if( syms.Length > 1 )
            {
                //
                // Did the user obviously intend to have multiple breakpoints?
                //
                if( !WildcardPattern.ContainsWildcardCharacters( modName ) &&
                    !WildcardPattern.ContainsWildcardCharacters( bareSym ) &&
                    !Multiple )
                {
                    SafeWriteWarning( "The expression matched multiple symbols. If this is intended, you can use -Multiple to suppress this warning." );
                }

                // If the user specified -Multiple, we'll assume they know what they are doing with an offset.
                if( (offset != 0) && !Multiple )
                {
                    SafeWriteWarning( "Is it safe to use an offset with an expression that matches multiple symbols?" );
                }
            }

            ulong[] addrs = new ulong[ syms.Length ];
            for( int i = 0; i < syms.Length; i++ )
            {
                addrs[ i ] = syms[ i ].Address + offset;
            }

            return addrs;
        } // end _TryResolveExpression()


        private DbgBreakpointInfo _CreateBaseBp( ulong addr )
        {
            DbgBreakpointInfo bp = Debugger.TryFindMatchingBreakpoint( Expression, addr );
            uint oldId = DebuggerObject.DEBUG_ANY_ID;
            if( null != bp )
            {
                oldId = bp.Id;
                SafeWriteWarning( "Redefining existing breakpoint {0}.", oldId );
                Debugger.RemoveBreakpoint( ref bp );
            }

            DEBUG_BREAKPOINT_TYPE bpType;
            if( _IsDataBreakpoint )
                bpType = DEBUG_BREAKPOINT_TYPE.DATA;
            else
                bpType = DEBUG_BREAKPOINT_TYPE.CODE;

            bp = Debugger.NewBreakpoint( bpType, oldId );

            return bp;
        } // end _CreateBaseBp()


        private void _CreateDeferredBp()
        {
            _CreateBp( 0 );
        }


        private void _CreateBp( ulong addr )
        {
            DbgBreakpointInfo bp = _CreateBaseBp( addr );

            if( 0 != addr )
                bp.Offset = addr;
            else
                bp.OffsetExpression = Expression;

            _SetOtherBpProperties( bp );

            //
            // DbgEng will let you set breakpoints that are not valid.
            //
            // Subsequently trying to step (after setting a bogus breakpoint) will fail.
            //
            // It would be much nicer to give an error about the breakpoint up front.
            //
            _TryValidateBp( ref bp ); // will null it out if bogus

            if( null != bp )
                SafeWriteObject( bp );
        } // end _CreateBp()


        private void _SetOtherBpProperties( DbgBreakpointInfo bp )
        {
            bp.IsEnabled = true;

            if( OneShot )
            {
                bp.IsOneShot = true;
            }

            if( 0 != PassCount )
            {
                bp.PassCount = PassCount;
            }

            if( null != Command )
            {
                bp.ScriptBlockCommand = Command;
            }
            else if( !String.IsNullOrEmpty( DbgEngCommand ) )
            {
                bp.DbgEngCommand = DbgEngCommand;
            }

            if( null != MatchThread )
            {
                bp.MatchThread = MatchThread.DebuggerId;
            }

            if( 0 != DataSize )
            {
                bp.DataAccessType = DataAccessType;
                bp.DataSize = DataSize;
            }
        }


        /// <summary>
        ///    This method is "best effort"--it probably won't screen out all possible
        ///    invalid breakpoints. But it should hopefully catch some obviously bad ones.
        /// </summary>
        private void _TryValidateBp( ref DbgBreakpointInfo bp )
        {
            bool bpIsActuallyBogus = false;

            if( bp.IsEnabled )
            {
                if( bp.IsDeferred )
                {
                    try
                    {
                        Util.Assert( !String.IsNullOrEmpty( Expression ) );
                        // TODO: What if the expression is not a symbol??
                        string mod, bareSym;
                        ulong offset;
                        DbgProvider.ParseSymbolName( Expression, out mod, out bareSym, out offset );

                        if( "*" == mod )
                        {
                            bpIsActuallyBogus = true;
                        }
                        else
                        {
                            var modInfos = Debugger.GetModuleByName( mod ).ToArray();
                            if( modInfos.Length > 0 )
                            {
                                // The module is loaded and yet the bp is deferred... the
                                // expression is no good.
                                //
                                // Or there is more than one module with that name.
                                bpIsActuallyBogus = true;
                            }
                        }
                    }
                    catch( ArgumentException ae )
                    {
                        LogManager.Trace( "Failed to parse expression as a symbol: {0}",
                                          Util.GetExceptionMessages( ae ) );
                    }
                } // end if( bp is deferred )
                else if( 0 != bp.Offset )
                {
                    // The address shouldn't be invalid, because that would indicate the
                    // bp is deferred, but we already checked for that.
                    Util.Assert( bp.Offset != DebuggerObject.InvalidAddress );

                    MEMORY_BASIC_INFORMATION64 mbi;
                    int hr = Debugger.TryQueryVirtual( bp.Offset, out mbi );
                    if( hr != 0 )
                    {
                        // iDNA targets don't yet handle QueryVirtual.
                        if( hr != DebuggerObject.E_NOTIMPL )
                        {
                            LogManager.Trace( "_TryValidateBp: TryQueryVirtual failed: {0}", Util.FormatErrorCode( hr ) );
                            bpIsActuallyBogus = true;
                        }
                    }
                    else
                    {
                        if( (mbi.Protect != PAGE.EXECUTE) &&
                            (mbi.Protect != PAGE.EXECUTE_READ) &&      // <--
                            (mbi.Protect != PAGE.EXECUTE_READWRITE) && // <-- I don't actually know what these are
                            (mbi.Protect != PAGE.EXECUTE_WRITECOPY) )  // <--
                        {
                            bpIsActuallyBogus = true;
                        }
                    }
                }
            } // end if( bp is enabled )

            if( bpIsActuallyBogus )
            {
                ulong addr = 0;
                if( (bp.Offset != 0) && (bp.Offset != DebuggerObject.InvalidAddress) )
                {
                    addr = bp.Offset;
                }

                Debugger.RemoveBreakpoint( ref bp );
                var dpe = new DbgProviderException( Util.Sprintf( "Failed to set breakpoint at {0}.",
                                                                  addr != 0 ? DbgProvider.FormatAddress( addr, Debugger.TargetIs32Bit, true ).ToString( false ) : Expression ),
                                                    "BpEnabledButDeferredAndOrBad",
                                                    ErrorCategory.InvalidArgument,
                                                    addr != 0 ? (object) addr : (object) Expression );
                try { throw dpe; } catch { } // give it a stack
                WriteError( dpe );
            }
        } // end _TryValidateBp()
    } // end class SetDbgBreakpointCommand
}
