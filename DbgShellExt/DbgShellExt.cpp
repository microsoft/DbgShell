//
// DbgShellExt.cpp : Defines the exported functions for the DLL.
//

#define INITGUID
#include "stdafx.h"
#include "DbgShellExt.h"
#include "ConsoleUtil.h"
#include "ClrHost.h"


static ConsoleUtil* g_pConsoleUtil = nullptr;
static WCHAR* g_pDbgShellExePath = nullptr;
static bool g_hostIsDbgShellExe = false;
static ClrHost* g_pClrHost = nullptr;
static volatile LONG LoadCount = 0;


EXTERN_C IMAGE_DOS_HEADER __ImageBase;

WCHAR* _GetDbgShellBinaryPath()
{
    DWORD cch;

    const DWORD cchPath = MAX_PATH + 1;
    WCHAR* path = new WCHAR[ cchPath ];

    //
    // Get the path to the currently executing module (e.g. "C:\foo\bar\DbgShellExt.dll")
    //
    cch = GetModuleFileName( reinterpret_cast<HMODULE>( &__ImageBase ), path, cchPath );
    if( 0 == cch )
    {
        wprintf( L"GetModuleFileName failed: %i\n", GetLastError() );
        RaiseFailFastException( nullptr, 0, 0 );
    }
    else if( cch == cchPath )
    {
        DWORD dwErr = GetLastError();
        if( dwErr == ERROR_INSUFFICIENT_BUFFER )
        {
            wprintf( L"We need a bigger buffer.\n" );
            RaiseFailFastException( nullptr, 0, 0 );
        }
    }

    //
    // Rewind to last '\'
    //
    WCHAR* p = path + cch - 1; // points to last character
    while( (*p != '\\') && (p > path) )
        p--;

    if( *p != '\\' )
        RaiseFailFastException( nullptr, 0, 0 );

    // Keep the '\'.
    p++;

    // Append "DbgShell.exe".
    wcscpy_s( p, cchPath - (p - path), L"DbgShell.exe" );

    return path;
} // end _GetDbgShellBinaryPath()


// Finds out if dbgshell.exe is the hosting process or not.
bool _IsHostDbgShellExe()
{
    DWORD cch;

    const DWORD cchPath = MAX_PATH + 1;
    WCHAR buf[ cchPath ] = { 0 };

    //
    // Get the path to the main module for the current process.
    //
    cch = GetModuleFileName( 0, buf, cchPath );
    if( 0 == cch )
    {
        wprintf( L"GetModuleFileName failed: %i\n", GetLastError() );
        RaiseFailFastException( nullptr, 0, 0 );
    }
    else if( cch == cchPath )
    {
        DWORD dwErr = GetLastError();
        if( dwErr == ERROR_INSUFFICIENT_BUFFER )
        {
            wprintf( L"We need a bigger buffer.\n" );
            RaiseFailFastException( nullptr, 0, 0 );
        }
    }

    // buf now contains a path; need to get just the file name.

    WCHAR* p = buf + cch - 1; // points to last character

    // Last char should not be a '\'
    if( *p == '\\' )
        RaiseFailFastException( nullptr, 0, 0 );

    // Rewind to last '\'
    while( (*p != '\\') && (p > buf) )
        p--;

    if( *p != '\\' )
        RaiseFailFastException( nullptr, 0, 0 );

    // Skip '\'
    p++;

    return (0 == _wcsicmp( L"DbgShell.exe", p )) ||
           (0 == _wcsicmp( L"DbgShell.vshost.exe", p ));
} // end _IsHostDbgShellExe()



// If you download the DbgShell files via IE (say, from an intranet site), they will be
// marked as being downloaded from the internet. This is done by sticking an alternate
// stream on the file with a particular name and particular content. It won't cause a
// problem for the standalone DbgShell.exe (you can double-click it and it will run fine),
// but for some reason if we try to run it via the hosting API (ExecuteAssembly), it will
// fail with COR_E_NOTSUPPORTED (0x80131515). There might be a way to present the proper
// "evidence" to the CLR to allow it to run anyway, but a simpler solution is to just
// remove the mark.
//
// We actually need to remove the mark on other files too, but since we can't depend on
// being run from the extension, we'll let DbgShell.exe take care of that.
void _RemoveMarkOfTheInternet( LPCWSTR path )
{
    // We could use SetFileInformationByHandle to delete the stream after we open it... or
    // we can just ask for it to be deleted when we close the handle (by using
    // FILE_FLAG_DELETE_ON_CLOSE).
    wstring altStreamPath( path );
    altStreamPath.append( L":Zone.Identifier" );
    HANDLE hAltStream = CreateFile( altStreamPath.c_str(),
                        STANDARD_RIGHTS_READ | DELETE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                        nullptr,                   // lpSecurityAttributes
                        OPEN_EXISTING,
                        FILE_FLAG_DELETE_ON_CLOSE, // dwFlagsAndAttributes
                        nullptr );                 // hTemplateFile
    if( hAltStream && (INVALID_HANDLE_VALUE != hAltStream) )
    {
        DbgPrintf( L"Removing Zone.Identifier alternate stream (\"mark of the internet\").\n" );
        BOOL bItWorked = CloseHandle( hAltStream );
        if( !bItWorked )
        {
            DWORD dwErr = GetLastError();
            DbgPrintf_Error( L"Unexpected: CloseHandle failed: %i\n", dwErr );
        }
    }
} // end _RemoveMarkOfTheInternet()


// Return value must be freed with delete[]
WCHAR* _Utf8ToUtf16( PCSTR input )
{
    int cchNeeded = MultiByteToWideChar( CP_UTF8,
                                         0,         // flags
                                         input,
                                         -1,        // cbMultiByte: -1 means "null-terminated"
                                         nullptr,
                                         0 );

    cchNeeded++;
    WCHAR* output = new WCHAR[ cchNeeded ];
    ZeroMemory( output, cchNeeded * sizeof( WCHAR ) );

    int result = MultiByteToWideChar( CP_UTF8,
                                      0,         // flags
                                      input,
                                      -1,        // cbMultiByte: -1 means "null-terminated"
                                      output,
                                      cchNeeded );

    if( 0 == result )
        RaiseFailFastException( nullptr, 0, 0 );

    return output;
} // end _Utf8ToUtf16()


// Called when the extension is loaded (".load").
HRESULT CALLBACK DebugExtensionInitialize( _Out_ PULONG Version, _Out_ PULONG Flags )
{
    // TODO: Get the version from the file.
//  *Version = DEBUG_EXTENSION_VERSION(MAJOR_VERSION, MINOR_VERSION);
    *Version = 0x01000000;
    *Flags = 0;


    HRESULT hr = S_OK;
    IDebugClient* pDebugClient = nullptr;
    PDEBUG_CONTROL7 pDebugControl = nullptr;

    hr = DebugCreate( IID_IDebugClient, reinterpret_cast< void** >( &pDebugClient ));
    if( FAILED( hr ) )
    {
        DbgPrintf_Error( L"DbgShellExt: DebugExtensionInitialize failed to create a DebugClient: %#x\n", hr );
        hr = E_FAIL;
        goto Cleanup;
    }

    hr = pDebugClient->QueryInterface( IID_IDebugControl7, reinterpret_cast< void** >( &pDebugControl ) );
    if( FAILED( hr ) )
    {
        DbgPrintf_Error( L"DbgShellExt: DebugExtensionInitialize failed to create an IDebugControl7: %#x\n", hr );
        hr = E_FAIL;
        goto Cleanup;
    }

    DWORD count = InterlockedIncrement( &LoadCount );
    if( count != 1 )
    {
        hr = pDebugControl->ControlledOutputWide( DEBUG_OUTCTL_ALL_CLIENTS,
                                                  DEBUG_OUTPUT_WARNING,
                                                  L"\nWarning: DbgShellExt is already loaded. This can happen if you load DbgShellExt by two names (eg DbgShellExt and DbgShellExt.dll)\n"
                                                  L"\nWarning: This is not harmful, but can be confusing when you try to unload\n" );
        if( FAILED( hr ) )
        {
            wprintf( L"Unexpected: ControlledOutputWide failed: %#x\n", hr );
            DbgPrintf_Error( L"DbgShellExt: Unexpected: ControlledOutputWide failed: %#x\n", hr );
            // ignore the error
        }

        hr = S_OK;
        goto Cleanup;
    }


    hr = pDebugControl->ControlledOutputWide( DEBUG_OUTCTL_DML | DEBUG_OUTCTL_ALL_CLIENTS, // DEBUG_OUTCTL_THIS_CLIENT,
                                              DEBUG_OUTPUT_NORMAL,
                                              L"\nRun <link cmd=\"!dbgshell\">!dbgshell</link> to pop open a DbgShell.\n\n"
                                              L"When you are done, you can run \"exit\" or \"q\" in the DbgShell to return here.\n\n" );
    if( FAILED( hr ) )
    {
        DbgPrintf_Error( L"DbgShellExt: Unexpected: ControlledOutputWide failed: %#x\n", hr );
        return hr;
    }

Cleanup:

    if( pDebugControl )
    {
        pDebugControl->Release();
        pDebugControl = nullptr;
    }

    if( pDebugClient )
    {
        pDebugClient->Release();
        pDebugClient = nullptr;
    }

    return hr;
} // end DebugExtensionInitialize()


// Called when the extension is unloaded (".unload").
void CALLBACK DebugExtensionUninitialize()
{
    if( g_pClrHost )
    {
        if( !g_hostIsDbgShellExe )
        {
            HRESULT hr = g_pClrHost->RunAssembly( 1, L"guestModeCleanup" );
            if( FAILED( hr ) )
            {
                wprintf( L"Warning: guestModeCleanup failed: %#x\n", hr );
            }
        }

        delete g_pClrHost; // this will unload the appdomain
        delete g_pDbgShellExePath;
        g_pClrHost = nullptr;
        g_pDbgShellExePath = nullptr;
    }

 // wprintf( L"Check output\n" );
 // getchar();

    if( g_pConsoleUtil )
    {
        delete g_pConsoleUtil; // this will free the console if we alloc'ed it.
        g_pConsoleUtil = nullptr;
    }
} // end DebugExtensionUninitialize()


void CALLBACK DebugExtensionNotify( _In_ ULONG Notify,
                                                    _In_ ULONG64 Argument )
{
    //wprintf( L"DebugExtensionNotify: I'm getting notified... (%#x, %#I64x)\n", Notify, Argument );
}


// Called when the extension is first loaded.
HRESULT CALLBACK DebugExtensionQueryValueNames( _In_  PDEBUG_CLIENT Client,
                                                                _In_  ULONG Flags,
                                                                _Out_ PWSTR Buffer,
                                                                _In_  ULONG BufferChars,
                                                                _Out_ PULONG BufferNeeded )
{
    ZeroMemory( Buffer, BufferChars * sizeof( Buffer[ 0 ] ) );
    *BufferNeeded = 0;

    return S_OK;
}


// Called when the extension is first loaded /if/ DebugExtensionQueryValueNames returned
// any names.
HRESULT CALLBACK DebugExtensionProvideValue( _In_  PDEBUG_CLIENT Client,
                                                             _In_  ULONG Flags,
                                                             _In_  PCWSTR Name,
                                                             _Out_ PULONG64 Value,
                                                             _Out_ PULONG64 TypeModBase,
                                                             _Out_ PULONG TypeId,
                                                             _Out_ PULONG TypeFlags )
{
    wprintf( L"ProvideValue %s\n", Name );
    return S_OK;
}


IDebugClient* g_pCurClient = nullptr;
ULONG g_originalOutputMask = 0;
int g_entranceCount = 0;


HRESULT CALLBACK internal_SwapMask( IDebugClient* debugClient, PCSTR args )
{
    HRESULT hr = S_OK;

    if( !g_pCurClient )
    {
        PDEBUG_CONTROL7 pDebugControl = nullptr;

        hr = debugClient->QueryInterface( IID_IDebugControl7, reinterpret_cast< void** >( &pDebugControl ) );
        if( FAILED( hr ) )
        {
            DbgPrintf_Error( L"DbgShellExt: '!internal_SwapMask' failed to create an IDebugControl7: %#x\n", hr );
            return S_OK;
        }

        hr = pDebugControl->ControlledOutputWide( DEBUG_OUTCTL_ALL_CLIENTS,
                                                  DEBUG_OUTPUT_ERROR,
                                                  L"\nError: !internal_SwapMask is for internal use of !dbgshell only.\n" );
        if( FAILED( hr ) )
        {
            wprintf( L"Unexpected: ControlledOutputWide failed: %#x\n", hr );
            DbgPrintf_Error( L"DbgShellExt: Unexpected: ControlledOutputWide failed: %#x\n", hr );
        }
        return S_OK;
    }

    // Interestingly, our debugClient is different than g_pCurClient.

    ULONG originalOutputMask = 0;
    hr = g_pCurClient->GetOutputMask( &originalOutputMask );
    if( FAILED( hr ) )
    {
        wprintf( L"DbgShellExt: Unexpected: failed to get output mask: %#x\n", hr );
        return S_OK;
    }

    //wprintf( L"Setting output mask to: %#x\n", g_originalOutputMask );
    hr = g_pCurClient->SetOutputMask( g_originalOutputMask );
    if( FAILED( hr ) )
    {
        wprintf( L"DbgShellExt: Unexpected: failed to zero out the output mask: %#x\n", hr );
        return S_OK;
    }

    g_originalOutputMask = originalOutputMask;
    return S_OK;
} // end !internal_SwapMask extension command


HRESULT help_worker( IDebugClient* debugClient, bool chainToOtherHelpCommands );


int IgnoreDebugBreakFilter( EXCEPTION_POINTERS* pEp )
{
    wprintf( L"In exception filter. ExceptionCode: %#x Flags: %#x\n",
             pEp->ExceptionRecord->ExceptionCode,
             pEp->ExceptionRecord->ExceptionFlags );

    if( (pEp->ExceptionRecord->ExceptionCode == STATUS_BREAKPOINT) ||
        (pEp->ExceptionRecord->ExceptionCode == STATUS_ASSERTION_FAILURE) )
    {
        return EXCEPTION_CONTINUE_EXECUTION;
    }

    return EXCEPTION_EXECUTE_HANDLER;
}


HRESULT SehWrapper( IDebugClient* debugClient, WCHAR* widenedArgs )
{
    HRESULT hr = S_OK;

    __try
    {
        hr = g_pClrHost->RunAssembly( 3,
                                      g_hostIsDbgShellExe ? L"guestAndHostMode" : L"guestMode",
                                      g_pConsoleUtil->DidWeAllocateANewConsole() ? L"consoleOwner" : L"shareConsole",
                                      widenedArgs );
    }
    __except( IgnoreDebugBreakFilter( GetExceptionInformation() ) )
    {
        wprintf( L"DbgShellExt: Unexpected: SEH exception. failed: %#x\n", hr );

        // This is awkward...
        debugClient->SetOutputMask( g_originalOutputMask );

        PDEBUG_CONTROL7 pDebugControl = nullptr;

        hr = debugClient->QueryInterface( IID_IDebugControl7, reinterpret_cast< void** >( &pDebugControl ) );
        if( FAILED( hr ) )
        {
            wprintf( L"DbgShellExt: '!dbshell exception handler' failed to create an IDebugControl7: %#x\n", hr );
        }
        else
        {
            hr = pDebugControl->ControlledOutputWide( DEBUG_OUTCTL_ALL_CLIENTS,
                DEBUG_OUTPUT_WARNING,
                L"\nWarning: DbgShellExt experienced an unhandled exception.\n"
                L"\nWarning: You are probably hosed. Sorry.\n" );
            if( FAILED( hr ) )
            {
                wprintf( L"Unexpected: ControlledOutputWide failed: %#x\n", hr );
                DbgPrintf_Error( L"DbgShellExt: Unexpected: ControlledOutputWide failed: %#x\n", hr );
                // ignore the error
            }

            pDebugControl->Release();
        }

        g_pClrHost->CallInEmergency();
    }

    return hr;
}


// The "!dbgshell" extension command. The debugger knows about it by virtue of it being
// exported.
HRESULT CALLBACK dbgshell( IDebugClient* debugClient, PCSTR args )
{
    HRESULT hr = S_OK;
    bool firstTime = false;
    WCHAR* widenedArgs = nullptr;
    ULONG originalOutputMask = 0;

    if( args &&
        ((0 == strcmp( args, "-?" )) ||
         (0 == strcmp( args, "/?" ))) )
    {
        return help_worker( debugClient, /* chainToOtherHelpCommands = */ false );
    }

    g_entranceCount++;

    HWND hwndOriginal = GetForegroundWindow();

    if( !g_pConsoleUtil )
    {
        firstTime = true;
        g_pConsoleUtil = new ConsoleUtil(); // will attempt to AllocConsole if we don't already have one
    }

    HWND hwnd = nullptr;

    if( g_pConsoleUtil->DidWeAllocateANewConsole() )
    {
        // We're being hosted in some sort of GUI where we had to allocate our own
        // console. We'll want to handle showing and hiding the console window as we are
        // activated and de-activated.
        hwnd = GetConsoleWindow();

        //
        // Let's bring the DbgShell window to the front (it happens automatically when the
        // conhost window is first created, but we don't know if we were the ones to do
        // that, and we might be getting re-activated (running !dbgshell a second time).
        //
        SwitchToThisWindow( hwnd, TRUE );
    }

    // In the shared-console case (like ntsd), we don't want debugger output all mixed in
    // with our output (e.g. modload events). This seems to do the trick.
    //
    // For the GUI debugger case (where DbgShell "owns" the console), it doesn't matter,
    // but it shouldn't hurt.
    hr = debugClient->GetOutputMask( &originalOutputMask );
    if( FAILED( hr ) )
    {
        wprintf( L"DbgShellExt: Unexpected: failed to get output mask: %#x\n", hr );
        hr = S_OK; // ignore it
    }

    hr = debugClient->SetOutputMask( 0 );
    if( FAILED( hr ) )
    {
        wprintf( L"DbgShellExt: Unexpected: failed to zero out the output mask: %#x\n", hr );
        hr = S_OK; // ignore it
    }

    if( 1 == g_entranceCount )
    {
        g_pCurClient = debugClient;
        g_originalOutputMask = originalOutputMask;
    }

    if( !g_pClrHost )
    {
        g_hostIsDbgShellExe = _IsHostDbgShellExe();
        g_pDbgShellExePath = _GetDbgShellBinaryPath();

		_RemoveMarkOfTheInternet( g_pDbgShellExePath );

        g_pClrHost = new ClrHost( g_pDbgShellExePath );
        hr = g_pClrHost->Initialize( /* createNewAppDomain = */ !g_hostIsDbgShellExe );
        if( FAILED( hr ) )
        {
            delete g_pClrHost;
            delete g_pDbgShellExePath;
            g_pClrHost = nullptr;
            g_pDbgShellExePath = nullptr;
            goto Cleanup;
        }
        OutputDebugString( L"DbgShell: Initialized CLR stuff.\n" );
    }

    //wprintf( L"\nextension args: %S\n\n", args );

    widenedArgs = _Utf8ToUtf16( args );

    hr = SehWrapper( debugClient, widenedArgs );

    if( FAILED( hr ) )
    {
        wprintf( L"DbgShellExt: Unexpected: SehWrapper failed: %#x\n", hr );
        hr = S_OK; // ignore it
    }

Cleanup:

    if( originalOutputMask )
    {
        hr = debugClient->SetOutputMask( originalOutputMask );
        if( FAILED( hr ) )
        {
            wprintf( L"DbgShellExt: Unexpected: SetOutputMask failed: %#x\n", hr );
            hr = S_OK; // ignore it
        }
    }

    if( widenedArgs )
    {
        delete[] widenedArgs;
        widenedArgs = nullptr;
    }

    OutputDebugString( L"Finished !dbgshell command.\n" );

    if( FAILED( hr ) )
    {
        wprintf( L"End hresult: %#x\n(press [enter] to continue)\n", hr );
        getchar();
    }

    if( hwnd )
    {
        //
        // Get the DbgShell window out of the way.
        //
        SwitchToThisWindow( hwndOriginal, TRUE );
    }

    if( 1 == g_entranceCount )
    {
        g_pCurClient = nullptr;
    }

    g_entranceCount--;

    // The debugger is not interested in our errors.
    return S_OK;
} // end !dbgshell extension command


// The "!DbgShellExt.help" extension command. The debugger knows about it by virtue of it
// being exported.
HRESULT CALLBACK help( IDebugClient* debugClient, PCSTR args )
{
    return help_worker( debugClient, /* chainToOtherHelpCommands = */ true );
} // end !help extension command


HRESULT help_worker( IDebugClient* debugClient, bool chainToOtherHelpCommands )
{
    HRESULT hr = S_OK;
    PDEBUG_CONTROL7 pDebugControl = nullptr;

    hr = debugClient->QueryInterface( IID_IDebugControl7, reinterpret_cast< void** >( &pDebugControl ) );
    if( FAILED( hr ) )
    {
        DbgPrintf_Error( L"DbgShellExt: '!help' failed to create an IDebugControl7: %#x\n", hr );
        hr = E_FAIL;
        goto Cleanup;
    }

    // I could do it all in one big string (would look nicer here), but that seems to give
    // the windbg UI indigestion.

    // We want to put "borders" around our help, because other help commands may also run,
    // and we don't want everything running together, as it would be confusing which help
    // is from which extension.

    hr = pDebugControl->ControlledOutputWide( DEBUG_OUTCTL_DML | DEBUG_OUTCTL_ALL_CLIENTS, // DEBUG_OUTCTL_THIS_CLIENT,
                                              DEBUG_OUTPUT_NORMAL,
                                              L"=================================================================================================\n\n"

                                              L"                                            <b>DbgShellExt</b>\n\n" );

    hr = pDebugControl->ControlledOutputWide( DEBUG_OUTCTL_DML | DEBUG_OUTCTL_ALL_CLIENTS, // DEBUG_OUTCTL_THIS_CLIENT,
                                              DEBUG_OUTPUT_NORMAL,
                                              L"The DbgShellExt extension is the host for the <link cmd=\"!dbgshell\">!dbgshell</link> command.\n\n"

                                              L"<b>DbgShell</b> is a PowerShell front-end for dbgeng. It can run standalone (dbgshell.exe), or be\n"
                                              L"hosted by a debugger (via this extension).\n\n"

                                              // TODO: I believe there's a way to get a DML link to pop open a browser.
                                              L"For more info on DbgShell go to <exec cmd=\".shell -x start http://CodeBox/DbgShell\">http://CodeBox/DbgShell</exec>.\n\n"

                                              L"For more local help, run \"Get-Help about_DbgShell\" from DbgShell.\n\n"
                                            );
    hr = pDebugControl->ControlledOutputWide( DEBUG_OUTCTL_DML | DEBUG_OUTCTL_ALL_CLIENTS, // DEBUG_OUTCTL_THIS_CLIENT,
                                              DEBUG_OUTPUT_NORMAL,
                                              L"<b>Usage:</b>\n\n"

                                              L"   <link cmd=\"!dbgshell\">!dbgshell</link>\n\n"

                                              L"      Starts (or re-enters) DbgShell interactively. When you are finished with DbgShell, you\n"
                                              L"      can run \"exit\" or \"q\" (in DbgShell) to return to the debugger. DbgShell will continue\n"
                                              L"      to run in the background (dormant), and can be re-entered by running !dbgshell again.\n\n"

                                              L"   <link cmd=\"!dbgshell\">!dbgshell</link> [-NoProfile] [-NoExit] [-Bp] [<i>&lt;powershell commands&gt;</i>]\n\n"

                                              L"      Starts (or re-enters) DbgShell. If commands are present, then DbgShell returns control\n"
                                              L"      back to the debugger after running them, unless -NoExit is also specified. If no\n"
                                              L"      commands are present, then DbgShell remains open for interactive use.\n\n"

                                              L"      The -NoProfile option is only useful if used the first time you run !dbgshell after\n"
                                              L"      loading DbgShellExt. It instructs DbgShell to not run any profile scripts when\n"
                                              L"      starting. (DbgShell remains loaded but dormant until DbgShellExt is unloaded, so\n"
                                              L"      there's no way to \"un-run\" the profile scripts after it has been started.)\n\n"

                                              L"      The -NoProfile and -NoExit options can be abbreviated. If ambiguous (\"-n\"), -NoExit\n"
                                              L"      is assumed.\n\n"

                                              L"      The -Bp flag is only needed if the !dbgshell command is being run as part of a\n"
                                              L"      breakpoint command. It should be added automatically when you create the breakpoint.\n\n"
                                            );
    hr = pDebugControl->ControlledOutputWide( DEBUG_OUTCTL_DML | DEBUG_OUTCTL_ALL_CLIENTS, // DEBUG_OUTCTL_THIS_CLIENT,
                                              DEBUG_OUTPUT_NORMAL,
                                              L"   <link cmd=\"!dbgshell\">!dbgshell</link> [-NoProfile] [-NoExit] [-Bp] -EncodedCommand <i>&lt;base64-encoded commands&gt;</i>\n\n"

                                              L"      Similar to the previous, but the commands can be base64-encoded, thus bypassing\n"
                                              L"      problems with quoting and semicolons.\n\n"

                                              L"      The encoded command can be UTF16 (like PowerShell's -EncodedCommand), but can also be\n"
                                              L"      UTF8, and can also include the BOM to unambiguously indicate the encoding (unlike\n"
                                              L"      PowerShell). From with DbgShell, you can use [MS.Dbg.DbgProvider]::EncodeString() to\n"
                                              L"      encode a command string.\n\n"
                                            );
    hr = pDebugControl->ControlledOutputWide( DEBUG_OUTCTL_DML | DEBUG_OUTCTL_ALL_CLIENTS, // DEBUG_OUTCTL_THIS_CLIENT,
                                              DEBUG_OUTPUT_NORMAL,
                                              L"<b>Examples:</b>\n\n"

                                              L"   !dbgshell -NoExit (kc).Frames | select -Unique -ExpandProperty Module\n\n"

                                              L"      Gives the list of modules present on the stack.\n\n"

                                              L"      Note that DbgShell, in true PowerShell fashion, deals in objects, not text, so you can\n"
                                              L"      easily continue to process the output, like:\n\n"

                                              L"   !dbgshell -n (kc).Frames | select -Unique -ExpandProperty Module | select Name, VersionInfo\n\n"

                                              L"      Gives just the names and versions of modules present on the stack.\n\n"

                                              L"   !dbgshell &amp; C:\\foo\\MyScript.ps1\n\n"

                                              L"      Executes the C:\\foo\\MyScript.ps1 script, and then returns back to the debugger.\n\n"

                                              L"   !dbgshell -NoExit -EncodedCommand 77u/V3JpdGUtSG9zdCAiYG5gbkRiZ1NoZWxsIGlzIEFXRVNPTUUuYG4iIC1Gb3JlIEdyZWVu\n\n"

                                              L"      You'll just have to try it and see.\n\n"
                                            );

    hr = pDebugControl->ControlledOutputWide( DEBUG_OUTCTL_DML | DEBUG_OUTCTL_ALL_CLIENTS, // DEBUG_OUTCTL_THIS_CLIENT,
                                              DEBUG_OUTPUT_NORMAL,
                                              L"=================================================================================================\n\n" );

    // TODO: other good examples? Maybe an example that includes non-debuggery things?
    // (like Invoke-WebRequest or checking the registry or something)

    if( FAILED( hr ) )
    {
        DbgPrintf_Error( L"DbgShellExt: Unexpected: ControlledOutputWide failed: %#x\n", hr );
    }

Cleanup:

    if( pDebugControl )
    {
        pDebugControl->Release();
        pDebugControl = nullptr;
    }

    // The debugger is not interested in our errors. But we should not "take over" the
    // !help command--let other extensions' !help commands also run:
    if( chainToOtherHelpCommands )
        return DEBUG_EXTENSION_CONTINUE_SEARCH;
    else
        return S_OK;
} // end help_worker
