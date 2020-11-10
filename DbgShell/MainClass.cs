//-----------------------------------------------------------------------
// <copyright file="MainClass.cs" company="Microsoft">
//    (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Text;
using System.Threading;
using MS.Dbg;


namespace MS.DbgShell
{
    class MainClass
    {
        private const string c_guestMode = "guestMode";
        private const string c_guestModeCleanup = "guestModeCleanup";
        private const string c_guestAndHostMode = "guestAndHostMode";
        private const string c_guestModeConsoleOwner = "consoleOwner";
        private const string c_guestModeShareConsole = "shareConsole";

        // About provider-qualified paths:
        //
        // Casual users of PowerShell may not know the syntax for unambiguously specifying
        // which namespace provider should be used to handle a given path. If the path is
        // not drive-qualified, then PS will just use the current namespace provider (the
        // namespace provider of the current working directory).
        //
        // Here's the problem: UNC paths don't count as "drive-qualified".
        //
        // So if your current working directory is something besides the FileSystem
        // provider (for instance, the DbgShell namespace provider), then if you try to
        // use a UNC path, things will not work. :(
        //
        // Since DbgShell provides a namespace provider, this is of special concern to us,
        // particularly if DbgShell is run from a network share, because then our root
        // drive is a UNC path. To avoid failures at runtime about paths not existing, we
        // need to use the special Provider-qualified path syntax.
        private const string c_FileSystem_PowerShellProviderPrefix = "FileSystem::";


        private static Thread sm_altMainThread;

        // Only used when in "guest mode", to shuttle the return code back from the
        // alternate thread.
        private static int sm_exitCode;


        static int Main( string[] args )
        {
            // This allows "[Console]::WriteLine( [char] 0x2026 )" to work just as well as
            // "[char] 0x2026".
            Console.OutputEncoding = Encoding.UTF8;

            //
            // We've got four possibilities:
            //
            // 1) Normal mode: somebody just ran DbgShell.exe.
            // 2) Guest mode: DbgShell is being hosted by a debugger extension
            //    (DbgShellExt.dll).
            // 3) Guest mode cleanup: the debugger extension (DbgShellExt) is being
            //    unloaded.
            // 4) GuestAndHost mode: DbgShell.exe is the host, but !DbgShellExt.dbgshell
            //    was called.
            //

            DbgProvider.EntryDepth++;

            ExceptionGuard entryPointStateDisposer = new ExceptionGuard(
                (Disposable) (() => LogManager.Trace( "Undoing entry point state changes." )) );

            entryPointStateDisposer.Protect( (Disposable) (() => DbgProvider.EntryDepth--) );

            using( entryPointStateDisposer )
            {
                if( (null != args) &&
                    (args.Length > 0) &&
                    (0 == StringComparer.Ordinal.Compare( args[ 0 ], c_guestMode )) )
                {
                    LogManager.Trace( "Main: GuestMode" );
                    //
                    // In guest mode, the entry thread is used as the DbgEng thread, and a
                    // separate thread is started to run our Main. When the user runs "exit",
                    // we don't actually exit--we "pause" the main thread, and then return
                    // from here (returning control back to the debugger (like windbg)).
                    //

                    args = args.Skip( 1 ).ToArray();

                    return _GuestModeMainWrapper( entryPointStateDisposer, args );
                }
                else if( (null != args) &&
                         (args.Length > 0) &&
                         (0 == StringComparer.Ordinal.Compare( args[ 0 ], c_guestModeCleanup )) )
                {
                    Util.Assert( args.Length == 1 ); // there shouldn't be any other args

                    LogManager.Trace( "Main: GuestModeCleanup" );

                    //
                    // We're in guest mode and the debugger extension is being unloaded--time
                    // to clean up.
                    //
                    return _GuestModeCleanup();
                }
                else if( (null != args) &&
                         (args.Length > 0) &&
                         (0 == StringComparer.Ordinal.Compare( args[ 0 ], c_guestAndHostMode )) )
                {
                    LogManager.Trace( "Main: GuestAndHostMode" );
                    //
                    // GuestAndHost mode is pretty much just like the "reentrant" case in
                    // GuestMode. Because DbgShell.exe is already running and owns "main" and
                    // everything, we don't use any of the other guest mode machinery besides
                    // the CurrentActionQueue and EntryDepth.
                    //

                    args = args.Skip( 1 ).ToArray();

                    return _GuestAndHostModeMainWrapper( entryPointStateDisposer, args );
                }
                else
                {
                    LogManager.Trace( "Main: Normal" );
                    //
                    // "Normal" (non-guest) mode.
                    //
                    return _NormalModeMainWrapper( args );
                } // end else "normal" mode

                // The stuff protected by the entryPointStateDisposer may not get disposed
                // here--it may have been transferred to a different thread via
                // entryPointStateDisposer.Transfer().
            } // end using( entryPointStateDisposer )
        } // end Main()


        private static char[] sm_delims = { ' ', '\t' };

        // We have separate argument parsing for guest mode.
        //
        // There are a few reasons:
        //    * The arguments for the standalone EXE and the arguments for guest mode
        //      don't necessarily match (we don't need -File for guest mode, we want to
        //      let people just type a command instead of having to type "-Command" first,
        //      -ReadFromStdin doesn't make sense, etc.)
        //    * The "normal mode" argument parsing happens on a code path that is only run
        //      once in guest mode, the first time !dbgshell is run. Of course it would be
        //      possible to change that, but I'm trying to minimize the amount of churn
        //      caused by introducing guest mode.
        private static string _ParseGuestModeArgs( string line,
                                                   bool firstTime,
                                                   out bool noProfile,
                                                   out bool inBreakpointCommand )
        {
            line = line.Trim();

            bool noExit = false;
            bool isEncoded = false;
            noProfile = false;
            inBreakpointCommand = false;

            StringBuilder sbCmd = new StringBuilder( line.Length * 2 );
            sbCmd.AppendLine( "try {" );

            // I think we shouldn't need "-File", because both execution operators seem to
            // work fine:
            //
            //    !dbgshell & D:\blah\foo.ps1
            //    !dbgshell . D:\blah\foo.ps1
            //

            int idx = 0;
            while( idx < line.Length )
            {
                // Eat space.
                while( (idx < line.Length) &&
                       ((line[ idx ] == ' ') || (line[ idx ] == '\t')) )
                {
                    idx++;
                }

                if( idx >= line.Length )
                    break;

                if( (line[ idx ] == '/') || (line[ idx ] == '-') )
                {
                    idx++;

                    int idxSpace = line.IndexOfAny( sm_delims, idx );

                    if( idxSpace < 0 )
                        idxSpace = line.Length;

                    string opt = line.Substring( idx, (idxSpace - idx) );

                    if( 0 == opt.Length )
                    {
                        // Just a "- "?
                        idx++;
                        continue;
                    }

                    if( "NoExit".StartsWith( opt, StringComparison.OrdinalIgnoreCase ) )
                    {
                        noExit = true;
                    }
                    else if( "NoProfile".StartsWith( opt, StringComparison.OrdinalIgnoreCase ) )
                    {
                        noProfile = true;
                    }
                    else if( "EncodedCommand".StartsWith( opt, StringComparison.OrdinalIgnoreCase ) )
                    {
                        isEncoded = true;
                        idx = idx + opt.Length;
                        break;
                    }
                    else if( "Bp".StartsWith( opt, StringComparison.OrdinalIgnoreCase ) )
                    {
                        inBreakpointCommand = true;

                        // When we hit a breakpoint, we could be on a different thread.
                        sbCmd.AppendLine( "[MS.Dbg.DbgProvider]::SetLocationByDebuggerContext()" );
                    }
                    else
                    {
                        sbCmd.AppendLine( Util.Sprintf( "Write-Error 'Unexpected option: ''-{0}''.'", opt ) );
                    }

                    idx = idx + opt.Length;
                } // end if( it's an option )
                else
                {
                    // Done with options.
                    break;
                }
            } // end while( idx < line.Length )


            if( noProfile && !firstTime )
            {
                sbCmd.AppendLine( "Write-Warning '-NoProfile only works when used with the first invocation of !dbgshell.'" );
                sbCmd.AppendLine( "Write-Warning 'You can unload and reload DbgShellExt, and then run \"!dbgshell -NoProfile\".'" );
            }

            if( isEncoded )
            {
                string encoded = line.Substring( idx ).Trim();
                if( 0 == encoded.Length )
                {
                    sbCmd.AppendLine( "Write-Warning \"Encoded command was empty.\"" );
                }
                else
                {
                    sbCmd.AppendLine( DbgProvider.DecodeString( encoded ) );
                }
            }
            else
            {
                if( idx < line.Length )
                {
                    sbCmd.AppendLine( line.Substring( idx ) );
                }
                else
                {
                    // No command implies we want the shell to stick around so we can use it
                    // interactively.
                    noExit = true;
                }
            }

            bool reentering = DbgProvider.EntryDepth > 1;

            if( !noExit && !reentering)
            {
                sbCmd.AppendLine( "exit" );
            }
            else if( noExit && reentering )
            {
                sbCmd.AppendLine( "Write-Warning 'N.B. There is already a running pipeline--you are now in a nested prompt.'" );
                sbCmd.AppendLine( "$Host.EnterNestedPrompt()" );
            }

            sbCmd.AppendLine( "} finally { }" );
            return sbCmd.ToString();
        } // end _ParseGuestModeArgs()


        private static object[] sm_noInput = new object[ 0 ];

        private static int _GuestModeMainWrapper( ExceptionGuard entryPointStateDisposer,
                                                  string[] args )
        {
            // We should have two things:
            //    args[ 0 ]: either "consoleOwner" or "shareConsole" (windbg versus ntsd, for instance)
            //    args[ 1 ]: command args should all should get passed in one big string.
            Util.Assert( 2 == args.Length );

            bool shareConsole = false;
            if( 0 == StringComparer.Ordinal.Compare( args[ 0 ], c_guestModeShareConsole ) )
            {
                shareConsole = true;
            }
            else
            {
                if( 0 != StringComparer.Ordinal.Compare( args[ 0 ], c_guestModeConsoleOwner ) )
                {
                    var msg = "Unexpected arg: I expected to see \"consoleOwner\".";
                    Util.Fail( msg );
                    throw new Exception( msg );
                }
            }

            bool firstTime = null == sm_altMainThread;
            bool alreadyRunning = DbgProvider.EntryDepth > 1;

            try
            {
                bool noProfile;
                bool inBreakpointCommand;
                string command = _ParseGuestModeArgs( args[ 1 ],
                                                      firstTime,
                                                      out noProfile,
                                                      out inBreakpointCommand );

                if( inBreakpointCommand && !DbgProvider.IsInBreakpointCommand )
                {
                    DbgProvider.IsInBreakpointCommand = true;
                    entryPointStateDisposer.Protect( (Disposable) (() => DbgProvider.IsInBreakpointCommand = false) );
                }

                if( firstTime )
                {
                    Util.Assert( !alreadyRunning );
                    ColorConsoleHost.GuestModeInjectCommand( command );

                    LogManager.Trace( "_GuestModeMainWrapper: initial entry." );
                    // This is the first time we've been run, so we need to start everything
                    // up, pretty much the same as when running as a normal EXE.
                    sm_altMainThread = new Thread( _AltMainThread );

                    DbgProvider.SetGuestMode( shareConsole );

                    string[] mainArgs = null;
                    if( noProfile )
                        mainArgs = new string[] { "-NoProfile" };

                    sm_altMainThread.Start( mainArgs );

                    // This will get signaled when things have been initialized enough to
                    // where we can start the guest mode thread activity.
                    DbgProvider.GuestModeEvent.WaitOne();
                    DbgProvider.GuestModeEvent.Reset();
                }
                else
                {
                    if( alreadyRunning )
                    {
                        // Re-entrant case: DbgShell is already running, but something called
                        // the debugger "!dbgshell" extension command (for instance, a
                        // breakpoint command).
                        LogManager.Trace( "_GuestModeMainWrapper: re-entering (entryDepth: {0}).", DbgProvider.EntryDepth );
                        if( null == DbgProvider.CurrentActionQueue )
                        {
                            Util.Fail( "DbgShell is already running, but I don't have a CurrentActionQueue to inject commands." );
                            throw new Exception( "DbgShell is already running, but I don't have a CurrentActionQueue to inject commands." );
                        }
                        var transferTicket = entryPointStateDisposer.Transfer();
                        DbgProvider.CurrentActionQueue.PostCommand( (cii) =>
                            {
                                using( transferTicket.Redeem() )
                                {
                                    LogManager.Trace( "Executing injected (reentrant) commands:" );
                                    LogManager.Trace( command.Replace( "\n", "\n > " ) );
                                    cii.InvokeScript( command,
                                                      false,
                                                      PipelineResultTypes.Error |
                                                          PipelineResultTypes.Output,
                                                      sm_noInput );
                                } // end using( new disposer )
                            } );
                        transferTicket.CommitTransfer();
                    }
                    else
                    {
                        LogManager.Trace( "_GuestModeMainWrapper: resuming." );
                        ColorConsoleHost.GuestModeInjectCommand( command );

                        // We already have an alternate main thread, which is "paused" (blocked),
                        // waiting to resume.

                        DbgProvider.GuestModeResume();
                    }
                }

                if( !alreadyRunning )
                {
                    LogManager.Trace( "_GuestModeMainWrapper: entering GuestModeGuestThreadActivity." );
                    // The guest mode thread activity is to pump the DbgEngThread (this thread is
                    // the thread that the debugger extension was called on, and we need to use
                    // this thread to interact with dbgeng).
                    //
                    // (if alreadyRunning, then we are already running the
                    // GuestModeGuestThreadActivity (it's below us on the stack))
                    DbgProvider.GuestModeGuestThreadActivity();
                }

                LogManager.Trace( "_GuestModeMainWrapper: returning {0}.",
                                  Util.FormatErrorCode( sm_exitCode ) );
                return sm_exitCode;
            }
            finally
            {
                LogManager.Trace( "_GuestModeMainWrapper: leaving." );
            }
        } // end _GuestModeMainWrapper()


        private static int _GuestModeCleanup()
        {
            if( null == sm_altMainThread )
            {
                // Nothing to do. (I guess we were never started.)
                Util.Fail( "currently not possible, therefore not tested" );
            }
            else
            {
                // We already have an alternate main thread, which is "paused" (blocked),
                // waiting to resume. Before we unblock it, we'll set this bit to let it
                // know that it should actually exit.
                DbgProvider.GuestModeTimeToExitAllTheWay = true;

                DbgProvider.GuestModeResume();

                LogManager.Trace( "Waiting for the DbgShell main thread." );
                sm_altMainThread.Join();
                LogManager.Trace( "_AltMainThread finished." );

                // Calling GuestModeFinalCleanup works even though we don't fire up the
                // DbgEngThread because the current thread has already been marked as the
                // thread that DbgEngThread's pump runs on, so it just runs synchronously.
                DbgProvider.GuestModeFinalCleanup();
            }

            return sm_exitCode;
        } // end _GuestModeCleanup()


        private static int _NormalModeMainWrapper( string[] args )
        {
            //
            // In this case we need to make sure that dbgeng.dll et al get loaded from the
            // proper location (not system32). DbgEngWrapper.dll used to load the right
            // one for us (the one right next to it), until we changed it to be a "pure"
            // .NET assembly (to workaround the appdomain/process-global problem).
            //

            string rootDir = Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location );
            string pathToDbgEng = Path.Combine( rootDir, "Debugger", "dbgeng.dll" );

            IntPtr hDbgEng = NativeMethods.LoadLibraryEx( pathToDbgEng,
                                                          IntPtr.Zero,
                                                          LoadLibraryExFlags.LOAD_WITH_ALTERED_SEARCH_PATH );
            if( IntPtr.Zero == hDbgEng )
                throw new Exception( "Could not load dbgeng.dll." );

            //
            // Let's also try to fix up the extension path, so that extensions can be found.
            //
            // First, let's add our own root dir, so that dbgshellext can be found.
            //

            StringBuilder sbNewExtPath;
            string curExtPath = Environment.GetEnvironmentVariable( "_NT_DEBUGGER_EXTENSION_PATH" );

            if( String.IsNullOrEmpty( curExtPath ) )
            {
                sbNewExtPath = new StringBuilder();
            }
            else
            {
                sbNewExtPath = new StringBuilder( curExtPath,
                                                  2 * curExtPath.Length );
                sbNewExtPath.Append( ';' );
            }

            sbNewExtPath.Append( rootDir );

            //
            // Can we even find some other extensions?
            //

            string debuggersDir = DbgProvider._TryFindDebuggersDir();
            if( null != debuggersDir )
            {
                sbNewExtPath.Append( ';' );
                sbNewExtPath.Append( Path.Combine( debuggersDir, "winxp" ) );

                sbNewExtPath.Append( ';' );
                sbNewExtPath.Append( Path.Combine( debuggersDir, "winext" ) );

                sbNewExtPath.Append( ';' );
                sbNewExtPath.Append( Path.Combine( debuggersDir, @"winext\arcade" ) );

                sbNewExtPath.Append( ';' );
                sbNewExtPath.Append( Path.Combine( debuggersDir, "pri" ) );

                sbNewExtPath.Append( ';' );
                sbNewExtPath.Append( debuggersDir );
            }

            Environment.SetEnvironmentVariable( "_NT_DEBUGGER_EXTENSION_PATH",
                                                sbNewExtPath.ToString() );

            //
            // IDEA: Hmm... maybe if we find a debuggers directory, we should load dbgeng
            // out of there? But what if they are old?
            //

            try
            {
                return MainWorker( args );
            }
            finally
            {
                NativeMethods.FreeLibrary( hDbgEng );
            }
        } // end _NormalModeMainWrapper()


        private static int _GuestAndHostModeMainWrapper( ExceptionGuard entryPointStateDisposer,
                                                         string[] args )
        {
            // We should have two things:
            //    args[ 0 ]: "shareConsole"
            //    args[ 1 ]: command args should all should get passed in one big string.
            //
            // Unlike plain guest mode, we know we are "sharing" the console (because
            // DbgShell.exe is the host!), but it's convenient for the caller to still
            // pass the console ownership arg.
            Util.Assert( 2 == args.Length );

            if( 0 != StringComparer.Ordinal.Compare( args[ 0 ], c_guestModeShareConsole ) )
            {
                var msg = "Unexpected arg: I expected to see \"shareConsole\".";
                Util.Fail( msg );
                throw new Exception( msg );
            }

            //
            // We're getting called on the dbgeng thread, which should be a background
            // thread, but the API we use from DbgShellExt.dll (RunAssembly) causes this
            // thread to get marked as non-background, so we need to put that back.
            //
            Thread.CurrentThread.IsBackground = true;

            try
            {
                // We'll always pretend it's the first time for the purpose of parsing
                // arguments--on the one hand, -NoProfile doesn't make sense (it can't
                // /ever/ make sense in guestAndHostMode; the profile was run or not
                // when DbgShell.exe started), but we don't want to warn about it.
                //
                // TODO: Is that the right decision?

                bool noProfile;
                bool inBreakpointCommand;
                string command = _ParseGuestModeArgs( args[ 1 ],
                                                      /* firstTime = */ true,
                                                      out noProfile,
                                                      out inBreakpointCommand );

                if( inBreakpointCommand && !DbgProvider.IsInBreakpointCommand )
                {
                    DbgProvider.IsInBreakpointCommand = true;
                    entryPointStateDisposer.Protect( (Disposable) (() => DbgProvider.IsInBreakpointCommand = false) );
                }

                // Re-entrant case: DbgShell is already running, but something called
                // the debugger "!dbgshell" extension command (for instance, a
                // breakpoint command).
                LogManager.Trace( "_GuestAndHostModeMainWrapper: re-entering (entryDepth: {0}).", DbgProvider.EntryDepth );
                if( null == DbgProvider.CurrentActionQueue )
                {
                    var msg = "(_GuestAndHostModeMainWrapper) DbgShell is already running, but I don't have a CurrentActionQueue to inject commands.";
                    Util.Fail( msg );
                    throw new Exception( msg );
                }
                var transferTicket = entryPointStateDisposer.Transfer();
                DbgProvider.CurrentActionQueue.PostCommand( (cii) =>
                    {
                        // TODO: BUGBUG: I need to do something here to separate CTRL+C
                        // handling for 'command' versus the cmdlet that is running the
                        // CurrentActionQueue. That is, if we post a command here that
                        // does a naked "Get-Content", it will prompt you for a path, and
                        // if you then do a CTRL+C, I want it to only cancel 'command',
                        // not the entire pipeline including the Resume-Exection command
                        // or whatever it is that is running the CurrentActionQueue.
                        //
                        // Besides just to behave as the user wants/expects, it throws a
                        // wrench in things for execution commands to get canceled--it's
                        // very important, so they do their own CTRL+C handling.
                        using( transferTicket.Redeem() )
                        {
                            LogManager.Trace( "Executing injected (reentrant) commands:" );
                            LogManager.Trace( command.Replace( "\n", "\n > " ) );
                            cii.InvokeScript( command,
                                              false,
                                              PipelineResultTypes.Error |
                                                  PipelineResultTypes.Output,
                                              sm_noInput );
                        } // end using( new disposer )
                    } );
                transferTicket.CommitTransfer();

                LogManager.Trace( "_GuestAndHostModeMainWrapper: returning {0}.",
                                  Util.FormatErrorCode( sm_exitCode ) );
                return sm_exitCode;
            }
            finally
            {
                LogManager.Trace( "_GuestAndHostModeMainWrapper: leaving." );
            }
        } // end _GuestAndHostModeMainWrapper()


        static void _AltMainThread( object obj )
        {
            string[] args = (string[]) obj;
            sm_exitCode = MainWorker( args );
        } // end _AltMainThread()


        static int MainWorker( string[] args )
        {
            try
            {
                var profileDir = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData,
                                                                          Environment.SpecialFolderOption.DoNotVerify ),
                                              "Temp",
                                              "DbgProvider",
                                              "ProfileOpt" );
                Directory.CreateDirectory( profileDir );

                System.Runtime.ProfileOptimization.SetProfileRoot( profileDir );

                System.Runtime.ProfileOptimization.StartProfile( "StartupProfileData-" +
                                                                 (DbgProvider.IsInGuestMode ? "GuestMode"
                                                                                            : "NormalMode") );
            }
            catch
            {
                Util.Fail( "SetProfileRoot/StartProfile failed" );
                // It's safe to ignore errors, the guarded code is just there to try and
                // improve startup performance.
            }

            string rootDir;
            try
            {
                rootDir = Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location );
            }
            catch( Exception e )
            {
                Console.WriteLine( "Exception! {0}", e );
                return -1;
            }

            //Console.WriteLine( "rootDir: {0}", rootDir );

            /*
            string pathToMsvcr120 = Assembly.GetExecutingAssembly().Location;
            pathToMsvcr120 = Path.GetDirectoryName( pathToMsvcr120 );
#if DEBUG
            pathToMsvcr120 = Path.Combine( pathToMsvcr120, "Debugger", "MSVCR120d.dll" );
#else
            pathToMsvcr120 = Path.Combine( pathToMsvcr120, "Debugger", "MSVCR120.dll" );
#endif

            IntPtr hMsvcr = NativeMethods.LoadLibraryEx( pathToMsvcr120,
                                                          IntPtr.Zero,
                                                          LoadLibraryExFlags.LOAD_WITH_ALTERED_SEARCH_PATH );
            if( IntPtr.Zero == hMsvcr )
                throw new Exception( "Could not load MSVCR120.dll." );
            */


            _ConfigureModulePath( rootDir );

            _RemoveMarkOfTheInternet( rootDir );

            string dbgModuleDir = Path.Combine( rootDir, "Debugger" );


            RunspaceConfiguration config = RunspaceConfiguration.Create();
            // TODO, FILE_A_PS_BUG: Why can't I set the shell ID?
            //config.ShellId = "MS.DbgShell";

            config.InitializationScripts.Append(
                    new ScriptConfigurationEntry( "BypassExecutionPolicy",
                                                  "Set-ExecutionPolicy -Scope Process Bypass -Force ; Set-StrictMode -Version Latest" ) );

            config.InitializationScripts.Append(
                    new ScriptConfigurationEntry( "ImportOurModule",
                                                  Util.Sprintf( @"Import-Module ""{0}""",
                                                                Path.Combine( dbgModuleDir, "Debugger.psd1" ) ) ) );

            config.InitializationScripts.Append(
                    new ScriptConfigurationEntry( "CreateBinDrive",
                                                  String.Format( CultureInfo.InvariantCulture,
                                                                 @"[void] (New-PSDrive Bin FileSystem ""{0}"")",
                                                                 rootDir ) ) );

            config.InitializationScripts.Append(
                    new ScriptConfigurationEntry( "SetStartLocation", "Set-Location Dbg:\\" ) );

            if( DbgProvider.IsInGuestMode )
            {
                // In guest mode, we are already attached to something, so we need to
                // build the namespace based on the existing dbgeng state.
                config.InitializationScripts.Append(
                        new ScriptConfigurationEntry( "RebuildNs", "[MS.Dbg.DbgProvider]::ForceRebuildNamespace()" ) );
            }

            // I cannot explain why, but using Update-FormatData (which is [a proxy
            // function] defined in our module instead of Invoke-Script (a C# cmdlet
            // defined in our module) subtly changes something having to do with scope or
            // something, such that the $AltListIndent variable wasn't working... until
            // all the format data was reloaded via Update-FormatData.
            var fmtScripts = _GetFmtScripts( dbgModuleDir );
            config.InitializationScripts.Append(
                    new ScriptConfigurationEntry( "LoadFmtDefinitions",
                                                  String.Format( CultureInfo.InvariantCulture,
                                                                 @"[void] (Update-FormatData ""{0}"")",
                                                                 String.Join( "\", \"", fmtScripts ) ) ) );

            // And in fact it seems that the trick to not losing our "captured contexts"
            // is that Update-AltFormatData needs to always be run in the context of the
            // Debugger.Formatting module.
            string monkeyPatched = @"function Update-AltFormatData {
    [CmdletBinding()]
    param( [Parameter( Mandatory = $false )]
           [string[]] $AppendPath = @(),

           [Parameter( Mandatory = $false )]
           [string[]] $PrependPath = @()
         )

    process
    {
        try
        {
            # This is pretty ugly. In particular, I can't find a better way to pass in
            # -Verbose. Ideally it could just be captured magically, but things like
            # <scriptblock>.GetNewClosure() don't seem to help.

            Invoke-InAlternateScope -ScriptBlock { $VerbosePreference = $args[2] ; Debugger\Update-AltFormatData -AppendPath $args[0] -PrependPath $args[1] } `
                                    -Arguments @( $AppendPath, $PrependPath, $VerbosePreference ) `
                                    -ScopingModule ((Get-Module Debugger).NestedModules | where name -eq 'Debugger.Formatting')
        }
        finally { }
    } # end 'process' block
<#
.ForwardHelpTargetName Update-AltFormatData
.ForwardHelpCategory Cmdlet
#>
}";
            config.InitializationScripts.Append(
                    new ScriptConfigurationEntry( "MonkeyPatchUpdateAltFormatData",
                                                  monkeyPatched ) );


            string typesPs1Xml = Path.Combine( dbgModuleDir, "Types.ps1xml" );

            if( File.Exists( typesPs1Xml ) ) // TODO: Remove once types.ps1xml is picked in
            {
                config.InitializationScripts.Append(
                        new ScriptConfigurationEntry( "LoadTypeAdapterStuff",
                                                      String.Format( CultureInfo.InvariantCulture,
                                                                     @"[void] (Update-TypeData ""{0}"")",
                                                                     c_FileSystem_PowerShellProviderPrefix + typesPs1Xml ) ) );
            }

            var converterScripts = Directory.GetFiles( dbgModuleDir, "Debugger.Converters.*.ps1" );
            StringBuilder loadConvertersCmd = new StringBuilder();
            foreach( var converterScript in converterScripts )
            {
                loadConvertersCmd.Append( Util.Sprintf( "; [void] (& '{0}{1}')", c_FileSystem_PowerShellProviderPrefix, converterScript ) );
            }

            string argCompleterScript = Path.Combine( dbgModuleDir, "Debugger.ArgumentCompleters.ps1" );
            loadConvertersCmd.Append( Util.Sprintf( "; [void] (& '{0}{1}')", c_FileSystem_PowerShellProviderPrefix, argCompleterScript) );

            config.InitializationScripts.Append(
                    new ScriptConfigurationEntry( "LoadConverters", loadConvertersCmd.ToString() ) );

            // TODO: wrap
            var colorBanner = new ColorString( ConsoleColor.Cyan, "Microsoft Debugger DbgShell\n" )
                    .AppendFg( ConsoleColor.DarkCyan ).Append( "Copyright (c) 2015\n\n" )
                    .AppendFg( ConsoleColor.Magenta ).Append( "Welcome.\n\n" )
                    .AppendFg( ConsoleColor.Gray ).Append( "Note that script execution policy is '" )
                    .AppendFg( ConsoleColor.Yellow ).Append( "Bypass" )
                    .AppendFg( ConsoleColor.Gray ).Append( "' for this process.\nRun '" )
                    .AppendFg( ConsoleColor.Yellow ).Append( "Get-Help" )
                    .AppendFg( ConsoleColor.Gray ).Append( " about_DbgShell_GettingStarted' to learn about DbgShell.\n" );

            if( DbgProvider.IsInGuestMode )
            {
                colorBanner
                    .AppendFg( ConsoleColor.Gray ).Append( "\nWhen you are finished here and want to return control back to the debugger, run '" )
                    .AppendFg( ConsoleColor.Yellow ).Append( "q" )
                    .AppendFg( ConsoleColor.Gray ).Append( "' or '" )
                    .AppendFg( ConsoleColor.Green ).Append( "exit" )
                    .AppendFg( ConsoleColor.Gray ).Append( "'." );
            }

            string banner = colorBanner.ToString( true );

            int rc = ColorConsoleHost.Start( config,
                                             banner,
                                             String.Empty,
                                             args );
            return rc;
        } // end MainWorker()


    //  private static bool _IsRunningFromUncPath;

        private static void _ConfigureModulePath( string myDir )
        {
            // If we're running from a UNC path, setting the module path does not really
            // work. (TODO: file a bug) We'll still do it, in case they ever fix it,
            // though.
            if( myDir.StartsWith( @"\\" ) )
            {
                LogManager.Trace( "Running from a network share." );
    //          _IsRunningFromUncPath = true;
            }

            // Apparently PowerShell.exe sets up the user module path. Huh.
            string userModules = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ),
                                               "WindowsPowerShell",
                                               "Modules" );

            string programFilesModules = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.ProgramFiles ),
                                                       "WindowsPowerShell",
                                                       "Modules" );

            string wow64path = String.Empty;
            if( Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess )
            {
                // This is to work around INT2ecf8d94, where opening an x86 powershell
                // window fails to load PSReadLine, because it's not in the x86 module
                // path.
                //
                // The actual path to PSReadLine will be something like:
                //
                //    C:\Program Files\WindowsPowerShell\Modules\PSReadline\1.1\PSReadline.psd1
                //
                wow64path = Path.PathSeparator +
                            Path.Combine( Path.GetPathRoot( Environment.SystemDirectory ),
                                          @"Program Files\WindowsPowerShell\Modules" );
            }

            string psModPath = Environment.GetEnvironmentVariable( "PSModulePath" );
            psModPath = Util.Sprintf( "{0}{1}{2}{1}{3}{1}{4}{5}",
                                      userModules,
                                      Path.PathSeparator,
                                      programFilesModules,
                                      psModPath,
                                      c_FileSystem_PowerShellProviderPrefix + myDir,
                                      wow64path );
            Environment.SetEnvironmentVariable( "PSModulePath", psModPath );
        } // end _ConfigureModulePath()


        private static void _RemoveMarkOfTheInternet( string rootDir )
        {
            // We'll do a spot check, and if it had the mark, we'll do the whole thing.
            //
            // We won't use the current binary for the spot check, because if we got
            // launched by DbgShellExt ("guest mode"), then it already removed the mark on
            // this binary. We need the mark to be removed from lots of other binaries,
            // too, though. Rather than try to maintain a list of exactly which files need
            // treatment, we'll just remove it from everything. Maybe a little expensive,
            // but it should only need to be done once.
            string spotCheckFile = Path.Combine( rootDir, "Debugger", "DbgProvider.dll" );
            if( Util.RemoveMarkOfTheInternet( spotCheckFile ) )
            {
                Util.RemoveMarkOfTheInternet( rootDir );
            }
        } // end _RemoveMarkOfTheInternet()


        private static IEnumerable<string> _GetFmtScripts( string myDir )
        {
            var fmtScripts = Directory.GetFiles( myDir, "*.psfmt" );
            // We want this one to always come first, so that other scripts can use stuff
            // from it.
            string baseDebuggerPsfmt = Path.Combine( myDir, "Debugger.psfmt" );
            //
            // For example, consider a UDT type called Foo, and you want to define a table
            // view for Foo, but you don't want it to be the default--you just want the
            // default UDT view (defined for DbgUdtValue) to be the default. In that case,
            // you can write something like this:
            //
            //    New-AltTypeFormatEntry -TypeName 'MyModule!Foo' {
            //        # We want to define a custom table format, but let the custom view for base type
            //        # DbgUdtValue take precedence. So we'll just ask for the DbgUdtValue's view and
            //        # emit it again here.
            //        $udtVdi = Get-AltTypeFormatEntry -TypeName 'MS.Dbg.DbgUdtValue' `
            //                                         -FormatInfoType ([MS.Dbg.Formatting.AltCustomViewDefinition])
            //        $udtVdi.ViewDefinitionInfo.ViewDefinition
            //
            //        New-AltTableViewDefinition -ShowIndex {
            //            New-AltColumns {
            //                ...
            //                ...
            //                ...
            //            } # End Columns
            //        } # end Table view
            //    } # end Type Windows_UI_Xaml!CDependencyObject
            yield return c_FileSystem_PowerShellProviderPrefix + baseDebuggerPsfmt;

            foreach( string fmtScript in fmtScripts )
            {
                if( 0 != Util.Strcmp_OI( fmtScript, baseDebuggerPsfmt ) )
                    yield return c_FileSystem_PowerShellProviderPrefix + fmtScript;
            }
        } // end _GetFmtScripts()
    } // end class MainClass
}

