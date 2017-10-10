#pragma once
#include "stdafx.h"


class ConsoleUtil
{
private:
    // Prevent copy construction and assignment:
    ConsoleUtil( const ConsoleUtil& ) = delete;
    ConsoleUtil& operator=(const ConsoleUtil&) = delete;

    bool m_bAllocatedConsole;


    bool _AlreadyHaveConsole()
    {
        // GetStdHandle does not give the results we want: after unloading and reloading
        // DbgShellExt, it tells us that we still have a console, when in fact we don't,
        // and things get totally discombobulated.
        //
        // (This seems like a bug, but I doubt it would get fixed. It repro's on Win8.1
        // and Win10.)
     // HANDLE hStdOut = GetStdHandle( STD_OUTPUT_HANDLE );
     // if( hStdOut && (INVALID_HANDLE_VALUE != hStdOut) )
     // {
     //     DbgPrintf( L"We already have a console.\n" );
     //     // Seems like we already have a console.
     //     //
     //     // (Could we get into this case if output is redirected? Perhaps. But I don't
     //     // know what scenario would require us to care about that.)
     //     return;
     // }
     // DbgPrintf( L"We do NOT already have a console.\n" );

        // Fortunately, this seems to work fine, and is a lot nicer than checking for
        // ERROR_ACCESS_DENIED after calling AllocConsole.
        return nullptr != GetConsoleWindow();
    }

    void _TryCreateConsole()
    {
        if( _AlreadyHaveConsole() )
        {
            DbgPrintf( L"We already have a console.\n" );
            return;
        }

        DbgPrintf( L"We do NOT already have a console.\n" );

        BOOL bItWorked = ::AllocConsole();

        if( !bItWorked )
        {
            DWORD dwLastErr = GetLastError();
            DbgPrintf_Error( L"AllocConsole failed: %#x\n", dwLastErr );
            // Dunno what to do about this. I would prefer to failfast, but the host might
            // not appreciate a guest burning the house down over spilled milk. If things
            // aren't working, we'll just let the user unload us and get on with life.
            // (assuming that returning here doesn't cause the process to crash in some
            // other way :/)
            return;
        }

        DbgPrintf( L"Allocated a console.\n" );
        m_bAllocatedConsole = true;

        //
        // Need to re-open std handles for std io to work.
        //
        FILE* dontCare;

        _wfreopen_s( &dontCare, L"CONOUT$", L"w", stdout );
        _wfreopen_s( &dontCare, L"CONOUT$", L"w", stderr );
        _wfreopen_s( &dontCare, L"CONIN$", L"r", stdin );

     // //
     // // Let's check a few things:
     // //
     // UINT cp = GetConsoleOutputCP();
     // wprintf( L"ConsoleOutputCP: %#x\n", cp );
     // CPINFOEXW cpInfo = { 0 };
     // bItWorked = GetCPInfoExW( cp, 0, &cpInfo );
     // if( !bItWorked )
     //     RaiseFailFastException( nullptr, 0, 0 );

     // wprintf( L"cpInfo.CodePageName: %s\n", cpInfo.CodePageName );

     // DWORD dwMode;
     // bItWorked = GetConsoleMode( GetStdHandle( STD_OUTPUT_HANDLE ), &dwMode );
     // if( !bItWorked )
     //     RaiseFailFastException( nullptr, 0, 0 );

     // wprintf( L"Mode: %#x\n", dwMode );
    } // end _TryCreateConsole()


    void _DestroyConsole()
    {
        DbgPrintf( L"Freeing the console.\n" );
        BOOL bItWorked = ::FreeConsole();

        if( !bItWorked )
            RaiseFailFastException( nullptr, 0, 0 );

        //
        // We need to do this again, else the console window won't go away.
        //
        FILE* dontCare;

        _wfreopen_s( &dontCare, L"CONOUT$", L"w", stdout );
        _wfreopen_s( &dontCare, L"CONOUT$", L"w", stderr );
        _wfreopen_s( &dontCare, L"CONIN$", L"r", stdin );
    } // end _DestroyConsole()


public:
    ConsoleUtil() : m_bAllocatedConsole( false )
    {
        _TryCreateConsole();
    }

    ~ConsoleUtil()
    {
        if( m_bAllocatedConsole )
        {
            _DestroyConsole();
        }
    }

    bool DidWeAllocateANewConsole()
    {
        return m_bAllocatedConsole;
    }
};
