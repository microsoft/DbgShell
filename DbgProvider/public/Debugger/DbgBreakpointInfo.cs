using System;
using System.IO;
using System.Management.Automation;
using System.Reflection;
using DbgEngWrapper;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    public class DbgBreakpointInfo : DebuggerObject
    {
        private DEBUG_BREAKPOINT_PARAMETERS m_nativeParams;
        public DEBUG_BREAKPOINT_PARAMETERS NativeParams { get { return m_nativeParams; } }

        internal const uint StepOutBreakpointId = 0x60606060;


        public uint Id { get { return m_nativeParams.Id; } }

        private bool m_invalid;

        internal void MarkInvalid()
        {
            m_invalid = true;
            // You'd think we should dispose it, right? Nope; that's asking for an AV.
            if( null != __db )
            {
                __db.AbandonInterface();
                __db = null;
            }
        }

        public bool IsValid { get { return !m_invalid; } }


        private WDebugBreakpoint __db;
        public WDebugBreakpoint Bp
        {
            get
            {
                if( m_invalid )
                    throw new InvalidOperationException( "The underlying breakpoint has been cleared." );

                if( null == __db )
                {
                    Debugger.ExecuteOnDbgEngThread( () =>
                        {
                            CheckHr( m_debugControl.GetBreakpointById2( Id, out __db ) );
                        } );
                }
                return __db;
            }
        } // end property Bp

        public Guid Guid
        {
            get
            {
                return Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        Guid g;
                        CheckHr( Bp.GetGuid( out g ) );
                        return g;
                    } );
            }
        } // end property Guid

        public bool IsEnabled
        {
            get
            {
                return m_nativeParams.Flags.HasFlag( DEBUG_BREAKPOINT_FLAG.ENABLED );
            }
            set
            {
                Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        // TODO: test this!
                        if( value )
                        {
                            CheckHr( Bp.AddFlags( DEBUG_BREAKPOINT_FLAG.ENABLED ) );
                        }
                        else
                        {
                            CheckHr( Bp.RemoveFlags( DEBUG_BREAKPOINT_FLAG.ENABLED ) );
                        }
                        Refresh();
                    } );
            }
        } // end property IsEnabled

        public bool IsDeferred
        {
            get
            {
                return m_nativeParams.Flags.HasFlag( DEBUG_BREAKPOINT_FLAG.DEFERRED );
            }
        } // end property IsDeferred

        public bool IsOneShot
        {
            get
            {
                return m_nativeParams.Flags.HasFlag( DEBUG_BREAKPOINT_FLAG.ONE_SHOT );
            }
            set
            {
                Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        if( value )
                        {
                            CheckHr( Bp.AddFlags( DEBUG_BREAKPOINT_FLAG.ONE_SHOT ) );
                        }
                        else
                        {
                            CheckHr( Bp.RemoveFlags( DEBUG_BREAKPOINT_FLAG.ONE_SHOT ) );
                        }
                        Refresh();
                    } );
            }
        } // end property IsOneShot

        /// <summary>
        ///    The [dbgeng] id of the only thread allowed to hit the breakpoint (or
        ///    DEBUG_ANY_ID if not constrained by thread id).
        /// </summary>
        public uint MatchThread
        {
            get
            {
                return m_nativeParams.MatchThread;
            }
            set
            {
                Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        CheckHr( Bp.SetMatchThreadId( value ) );
                        Refresh(); // because it's also in m_nativeParams.
                    } );
            }
        } // end property MatchThread


        public DEBUG_BREAKPOINT_FLAG Flags
        {
            get
            {
                // Strip off some dbgeng-internal flags so that we get nice output.
                return (DEBUG_BREAKPOINT_FLAG) (0x1f & (int) NativeParams.Flags);
            }
        } // end property Flags

        public string DbgEngCommand
        {
            get
            {
                return Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        string cmd;
                        CheckHr( Bp.GetCommandWide( NativeParams.CommandSize + 1, out cmd ) );
                        return cmd;
                    } );
            }
            set
            {
                Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        CheckHr( Bp.SetCommandWide( value ) );
                        Refresh();
                    } );
            }
        } // end property Command

        public string OffsetExpression
        {
            get
            {
                return Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        string oe;
                        CheckHr( Bp.GetOffsetExpressionWide( m_nativeParams.OffsetExpressionSize + 1, out oe ) );
                        return oe;
                    } );
            }
            set
            {
                Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        CheckHr( Bp.SetOffsetExpressionWide( value ) );
                        Refresh();
                    } );
            }
        } // end property OffsetExpression

        public ulong Offset
        {
            get
            {
                return m_nativeParams.Offset;
            }
            set
            {
                Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        CheckHr( Bp.SetOffset( value ) );
                        Refresh();
                    } );
            }
        } // end property Offset


        public uint PassCount
        {
            get
            {
                return Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        uint pc;
                        CheckHr( Bp.GetPassCount( out pc ) );
                        return pc;
                    } );
            }
            set
            {
                Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        CheckHr( Bp.SetPassCount( value ) );
                    } );
            }
        }

        public uint CurrentPassCount
        {
            get
            {
                return Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        uint pc;
                        CheckHr( Bp.GetCurrentPassCount( out pc ) );
                        return pc;
                    } );
            }
        }


        private static string sm_dbgShellExtPath;

        // We used to only load DbgShellExt once, but the problem with that is that
        // somebody might have unloaded it later (whether explicitly or by killing and
        // restarting the target) (and if DbgShell.exe is the host, then the static flag
        // keeping track of it would not get cleared away with appdomain unload).
        private void _LoadDbgShellExt()
        {
            if( String.IsNullOrEmpty( sm_dbgShellExtPath ) )
            {
                string dir =
                    Path.GetDirectoryName(
                        Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location ) );
                sm_dbgShellExtPath = Path.Combine( dir, "DbgShellExt.dll" );
            }

            // We don't want to print the load output since we're alredy in
            // DbgShell anyway.
            var elr = Util.Await( Debugger.AddExtensionAsync( sm_dbgShellExtPath ) );
            if( elr.hExt == 0 )
            {
                Util.Fail( "We should always be able to load DbgShellExt." );
                throw new DbgProviderException( Util.Sprintf( "Attempt to load DbgShellExt.dll failed. Path used: {0}.",
                                                              sm_dbgShellExtPath ),
                                                "FailedToAutoLoadDbgShellExt",
                                                ErrorCategory.OpenError,
                                                sm_dbgShellExtPath );
            }
        } // end _LoadDbgShellExt()


        public ScriptBlock ScriptBlockCommand
        {
            get
            {
                return Debugger.TryGetBpCommandScriptBlock( Guid );
            }
            set
            {
                _LoadDbgShellExt();
                Debugger.SetBpCommandScriptBlock( Guid, value );
                if( null != value )
                {
                    // N.B. This must match the pattern defined by
                    // DbgEngDebugger.sm_specialBpCmdPat.
                    DbgEngCommand = Util.Sprintf( "!DbgShellExt.dbgshell -Bp & ((Get-DbgBreakpoint -Guid '{0}').ScriptBlockCommand)",
                                            Guid );
                }
                else
                {
                    DbgEngCommand = null;
                }
            }
        }


        /// <summary>
        ///    Attempts to get a symbolic name for the breakpoint, even if the breakpoint
        ///    only has an address.
        /// </summary>
        public string SymbolicName
        {
            get
            {
                string expr = OffsetExpression;
                if( unchecked( (ulong) -1L ) == Offset )
                {
                    Util.Assert( !String.IsNullOrEmpty( expr ) );
                    return expr;
                }

                if( !string.IsNullOrEmpty( expr ) )
                    return expr;

                try
                {
                    ulong displacement;
                    string sym = GetNameByOffset( Offset, out displacement );
                    if( 0 == displacement )
                        return sym;
                    else
                        return sym + "+0x" + displacement.ToString( "x" );
                }
                catch( DbgProviderException dpe )
                {
                    LogManager.Trace( "Could not get name for offset {0:x16}: {1}.", Offset, dpe );
                    return "????";
                }
            }
        } // end property SymbolicName


        public DEBUG_BREAKPOINT_TYPE BreakType { get { return m_nativeParams.BreakType; } }


        public uint DataSize
        {
            get
            {
                return m_nativeParams.DataSize;
            }
            set
            {
                Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        var accessType = DataAccessType;
                        if( 0 == (uint) accessType )
                            accessType = DEBUG_BREAKPOINT_ACCESS_TYPE.READ; // not set; we'll use a dummy value

                        CheckHr( Bp.SetDataParameters( value, (uint) accessType ) );
                        Refresh();
                    } );
            }
        }

        public DEBUG_BREAKPOINT_ACCESS_TYPE DataAccessType
        {
            get
            {
                return m_nativeParams.DataAccessType;
            }
            set
            {
                Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        uint size = DataSize;
                        if( 0 == size )
                            size = 1; // not set yet; we'll use a dummy value.

                        CheckHr( Bp.SetDataParameters( size, (uint) value ) );
                        Refresh();
                    } );
            }
        }


        private WDebugControl m_debugControl;

        internal DbgBreakpointInfo( DbgEngDebugger debugger,
                                    DEBUG_BREAKPOINT_PARAMETERS nativeParams )
            : this( debugger, nativeParams, null )
        {
        }

        internal DbgBreakpointInfo( DbgEngDebugger debugger,
                                    DEBUG_BREAKPOINT_PARAMETERS nativeParams,
                                    WDebugBreakpoint db )
            : base( debugger )
        {
            m_nativeParams = nativeParams;
            m_debugControl = Debugger.ExecuteOnDbgEngThread( () =>
                {
                    return (WDebugControl) debugger.DebuggerInterface;
                } );
            __db = db;
        } // end constructor

        internal DbgBreakpointInfo( DbgEngDebugger debugger, WDebugBreakpoint db )
            : base( debugger )
        {
            if( null == db )
                throw new ArgumentNullException( "db" );

            Debugger.ExecuteOnDbgEngThread( () =>
                {
                    m_debugControl = (WDebugControl) debugger.DebuggerInterface;
                    __db = db;
                    Refresh();
                } );
        } // end constructor


        // N.B. Assumes we are on the dbgeng thread.
        internal void Refresh()
        {
            uint id;
            CheckHr( Bp.GetId( out id ) );
            DEBUG_BREAKPOINT_PARAMETERS[] bpParams;
            uint[] ids = new uint[] { id };
            int hr = m_debugControl.GetBreakpointParameters( ids, out bpParams );
            if( S_FALSE == hr )
            {
                // Uh-oh... breakpoint isn't valid anymore.
                MarkInvalid();
            }
            else
            {
                CheckHr( hr );
                m_nativeParams = bpParams[ 0 ];
            }
        } // end Refresh()

        internal void RefreshNativeParams( DEBUG_BREAKPOINT_PARAMETERS newParams )
        {
            m_nativeParams = newParams;
        } // end RefreshNativeParams()

        // TODO:
        // for now I'd rather have a nicer table formatting via ps/format.ps1xml
     // // The native format looks like this:
     // //     0 e 763ae8f8     0001 (0001)  0:**** kernel32!CreateFileW
     // public override string ToString()
     // {
     //     StringBuilder sb = new StringBuilder();
     //     sb.Append( Id
     // } // end ToString()
    } // end class DbgBreakpointInfo
}
