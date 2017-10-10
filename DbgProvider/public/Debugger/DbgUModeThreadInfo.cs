using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Linq;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;
using DbgEngWrapper;

namespace MS.Dbg
{

    [NsContainer( NameProperty = "DebuggerId" )]
    public class DbgUModeThreadInfo : DebuggerObject, IEquatable< DbgUModeThreadInfo >, ICanSetContext
    {
        public DbgEngContext Context { get; private set; }

        /// <summary>
        ///    The thread id that dbgeng uses.
        /// </summary>
        public uint DebuggerId { get { return (uint) Context.ThreadIndexOrAddress; } }

        /// <summary>
        ///    The OS thread id.
        /// </summary>
        public uint Tid { get; private set; }

        public string Path
        {
            get
            {
                return DbgProvider.GetPathForDebuggerContext( Context );
            }
        }

        private WDebugSystemObjects __dso;
        private WDebugSystemObjects _DSO
        {
            get
            {
                if( null == __dso )
                {
                    __dso = (WDebugSystemObjects) Debugger.DebuggerInterface;
                }
                return __dso;
            }
        } // end property _DSO


        internal DbgUModeThreadInfo( DbgEngDebugger debugger,
                                     DbgEngContext context,
                                     uint sysTid )
            : base( debugger )
        {
            if( null == context )
                throw new ArgumentNullException( "context" );

            if( (context.SystemIndex < 0) ||
                (context.ProcessIndexOrAddress < 0) ||
                (context.ThreadIndexOrAddress < 0) ||
                (context.FrameIndex != 0) )
            {
                throw new ArgumentException( "You must specify the system, process, and thread index, and a frame index of 0.", "context" );
            }

            Context = context;
            Tid = sysTid;
        } // end constructor


        private DbgStackInfo m_stack;

        [NsContainer( ChildrenMember = "EnumerateStackFrames" )]
        public DbgStackInfo Stack
        {
            get
            {
                if( null == m_stack )
                {
                    m_stack = new DbgStackInfo( this );
                }
                return m_stack;
            }
        }

        internal DbgStackInfo GetStackWithMaxFrames( int maxFrames )
        {
            if( maxFrames == 0 )
            {
                return Stack;
            }

            return new DbgStackInfo( this, maxFrames );
        }


        private ulong m_teb;

        [NsLeafItem]
        public ulong TebAddress
        {
            get
            {
                if( 0 == m_teb )
                {
                    Debugger.ExecuteOnDbgEngThread( () =>
                        {
                            using( new DbgEngContextSaver( Debugger, Context ) )
                            {
                                CheckHr( _DSO.GetCurrentThreadTeb( out m_teb ) );
                                // TODO: BUGBUG? Is this going to wipe out a frame context?
                            }
                        } );
                }
                return m_teb;
            }
        } // end property TebAddress


        private DbgSymbol __tebSym;
        [NsLeafItem]
        public dynamic Teb
        {
            get
            {
                if( null == __tebSym )
                {
                    string symName = Util.Sprintf( "Thread_0x{0}_TEB", DebuggerId );
                    using( var ctrlC = new CancelOnCtrlC() )
                    {
                        using( new DbgEngContextSaver( Debugger, Context ) )
                        {
                            try
                            {
                                __tebSym = Debugger.CreateSymbolForAddressAndType( TebAddress,
                                                                                   "ntdll!_TEB",
                                                                                   ctrlC.CTS.Token,
                                                                                   symName );
                            }
                            catch( DbgProviderException dpe )
                            {
                                throw new DbgProviderException( "Could not create symbol for TEB. Are you missing the PDB for ntdll?",
                                                                "NoTebSymbol",
                                                                System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                                dpe,
                                                                this );
                            }
                        }
                    }
                }
                return __tebSym.Value;
            }
        }


        /// <summary>
        ///    The address of a block of pointers of TLS storage ("implicit" TLS
        ///    (__declspec(thread)), as opposed to "explicit TLS" (TlsAlloc et al)). Each
        ///    pointer points to the TLS storage area for a particular DLL. It could be
        ///    null.
        ///
        ///    To find the pointer for a particular DLL, you have to dig around in the PE
        ///    header for that DLL to find the index (actually you find the address of the
        ///    index, then read the index).
        ///
        ///    Best documentation I've found for the inner workings of implicit TLS:
        ///
        ///       http://www.nynaeve.net/?tag=tls
        ///
        ///    Other good resources:
        ///
        ///       Official Windows PE/COFF spec: https://www.microsoft.com/en-us/download/confirmation.aspx?id=19509
        ///
        ///       Good SO post with lots of links: http://stackoverflow.com/questions/14538159/about-tls-callback-in-windows
        ///
        ///    (plus of course the CRT headers and source that ship with Visual Studio,
        ///    like tlssup.c et al)
        /// </summary>
        internal ulong ImplicitTlsStorage
        {
            get
            {
                //
                // We're just going to reach into the TEB and grab the 11th pointer.
                // Seems sketchy at first glance, but word is that it should be pretty
                // solid. From nynaeve:
                //
                //    Many of the details of implicit TLS are actually rather set in stone
                //    at this point, due to the fact that the compiler has been emitting
                //    code to directly access the ThreadLocalStoragePointer field in the
                //    TEB. Interestingly enough, this makes ThreadLocalStoragePointer a
                //    "guaranteed portable" part of the TEB, along with the NT_TIB header,
                //    despite the fact that the contents between the two are not defined
                //    to be portable (and are certainly not across, say, Windows 95).
                //
                return Debugger.ReadMemSlotInPointerArray( TebAddress, 11 );
            }
        }


        [NsLeafItem]
        public bool IsCurrentThread
        {
            get
            {
                uint sysId;
                var ctx = Debugger.GetCurrentThreadContext( out sysId );
                return ctx == Context;
            }
        } // end property IsCurrentThread


        [NsLeafItem]
        public bool IsEventThread
        {
            get
            {
                return this == Debugger.GetEventThread();
            }
        } // end property IsEventThread

        public void IncrementSuspendCount()
        {
            using( new DbgEngContextSaver( Debugger, Context ) )
            {
                Debugger.IncrementThreadSuspendCount( DebuggerId );
            }
        }

        public void DecrementSuspendCount()
        {
            using( new DbgEngContextSaver( Debugger, Context ) )
            {
                Debugger.DecrementThreadSuspendCount( DebuggerId );
            }
        }


        // Example:
        //
        //    .  0  Id: 1b3c.1054 Suspend: 1 Teb: 00007ff6`e80de000 Unfrozen
        //          Start: notepad!WinMainCRTStartup (00007ff6`e8f76094)
        //          Priority: 0  Priority class: 32  Affinity: f
        //
        private static Regex sm_tildeCommandRegex = new Regex(
                                @"(?<indicator>.)" +
                                @"\s*" +
                                @"(?<dbgEngThreadId>\d+)" +
                                @"\s+Id:\s+" +
                                @"(?<osPid>[0-9a-fA-F]+)\.(?<osTid>[0-9a-fA-F]+)" +
                                @"\s+Suspend:\s+" +
                                @"(?<suspendCount>\d+)" +
                                @"\s+Teb:\s+" +
                                @"(?<teb>[0-9a-fA-F]{8}(`?[0-9a-fA-F]{8})?)" +
                                @"\s+" +
                                @"(?<frozenStatus>.*)\r\n" +
                                // Second line
                                @"\s+Start:\s+" +
                                @"(?<startSymbol>.*)\(" +
                                @"(?<startAddress>[0-9a-fA-F]{8}(`?[0-9a-fA-F]{8})?)\)\r\n" +
                                // Third line
                                @"\s+Priority:\s+" +
                                @"(?<priority>[-]?\d+)" +
                                @"\s+Priority class:\s+" +
                                @"(?<priorityClass>\d+)" +
                                @"\s+Affinity:\s+" +
                                @"(?<affinity>[0-9a-fA-F]+)",
                                RegexOptions.Compiled );


        private string _RunTildeCommand( string andGetMatchItem )
        {
            StringBuilder sb = new StringBuilder( 300 );

            using( new DbgEngContextSaver( Debugger, Context ) )
            {
                // We need to execute this once as a "throwaway", because we might get a grody
                // "*** WARNING: Could not verify checksum for blah.exe" in the middle of the
                // output.
                Debugger.InvokeDbgEngCommand( Util.Sprintf( "~{0}", DebuggerId ), false );

                using( Debugger.HandleDbgEngOutput( ( s ) => sb.AppendLine( s ) ) )
                {
                    Debugger.InvokeDbgEngCommand( Util.Sprintf( "~{0}", DebuggerId ), false );
                }
            }

            // Sometimes we can't get the info:
            // Unable to get thread data for thread 0

            var str = sb.ToString();

            if( str.StartsWith( "Unable to get thread data for thread" ) )
            {
                // This can happen when mounting an image file directly.
                throw new DbgProviderException( "No thread data.",
                                                "NoThreadData",
                                                System.Management.Automation.ErrorCategory.ObjectNotFound,
                                                str );
            }

         // Console.WriteLine( "Raw output:" );
         // Console.WriteLine( sb.ToString() );
            foreach( Match match in sm_tildeCommandRegex.Matches( str ) )
            {
                Util.Assert( Tid == UInt32.Parse( match.Groups[ "osTid" ].Value, System.Globalization.NumberStyles.HexNumber ) );

             // Console.WriteLine( "Indicator: {0}", match.Groups[ "indicator" ].Value );
             // Console.WriteLine( "DbgEngThreadId: {0}", match.Groups[ "dbgEngThreadId" ].Value );
             // Console.WriteLine( "OsPid.OsTid: {0}", match.Groups[ "osPid" ].Value, match.Groups[ "osTid" ].Value );
             // Console.WriteLine( "SuspendCount: {0}", match.Groups[ "suspendCount" ].Value );
             // Console.WriteLine( "TEB: {0}", match.Groups[ "teb" ].Value );
             // Console.WriteLine( "FrozenStatus: {0}", match.Groups[ "frozenStatus" ].Value );
             // Console.WriteLine( "StartSymbol: {0}", match.Groups[ "startSymbol" ].Value );
             // Console.WriteLine( "StartAddress: {0}", match.Groups[ "startAddress" ].Value );
             // Console.WriteLine( "Priority: {0}", match.Groups[ "priority" ].Value );
             // Console.WriteLine( "PriorityClass: {0}", match.Groups[ "priorityClass" ].Value );
             // Console.WriteLine( "Affinity: {0}", match.Groups[ "affinity" ].Value );

             // m_suspendCount = Int32.Parse( match.Groups[ "suspendCount" ].Value );
             // m_frozenStatus = match.Groups[ "frozenStatus" ].Value;
             // m_startSymbol = match.Groups[ "startSymbol" ].Value;
             // m_startAddress = DbgProvider.ParseHexOrDecimalNumber( match.Groups[ "startAddress" ].Value );
             // m_priority = Int32.Parse( match.Groups[ "priority" ].Value );
             // m_priorityClass = Int32.Parse( match.Groups[ "priorityClass" ].Value );
             // m_affinity = Int32.Parse( match.Groups[ "affinity" ].Value );
                var group = match.Groups[ andGetMatchItem ];
                Util.Assert( group.Success );
             // Console.WriteLine( "requested value raw: {0}", group.Value );
                return group.Value;
            }
            throw new DbgProviderException( "The regex for parsing '~' output didn't work.",
                                            "IWishThereWasAnApiInsteadOfARegex",
                                            System.Management.Automation.ErrorCategory.ParserError,
                                            str );
        } // end _RunTildeCommand()


        // TODO: Replace these with proper API call.
        public int SuspendCount
        {
            get
            {
                return Int32.Parse( _RunTildeCommand( "suspendCount" ) );
            }
        }

        // TODO: Replace these with proper API call.
        public string FrozenStatus
        {
            get
            {
                return _RunTildeCommand( "frozenStatus" );
            }
        }

        // TODO: Replace these with proper API call.
        public string StartSymbol
        {
            get
            {
                return _RunTildeCommand( "startSymbol" );
            }
        }

        // TODO: Replace these with proper API call.
        public ulong StartAddress
        {
            get
            {
                return DbgProvider.ParseHexOrDecimalNumber( _RunTildeCommand( "startAddress" ) );
            }
        }

        // TODO: Replace these with proper API call.
        public int Priority
        {
            get
            {
                return Int32.Parse( _RunTildeCommand( "priority" ) );
            }
        }

        // TODO: Replace these with proper API call.
        public int PriorityClass
        {
            get
            {
                return Int32.Parse( _RunTildeCommand( "priorityClass" ) );
            }
        }

        // TODO: Replace these with proper API call.
        public int Affinity
        {
            get
            {
                return Int32.Parse( _RunTildeCommand( "affinity" ), System.Globalization.NumberStyles.HexNumber );
            }
        }



        //private ClrThread m_managedThread;

        //public IEnumerable< ClrThread > ManagedThread
        public IList< ClrThread > ManagedThreads
        {
            get
            {
              //if( null == m_managedThread )
              //{
                var list = new List< ClrThread >();
                    Debugger.ExecuteOnDbgEngThread( () =>
                        {
                            var target = Debugger.GetTargetForContext( Context );
                            //m_managedThread = target.ClrRuntimes
                            foreach( var dac in target.ClrRuntimes )
                            {
                                var t = dac.Threads.Where( ( x ) => x.OSThreadId == Tid ).FirstOrDefault();
                                //
                                // N.B. We filter out NativeThread threads here.
                                //
                                // https://github.com/Microsoft/clrmd/issues/54
                                if( (null != t) && !(t.GetType().Name.Contains( "NativeThread" )) )
                                    list.Add( t );
                            }
                        } );
                    return list;
              //}
              //return m_managedThread;
            }
        }

        public bool Equals( DbgUModeThreadInfo other )
        {
            if( null == other )
                return base.Equals( other );

            return Context == other.Context;
        }

        public override bool Equals( object obj )
        {
            return Equals( obj as DbgUModeThreadInfo );
        }

        public override int GetHashCode()
        {
            return Context.GetHashCode();
        }

        public static bool operator ==( DbgUModeThreadInfo t1, DbgUModeThreadInfo t2 )
        {
            if( null == (object) t1 )
                return (null == (object) t2);

            return t1.Equals( t2 );
        }

        public static bool operator !=( DbgUModeThreadInfo t1, DbgUModeThreadInfo t2 )
        {
            return !(t1 == t2);
        }

        public override string ToString()
        {
            return Util.Sprintf( "Thread {0} (tid 0x{1:x})", DebuggerId, Tid );
        }

        // This is awkward. But I don't want the interface or the method to be public.
        internal void SetContext()
        {
            ((ICanSetContext) this).SetContext();
        }

        // Explicitly implemented because I don't want the interface or the method to be public.
        void ICanSetContext.SetContext()
        {
            Debugger.SetCurrentDbgEngContext( Context );
        }
    } // end class DbgUModeThreadInfo
}
