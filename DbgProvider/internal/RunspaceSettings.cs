using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace MS.Dbg
{
    using PathStack = Dictionary< string, Stack< PathInfo > >;

    /// <summary>
    ///    Indicates different styles of path bugginess.
    /// </summary>
    /// <remarks>
    ///    Note that using a particular path bugginess style does not mean that we will exhibit the
    ///    /exact/ same set of bugs/features as that provider; only a similar set.
    ///
    ///    For instance, the Certificate provider can tolerate things like "cd Certificate::\\\\\",
    ///    but we will not, even if you are using PathBugginessStyle.CertificateProvider.
    /// </remarks>
    public enum PathBugginessStyle
    {
        /// <summary>
        ///    Implement path manipulation functions such that the provider exhibits path
        ///    manipulation bugs like the Registry provider exhibits.
        /// </summary>
        /// <remarks>
        ///    Using this style of path bugginess behavior means that tab completion for
        ///    items at the root does not work when CWD is a provider-qualified path that
        ///    is not at the root.
        ///
        ///    Repro steps:
        ///       1. cd Registry::HKEY_CURRENT_USER
        ///       -or-
        ///       1. cd Registry::\HKEY_CURRENT_USER
        ///       2. dir \HKEY_U[tab][enter]
        ///
        ///    Expected results:
        ///       Tab completion should yield "\HKEY_USERS" (and the dir command should
        ///       succeed).
        ///
        ///    Actual results:
        ///       Tab completion yields "HKEY_USERS" (and the dir command will fail).
        ///
        ///    Workaround:
        ///       Specify a provider-qualified path (like "dir Registry::HKEY_U[tab]")
        ///       -or-
        ///       Don't use provider-qualified paths.
        ///
        ///    (INT460e2df1)
        ///
        ///
        ///    There is actually another path manipulation bug which I believe I've
        ///    managed to eliminate from this provider (INT460e2dee):
        ///
        ///    Repro steps:
        ///       1. cd Registry::\        # note the trailing whack
        ///       2. dir HKEY_C[tab]
        ///       -or-
        ///       2. dir .\HKEY_C[tab]
        ///
        ///    Expected result:
        ///       Tab completion to HKEY_CLASSES_ROOT, or maybe .\HKEY_CLASSES_ROOT.
        ///
        ///    Actual results:
        ///       No tab completion.
        ///
        /// </remarks>
        RegistryProvider = 0, // the WSMan provider also uses this style

        /// <summary>
        ///    Implement path manipulation functions such that the provider exhibits path
        ///    manipulation bugs like the Certificate provider exhibits.
        /// </summary>
        /// <remarks>
        ///    Using this style of path bugginess behavior means that tab completion for
        ///    items at the root of the namespace appears wrong, but seems to work anyway.
        ///    It doesn't matter if CWD is drive-qualified or provider-qualified.
        ///
        ///    Repro steps:
        ///       1. cd Cert:\
        ///       2. dir .\Cur[tab]
        ///       -or-
        ///       2. dir Cur[tab]
        ///
        ///    Expected results:
        ///       Tab completion should yield ".\CurrentUser" or "CurrentUser".
        ///
        ///    Actual reults:
        ///       Tab completion yields ".\\CurrentUser". (despite this weirdness, the
        ///       "dir" command will succeed)
        ///
        ///    (INTe8b5a28c)
        ///
        ///    Note that the actual Certificate provider also has bugginess similar to the
        ///    Registry provider, but this provider should not when using the
        ///    PathBugginessStyle.CertificateProvider setting.
        /// </remarks>
        CertificateProvider,

        // I don't want to even contemplate simulating the weirdness of the
        // FileSystemProvider.
        //
        // A PowerShell NavigationCmdletProvider is a way to represent and manipulate a
        // virtual, hierarchical namespace, where "drives" provide "windows" or entry
        // points into this virtual namespace. The FileSystemProvider has a "hole" at the
        // root of this virtual namespace--there are no drives that map to the root of the
        // virtual namespace, and if you try to access it using a provider-qualified path,
        // you get an error. Mostly. For instance, "cd FileSystem::\" will complain it's
        // an invalid path, but "dir FileSystem::\" will show you ... some stuff
        // (INT30d2893a).
        //
        // In addition to that bugginess, the drives used by the FileSystemProvider
        // include a ":\" in their names ("C:\", "D:\", etc.). This causes all kinds of
        // difficulty. I found it impossible to implement anything like that (having a
        // drive with ":\" in the drive name) without basically copying all of the
        // /extremely/ complex path manipulation code directly from the FileSystemProvider
        // itself (c.f. NormalizeRelativePath). In fact, I suspect that the reason there
        // are so many complicated path manipulation methods in the first place is due to
        // making the FileSystemProvider work to provide paths that "look like" what you
        // might see in cmd.exe, without anything at the root of the virtual namespace.
        //
        // By not attempting to implement the same craziness as the FileSystemProvider, we
        // only have to actually implement a small subset of the path manipulation
        // functions, and for the ones we do, we only have to have a few "special cases"
        // (which determine the other bugs or "bugginess style").
        //
        //FileSystemProvider,

        // As far as I can tell, it is essentially impossible to write reasonable code
        // that exposes a plain, virtual namespace such that there are no weird path
        // manipulation bugs.
        //
        //NoBugginess <-- impossible, sorry (INT30d2892f)
    } // end PathBugginessStyle enum


    public partial class DbgProvider : NavigationCmdletProvider
    {
        internal class RunspaceSettings : ProviderInfoBase< RunspaceSettings >
        {
            public NsRoot NsRoot { get; private set; }

            public PathBugginessStyle PathBugginessStyle { get; set; }

            private Stack< NamespaceState > m_nsStack = new Stack< NamespaceState >();
            private DebuggerPSVariable m_debuggerPSVar;
            private AutoRepeatCommandPSVariable m_autoRepeatCommandPSVar;
            private ObjectPool< PowerShell > m_shellPool;


            public Collection< PSDriveInfo > InitializeDefaultDrives()
            {
                var rootDrive = new DbgRootDriveInfo( NsRoot,
                                                      this,
                                                      "Root for the DbgProvider provider." );

                var procDrive = new DbgDriveInfo( "procs",
                                                  NsRoot.EnumerateChildItems().First(),
                                                  this,
                                                  "drive for the processes directory" );

                Collection< PSDriveInfo > collection = new Collection< PSDriveInfo >();
                collection.Add( rootDrive );
                collection.Add( procDrive );
                return collection;
            } // end InitializeDefaultDrives()


            private class NamespaceState
            {
                public NsRoot NsRoot { get; private set; }
                public IEnumerable< PSDriveInfo > Drives { get; private set; }
                public PathStack LocationStack { get; private set; }
                public PathInfo WorkingDirectory { get; private set; }
                public PathBugginessStyle PathBugginessStyle { get; private set; }

                public NamespaceState( NsRoot nsRoot,
                                       IEnumerable< PSDriveInfo > drives,
                                       PathStack locationStack,
                                       PathInfo workingDirectory,
                                       PathBugginessStyle pathBugginessStyle )
                {
                    if( null == nsRoot )
                        throw new ArgumentNullException( "nsRoot" );

                    if( null == drives )
                        throw new ArgumentNullException( "drives" );

                    // Since we're using private reflection to get this, we'll tolerate it being null
                    // to gracefully handle private implementation change.
                 // if( null == locationStack )
                 //     throw new ArgumentNullException( "locationStack" );

                    if( null == workingDirectory )
                        throw new ArgumentNullException( "workingDirectory" );

                    NsRoot = nsRoot;
                    Drives = drives;
                    LocationStack = locationStack;
                    WorkingDirectory = workingDirectory;
                    PathBugginessStyle = pathBugginessStyle;
                } // end constructor
            } // end class NamespaceState


            private bool _GetLocationStackField( out FieldInfo fi, out object sessionStateInternal )
            {
                // Here is what we are going to reflect to:
                //
                //    this.SessionState.sessionState._context._topLevelSessionState.workingLocationStack
                //
                // The real trick was figuring out that I had to take the detour through the
                // ExecutionContext in order to get the _topLevelSessionState SessionStateInternal
                // object, which has the "real" workingLocationStack.

                fi = null;
                sessionStateInternal = null;
                Type t = SessionState.GetType();
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic;
                FieldInfo tmpFi = t.GetField( "sessionState", bf );
                if( null == tmpFi )
                    return false;

                sessionStateInternal = tmpFi.GetValue( SessionState );
                if( null == sessionStateInternal )
                    return false;

                t = sessionStateInternal.GetType();
                tmpFi = t.GetField( "_context", bf );
                if( null == tmpFi )
                    return false;

                object execCtx = tmpFi.GetValue( sessionStateInternal );
                if( null == execCtx )
                    return false;

                t = execCtx.GetType();
                tmpFi = t.GetField( "_topLevelSessionState", bf );
                if( null == tmpFi )
                    return false;

                sessionStateInternal = tmpFi.GetValue( execCtx );
                if( null == sessionStateInternal )
                    return false;

                t = sessionStateInternal.GetType();
                fi = t.GetField( "workingLocationStack", bf );
                return null != fi;
            } // end _GetLocationStackField()


            private PathStack _GetLocationStack()
            {
                FieldInfo fi;
                object sessionStateInternal;
                if( _GetLocationStackField( out fi, out sessionStateInternal ) )
                {
                    try
                    {
                        var retVal = (PathStack) fi.GetValue( sessionStateInternal );
                        if( null == retVal )
                        {
                            return new PathStack( StringComparer.OrdinalIgnoreCase );
                        }
                     // foreach( string stackName in retVal.Keys )
                     // {
                     //     Console.WriteLine( "   stack: {0}", stackName );
                     // }
                        return retVal;
                    }
                    catch( InvalidCastException ice )
                    {
                        Util.Fail( Util.Sprintf( "Check the type of workingLocationStack for this version of PS. ({0})",
                                                 ice ) );
                        return null;
                    }
                }
                else
                {
                    Util.Fail( "Check how to get workingLocationStack for this version of PS." );
                    return null;
                }
            } // end _GetLocationStack()


            private void _SetLocationStack( PathStack locationStack )
            {
                FieldInfo fi;
                object sessionStateInternal;
                if( _GetLocationStackField( out fi, out sessionStateInternal ) )
                {
                    try
                    {
                        fi.SetValue( sessionStateInternal, locationStack );
                    }
                    catch( ArgumentException ae )
                    {
                        Util.Fail( Util.Sprintf( "Check the type of workingLocationStack for this version of PS. ({0})",
                                                 ae ) );
                    }
                }
                else
                {
                    Util.Fail( "Check how to set workingLocationStack for this version of PS." );
                }
            } // end _SetLocationStack()


            public void PushNamespace()
            {
                TraceSource.TraceInformation( "Pushing new virtual namespace." );
                var locationStack = _GetLocationStack();
                var workingDir = SessionState.Path.CurrentLocation;

                NamespaceState currentState = new NamespaceState( NsRoot,
                                                                  Drives,
                                                                  locationStack,
                                                                  workingDir,
                                                                  PathBugginessStyle );
                m_nsStack.Push( currentState );
                PathBugginessStyle = PathBugginessStyle.RegistryProvider; // reset to default
                NsRoot = new NsRoot( currentState.NsRoot.Name );

                // Now we need to get rid of the old drives and create a fresh set.
                foreach( PSDriveInfo di in Drives )
                {
                    SessionState.Drive.Remove( di.Name, true, null ); // true: "force"
                }

                foreach( PSDriveInfo di in InitializeDefaultDrives() )
                {
                    SessionState.Drive.New( di, "global" );
                }

                if( null != locationStack ) // meaning, we likely know how to set it since we got it
                    _SetLocationStack( new PathStack( StringComparer.OrdinalIgnoreCase ) );

                SessionState.Path.SetLocation( @"Dbg:\" );
            } // end PushNamespace()


            // This function is solely for working around INT9f732f8e.
            private static string _TryEscapeWildcards( string path )
            {
                var sb = new System.Text.StringBuilder( path.Length + 4 );
                foreach( char c in path )
                {
                    if( ('[' == c) ||
                        (']' == c) ||
                        ('*' == c) ||
                        ('?' == c) )
                    {
                        sb.Append( '`' );
                    }
                    sb.Append( c );
                }
                if( sb.Length != path.Length )
                    return sb.ToString();
                else
                    return path;
            } // end _TryEscapeWildcards()


            private string _SetLocation( string path )
            {
                Exception e = null;
                try
                {
                    SessionState.Path.SetLocation( path );
                }
                catch( WildcardPatternException wpe ) { e = wpe; }
                catch( ItemNotFoundException infe ) { e = infe; }

                if( null != e )
                {
                    // INT9f732f8e: Path.SetLocation should treat the
                    // specified path as literal, but doesn't.
                    LogManager.Trace( "Initial _SetLocation failed: {0}",
                                      Util.GetExceptionMessages( e ) );

                    path = _TryEscapeWildcards( path );
                    SessionState.Path.SetLocation( path );
                }
                return path;
            } // end _SetLocation()


            public void PopNamespace()
            {
                if( 0 == m_nsStack.Count )
                    throw new InvalidOperationException( "Namespace stack is empty." );

                TraceSource.TraceInformation( "Popping last virtual namespace." );
                NamespaceState oldState = m_nsStack.Pop();
                PathBugginessStyle = oldState.PathBugginessStyle;
                NsRoot = oldState.NsRoot;

                // Now we need to get rid of the drives and restore the old ones.
                foreach( PSDriveInfo di in Drives )
                {
                    SessionState.Drive.Remove( di.Name, true, null ); // true: "force"
                }

                // Need to add this drive first, so that other drives can be added (we will
                // _TryGetItem on their roots, and for that to work, we need to have the root
                // drive in place).
                SessionState.Drive.New( NsRoot.RootDrive, "global" );
                foreach( PSDriveInfo di in oldState.Drives )
                {
                    if( di != NsRoot.RootDrive )
                        SessionState.Drive.New( di, "global" );
                }

                if( null != oldState.LocationStack )
                    _SetLocationStack( oldState.LocationStack );

                _SetLocation( oldState.WorkingDirectory.Path );
            } // end PopNamespace()


            protected override void Dispose( bool disposing )
            {
                if( disposing )
                {
                    if( null != m_debuggerPSVar )
                    {
                        SessionState.PSVariable.Remove( m_debuggerPSVar );
                        m_debuggerPSVar = null;
                    }

                    if( null != m_shellPool )
                    {
                        m_shellPool.Dispose();
                        m_shellPool = null;
                    }

                    DbgEngDebugger._GlobalDebugger.UmProcessRemoved -= _GlobalDebugger_ProcessRemoved;
                    DbgEngDebugger._GlobalDebugger.UmProcessAdded -= _GlobalDebugger_ProcessAdded;

                    DbgEngDebugger._GlobalDebugger.KmTargetRemoved -= _GlobalDebugger_KmTargetRemoved;
                    DbgEngDebugger._GlobalDebugger.KmTargetAdded -= _GlobalDebugger_KmTargetAdded;
                }
            } // end Dispose()


            private class DebuggerPSVariable : PSVariable
            {
                internal const string c_DebuggerName = "Debugger";

                private RunspaceSettings m_rs;

                internal DebuggerPSVariable( RunspaceSettings rs ) : base( c_DebuggerName )
                {
                    m_rs = rs;
                }

                public override object Value
                {
                    get
                    {
                        PathInfo pi = m_rs.SessionState.Path.CurrentProviderLocation( DbgProvider.ProviderId );
                        return DbgProvider.GetDebugger( pi.ProviderPath );
                    }
                    set
                    {
                        throw new PSInvalidOperationException( "You cannot modify this value." );
                    }
                }

                public override ScopedItemOptions Options
                {
                    get
                    {
                        return ScopedItemOptions.ReadOnly | ScopedItemOptions.AllScope;
                    }
                    set
                    {
                        throw new PSInvalidOperationException( "You cannot modify this value." );
                    }
                }
            } // end class DebuggerPSVariable


            private class RegisterPSVariable : PSVariable
            {
                private RunspaceSettings m_rs;
                private bool m_isPseudoReg;

                internal RegisterPSVariable( string name,
                                             RunspaceSettings rs )
                    : this( name, false, rs )
                {
                }

                internal RegisterPSVariable( string name,
                                             bool isPseudoRegister,
                                             RunspaceSettings rs )
                     : base( name )
                {
                    m_isPseudoReg = isPseudoRegister;
                    m_rs = rs;
                }


                private DbgRegisterSetBase _GetRegisterSet()
                {
                    PathInfo pi = m_rs.SessionState.Path.CurrentProviderLocation( DbgProvider.ProviderId );

                    var regSet = DbgProvider.GetRegisterSetForPath( pi.ProviderPath );
                    if( null == regSet )
                    {
                        throw new DbgProviderException( Util.Sprintf( "No register context for path: {0}.",
                                                                      pi.ProviderPath ),
                                                        "NoRegisterContext",
                                                        ErrorCategory.ObjectNotFound,
                                                        pi.ProviderPath );
                    }
                    return regSet;
                } // end _GetRegisterSet()


                public override object Value
                {
                    get
                    {
                        if( m_isPseudoReg )
                            return _GetRegisterSet().Pseudo[ "$" + Name ].Value;
                        else
                            return _GetRegisterSet()[ Name ].Value;
                    }
                    set
                    {
                        throw new NotImplementedException();
                    }
                }

                public override ScopedItemOptions Options
                {
                    get
                    {
                        // TODO: Should we set ScopedItemOptions.ReadOnly if the target
                        // isn't live?
                        return ScopedItemOptions.AllScope;
                    }
                    set
                    {
                        throw new PSInvalidOperationException( "You cannot modify this value." );
                    }
                }
            } // end class RegisterPSVariable


            private class AutoRepeatCommandPSVariable : PSVariable
            {
                // Must match ColorConsoleHost's c_DbgAutoRepeatCmd.
                public const string c_DbgAutoRepeatCommandVariableName = "DbgAutoRepeatCommand";

                private RunspaceSettings m_rs;
                private string m_autoRepeatCommand;

                internal AutoRepeatCommandPSVariable( RunspaceSettings rs )
                    : base( c_DbgAutoRepeatCommandVariableName )
                {
                    m_rs = rs;
                }

                public override object Value
                {
                    get { return m_autoRepeatCommand; }
                    set { m_autoRepeatCommand = (string) value; }
                }

                public override ScopedItemOptions Options
                {
                    get { return ScopedItemOptions.AllScope; }
                    set
                    {
                        throw new PSInvalidOperationException( "You cannot modify options for this value." );
                    }
                }
            } // end class AutoRepeatCommandPSVariable


            public void SetAutoRepeatCommand( string command )
            {
                m_autoRepeatCommandPSVar.Value = command;
            } // end SetAutoRepeatCommand()


            private class DbgEngAliasPSVariable : PSVariable
            {
                private DbgEngDebugger m_debugger;
                private uint m_index;

                internal DbgEngAliasPSVariable( DbgEngDebugger debugger, string aliasWithoutDollarPrefix )
                    : base( aliasWithoutDollarPrefix )
                {
                    Util.Assert( aliasWithoutDollarPrefix[ 0 ] != '$' );
                    Util.Assert( !String.IsNullOrEmpty( aliasWithoutDollarPrefix ) );
                    m_debugger = debugger;
                    m_index = UInt32.MaxValue;
                }

                public override object Value
                {
                    get
                    {
                        if( m_index != UInt32.MaxValue )
                        {
                            // Alias indexes are not stable, so we'll need to check the
                            // name.
                            var alias = m_debugger.GetTextReplacement( m_index );
                            if( (null != alias) &&
                                (0 == Util.Strcmp_OI( alias.Name, "$" + Name )) )
                            {
                                return alias.Value;
                            }

                            // Doesn't seem to be good anymore.
                            m_index = UInt32.MaxValue;
                        }

                        foreach( var a in m_debugger.EnumerateTextReplacements() )
                        {
                            if( a.Name == "$" + Name )
                            {
                                m_index = a.Index;
                                return a.Value;
                            }
                        }
                        // Maybe somebody asked for $ntsym, but we aren't attached to anything.
                        //Util.Fail( Util.Sprintf( "What happened to alias {0}", Name ) );
                        return null;
                    }
                    set
                    {
                        throw new PSInvalidOperationException( "You cannot modify this value (automatic dbgeng alias)." );
                    }
                }

                public override ScopedItemOptions Options
                {
                    get
                    {
                        return ScopedItemOptions.ReadOnly | ScopedItemOptions.AllScope;
                    }
                    set
                    {
                        throw new PSInvalidOperationException( "You cannot modify this value (automatic dbgeng alias)." );
                    }
                }
            } // end class DbgEngAliasPSVariable


            public IDisposable LeaseShell( out PowerShell shell )
            {
                return m_shellPool.Lease( out shell );
            } // end LeaseShell()


            // Normally you need to store settings per-runspace. But dbgeng restricts us
            // to one per process, and we need to get access to stuff from callback
            // threads (where we wouldn't know the runspace id), so ... here it is.
            public static RunspaceSettings Singleton { get; private set; }


            private static readonly string[] s_regVarNames = {
                "Rax",
                "Rbx",
                "Rcx",
                "Rdx",
                "Rsi",
                "Rdi",
                "Rip",
                "Rsp",
                "Rbp",
                "R8",
                "R9",
                "R10",
                "R11",
                "R12",
                "R13",
                "R14",
                "R15",
                "Iopl",
             // "Cs",
             // "Ss",
             // "Ds",
             // "Es",
             // "Fs",
             // "Gs",
                "Efl",
                "Eax",
                "Ebx",
                "Ecx",
                "Edx",
                "Esi",
                "Edi",
                "Eip",
                "Esp",
                "Ebp",
            };

            private static readonly string[] s_pseudoRegVarNames = {
                "exp",
             // "p",
                "ra",
                "thread",
                "proc",
                "teb",
                "peb",
                "tid",
                "tpid",
                "retreg",
                "retreg64",
                "ip",
                "eventip",
                "previp",
             // "t0",
             // "t1",
             // "t2",
             // "t3",
             // "t4",
             // "t5",
             // "t6",
             // "t7",
             // "t8",
             // "t9",
             // "t10",
             // "t11",
             // "t12",
             // "t13",
             // "t14",
             // "t15",
             // "t16",
             // "t17",
             // "t18",
             // "t19",
                "exentry",
                "dtid",
                "dpid",
                "dsid",
                "frame",
                "scopeip",
                "dbgtime",
                "csp",
                "callret",
                "exr_chance",
                "exr_code",
                "exr_numparams",
                "exr_param0",
                "exr_param1",
                "exr_param2",
                "extret",
                "ptrsize",
                "pagesize",

                // DbgEng reports the type of $argreg is a dbgeng-generated type. If you
                // debug into dbgeng and look at the generated type, it's a UInt8*. That
                // is pretty meaningless, though--what gets stored in there is a
                // DBG_STATUS_XXX value (like DBG_STATUS_CONTROL_C, DBG_STATUS_SYSRQ,
                // DBG_STATUS_BUGCHECK_FIRST, DBG_STATUS_FATAL, etc.) (not to be confused
                // with the DEBUG_STATUS_XXX values).
                "argreg",
            };


            // It would be nice to just dynamically build this list at runtime (by
            // enumerating "text replacements"), but some aliases can't be enumerated when
            // not attached to anything, so we'll go with a list of known aliases.
            private static readonly string[] sm_dbgengAutoAliases =
            {
                "CurrentDumpFile",
                "CurrentDumpPath",
                "CurrentDumpArchiveFile",
                "CurrentDumpArchivePath",
                "ntsym",
                "ntnsym",
                "ntwsym",
                "ntdllsym",
                "ntdllnsym",
                "ntdllwsym",
                // I don't think we need these
            //  "tmpwrite",
            //  "lowrite",
            };


            public RunspaceSettings( Guid runspaceId, ProviderInfo providerInfo, SessionState sessionState )
                : base( runspaceId, providerInfo, sessionState, "DbgProviderTracer" )
            {
                if( null == sessionState )
                    throw new ArgumentNullException( "sessionState" );

                Home = @"Dbg:\";
                NsRoot = new NsRoot( "Dbg" );
                //PathBugginessStyle = PathBugginessStyle.CertificateProvider;

                m_debuggerPSVar = new DebuggerPSVariable( this );
                SessionState.PSVariable.Set( m_debuggerPSVar );

                m_autoRepeatCommandPSVar = new AutoRepeatCommandPSVariable( this );
                SessionState.PSVariable.Set( m_autoRepeatCommandPSVar );
                SessionState.InvokeCommand.CommandNotFoundAction += _OnCommandNotFound;
             // SessionState.InvokeCommand.PreCommandLookupAction += _OnPreCommandLookup;

                foreach( string regVarName in s_regVarNames )
                {
                    SessionState.PSVariable.Set( new RegisterPSVariable( regVarName, this ) );
                }

                foreach( string pseudoRegVarName in s_pseudoRegVarNames )
                {
                    SessionState.PSVariable.Set( new RegisterPSVariable( pseudoRegVarName, true, this ) );
                }

                foreach( string alias in sm_dbgengAutoAliases )
                {
                    SessionState.PSVariable.Set( new DbgEngAliasPSVariable( DbgEngDebugger._GlobalDebugger,
                                                                            alias ) );
                }

                m_shellPool = new ObjectPool<PowerShell>(
                    () => PowerShell.Create( RunspaceMode.CurrentRunspace ),
                    ( s ) => { s.Commands.Clear(); s.Streams.ClearStreams(); } );

                if( null != Singleton )
                    throw new InvalidOperationException( "The Debugger provider does not support multiple runspaces." );

                Singleton = this;

                DbgEngDebugger._GlobalDebugger.UmProcessRemoved += _GlobalDebugger_ProcessRemoved;
                DbgEngDebugger._GlobalDebugger.UmProcessAdded += _GlobalDebugger_ProcessAdded;

                DbgEngDebugger._GlobalDebugger.KmTargetRemoved += _GlobalDebugger_KmTargetRemoved;
                DbgEngDebugger._GlobalDebugger.KmTargetAdded += _GlobalDebugger_KmTargetAdded;
            } // end constructor


            void _GlobalDebugger_ProcessRemoved( object sender, UmProcessRemovedEventArgs e )
            {
                if( e.Removed.Count > 0 )
                {
                    var toRemove = new List< NsContainer< DbgUModeProcess > >( e.Removed.Count );
                    foreach( var procToRemove in e.Removed )
                    {
                        foreach( var child in NsRoot.EnumerateChildItems() )
                        {
                            NsContainer< DbgUModeProcess > nsTarget = child as NsContainer< DbgUModeProcess >;
                            if( null == nsTarget )
                                continue;

                            if( (nsTarget.RepresentedObject.DbgEngSystemId == procToRemove.DbgEngSystemId) &&
                                (nsTarget.RepresentedObject.DbgEngProcessId == procToRemove.DbgEngProcessId) )
                            {
                                toRemove.Add( nsTarget );
                                break;
                            }
                        } // end foreach( child of NsRoot )
                    } // end foreach( procToRemove )

                    foreach( var nsTarget in toRemove )
                    {
                        nsTarget.Unlink();
                    }
                } // end if( processesRemoved.Count )
            } // end _GlobalDebugger_ProcessRemoved()


            void _GlobalDebugger_ProcessAdded( object sender, UmProcessAddedEventArgs e )
            {
                var debugger = (DbgEngDebugger) sender;
                if( e.Added.Count > 0 )
                {
                    foreach( var procToAdd in e.Added )
                    {
                        // (the constructor hooks it up to its parent)
                        NsContainer.CreateStronglyTyped( procToAdd.TargetFriendlyName, // TODO: shouldn't need to pass name
                                                         NsRoot,
                                                         procToAdd );
                    } // end foreach( procToAdd )
                } // end if( processesAdded.Count )
            } // end _GlobalDebugger_ProcessAdded()


            void _GlobalDebugger_KmTargetRemoved( object sender, KmTargetRemovedEventArgs e )
            {
                if( e.Removed.Count > 0 )
                {
                    var toRemove = new List< NsContainer< DbgKModeTarget > >( e.Removed.Count );
                    foreach( var kmTargetToRemove in e.Removed )
                    {
                        foreach( var child in NsRoot.EnumerateChildItems() )
                        {
                            NsContainer< DbgKModeTarget > nsTarget = child as NsContainer< DbgKModeTarget >;
                            if( null == nsTarget )
                                continue;

                            if( nsTarget.RepresentedObject.DbgEngSystemId == kmTargetToRemove.DbgEngSystemId )
                            {
                                toRemove.Add( nsTarget );
                                break;
                            }
                        } // end foreach( child of NsRoot )
                    } // end foreach( kmTargetToRemove )

                    foreach( var nsTarget in toRemove )
                    {
                        nsTarget.Unlink();
                    }
                } // end if( targetsRemoved.Count )
            } // end _GlobalDebugger_KmTargetRemoved()


            void _GlobalDebugger_KmTargetAdded( object sender, KmTargetAddedEventArgs e )
            {
                var debugger = (DbgEngDebugger) sender;
                if( e.Added.Count > 0 )
                {
                    foreach( var targetToAdd in e.Added )
                    {
                        // (the constructor hooks it up to its parent)
                        NsContainer.CreateStronglyTyped( targetToAdd.TargetFriendlyName, // TODO: shouldn't need to pass name
                                                         NsRoot,
                                                         targetToAdd );
                    } // end foreach( procToAdd )
                } // end if( processesAdded.Count )
            } // end _GlobalDebugger_KmTargetAdded()


         // private void _OnPreCommandLookup( object sender, CommandLookupEventArgs e )
         // {
         //     Console.WriteLine( "_OnPreCommandLookup:" );
         //     Console.WriteLine( "   {0}", e.CommandName );
         // }


            private void _OnCommandNotFound( object sender, CommandLookupEventArgs clea )
            {
                // TODO: Q: How can I fail on accidental recursion? (imagine that I generate a command,
                // where the command still doesn't exist, and matches my pattern for generating a command,
                // ad nauseum) Currently, you just get stuck in an infinite loop.
                try
                {
                    LogManager.Trace( "_OnCommandNotFound CommandOrigin: {0}", clea.CommandOrigin );
                    LogManager.Trace( "_OnCommandNotFound CommandName: {0}", clea.CommandName );

                    if( _TryHandleBangCommand( clea ) )
                        return;
                    else if( _TryHandleThreadCommand( clea ) )
                        return;
                    else if( _TryHandleBreakpointCommand( clea ) )
                        return;
                    else if( _TryHandleDebuggerEngineCommands( clea ) )
                        return;

                }
                catch( Exception e )
                {
                    // PS runtime will swallow anything coming out of here; it won't even
                    // show up in $error.
                    Util.FailFast( Util.Sprintf( "Unhandled exception in _OnCommandNotFound: {0}",
                                                 Util.GetExceptionMessages( e ) ),
                                   e );
                }
            }


            private static WildcardPattern s_dashCmdPattern = new WildcardPattern( "*?-?*",
                                                                                   WildcardOptions.Compiled );

            /// <summary>
            ///    If the command passed is not recognized, run it as a debugger command
            ///    using Invoke-DbgEng.
            /// </summary>
            private bool _TryHandleDebuggerEngineCommands( CommandLookupEventArgs clea )
            {
                // A common case: PS likes to always try prepending "get-" to whatever
                // junk you pass it.
                if( clea.CommandName.StartsWith( "get-" ) )
                    return false;

                if( s_dashCmdPattern.IsMatch( clea.CommandName ) )
                {
                    // I've never seen a windbg command with a '-' in the middle; this is
                    // likely just a stray/missing PowerShell command or typo.
                    return false;
                }

                // Notice the use of $args to "soak up" additional parameters.
                var script = Util.Sprintf( "Invoke-DbgEng -command ('{0}' + ' ' + $args)",
                                           clea.CommandName );
                clea.CommandScriptBlock = ScriptBlock.Create( script );
                return true;
            } // end _TryHandleDebuggerEngineCommands()


            private bool _TryHandleBangCommand( CommandLookupEventArgs clea )
            {
                if( clea.CommandName[ 0 ] != '!' )
                    return false;

                // TODO: BUG: when you use a comma on the command line, the value for the
                // ExtensionArgs parameter ends up being "System.Object[]". Is there a way
                // to fix that?
                var script = Util.Sprintf( "Invoke-DbgExtension -ExtensionCommand {0} -ExtensionArgs ([string]::Join( ' ', $args ))",
                                           clea.CommandName.Substring( 1 ) );

                clea.CommandScriptBlock = ScriptBlock.Create( script );

                return true;
            } // end _TryHandleBangCommand()


            private static Regex sm_threadCmdPat = new Regex( @"~\s*(?<id>\d+|\.|#|\*)?\s*(?<command>.*)\z", RegexOptions.Singleline );

            private bool _TryHandleThreadCommand( CommandLookupEventArgs clea )
            {
                var matches = sm_threadCmdPat.Matches( clea.CommandName );
                if( 0 == matches.Count )
                    return false;

                Util.Assert( 1 == matches.Count );
                var match = matches[ 0 ];
                string strId = match.Groups[ "id" ].Value;
                if( 0 == Util.Strcmp_OI( "#", strId ) )
                    strId = "'#'"; // else PS will interpret it as a comment marker.

                var script = Util.Sprintf( "~x {0} {1} @args",
                                           strId,
                                           match.Groups[ "command" ].Value );
                clea.CommandScriptBlock = ScriptBlock.Create( script );
                LogManager.Trace( "Created script: {0}", script );
                return true;
            } // end _TryHandleThreadCommand()


            //
            // These breakpoint command regexes would work pretty well, except that they
            // won't be used for scenarios with spaces or commas in the command--i.e. they
            // will work for "bd1", but not "bd1,5" or "bd1 5". PowerShell will parse
            // the command line and interpret stuff after a space as an argument to the
            // command.
            //
            // You could get those sorts of commands to work by passing them as a string
            // literal to the execution operator (& 'bd1 5'), but since the point of these
            // handlers is disturbing-fidelity preservation of the windbg command
            // experience, that's not really useful.
            //
            // Another idea is to update DbgShell.exe's input loop itself to screen for
            // these. That would be no good because it would only work for direct input
            // (scripts wouldn't work).
            //

            // This isn't /exactly/ the same as dbgeng/windbg (for example, windbg will
            // accept "bd1,*", but this won't accept both the star and the id), but it's
            // close enough.
            private const string __id = @"(?<id>\d+)";
            private const string __range = @"(?<range>(?<from>\d+)-(?<to>\d+))";
            private const string __spaceAndOrComma = @"((\s*[,]\s*)|(\s+))";
            private const string __idOrRange = "(" + __id + "|" + __range + ")";

            private const string __idOrRangeList = __idOrRange +
                                                    "(" +
                                                        __spaceAndOrComma + __idOrRange +
                                                    ")*";

            private static Regex sm_breakpointListCmdPat = new Regex(
                    @"^b(?<cmd>(l|d|e|c))\s*" + // command, and optional space
                    @"(" +
                        @"(?<all>\*)" +         // we can either have '*', or...
                        @"|" +
                        @"(" +
                            __idOrRangeList +   // ...a list of ids/ranges
                        @")" +
                    @")?\z",                    // '?': or maybe no ids at all
                    RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );

            // Note that the spaces really are optional (yes, windbg really will accept
            // "~0bpkernelbase!CreateFileW"), although I require some space that dbgeng
            // doesn't.
            //
            // The options would be a pain to do in regex, but the stuff that precedes
            // them is plenty to identify a bp command.
            private static Regex sm_breakpointSetCmdPat = new Regex(
                    @"^(~\s*(?<tid>\d*))?\s*" +     // thread id
                    @"b(?<cmd>(p|m|u))\s*" +        // command
                    @"(?<bpid>\d+)?\s+" +           // breakpoint id
                    @"(?<theRest>.*)\z",            // options, address expression, passes, command
                    RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );

            private bool _TryHandleBreakpointCommand( CommandLookupEventArgs clea )
            {
                var matches = sm_breakpointSetCmdPat.Matches( clea.CommandName );
                if( 1 == matches.Count )
                {
                    _HandleBreakpointSetCommand( matches[ 0 ], clea );
                    return true;
                }
                else
                {
                    Util.Assert( matches.Count == 0 );
                }

                matches = sm_breakpointListCmdPat.Matches( clea.CommandName );
                if( 1 == matches.Count )
                {
                    _HandleBreakpointListCommand( matches[ 0 ], clea );
                    return true;
                }
                else
                {
                    Util.Assert( matches.Count == 0 );
                }

                return false;
            } // end _TryHandleBreakpointCommand()


            private void _HandleBreakpointSetCommand( Match match, CommandLookupEventArgs clea )
            {
                string cmd     = match.Groups[ "cmd" ].Value;
                string tid     = match.Groups[ "tid" ].Value;
                string bpid    = match.Groups[ "bpid" ].Value;
                string theRest = match.Groups[ "theRest" ].Value;

                if( 0 == Util.Strcmp_OI( "p", cmd ) )
                {
                    //
                    // bp : set breakpoint
                    //
                    throw new NotImplementedException( "\"bp\" not yet implemented." );
                }
                else if( 0 == Util.Strcmp_OI( "m", cmd ) )
                {
                    //
                    // bm : set match/multiple breakpoint
                    //
                    throw new NotImplementedException( "\"bm\" not yet implemented." );
                }
                else if( 0 == Util.Strcmp_OI( "u", cmd ) )
                {
                    //
                    // bu : set unresolved breakpoint
                    //
                    throw new NotImplementedException( "\"bu\" not yet implemented." );
                }
                else
                {
                    Util.Fail( "unexpected cmd" );
                }
            }

            private void _HandleBreakpointListCommand( Match match, CommandLookupEventArgs clea )
            {
                string cmd     = match.Groups[ "cmd" ].Value;

                string ids = null;

                bool haveStar   = match.Groups[ "all"   ].Success;
                bool haveIds    = match.Groups[ "id"    ].Success;
                bool haveRanges = match.Groups[ "range" ].Success;

                if( !haveStar && (haveIds || haveRanges) )
                {
                    // You can put the ids/ranges in arbitrary order, and we want to
                    // preserve the order. To do that, we'll sort by capture index.
                    string[] items = new string[ match.Length ];

                    foreach( Capture range in match.Groups[ "range" ].Captures )
                    {
                        // Transform to PS range syntax: change "2-4" into "(2..4)":
                        items[ range.Index ] = "(" + range.Value.Replace( "-", ".." ) + ")";
                    }

                    foreach( Capture id in match.Groups[ "id" ].Captures )
                    {
                        items[ id.Index ] = id.Value;
                    }

                    StringBuilder sb = new StringBuilder( match.Length * 2 );
                    sb.Append( "@( " );

                    bool first = true;
                    foreach( string item in items )
                    {
                        if( String.IsNullOrEmpty( item ) )
                            continue;

                        if( first )
                            first = false;
                        else
                            sb.Append( ", " );

                        sb.Append( item );
                    }

                    sb.Append( ")" );
                    ids = sb.ToString();
                } // end if( ids specified )


                string script;
                if( 0 == Util.Strcmp_OI( "l", cmd ) )
                {
                    //
                    // bl : list breakpoint
                    //

                    //
                    // '*' doesn't matter for Get-DbgBreakpoint.
                    //

                    if( null == ids )
                        script = "Get-DbgBreakpoint";
                    else
                        script = ids + " | Get-DbgBreakpoint";
                }
                else if( 0 == Util.Strcmp_OI( "d", cmd ) )
                {
                    //
                    // bd : disable breakpoint
                    //
                    if( haveStar )
                        script = "Disable-DbgBreakpoint *";
                    else if( null == ids )
                        script = "Write-Warning 'No breakpoints specified; none will be disabled (use ''*'' to disable all breakpoints).'";
                    else
                        script = ids + " | Disable-DbgBreakpoint";
                }
                else if( 0 == Util.Strcmp_OI( "e", cmd ) )
                {
                    //
                    // be : enable breakpoint
                    //
                    if( haveStar )
                        script = "Enable-DbgBreakpoint *";
                    else if( null == ids )
                        script = "Write-Warning 'No breakpoints specified; none will be enabled (use ''*'' to enable all breakpoints).'";
                    else
                        script = ids + " | Enable-DbgBreakpoint";
                }
                else if( 0 == Util.Strcmp_OI( "c", cmd ) )
                {
                    //
                    // bc : clear breakpoint
                    //
                    if( haveStar )
                        script = "Remove-DbgBreakpoint *";
                    else if( null == ids )
                        script = "Write-Warning 'No breakpoints specified; none will be removed (use ''*'' to remove all breakpoints).'";
                    else
                        script = ids + " | Remove-DbgBreakpoint";
                }
                else
                {
                    Util.Fail( "unexpected cmd" );
                    script = "Write-Error 'Unexpected cmd: " + cmd + "'";
                }

                // Allow passing -Verbose:
                script = "[CmdletBinding()] param() process { " + script + " }";

                clea.CommandScriptBlock = ScriptBlock.Create( script );
                LogManager.Trace( "Created script: {0}", script );
            } // end _HandleBreakpointListCommand()


            /// <summary>
            ///    Returns the new current path. Null if we couldn't figure it out.
            /// </summary>
            public string SetLocationByDebuggerContext( DbgEngDebugger debugger )
            {
                string path = null;
                if( debugger.NoTarget )
                {
                    path = @"Dbg:\";
                    SessionState.Path.PushCurrentLocation( null );
                    SessionState.Path.SetLocation( path );
                    return path;
                }

                // TODO: this could delete our working directory... figure out if
                // I need to do anything about this.
                DbgProvider.RefreshTargetByDebugger( debugger );

                path = DbgProvider.GetPathForCurrentContext( debugger );
                if( null == path )
                {
                    //SafeWriteWarning( "Could not determine current debugger context." );
                    //WriteWarning( "Could not determine current debugger context." );
                    SessionState.InvokeCommand.InvokeScript( "Write-Warning 'Could not determine path for current debugger context.'" );
                    return path;
                }

                // TODO: write a warning if the current context does not include a thread context.

                // User could be on a different drive, so drive-qualify the path.
                path = "Dbg:" + path;

                SessionState.Path.PushCurrentLocation( null );

                _SetLocation( path );
                // Note that we're not returning the [potentially] escaped path returned
                // by _SetLocation. I'm pretty sure that's what we want... our own code
                // doesn't want to deal with weird, escaped paths; only literal paths.

                //SessionState.InvokeCommand.InvokeScript( "Write-Warning 'Testing write-warning here.'" );
                return path;
            } // end SetLocationByDebuggerContext()


            public void ForceRebuildNamespace( DbgEngDebugger debugger )
            {
                List< NsContainer > toRemove = new List< NsContainer >();
                foreach( var child in NsRoot.EnumerateChildItems() )
                {
                    if( (child is NsContainer< DbgUModeProcess >) ||
                        (child is NsContainer< DbgKModeTarget >) )
                    {
                        toRemove.Add( (NsContainer) child );
                    }
                }

                foreach( var nsTarget in toRemove )
                {
                    nsTarget.Unlink();
                }

                debugger.ForceRebuildProcessTree();

                SessionState.Path.SetLocation( @"Dbg:\" );
            } // end ForceRebuildNamespace()


            public bool IsTargetNameInUse( string targetName )
            {
                NamespaceItem nsChild;
                return NsRoot.TryFindChild( targetName, out nsChild );
            } // end IsTargetNameInUse()
        } // end class RunspaceSettings
    } // end partial class DbgProvider
}
