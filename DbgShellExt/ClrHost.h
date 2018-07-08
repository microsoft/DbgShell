#define INITGUID
#include "stdafx.h"

using namespace std;
using namespace experimental::filesystem::v1;



#if defined(_AMD64_) || defined(_AMD64)
    #import "C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\mscorlib.tlb" raw_interfaces_only rename("ReportEvent", "_ReportEvent")
#else
    #import "C:\\Windows\\Microsoft.NET\\Framework\\v4.0.30319\\mscorlib.tlb" raw_interfaces_only rename("ReportEvent", "_ReportEvent")
#endif
typedef mscorlib::_AppDomain _AppDomain;
typedef mscorlib::_Assembly _Assembly;
typedef mscorlib::_Type _Type;
typedef mscorlib::BindingFlags BindingFlags;
typedef mscorlib::IAppDomainSetup IAppDomainSetup;
#define BindingFlags_Public mscorlib::BindingFlags_Public
#define BindingFlags_Static mscorlib::BindingFlags_Static
#define BindingFlags_InvokeMethod mscorlib::BindingFlags_InvokeMethod
#define BindingFlags_FlattenHierarchy mscorlib::BindingFlags_FlattenHierarchy

#include <metahost.h>


// This is necessary because the v2 of <filesystem> didn't handle UNC paths well--it
// frequently squashes the two leading whacks down to one.
wstring _FixUncPathIfNecessary( path& path )
{
 // if( (path.generic_wstring().size() > 2) &&
 //     (path.generic_wstring()[ 0 ] == '\\') &&
 //     (path.generic_wstring()[ 1 ] != '\\') )
 // {
 //     return wstring( L"\\" ) + path.generic_wstring();
 // }
 // else
 // {
        return path.generic_wstring();
 // }
}


class ClrHost
{
private:
    path m_exePath;
    path m_appBasePath;
    path m_configFilePath;

    bool m_separateAppDomain = false;
    _AppDomain* m_pAppDomain = nullptr;

    ICLRMetaHostPolicy* m_pMetaHostPolicy = nullptr;
    ICLRRuntimeInfo* m_pClrRuntimeInfo = nullptr;
    ICorRuntimeHost* m_pCorRuntimeHost = nullptr;

    bool m_emergencyStopped = false;

    // Based on MEX
    static int CreateNewAppDomain( ICorRuntimeHost* pCorRuntimeHost,
                                   LPCWSTR appDomainBaseDirectory,
                                   LPCWSTR appDomainConfigFile,
                                   _AppDomain** ppNewAppDomain)
    {
        HRESULT hr;
        IUnknown* iUnknown = nullptr;
        _AppDomain* pAppDomain = nullptr;
        IUnknown* iAppDomainSetupUnknown = nullptr;
        IAppDomainSetup* iAppDomainSetup = nullptr;
        BSTR bstrAppDomainBaseDirectory = nullptr;
        BSTR bstrConfigFilePath = nullptr;

        *ppNewAppDomain = nullptr;

        //wprintf( L"App domain base: %s\n", appDomainBaseDirectory );

        bstrAppDomainBaseDirectory = SysAllocString( appDomainBaseDirectory );
        if( bstrAppDomainBaseDirectory == nullptr )
        {
            hr = E_FAIL;
            wprintf( L"ERROR! SysAllocString failed for string '%s'\n", appDomainBaseDirectory );
            goto ExitWithFailure;
        }

        bstrConfigFilePath = SysAllocString( appDomainConfigFile );
        if( bstrConfigFilePath == nullptr )
        {
            hr = E_FAIL;
            wprintf( L"ERROR! SysAllocString failed for string '%s'\n", appDomainConfigFile );
            goto ExitWithFailure;
        }

        hr = pCorRuntimeHost->CreateDomainSetup( &iAppDomainSetupUnknown );
        if( FAILED( hr ) )
        {
            wprintf( L"ERROR! pCorRuntimeHost::CreateDomainSetup failed: %.8x\n", hr );
            goto ExitWithFailure;
        }

        hr = iAppDomainSetupUnknown->QueryInterface( __uuidof( IAppDomainSetup ),
                                                     (void**) &iAppDomainSetup );
        if( FAILED( hr ) )
        {
            wprintf( L"ERROR! Failed getting an interface for IAppDomainSetup: %.8x\n", hr );
            goto ExitWithFailure;
        }

        hr = iAppDomainSetup->put_ApplicationBase( bstrAppDomainBaseDirectory );
        if( FAILED( hr ) )
        {
            wprintf( L"ERROR! IAppDomainSetup::put_ApplicationBase failed: %.8x\n", hr );
            goto ExitWithFailure;
        }

        hr = iAppDomainSetup->put_ConfigurationFile( bstrConfigFilePath );
        if( FAILED( hr ) )
        {
            wprintf( L"ERROR! IAppDomainSetup::put_ConfigurationFile failed: %.8x\n", hr );
            goto ExitWithFailure;
        }

        hr = pCorRuntimeHost->CreateDomainEx( L"DbgShellExtAppDomain",
                                              iAppDomainSetup,
                                              nullptr,
                                              &iUnknown );
        if( FAILED( hr ) )
        {
            wprintf( L"ERROR! Failed creating new app domain: %.8x\n", hr );
            goto ExitWithFailure;
        }

        hr = iUnknown->QueryInterface( __uuidof( _AppDomain ),
                                       (void**) &pAppDomain );
        if( FAILED( hr ) )
        {
            wprintf( L"ERROR! Failed querying AppDomain interface: %.8x\n", hr );
            goto ExitWithFailure;
        }

        OutputDebugString( L"DbgShell: Created new app domain\n" );

        *ppNewAppDomain = pAppDomain;
        goto Exit;

    ExitWithFailure:
        if( pAppDomain != nullptr )
        {
            pAppDomain->Release();
            pAppDomain = nullptr;
        }
    Exit:
        if( iUnknown != nullptr )
        {
            iUnknown->Release();
            iUnknown = nullptr;
        }
        if( iAppDomainSetup != nullptr )
        {
            iAppDomainSetup->Release();
            iAppDomainSetup = nullptr;
        }
        if( iAppDomainSetupUnknown != nullptr )
        {
            iAppDomainSetupUnknown->Release();
            iAppDomainSetupUnknown = nullptr;
        }
        if( bstrAppDomainBaseDirectory != nullptr )
        {
            SysFreeString( bstrAppDomainBaseDirectory );
            bstrAppDomainBaseDirectory = nullptr;
        }
        if( bstrConfigFilePath != nullptr )
        {
            SysFreeString( bstrConfigFilePath );
            bstrConfigFilePath = nullptr;
        }
        return hr;
    } // end CreateNewAppDomain()


    static int GetDefaultDomain( ICorRuntimeHost* pCorRuntimeHost,
                                 _AppDomain** ppDefaultAppDomain)
    {
        HRESULT hr;
        IUnknown* iUnknown = nullptr;
        _AppDomain* pAppDomain = nullptr;

        *ppDefaultAppDomain = nullptr;

        hr = pCorRuntimeHost->GetDefaultDomain( &iUnknown );
        if( FAILED( hr ) )
        {
            wprintf( L"ERROR! Failed getting default app domain: %.8x\n", hr );
            goto ExitWithFailure;
        }

        hr = iUnknown->QueryInterface( __uuidof( _AppDomain ),
                                       (void**) &pAppDomain );
        if( FAILED( hr ) )
        {
            wprintf( L"ERROR! Failed querying AppDomain interface: %.8x\n", hr );
            goto ExitWithFailure;
        }

        OutputDebugString( L"DbgShell: got default app domain\n" );

        *ppDefaultAppDomain = pAppDomain;
        goto Exit;

    ExitWithFailure:
        if( pAppDomain != nullptr )
        {
            pAppDomain->Release();
            pAppDomain = nullptr;
        }
    Exit:
        if( iUnknown != nullptr )
        {
            iUnknown->Release();
            iUnknown = nullptr;
        }
        return hr;
    } // end GetDefaultDomain()


public:
    ClrHost( path exePath )
        : m_exePath( exePath )
    {
        m_appBasePath = exePath.parent_path();
        m_configFilePath = _FixUncPathIfNecessary( exePath ) + L".config";
    } // end constructor


    ~ClrHost()
    {
        if( m_pAppDomain )
        {
            if( m_separateAppDomain && !m_emergencyStopped )
            {
                HRESULT hr = m_pCorRuntimeHost->UnloadDomain( m_pAppDomain );
                if( FAILED( hr ) )
                {
                    wprintf( L"Warning: UnloadDomain failed: %#x\n", hr );
                }
            }
            m_pAppDomain->Release();
            m_pAppDomain = nullptr;
        }

        if( m_pCorRuntimeHost )
        {
            m_pCorRuntimeHost->Release();
            m_pCorRuntimeHost = nullptr;
        }

        if( m_pClrRuntimeInfo )
        {
            m_pClrRuntimeInfo->Release();
            m_pClrRuntimeInfo = nullptr;
        }

        if( m_pMetaHostPolicy )
        {
            m_pMetaHostPolicy->Release();
            m_pMetaHostPolicy = nullptr;
        }
    } // end destructor


    void CallInEmergency()
    {
        m_emergencyStopped = true;
        if( m_pCorRuntimeHost )
        {
            m_pCorRuntimeHost->Stop();
        }
    } // end CallInEmergency()




    // Loads and starts the CLR (if necessary) and creates a new appdomain to run
    // managed code.
    //
    // (We don't want to use the default appdomain because we want to be able to unload
    // stuff, which you can only do by unloading an appdomain, and you can't unload the
    // default appdomain without shutting down the CLR, and we don't want to shut down the
    // CLR, because then it can't be restarted.)
    HRESULT Initialize( bool createNewAppDomain )
    {
        HRESULT hr = S_OK;
        DWORD dwConfigFlags = 0;
        BOOL bIsLoadable = FALSE;
        BOOL bIsLoaded = FALSE;
        BOOL bIsStarted = FALSE;
        DWORD dwStartupFlags = 0;
        bool bNeedToStop = false;
        DWORD dwExitCode = 0;

        BOOL bItWorked = FALSE;

        hr = CLRCreateInstance( CLSID_CLRMetaHostPolicy,
                                IID_ICLRMetaHostPolicy,
                                reinterpret_cast< void** >( &m_pMetaHostPolicy ) );

        if( FAILED( hr ) )
        {
            wprintf( L"CLRCreateInstance failed: %#x\n", hr );
            goto Cleanup;
        }
        else
        {
            OutputDebugString( L"DbgShell: Loaded ICLRMetaHostPolicy.\n" );
        }

        hr = m_pMetaHostPolicy->GetRequestedRuntime( METAHOST_POLICY_APPLY_UPGRADE_POLICY,
                                                     _FixUncPathIfNecessary( m_exePath ).c_str(),
                                                     nullptr,
                                                     nullptr,
                                                     nullptr,
                                                     nullptr,
                                                     nullptr,
                                                     &dwConfigFlags,
                                                     IID_ICLRRuntimeInfo,
                                                     reinterpret_cast<void**>( &m_pClrRuntimeInfo ) );

        if( FAILED( hr ) )
        {
            wprintf( L"pMetaHostPolicy->GetRequestedRuntime failed: %#x\n", hr );
            goto Cleanup;
        }

        //wprintf( L"dwConfigFlags: %#x\n", dwConfigFlags );

        // This will allow v2 components to load. It's not necessarily nice to use it in a
        // shared hosting environment because it a) is hardcoded, and b) is global, but I
        // couldn't figure out how to get a config file to work.
        //
        // TODO: We used to need to be able to load v2 components because of some test
        // binaries, but that is no more... do we still need this?
        //
        hr = m_pClrRuntimeInfo->BindAsLegacyV2Runtime();
        if( FAILED( hr ) )
        {
            wprintf( L"Warning: BindAsLegacyV2Runtime failed: %#x\n", hr );
            // ignore it
            hr = S_OK;
        }

        hr = m_pClrRuntimeInfo->IsLoaded( GetCurrentProcess(), &bIsLoaded );
        if( FAILED( hr ) )
            RaiseFailFastException( nullptr, 0, 0 );

        hr = m_pClrRuntimeInfo->IsLoadable( &bIsLoadable );
        if( FAILED( hr ) )
            RaiseFailFastException( nullptr, 0, 0 );

        hr = m_pClrRuntimeInfo->IsStarted( &bIsStarted, &dwStartupFlags );
        if( FAILED( hr ) )
            RaiseFailFastException( nullptr, 0, 0 );

     // wprintf( L"Loadable? %s   Loaded? %s   Started? %s   dwStartupFlags: %#x\n",
     //          bIsLoadable ? L"yes" : L"no",
     //          bIsLoaded ? L"yes" : L"no",
     //          bIsStarted ? L"yes" : L"no",
     //          dwStartupFlags );

        hr = m_pClrRuntimeInfo->GetInterface( CLSID_CorRuntimeHost,
                                              IID_ICorRuntimeHost,
                                              reinterpret_cast< void** >( &m_pCorRuntimeHost ) );

        if( FAILED( hr ) )
        {
            wprintf( L"pClrRuntimeInfo->GetInterface( ICorRuntimeHost ) failed: %#x\n", hr );
            goto Cleanup;
        }

        if( !bIsStarted )
        {
            hr = m_pCorRuntimeHost->Start();
            if( FAILED( hr ) )
            {
                wprintf( L"Failed to start the CLR: %#x\n", hr );
                goto Cleanup;
            }
            OutputDebugString( L"DbgShell: Started the CLR.\n" );
            bNeedToStop = true;
        }

        if( createNewAppDomain )
        {
            hr = CreateNewAppDomain( m_pCorRuntimeHost,
                                     _FixUncPathIfNecessary( m_appBasePath ).c_str(),
                                     _FixUncPathIfNecessary( m_configFilePath ).c_str(),
                                     &m_pAppDomain );
            if( FAILED( hr ) )
            {
                wprintf( L"Failed to create an appdomain: %#x\n", hr );
                goto Cleanup;
            }
            m_separateAppDomain = true;
        }
        else
        {
            hr = GetDefaultDomain( m_pCorRuntimeHost, &m_pAppDomain );
            if( FAILED( hr ) )
            {
                wprintf( L"Failed to get the default appdomain: %#x\n", hr );
                goto Cleanup;
            }
        }

    Cleanup:
        return hr;
    } // end Initialize()


    HRESULT RunAssembly( int numArgs, ... )
    {
        va_list vlArgs;
        vector< LPCWSTR > vectorArgs( numArgs );

        va_start( vlArgs, numArgs );

        for( LONG i = 0; i < numArgs; i++ )
        {
            vectorArgs[ i ] = va_arg( vlArgs, LPCWSTR );
        }

        va_end( vlArgs );

        return RunAssembly( vectorArgs );
    } // end RunAssembly()


    HRESULT RunAssembly( const vector< LPCWSTR >& args )
    {
        if( m_emergencyStopped )
        {
            return E_ABORT;
        }

        HRESULT hr = S_OK;
        CComBSTR bstrAssembly( _FixUncPathIfNecessary( m_exePath ).c_str() );

        SAFEARRAY* saArgs = SafeArrayCreateVector( VT_BSTR, 0, static_cast< ULONG >( args.size() ) );
        if( !saArgs )
        {
            wprintf( L"Failed to create saArgs.\n" );
            RaiseFailFastException( nullptr, 0, 0 );
        }

        for( LONG i = 0; i < static_cast< LONG >( args.size() ); i++ )
        {
            BSTR tmp = SysAllocString( args[ i ] );
            SafeArrayPutElement( saArgs, &i, static_cast< void* >( tmp ) );
        }

        long retval = 0;
        hr = m_pAppDomain->ExecuteAssembly_3( bstrAssembly.m_str,
                                              nullptr, // evidence
                                              saArgs,
                                              &retval );

        // This allegedly also frees all the BSTRs that I put into it.
        SafeArrayDestroy( saArgs );

        if( FAILED( hr ) )
        {
            wprintf( L"Failed to execute assembly: %#x\n", hr );
            goto Cleanup;
        }

        //wprintf( L"Assembly execution finished! Exit code: %i\n", retval );

    Cleanup:
        return hr;
    }
}; // end class ClrHost

