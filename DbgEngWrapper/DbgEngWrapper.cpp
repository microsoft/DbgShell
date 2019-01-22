#include "stdafx.h"

#undef CreateProcess // We define some functions that we want called "CreateProcess", not "CreateProcessW".
#include "DbgEngWrapper.h"
#include "Callbacks.h"

namespace DbgEngWrapper
{

WDebugClient::WDebugClient( ::IDebugClient6* pDc )
    : WDebugEngInterface( pDc )
{
} // end constructor


WDebugClient::WDebugClient( IntPtr pDc )
    : WDebugEngInterface( pDc )
{
} // end constructor


int WDebugClient::DisconnectProcessServer(
     UInt64 Server)
{
    return m_pNative->DisconnectProcessServer( Server );
}


int WDebugClient::GetRunningProcessSystemIds(
     UInt64 Server,
     [Out] array<ULONG>^% Ids)
   //array<ULONG>^ Ids,
   //ULONG Count,
   //[Out] ULONG% ActualCount)
{
    ULONG numIdsAllocated = 100;
    ULONG actualNumIds = 0;
    Ids = nullptr;
    array<ULONG>^ tmp = nullptr;
    int hr = S_FALSE;

    while( S_FALSE == hr )
    {
        tmp = gcnew array<ULONG>( numIdsAllocated );
        pin_ptr<ULONG> ppTmp = &tmp[ 0 ];
        hr = m_pNative->GetRunningProcessSystemIds( Server,
                                                    ppTmp,
                                                    numIdsAllocated,
                                                    &actualNumIds );
    }

    if( S_OK == hr )
    {
        array<ULONG>::Resize( tmp, actualNumIds );
        Ids = tmp;
    }

    g_log->Write( L"GetRunningProcessSystemIds", gcnew TlPayload_Int( hr ) );
    return hr;
}


int WDebugClient::AttachProcess(
     UInt64 Server,
     ULONG ProcessID,
     DEBUG_ATTACH AttachFlags)
{
    HRESULT hr = m_pNative->AttachProcess( Server,
                                           ProcessID,
                                           safe_cast<ULONG>( AttachFlags ) );
    g_log->Write( L"AttachProcess", gcnew TlPayload_Int( hr ) );
    return hr;
}


int WDebugClient::GetProcessOptions(
     [Out] DEBUG_PROCESS% Options)
{
    Options = (DEBUG_PROCESS) 0;
    pin_ptr<DEBUG_PROCESS> pp = &Options;
    HRESULT hr = m_pNative->GetProcessOptions( (PULONG) pp );
    g_log->Write( L"GetProcessOptions", gcnew TlPayload_Int( hr ) );
    return hr;
}


int WDebugClient::AddProcessOptions(
     DEBUG_PROCESS Options)
{
    g_log->Write( L"AddProcessOptions" );
    return m_pNative->AddProcessOptions( safe_cast<ULONG>( Options ) );
}


int WDebugClient::RemoveProcessOptions(
     DEBUG_PROCESS Options)
{
    g_log->Write( L"RemoveProcessOptions" );
    return m_pNative->RemoveProcessOptions( safe_cast<ULONG>( Options ) );
}


int WDebugClient::SetProcessOptions(
     DEBUG_PROCESS Options)
{
    g_log->Write( L"SetProcessOptions" );
    return m_pNative->SetProcessOptions( safe_cast<ULONG>( Options ) );
}


int WDebugClient::ConnectSession(
     DEBUG_CONNECT_SESSION Flags,
     ULONG HistoryLimit)
{
    g_log->Write( L"ConnectSession" );
    return m_pNative->ConnectSession( safe_cast<ULONG>( Flags ), HistoryLimit );
}

int WDebugClient::TerminateProcesses()
{
    g_log->Write( L"TerminateProcesses" );
    return m_pNative->TerminateProcesses();
}


int WDebugClient::DetachProcesses()
{
    g_log->Write( L"DetachProcesses" );
    return m_pNative->DetachProcesses();
}


int WDebugClient::EndSession(
     DEBUG_END Flags)
{
    g_log->Write( L"EndSession", gcnew TlPayload_Int( static_cast<ULONG>( Flags ) ) );
    return m_pNative->EndSession( safe_cast<ULONG>( Flags ) );
}


int WDebugClient::GetExitCode(
     [Out] unsigned long% Code)
{
    g_log->Write( L"GetExitCode" );
    Code = 0xdeadbeef;
    pin_ptr<unsigned long> pp = &Code;
    return m_pNative->GetExitCode( pp );
}


int WDebugClient::DispatchCallbacks(
     ULONG Timeout)
{
    g_log->Write( L"DispatchCallbacks" );
    return m_pNative->DispatchCallbacks( Timeout );
}


// TODO: This is a little odd... could we get rid of the parameter? Or do you have
// to have a separate IDebugClient to call this?
int WDebugClient::ExitDispatch(
     WDebugClient^ Client)
{
    g_log->Write( L"ExitDispatch" );
    return m_pNative->ExitDispatch( (PDEBUG_CLIENT) Client->m_pNative );
}


int WDebugClient::CreateClient(
     [Out] WDebugClient^% Client)
{
    g_log->Write( L"CreateClient" );
    Client = nullptr;
    ::IDebugClient* pNewClient = nullptr;
    int retval = m_pNative->CreateClient( &pNewClient );
    if( pNewClient )
    {
        Client = gcnew WDebugClient( (::IDebugClient6*) pNewClient );
    }
    return retval;
}


int WDebugClient::GetInputCallbacks(
    // TODO: what?
    [Out] IntPtr% Callbacks )
     //[Out] WDebugInputCallbacks^% Callbacks)
{
    g_log->Write( L"GetInputCallbacks" );
    // I prefer the assignment to IntPtr::Zero, but it gives Intellisense a headache.
    //Callbacks = IntPtr::Zero;
    Callbacks = IntPtr( 0 );
    pin_ptr<IntPtr> pp = &Callbacks;
    return m_pNative->GetInputCallbacks( (PDEBUG_INPUT_CALLBACKS*) pp );
}


int WDebugClient::SetInputCallbacks(
     IDebugInputCallbacksImp^ Callbacks )
{
    if( nullptr == Callbacks )
    {
        g_log->Write( L"SetInputCallbacks (null)" );
        return m_pNative->SetInputCallbacks( nullptr );
    }

    g_log->Write( L"SetInputCallbacks" );

    // First we apply a C++/CLI COM adapter wrapper over its managed counterpart:
    auto pAdapter = gcnew DbgEngInputCallbacksAdapter( Callbacks );

    // Then get the IUnknown pointer for it.
    auto pNative = (PDEBUG_INPUT_CALLBACKS)
        Marshal::GetComInterfaceForObject( pAdapter,
                                           IComDbgEngInputCallbacks::typeid ).ToPointer();

    return m_pNative->SetInputCallbacks( pNative );
}

/* GetOutputCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */


// Use GetOutputCallbacksWide instead.
// int GetOutputCallbacks(
//      [Out] IntPtr% Callbacks)

/* We may have to pass a debugger engine conversion thunk back in so we can't specify a specific interface */


// Use SetOutputCallbacksWide instead.
// int SetOutputCallbacks(
//      IntPtr Callbacks)


int WDebugClient::GetOutputMask(
     [Out] DEBUG_OUTPUT% Mask)
{
    g_log->Write( L"GetOutputMask" );
    Mask = (DEBUG_OUTPUT) 0;
    pin_ptr<DEBUG_OUTPUT> pp = &Mask;
    return m_pNative->GetOutputMask( (PULONG) pp );
}


int WDebugClient::SetOutputMask(
     DEBUG_OUTPUT Mask)
{
    g_log->Write( L"SetOutputMask" );
    return m_pNative->SetOutputMask( safe_cast<ULONG>( Mask ) );
}


int WDebugClient::GetOtherOutputMask(
     WDebugClient^ Client,
     [Out] DEBUG_OUTPUT% Mask)
{
    g_log->Write( L"GetOtherOutputMask" );
    Mask = (DEBUG_OUTPUT) 0;
    pin_ptr<DEBUG_OUTPUT> pp = &Mask;
    return m_pNative->GetOtherOutputMask( (::IDebugClient*) Client->m_pNative, (PULONG) pp );
}


int WDebugClient::SetOtherOutputMask(
     WDebugClient^ Client,
     DEBUG_OUTPUT Mask)
{
    g_log->Write( L"SetOtherOutputMask" );
    return m_pNative->SetOtherOutputMask( (::IDebugClient*) Client->m_pNative, safe_cast<ULONG>( Mask ) );
}


int WDebugClient::GetOutputWidth(
     [Out] ULONG% Columns)
{
    g_log->Write( L"GetOutputWidth" );
    Columns = 0;
    pin_ptr<ULONG> pp = &Columns;
    return m_pNative->GetOutputWidth( pp );
}


int WDebugClient::SetOutputWidth(
     ULONG Columns)
{
    g_log->Write( L"SetOutputWidth" );
    return m_pNative->SetOutputWidth( Columns );
}


/* GetEventCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */


// int GetEventCallbacks(
//      [Out] IntPtr% Callbacks)
// {
//     throw gcnew NotImplementedException();
// }

/* We may have to pass a debugger engine conversion thunk back in so we can't specify a specific interface */


//int SetEventCallbacks(
//     IntPtr Callbacks)
//{
//    //throw gcnew NotImplementedException();
//    return m_pNative->SetEventCallbacks( (PDEBUG_EVENT_CALLBACKS) (void*) Callbacks );
//}


int WDebugClient::FlushCallbacks()
{
    g_log->Write( L"FlushCallbacks" );
    return m_pNative->FlushCallbacks();
}


/* IDebugClient2 */

int WDebugClient::EndProcessServer(
     UInt64 Server)
{
    g_log->Write( L"EndProcessServer" );
    return m_pNative->EndProcessServer( Server );
}


int WDebugClient::WaitForProcessServerEnd(
     ULONG Timeout)
{
    g_log->Write( L"WaitForProcessServerEnd" );
    return m_pNative->WaitForProcessServerEnd( Timeout );
}


int WDebugClient::IsKernelDebuggerEnabled()
{
    g_log->Write( L"IsKernelDebuggerEnabled" );
    return m_pNative->IsKernelDebuggerEnabled();
}


int WDebugClient::TerminateCurrentProcess()
{
    g_log->Write( L"TerminateCurrentProcess" );
    return m_pNative->TerminateCurrentProcess();
}


int WDebugClient::DetachCurrentProcess()
{
    g_log->Write( L"DetachCurrentProcess" );
    return m_pNative->DetachCurrentProcess();
}


int WDebugClient::AbandonCurrentProcess()
{
    g_log->Write( L"AbandonCurrentProcess" );
    return m_pNative->AbandonCurrentProcess();
}

/* IDebugClient3 */


int WDebugClient::GetRunningProcessSystemIdByExecutableNameWide(
     UInt64 Server,
     String^ ExeName,
     DEBUG_GET_PROC Flags,
     [Out] ULONG% Id)
{
    g_log->Write( L"GetRunningProcessSystemIdByExecutableNameWide" );
    marshal_context mc;
    pin_ptr<ULONG> pp = &Id;
    return m_pNative->GetRunningProcessSystemIdByExecutableNameWide( Server,
                                                                     mc.marshal_as<const wchar_t*>( ExeName ),
                                                                     safe_cast<ULONG>( Flags ),
                                                                     pp );
}


int WDebugClient::GetRunningProcessDescriptionWide(
     UInt64 Server,
     ULONG SystemId,
     DEBUG_PROC_DESC Flags,
     [Out] String^% ExeName,
     [Out] String^% Description)
{
    g_log->Write( L"GetRunningProcessDescriptionWide" );
    ULONG cchExeName = MAX_PATH;
    ULONG cchDescription = MAX_PATH;

    ExeName = nullptr;
    Description = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszExeName( new wchar_t[ cchExeName ] );
        std::unique_ptr<wchar_t[]> wszDescription( new wchar_t[ cchDescription ] );

        hr = m_pNative->GetRunningProcessDescriptionWide( Server,
                                                          SystemId,
                                                          safe_cast<ULONG>( Flags ),
                                                          wszExeName.get(),
                                                          cchExeName,
                                                          &cchExeName,
                                                          wszDescription.get(),
                                                          cchDescription,
                                                          &cchDescription );

        if( S_OK == hr )
        {
            ExeName = gcnew String( wszExeName.get() );
            Description = gcnew String( wszDescription.get() );
        }
    }

    return hr;
}


// TODO: Should I deprecate this in favor of CreateProcess2
int WDebugClient::CreateProcessWide(
     UInt64 Server,
     String^ CommandLine,
     DEBUG_CREATE_PROCESS CreateFlags)
{
    g_log->Write( L"CreateProcessWide" );
    marshal_context mc;
    const wchar_t* wszConstCommandLine = mc.marshal_as<const wchar_t*>( CommandLine );
    std::unique_ptr<wchar_t, free_delete> wszMutableCommandLine( _wcsdup( wszConstCommandLine ) );
    return m_pNative->CreateProcessWide( Server,
                                         wszMutableCommandLine.get(),
                                         safe_cast<ULONG>( CreateFlags ) );
}


int WDebugClient::CreateProcessAndAttachWide(
     UInt64 Server,
     String^ CommandLine,
     DEBUG_CREATE_PROCESS CreateFlags,
     ULONG ProcessId,
     DEBUG_ATTACH AttachFlags)
{
    g_log->Write( L"CreateProcessAndAttachWide" );
    marshal_context mc;
    const wchar_t* wszConstCommandLine = mc.marshal_as<const wchar_t*>( CommandLine );
    std::unique_ptr<wchar_t, free_delete> wszMutableCommandLine( _wcsdup( wszConstCommandLine ) );
    return m_pNative->CreateProcessAndAttachWide( Server,
                                                  wszMutableCommandLine.get(),
                                                  safe_cast<ULONG>( CreateFlags ),
                                                  ProcessId,
                                                  safe_cast<ULONG>( AttachFlags ) );
}

/* IDebugClient4 */


int WDebugClient::OpenDumpFileWide(
     String^ FileName,
     UInt64 FileHandle)
{
    g_log->Write( L"OpenDumpFileWide" );
    marshal_context mc;
    return m_pNative->OpenDumpFileWide( mc.marshal_as<const wchar_t*>( FileName ), FileHandle );
}


int WDebugClient::WriteDumpFileWide(
     String^ DumpFile,
     UInt64 FileHandle,
     DEBUG_DUMP Qualifier,
     DEBUG_FORMAT FormatFlags,
     String^ Comment)
{
    g_log->Write( L"WriteDumpFileWide" );
    marshal_context mc;
    return m_pNative->WriteDumpFileWide( mc.marshal_as<const wchar_t*>( DumpFile ),
                                         FileHandle,
                                         safe_cast<ULONG>( Qualifier ),
                                         safe_cast<ULONG>( FormatFlags ),
                                         mc.marshal_as<const wchar_t*>( Comment ) );
}


int WDebugClient::AddDumpInformationFileWide(
     String^ FileName,
     UInt64 FileHandle,
     DEBUG_DUMP_FILE Type)
{
    g_log->Write( L"AddDumpInformationFileWide" );
    marshal_context mc;
    return m_pNative->AddDumpInformationFileWide( mc.marshal_as<const wchar_t*>( FileName ),
                                                  FileHandle,
                                                  safe_cast<ULONG>( Type ) );
}


int WDebugClient::GetNumberDumpFiles(
     [Out] ULONG% Number)
{
    g_log->Write( L"GetNumberDumpFiles" );
    pin_ptr<ULONG> pp = &Number;
    return m_pNative->GetNumberDumpFiles( pp );
}


int WDebugClient::GetDumpFileWide(
     ULONG Index,
     [Out] String^% Name,
  // StringBuilder^ Buffer,
  // Int32 BufferSize,
  // [Out] ULONG% NameSize,
     [Out] UInt64% Handle,
     [Out] ULONG% Type)
{
    g_log->Write( L"GetDumpFileWide" );
    ULONG cchName = MAX_PATH;
    pin_ptr<UInt64> ppHandle = &Handle;
    pin_ptr<ULONG> ppType = &Type;

    Name = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszName( new wchar_t[ cchName ] );

        hr = m_pNative->GetDumpFileWide( Index,
                                         wszName.get(),
                                         cchName,
                                         &cchName,
                                         ppHandle,
                                         ppType);

        if( S_OK == hr )
        {
            Name = gcnew String( wszName.get() );
        }
    }

    return hr;
}

/* IDebugClient5 */


int WDebugClient::AttachKernelWide(
     DEBUG_ATTACH Flags,
     String^ ConnectOptions)
{
    g_log->Write( L"AttachKernelWide" );
    marshal_context mc;
    return m_pNative->AttachKernelWide( safe_cast<ULONG>( Flags ),
                                        mc.marshal_as<const wchar_t*>( ConnectOptions ) );
}


int WDebugClient::GetKernelConnectionOptionsWide(
     [Out] String^% Options)
  // StringBuilder^ Buffer,
  // Int32 BufferSize,
  // [Out] ULONG% OptionsSize)
{
    g_log->Write( L"GetKernelConnectionOptionsWide" );
    ULONG cchOptions = MAX_PATH;

    Options = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszOptions( new wchar_t[ cchOptions ] );

        hr = m_pNative->GetKernelConnectionOptionsWide( wszOptions.get(),
                                                        cchOptions,
                                                        &cchOptions );

        if( S_OK == hr )
        {
            Options = gcnew String( wszOptions.get() );
        }
    }

    return hr;
}


int WDebugClient::SetKernelConnectionOptionsWide(
     String^ Options)
{
    g_log->Write( L"SetKernelConnectionOptionsWide" );
    marshal_context mc;
    return m_pNative->SetKernelConnectionOptionsWide( mc.marshal_as<const wchar_t*>( Options ) );
}


int WDebugClient::StartProcessServerWide(
     DEBUG_CLASS Flags,
     String^ Options,
     IntPtr Reserved)
{
    g_log->Write( L"StartProcessServerWide" );
    marshal_context mc;
    return m_pNative->StartProcessServerWide( safe_cast<ULONG>( Flags ),
                                              mc.marshal_as<const wchar_t*>( Options ),
                                              safe_cast<PVOID>( Reserved ) );
}


int WDebugClient::ConnectProcessServerWide(
     String^ RemoteOptions,
     [Out] UInt64% Server)
{
    g_log->Write( L"ConnectProcessServerWide" );
    marshal_context mc;
    pin_ptr<UInt64> pp = &Server;
    return m_pNative->ConnectProcessServerWide( mc.marshal_as<const wchar_t*>( RemoteOptions ),
                                                safe_cast<PULONG64>( pp ) );
}


int WDebugClient::StartServerWide(
     String^ Options)
{
    g_log->Write( L"StartServerWide" );
    marshal_context mc;
    return m_pNative->StartServerWide( mc.marshal_as<const wchar_t*>( Options ) );
}


int WDebugClient::OutputServersWide(
     DEBUG_OUTCTL OutputControl,
     String^ Machine,
     DEBUG_SERVERS Flags)
{
    g_log->Write( L"OutputServersWide" );
    marshal_context mc;
    return m_pNative->OutputServersWide( safe_cast<ULONG>( OutputControl ),
                                         mc.marshal_as<const wchar_t*>( Machine ),
                                         safe_cast<ULONG>( Flags ) );
}

/* GetOutputCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */


int WDebugClient::GetOutputCallbacksWide(
     [Out] IntPtr% Callbacks)
{
    g_log->Write( L"GetOutputCallbacksWide" );
    Callbacks = IntPtr( 0 );
    pin_ptr<IntPtr> pp = &Callbacks;
    return m_pNative->GetOutputCallbacksWide( (PDEBUG_OUTPUT_CALLBACKS_WIDE*) pp );
}

/* We may have to pass a debugger engine conversion thunk back in so we can't specify a specific interface */


int WDebugClient::SetOutputCallbacksWide(
     [In] IDebugOutputCallbacksImp^ Callbacks)
{
    g_log->Write( L"SetOutputCallbacksWide" );
    if( nullptr == Callbacks )
    {
        return m_pNative->SetOutputCallbacksWide( nullptr );
    }

    // First we apply a C++/CLI COM adapter wrapper over its managed counterpart:
    auto pAdapter = gcnew DbgEngOutputCallbacksAdapter( Callbacks );

    // Then get the IUnknown pointer for it.
    auto pNative = (PDEBUG_OUTPUT_CALLBACKS_WIDE)
        Marshal::GetComInterfaceForObject( pAdapter,
                                           IComDbgEngOutputCallbacks::typeid ).ToPointer();

    return m_pNative->SetOutputCallbacksWide( pNative );
}


int WDebugClient::GetOutputLinePrefixWide(
     [Out] String^% Prefix)
  // StringBuilder^ Buffer,
  // Int32 BufferSize,
  // [Out] ULONG% PrefixSize)
{
    g_log->Write( L"GetOutputLinePrefixWide" );
    ULONG cchPrefix = MAX_PATH;

    Prefix = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszPrefix( new wchar_t[ cchPrefix ] );

        hr = m_pNative->GetOutputLinePrefixWide( wszPrefix.get(),
                                                 cchPrefix,
                                                 &cchPrefix );

        if( S_OK == hr )
        {
            Prefix = gcnew String( wszPrefix.get() );
        }
    }

    return hr;
}


int WDebugClient::SetOutputLinePrefixWide(
     String^ Prefix)
{
    g_log->Write( L"SetOutputLinePrefixWide" );
    marshal_context mc;
    return m_pNative->SetOutputLinePrefixWide( mc.marshal_as<const wchar_t*>( Prefix ) );
}


int WDebugClient::GetIdentityWide(
     [Out] String^% Identity)
  // StringBuilder^ Buffer,
  // Int32 BufferSize,
  // [Out] ULONG% IdentitySize);
{
    g_log->Write( L"GetIdentityWide" );
    ULONG cchIdentity = MAX_PATH;

    Identity = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszIdentity( new wchar_t[ cchIdentity ] );

        hr = m_pNative->GetIdentityWide( wszIdentity.get(),
                                         cchIdentity,
                                         &cchIdentity );

        if( S_OK == hr )
        {
            Identity = gcnew String( wszIdentity.get() );
        }
    }

    return hr;
}


int WDebugClient::OutputIdentityWide(
     DEBUG_OUTCTL OutputControl,
     ULONG Flags,
     String^ Format)
{
    g_log->Write( L"OutputIdentityWide" );
    marshal_context mc;
    return m_pNative->OutputIdentityWide( safe_cast<ULONG>( OutputControl ),
                                          Flags,
                                          mc.marshal_as<const wchar_t*>( Format ) );
}

/* GetEventCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */


int WDebugClient::GetEventCallbacksWide(
     [Out] IntPtr% Callbacks)
{
    g_log->Write( L"GetEventCallbacksWide" );
    pin_ptr<IntPtr> pp = &Callbacks;
    return m_pNative->GetEventCallbacksWide( (PDEBUG_EVENT_CALLBACKS_WIDE*) pp );
}

/* We may have to pass a debugger engine conversion thunk back in so we can't specify a specific interface */


int WDebugClient::SetEventCallbacksWide(
     IDebugEventCallbacksWideImp^ Callbacks )
{
    g_log->Write( L"SetEventCallbacksWide" );
    if( nullptr == Callbacks )
    {
        return m_pNative->SetEventCallbacksWide( nullptr );
    }

    // First we apply a C++/CLI COM adapter wrapper over its managed counterpart:
    auto pAdapter = gcnew DbgEngEventCallbacksAdapter( Callbacks );

    // Then get the IUnknown pointer for it.
    auto pNative = (PDEBUG_EVENT_CALLBACKS_WIDE)
        Marshal::GetComInterfaceForObject( pAdapter,
                                           IComDbgEngEventCallbacks::typeid ).ToPointer();

    return m_pNative->SetEventCallbacksWide( pNative );
}


int WDebugClient::CreateProcess2Wide(
     UInt64 Server,
     String^ CommandLine,
     Microsoft::Diagnostics::Runtime::Interop::DEBUG_CREATE_PROCESS_OPTIONS% OptionsBuffer,
     String^ InitialDirectory,
     String^ Environment)
{
    g_log->Write( L"CreateProcess2Wide" );
    marshal_context mc;
    const wchar_t* wszConstCommandLine = mc.marshal_as<const wchar_t*>( CommandLine );
    std::unique_ptr<wchar_t, free_delete> wszMutableCommandLine( _wcsdup( wszConstCommandLine ) );
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_CREATE_PROCESS_OPTIONS> pp = &OptionsBuffer;
    return m_pNative->CreateProcess2Wide( Server,
                                          wszMutableCommandLine.get(),
                                          (void*) pp,
                                          Marshal::SizeOf( OptionsBuffer ),
                                          mc.marshal_as<const wchar_t*>( InitialDirectory ),
                                          mc.marshal_as<const wchar_t*>( Environment ) );
}


int WDebugClient::CreateProcessAndAttach2Wide(
     UInt64 Server,
     String^ CommandLine,
     Microsoft::Diagnostics::Runtime::Interop::DEBUG_CREATE_PROCESS_OPTIONS* OptionsBuffer,
     //ULONG OptionsBufferSize,
     String^ InitialDirectory,
     String^ Environment,
     ULONG ProcessId,
     DEBUG_ATTACH AttachFlags)
{
    g_log->Write( L"CreateProcessAndAttach2Wide" );
    marshal_context mc;
    const wchar_t* wszConstCommandLine = mc.marshal_as<const wchar_t*>( CommandLine );
    std::unique_ptr<wchar_t, free_delete> wszMutableCommandLine( _wcsdup( wszConstCommandLine ) );
    //pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_CREATE_PROCESS_OPTIONS> pp = &OptionsBuffer;
    return m_pNative->CreateProcessAndAttach2Wide( Server,
                                                   wszMutableCommandLine.get(),
                                                   //(void*) pp,
                                                   (void*) OptionsBuffer,
                                                   Marshal::SizeOf( Microsoft::Diagnostics::Runtime::Interop::DEBUG_CREATE_PROCESS_OPTIONS::typeid ),
                                                   mc.marshal_as<const wchar_t*>( InitialDirectory ),
                                                   mc.marshal_as<const wchar_t*>( Environment ),
                                                   ProcessId,
                                                   safe_cast<ULONG>( AttachFlags ) );
}


int WDebugClient::PushOutputLinePrefixWide(
     String^ NewPrefix,
     [Out] UInt64% Handle)
{
    g_log->Write( L"PushOutputLinePrefixWide" );
    Handle = 0;
    marshal_context mc;
    pin_ptr<UInt64> pp = &Handle;
    return m_pNative->PushOutputLinePrefixWide( mc.marshal_as<const wchar_t*>( NewPrefix ), pp );
}


int WDebugClient::PopOutputLinePrefix(
     UInt64 Handle)
{
    g_log->Write( L"PopOutputLinePrefix" );
    return m_pNative->PopOutputLinePrefix( Handle );
}


int WDebugClient::GetNumberInputCallbacks(
     [Out] ULONG% Count)
{
    g_log->Write( L"GetNumberInputCallbacks" );
    pin_ptr<ULONG> pp = &Count;
    return m_pNative->GetNumberInputCallbacks( pp );
}


int WDebugClient::GetNumberOutputCallbacks(
     [Out] ULONG% Count)
{
    g_log->Write( L"GetNumberOutputCallbacks" );
    pin_ptr<ULONG> pp = &Count;
    return m_pNative->GetNumberOutputCallbacks( pp );
}


int WDebugClient::GetNumberEventCallbacks(
     Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT Flags,
     [Out] ULONG% Count)
{
    g_log->Write( L"GetNumberEventCallbacks" );
    pin_ptr<ULONG> pp = &Count;
    return m_pNative->GetNumberEventCallbacks( safe_cast<ULONG>( Flags ), pp );
}


int WDebugClient::GetQuitLockStringWide(
     [Out] String^% QuitLockString)
   //StringBuilder^ Buffer,
   //Int32 BufferSize,
   //[Out] ULONG% StringSize)
{
    g_log->Write( L"GetQuitLockStringWide" );
    ULONG cchQuitLockString = MAX_PATH;

    QuitLockString = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszQuitLockString( new wchar_t[ cchQuitLockString ] );

        hr = m_pNative->GetQuitLockStringWide( wszQuitLockString.get(),
                                               cchQuitLockString,
                                               &cchQuitLockString );

        if( S_OK == hr )
        {
            QuitLockString = gcnew String( wszQuitLockString.get() );
        }
    }

    return hr;
}


int WDebugClient::SetQuitLockStringWide(
     String^ LockString)
{
    g_log->Write( L"SetQuitLockStringWide" );
    marshal_context mc;
    return m_pNative->SetQuitLockStringWide( mc.marshal_as<const wchar_t*>( LockString ) );
}


int WDebugClient::SetEventContextCallbacks(
     IDebugEventContextCallbacksImp^ Callbacks )
{
    g_log->Write( L"SetEventContextCallbacks" );
    if( nullptr == Callbacks )
    {
        return m_pNative->SetEventContextCallbacks( nullptr );
    }

    // First we apply a C++/CLI COM adapter wrapper over its managed counterpart:
    auto pAdapter = gcnew DbgEngEventContextCallbacksAdapter( Callbacks );

    // Then get the IUnknown pointer for it.
    auto pNative = (PDEBUG_EVENT_CONTEXT_CALLBACKS)
        Marshal::GetComInterfaceForObject( pAdapter,
                                           IComDbgEngEventContextCallbacks::typeid ).ToPointer();

    return m_pNative->SetEventContextCallbacks( pNative );
}

//
// WDebugBreakpoint stuff
//


WDebugBreakpoint::WDebugBreakpoint( ::IDebugBreakpoint3* pBp )
    : WDebugEngInterface( pBp )
{
    // We don't ever, ever want to call Release--it's a sham anyway.
    GC::SuppressFinalize( this );
} // end constructor

WDebugBreakpoint::WDebugBreakpoint( IntPtr pBp )
    : WDebugEngInterface( pBp )
{
    // We don't ever, ever want to call Release--it's a sham anyway.
    GC::SuppressFinalize( this );
} // end constructor


// Retrieves debugger engine unique ID
// for the breakpoint.  This ID is
// fixed as long as the breakpoint exists
// but after that may be reused.
int WDebugBreakpoint::GetId(
    [Out] ULONG% Id )
{
    WDebugClient::g_log->Write( L"BP::GetId" );
    _CheckInterfaceAbandoned();
    pin_ptr<ULONG> pp = &Id;
    return m_pNative->GetId( pp );
}

// Retrieves the type of break and
// processor type for the breakpoint.
int WDebugBreakpoint::GetType(
    [Out] ULONG% BreakType,
    [Out] ULONG% ProcType )
{
    WDebugClient::g_log->Write( L"BP::GetType" );
    _CheckInterfaceAbandoned();
    pin_ptr<ULONG> ppBreakType = &BreakType;
    pin_ptr<ULONG> ppProcType = &ProcType;
    return m_pNative->GetType( ppBreakType, ppProcType );
}

// Returns the client that called AddBreakpoint.
int WDebugBreakpoint::GetAdder(
    [Out] WDebugClient^% Adder )
{
    WDebugClient::g_log->Write( L"BP::GetAdder" );
    _CheckInterfaceAbandoned();
    Adder = nullptr;
    PDEBUG_CLIENT pdc = nullptr;
    int retval = m_pNative->GetAdder( &pdc );
    if( (S_OK == retval) && pdc )
    {
        Adder = gcnew WDebugClient( (::IDebugClient6*) pdc );
    }
    return retval;
}

int WDebugBreakpoint::GetFlags(
    [Out] DEBUG_BREAKPOINT_FLAG% Flags )
{
    WDebugClient::g_log->Write( L"BP::GetFlags" );
    _CheckInterfaceAbandoned();
    pin_ptr<DEBUG_BREAKPOINT_FLAG> pp = &Flags;
    return m_pNative->GetFlags( (PULONG) pp );
}

// Only certain flags can be changed.  Flags
// are: GO_ONLY, ENABLE.
// Sets the given flags.
int WDebugBreakpoint::AddFlags(
    DEBUG_BREAKPOINT_FLAG Flags )
{
    WDebugClient::g_log->Write( L"BP::AddFlags" );
    _CheckInterfaceAbandoned();
    return m_pNative->AddFlags( (ULONG) Flags );
}

// Clears the given flags.
int WDebugBreakpoint::RemoveFlags(
    DEBUG_BREAKPOINT_FLAG Flags )
{
    WDebugClient::g_log->Write( L"BP::RemoveFlags" );
    _CheckInterfaceAbandoned();
    return m_pNative->RemoveFlags( (ULONG) Flags );
}

// Sets the flags.
int WDebugBreakpoint::SetFlags(
    DEBUG_BREAKPOINT_FLAG Flags )
{
    WDebugClient::g_log->Write( L"BP::SetFlags" );
    _CheckInterfaceAbandoned();
    return m_pNative->SetFlags( (ULONG) Flags );
}


// Controls the offset of the breakpoint.  The
// interpretation of the offset value depends on
// the type of breakpoint and its settings.  It
// may be a code address, a data address, an
// I/O port, etc.
int WDebugBreakpoint::GetOffset(
    [Out] UInt64% Offset )
{
    WDebugClient::g_log->Write( L"BP::GetOffset" );
    _CheckInterfaceAbandoned();
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetOffset( pp );
}

int WDebugBreakpoint::SetOffset(
    UInt64 Offset )
{
    WDebugClient::g_log->Write( L"BP::SetOffset" );
    _CheckInterfaceAbandoned();
    return m_pNative->SetOffset( Offset );
}


// Data breakpoint methods will fail if the
// target platform does not support the
// parameters used.
// These methods only function for breakpoints
// created as data breakpoints.
int WDebugBreakpoint::GetDataParameters(
    [Out] ULONG% Size,
    [Out] ULONG% AccessType )
{
    WDebugClient::g_log->Write( L"BP::GetDataParameters" );
    _CheckInterfaceAbandoned();
    pin_ptr<ULONG> ppSize = &Size;
    pin_ptr<ULONG> ppAccessType = &AccessType;
    return m_pNative->GetDataParameters( ppSize, ppAccessType );
}

int WDebugBreakpoint::SetDataParameters(
    ULONG Size,
    ULONG AccessType )
{
    WDebugClient::g_log->Write( L"BP::SetDataParameters" );
    _CheckInterfaceAbandoned();
    return m_pNative->SetDataParameters( Size, AccessType );
}


// Pass count defaults to one.
int WDebugBreakpoint::GetPassCount(
    [Out] ULONG% Count )
{
    WDebugClient::g_log->Write( L"BP::GetPassCount" );
    _CheckInterfaceAbandoned();
    pin_ptr<ULONG> pp = &Count;
    return m_pNative->GetPassCount( pp );
}

int WDebugBreakpoint::SetPassCount(
    ULONG Count )
{
    WDebugClient::g_log->Write( L"BP::SetPassCount" );
    _CheckInterfaceAbandoned();
    return m_pNative->SetPassCount( Count );
}

// Gets the current number of times
// the breakpoint has been hit since
// it was last triggered.
int WDebugBreakpoint::GetCurrentPassCount(
    [Out] ULONG% Count )
{
    WDebugClient::g_log->Write( L"BP::GetCurrentPassCount" );
    _CheckInterfaceAbandoned();
    pin_ptr<ULONG> pp = &Count;
    return m_pNative->GetCurrentPassCount( pp );
}


// If a match thread is set this breakpoint will
// only trigger if it occurs on the match thread.
// Otherwise it triggers for all threads.
// Thread restrictions are not currently supported
// in kernel mode.
int WDebugBreakpoint::GetMatchThreadId(
    [Out] ULONG% Id )
{
    WDebugClient::g_log->Write( L"BP::GetMatchThreadId" );
    _CheckInterfaceAbandoned();
    pin_ptr<ULONG> pp = &Id;
    return m_pNative->GetMatchThreadId( pp );
}

int WDebugBreakpoint::SetMatchThreadId(
    ULONG Thread )
{
    WDebugClient::g_log->Write( L"BP::SetMatchThreadId" );
    _CheckInterfaceAbandoned();
    return m_pNative->SetMatchThreadId( Thread );
}


int WDebugBreakpoint::GetParameters(
    //[Out] PDEBUG_BREAKPOINT_PARAMETERS Params
    [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_BREAKPOINT_PARAMETERS% Params )
{
    WDebugClient::g_log->Write( L"BP::GetParameters" );
    _CheckInterfaceAbandoned();
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_BREAKPOINT_PARAMETERS> pp = &Params;
    return m_pNative->GetParameters( (PDEBUG_BREAKPOINT_PARAMETERS) pp );
}


// IDebugBreakpoint2.

int WDebugBreakpoint::GetCommandWide(
    [Out] String^% Command )
{
    WDebugClient::g_log->Write( L"BP::GetCommandWide" );
    _CheckInterfaceAbandoned();
    return GetCommandWide( MAX_PATH, Command );
}

// The command for a breakpoint is automatically
// executed by the engine before the event
// is propagated.  If the breakpoint continues
// execution the event will begin with a continue
// status.  If the breakpoint does not continue
// the event will begin with a break status.
// This allows breakpoint commands to participate
// in the normal event status voting.
// Breakpoint commands are only executed until
// the first command that alters the execution
// status, such as g, p and t.
int WDebugBreakpoint::GetCommandWide(
    [In] ULONG SizeHint,
    [Out] String^% Command )
 // _Out_writes_opt_(BufferSize) PWSTR Buffer,
 // ULONG BufferSize,
 // _Out_opt_ ULONG% CommandSize )
{
    WDebugClient::g_log->Write( L"BP::GetCommandWide (with size hint)" );
    _CheckInterfaceAbandoned();
    ULONG cchBuffer = SizeHint;

    Command = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszCommand( new wchar_t[ cchBuffer ] );

        hr = m_pNative->GetCommandWide( wszCommand.get(),
                                        cchBuffer,
                                        &cchBuffer );

        if( S_OK == hr )
        {
            Command = gcnew String( wszCommand.get() );
        }
    }

    return hr;
}

int WDebugBreakpoint::SetCommandWide(
    String^ Command
    )
{
    WDebugClient::g_log->Write( L"BP::SetCommandWide" );
    _CheckInterfaceAbandoned();
    marshal_context mc;
    return m_pNative->SetCommandWide( mc.marshal_as<const wchar_t*>( Command ) );
}


int WDebugBreakpoint::GetOffsetExpressionWide(
    [Out] String^% Expression )
{
    _CheckInterfaceAbandoned();
    return GetOffsetExpressionWide( MAX_PATH, Expression );
}

// Offset expressions are evaluated immediately
// and at module load and unload events.  If the
// evaluation is successful the breakpoints
// offset is updated and the breakpoint is
// handled normally.  If the expression cannot
// be evaluated the breakpoint is deferred.
// Currently the only offset expression
// supported is a module-relative symbol
// of the form <Module>!<Symbol>.
int WDebugBreakpoint::GetOffsetExpressionWide(
    [In] ULONG SizeHint,
    [Out] String^% Expression )
 // _Out_writes_opt_(BufferSize) PWSTR Buffer,
 // ULONG BufferSize,
 // _Out_opt_ ULONG% ExpressionSize )
{
    WDebugClient::g_log->Write( L"BP::GetOffsetExpressionWide" );
    _CheckInterfaceAbandoned();
    ULONG cchExpression = SizeHint;

    Expression = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszExpression( new wchar_t[ cchExpression ] );

        hr = m_pNative->GetOffsetExpressionWide( wszExpression.get(),
                                                 cchExpression,
                                                 &cchExpression );

        if( S_OK == hr )
        {
            Expression = gcnew String( wszExpression.get() );
        }
    }

    return hr;
}

int WDebugBreakpoint::SetOffsetExpressionWide(
    String^ Expression )
{
    WDebugClient::g_log->Write( L"BP::SetOffsetExpressionWide" );
    _CheckInterfaceAbandoned();
    marshal_context mc;
    return m_pNative->SetOffsetExpressionWide( mc.marshal_as<const wchar_t*>( Expression ) );
}


// IDebugBreakpoint3.

int WDebugBreakpoint::GetGuid(
    [Out] System::Guid% Guid
    //[Out] LPGUID Guid
    )
{
    WDebugClient::g_log->Write( L"BP::GetGuid" );
    _CheckInterfaceAbandoned();
    pin_ptr<System::Guid> pp = &Guid;
    return m_pNative->GetGuid( (LPGUID) pp );
}


//
// WDebugControl stuff
//

WDebugControl::WDebugControl( ::IDebugControl6* pDc )
    : WDebugEngInterface( pDc )
{
    WDebugClient::g_log->Write( L"Created IDebugControl" );
} // end constructor


WDebugControl::WDebugControl( IntPtr pDc )
    : WDebugEngInterface( pDc )
{
    WDebugClient::g_log->Write( L"Created IDebugControl" );
} // end constructor


int WDebugControl::GetInterrupt()
{
    WDebugClient::g_log->Write( L"DebugControl::GetInterrupt" );
    return m_pNative->GetInterrupt();
}

int WDebugControl::SetInterrupt(
    [In] DEBUG_INTERRUPT Flags)
{
    WDebugClient::g_log->Write( L"DebugControl::SetInterrupt" );
    return m_pNative->SetInterrupt( (ULONG) Flags );
}

int WDebugControl::GetInterruptTimeout(
    [Out] ULONG% Seconds)
{
    WDebugClient::g_log->Write( L"DebugControl::GetInterruptTimeout" );
    pin_ptr<ULONG> pp = &Seconds;
    return m_pNative->GetInterruptTimeout( pp );
}

int WDebugControl::SetInterruptTimeout(
    [In] ULONG Seconds)
{
    WDebugClient::g_log->Write( L"DebugControl::SetInterruptTimeout" );
    return m_pNative->SetInterruptTimeout( Seconds );
}

//    int GetLogFile(
//        [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
//        [In] Int32 BufferSize,
//        [Out] ULONG% FileSize,
//        [Out, MarshalAs(UnmanagedType.Bool)] out bool Append);

//    int OpenLogFile(
//        [In, MarshalAs(UnmanagedType.LPStr)] string File,
//        [In, MarshalAs(UnmanagedType.Bool)] bool Append);

//    int CloseLogFile();

//    int GetLogMask(
//        [Out] out DEBUG_OUTPUT Mask);

//    int SetLogMask(
//        [In] DEBUG_OUTPUT Mask);

//    int Input(
//        [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
//        [In] Int32 BufferSize,
//        [Out] ULONG% InputSize);

//    int ReturnInput(
//        [In, MarshalAs(UnmanagedType.LPStr)] string Buffer);

//    int Output(
//        [In] DEBUG_OUTPUT Mask,
//        [In, MarshalAs(UnmanagedType.LPStr)] string Format);

//    int OutputVaList( /* THIS SHOULD NEVER BE CALLED FROM C# */
//        [In] DEBUG_OUTPUT Mask,
//        [In, MarshalAs(UnmanagedType.LPStr)] string Format,
//        [In] IntPtr va_list_Args);

//    int ControlledOutput(
//        [In] DEBUG_OUTCTL OutputControl,
//        [In] DEBUG_OUTPUT Mask,
//        [In, MarshalAs(UnmanagedType.LPStr)] string Format);

//    int ControlledOutputVaList( /* THIS SHOULD NEVER BE CALLED FROM C# */
//        [In] DEBUG_OUTCTL OutputControl,
//        [In] DEBUG_OUTPUT Mask,
//        [In, MarshalAs(UnmanagedType.LPStr)] string Format,
//        [In] IntPtr va_list_Args);

//    int OutputPrompt(
//        [In] DEBUG_OUTCTL OutputControl,
//        [In, MarshalAs(UnmanagedType.LPStr)] string Format);

//    int OutputPromptVaList( /* THIS SHOULD NEVER BE CALLED FROM C# */
//        [In] DEBUG_OUTCTL OutputControl,
//        [In, MarshalAs(UnmanagedType.LPStr)] string Format,
//        [In] IntPtr va_list_Args);

//    int GetPromptText(
//        [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
//        [In] Int32 BufferSize,
//        [Out] ULONG% TextSize);

//    int OutputCurrentState(
//        [In] DEBUG_OUTCTL OutputControl,
//        [In] DEBUG_CURRENT Flags);

//    int OutputVersionInformation(
//        [In] DEBUG_OUTCTL OutputControl);

//    int GetNotifyEventHandle(
//        [Out] out UInt64 Handle);

//    int SetNotifyEventHandle(
//        [In] UInt64 Handle);

//    int Assemble(
//        [In] UInt64 Offset,
//        [In, MarshalAs(UnmanagedType.LPStr)] string Instr,
//        [Out] out UInt64 EndOffset);

//    int Disassemble(
//        [In] UInt64 Offset,
//        [In] DEBUG_DISASM Flags,
//        [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
//        [In] Int32 BufferSize,
//        [Out] ULONG% DisassemblySize,
//        [Out] out UInt64 EndOffset);

int WDebugControl::GetDisassembleEffectiveOffset(
    [Out] UInt64% Offset)
{
    WDebugClient::g_log->Write( L"DebugControl::GetDisassembleEffectiveOffset" );
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetDisassembleEffectiveOffset( pp );
}

//    int OutputDisassembly(
//        [In] DEBUG_OUTCTL OutputControl,
//        [In] UInt64 Offset,
//        [In] DEBUG_DISASM Flags,
//        [Out] out UInt64 EndOffset);

//    int OutputDisassemblyLines(
//        [In] DEBUG_OUTCTL OutputControl,
//        [In] ULONG PreviousLines,
//        [In] ULONG TotalLines,
//        [In] UInt64 Offset,
//        [In] DEBUG_DISASM Flags,
//        [Out] ULONG% OffsetLine,
//        [Out] out UInt64 StartOffset,
//        [Out] out UInt64 EndOffset,
//        [Out, MarshalAs(UnmanagedType.LPArray)] UInt64[] LineOffsets);

int WDebugControl::GetNearInstruction(
    [In] UInt64 Offset,
    [In] int Delta,
    [Out] UInt64% NearOffset)
{
    WDebugClient::g_log->Write( L"DebugControl::GetNearInstruction" );
    pin_ptr<UInt64> pp = &NearOffset;
    return m_pNative->GetNearInstruction( Offset, Delta, pp );
}

//    int GetStackTrace(
//        [In] UInt64 FrameOffset,
//        [In] UInt64 StackOffset,
//        [In] UInt64 InstructionOffset,
//        [Out, MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME[] Frames,
//        [In] Int32 FrameSize,
//        [Out] ULONG% FramesFilled);

//    int GetReturnOffset(
//        [Out] out UInt64 Offset);

//    int OutputStackTrace(
//        [In] DEBUG_OUTCTL OutputControl,
//        [In, MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME[] Frames,
//        [In] Int32 FramesSize,
//        [In] DEBUG_STACK Flags);

int WDebugControl::GetDebuggeeType(
    [Out] DEBUG_CLASS% Class,
    [Out] DEBUG_CLASS_QUALIFIER% Qualifier)
{
    WDebugClient::g_log->Write( L"DebugControl::GetDebuggeeType" );
    pin_ptr<DEBUG_CLASS> ppClass = &Class;
    pin_ptr<DEBUG_CLASS_QUALIFIER> ppQualifier = &Qualifier;
    return m_pNative->GetDebuggeeType( (PULONG) ppClass,
                                       (PULONG) ppQualifier );
}

int WDebugControl::GetActualProcessorType(
    [Out] IMAGE_FILE_MACHINE% Type )
{
    WDebugClient::g_log->Write( L"DebugControl::GetActualProcessorType" );
    pin_ptr<IMAGE_FILE_MACHINE> pp = &Type;
    return m_pNative->GetActualProcessorType( (PULONG) pp );
}

int WDebugControl::GetExecutingProcessorType(
    [Out] IMAGE_FILE_MACHINE% Type )
{
    WDebugClient::g_log->Write( L"DebugControl::GetExecutingProcessorType" );
    pin_ptr<IMAGE_FILE_MACHINE> pp = &Type;
    return m_pNative->GetExecutingProcessorType( (PULONG) pp );
}

//    int GetNumberPossibleExecutingProcessorTypes(
//        [Out] ULONG% Number);

//    int GetPossibleExecutingProcessorTypes(
//        [In] ULONG Start,
//        [In] ULONG Count,
//        [Out, MarshalAs(UnmanagedType.LPArray)] IMAGE_FILE_MACHINE[] Types);

int WDebugControl::GetNumberProcessors(
    [Out] ULONG% Number)
{
    WDebugClient::g_log->Write( L"DebugControl::GetNumberProcessors" );
    pin_ptr<ULONG> pp = &Number;
    return m_pNative->GetNumberProcessors( pp );
}

//    int GetSystemVersion(
//        [Out] ULONG% PlatformId,
//        [Out] ULONG% Major,
//        [Out] ULONG% Minor,
//        [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder ServicePackString,
//        [In] Int32 ServicePackStringSize,
//        [Out] ULONG% ServicePackStringUsed,
//        [Out] ULONG% ServicePackNumber,
//        [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder BuildString,
//        [In] Int32 BuildStringSize,
//        [Out] ULONG% BuildStringUsed);

int WDebugControl::GetSystemVersion(
    [Out] ULONG% PlatformId,
    [Out] ULONG% Major,
    [Out] ULONG% Minor,
    [Out] String^% ServicePackString,
    [Out] ULONG% ServicePackNumber,
    [Out] String^% BuildString)
{
    WDebugClient::g_log->Write( L"DebugControl::GetSystemVersion" );

    ServicePackString = nullptr;
    BuildString = nullptr;
    PlatformId = 0;
    Major = 0;
    Minor = 0;
    ServicePackNumber = 0;

    pin_ptr<ULONG> ppPlatformId = &PlatformId;
    pin_ptr<ULONG> ppMajor = &Major;
    pin_ptr<ULONG> ppMinor = &Minor;
    pin_ptr<ULONG> ppServicePackNumber = &ServicePackNumber;

    char servicePackStringStackBuf[ 512 ] = { 0 };
    char buildStringStackBuf[ 1024 ] = { 0 };
    pin_ptr<char> ppServicePackStringStackBuf = &servicePackStringStackBuf[ 0 ];
    pin_ptr<char> ppBuildStringStackBuf = &buildStringStackBuf[ 0 ];

    ULONG servicePackStringUsed = 0;
    ULONG buildStringUsed = 0;

    HRESULT hr = m_pNative->GetSystemVersion(
        ppPlatformId,
        ppMajor,
        ppMinor,
        ppServicePackStringStackBuf,
        sizeof( servicePackStringStackBuf ),
        &servicePackStringUsed,
        ppServicePackNumber,
        ppBuildStringStackBuf,
        sizeof( buildStringStackBuf ),
        &buildStringUsed );

    if (SUCCEEDED(hr))
    {
        // It might've returned S_FALSE if the string buffers were too small... but I don't really care.
        ServicePackString = Marshal::PtrToStringAnsi( static_cast<IntPtr>( servicePackStringStackBuf ) );
        BuildString       = Marshal::PtrToStringAnsi( static_cast<IntPtr>( buildStringStackBuf ) );
    }

    return hr;
}



//    int GetPageSize(
//        [Out] ULONG% Size);

int WDebugControl::IsPointer64Bit()
{
    WDebugClient::g_log->Write( L"DebugControl::IsPointer64Bit" );
    return m_pNative->IsPointer64Bit();
}

//    int ReadBugCheckData(
//        [Out] ULONG% Code,
//        [Out] out UInt64 Arg1,
//        [Out] out UInt64 Arg2,
//        [Out] out UInt64 Arg3,
//        [Out] out UInt64 Arg4);

//    int GetNumberSupportedProcessorTypes(
//        [Out] ULONG% Number);

//    int GetSupportedProcessorTypes(
//        [In] ULONG Start,
//        [In] ULONG Count,
//        [Out, MarshalAs(UnmanagedType.LPArray)] IMAGE_FILE_MACHINE[] Types);

//    int GetProcessorTypeNames(
//        [In] IMAGE_FILE_MACHINE Type,
//        [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder FullNameBuffer,
//        [In] Int32 FullNameBufferSize,
//        [Out] ULONG% FullNameSize,
//        [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder AbbrevNameBuffer,
//        [In] Int32 AbbrevNameBufferSize,
//        [Out] ULONG% AbbrevNameSize);

int WDebugControl::GetEffectiveProcessorType(
    [Out] IMAGE_FILE_MACHINE% Type )
{
    WDebugClient::g_log->Write( L"DebugControl::GetEffectiveProcessorType" );
    pin_ptr<IMAGE_FILE_MACHINE> pp = &Type;
    return m_pNative->GetEffectiveProcessorType( (PULONG) pp );
}

int WDebugControl::SetEffectiveProcessorType(
    [In] IMAGE_FILE_MACHINE Type )
{
    WDebugClient::g_log->Write( L"DebugControl::SetEffectiveProcessorType" );
    return m_pNative->SetEffectiveProcessorType( (ULONG) Type );
}

int WDebugControl::GetExecutionStatus(
    [Out] DEBUG_STATUS% Status)
{
    WDebugClient::g_log->Write( L"DebugControl::GetExecutionStatus" );
    pin_ptr<DEBUG_STATUS> pp = &Status;
    return m_pNative->GetExecutionStatus( (PULONG) pp );
}

int WDebugControl::SetExecutionStatus(
    [In] DEBUG_STATUS Status)
{
    WDebugClient::g_log->Write( L"DebugControl::SetExecutionStatus" );
    return m_pNative->SetExecutionStatus( (ULONG) Status );
}

int WDebugControl::GetCodeLevel(
    [Out] DEBUG_LEVEL% Level)
{
    WDebugClient::g_log->Write( L"DebugControl::GetCodeLevel" );
    pin_ptr<DEBUG_LEVEL> pp = &Level;
    return m_pNative->GetCodeLevel( (PULONG) pp );
}

int WDebugControl::SetCodeLevel(
    [In] DEBUG_LEVEL Level)
{
    WDebugClient::g_log->Write( L"DebugControl::SetCodeLevel" );
    return m_pNative->SetCodeLevel( (ULONG) Level );
}

int WDebugControl::GetEngineOptions(
    [Out] DEBUG_ENGOPT% Options)
{
    WDebugClient::g_log->Write( L"DebugControl::GetEngineOptions" );
    pin_ptr<DEBUG_ENGOPT> pp = &Options;
    return m_pNative->GetEngineOptions( (PULONG) pp );
}

int WDebugControl::AddEngineOptions(
    [In] DEBUG_ENGOPT Options)
{
    WDebugClient::g_log->Write( L"DebugControl::AddEngineOptions", gcnew TlPayload_Int( (ULONG) Options ) );
    return m_pNative->AddEngineOptions( (ULONG) Options );
}

int WDebugControl::RemoveEngineOptions(
    [In] DEBUG_ENGOPT Options)
{
    WDebugClient::g_log->Write( L"DebugControl::RemoveEngineOptions" );
    return m_pNative->RemoveEngineOptions( (ULONG) Options );
}

int WDebugControl::SetEngineOptions(
    [In] DEBUG_ENGOPT Options)
{
    WDebugClient::g_log->Write( L"DebugControl::SetEngineOptions" );
    return m_pNative->SetEngineOptions( (ULONG) Options );
}

int WDebugControl::GetSystemErrorControl(
    [Out] ERROR_LEVEL% OutputLevel,
    [Out] ERROR_LEVEL% BreakLevel)
{
    WDebugClient::g_log->Write( L"DebugControl::GetSystemErrorControl" );
    pin_ptr<ERROR_LEVEL> ppOutputLevel = &OutputLevel;
    pin_ptr<ERROR_LEVEL> ppBreakLevel = &BreakLevel;
    return m_pNative->GetSystemErrorControl( (PULONG) ppOutputLevel,
                                             (PULONG) ppBreakLevel );
}

int WDebugControl::SetSystemErrorControl(
    [In] ERROR_LEVEL OutputLevel,
    [In] ERROR_LEVEL BreakLevel)
{
    WDebugClient::g_log->Write( L"DebugControl::SetSystemErrorControl" );
    return m_pNative->SetSystemErrorControl( (ULONG) OutputLevel, (ULONG) BreakLevel );
}

//    int GetTextMacro(
//        [In] ULONG Slot,
//        [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
//        [In] Int32 BufferSize,
//        [Out] ULONG% MacroSize);

//    int SetTextMacro(
//        [In] ULONG Slot,
//        [In, MarshalAs(UnmanagedType.LPStr)] string Macro);

//    int GetRadix(
//        [Out] ULONG% Radix);

//    int SetRadix(
//        [In] ULONG Radix);

//    int Evaluate(
//        [In, MarshalAs(UnmanagedType.LPStr)] string Expression,
//        [In] DEBUG_VALUE_TYPE DesiredType,
//        [Out] out DEBUG_VALUE Value,
//        [Out] ULONG% RemainderIndex);

//    int CoerceValue(
//        [In] DEBUG_VALUE In,
//        [In] DEBUG_VALUE_TYPE OutType,
//        [Out] out DEBUG_VALUE Out);

//    int CoerceValues(
//        [In] ULONG Count,
//        [In, MarshalAs(UnmanagedType.LPArray)] DEBUG_VALUE[] In,
//        [In, MarshalAs(UnmanagedType.LPArray)] DEBUG_VALUE_TYPE[] OutType,
//        [Out, MarshalAs(UnmanagedType.LPArray)] DEBUG_VALUE[] Out);

//    int Execute(
//        [In] DEBUG_OUTCTL OutputControl,
//        [In, MarshalAs(UnmanagedType.LPStr)] string Command,
//        [In] DEBUG_EXECUTE Flags);

//    int ExecuteCommandFile(
//        [In] DEBUG_OUTCTL OutputControl,
//        [In, MarshalAs(UnmanagedType.LPStr)] string CommandFile,
//        [In] DEBUG_EXECUTE Flags);

int WDebugControl::GetNumberBreakpoints(
    [Out] ULONG% Number)
{
    WDebugClient::g_log->Write( L"DebugControl::GetNumberBreakpoints" );
    pin_ptr<ULONG> pp = &Number;
    return m_pNative->GetNumberBreakpoints( pp );
}

//    int GetBreakpointByIndex(
//        [In] ULONG Index,
//        [Out, MarshalAs(UnmanagedType.Interface)] out IDebugBreakpoint bp);

//    int GetBreakpointById(
//        [In] ULONG Id,
//        [Out, MarshalAs(UnmanagedType.Interface)] out IDebugBreakpoint bp);

int WDebugControl::GetBreakpointParameters(
    //[In] ULONG Count,
    [In] array<ULONG>^ Ids,
    //[In] ULONG Start,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_BREAKPOINT_PARAMETERS>^% Params)
{
    WDebugClient::g_log->Write( L"DebugControl::GetBreakpointParameters" );
    Params = nullptr;

    if( !Ids )
        throw gcnew ArgumentNullException( L"Ids" );

    pin_ptr<ULONG> ppIds = &Ids[ 0 ];
    array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_BREAKPOINT_PARAMETERS>^ tmp = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_BREAKPOINT_PARAMETERS>( Ids->Length );
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_BREAKPOINT_PARAMETERS> ppTmp = &tmp[ 0 ];

    int hr = m_pNative->GetBreakpointParameters( Ids->Length,
                                                 ppIds,
                                                 0, // Start
                                                 (PDEBUG_BREAKPOINT_PARAMETERS) ppTmp );
    // S_FALSE indicates a deleted breakpoint. It's ID will be set to DEBUG_ANY_ID.
    if( (S_OK == hr) || (S_FALSE == hr) )
    {
        Params = tmp;
    }
    return hr;
}

int WDebugControl::GetBreakpointParameters(
    [In] ULONG Count,
    //[In] array<ULONG>^ Ids,
    [In] ULONG Start,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_BREAKPOINT_PARAMETERS>^% Params)
{
    WDebugClient::g_log->Write( L"DebugControl::GetBreakpointParameters(2)" );
    Params = nullptr;

    if( !Count )
        throw gcnew ArgumentException( L"You should request at least one breakpoint param.", L"Count" );

    array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_BREAKPOINT_PARAMETERS>^ tmp = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_BREAKPOINT_PARAMETERS>( Count );
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_BREAKPOINT_PARAMETERS> ppTmp = &tmp[ 0 ];

    int hr = m_pNative->GetBreakpointParameters( Count,
                                                 nullptr,
                                                 Start,
                                                 (PDEBUG_BREAKPOINT_PARAMETERS) ppTmp );
    // S_FALSE indicates a deleted breakpoint. It's ID will be set to DEBUG_ANY_ID.
    if( (S_OK == hr) || (S_FALSE == hr) )
    {
        Params = tmp;
    }
    return hr;
}

//    int AddBreakpoint(
//        [In] DEBUG_BREAKPOINT_TYPE Type,
//        [In] ULONG DesiredId,
//        [Out, MarshalAs(UnmanagedType.Interface)] out IDebugBreakpoint Bp);

//    int RemoveBreakpoint(
//        [In, MarshalAs(UnmanagedType.Interface)] IDebugBreakpoint Bp);

//    int AddExtension(
//        [In, MarshalAs(UnmanagedType.LPStr)] string Path,
//        [In] ULONG Flags,
//        [Out] out UInt64 Handle);

int WDebugControl::RemoveExtension(
    [In] UInt64 Handle)
{
    WDebugClient::g_log->Write( L"DebugControl::RemoveExtension" );
    return m_pNative->RemoveExtension( Handle );
}

      // Use GetExtensionByPathWide instead.
//    int GetExtensionByPath(
//        [In, MarshalAs(UnmanagedType.LPStr)] string Path,
//        [Out] out UInt64 Handle);

//    int CallExtension(
//        [In] UInt64 Handle,
//        [In, MarshalAs(UnmanagedType.LPStr)] string Function,
//        [In, MarshalAs(UnmanagedType.LPStr)] string Arguments);

//    int GetExtensionFunction(
//        [In] UInt64 Handle,
//        [In, MarshalAs(UnmanagedType.LPStr)] string FuncName,
//        [Out] out IntPtr Function);

//    int GetWindbgExtensionApis32(
//        [In, Out] ref WINDBG_EXTENSION_APIS Api);

//    /* Must be In and Out as the nSize member has to be initialized */

//    int GetWindbgExtensionApis64(
//        [In, Out] ref WINDBG_EXTENSION_APIS Api);

/* Must be In and Out as the nSize member has to be initialized */

int WDebugControl::GetNumberEventFilters(
    [Out] ULONG% SpecificEvents,
    [Out] ULONG% SpecificExceptions,
    [Out] ULONG% ArbitraryExceptions)
{
    WDebugClient::g_log->Write( L"DebugControl::GetNumberEventFilters" );
    pin_ptr<ULONG> ppSpecificEvents = &SpecificEvents;
    pin_ptr<ULONG> ppSpecificExceptions = &SpecificExceptions;
    pin_ptr<ULONG> ppArbitraryExceptions = &ArbitraryExceptions;
    return m_pNative->GetNumberEventFilters( ppSpecificEvents,
                                             ppSpecificExceptions,
                                             ppArbitraryExceptions );
}

//   int GetEventFilterText(
//       [In] ULONG Index,
//       [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
//       [In] Int32 BufferSize,
//       [Out] ULONG% TextSize);

//   int GetEventFilterCommand(
//       [In] ULONG Index,
//       [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
//       [In] Int32 BufferSize,
//       [Out] ULONG% CommandSize);

//   int SetEventFilterCommand(
//       [In] ULONG Index,
//       [In, MarshalAs(UnmanagedType.LPStr)] string Command);

int WDebugControl::GetSpecificFilterParameters(
    [In] ULONG Start,
    [In] ULONG Count,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_SPECIFIC_FILTER_PARAMETERS>^% Params)
{
    WDebugClient::g_log->Write( L"DebugControl::GetSpecificFilterParameters" );
    Params = nullptr;
    auto tmp = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_SPECIFIC_FILTER_PARAMETERS>( Count );
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_SPECIFIC_FILTER_PARAMETERS> pp = &tmp[ 0 ];
    int hr = m_pNative->GetSpecificFilterParameters( Start,
                                                     Count,
                                                     (PDEBUG_SPECIFIC_FILTER_PARAMETERS) pp );
    if( S_OK == hr )
    {
        Params = tmp;
    }
    return hr;
}

//   int SetSpecificFilterParameters(
//       [In] ULONG Start,
//       [In] ULONG Count,
//       [In, MarshalAs(UnmanagedType.LPArray)] DEBUG_SPECIFIC_FILTER_PARAMETERS[] Params);

//   int GetSpecificEventFilterArgument(
//       [In] ULONG Index,
//       [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
//       [In] Int32 BufferSize,
//       [Out] ULONG% ArgumentSize);

//   int SetSpecificEventFilterArgument(
//       [In] ULONG Index,
//       [In, MarshalAs(UnmanagedType.LPStr)] string Argument);

int WDebugControl::GetExceptionFilterParameters(
    //[In] ULONG Count,
    [In] array<ULONG>^ Codes,
    //[In] ULONG Start,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_EXCEPTION_FILTER_PARAMETERS>^% Params)
{
    WDebugClient::g_log->Write( L"DebugControl::GetExceptionFilterParameters" );
    Params = nullptr;
    if( !Codes )
        throw gcnew ArgumentNullException( L"Codes" );

    pin_ptr<ULONG> ppCodes = &Codes[ 0 ];
    array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_EXCEPTION_FILTER_PARAMETERS>^ tmp = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_EXCEPTION_FILTER_PARAMETERS>( Codes->Length );
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_EXCEPTION_FILTER_PARAMETERS> ppParams = &Params[ 0 ];

    int hr = m_pNative->GetExceptionFilterParameters( Codes->Length,
                                                      ppCodes,
                                                      0, // Start
                                                      (PDEBUG_EXCEPTION_FILTER_PARAMETERS) ppParams );
    if( S_OK == hr )
    {
        Params = tmp;
    }
    return hr;
}

int WDebugControl::GetExceptionFilterParameters(
    [In] ULONG Count,
    //[In] array<ULONG>^ Codes,
    [In] ULONG Start,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_EXCEPTION_FILTER_PARAMETERS>^% Params)
{
    WDebugClient::g_log->Write( L"DebugControl::GetExceptionFilterParameters(2)" );
    Params = nullptr;

    if( !Count )
        throw gcnew ArgumentException( L"You should request at least one exception filter parameter.", L"Count" );

    array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_EXCEPTION_FILTER_PARAMETERS>^ tmp = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_EXCEPTION_FILTER_PARAMETERS>( Count );
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_EXCEPTION_FILTER_PARAMETERS> ppParams = &tmp[ 0 ];

    int hr = m_pNative->GetExceptionFilterParameters( Count,
                                                      nullptr,
                                                      Start,
                                                      (PDEBUG_EXCEPTION_FILTER_PARAMETERS) ppParams );
    if( S_OK == hr )
    {
        Params = tmp;
    }
    return hr;
}

//   int SetExceptionFilterParameters(
//       [In] ULONG Count,
//       [In, MarshalAs(UnmanagedType.LPArray)] DEBUG_EXCEPTION_FILTER_PARAMETERS[] Params);

//   int GetExceptionFilterSecondCommand(
//       [In] ULONG Index,
//       [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
//       [In] Int32 BufferSize,
//       [Out] ULONG% CommandSize);

//   int SetExceptionFilterSecondCommand(
//       [In] ULONG Index,
//       [In, MarshalAs(UnmanagedType.LPStr)] string Command);

int WDebugControl::WaitForEvent(
    [In] DEBUG_WAIT Flags,
    [In] ULONG Timeout)
{
    WDebugClient::g_log->Write( L"DebugControl::WaitForEvent" );
    return m_pNative->WaitForEvent( (ULONG) Flags, Timeout );
}

//   int GetLastEventInformation(
//       [Out] out DEBUG_EVENT Type,
//       [Out] ULONG% ProcessId,
//       [Out] ULONG% ThreadId,
//       [In] IntPtr ExtraInformation,
//       [In] ULONG ExtraInformationSize,
//       [Out] ULONG% ExtraInformationUsed,
//       [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Description,
//       [In] Int32 DescriptionSize,
//       [Out] ULONG% DescriptionUsed);

/* IDebugControl2 */

//   int GetCurrentTimeDate(
//       [Out] ULONG% TimeDate);

//   int GetCurrentSystemUpTime(
//       [Out] ULONG% UpTime);

int WDebugControl::GetDumpFormatFlags(
    [Out] DEBUG_FORMAT% FormatFlags)
{
    WDebugClient::g_log->Write( L"DebugControl::GetDumpFormatFlags" );
    FormatFlags = (DEBUG_FORMAT) 0;
    pin_ptr<DEBUG_FORMAT> pp = &FormatFlags;
    return m_pNative->GetDumpFormatFlags( (PULONG) pp );
}

 int WDebugControl::GetNumberTextReplacements(
     [Out] ULONG% NumRepl)
 {
    pin_ptr<ULONG> pp = &NumRepl;
    return m_pNative->GetNumberTextReplacements( pp );
 }

//   int GetTextReplacement(
//       [In, MarshalAs(UnmanagedType.LPStr)] string SrcText,
//       [In] ULONG Index,
//       [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder SrcBuffer,
//       [In] Int32 SrcBufferSize,
//       [Out] ULONG% SrcSize,
//       [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder DstBuffer,
//       [In] Int32 DstBufferSize,
//       [Out] ULONG% DstSize);

//   int SetTextReplacement(
//       [In, MarshalAs(UnmanagedType.LPStr)] string SrcText,
//       [In, MarshalAs(UnmanagedType.LPStr)] string DstText);

int WDebugControl::RemoveTextReplacements()
{
    WDebugClient::g_log->Write( L"DebugControl::RemoveTextReplacements" );
    return m_pNative->RemoveTextReplacements();
}

//   int OutputTextReplacements(
//       [In] DEBUG_OUTCTL OutputControl,
//       [In] DEBUG_OUT_TEXT_REPL Flags);

//   /* IDebugControl3 */

int WDebugControl::GetAssemblyOptions(
    [Out] DEBUG_ASMOPT% Options)
{
    WDebugClient::g_log->Write( L"DebugControl::GetAssemblyOptions" );
   pin_ptr<DEBUG_ASMOPT> pp = &Options;
   return m_pNative->GetAssemblyOptions( (PULONG) pp );
}

int WDebugControl::AddAssemblyOptions(
    [In] DEBUG_ASMOPT Options)
{
    WDebugClient::g_log->Write( L"DebugControl::AddAssemblyOptions" );
    return m_pNative->AddAssemblyOptions( (ULONG) Options );
}

int WDebugControl::RemoveAssemblyOptions(
    [In] DEBUG_ASMOPT Options)
{
    WDebugClient::g_log->Write( L"DebugControl::RemoveAssemblyOptions" );
    return m_pNative->RemoveAssemblyOptions( (ULONG) Options );
}

int WDebugControl::SetAssemblyOptions(
    [In] DEBUG_ASMOPT Options)
{
    WDebugClient::g_log->Write( L"DebugControl::SetAssemblyOptions" );
    return m_pNative->SetAssemblyOptions( (ULONG) Options );
}

//   int GetExpressionSyntax(
//       [Out] out DEBUG_EXPR Flags);

//   int SetExpressionSyntax(
//       [In] DEBUG_EXPR Flags);

//   int SetExpressionSyntaxByName(
//       [In, MarshalAs(UnmanagedType.LPStr)] string AbbrevName);

//   int GetNumberExpressionSyntaxes(
//       [Out] ULONG% Number);

//   int GetExpressionSyntaxNames(
//       [In] ULONG Index,
//       [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder FullNameBuffer,
//       [In] Int32 FullNameBufferSize,
//       [Out] ULONG% FullNameSize,
//       [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder AbbrevNameBuffer,
//       [In] Int32 AbbrevNameBufferSize,
//       [Out] ULONG% AbbrevNameSize);

//   int GetNumberEvents(
//       [Out] ULONG% Events);

//   int GetEventIndexDescription(
//       [In] ULONG Index,
//       [In] DEBUG_EINDEX Which,
//       [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
//       [In] Int32 BufferSize,
//       [Out] ULONG% DescSize);

//   int GetCurrentEventIndex(
//       [Out] ULONG% Index);

//   int SetNextEventIndex(
//       [In] DEBUG_EINDEX Relation,
//       [In] ULONG Value,
//       [Out] ULONG% NextIndex);

/* IDebugControl4 */

//   int GetLogFileWide(
//       [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
//       [In] Int32 BufferSize,
//       [Out] ULONG% FileSize,
//       [Out, MarshalAs(UnmanagedType.Bool)] out bool Append);

//   int OpenLogFileWide(
//       [In, MarshalAs(UnmanagedType.LPWStr)] string File,
//       [In, MarshalAs(UnmanagedType.Bool)] bool Append);

//   int InputWide(
//       [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
//       [In] Int32 BufferSize,
//       [Out] ULONG% InputSize);

//   int ReturnInputWide(
//       [In, MarshalAs(UnmanagedType.LPWStr)] string Buffer);

//   int OutputWide(
//       [In] DEBUG_OUTPUT Mask,
//       [In, MarshalAs(UnmanagedType.LPWStr)] string Format);

//   int OutputVaListWide( /* THIS SHOULD NEVER BE CALLED FROM C# */
//       [In] DEBUG_OUTPUT Mask,
//       [In, MarshalAs(UnmanagedType.LPWStr)] string Format,
//       [In] IntPtr va_list_Args);

 int WDebugControl::ControlledOutputWide(
     [In] DEBUG_OUTCTL OutputControl,
     [In] DEBUG_OUTPUT Mask,
     [In] String^ Message)
 {
    marshal_context mc;
    return m_pNative->ControlledOutputWide( (ULONG) OutputControl,
                                            (ULONG) Mask,
                                            mc.marshal_as<const wchar_t*>( Message ) );
 }

//   int ControlledOutputVaListWide( /* THIS SHOULD NEVER BE CALLED FROM C# */
//       [In] DEBUG_OUTCTL OutputControl,
//       [In] DEBUG_OUTPUT Mask,
//       [In, MarshalAs(UnmanagedType.LPWStr)] string Format,
//       [In] IntPtr va_list_Args);

//   int OutputPromptWide(
//       [In] DEBUG_OUTCTL OutputControl,
//       [In, MarshalAs(UnmanagedType.LPWStr)] string Format);

//   int OutputPromptVaListWide( /* THIS SHOULD NEVER BE CALLED FROM C# */
//       [In] DEBUG_OUTCTL OutputControl,
//       [In, MarshalAs(UnmanagedType.LPWStr)] string Format,
//       [In] IntPtr va_list_Args);

//   int GetPromptTextWide(
//       [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
//       [In] Int32 BufferSize,
//       [Out] ULONG% TextSize);

//   int AssembleWide(
//       [In] UInt64 Offset,
//       [In, MarshalAs(UnmanagedType.LPWStr)] string Instr,
//       [Out] out UInt64 EndOffset);

int WDebugControl::DisassembleWide(
    [In] UInt64 Offset,
    [In] DEBUG_DISASM Flags,
    [Out] String^% Disassembly,
    //[Out] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% DisassemblySize,
    [Out] UInt64% EndOffset)
{
    WDebugClient::g_log->Write( L"DebugControl::DisassembleWide" );
    ULONG cchDisassembly = MAX_PATH;

    Disassembly = nullptr;
    EndOffset = 0;
    ULONG64 tmpEndOffset = 0;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszDisassembly( new wchar_t[ cchDisassembly ] );

        hr = m_pNative->DisassembleWide( Offset,
                                         (ULONG) Flags,
                                         wszDisassembly.get(),
                                         cchDisassembly,
                                         &cchDisassembly,
                                         &tmpEndOffset );
        if( S_OK == hr )
        {
            Disassembly = gcnew String( wszDisassembly.get() );
            EndOffset = tmpEndOffset;
        }
    }

    return hr;
}

//   int GetProcessorTypeNamesWide(
//       [In] IMAGE_FILE_MACHINE Type,
//       [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder FullNameBuffer,
//       [In] Int32 FullNameBufferSize,
//       [Out] ULONG% FullNameSize,
//       [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder AbbrevNameBuffer,
//       [In] Int32 AbbrevNameBufferSize,
//       [Out] ULONG% AbbrevNameSize);

//   int GetTextMacroWide(
//       [In] ULONG Slot,
//       [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
//       [In] Int32 BufferSize,
//       [Out] ULONG% MacroSize);

//   int SetTextMacroWide(
//       [In] ULONG Slot,
//       [In, MarshalAs(UnmanagedType.LPWStr)] string Macro);

//   int EvaluateWide(
//       [In, MarshalAs(UnmanagedType.LPWStr)] string Expression,
//       [In] DEBUG_VALUE_TYPE DesiredType,
//       [Out] out DEBUG_VALUE Value,
//       [Out] ULONG% RemainderIndex);

int WDebugControl::ExecuteWide(
    [In] DEBUG_OUTCTL OutputControl,
    [In] String^ Command,
    [In] DEBUG_EXECUTE Flags)
{
    WDebugClient::g_log->Write( L"DebugControl::ExecuteWide" );
    marshal_context mc;
    return m_pNative->ExecuteWide( (ULONG) OutputControl,
                                   mc.marshal_as<const wchar_t*>( Command ),
                                   (ULONG) Flags );
}

//   int ExecuteCommandFileWide(
//       [In] DEBUG_OUTCTL OutputControl,
//       [In, MarshalAs(UnmanagedType.LPWStr)] string CommandFile,
//       [In] DEBUG_EXECUTE Flags);

int WDebugControl::GetBreakpointByIndex2(
    [In] ULONG Index,
    [Out] WDebugBreakpoint^% bp)
{
    WDebugClient::g_log->Write( L"DebugControl::GetBreakpointByIndex2" );
    bp = nullptr;
    PDEBUG_BREAKPOINT2 pbp = nullptr;
    HRESULT hr = m_pNative->GetBreakpointByIndex2( Index, &pbp );
    if( S_OK == hr )
    {
        bp = WDebugBreakpoint::GetBreakpoint( (IntPtr) pbp );
    }
    return hr;
}

int WDebugControl::GetBreakpointById2(
    [In] ULONG Id,
    [Out] WDebugBreakpoint^% bp)
{
    WDebugClient::g_log->Write( L"DebugControl::GetBreakpointById2" );
    bp = nullptr;
    PDEBUG_BREAKPOINT2 pbp = nullptr;
    HRESULT hr = m_pNative->GetBreakpointById2( Id, &pbp );
    if( S_OK == hr )
    {
        bp = WDebugBreakpoint::GetBreakpoint( (IntPtr) pbp );
    }
    return hr;
}

int WDebugControl::AddBreakpoint2(
    [In] DEBUG_BREAKPOINT_TYPE Type,
    [In] ULONG DesiredId,
    [Out] WDebugBreakpoint^% Bp)
{
    WDebugClient::g_log->Write( L"DebugControl::AddBreakpoint2" );
    Bp = nullptr;
    PDEBUG_BREAKPOINT2 pbp = nullptr;
    HRESULT hr = m_pNative->AddBreakpoint2( (ULONG) Type,
                                            DesiredId,
                                            &pbp );
    if( S_OK == hr )
    {
        Bp = WDebugBreakpoint::GetBreakpoint( (IntPtr) pbp );
    }
    return hr;
}

int WDebugControl::RemoveBreakpoint2(
    [In] WDebugBreakpoint^ Bp)
{
    WDebugClient::g_log->Write( L"DebugControl::RemoveBreakpoint2" );
    // Nobody else should use this, as removing the breakpoint will delete it.
    // (the refcounting on it is a sham)
    PDEBUG_BREAKPOINT2 pbp = (PDEBUG_BREAKPOINT2) (void*) Bp->GetRaw();
    Bp->AbandonInterface();
    return m_pNative->RemoveBreakpoint2( pbp );
}

int WDebugControl::AddExtensionWide(
    [In] String^ Path,
    [In] ULONG Flags,
    [Out] UInt64% Handle)
{
    WDebugClient::g_log->Write( L"DebugControl::AddExtensionWide" );
    marshal_context mc;
    pin_ptr<UInt64> pp = &Handle;
    return m_pNative->AddExtensionWide( mc.marshal_as<const wchar_t*>( Path ),
                                        Flags,
                                        pp );
}

int WDebugControl::GetExtensionByPathWide(
    [In] String^ Path,
    [Out] UInt64% Handle)
{
    WDebugClient::g_log->Write( L"DebugControl::GetExtensionByPathWide" );
    Handle = 0;
    marshal_context mc;
    pin_ptr<UInt64> pp = &Handle;
    return m_pNative->GetExtensionByPathWide( mc.marshal_as<const wchar_t*>( Path ),
                                              pp );
}

int WDebugControl::CallExtensionWide(
    [In] UInt64 Handle,
    [In] String^ Function,
    [In] String^ Arguments)
{
    WDebugClient::g_log->Write( L"DebugControl::CallExtensionWide" );
    marshal_context mc;
    return m_pNative->CallExtensionWide( Handle,
                                         mc.marshal_as<const wchar_t*>( Function ),
                                         mc.marshal_as<const wchar_t*>( Arguments ) );
}

int WDebugControl::GetExtensionFunctionWide(
    [In] UInt64 Handle,
    [In] String^ FuncName,
    [Out] IntPtr% Function)
{
    WDebugClient::g_log->Write( L"DebugControl::GetExtensionFunctionWide" );
    marshal_context mc;
    pin_ptr<IntPtr> pp = &Function;
    return m_pNative->GetExtensionFunctionWide( Handle,
                                                mc.marshal_as<const wchar_t*>( FuncName ),
                                                reinterpret_cast<FARPROC*>( pp ) );
}

int WDebugControl::GetEventFilterTextWide(
    [In] ULONG Index,
    [Out] String^% FilterText )
    //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% TextSize)
{
    WDebugClient::g_log->Write( L"DebugControl::GetEventFilterTextWide" );
    ULONG cchFilterText = MAX_PATH;

    FilterText = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszFilterText( new wchar_t[ cchFilterText ] );

        hr = m_pNative->GetEventFilterTextWide( Index,
                                                wszFilterText.get(),
                                                cchFilterText,
                                                &cchFilterText );

        if( S_OK == hr )
        {
            FilterText = gcnew String( wszFilterText.get() );
        }
    }

    return hr;
}

int WDebugControl::GetEventFilterCommandWide(
    [In] ULONG Index,
    [Out] String^% Command )
    //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% CommandSize)
{
    WDebugClient::g_log->Write( L"DebugControl::GetEventFilterCommandWide" );
    ULONG cchCommand = MAX_PATH;

    Command = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszCommand( new wchar_t[ cchCommand ] );

        hr = m_pNative->GetEventFilterCommandWide( Index,
                                                   wszCommand.get(),
                                                   cchCommand,
                                                   &cchCommand );

        if( S_OK == hr )
        {
            Command = gcnew String( wszCommand.get() );
        }
    }

    return hr;
}

int WDebugControl::SetEventFilterCommandWide(
    [In] ULONG Index,
    [In] String^ Command)
{
    WDebugClient::g_log->Write( L"DebugControl::SetEventFilterCommandWide" );
    marshal_context mc;
    return m_pNative->SetEventFilterCommandWide( Index,
                                                 mc.marshal_as<const wchar_t*>( Command ) );
}

int WDebugControl::GetSpecificFilterArgumentWide(
    [In] ULONG Index,
    [Out] String^% Argument )
    //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% ArgumentSize)
{
    WDebugClient::g_log->Write( L"DebugControl::GetSpecificFilterArgumentWide" );
    ULONG cchArgument = MAX_PATH;

    Argument = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszArgument( new wchar_t[ cchArgument ] );

        hr = m_pNative->GetSpecificFilterArgumentWide( Index,
                                                       wszArgument.get(),
                                                       cchArgument,
                                                       &cchArgument );

        if( S_OK == hr )
        {
            Argument = gcnew String( wszArgument.get() );
        }
    }

    return hr;
}

int WDebugControl::SetSpecificFilterArgumentWide(
    [In] ULONG Index,
    [In] String^ Argument)
{
    WDebugClient::g_log->Write( L"DebugControl::SetSpecificFilterArgumentWide" );
    marshal_context mc;
    return m_pNative->SetSpecificFilterArgumentWide( Index,
                                                     mc.marshal_as<const wchar_t*>( Argument ) );
}

int WDebugControl::GetExceptionFilterSecondCommandWide(
    [In] ULONG Index,
    [Out] String^% Command )
    //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% CommandSize)
{
    WDebugClient::g_log->Write( L"DebugControl::GetExceptionFilterSecondCommandWide" );
    ULONG cchCommand = MAX_PATH;

    Command = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszCommand( new wchar_t[ cchCommand ] );

        hr = m_pNative->GetExceptionFilterSecondCommandWide( Index,
                                                             wszCommand.get(),
                                                             cchCommand,
                                                             &cchCommand );

        if( S_OK == hr )
        {
            Command = gcnew String( wszCommand.get() );
        }
    }

    return hr;
}

//   int SetExceptionFilterSecondCommandWide(
//       [In] ULONG Index,
//       [In, MarshalAs(UnmanagedType.LPWStr)] string Command);



int WDebugControl::GetLastEventInformationWide(
    [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT% Type,
    [Out] ULONG% ProcessId,
    [Out] ULONG% ThreadId,
    [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_LAST_EVENT_INFO% ExtraInformation,
    [Out] String^% Description)
{
    WDebugClient::g_log->Write( L"DebugControl::GetLastEventInformationWide" );
    ULONG cchName = MAX_PATH;
    ULONG cbExtraInfo = Marshal::SizeOf( ExtraInformation );

    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT> ppType = &Type;
    pin_ptr<ULONG> ppProcessId = &ProcessId;
    pin_ptr<ULONG> ppThreadId = &ThreadId;
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_LAST_EVENT_INFO> ppInfo = &ExtraInformation;

    Description = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszDescription( new wchar_t[ cchName ] );

        hr = m_pNative->GetLastEventInformationWide( (PULONG) ppType,
                                                     ppProcessId,
                                                     ppThreadId,
                                                     ppInfo,
                                                     cbExtraInfo,
                                                     &cbExtraInfo,
                                                     wszDescription.get(),
                                                     cchName,
                                                     &cchName );

        if( S_OK == hr )
        {
            Description = gcnew String( wszDescription.get() );
        }
        else
        {
            if( (S_FALSE == hr) &&
                (cbExtraInfo != Marshal::SizeOf( ExtraInformation )) )
            {
                throw gcnew Exception( String::Format( L"Unexpected size of ExtraInformation: {0} instead of {1}.",
                                                       cbExtraInfo,
                                                       Marshal::SizeOf( ExtraInformation ) ) );
            }
        }
    }

    return hr;
}


int WDebugControl::GetTextReplacementWide(
    [In] ULONG Index,
    [Out] String^% AliasName,
    [Out] String^% AliasValue)
{
    WDebugClient::g_log->Write( L"DebugControl::GetTextReplacementWide" );
    ULONG cchName = MAX_PATH / 2; // arbitrary estimate
    ULONG cchValue = MAX_PATH / 2; // arbitrary estimate
    AliasName = nullptr;
    AliasValue = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszName( new wchar_t[ cchName ] );
        std::unique_ptr<wchar_t[]> wszValue( new wchar_t[ cchValue ] );
        ZeroMemory( wszName.get(), cchName * sizeof( wchar_t ) );
        ZeroMemory( wszValue.get(), cchValue * sizeof( wchar_t ) );

        hr = m_pNative->GetTextReplacementWide( nullptr,
                                                Index,
                                                wszName.get(),
                                                cchName,
                                                &cchName,
                                                wszValue.get(),
                                                cchValue,
                                                &cchValue );
        if( S_OK == hr )
        {
            AliasName = gcnew String( wszName.get() );
            AliasValue = gcnew String( wszValue.get() );
        }
    }

    return hr;
}

int WDebugControl::GetTextReplacementWide(
    [In] String^ AliasName,
    [Out] String^% AliasValue)
{
    WDebugClient::g_log->Write( L"DebugControl::GetTextReplacementWide" );
    marshal_context mc;
    ULONG cchValue = MAX_PATH / 2; // arbitrary estimate
    AliasValue = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszValue( new wchar_t[ cchValue ] );
        ZeroMemory( wszValue.get(), cchValue * sizeof( wchar_t ) );

        hr = m_pNative->GetTextReplacementWide( mc.marshal_as<const wchar_t*>( AliasName ),
                                                0,       // Index
                                                nullptr, // SrcBuffer
                                                0,       // SrcBufferSize
                                                nullptr, // SrcSize
                                                wszValue.get(),
                                                cchValue,
                                                &cchValue );
        if( S_OK == hr )
        {
            AliasValue = gcnew String( wszValue.get() );
        }
    }

    return hr;
}


int WDebugControl::SetTextReplacementWide(
    [In] String^ AliasName,    // "SrcText" is a lousy param name
    [In] String^ AliasValue)   // "DrcText" is a lousy param name
{
    WDebugClient::g_log->Write( L"DebugControl::SetTextReplacementWide" );
    marshal_context mc;
    return m_pNative->SetTextReplacementWide( mc.marshal_as<const wchar_t*>( AliasName ),
                                              mc.marshal_as<const wchar_t*>( AliasValue ) );
}


//  int SetExpressionSyntaxByNameWide(
//      [In, MarshalAs(UnmanagedType.LPWStr)] string AbbrevName);

//  int GetExpressionSyntaxNamesWide(
//      [In] ULONG Index,
//      [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder FullNameBuffer,
//      [In] Int32 FullNameBufferSize,
//      [Out] ULONG% FullNameSize,
//      [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder AbbrevNameBuffer,
//      [In] Int32 AbbrevNameBufferSize,
//      [Out] ULONG% AbbrevNameSize);

//  int GetEventIndexDescriptionWide(
//      [In] ULONG Index,
//      [In] DEBUG_EINDEX Which,
//      [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
//      [In] Int32 BufferSize,
//      [Out] ULONG% DescSize);

//  int GetLogFile2(
//      [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
//      [In] Int32 BufferSize,
//      [Out] ULONG% FileSize,
//      [Out] out DEBUG_LOG Flags);

//  int OpenLogFile2(
//      [In, MarshalAs(UnmanagedType.LPStr)] string File,
//      [Out] out DEBUG_LOG Flags);

//  int GetLogFile2Wide(
//      [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
//      [In] Int32 BufferSize,
//      [Out] ULONG% FileSize,
//      [Out] out DEBUG_LOG Flags);

//  int OpenLogFile2Wide(
//      [In, MarshalAs(UnmanagedType.LPWStr)] string File,
//      [Out] out DEBUG_LOG Flags);

int WDebugControl::GetSystemVersionValues(
    [Out] ULONG% PlatformId,
    [Out] ULONG% Win32Major,
    [Out] ULONG% Win32Minor,
    [Out] ULONG% KdMajor,
    [Out] ULONG% KdMinor)
{
    PlatformId = 0;
    Win32Major = 0;
    Win32Minor = 0;
    KdMajor = 0;
    KdMinor = 0;

    WDebugClient::g_log->Write( L"WDebugControl::GetSystemVersionValues" );

    pin_ptr<ULONG> ppPlatformId = &PlatformId;
    pin_ptr<ULONG> ppWin32Major = &Win32Major;
    pin_ptr<ULONG> ppWin32Minor = &Win32Minor;
    pin_ptr<ULONG> ppKdMajor    = &KdMajor;
    pin_ptr<ULONG> ppKdMinor    = &KdMinor;

    return m_pNative->GetSystemVersionValues( ppPlatformId ,
                                              ppWin32Major ,
                                              ppWin32Minor ,
                                              ppKdMajor    ,
                                              ppKdMinor );
}

//  int GetSystemVersionString(
//      [In] DEBUG_SYSVERSTR Which,
//      [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
//      [In] Int32 BufferSize,
//      [Out] ULONG% StringSize);

//  int GetSystemVersionStringWide(
//      [In] DEBUG_SYSVERSTR Which,
//      [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
//      [In] Int32 BufferSize,
//      [Out] ULONG% StringSize);

int WDebugControl::GetSystemVersionStringWide(
    [In] DEBUG_SYSVERSTR Which,
    [Out] String^% VersionString)
{
    WDebugClient::g_log->Write( L"DebugControl::GetSystemVersionStringWide" );

    VersionString = nullptr;

    WCHAR buf[ 512 ] = { 0 };
    pin_ptr<WCHAR> ppBuf = &buf[ 0 ];

    ULONG cchUsed = 0;
    HRESULT hr = m_pNative->GetSystemVersionStringWide( static_cast< ULONG >( Which ),
                                                        ppBuf,
                                                        _countof( buf ),
                                                        &cchUsed );

    // S_FALSE means the string was truncated, but I don't really care.
    if( SUCCEEDED( hr ) )
    {
        VersionString = gcnew String( buf );
    }

    return hr;
}

//  int GetContextStackTrace(
//      [In] IntPtr StartContext,
//      [In] ULONG StartContextSize,
//      [Out, MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME[] Frames,
//      [In] Int32 FrameSize,
//      [In] IntPtr FrameContexts,
//      [In] ULONG FrameContextsSize,
//      [In] ULONG FrameContextsEntrySize,
//      [Out] ULONG% FramesFilled);

//  int OutputContextStackTrace(
//      [In] DEBUG_OUTCTL OutputControl,
//      [In, MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME[] Frames,
//      [In] Int32 FramesSize,
//      [In] IntPtr FrameContexts,
//      [In] ULONG FrameContextsSize,
//      [In] ULONG FrameContextsEntrySize,
//      [In] DEBUG_STACK Flags);

//  int GetStoredEventInformation(
//      [Out] out DEBUG_EVENT Type,
//      [Out] ULONG% ProcessId,
//      [Out] ULONG% ThreadId,
//      [In] IntPtr Context,
//      [In] ULONG ContextSize,
//      [Out] ULONG% ContextUsed,
//      [In] IntPtr ExtraInformation,
//      [In] ULONG ExtraInformationSize,
//      [Out] ULONG% ExtraInformationUsed);

//  int GetManagedStatus(
//      [Out] out DEBUG_MANAGED Flags,
//      [In] DEBUG_MANSTR WhichString,
//      [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder String,
//      [In] Int32 StringSize,
//      [Out] ULONG% StringNeeded);

//  int GetManagedStatusWide(
//      [Out] out DEBUG_MANAGED Flags,
//      [In] DEBUG_MANSTR WhichString,
//      [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder String,
//      [In] Int32 StringSize,
//      [Out] ULONG% StringNeeded);

//  int ResetManagedStatus(
//      [In] DEBUG_MANRESET Flags);

/* IDebugControl5 */

int WDebugControl::GetStackTraceEx(
    [In] UInt64 FrameOffset,
    [In] UInt64 StackOffset,
    [In] UInt64 InstructionOffset,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME_EX>^% Frames )
    //[In] Int32 FramesSize,
    //[Out] ULONG% FramesFilled)
{
    return GetStackTraceEx( FrameOffset,
                            StackOffset,
                            InstructionOffset,
                            0,
                            Frames );
}

int WDebugControl::GetStackTraceEx(
    [In] UInt64 FrameOffset,
    [In] UInt64 StackOffset,
    [In] UInt64 InstructionOffset,
    [In] Int32 MaxFrames, // <= 0 means "give me ALL the frames"
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME_EX>^% Frames )
    //[In] Int32 FramesSize,
    //[Out] ULONG% FramesFilled)
{
    WDebugClient::g_log->Write( L"DebugControl::GetStackTraceEx" );
    Frames = nullptr;
    ULONG numFramesAllocated;
    ULONG numFramesFilled = 0;
    array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME_EX>^ tmpArray = nullptr;

    if( MaxFrames <= 0 )
        numFramesAllocated = 1024;
    else
        numFramesAllocated = MaxFrames;

    do
    {
        tmpArray = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME_EX>( numFramesAllocated );
        pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME_EX> pp = &tmpArray[ 0 ];
        HRESULT hr = m_pNative->GetStackTraceEx( FrameOffset,
                                                 StackOffset,
                                                 InstructionOffset,
                                                 (PDEBUG_STACK_FRAME_EX) pp,
                                                 numFramesAllocated,
                                                 &numFramesFilled );
        // TODO: I don't know how to properly handle more than 1024 frames
        if( S_OK != hr )
            return hr;

        // MaxFrames <= 0 means "give me ALL the frames".
        if( (numFramesFilled == numFramesAllocated) && (MaxFrames <= 0) )
            numFramesAllocated = numFramesAllocated * 2;
        else
            break;
    } while( numFramesAllocated < (16 * 1024) );

    if( (numFramesFilled == numFramesAllocated) && (MaxFrames <= 0) )
    {
        // TODO: Log or otherwise somehow warn that we've truncated the [ridiculously
        // gigantic] stack?
    }

    System::Array::Resize( tmpArray, numFramesFilled );
    Frames = tmpArray;
    return S_OK;
}


//  int OutputStackTraceEx(
//      [In] ULONG OutputControl,
//      [In, MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME_EX[] Frames,
//      [In] Int32 FramesSize,
//      [In] DEBUG_STACK Flags);

//  int GetContextStackTraceEx(
//      [In] IntPtr StartContext,
//      [In] ULONG StartContextSize,
//      [Out, MarshalAs(UnmanagedType.LPArray)] DEBUG_STACK_FRAME_EX[] Frames,
//      [In] Int32 FramesSize,
//      [In] IntPtr FrameContexts,
//      [In] ULONG FrameContextsSize,
//      [In] ULONG FrameContextsEntrySize,
//      [Out] ULONG% FramesFilled);

//  int OutputContextStackTraceEx(
//      [In] ULONG OutputControl,
//      [In] DEBUG_STACK_FRAME_EX[] Frames,
//      [In] Int32 FramesSize,
//      [In] IntPtr FrameContexts,
//      [In] ULONG FrameContextsSize,
//      [In] ULONG FrameContextsEntrySize,
//      [In] DEBUG_STACK Flags);

int WDebugControl::GetBreakpointByGuid(
    [In] System::Guid Guid,
    [Out] WDebugBreakpoint^% Bp)
{
    WDebugClient::g_log->Write( L"DebugControl::GetBreakpointByGuid" );
    Bp = nullptr;
    pin_ptr<System::Guid> pp = &Guid;
    PDEBUG_BREAKPOINT3 pbp = nullptr;
    HRESULT hr = m_pNative->GetBreakpointByGuid( (LPGUID) pp,
                                                 &pbp );
    if( (S_OK == hr) && pbp )
    {
        Bp = WDebugBreakpoint::GetBreakpoint( pbp );
    }

    return hr;
}

/* IDebugControl6 */

int WDebugControl::GetExecutionStatusEx( [Out] DEBUG_STATUS% Status )
{
    WDebugClient::g_log->Write( L"DebugControl::GetExecutionStatusEx" );
    pin_ptr<DEBUG_STATUS> pp = &Status;
    return m_pNative->GetExecutionStatusEx( (PULONG) pp );
}

int WDebugControl::GetSynchronizationStatus(
    [Out] ULONG% SendsAttempted,
    [Out] ULONG% SecondsSinceLastResponse)
{
    WDebugClient::g_log->Write( L"DebugControl::GetSynchronizationStatus" );
    pin_ptr<ULONG> ppSendsAttempted = &SendsAttempted;
    pin_ptr<ULONG> ppSecondsSinceLastResponse = &SecondsSinceLastResponse;
    return m_pNative->GetSynchronizationStatus( ppSendsAttempted,
                                                ppSecondsSinceLastResponse );
}


//
// WDebugSystemObjects stuff
//

WDebugSystemObjects::WDebugSystemObjects( ::IDebugSystemObjects4* pDso )
    : WDebugEngInterface( pDso )
{
    WDebugClient::g_log->Write( L"Created DebugSystemObjects" );
} // end constructor


WDebugSystemObjects::WDebugSystemObjects( IntPtr pDso )
    : WDebugEngInterface( pDso )
{
    WDebugClient::g_log->Write( L"Created DebugSystemObjects" );
} // end constructor


int WDebugSystemObjects::GetEventThread(
    [Out] ULONG% Id)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetEventThread" );
    Id = DEBUG_ANY_ID;
    pin_ptr<ULONG> pp = &Id;
    return m_pNative->GetEventThread( pp );
}

int WDebugSystemObjects::GetEventProcess(
    [Out] ULONG% Id)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetEventProcess" );
    Id = DEBUG_ANY_ID;
    pin_ptr<ULONG> pp = &Id;
    return m_pNative->GetEventProcess( pp );
}

int WDebugSystemObjects::GetCurrentThreadId(
    [Out] ULONG% Id)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetCurrentThreadId" );
    Id = DEBUG_ANY_ID;
    pin_ptr<ULONG> pp = &Id;
    return m_pNative->GetCurrentThreadId( pp );
}

int WDebugSystemObjects::SetCurrentThreadId(
    [In] ULONG Id)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::SetCurrentThreadId" );
    return m_pNative->SetCurrentThreadId( Id );
}

int WDebugSystemObjects::GetCurrentProcessId(
    [Out] ULONG% Id)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetCurrentProcessId" );
    Id = DEBUG_ANY_ID;
    pin_ptr<ULONG> pp = &Id;
    return m_pNative->GetCurrentProcessId( pp );
}

int WDebugSystemObjects::SetCurrentProcessId(
    [In] ULONG Id)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::SetCurrentProcessId" );
    return m_pNative->SetCurrentProcessId( Id );
}

int WDebugSystemObjects::GetNumberThreads(
    [Out] ULONG% Number)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetNumberThreads" );
    pin_ptr<ULONG> pp = &Number;
    return m_pNative->GetNumberThreads( pp );
}

int WDebugSystemObjects::GetTotalNumberThreads(
    [Out] ULONG% Total,
    [Out] ULONG% LargestProcess)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetTotalNumberThreads" );
    pin_ptr<ULONG> ppTotal = &Total;
    pin_ptr<ULONG> ppLargestProcess = &LargestProcess;
    return m_pNative->GetTotalNumberThreads( ppTotal,
                                             ppLargestProcess );
}

int WDebugSystemObjects::GetThreadIdsByIndex(
    [In] ULONG Start,
    [In] ULONG Count,
    [Out] array<ULONG>^% Ids,
    [Out] array<ULONG>^% SysIds)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetThreadIdsByIndex" );
    Ids = gcnew array<ULONG>( Count );
    SysIds = gcnew array<ULONG>( Count );

    pin_ptr<ULONG> ppIds = &Ids[ 0 ];
    pin_ptr<ULONG> ppSysIds = &SysIds[ 0 ];

    return m_pNative->GetThreadIdsByIndex( Start,
                                           Count,
                                           ppIds,
                                           ppSysIds );
}

// int GetThreadIdByProcessor(
//     [In] ULONG Processor,
//     [Out] ULONG% Id);

int WDebugSystemObjects::GetCurrentThreadDataOffset(
    [Out] UInt64% Offset)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetCurrentThreadDataOffset" );
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetCurrentThreadDataOffset( pp );
}

// int GetThreadIdByDataOffset(
//     [In] UInt64 Offset,
//     [Out] ULONG% Id);

int WDebugSystemObjects::GetCurrentThreadTeb(
    [Out] UInt64% Offset)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetCurrentThreadTeb" );
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetCurrentThreadTeb( pp );
}

int WDebugSystemObjects::GetThreadIdByTeb(
    [In] UInt64 Offset,
    [Out] ULONG% Id)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetThreadIdByTeb" );
    pin_ptr<ULONG> pp = &Id;
    return m_pNative->GetThreadIdByTeb( Offset, pp );
}

int WDebugSystemObjects::GetCurrentThreadSystemId(
    [Out] ULONG% SysId)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetCurrentThreadSystemId" );
    pin_ptr<ULONG> pp = &SysId;
    return m_pNative->GetCurrentThreadSystemId( pp );
}

int WDebugSystemObjects::GetThreadIdBySystemId(
    [In] ULONG SysId,
    [Out] ULONG% Id)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetThreadIdBySystemId" );
    pin_ptr<ULONG> pp = &Id;
    return m_pNative->GetThreadIdBySystemId( SysId, pp );
}

// int GetCurrentThreadHandle(
//     [Out] UInt64 Handle);

// int GetThreadIdByHandle(
//     [In] UInt64 Handle,
//     [Out] ULONG% Id);

int WDebugSystemObjects::GetNumberProcesses(
    [Out] ULONG% Number)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetNumberProcesses" );
    pin_ptr<ULONG> pp = &Number;
    return m_pNative->GetNumberProcesses( pp );
}

int WDebugSystemObjects::GetProcessIdsByIndex(
    [In] ULONG Start,
    [In] ULONG Count,
    [Out] array<ULONG>^% Ids,
    [Out] array<ULONG>^% SysIds)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetProcessIdsByIndex" );
    if( 0 == Count )
    {
        Ids = gcnew array<ULONG>( 0 );
        SysIds = gcnew array<ULONG>( 0 );
        return 0;
    }

    Ids = nullptr;
    SysIds = nullptr;
    array<ULONG>^ tmpIds = gcnew array<ULONG>( Count );
    array<ULONG>^ tmpSysIds = gcnew array<ULONG>( Count );
    pin_ptr<ULONG> ppIds = &tmpIds[ 0 ];
    pin_ptr<ULONG> ppSysIds = &tmpSysIds[ 0 ];
    int hr = m_pNative->GetProcessIdsByIndex( Start, Count, ppIds, ppSysIds );
    if( 0 == hr )
    {
        Ids = tmpIds;
        SysIds = tmpSysIds;
    }
    return hr;
}

int WDebugSystemObjects::GetCurrentProcessDataOffset(
    [Out] UInt64% Offset)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetCurrentProcessDataOffset" );
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetCurrentProcessDataOffset( pp );
}

// int GetProcessIdByDataOffset(
//     [In] UInt64 Offset,
//     [Out] ULONG% Id);

int WDebugSystemObjects::GetCurrentProcessPeb(
    [Out] UInt64% Offset)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetCurrentProcessPeb" );
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetCurrentProcessPeb( pp );
}

// int GetProcessIdByPeb(
//     [In] UInt64 Offset,
//     [Out] ULONG% Id);

int WDebugSystemObjects::GetCurrentProcessSystemId(
    [Out] ULONG% SysId)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetCurrentProcessSystemId" );
    pin_ptr<ULONG> pp = &SysId;
    return m_pNative->GetCurrentProcessSystemId( pp );
}

int WDebugSystemObjects::GetProcessIdBySystemId(
    [In] ULONG SysId,
    [Out] ULONG% Id)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetProcessIdBySystemId" );
    pin_ptr<ULONG> pp = &Id;
    return m_pNative->GetProcessIdBySystemId( SysId, pp );
}

int WDebugSystemObjects::GetCurrentProcessHandle(
    [Out] UInt64% Handle)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetCurrentProcessHandle" );
    pin_ptr<UInt64> pp = &Handle;
    return m_pNative->GetCurrentProcessHandle( pp );
}

// int GetProcessIdByHandle(
//     [In] UInt64 Handle,
//     [Out] ULONG% Id);

//  int GetCurrentProcessExecutableName(
//      [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
//      [In] Int32 BufferSize,
//      [Out] ULONG% ExeSize);

//  /* IDebugSystemObjects2 */


// Returns "the number of seconds the current process has been running."
int WDebugSystemObjects::GetCurrentProcessUpTime(
    [Out] ULONG% UpTime)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetCurrentProcessUpTime" );
    pin_ptr<ULONG> pp = &UpTime;
    return m_pNative->GetCurrentProcessUpTime( pp );
}

int WDebugSystemObjects::GetImplicitThreadDataOffset(
    [Out] UInt64% Offset)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetImplicitThreadDataOffset" );
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetImplicitThreadDataOffset( pp );
}

int WDebugSystemObjects::SetImplicitThreadDataOffset(
    [In] UInt64 Offset)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::SetImplicitThreadDataOffset" );
    return m_pNative->SetImplicitThreadDataOffset( Offset );
}

int WDebugSystemObjects::GetImplicitProcessDataOffset(
    [Out] UInt64% Offset)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetImplicitProcessDataOffset" );
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetImplicitProcessDataOffset( pp );
}

int WDebugSystemObjects::SetImplicitProcessDataOffset(
    [In] UInt64 Offset)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::SetImplicitProcessDataOffset" );
    return m_pNative->SetImplicitProcessDataOffset( Offset );
}

int WDebugSystemObjects::GetEventSystem(
    [Out] ULONG% Id)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetEventSystem" );
    pin_ptr<ULONG> pp = &Id;
    return m_pNative->GetEventSystem( pp );
}

int WDebugSystemObjects::GetCurrentSystemId(
    [Out] ULONG% Id)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetCurrentSystemId" );
    pin_ptr<ULONG> pp = &Id;
    return m_pNative->GetCurrentSystemId( pp );
}

int WDebugSystemObjects::SetCurrentSystemId(
    [In] ULONG Id)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::SetCurrentSystemId" );
    return m_pNative->SetCurrentSystemId( Id );
}

int WDebugSystemObjects::GetNumberSystems(
    [Out] ULONG% Number)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetNumberSystems" );
    pin_ptr<ULONG> pp = &Number;
    return m_pNative->GetNumberSystems( pp );
}

int WDebugSystemObjects::GetSystemIdsByIndex(
    [In] ULONG Start,
    [In] ULONG Count,
    [Out] array<ULONG>^% Ids)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetSystemIdsByIndex" );
    if( 0 == Count )
    {
        Ids = gcnew array<ULONG>( 0 );
        return 0;
    }

    Ids = nullptr;
    array<ULONG>^ tmp = gcnew array<ULONG>( Count );
    pin_ptr<ULONG> pp = &tmp[ 0 ];
    int hr = m_pNative->GetSystemIdsByIndex( Start, Count, pp );
    if( 0 == hr )
    {
        Ids = tmp;
    }
    return hr;
}


int WDebugSystemObjects::GetCurrentProcessExecutableNameWide(
//  _Out_writes_opt_(BufferSize) PWSTR Buffer,
//  [In] ULONG BufferSize,
//  _Out_opt_ PULONG ExeSize);
    [Out] String^% Name)
{
    WDebugClient::g_log->Write( L"DebugSystemObjects::GetCurrentProcessExecutableNameWide" );
    Name = nullptr;
    ULONG cch = MAX_PATH;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszName( new wchar_t[ cch ] );

        hr = m_pNative->GetCurrentProcessExecutableNameWide( wszName.get(), cch, &cch );

        if( S_OK == hr )
        {
            Name = gcnew String( wszName.get() );
        }
    }

    return hr;
}


//
// WDebugSymbols stuff
//

WDebugSymbols::WDebugSymbols( ::IDebugSymbols5* pDs )
    : WDebugEngInterface( pDs )
{
    WDebugClient::g_log->Write( L"Created DebugSymbols" );
} // end constructor


WDebugSymbols::WDebugSymbols( IntPtr pDs )
    : WDebugEngInterface( pDs )
{
    WDebugClient::g_log->Write( L"Created DebugSymbols" );
} // end constructor


int WDebugSymbols::GetSymbolOptions(
    [Out] SYMOPT% Options)
{
    pin_ptr<SYMOPT> pp = &Options;
    return m_pNative->GetSymbolOptions( (PULONG) pp );
}

int WDebugSymbols::AddSymbolOptions(
    [In] SYMOPT Options)
{
    return m_pNative->AddSymbolOptions( (ULONG) Options );
}

int WDebugSymbols::RemoveSymbolOptions(
    [In] SYMOPT Options)
{
    return m_pNative->RemoveSymbolOptions( (ULONG) Options );
}

int WDebugSymbols::SetSymbolOptions(
    [In] SYMOPT Options)
{
    return m_pNative->SetSymbolOptions( (ULONG) Options );
}

int WDebugSymbols::GetNumberModules(
    [Out] ULONG% Loaded,
    [Out] ULONG% Unloaded)
{
    pin_ptr<ULONG> ppLoaded = &Loaded;
    pin_ptr<ULONG> ppUnloaded = &Unloaded;
    return m_pNative->GetNumberModules( ppLoaded,
                                        ppUnloaded );
}

int WDebugSymbols::GetModuleByIndex(
    [In] ULONG Index,
    [Out] UInt64% Base)
{
    pin_ptr<UInt64> pp = &Base;
    return m_pNative->GetModuleByIndex( Index, pp );
}

int WDebugSymbols::GetModuleByOffset(
    [In] UInt64 Offset,
    [In] ULONG StartIndex,
    [Out] ULONG% Index,
    [Out] UInt64% Base)
{
    pin_ptr<ULONG> ppIndex = &Index;
    pin_ptr<UInt64> ppBase = &Base;
    return m_pNative->GetModuleByOffset( Offset,
                                         StartIndex,
                                         ppIndex,
                                         ppBase );
}

int WDebugSymbols::GetModuleParameters(
    [In] ULONG Count,
    [In] array<UInt64>^ Bases,
    [In] ULONG Start,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_PARAMETERS>^% Params)
{
    // This is one of those interesting "dual-mode" APIs.
    Params = nullptr;
    array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_PARAMETERS>^ tmpParams = nullptr;
    if( !Bases )
    {
        if( 0 == Count )
            return E_INVALIDARG;

        tmpParams = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_PARAMETERS>( Count );
    }
    else
    {
        if( Start )
            return E_INVALIDARG;

        tmpParams = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_PARAMETERS>( Bases->Length );
    }

    pin_ptr<UInt64> ppBases = Bases ? &Bases[ 0 ] : nullptr;
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_PARAMETERS> ppParams = &tmpParams[ 0 ];
    int retval = m_pNative->GetModuleParameters( Count,
                                                 ppBases,
                                                 Start,
                                                 (PDEBUG_MODULE_PARAMETERS) ppParams );
    if( S_OK == retval )
    {
        Params = tmpParams;
    }
    return retval;
}


int WDebugSymbols::GetTypeSize(
    [In] UInt64 Module,
    [In] ULONG TypeId,
    [Out] ULONG% Size)
{
    pin_ptr<ULONG> pp = &Size;
    return m_pNative->GetTypeSize( Module, TypeId, pp );
}

int WDebugSymbols::GetOffsetTypeId(
    [In] UInt64 Offset,
    [Out] ULONG% TypeId,
    [Out] UInt64% Module)
{
    pin_ptr<ULONG> ppTypeId = &TypeId;
    pin_ptr<UInt64> ppModule = &Module;
    return m_pNative->GetOffsetTypeId( Offset, ppTypeId, ppModule );
}

//  int ReadTypedDataVirtual(
//      [In] UInt64 Offset,
//      [In] UInt64 Module,
//      [In] ULONG TypeId,
//      [In] IntPtr Buffer,
//      [In] ULONG BufferSize,
//      [Out] ULONG% BytesRead);

//  int WriteTypedDataVirtual(
//      [In] UInt64 Offset,
//      [In] UInt64 Module,
//      [In] ULONG TypeId,
//      [In] IntPtr Buffer,
//      [In] ULONG BufferSize,
//      [Out] ULONG% BytesWritten);

//  int OutputTypedDataVirtual(
//      [In] DEBUG_OUTCTL OutputControl,
//      [In] UInt64 Offset,
//      [In] UInt64 Module,
//      [In] ULONG TypeId,
//      [In] DEBUG_TYPEOPTS Flags);

//  int ReadTypedDataPhysical(
//      [In] UInt64 Offset,
//      [In] UInt64 Module,
//      [In] ULONG TypeId,
//      [In] IntPtr Buffer,
//      [In] ULONG BufferSize,
//      [Out] ULONG% BytesRead);

//  int WriteTypedDataPhysical(
//      [In] UInt64 Offset,
//      [In] UInt64 Module,
//      [In] ULONG TypeId,
//      [In] IntPtr Buffer,
//      [In] ULONG BufferSize,
//      [Out] ULONG% BytesWritten);

//  int OutputTypedDataPhysical(
//      [In] DEBUG_OUTCTL OutputControl,
//      [In] UInt64 Offset,
//      [In] UInt64 Module,
//      [In] ULONG TypeId,
//      [In] DEBUG_TYPEOPTS Flags);

//  int GetScope(
//      [Out] UInt64% InstructionOffset,
//      [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME% ScopeFrame,
//      [In] IntPtr ScopeContext,
//      [In] ULONG ScopeContextSize);

//  int SetScope(
//      [In] UInt64 InstructionOffset,
//      [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME ScopeFrame,
//      [In] IntPtr ScopeContext,
//      [In] ULONG ScopeContextSize);

int WDebugSymbols::ResetScope()
{
    WDebugClient::g_log->Write( L"DebugSymbols::ResetScope" );
    return m_pNative->ResetScope();
}

int WDebugSymbols::EndSymbolMatch(
    [In] UInt64 Handle)
{
    return m_pNative->EndSymbolMatch( Handle );
}


/* IDebugSymbols2 */

int WDebugSymbols::GetTypeOptions(
    [Out] DEBUG_TYPEOPTS% Options)
{
    pin_ptr<DEBUG_TYPEOPTS> pp = &Options;
    return m_pNative->GetTypeOptions( (PULONG) pp );
}

int WDebugSymbols::AddTypeOptions(
    [In] DEBUG_TYPEOPTS Options)
{
    return m_pNative->AddTypeOptions( (ULONG) Options );
}

int WDebugSymbols::RemoveTypeOptions(
    [In] DEBUG_TYPEOPTS Options)
{
    return m_pNative->RemoveTypeOptions( (ULONG) Options );
}

int WDebugSymbols::SetTypeOptions(
    [In] DEBUG_TYPEOPTS Options)
{
    return m_pNative->SetTypeOptions( (ULONG) Options );
}

/* IDebugSymbols3 */

int WDebugSymbols::GetNameByOffsetWide(
    [In] UInt64 Offset,
    [Out] String^% Name,
    //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder NameBuffer,
    //[In] Int32 NameBufferSize,
    //[Out] ULONG% NameSize,
    [Out] UInt64% Displacement)
{
    ULONG cchName = MAX_PATH;
    pin_ptr<UInt64> ppDisplacement = &Displacement;

    Name = nullptr;
    Displacement = 0;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszName( new wchar_t[ cchName ] );

        hr = m_pNative->GetNameByOffsetWide( Offset,
                                             wszName.get(),
                                             cchName,
                                             &cchName,
                                             ppDisplacement );

        if( S_OK == hr )
        {
            Name = gcnew String( wszName.get() );
        }
    }

    return hr;
}

int WDebugSymbols::GetOffsetByNameWide(
    [In] String^ Symbol,
    [Out] UInt64% Offset)
{
    marshal_context mc;
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetOffsetByNameWide( mc.marshal_as<const wchar_t*>( Symbol ),
                                           pp );
}

int WDebugSymbols::GetNearNameByOffsetWide(
    [In] UInt64 Offset,
    [In] int Delta,
    [Out] String^% Name,
    //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder NameBuffer,
    //[In] Int32 NameBufferSize,
    //[Out] ULONG% NameSize,
    [Out] UInt64% Displacement)
{
    ULONG cchName = MAX_PATH;
    pin_ptr<UInt64> ppDisplacement = &Displacement;

    Name = nullptr;
    Displacement = 0;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszName( new wchar_t[ cchName ] );

        hr = m_pNative->GetNearNameByOffsetWide( Offset,
                                                 Delta,
                                                 wszName.get(),
                                                 cchName,
                                                 &cchName,
                                                 ppDisplacement );

        if( S_OK == hr )
        {
            Name = gcnew String( wszName.get() );
        }
    }

    return hr;
}

int WDebugSymbols::GetLineByOffsetWide(
    [In] UInt64 Offset,
    [Out] ULONG% Line,
    [Out] String^% File,
    //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder FileBuffer,
    //[In] Int32 FileBufferSize,
    //[Out] ULONG% FileSize,
    [Out] UInt64% Displacement)
{
    ULONG cchFile = MAX_PATH;
    pin_ptr<UInt64> ppDisplacement = &Displacement;
    pin_ptr<ULONG> ppLine = &Line;

    File = nullptr;
    Line = 0;
    Displacement = 0;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszFile( new wchar_t[ cchFile ] );

        hr = m_pNative->GetLineByOffsetWide( Offset,
                                             ppLine,
                                             wszFile.get(),
                                             cchFile,
                                             &cchFile,
                                             ppDisplacement );

        if( S_OK == hr )
        {
            File = gcnew String( wszFile.get() );
        }
    }

    return hr;
}

int WDebugSymbols::GetOffsetByLineWide(
    [In] ULONG Line,
    [In] String^ File,
    [Out] UInt64% Offset)
{
    marshal_context mc;
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetOffsetByLineWide( Line,
                                           mc.marshal_as<const wchar_t*>( File ),
                                           pp );
}

int WDebugSymbols::GetModuleByModuleNameWide(
    [In] String^ Name,
    [In] ULONG StartIndex,
    [Out] ULONG% Index,
    [Out] UInt64% Base)
{
    marshal_context mc;
    pin_ptr<ULONG> ppIndex = &Index;
    pin_ptr<UInt64> ppBase = &Base;
    return m_pNative->GetModuleByModuleNameWide( mc.marshal_as<const wchar_t*>( Name ),
                                                 StartIndex,
                                                 ppIndex,
                                                 ppBase );
}

int WDebugSymbols::GetSymbolModuleWide(
    [In] String^ Symbol,
    [Out] UInt64% Base)
{
    marshal_context mc;
    pin_ptr<UInt64> pp = &Base;
    return m_pNative->GetSymbolModuleWide( mc.marshal_as<const wchar_t*>( Symbol ),
                                           pp );
}

int WDebugSymbols::GetTypeNameWide(
    [In] UInt64 Module,
    [In] ULONG TypeId,
    [Out] String^% TypeName)
    //[Out] StringBuilder NameBuffer,
    //[In] Int32 NameBufferSize,
    //[Out] ULONG% NameSize);
{
    ULONG cchName = MAX_PATH;

    TypeName = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszTypeName( new wchar_t[ cchName ] );

        hr = m_pNative->GetTypeNameWide( Module,
                                         TypeId,
                                         wszTypeName.get(),
                                         cchName,
                                         &cchName );

        if( S_OK == hr )
        {
            TypeName = gcnew String( wszTypeName.get() );
        }
    }

    return hr;
}

int WDebugSymbols::GetTypeIdWide(
    [In] UInt64 Module,
    [In] String^ Name,
    [Out] ULONG% TypeId)
{
    marshal_context mc;
    pin_ptr<ULONG> pp = &TypeId;

    return m_pNative->GetTypeIdWide( Module,
                                     mc.marshal_as<const wchar_t*>( Name ),
                                     pp );
}

int WDebugSymbols::GetFieldOffsetWide(
    [In] UInt64 Module,
    [In] ULONG TypeId,
    [In] String^ Field,
    [Out] ULONG% Offset)
{
    marshal_context mc;
    pin_ptr<ULONG> pp = &Offset;

    return m_pNative->GetFieldOffsetWide( Module,
                                          TypeId,
                                          mc.marshal_as<const wchar_t*>( Field ),
                                          pp );
}

int WDebugSymbols::GetSymbolTypeIdWide(
    [In] String^ Symbol,
    [Out] ULONG% TypeId,
    [Out] UInt64% Module)
{
    marshal_context mc;
    pin_ptr<ULONG> ppTypeId = &TypeId;
    pin_ptr<UInt64> ppModule = &Module;

    return m_pNative->GetSymbolTypeIdWide( mc.marshal_as<const wchar_t*>( Symbol ),
                                           ppTypeId,
                                           ppModule );
}

int WDebugSymbols::GetScopeSymbolGroup2(
    [In] DEBUG_SCOPE_GROUP Flags,
    [In] WDebugSymbolGroup^ Update, // optional
    [Out] WDebugSymbolGroup^% Symbols)
{
    Symbols = nullptr;
    PDEBUG_SYMBOL_GROUP2 pdsg = nullptr;

    int retval = m_pNative->GetScopeSymbolGroup2( (ULONG) Flags,
                                                  Update ? (PDEBUG_SYMBOL_GROUP2) (void*) Update->GetRaw() : nullptr,
                                                  &pdsg );
    if( S_OK == retval )
    {
        Symbols = gcnew WDebugSymbolGroup( pdsg );
    }
    return retval;
}

int WDebugSymbols::CreateSymbolGroup2(
    [Out] WDebugSymbolGroup^% Group)
{
    Group = nullptr;
    PDEBUG_SYMBOL_GROUP2 pdsg = nullptr;
    int retval = m_pNative->CreateSymbolGroup2( &pdsg );
    if( S_OK == retval )
    {
        Group = gcnew WDebugSymbolGroup( pdsg );
    }
    return retval;
}

int WDebugSymbols::StartSymbolMatchWide(
    [In] String^ Pattern,
    [Out] UInt64% Handle)
{
    marshal_context mc;
    pin_ptr<UInt64> pp = &Handle;

    return m_pNative->StartSymbolMatchWide( mc.marshal_as<const wchar_t*>( Pattern ),
                                            pp );
}

int WDebugSymbols::GetNextSymbolMatchWide(
    [In] UInt64 Handle,
    [Out] String^% Match,
    //[Out] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% MatchSize,
    [Out] UInt64% Offset)
{
    ULONG cchMatch = MAX_PATH;
    pin_ptr<UInt64> ppOffset = &Offset;

    Match = nullptr;
    Offset = 0;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszMatch( new wchar_t[ cchMatch ] );

        hr = m_pNative->GetNextSymbolMatchWide( Offset,
                                                wszMatch.get(),
                                                cchMatch,
                                                &cchMatch,
                                                ppOffset );

        if( S_OK == hr )
        {
            Match = gcnew String( wszMatch.get() );
        }
    }

    return hr;
}

int WDebugSymbols::ReloadWide(
    [In] String^ Module)
{
    WDebugClient::g_log->Write( L"DebugSymbols::ReloadWide" );
    marshal_context mc;
    return m_pNative->ReloadWide( mc.marshal_as<const wchar_t*>( Module ) );
}

int WDebugSymbols::GetSymbolPathWide(
    [Out] String^% Path)
    //[Out] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% PathSize);
{
    ULONG cchPath = 1024;

    Path = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszPath( new wchar_t[ cchPath ] );

        hr = m_pNative->GetSymbolPathWide( wszPath.get(),
                                           cchPath,
                                           &cchPath );

        if( S_OK == hr )
        {
            Path = gcnew String( wszPath.get() );
        }
    }

    return hr;
}

int WDebugSymbols::SetSymbolPathWide(
    [In] String^ Path)
{
    marshal_context mc;
    return m_pNative->SetSymbolPathWide( mc.marshal_as<const wchar_t*>( Path ) );
}

int WDebugSymbols::AppendSymbolPathWide(
    [In] String^ Addition)
{
    marshal_context mc;
    return m_pNative->AppendSymbolPathWide( mc.marshal_as<const wchar_t*>( Addition ) );
}

int WDebugSymbols::GetImagePathWide(
    [Out] String ^% ImagePath)
    //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% PathSize);
{
    ULONG cchImagePath = 1024;

    ImagePath = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszImagePath( new wchar_t[ cchImagePath ] );

        hr = m_pNative->GetImagePathWide( wszImagePath.get(),
                                          cchImagePath,
                                          &cchImagePath );

        if( S_OK == hr )
        {
            ImagePath = gcnew String( wszImagePath.get() );
        }
    }

    return hr;
}

int WDebugSymbols::SetImagePathWide(
    [In] String^ Path)
{
    marshal_context mc;

    return m_pNative->SetImagePathWide( mc.marshal_as<const wchar_t*>( Path ) );
}

int WDebugSymbols::AppendImagePathWide(
    [In] String^ Addition)
{
    marshal_context mc;

    return m_pNative->AppendImagePathWide( mc.marshal_as<const wchar_t*>( Addition ) );
}

int WDebugSymbols::GetSourcePathWide(
    [Out] String^% SourcePath)
    //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% PathSize);
{
    ULONG cchSourcePath = 1024;

    SourcePath = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszSourcePath( new wchar_t[ cchSourcePath ] );

        hr = m_pNative->GetSourcePathWide( wszSourcePath.get(),
                                           cchSourcePath,
                                           &cchSourcePath );

        if( S_OK == hr )
        {
            SourcePath = gcnew String( wszSourcePath.get() );
        }
    }

    return hr;
}

int WDebugSymbols::GetSourcePathElementWide(
    [In] ULONG Index,
    [Out] String^% Element)
    //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% ElementSize);
{
    ULONG cchElement = MAX_PATH;

    Element = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszElement( new wchar_t[ cchElement ] );

        hr = m_pNative->GetSourcePathElementWide( Index,
                                                  wszElement.get(),
                                                  cchElement,
                                                  &cchElement );

        if( S_OK == hr )
        {
            Element = gcnew String( wszElement.get() );
        }
    }

    return hr;
}

int WDebugSymbols::SetSourcePathWide(
    [In] String^ Path)
{
    marshal_context mc;

    return m_pNative->SetSourcePathWide( mc.marshal_as<const wchar_t*>( Path ) );
}

int WDebugSymbols::AppendSourcePathWide(
    [In] String^ Addition)
{
    marshal_context mc;

    return m_pNative->AppendSourcePathWide( mc.marshal_as<const wchar_t*>( Addition ) );
}

// int FindSourceFileWide(
//     [In] ULONG StartElement,
//     [In] String^ File,
//     [In] DEBUG_FIND_SOURCE Flags,
//     [Out] ULONG% FoundElement,
//     [Out] String^% Found);
//     //[Out] StringBuilder Buffer,
//     //[In] Int32 BufferSize,
//     //[Out] ULONG% FoundSize);

// int GetSourceFileLineOffsetsWide(
//     [In] String^ File,
//     [Out] array<UInt64>^% Buffer,
//     [In] Int32 BufferLines,
//     [Out] ULONG% FileLines);

// int WDebugSymbols::GetModuleVersionInformationWide(
//     [In] ULONG Index,
//     [In] UInt64 Base,
//     [In] String^ Item,
//     [In] IntPtr Buffer,
//     [In] Int32 BufferSize,
//     [Out] ULONG% VerInfoSize)
// {
// }

int WDebugSymbols::GetModuleVersionInformationWide_VS_FIXEDFILEINFO(
    [In] UInt64 Base,
    [Out] Microsoft::Diagnostics::Runtime::Interop::VS_FIXEDFILEINFO% fixedFileInfo )
{
    return _GetModuleVersionInformationWide_VS_FIXEDFILEINFO( DEBUG_ANY_ID,
                                                              Base,
                                                              fixedFileInfo );
}

int WDebugSymbols::GetModuleVersionInformationWide_VS_FIXEDFILEINFO(
    [In] ULONG Index,
    [Out] Microsoft::Diagnostics::Runtime::Interop::VS_FIXEDFILEINFO% fixedFileInfo )
{
    return _GetModuleVersionInformationWide_VS_FIXEDFILEINFO( Index,
                                                              0,
                                                              fixedFileInfo );
}

int WDebugSymbols::_GetModuleVersionInformationWide_VS_FIXEDFILEINFO(
    [In] ULONG Index,
    [In] UInt64 Base,
    [Out] Microsoft::Diagnostics::Runtime::Interop::VS_FIXEDFILEINFO% fixedFileInfo )
{
    pin_ptr<VOID> pp = &fixedFileInfo;
    ULONG actualSize = 0;
    int hr = m_pNative->GetModuleVersionInformationWide( Index,
                                                         Base,
                                                         L"\\",
                                                         pp,
                                                         //Marshal::SizeOf( Microsoft::Diagnostics::Runtime::Interop::VS_FIXEDFILEINFO ),
                                                         Marshal::SizeOf( fixedFileInfo ),
                                                         &actualSize );
    return hr;
}

int WDebugSymbols::GetModuleVersionInformationWide_Translations(
    [In] UInt64 Base,
    [Out] array<DWORD>^% LangCodepagePairs )
{
    return _GetModuleVersionInformationWide_Translations( DEBUG_ANY_ID,
                                                          Base,
                                                          LangCodepagePairs );
}

int WDebugSymbols::GetModuleVersionInformationWide_Translations(
    [In] ULONG Index,
    [Out] array<DWORD>^% LangCodepagePairs )
{
    return _GetModuleVersionInformationWide_Translations( Index,
                                                          0,
                                                          LangCodepagePairs );
}

int WDebugSymbols::_GetModuleVersionInformationWide_Translations(
    [In] ULONG Index,
    [In] UInt64 Base,
    [Out] array<DWORD>^% LangCodepagePairs )
{
    DWORD stackBuf[ 128 ] = { 0 };
    pin_ptr<VOID> pp = &stackBuf[ 0 ];
    ULONG actualSize = 0;
    LangCodepagePairs = nullptr;
    int hr = m_pNative->GetModuleVersionInformationWide( Index,
                                                         Base,
                                                         L"\\VarFileInfo\\Translation",
                                                         pp,
                                                         sizeof( stackBuf ),
                                                         &actualSize );
    if( hr )
        return hr;

    if( 0 != (actualSize % 4) )
        return E_UNEXPECTED;

    int numValues = actualSize / 4;
    LangCodepagePairs = gcnew array<DWORD>( numValues );

    for( int i = 0; i < numValues; i++ )
    {
        LangCodepagePairs[ i ] = stackBuf[ i ];
    }

    return S_OK;
}

int WDebugSymbols::GetModuleVersionInformationWide_StringInfo(
    [In] UInt64 Base,
    [In] DWORD LangCodepagePair,
    [In] String^ StringName,
    [Out] String^% StringValue )
{
    return _GetModuleVersionInformationWide_StringInfo( DEBUG_ANY_ID,
                                                        Base,
                                                        LangCodepagePair,
                                                        StringName,
                                                        StringValue );
}

int WDebugSymbols::GetModuleVersionInformationWide_StringInfo(
    [In] ULONG Index,
    [In] DWORD LangCodepagePair,
    [In] String^ StringName,
    [Out] String^% StringValue )
{
    return _GetModuleVersionInformationWide_StringInfo( Index,
                                                        0,
                                                        LangCodepagePair,
                                                        StringName,
                                                        StringValue );
}

int WDebugSymbols::_GetModuleVersionInformationWide_StringInfo(
    [In] ULONG Index,
    [In] UInt64 Base,
    [In] DWORD LangCodepagePair,
    [In] String^ StringName,
    [Out] String^% StringValue )
{
    WCHAR stackBuf[ 512 ] = { 0 };
    pin_ptr<VOID> pp = &stackBuf[ 0 ];
    ULONG actualSize = 0;
    StringValue = nullptr;
    marshal_context mc;
    String^ queryStr = String::Format( L"\\StringFileInfo\\{0:x4}{1:x4}\\{2}",
                                       LangCodepagePair & 0x0000ffff,
                                       (LangCodepagePair & 0xffff0000) >> 16,
                                       StringName );

    int hr = m_pNative->GetModuleVersionInformationWide( Index,
                                                         Base,
                                                         mc.marshal_as<const wchar_t*>( queryStr ),
                                                         pp,
                                                         sizeof( stackBuf ),
                                                         &actualSize );

    static const int hr_resNotFound = HRESULT_FROM_WIN32( ERROR_RESOURCE_TYPE_NOT_FOUND );
    if( hr && (hr != hr_resNotFound) )
        return hr;

    if( hr != hr_resNotFound )
        StringValue = gcnew String( stackBuf, 0, actualSize / 2 );

    return S_OK;
}


int WDebugSymbols::GetModuleNameStringWide(
    [In] DEBUG_MODNAME Which,
    [In] ULONG Index,
    [In] UInt64 Base,
    [Out] String^% Name)
    //[Out] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% NameSize);
{
    return GetModuleNameStringWide( Which,
                                    Index,
                                    Base,
                                    MAX_PATH,
                                    Name );
}

int WDebugSymbols::GetModuleNameStringWide(
    [In] DEBUG_MODNAME Which,
    [In] ULONG Index,
    [In] UInt64 Base,
    [In] ULONG NameSizeHint,
    [Out] String^% Name)
{
    ULONG cchName = NameSizeHint + 1;

    Name = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszName( new wchar_t[ cchName ] );

        hr = m_pNative->GetModuleNameStringWide( (ULONG) Which,
                                                 Index,
                                                 Base,
                                                 wszName.get(),
                                                 cchName,
                                                 &cchName );

        if( S_OK == hr )
        {
            Name = gcnew String( wszName.get() );
        }
    }

    return hr;
}

int WDebugSymbols::GetConstantNameWide(
    [In] UInt64 Module,
    [In] ULONG TypeId,
    [In] UInt64 Value,
    [Out] String^% Name)
    //[Out] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% NameSize);
{
    ULONG cchName = MAX_PATH;

    Name = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszName( new wchar_t[ cchName ] );

        hr = m_pNative->GetConstantNameWide( Module,
                                             TypeId,
                                             Value,
                                             wszName.get(),
                                             cchName,
                                             &cchName );

        if( S_OK == hr )
        {
            Name = gcnew String( wszName.get() );
        }
    }

    return hr;
}

int WDebugSymbols::GetFieldNameWide(
    [In] UInt64 Module,
    [In] ULONG TypeId,
    [In] ULONG FieldIndex,
    [Out] String^% Name)
    //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% NameSize);
{
    ULONG cchName = MAX_PATH;

    Name = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszName( new wchar_t[ cchName ] );

        hr = m_pNative->GetFieldNameWide( Module,
                                          TypeId,
                                          FieldIndex,
                                          wszName.get(),
                                          cchName,
                                          &cchName );

        if( S_OK == hr )
        {
            Name = gcnew String( wszName.get() );
        }
    }

    return hr;
}

int WDebugSymbols::IsManagedModule(
    [In] ULONG Index,
    [In] UInt64 Base)
{
    return m_pNative->IsManagedModule( Index, Base );
}

int WDebugSymbols::GetModuleByModuleName2Wide(
    [In] String^ Name,
    [In] ULONG StartIndex,
    [In] DEBUG_GETMOD Flags,
    [Out] ULONG% Index,
    [Out] UInt64% Base)
{
    marshal_context mc;
    pin_ptr<ULONG> ppIndex = &Index;
    pin_ptr<UInt64> ppBase = &Base;

    return m_pNative->GetModuleByModuleName2Wide( mc.marshal_as<const wchar_t*>( Name ),
                                                  StartIndex,
                                                  (ULONG) Flags,
                                                  ppIndex,
                                                  ppBase );
}

int WDebugSymbols::GetModuleByOffset2(
    [In] UInt64 Offset,
    [In] ULONG StartIndex,
    [In] DEBUG_GETMOD Flags,
    [Out] ULONG% Index,
    [Out] UInt64% Base)
{
    pin_ptr<ULONG> ppIndex = &Index;
    pin_ptr<UInt64> ppBase = &Base;

    return m_pNative->GetModuleByOffset2( Offset,
                                          StartIndex,
                                          (ULONG) Flags,
                                          ppIndex,
                                          ppBase );
}

int WDebugSymbols::AddSyntheticModuleWide(
    [In] UInt64 Base,
    [In] ULONG Size,
    [In] String^ ImagePath,
    [In] String^ ModuleName,
    [In] DEBUG_ADDSYNTHMOD Flags)
{
    marshal_context mc;

    return m_pNative->AddSyntheticModuleWide( Base,
                                              Size,
                                              mc.marshal_as<const wchar_t*>( ImagePath ),
                                              mc.marshal_as<const wchar_t*>( ModuleName ),
                                              (ULONG) Flags );
}

int WDebugSymbols::RemoveSyntheticModule(
    [In] UInt64 Base)
{
    return m_pNative->RemoveSyntheticModule( Base );
}

int WDebugSymbols::GetCurrentScopeFrameIndex(
    [Out] ULONG% Index)
{
    WDebugClient::g_log->Write( L"DebugSymbols::GetCurrentScopeFrameIndex" );
    pin_ptr<ULONG> pp = &Index;

    return m_pNative->GetCurrentScopeFrameIndex( pp );
}

int WDebugSymbols::SetScopeFrameByIndex(
    [In] ULONG Index)
{
    WDebugClient::g_log->Write( L"DebugSymbols::SetScopeFrameByIndex" );
    return m_pNative->SetScopeFrameByIndex( Index );
}

int WDebugSymbols::SetScopeFromJitDebugInfo(
    [In] ULONG OutputControl,
    [In] UInt64 InfoOffset)
{
    WDebugClient::g_log->Write( L"DebugSymbols::SetScopeFromJitDebugInfo" );
    return m_pNative->SetScopeFromJitDebugInfo( OutputControl, InfoOffset );
}

int WDebugSymbols::SetScopeFromStoredEvent()
{
    WDebugClient::g_log->Write( L"DebugSymbols::SetScopeFromStoredEvent" );
    return m_pNative->SetScopeFromStoredEvent();
}

// int OutputSymbolByOffset(
//     [In] ULONG OutputControl,
//     [In] DEBUG_OUTSYM Flags,
//     [In] UInt64 Offset);

// int GetFunctionEntryByOffset(
//     [In] UInt64 Offset,
//     [In] DEBUG_GETFNENT Flags,
//     [In] IntPtr Buffer,
//     [In] ULONG BufferSize,
//     [Out] ULONG% BufferNeeded);

int WDebugSymbols::GetFieldTypeAndOffsetWide(
    [In] UInt64 Module,
    [In] ULONG ContainerTypeId,
    [In] String^ Field,
    [Out] ULONG% FieldTypeId,
    [Out] ULONG% Offset)
{
    marshal_context mc;
    pin_ptr<ULONG> ppFieldTypeId = &FieldTypeId;
    pin_ptr<ULONG> ppOffset = &Offset;

    return m_pNative->GetFieldTypeAndOffsetWide( Module,
                                                 ContainerTypeId,
                                                 mc.marshal_as<const wchar_t*>( Field ),
                                                 ppFieldTypeId,
                                                 ppOffset );
}

int WDebugSymbols::AddSyntheticSymbolWide(
    [In] UInt64 Offset,
    [In] ULONG Size,
    [In] String^ Name,
    [In] DEBUG_ADDSYNTHSYM Flags,
    [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID% Id)
{
    marshal_context mc;
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID> pp = &Id;

    return m_pNative->AddSyntheticSymbolWide( Offset,
                                              Size,
                                              mc.marshal_as<const wchar_t*>( Name ),
                                              (ULONG) Flags,
                                              (PDEBUG_MODULE_AND_ID) pp );
}

int WDebugSymbols::RemoveSyntheticSymbol([In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID Id)
{
    return m_pNative->RemoveSyntheticSymbol( (PDEBUG_MODULE_AND_ID) &Id );
}

int WDebugSymbols::GetSymbolEntriesByOffset(
    [In] UInt64 Offset,
    [In] ULONG Flags,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID>^% Ids,
    [Out] array<UInt64>^% Displacements,
    [In] ULONG IdsCount)
    //[Out] ULONG% Entries);
{
    ULONG count = 10;
    Ids = nullptr;
    Displacements = nullptr;
    HRESULT hr = S_FALSE;

    while( S_FALSE == hr )
    {
        array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID>^ tmpIdsArray = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID>( (int) count );
        pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID> ppIds = &tmpIdsArray[ 0 ];

        array<UInt64>^ tmpDispsArray = gcnew array<UInt64>( (int) count );
        pin_ptr<UInt64> ppDisps = &tmpDispsArray[ 0 ];

        hr = m_pNative->GetSymbolEntriesByOffset( Offset,
                                                  Flags,
                                                  (PDEBUG_MODULE_AND_ID) ppIds,
                                                  ppDisps,
                                                  count,
                                                  &count );

        if( S_OK == hr )
        {
            array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID>::Resize( tmpIdsArray, (int) count );
            array<UInt64>::Resize( tmpDispsArray, (int) count );
            Ids = tmpIdsArray;
            Displacements = tmpDispsArray;
        }
    }

    return hr;
}

int WDebugSymbols::GetSymbolEntriesByNameWide(
    [In] String^ Symbol,
    [In] ULONG Flags,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID>^% Ids)
    //[In] ULONG IdsCount,
    //[Out] ULONG% Entries);
{
    marshal_context mc;
    ULONG count = 10;

    Ids = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID>^ tmpArray = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID>( (int) count );
        pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID> pp = &tmpArray[ 0 ];

        hr = m_pNative->GetSymbolEntriesByNameWide( mc.marshal_as<const wchar_t*>( Symbol ),
                                                    Flags,
                                                    (PDEBUG_MODULE_AND_ID) pp,
                                                    count,
                                                    &count );

        if( S_OK == hr )
        {
            array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID>::Resize( tmpArray, (int) count );
            Ids = tmpArray;
        }
    }

    return hr;
}

int WDebugSymbols::GetSymbolEntryByToken(
    [In] UInt64 ModuleBase,
    [In] ULONG Token,
    [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID% Id)
{
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID> pp = &Id;
    return m_pNative->GetSymbolEntryByToken( ModuleBase,
                                             Token,
                                             (PDEBUG_MODULE_AND_ID) pp );
}

int WDebugSymbols::GetSymbolEntryInformation(
    [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID* Id,
    [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_ENTRY% Info)
{
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_ENTRY> pp = &Info;

    memset( pp, 0, sizeof(::DEBUG_SYMBOL_ENTRY) );
    return m_pNative->GetSymbolEntryInformation( (PDEBUG_MODULE_AND_ID) Id,
                                                 (PDEBUG_SYMBOL_ENTRY) pp );
}

// int GetSymbolEntryStringWide(
//     [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID Id,
//     [In] ULONG Which,
//     [Out] String^% SymbolEntry);
//     //[Out] StringBuilder Buffer,
//     //[In] Int32 BufferSize,
//     //[Out] ULONG% StringSize);

// int GetSymbolEntryOffsetRegions(
//     [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID Id,
//     [In] ULONG Flags,
//     [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_OFFSET_REGION>^% Regions);
//     //[In] ULONG RegionsCount,
//     //[Out] ULONG% RegionsAvail);

// [Obsolete( "Do not use: no longer implemented.", true )]
// int GetSymbolEntryBySymbolEntry(
//     [In, MarshalAs(UnmanagedType.LPStruct)] DEBUG_MODULE_AND_ID FromId,
//     [In] ULONG Flags,
//     [Out] out DEBUG_MODULE_AND_ID ToId);

// int GetSourceEntriesByOffset(
//     [In] UInt64 Offset,
//     [In] ULONG Flags,
//     [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_SOURCE_ENTRY>^% Entries);
//     //[In] ULONG EntriesCount,
//     //[Out] ULONG% EntriesAvail);

// int GetSourceEntriesByLineWide(
//     [In] ULONG Line,
//     [In] String^ File,
//     [In] ULONG Flags,
//     [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_SOURCE_ENTRY>^% Entries);
//     //[In] ULONG EntriesCount,
//     //[Out] ULONG% EntriesAvail);

// int GetSourceEntryStringWide(
//     [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_SOURCE_ENTRY Entry,
//     [In] ULONG Which,
//     [Out] String^% SourceEntry);
//     //[Out] StringBuilder Buffer,
//     //[In] Int32 BufferSize,
//     //[Out] ULONG% StringSize);

// int GetSourceEntryOffsetRegions(
//     [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_SOURCE_ENTRY Entry,
//     [In] ULONG Flags,
//     [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_OFFSET_REGION>^% Regions);
//     //[In] ULONG RegionsCount,
//     //[Out] ULONG% RegionsAvail);

// int GetSourceEntryBySourceEntry(
//     [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_SOURCE_ENTRY FromEntry,
//     [In] ULONG Flags,
//     [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_SOURCE_ENTRY% ToEntry);

/* IDebugSymbols4 */

int WDebugSymbols::GetScopeEx(
    [Out] UInt64% InstructionOffset,
    [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME_EX% ScopeFrame,
    [In] IntPtr ScopeContext,
    [In] ULONG ScopeContextSize)
{
    WDebugClient::g_log->Write( L"DebugSymbols::GetScopeEx" );
    pin_ptr<UInt64> ppInstOffset = &InstructionOffset;
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME_EX> ppScopeFrame = &ScopeFrame;

    return m_pNative->GetScopeEx( ppInstOffset,
                                  (PDEBUG_STACK_FRAME_EX) ppScopeFrame,
                                  (PVOID) ScopeContext,
                                  ScopeContextSize );
}

int WDebugSymbols::SetScopeEx(
    [In] UInt64 InstructionOffset,
    [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME_EX ScopeFrame,
    [In] IntPtr ScopeContext,
    [In] ULONG ScopeContextSize)
{
    WDebugClient::g_log->Write( L"DebugSymbols::SetScopeEx" );
    return m_pNative->SetScopeEx( InstructionOffset,
                                  (PDEBUG_STACK_FRAME_EX) &ScopeFrame,
                                  (PVOID) ScopeContext,
                                  ScopeContextSize );
}

int WDebugSymbols::GetNameByInlineContextWide(
    [In] UInt64 Offset,
    [In] ULONG InlineContext,
    [Out] String^% Name,
    //[Out] StringBuilder NameBuffer,
    //[In] Int32 NameBufferSize,
    //[Out] ULONG% NameSize,
    [Out] UInt64% Displacement)
{
    ULONG cchName = MAX_PATH;
    pin_ptr<UInt64> ppDisplacement = &Displacement;

    Name = nullptr;
    Displacement = 0;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszName( new wchar_t[ cchName ] );

        hr = m_pNative->GetNameByInlineContextWide( Offset,
                                                    InlineContext,
                                                    wszName.get(),
                                                    cchName,
                                                    &cchName,
                                                    ppDisplacement );

        if( S_OK == hr )
        {
            Name = gcnew String( wszName.get() );
        }
    }

    return hr;
}

//  int GetLineByInlineContextWide(
//      [In] UInt64 Offset,
//      [In] ULONG InlineContext,
//      [Out] ULONG% Line,
//      [Out] String^% File,
//      //[Out] StringBuilder FileBuffer,
//      //[In] Int32 FileBufferSize,
//      //[Out] ULONG% FileSize,
//      [Out] UInt64% Displacement);

//  int OutputSymbolByInlineContext(
//      [In] ULONG OutputControl,
//      [In] ULONG Flags,
//      [In] UInt64 Offset,
//      [In] ULONG InlineContext);

/* IDebugSymbols5 */

int WDebugSymbols::GetCurrentScopeFrameIndexEx(
    [In] DEBUG_FRAME Flags,
    [Out] ULONG% Index)
{
    WDebugClient::g_log->Write( L"DebugSymbols::GetCurrentScopeFrameIndexEx" );
    pin_ptr<ULONG> pp = &Index;
    return m_pNative->GetCurrentScopeFrameIndexEx( (ULONG) Flags, pp );
}

int WDebugSymbols::SetScopeFrameByIndexEx(
    [In] DEBUG_FRAME Flags,
    [In] ULONG Index)
{
    WDebugClient::g_log->Write( L"DebugSymbols::SetScopeFrameByIndexEx" );
    return m_pNative->SetScopeFrameByIndexEx( (ULONG) Flags, Index );
}


//
// WDebugSymbolGroup stuff
//

WDebugSymbolGroup::WDebugSymbolGroup( ::IDebugSymbolGroup2* pDsg )
{
    if( !pDsg )
        throw gcnew ArgumentNullException( L"pDsg" );

    m_pDsg = pDsg;
} // end constructor


WDebugSymbolGroup::WDebugSymbolGroup( IntPtr pDsg )
{
    m_pDsg = (::IDebugSymbolGroup2*) (void*) pDsg;
} // end constructor


int WDebugSymbolGroup::GetNumberSymbols(
    [Out] ULONG% Number)
{
    pin_ptr<ULONG> pp = &Number;
    return m_pDsg->GetNumberSymbols( pp );
}

int WDebugSymbolGroup::RemoveSymbolByIndex(
    [In] ULONG Index)
{
    return m_pDsg->RemoveSymbolByIndex( Index );
}

int WDebugSymbolGroup::GetSymbolParameters(
    [In] ULONG Start,
    [In] ULONG Count,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_PARAMETERS>^% Params)
{
    Params = nullptr;
    array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_PARAMETERS>^ tmp = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_PARAMETERS>( Count );
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_PARAMETERS> pp = &tmp[ 0 ];

    int retval = m_pDsg->GetSymbolParameters( Start,
                                              Count,
                                              (PDEBUG_SYMBOL_PARAMETERS) pp );

    if( S_OK == retval )
    {
        Params = tmp;
    }
    return retval;
}

int WDebugSymbolGroup::ExpandSymbol(
    [In] ULONG Index,
    [In] Boolean Expand)
{
    return m_pDsg->ExpandSymbol( Index, Expand );
}

// #pragma push_macro("DEBUG_OUTPUT_SYMBOLS")
// #undef DEBUG_OUTPUT_SYMBOLS
// int WDebugSymbolGroup::OutputSymbols(
//     [In] DEBUG_OUTCTL OutputControl,
//     [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_OUTPUT_SYMBOLS Flags,
//     [In] ULONG Start,
//     [In] ULONG Count)
// {
// }
// #pragma pop_macro("DEBUG_OUTPUT_SYMBOLS")

/* IDebugSymbolGroup2 */

int WDebugSymbolGroup::AddSymbolWide(
    [In] String^ Name,
    [In, Out] ULONG% Index)
{
    marshal_context mc;
    pin_ptr<ULONG> pp = &Index;
    return m_pDsg->AddSymbolWide( mc.marshal_as<const wchar_t*>( Name ),
                                  pp );
}

int WDebugSymbolGroup::RemoveSymbolByNameWide(
    [In] String^ Name)
{
    marshal_context mc;
    return m_pDsg->RemoveSymbolByNameWide( mc.marshal_as<const wchar_t*>( Name ) );
}

int WDebugSymbolGroup::GetSymbolNameWide(
    [In] ULONG Index,
    [Out] String^% Name)
    //[Out] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% NameSize);
{
    ULONG cchName = MAX_PATH;

    Name = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszName( new wchar_t[ cchName ] );

        hr = m_pDsg->GetSymbolNameWide( Index,
                                        wszName.get(),
                                        cchName,
                                        &cchName );

        if( S_OK == hr )
        {
            Name = gcnew String( wszName.get() );
        }
    }

    return hr;
}

int WDebugSymbolGroup::WriteSymbolWide(
    [In] ULONG Index,
    [In] String^ Value)
{
    marshal_context mc;
    return m_pDsg->WriteSymbolWide( Index, mc.marshal_as<const wchar_t*>( Value ) );
}

// This would be better named "ChangeType"...
int WDebugSymbolGroup::OutputAsTypeWide(
    [In] ULONG Index,
    [In] String^ Type)
{
    marshal_context mc;
    return m_pDsg->OutputAsTypeWide( Index, mc.marshal_as<const wchar_t*>( Type ) );
}

int WDebugSymbolGroup::GetSymbolTypeNameWide(
    [In] ULONG Index,
    [Out] String^% Name)
    //[Out] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% NameSize);
{
    ULONG cchName = MAX_PATH;

    Name = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszName( new wchar_t[ cchName ] );

        hr = m_pDsg->GetSymbolTypeNameWide( Index,
                                            wszName.get(),
                                            cchName,
                                            &cchName );

        if( S_OK == hr )
        {
            Name = gcnew String( wszName.get() );
        }
    }

    return hr;
}

int WDebugSymbolGroup::GetSymbolSize(
    [In] ULONG Index,
    [Out] ULONG% Size)
{
    pin_ptr<ULONG> pp = &Size;

    return m_pDsg->GetSymbolSize( Index, pp );
}

int WDebugSymbolGroup::GetSymbolOffset(
    [In] ULONG Index,
    [Out] UInt64% Offset)
{
    pin_ptr<UInt64> pp = &Offset;

    return m_pDsg->GetSymbolOffset( Index, pp );
}

int WDebugSymbolGroup::GetSymbolRegister(
    [In] ULONG Index,
    [Out] ULONG% Register)
{
    pin_ptr<ULONG> pp = &Register;

    return m_pDsg->GetSymbolRegister( Index, pp );
}

int WDebugSymbolGroup::GetSymbolValueTextWide(
    [In] ULONG Index,
    [Out] String^% Text)
    //[Out] StringBuilder Buffer,
    //[In] Int32 BufferSize,
    //[Out] ULONG% NameSize);
{
    ULONG cchText = MAX_PATH;

    Text = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszText( new wchar_t[ cchText ] );

        hr = m_pDsg->GetSymbolValueTextWide( Index,
                                             wszText.get(),
                                             cchText,
                                             &cchText );

        if( S_OK == hr )
        {
            Text = gcnew String( wszText.get() );
        }
    }

    return hr;
}

int WDebugSymbolGroup::GetSymbolEntryInformation(
    [In] ULONG Index,
    [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_ENTRY% Info)
{
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_ENTRY> pp = &Info;

    memset( pp, 0, sizeof(::DEBUG_SYMBOL_ENTRY) );
    return m_pDsg->GetSymbolEntryInformation( Index, (PDEBUG_SYMBOL_ENTRY) pp );
}



//
// WDebugDataSpaces stuff
//


WDebugDataSpaces::WDebugDataSpaces( ::IDebugDataSpaces4* pDds )
    : WDebugEngInterface( pDds )
{
    WDebugClient::g_log->Write( L"Created DataSpaces" );
} // end constructor


WDebugDataSpaces::WDebugDataSpaces( IntPtr pDds )
    : WDebugEngInterface( pDds )
{
    WDebugClient::g_log->Write( L"Created DataSpaces" );
} // end constructor


// Note that fewer than BytesRequested bytes may be returned!
int WDebugDataSpaces::ReadVirtual(
    [In] UInt64 Offset,
    [In] ULONG BytesRequested,
    [Out] array<byte>^% buffer)
    //[In] ULONG BufferSize,
    //[Out] ULONG% BytesRead);
{
    buffer = nullptr;

    if( 0 == BytesRequested )
        throw gcnew ArgumentException( L"You must request at least one byte." );

    array<byte>^ tmp = gcnew array<byte>( BytesRequested );
    pin_ptr<byte> pp = &tmp[ 0 ];
    ULONG bytesRead = 0;
    int hr = m_pNative->ReadVirtual( Offset,
                                     pp,
                                     BytesRequested,
                                     &bytesRead );
    if( S_OK == hr )
    {
        if( bytesRead != BytesRequested )
        {
            array<byte>::Resize( tmp, bytesRead );
        }
        buffer = tmp;
    }

    return hr;
}


int WDebugDataSpaces::ReadVirtualDirect(
    [In] UInt64 Offset,
    [In] ULONG BytesRequested,
    [In] BYTE* buffer,
    [Out] ULONG% BytesRead)
{
    BytesRead = 0;

    if( 0 == BytesRequested )
        throw gcnew ArgumentException( L"You must request at least one byte." );

    pin_ptr<ULONG> pBytesRead = &BytesRead;
    int hr = m_pNative->ReadVirtual( Offset,
                                     buffer,
                                     BytesRequested,
                                     pBytesRead );
    return hr;
}

generic <typename TValue>
where TValue : value class
int WDebugDataSpaces::ReadVirtualValue(
	[In] UInt64 Offset,
	[Out] TValue% value)
{
	ULONG BytesRead;
	pin_ptr<TValue> pval = &value;
	int hr = m_pNative->ReadVirtual( Offset,
									 pval,
									 sizeof(TValue),
									 &BytesRead);
	
	//Since we are reading a single discrete value, treat under-read as failure
	if (hr == S_OK && BytesRead < sizeof(TValue))
	{
		return HRESULT_FROM_WIN32(ERROR_READ_FAULT);
	}
	return hr;
}

// Note that not all bytes may be written!
int WDebugDataSpaces::WriteVirtual(
    [In] UInt64 Offset,
    [In] array<byte>^ buffer,
    //[In] ULONG BufferSize,
    [Out] ULONG% BytesWritten)
{
    pin_ptr<byte> ppBuffer = &buffer[ 0 ];
    pin_ptr<ULONG> ppBytesWritten = &BytesWritten;

    return m_pNative->WriteVirtual( Offset,
                                    ppBuffer,
                                    buffer->Length,
                                    ppBytesWritten );
}

// Note that not all bytes may be written!
int WDebugDataSpaces::WriteVirtual(
    [In] UInt64 Offset,
    [In] BYTE* Buffer,
    [In] ULONG BufferSize,
    [Out] ULONG% BytesWritten)
{
    pin_ptr<ULONG> ppBytesWritten = &BytesWritten;

    return m_pNative->WriteVirtual( Offset,
                                    Buffer,
                                    BufferSize,
                                    ppBytesWritten );
}


int WDebugDataSpaces::SearchVirtual(
    [In] UInt64 Offset,
    [In] UInt64 Length,
    [In] array<byte>^ Pattern,
    //[In] ULONG PatternSize,
    [In] ULONG PatternGranularity,
    [Out] UInt64% MatchOffset)
{
    pin_ptr<byte> ppPattern = &Pattern[ 0 ];
    pin_ptr<UInt64> ppMatchOffset = &MatchOffset;
    return m_pNative->SearchVirtual( Offset,
                                     Length,
                                     ppPattern,
                                     Pattern->Length,
                                     PatternGranularity,
                                     ppMatchOffset );
}

int WDebugDataSpaces::ReadVirtualUncached(
    [In] UInt64 Offset,
    [In] ULONG BytesRequested,
    [Out] array<byte>^% buffer)
    //[In] ULONG BufferSize,
    //[Out] ULONG% BytesRead);
{
    buffer = nullptr;
    array<byte>^ tmp = gcnew array<byte>( BytesRequested );
    pin_ptr<byte> pp = &tmp[ 0 ];
    ULONG bytesRead = 0;
    int hr = m_pNative->ReadVirtualUncached( Offset,
                                             pp,
                                             BytesRequested,
                                             &bytesRead );
    if( S_OK == hr )
    {
        if( bytesRead != BytesRequested )
        {
            array<byte>::Resize( tmp, bytesRead );
        }
        buffer = tmp;
    }

    return hr;
}

int WDebugDataSpaces::WriteVirtualUncached(
    [In] UInt64 Offset,
    [In] array<byte>^ buffer,
    //[In] ULONG BufferSize,
    [Out] ULONG% BytesWritten)
{
    pin_ptr<byte> ppBuffer = &buffer[ 0 ];
    pin_ptr<ULONG> ppBytesWritten = &BytesWritten;

    return m_pNative->WriteVirtualUncached( Offset,
                                            ppBuffer,
                                            buffer->Length,
                                            ppBytesWritten );
}

int WDebugDataSpaces::ReadPointersVirtual(
    [In] ULONG Count,
    [In] UInt64 Offset,
    [Out] array<UInt64>^% Ptrs)
{
    Ptrs = nullptr;
    array<UInt64>^ tmp = gcnew array<UInt64>( Count );
    pin_ptr<UInt64> pp = &tmp[ 0 ];
    ULONG bytesRead = 0;
    int hr = m_pNative->ReadPointersVirtual( Count,
                                             Offset,
                                             pp );
    if( S_OK == hr )
    {
        Ptrs = tmp;
    }

    return hr;
}

int WDebugDataSpaces::WritePointersVirtual(
    //[In] ULONG Count,
    [In] UInt64 Offset,
    [In] array<UInt64>^ Ptrs)
{
    pin_ptr<UInt64> pp = &Ptrs[ 0 ];

    return m_pNative->WritePointersVirtual( Ptrs->Length,
                                            Offset,
                                            pp );
}

int WDebugDataSpaces::ReadPhysical(
    [In] UInt64 Offset,
    [In] ULONG BytesRequested,
    [Out] array<byte>^% buffer)
    //[In] ULONG BufferSize,
    //[Out] ULONG% BytesRead);
{
    buffer = nullptr;
    array<byte>^ tmp = gcnew array<byte>( BytesRequested );
    pin_ptr<byte> pp = &tmp[ 0 ];
    ULONG bytesRead = 0;
    int hr = m_pNative->ReadPhysical( Offset,
                                      pp,
                                      BytesRequested,
                                      &bytesRead );
    if( S_OK == hr )
    {
        if( bytesRead != BytesRequested )
        {
            array<byte>::Resize( tmp, bytesRead );
        }
        buffer = tmp;
    }

    return hr;
}

int WDebugDataSpaces::WritePhysical(
    [In] UInt64 Offset,
    [In] array<byte>^ buffer,
    //[In] ULONG BufferSize,
    [Out] ULONG% BytesWritten)
{
    pin_ptr<byte> ppBuffer = &buffer[ 0 ];
    pin_ptr<ULONG> ppBytesWritten = &BytesWritten;

    return m_pNative->WritePhysical( Offset,
                                     ppBuffer,
                                     buffer->Length,
                                     ppBytesWritten );
}

int WDebugDataSpaces::ReadControl(
    [In] ULONG Processor,
    [In] UInt64 Offset,
    [In] ULONG BytesRequested,
    [Out] array<byte>^% buffer)
    //[In] Int32 BufferSize,
    //[Out] ULONG% BytesRead);
{
    buffer = nullptr;
    array<byte>^ tmp = gcnew array<byte>( BytesRequested );
    pin_ptr<byte> pp = &tmp[ 0 ];
    ULONG bytesRead = 0;
    int hr = m_pNative->ReadControl( Processor,
                                     Offset,
                                     pp,
                                     BytesRequested,
                                     &bytesRead );
    if( S_OK == hr )
    {
        if( bytesRead != BytesRequested )
        {
            array<byte>::Resize( tmp, bytesRead );
        }
        buffer = tmp;
    }

    return hr;
}

int WDebugDataSpaces::WriteControl(
    [In] ULONG Processor,
    [In] UInt64 Offset,
    [In] array<byte>^ buffer,
    //[In] Int32 BufferSize,
    [Out] ULONG% BytesWritten)
{
    pin_ptr<byte> ppBuffer = &buffer[ 0 ];
    pin_ptr<ULONG> ppBytesWritten = &BytesWritten;

    return m_pNative->WriteControl( Processor,
                                    Offset,
                                    ppBuffer,
                                    buffer->Length,
                                    ppBytesWritten );
}

int WDebugDataSpaces::ReadIo(
    [In] INTERFACE_TYPE InterfaceType,
    [In] ULONG BusNumber,
    [In] ULONG AddressSpace,
    [In] UInt64 Offset,
    [In] ULONG BytesRequested,
    [Out] array<byte>^% buffer)
    //[In] ULONG BufferSize,
    //[Out] ULONG% BytesRead);
{
    buffer = nullptr;
    array<byte>^ tmp = gcnew array<byte>( BytesRequested );
    pin_ptr<byte> pp = &tmp[ 0 ];
    ULONG bytesRead = 0;
    int hr = m_pNative->ReadIo( (ULONG) InterfaceType,
                                BusNumber,
                                AddressSpace,
                                Offset,
                                pp,
                                BytesRequested,
                                &bytesRead );
    if( S_OK == hr )
    {
        if( bytesRead != BytesRequested )
        {
            array<byte>::Resize( tmp, bytesRead );
        }
        buffer = tmp;
    }

    return hr;
}

int WDebugDataSpaces::WriteIo(
    [In] INTERFACE_TYPE InterfaceType,
    [In] ULONG BusNumber,
    [In] ULONG AddressSpace,
    [In] UInt64 Offset,
    [In] array<byte>^ buffer,
    //[In] ULONG BufferSize,
    [Out] ULONG% BytesWritten)
{
    pin_ptr<byte> ppBuffer = &buffer[ 0 ];
    pin_ptr<ULONG> ppBytesWritten = &BytesWritten;

    return m_pNative->WriteIo( (ULONG) InterfaceType,
                               BusNumber,
                               AddressSpace,
                               Offset,
                               ppBuffer,
                               buffer->Length,
                               ppBytesWritten );
}

int WDebugDataSpaces::ReadMsr(
    [In] ULONG Msr,
    [Out] UInt64% MsrValue)
{
    pin_ptr<UInt64> pp = &MsrValue;
    return m_pNative->ReadMsr( Msr, pp );
}

int WDebugDataSpaces::WriteMsr(
    [In] ULONG Msr,
    [In] UInt64 MsrValue)
{
    return m_pNative->WriteMsr( Msr, MsrValue );
}

int WDebugDataSpaces::ReadBusData(
    [In] BUS_DATA_TYPE BusDataType,
    [In] ULONG BusNumber,
    [In] ULONG SlotNumber,
    [In] ULONG Offset,
    [In] ULONG BytesRequested,
    [Out] array<byte>^% buffer)
    //[In] ULONG BufferSize,
    //[Out] ULONG% BytesRead);
{
    buffer = nullptr;
    array<byte>^ tmp = gcnew array<byte>( BytesRequested );
    pin_ptr<byte> pp = &tmp[ 0 ];
    ULONG bytesRead = 0;
    int hr = m_pNative->ReadBusData( (ULONG) BusDataType,
                                     BusNumber,
                                     SlotNumber,
                                     Offset,
                                     pp,
                                     BytesRequested,
                                     &bytesRead );
    if( S_OK == hr )
    {
        if( bytesRead != BytesRequested )
        {
            array<byte>::Resize( tmp, bytesRead );
        }
        buffer = tmp;
    }

    return hr;
}

int WDebugDataSpaces::WriteBusData(
    [In] BUS_DATA_TYPE BusDataType,
    [In] ULONG BusNumber,
    [In] ULONG SlotNumber,
    [In] ULONG Offset,
    [In] array<byte>^ buffer,
    //[In] ULONG BufferSize,
    [Out] ULONG% BytesWritten)
{
    pin_ptr<byte> ppBuffer = &buffer[ 0 ];
    pin_ptr<ULONG> ppBytesWritten = &BytesWritten;

    return m_pNative->WriteBusData( (ULONG) BusDataType,
                                    BusNumber,
                                    SlotNumber,
                                    Offset,
                                    ppBuffer,
                                    buffer->Length,
                                    ppBytesWritten );
}

int WDebugDataSpaces::CheckLowMemory()
{
    return m_pNative->CheckLowMemory();
}

int WDebugDataSpaces::ReadDebuggerData(
    [In] ULONG Index,
    [In,Out] array<byte>^ buffer, // I'm going to punt on this one and make the caller decide how much to allocate
    //[In] ULONG BufferSize,
    [Out] ULONG% DataSize)
{
    pin_ptr<byte> ppBuffer = &buffer[ 0 ];
    pin_ptr<ULONG> ppDataSize = &DataSize;
    return m_pNative->ReadDebuggerData( Index,
                                        ppBuffer,
                                        buffer->Length,
                                        ppDataSize );
}

int WDebugDataSpaces::ReadProcessorSystemData(
    [In] ULONG Processor,
    [In] DEBUG_DATA Index,
    [In,Out] array<byte>^ buffer, // I'm going to punt on this one and make the caller decide how much to allocate
    //[In] ULONG BufferSize,
    [Out] ULONG% DataSize)
{
    pin_ptr<byte> ppBuffer = &buffer[ 0 ];
    pin_ptr<ULONG> ppDataSize = &DataSize;
    return m_pNative->ReadProcessorSystemData( Processor,
                                               (ULONG) Index,
                                               ppBuffer,
                                               buffer->Length,
                                               ppDataSize );
}

/* IDebugDataSpaces2 */

int WDebugDataSpaces::VirtualToPhysical(
    [In] UInt64 Virtual,
    [Out] UInt64% Physical)
{
    pin_ptr<UInt64> pp = &Physical;
    return m_pNative->VirtualToPhysical( Virtual, pp );
}

int WDebugDataSpaces::GetVirtualTranslationPhysicalOffsets(
    [In] UInt64 Virtual,
    [In,Out] array<UInt64>^ Offsets, // I'm going to punt on this one and make the caller decide how much to allocate
    //[In] ULONG OffsetsSize,
    [Out] ULONG% Levels)
{
    pin_ptr<UInt64> ppOffsets = &Offsets[ 0 ];
    pin_ptr<ULONG> ppLevels = &Levels;
    return m_pNative->GetVirtualTranslationPhysicalOffsets( Virtual,
                                                            ppOffsets,
                                                            Offsets->Length,
                                                            ppLevels );
}

int WDebugDataSpaces::ReadHandleData(
    [In] UInt64 Handle,
    [In] DEBUG_HANDLE_DATA_TYPE DataType,
    [In,Out] array<byte>^% buffer, // I'm going to punt on this one and make the caller decide how much to allocate
    //[In] ULONG BufferSize,
    [Out] ULONG% DataSize)
{
    pin_ptr<byte> ppBuffer = &buffer[ 0 ];
    pin_ptr<ULONG> ppDataSize = &DataSize;
    return m_pNative->ReadHandleData( Handle,
                                      (ULONG) DataType,
                                      ppBuffer,
                                      buffer->Length,
                                      ppDataSize );
}

int WDebugDataSpaces::FillVirtual(
    [In] UInt64 Start,
    [In] ULONG Size,
    [In] array<byte>^ Pattern,
    //[In] ULONG PatternSize,
    [Out] ULONG% Filled)
{
    pin_ptr<byte> ppPattern = &Pattern[ 0 ];
    pin_ptr<ULONG> ppFilled = &Filled;
    return m_pNative->FillVirtual( Start,
                                   Size,
                                   ppPattern,
                                   Pattern->Length,
                                   ppFilled );
}

int WDebugDataSpaces::FillPhysical(
    [In] UInt64 Start,
    [In] ULONG Size,
    [In] array<byte>^ Pattern,
    //[In] ULONG PatternSize,
    [Out] ULONG% Filled)
{
    pin_ptr<byte> ppPattern = &Pattern[ 0 ];
    pin_ptr<ULONG> ppFilled = &Filled;
    return m_pNative->FillPhysical( Start,
                                    Size,
                                    ppPattern,
                                    Pattern->Length,
                                    ppFilled );
}

int WDebugDataSpaces::QueryVirtual(
    [In] UInt64 Offset,
    [Out] Microsoft::Diagnostics::Runtime::Interop::MEMORY_BASIC_INFORMATION64% Info)
{
    // QueryVirtual requires the Info structure to be 16-byte aligned (for use with SIMD
    // instructions). Unfortunately there is no way to get that in .NET. We are not using
    // mixed-mode C++/CLI, so we can't just declare a local using the native type; we have
    // to manually allocate a chunk of memory large enough that we can slide the pointer
    // until it is 16-byte aligned.

    IntPtr originalPointer = Marshal::AllocHGlobal( Marshal::SizeOf( Info ) + 16 );
    ZeroMemory( (void*) originalPointer, Marshal::SizeOf( Info ) + 16 );
    IntPtr alignedPointer = originalPointer;
    while( 0 != (alignedPointer.ToInt64() & 0x0f) )
    {
        // I think we should always be at least 4-byte aligned.
        alignedPointer = alignedPointer + 4;
    }

    int hr = m_pNative->QueryVirtual( Offset, (::PMEMORY_BASIC_INFORMATION64) (void*) alignedPointer );

    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::MEMORY_BASIC_INFORMATION64> pp = &Info;
    memcpy( pp, (void*) alignedPointer, Marshal::SizeOf( Info ) );

    Marshal::FreeHGlobal( originalPointer );
    return hr;
}

/* IDebugDataSpaces3 */

int WDebugDataSpaces::ReadImageNtHeaders(
    [In] UInt64 ImageBase,
    [Out] Microsoft::Diagnostics::Runtime::Interop::IMAGE_NT_HEADERS64% Headers)
{
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::IMAGE_NT_HEADERS64> pp = &Headers;
    return m_pNative->ReadImageNtHeaders( ImageBase,
                                          (PIMAGE_NT_HEADERS64) pp );
}

int WDebugDataSpaces::ReadTagged(
    [In] Guid Tag,
    [In] ULONG Offset,
    [In,Out] array<byte>^% buffer,
    //[In] ULONG BufferSize,
    [Out] ULONG% TotalSize)
{
    //pin_ptr<Guid> ppTag = &Tag;
    pin_ptr<byte> ppBuffer = &buffer[ 0 ];
    pin_ptr<ULONG> ppTotalSize = &TotalSize;
    //return m_pNative->ReadTagged( (LPGUID) ppTag,
    return m_pNative->ReadTagged( (LPGUID) &Tag,
                                  Offset,
                                  ppBuffer,
                                  buffer->Length,
                                  ppTotalSize );
}

int WDebugDataSpaces::StartEnumTagged(
    [Out] UInt64% Handle)
{
    pin_ptr<UInt64> pp = &Handle;
    return m_pNative->StartEnumTagged( pp );
}

int WDebugDataSpaces::GetNextTagged(
    [In] UInt64 Handle,
    [Out] Guid% Tag,
    [Out] ULONG% Size)
{
    pin_ptr<Guid> ppTag = &Tag;
    pin_ptr<ULONG> ppSize = &Size;
    return m_pNative->GetNextTagged( Handle,
                                     (LPGUID) ppTag,
                                     ppSize );
}

int WDebugDataSpaces::EndEnumTagged(
    [In] UInt64 Handle)
{
    return m_pNative->EndEnumTagged( Handle );
}

/* IDebugDataSpaces4 */

int WDebugDataSpaces::GetOffsetInformation(
    [In] DEBUG_DATA_SPACE Space,
    [In] DEBUG_OFFSINFO Which,
    [In] UInt64 Offset,
    [In,Out] array<byte>^ buffer,
    //[In] ULONG BufferSize,
    [Out] ULONG% InfoSize)
{
    pin_ptr<byte> ppBuffer = &buffer[ 0 ];
    pin_ptr<ULONG> ppInfoSize = &InfoSize;
    return m_pNative->GetOffsetInformation( (ULONG) Space,
                                            (ULONG) Which,
                                            Offset,
                                            ppBuffer,
                                            buffer->Length,
                                            ppInfoSize );
}

int WDebugDataSpaces::GetNextDifferentlyValidOffsetVirtual(
    [In] UInt64 Offset,
    [Out] UInt64% NextOffset)
{
    pin_ptr<UInt64> pp = &NextOffset;
    return m_pNative->GetNextDifferentlyValidOffsetVirtual( Offset,
                                                            pp );
}

int WDebugDataSpaces::GetValidRegionVirtual(
    [In] UInt64 Base,
    [In] ULONG Size,
    [Out] UInt64% ValidBase,
    [Out] ULONG% ValidSize)
{
    pin_ptr<UInt64> ppValidBase = &ValidBase;
    pin_ptr<ULONG> ppValidSize = &ValidSize;
    return m_pNative->GetValidRegionVirtual( Base,
                                             Size,
                                             ppValidBase,
                                             ppValidSize );
}

int WDebugDataSpaces::SearchVirtual2(
    [In] UInt64 Offset,
    [In] UInt64 Length,
    [In] DEBUG_VSEARCH Flags,
    [In] array<byte>^ Pattern,
    //[In] ULONG PatternSize,
    [In] ULONG PatternGranularity,
    [Out] UInt64% MatchOffset)
{
    pin_ptr<byte> ppPattern = &Pattern[ 0 ];
    pin_ptr<UInt64> ppMatchOffset = &MatchOffset;
    return m_pNative->SearchVirtual2( Offset,
                                     Length,
                                     (ULONG) Flags,
                                     ppPattern,
                                     Pattern->Length,
                                     PatternGranularity,
                                     ppMatchOffset );
}

/*
int WDebugDataSpaces::ReadMultiByteStringVirtual(
    [In] UInt64 Offset,
    [In] ULONG MaxBytes,
    [Out] String^% Result)
{
    ULONG cchResult = 48;

    Result = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<char[]> szResult( new char[ cchResult ] );

        hr = m_pNative->ReadMultiByteStringVirtual( Offset,
                                                    MaxBytes,
                                                    szResult.get(),
                                                    cchResult,
                                                    &cchResult );

        if( S_OK == hr )
        {
            Result = Marshal::PtrToStringAnsi( static_cast<IntPtr>( szResult.get() ) );
        }
    }

    return hr;
}
*/

int WDebugDataSpaces::ReadMultiByteStringVirtualWide(
    [In] UInt64 Offset,
    [In] ULONG MaxBytes,
    [In] CODE_PAGE CodePage,
    [Out] String^% Result)
{
    ULONG cchResult = min( 48, MaxBytes );

    Result = nullptr;

    //wprintf( L"RMBSVW: Offset: %I64i, MaxBytes = %i\n", Offset, MaxBytes );
    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        cchResult = cchResult + 1; // Not clear if StringBytes param includes terminating NULL.
        std::unique_ptr<wchar_t[]> wszResult( new wchar_t[ cchResult ] );
        //wprintf( L"RMBSVW: Top of loop: cchResult is: %i (MaxBytes %i)\n", cchResult, MaxBytes );

        ULONG dbgEngSaysCchResultShouldBe = cchResult;
        hr = m_pNative->ReadMultiByteStringVirtualWide( Offset,
                                                        MaxBytes,
                                                        (ULONG) CodePage,
                                                        wszResult.get(),
                                                        cchResult,
                                                        &dbgEngSaysCchResultShouldBe );

        //wprintf( L"RMBSVW: After call: dbgEngSaysCchResultShouldBe is: %i (hr=%#x)\n", dbgEngSaysCchResultShouldBe, hr );
        // Workaround for INT34c803e7.
        if( dbgEngSaysCchResultShouldBe > cchResult )
        {
            //wprintf( L"RMBSVW: it says the buffer was too small. But hr is: %#x\n", hr );
            // Docs say in this case it should return S_FALSE. But... it doesn't.
            hr = S_FALSE;
        }

        cchResult = dbgEngSaysCchResultShouldBe;

        if( S_OK == hr )
        {
            // INTa0696e45: ReadUnicodeStringVirtualWide does not respect the
            // MaxBytes parameter. FTFY:
            if( cchResult > MaxBytes )
                cchResult = MaxBytes;

            // Don't include a terminating null:
            while( (cchResult > 0) &&
                   (wszResult[ cchResult - 1 ] == 0) )
            {
                cchResult -= 1;
            }
            Result = gcnew String( wszResult.get(), 0, cchResult );
        }
    }

    return hr;
}

int WDebugDataSpaces::ReadUnicodeStringVirtualWide(
    [In] UInt64 Offset,
    [In] ULONG MaxBytes,
    [Out] String^% Result)
{
    ULONG cchResult = min( 48, MaxBytes / 2 );
    ULONG cbResultValue = cchResult * 2;

    Result = nullptr;

    //wprintf( L"RUSVW: Offset: %I64i\n", Offset );
    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        cchResult = (cbResultValue / 2) + 1;
        //wprintf( L"RUSVW: Top of loop: cchResult is: %i (MaxBytes %i)\n", cchResult, MaxBytes );
        std::unique_ptr<wchar_t[]> wszResult( new wchar_t[ cchResult ] );

        hr = m_pNative->ReadUnicodeStringVirtualWide( Offset,
                                                      MaxBytes,
                                                      wszResult.get(),
                                                      cchResult,
                                                      &cbResultValue );
        //wprintf( L"RUSVW: After call: cbResultValue is: %i\n", cbResultValue );

        // I don't know if INT34c803e7 affects ReadUnicodeStringVirtualWide
        // too, but just in case:
        if( (cbResultValue / 2) > cchResult )
        {
            hr = S_FALSE;
        }

        if( S_OK == hr )
        {
            // INTa0696e45: ReadUnicodeStringVirtualWide does not respect the
            // MaxBytes parameter. FTFY:
            if( cbResultValue > MaxBytes )
                cbResultValue = MaxBytes;

            // Don't include a terminating null:
            while( (cbResultValue > 0) &&
                   (wszResult[ (cbResultValue / 2) - 1 ] == 0) )
            {
                cbResultValue -= 2;
            }
            Result = gcnew String( wszResult.get(), 0, cbResultValue / 2 );
        }
    }

    return hr;
}

int WDebugDataSpaces::ReadPhysical2(
    [In] UInt64 Offset,
    [In] ULONG BytesRequested,
    [In] DEBUG_PHYSICAL Flags,
    [Out] array<byte>^% buffer)
    //[In] ULONG BufferSize,
    //[Out] ULONG% BytesRead);
{
    buffer = nullptr;
    array<byte>^ tmp = gcnew array<byte>( BytesRequested );
    pin_ptr<byte> pp = &tmp[ 0 ];
    ULONG bytesRead = 0;
    int hr = m_pNative->ReadPhysical2( Offset,
                                       (ULONG) Flags,
                                       pp,
                                       BytesRequested,
                                       &bytesRead );
    if( S_OK == hr )
    {
        if( bytesRead != BytesRequested )
        {
            array<byte>::Resize( tmp, bytesRead );
        }
        buffer = tmp;
    }

    return hr;
}

int WDebugDataSpaces::WritePhysical2(
    [In] UInt64 Offset,
    [In] DEBUG_PHYSICAL Flags,
    [In] array<byte>^ buffer,
    //[In] ULONG BufferSize,
    [Out] ULONG% BytesWritten)
{
    pin_ptr<byte> ppBuffer = &buffer[ 0 ];
    pin_ptr<ULONG> ppBytesWritten = &BytesWritten;

    return m_pNative->WritePhysical2( Offset,
                                      (ULONG) Flags,
                                      ppBuffer,
                                      buffer->Length,
                                      ppBytesWritten );
}


//
// WDebugRegisters stuff
//

WDebugRegisters::WDebugRegisters( ::IDebugRegisters2* pDr )
    : WDebugEngInterface( pDr )
{
    WDebugClient::g_log->Write( L"Created DebugRegisters" );
} // end constructor


WDebugRegisters::WDebugRegisters( IntPtr pDr )
    : WDebugEngInterface( pDr )
{
    WDebugClient::g_log->Write( L"Created DebugRegisters" );
} // end constructor


int WDebugRegisters::GetNumberRegisters(
    [Out] ULONG% Number)
{
    pin_ptr<ULONG> pp = &Number;
    return m_pNative->GetNumberRegisters( pp );
}

int WDebugRegisters::GetValue(
    [In] ULONG Register,
    [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE% Value)
{
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE> pp = &Value;
    return m_pNative->GetValue( Register, (PDEBUG_VALUE) pp );
}

int WDebugRegisters::SetValue(
    [In] ULONG Register,
    [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE Value)
{
    return m_pNative->SetValue( Register, (PDEBUG_VALUE) &Value );
}


//int WDebugRegisters::GetValues( //FIX ME!!! This needs to be tested // [danthom] <-- Wha?
//    [In] ULONG Count,
//    [In] array<ULONG>^ Indices,
//    [In] ULONG Start,
//    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^% Values)
//{
//}

//int WDebugRegisters::SetValues(
//    [In] ULONG Count,
//    [In] array<ULONG>^ Indices,
//    [In] ULONG Start,
//    [In] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^ Values)
//{
//}

//int WDebugRegisters::OutputRegisters(
//    [In] DEBUG_OUTCTL OutputControl,
//    [In] DEBUG_REGISTERS Flags)
//{
//}

int WDebugRegisters::GetInstructionOffset(
    [Out] UInt64% Offset)
{
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetInstructionOffset( pp );
}

int WDebugRegisters::GetStackOffset(
    [Out] UInt64% Offset)
{
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetStackOffset( pp );
}

int WDebugRegisters::GetFrameOffset(
    [Out] UInt64% Offset)
{
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetFrameOffset( pp );
}

/* IDebugRegisters2 */

int WDebugRegisters::GetDescriptionWide(
    [In] ULONG Register,
    [Out] String^% Name,
    //[Out] StringBuilder NameBuffer,
    //[In] Int32 NameBufferSize,
    //[Out] ULONG% NameSize,
    [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_REGISTER_DESCRIPTION% Desc)
{
    ULONG cchName = 20;
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_REGISTER_DESCRIPTION> pp = &Desc;

    Name = nullptr;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszName( new wchar_t[ cchName ] );

        hr = m_pNative->GetDescriptionWide( Register,
                                            wszName.get(),
                                            cchName,
                                            &cchName,
                                            (PDEBUG_REGISTER_DESCRIPTION) pp );

        if( S_OK == hr )
        {
            Name = gcnew String( wszName.get() );
        }
    }

    return hr;
}

int WDebugRegisters::GetIndexByNameWide(
    [In] String^ Name,
    [Out] ULONG% Index)
{
    pin_ptr<ULONG> pp = &Index;
    marshal_context mc;
    return m_pNative->GetIndexByNameWide( mc.marshal_as<const wchar_t*>( Name ),
                                          pp );
}

int WDebugRegisters::GetNumberPseudoRegisters(
    [Out] ULONG% Number )
{
    pin_ptr<ULONG> pp = &Number;
    return m_pNative->GetNumberPseudoRegisters( pp );
}

int WDebugRegisters::GetPseudoDescriptionWide(
    [In] ULONG Register,
    [Out] String^% Name,
    //[Out] StringBuilder NameBuffer,
    //[In] Int32 NameBufferSize,
    //[Out] ULONG% NameSize,
    [Out] UInt64% TypeModule,
    [Out] ULONG% TypeId )
{
    ULONG cchName = MAX_PATH;
    Name = nullptr;
    pin_ptr<UInt64> ppTypeModule = &TypeModule;
    pin_ptr<ULONG> ppTypeId = &TypeId;

    HRESULT hr = S_FALSE;
    while( S_FALSE == hr )
    {
        std::unique_ptr<wchar_t[]> wszName( new wchar_t[ cchName ] );

        hr = m_pNative->GetPseudoDescriptionWide( Register,
                                                  wszName.get(),
                                                  cchName,
                                                  &cchName,
                                                  ppTypeModule,
                                                  ppTypeId );

        if( S_OK == hr )
        {
            Name = gcnew String( wszName.get() );
        }
    }

    return hr;
}

int WDebugRegisters::GetPseudoIndexByNameWide(
    [In] String^ Name,
    [Out] ULONG% Index )
{
    pin_ptr<ULONG> pp = &Index;
    marshal_context mc;
    return m_pNative->GetPseudoIndexByNameWide( mc.marshal_as<const wchar_t*>( Name ),
                                                pp );
}

int WDebugRegisters::GetPseudoValues(
    [In] DEBUG_REGSRC Source,
    [In] ULONG Count,
    [In] ULONG Start,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^% Values )
{
    Values = nullptr;

    if( !Count )
        throw gcnew ArgumentException( L"You must request at least one value.", L"Count" );

    array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^ tmp = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>( Count );
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE> ppTmp = &tmp[ 0 ];

    int hr = m_pNative->GetPseudoValues( (ULONG) Source,
                                         Count,
                                         nullptr,
                                         Start,
                                         (PDEBUG_VALUE) ppTmp );
    // Normally we would check that hr was S_OK before assigning the out parameter, but
    // the way that dbgeng sets hr is not so helpful here: if there is trouble getting the
    // value for any one register, it returns the hr for that one (or rather, it returns
    // the most recently encountered error). So if you have 40 registers and one error, it
    // returns that one error. So we'll just return whatever it got. Bad registers should
    // be marked with an "invalid" type code. (and GetPseudoDescriptionWide for that
    // register will also return an error)
    Values = tmp;

    return hr;
}

int WDebugRegisters::GetPseudoValues(
    [In] DEBUG_REGSRC Source,
    [In] ULONG Count,
    [In] array<ULONG>^ Indices,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^% Values )
{
    Values = nullptr;

    if( !Indices )
        throw gcnew ArgumentNullException( L"Indices" );

    pin_ptr<ULONG> ppIndices = &Indices[ 0 ];
    array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^ tmp = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>( Indices->Length );
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE> ppTmp = &tmp[ 0 ];

    int hr = m_pNative->GetPseudoValues( (ULONG) Source,
                                         Indices->Length,
                                         ppIndices,
                                         0, // Start
                                         (PDEBUG_VALUE) ppTmp );
    // Normally we would check that hr was S_OK before assigning the out parameter, but
    // the way that dbgeng sets hr is not so helpful here: if there is trouble getting the
    // value for any one register, it returns the hr for that one (or rather, it returns
    // the most recently encountered error). So if you have 40 registers and one error, it
    // returns that one error. So we'll just return whatever it got. Bad registers should
    // be marked with an "invalid" type code. (and GetPseudoDescriptionWide for that
    // register will also return an error)
    Values = tmp;

    return hr;
}

//int WDebugRegisters::SetPseudoValues(
//    [In] ULONG Source,
//    [In] ULONG Count,
//    [In] array<ULONG>^ Indices,
//    [In] ULONG Start,
//    [In] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^ Values )
//{
//}

int WDebugRegisters::GetValues2(
    [In] DEBUG_REGSRC Source,
    //[In] ULONG Count,
    [In] array<ULONG>^ Indices,
    //[In] ULONG Start,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^% Values )
{
    Values = nullptr;

    if( !Indices )
        throw gcnew ArgumentNullException( L"Indices" );

    pin_ptr<ULONG> ppIndices = &Indices[ 0 ];
    array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^ tmp = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>( Indices->Length );
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE> ppTmp = &tmp[ 0 ];

    int hr = m_pNative->GetValues2( (ULONG) Source,
                                    Indices->Length,
                                    ppIndices,
                                    0, // Start
                                    (PDEBUG_VALUE) ppTmp );
    if( S_OK == hr )
    {
        Values = tmp;
    }

    return hr;
}

int WDebugRegisters::GetValues2(
    [In] DEBUG_REGSRC Source,
    [In] ULONG Count,
    //[In] array<ULONG>^ Indices,
    [In] ULONG Start,
    [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^% Values )
{
    Values = nullptr;

    if( !Count )
        throw gcnew ArgumentException( L"You must request at least one value.", L"Count" );

    array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^ tmp = gcnew array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>( Count );
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE> ppTmp = &tmp[ 0 ];

    int hr = m_pNative->GetValues2( (ULONG) Source,
                                    Count,
                                    nullptr,
                                    Start,
                                    (PDEBUG_VALUE) ppTmp );
    if( S_OK == hr )
    {
        Values = tmp;
    }

    return hr;
}

int WDebugRegisters::SetValues2(
    [In] ULONG Source,
    //[In] ULONG Count,
    [In] array<ULONG>^ Indices,
    //[In] ULONG Start,
    [In] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^ Values )
{
    pin_ptr<ULONG> ppIndices = &Indices[ 0 ];
    pin_ptr<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE> ppValues = &Values[ 0 ];
    return m_pNative->SetValues2( Source,
                                  Indices->Length,
                                  ppIndices,
                                  0, // Start
                                  (PDEBUG_VALUE) ppValues );
}

//int WDebugRegisters::OutputRegisters2(
//    [In] ULONG OutputControl,
//    [In] ULONG Source,
//    [In] ULONG Flags )
//{
//}

int WDebugRegisters::GetInstructionOffset2(
    [In] ULONG Source,
    [Out] UInt64% Offset )
{
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetInstructionOffset2( Source, pp );
}

int WDebugRegisters::GetStackOffset2(
    [In] ULONG Source,
    [Out] UInt64% Offset )
{
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetStackOffset2( Source, pp );
}

int WDebugRegisters::GetFrameOffset2(
    [In] ULONG Source,
    [Out] UInt64% Offset )
{
    pin_ptr<UInt64> pp = &Offset;
    return m_pNative->GetFrameOffset2( Source, pp );
}


//
// WDebugAdvanced stuff
//


WDebugAdvanced::WDebugAdvanced( ::IDebugAdvanced3* pDA )
    : WDebugEngInterface( pDA )
{
    WDebugClient::g_log->Write( L"Created DebugAdvanced" );
} // end constructor


WDebugAdvanced::WDebugAdvanced( IntPtr pDA )
    : WDebugEngInterface( pDA )
{
    WDebugClient::g_log->Write( L"Created DebugAdvanced" );
} // end constructor


int WDebugAdvanced::GetThreadContext(
    [In] BYTE* Context,
    [In] ULONG ContextSize )
{
    WDebugClient::g_log->Write( L"DebugAdvanced::GetThreadContext" );
    return m_pNative->GetThreadContext( Context, ContextSize );
}

int WDebugAdvanced::SetThreadContext(
    [In] BYTE* Context,
    [In] ULONG ContextSize )
{
    WDebugClient::g_log->Write( L"DebugAdvanced::SetThreadContext" );
    return m_pNative->SetThreadContext( Context, ContextSize );
}

int WDebugAdvanced::Request(
    [In] DEBUG_REQUEST Request,
    [In] BYTE* InBuffer,
    [In] ULONG InBufferSize,
    [In] BYTE* OutBuffer,
    [In] ULONG OutBufferSize,
    [Out] ULONG% OutSize )
{
    WDebugClient::g_log->Write( L"DebugAdvanced::Request" );
    pin_ptr< ULONG > pp = &OutSize;
    return m_pNative->Request( static_cast< ULONG >( Request ),
                               InBuffer,
                               InBufferSize,
                               OutBuffer,
                               OutBufferSize,
                               pp );
}

} // end namespace
