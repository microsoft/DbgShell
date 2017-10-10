#pragma once
#include <windows.h>
#undef CreateProcess // We define some functions that we want called "CreateProcess", not "CreateProcessW".
#include "dbgeng.h"
#undef DEBUG_PROCESS // We want to use the managed enum definition.
#include "DbgEngWrapper.h"

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace Microsoft::Diagnostics::Runtime::Interop;

namespace DbgEngWrapper
{

// This is the interface that managed code will provide an implementation of.
// (DbgEngEventCallbacksAdapter will delegate to the managed object via this interface).
public interface class IDebugEventCallbacksWideImp
{
//public:
    int GetInterestMask(
        [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT% Mask);

    int Breakpoint(
        [In] WDebugBreakpoint^ Bp);

    int Exception(
        [In] Microsoft::Diagnostics::Runtime::Interop::EXCEPTION_RECORD64% Exception,
        [In] UInt32 FirstChance);

    int CreateThread(
        [In] UInt64 Handle,
        [In] UInt64 DataOffset,
        [In] UInt64 StartOffset);

    int ExitThread(
        [In] UInt32 ExitCode);

    int CreateProcess(
        [In] UInt64 ImageFileHandle,
        [In] UInt64 Handle,
        [In] UInt64 BaseOffset,
        [In] UInt32 ModuleSize,
        [In] String^ ModuleName,
        [In] String^ ImageName,
        [In] UInt32 CheckSum,
        [In] UInt32 TimeDateStamp,
        [In] UInt64 InitialThreadHandle,
        [In] UInt64 ThreadDataOffset,
        [In] UInt64 StartOffset);

    int ExitProcess(
        [In] UInt32 ExitCode);

    int LoadModule(
        [In] UInt64 ImageFileHandle,
        [In] UInt64 BaseOffset,
        [In] UInt32 ModuleSize,
        [In] String^ ModuleName,
        [In] String^ ImageName,
        [In] UInt32 CheckSum,
        [In] UInt32 TimeDateStamp);

    int UnloadModule(
        [In] String^ ImageBaseName,
        [In] UInt64 BaseOffset);

    int SystemError(
        [In] UInt32 Error,
        [In] UInt32 Level);

    int SessionStatus(
        [In] DEBUG_SESSION Status);

    int ChangeDebuggeeState(
        [In] DEBUG_CDS Flags,
        [In] UInt64 Argument);

    int ChangeEngineState(
        [In] DEBUG_CES Flags,
        [In] UInt64 Argument);

    int ChangeSymbolState(
        [In] DEBUG_CSS Flags,
        [In] UInt64 Argument);
};


// This is the interface that managed code will provide an implementation of.
// (DbgEngEventContextCallbacksAdapter will delegate to the managed object via this interface).
public interface class IDebugEventContextCallbacksImp
{
//public:
    int GetInterestMask(
        [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT% Mask);

    int Breakpoint(
        [In] WDebugBreakpoint^ Bp,
        [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT* Context);

    int Exception(
        [In] Microsoft::Diagnostics::Runtime::Interop::EXCEPTION_RECORD64% Exception,
        [In] UInt32 FirstChance,
        [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT* Context);

    int CreateThread(
        [In] UInt64 Handle,
        [In] UInt64 DataOffset,
        [In] UInt64 StartOffset,
        [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT* Context);

    int ExitThread(
        [In] UInt32 ExitCode,
        [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT* Context);

    int CreateProcess(
        [In] UInt64 ImageFileHandle,
        [In] UInt64 Handle,
        [In] UInt64 BaseOffset,
        [In] UInt32 ModuleSize,
        [In] String^ ModuleName,
        [In] String^ ImageName,
        [In] UInt32 CheckSum,
        [In] UInt32 TimeDateStamp,
        [In] UInt64 InitialThreadHandle,
        [In] UInt64 ThreadDataOffset,
        [In] UInt64 StartOffset,
        [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT* Context);

    int ExitProcess(
        [In] UInt32 ExitCode,
        [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT* Context);

    int LoadModule(
        [In] UInt64 ImageFileHandle,
        [In] UInt64 BaseOffset,
        [In] UInt32 ModuleSize,
        [In] String^ ModuleName,
        [In] String^ ImageName,
        [In] UInt32 CheckSum,
        [In] UInt32 TimeDateStamp,
        [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT* Context);

    int UnloadModule(
        [In] String^ ImageBaseName,
        [In] UInt64 BaseOffset,
        [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT* Context);

    int SystemError(
        [In] UInt32 Error,
        [In] UInt32 Level,
        [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT* Context);

    int SessionStatus(
        [In] DEBUG_SESSION Status);

    int ChangeDebuggeeState(
        [In] DEBUG_CDS Flags,
        [In] UInt64 Argument,
        [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT* Context);

    int ChangeEngineState(
        [In] DEBUG_CES Flags,
        [In] UInt64 Argument,
        [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT* Context);

    int ChangeSymbolState(
        [In] DEBUG_CSS Flags,
        [In] UInt64 Argument);
};


// This is the interface that managed code will provide an implementation of.
// (DbgEngInputCallbacks will delegate to the managed object via this interface).
public interface class IDebugInputCallbacksImp
{
//public:
    int StartInput( [In] UInt32 BufferSize );

    int EndInput();
};



// This is the interface that managed code will provide an implementation of.
// (DbgEngOutputCallbacks will delegate to the managed object via this interface).
public interface class IDebugOutputCallbacksImp
{
//public:
    int Output(
        [In] DEBUG_OUTPUT Mask,
        [In] String^ Text);
};


//
// COM Interfaces
//

[ComVisible( true )]
[Guid( L"0690e046-9c23-45ac-a04f-987ac29ad0d3" )]
[InterfaceType( ComInterfaceType::InterfaceIsIUnknown )]
public interface class IComDbgEngEventCallbacks
{
    [PreserveSigAttribute]
    virtual HRESULT GetInterestMask(
        __out PULONG Mask );

    [PreserveSigAttribute]
    HRESULT Breakpoint(
        __in PDEBUG_BREAKPOINT2 Bp );

    [PreserveSigAttribute]
    HRESULT Exception(
        __in PEXCEPTION_RECORD64 Exception,
        __in ULONG FirstChance );

    [PreserveSigAttribute]
    HRESULT CreateThread(
        __in ULONG64 Handle,
        __in ULONG64 DataOffset,
        __in ULONG64 StartOffset );

    [PreserveSigAttribute]
    HRESULT ExitThread(
        __in ULONG ExitCode );

    [PreserveSigAttribute]
    HRESULT CreateProcess(
        __in ULONG64 ImageFileHandle,
        __in ULONG64 Handle,
        __in ULONG64 BaseOffset,
        __in ULONG ModuleSize,
        __in_opt PCWSTR ModuleName,
        __in_opt PCWSTR ImageName,
        __in ULONG CheckSum,
        __in ULONG TimeDateStamp,
        __in ULONG64 InitialThreadHandle,
        __in ULONG64 ThreadDataOffset,
        __in ULONG64 StartOffset );

    [PreserveSigAttribute]
    HRESULT ExitProcess(
        __in ULONG ExitCode );

    [PreserveSigAttribute]
    HRESULT LoadModule(
        __in ULONG64 ImageFileHandle,
        __in ULONG64 BaseOffset,
        __in ULONG ModuleSize,
        __in_opt PCWSTR ModuleName,
        __in_opt PCWSTR ImageName,
        __in ULONG CheckSum,
        __in ULONG TimeDateStamp );

    [PreserveSigAttribute]
    HRESULT UnloadModule(
        __in_opt PCWSTR ImageBaseName,
        __in ULONG64 BaseOffset );

    [PreserveSigAttribute]
    HRESULT SystemError(
        __in ULONG Error,
        __in ULONG Level );

    [PreserveSigAttribute]
    HRESULT SessionStatus(
        __in ULONG Status );

    [PreserveSigAttribute]
    HRESULT ChangeDebuggeeState(
        __in ULONG Flags,
        __in ULONG64 Argument );

    [PreserveSigAttribute]
    HRESULT ChangeEngineState(
        __in ULONG Flags,
        __in ULONG64 Argument );

    [PreserveSigAttribute]
    HRESULT ChangeSymbolState(
        __in ULONG Flags,
        __in ULONG64 Argument );
}; // end interface IComDbgEngEventCallbacks


[ComVisible( true )]
[Guid( L"61a4905b-23f9-4247-b3c5-53d087529ab7" )]
[InterfaceType( ComInterfaceType::InterfaceIsIUnknown )]
public interface class IComDbgEngEventContextCallbacks
{
    [PreserveSigAttribute]
    virtual HRESULT GetInterestMask(
        __out PULONG Mask );

    [PreserveSigAttribute]
    HRESULT Breakpoint(
        __in PDEBUG_BREAKPOINT2 Bp,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize );

    [PreserveSigAttribute]
    HRESULT Exception(
        __in PEXCEPTION_RECORD64 Exception,
        __in ULONG FirstChance,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize );

    [PreserveSigAttribute]
    HRESULT CreateThread(
        __in ULONG64 Handle,
        __in ULONG64 DataOffset,
        __in ULONG64 StartOffset,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize );

    [PreserveSigAttribute]
    HRESULT ExitThread(
        __in ULONG ExitCode,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize );

    [PreserveSigAttribute]
    HRESULT CreateProcess(
        __in ULONG64 ImageFileHandle,
        __in ULONG64 Handle,
        __in ULONG64 BaseOffset,
        __in ULONG ModuleSize,
        __in_opt PCWSTR ModuleName,
        __in_opt PCWSTR ImageName,
        __in ULONG CheckSum,
        __in ULONG TimeDateStamp,
        __in ULONG64 InitialThreadHandle,
        __in ULONG64 ThreadDataOffset,
        __in ULONG64 StartOffset,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize );

    [PreserveSigAttribute]
    HRESULT ExitProcess(
        __in ULONG ExitCode,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize );

    [PreserveSigAttribute]
    HRESULT LoadModule(
        __in ULONG64 ImageFileHandle,
        __in ULONG64 BaseOffset,
        __in ULONG ModuleSize,
        __in_opt PCWSTR ModuleName,
        __in_opt PCWSTR ImageName,
        __in ULONG CheckSum,
        __in ULONG TimeDateStamp,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize );

    [PreserveSigAttribute]
    HRESULT UnloadModule(
        __in_opt PCWSTR ImageBaseName,
        __in ULONG64 BaseOffset,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize );

    [PreserveSigAttribute]
    HRESULT SystemError(
        __in ULONG Error,
        __in ULONG Level,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize );

    [PreserveSigAttribute]
    HRESULT SessionStatus(
        __in ULONG Status );

    [PreserveSigAttribute]
    HRESULT ChangeDebuggeeState(
        __in ULONG Flags,
        __in ULONG64 Argument,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize );

    [PreserveSigAttribute]
    HRESULT ChangeEngineState(
        __in ULONG Flags,
        __in ULONG64 Argument,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize );

    [PreserveSigAttribute]
    HRESULT ChangeSymbolState(
        __in ULONG Flags,
        __in ULONG64 Argument );
}; // end interface IComDbgEngEventContextCallbacks


[ComVisible( true )]
[Guid( L"9f50e42c-f136-499e-9a97-73036c94ed2d" )]
[InterfaceType( ComInterfaceType::InterfaceIsIUnknown )]
public interface class IComDbgEngInputCallbacks
{
    [PreserveSigAttribute]
    HRESULT StartInput(
        __in ULONG BufferSize );

    [PreserveSigAttribute]
    HRESULT EndInput();
}; // end interface IComDbgEngInputCallbacks


[ComVisible( true )]
[Guid( L"4c7fd663-c394-4e26-8ef1-34ad5ed3764c" )]
[InterfaceType( ComInterfaceType::InterfaceIsIUnknown )]
public interface class IComDbgEngOutputCallbacks
{
    [PreserveSigAttribute]
    HRESULT Output(
        __in ULONG Mask,
        __in PCWSTR Text );
}; // end class IComDbgEngOutputCallbacks





//
// Adapters: implements COM interface, and forwards to the managed *Imp interfaces.
//

template< typename TManagedCallbacks, const IID* NativeIID >
public ref class DbgEngCallbacksAdapter
{
protected:
    TManagedCallbacks^ m_managedCallbacks;

    DbgEngCallbacksAdapter( TManagedCallbacks^ managedCallbacks )
        : m_managedCallbacks( managedCallbacks )
    {
    }

    // TODO: use the NativeIID template parameter for ICustomQueryInterface?
}; // end template class DbgEngCallbacksAdapter


public ref class DbgEngEventCallbacksAdapter : DbgEngCallbacksAdapter<IDebugEventCallbacksWideImp, &IID_IDebugEventCallbacksWide>, IComDbgEngEventCallbacks
{
public:
    virtual HRESULT GetInterestMask(
        __out PULONG Mask )
    {
        // A tracking reference ("%") is very much like a regular C++ reference. As such, if we want
        // to provide a tracking reference to the underlying ULONG, we need to dereference the PULONG
        // first, because just like you can't go from a pointer to a C++ reference, you can't go from
        // a pointer to a tracking reference. Dereferencing the pointer results in an L-value which
        // allows the compiler to do the right magic.
        return m_managedCallbacks->GetInterestMask( (Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT%) *Mask );
    }

    virtual HRESULT Breakpoint(
        __in PDEBUG_BREAKPOINT2 Bp )
    {
        return m_managedCallbacks->Breakpoint( WDebugBreakpoint::GetBreakpoint( (::IDebugBreakpoint3*) Bp ) );
    }

    virtual HRESULT Exception(
        __in PEXCEPTION_RECORD64 Exception,
        __in ULONG FirstChance )
    {
        return m_managedCallbacks->Exception( (Microsoft::Diagnostics::Runtime::Interop::EXCEPTION_RECORD64%) *Exception,
                                              FirstChance );
    }

    virtual HRESULT CreateThread(
        __in ULONG64 Handle,
        __in ULONG64 DataOffset,
        __in ULONG64 StartOffset )
    {
        return m_managedCallbacks->CreateThread( Handle, DataOffset, StartOffset );
    }

    virtual HRESULT ExitThread(
        __in ULONG ExitCode )
    {
        return m_managedCallbacks->ExitThread( ExitCode );
    }

    virtual HRESULT CreateProcess(
        __in ULONG64 ImageFileHandle,
        __in ULONG64 Handle,
        __in ULONG64 BaseOffset,
        __in ULONG ModuleSize,
        __in_opt PCWSTR ModuleName,
        __in_opt PCWSTR ImageName,
        __in ULONG CheckSum,
        __in ULONG TimeDateStamp,
        __in ULONG64 InitialThreadHandle,
        __in ULONG64 ThreadDataOffset,
        __in ULONG64 StartOffset )
    {
        return m_managedCallbacks->CreateProcess( ImageFileHandle,
                                                  Handle,
                                                  BaseOffset,
                                                  ModuleSize,
                                                  ModuleName ? gcnew String( ModuleName ) : nullptr,
                                                  ImageName ? gcnew String( ImageName ) : nullptr,
                                                  CheckSum,
                                                  TimeDateStamp,
                                                  InitialThreadHandle,
                                                  ThreadDataOffset,
                                                  StartOffset );

    }

    virtual HRESULT ExitProcess(
        __in ULONG ExitCode )
    {
        return m_managedCallbacks->ExitProcess( ExitCode );
    }

    virtual HRESULT LoadModule(
        __in ULONG64 ImageFileHandle,
        __in ULONG64 BaseOffset,
        __in ULONG ModuleSize,
        __in_opt PCWSTR ModuleName,
        __in_opt PCWSTR ImageName,
        __in ULONG CheckSum,
        __in ULONG TimeDateStamp )
    {
        return m_managedCallbacks->LoadModule( ImageFileHandle,
                                               BaseOffset,
                                               ModuleSize,
                                               ModuleName ? gcnew String( ModuleName ) : nullptr,
                                               ImageName ? gcnew String( ImageName ) : nullptr,
                                               CheckSum,
                                               TimeDateStamp );
    }

    virtual HRESULT UnloadModule(
        __in_opt PCWSTR ImageBaseName,
        __in ULONG64 BaseOffset )
    {
        return m_managedCallbacks->UnloadModule( ImageBaseName ? gcnew String( ImageBaseName ) : nullptr,
                                                 BaseOffset );
    }

    virtual HRESULT SystemError(
        __in ULONG Error,
        __in ULONG Level )
    {
        return m_managedCallbacks->SystemError( Error, Level );
    }

    virtual HRESULT SessionStatus(
        __in ULONG Status )
    {
        return m_managedCallbacks->SessionStatus( (Microsoft::Diagnostics::Runtime::Interop::DEBUG_SESSION) Status );
    }

    virtual HRESULT ChangeDebuggeeState(
        __in ULONG Flags,
        __in ULONG64 Argument )
    {
        return m_managedCallbacks->ChangeDebuggeeState( (Microsoft::Diagnostics::Runtime::Interop::DEBUG_CDS) Flags, Argument );
    }

    virtual HRESULT ChangeEngineState(
        __in ULONG Flags,
        __in ULONG64 Argument )
    {
        return m_managedCallbacks->ChangeEngineState( (Microsoft::Diagnostics::Runtime::Interop::DEBUG_CES) Flags, Argument );
    }

    virtual HRESULT ChangeSymbolState(
        __in ULONG Flags,
        __in ULONG64 Argument )
    {
        return m_managedCallbacks->ChangeSymbolState( (Microsoft::Diagnostics::Runtime::Interop::DEBUG_CSS) Flags, Argument );
    }


    DbgEngEventCallbacksAdapter( IDebugEventCallbacksWideImp^ managedCallbacks )
        : DbgEngCallbacksAdapter( managedCallbacks )
    {
    }
}; // end class DbgEngEventCallbacksAdapter


public ref class DbgEngEventContextCallbacksAdapter : DbgEngCallbacksAdapter<IDebugEventContextCallbacksImp, &IID_IDebugEventContextCallbacks>, IComDbgEngEventContextCallbacks
{
public:
    virtual HRESULT GetInterestMask(
        __out PULONG Mask )
    {
        // A tracking reference ("%") is very much like a regular C++ reference. As such, if we want
        // to provide a tracking reference to the underlying ULONG, we need to dereference the PULONG
        // first, because just like you can't go from a pointer to a C++ reference, you can't go from
        // a pointer to a tracking reference. Dereferencing the pointer results in an L-value which
        // allows the compiler to do the right magic.
        return m_managedCallbacks->GetInterestMask( (Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT%) *Mask );
    }

    virtual HRESULT Breakpoint(
        __in PDEBUG_BREAKPOINT2 Bp,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize )
    {
        auto pContext = _ValidateContext( Context, ContextSize );
        return m_managedCallbacks->Breakpoint( WDebugBreakpoint::GetBreakpoint( (::IDebugBreakpoint3*) Bp ),
                                               pContext );
    }

    virtual HRESULT Exception(
        __in PEXCEPTION_RECORD64 Exception,
        __in ULONG FirstChance,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize )
    {
        auto pContext = _ValidateContext( Context, ContextSize );
        return m_managedCallbacks->Exception( (Microsoft::Diagnostics::Runtime::Interop::EXCEPTION_RECORD64%) *Exception,
                                              FirstChance,
                                              pContext );
    }

    virtual HRESULT CreateThread(
        __in ULONG64 Handle,
        __in ULONG64 DataOffset,
        __in ULONG64 StartOffset,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize )
    {
        auto pContext = _ValidateContext( Context, ContextSize );
        return m_managedCallbacks->CreateThread( Handle, DataOffset, StartOffset, pContext );
    }

    virtual HRESULT ExitThread(
        __in ULONG ExitCode,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize )
    {
        auto pContext = _ValidateContext( Context, ContextSize );
        return m_managedCallbacks->ExitThread( ExitCode, pContext );
    }

    virtual HRESULT CreateProcess(
        __in ULONG64 ImageFileHandle,
        __in ULONG64 Handle,
        __in ULONG64 BaseOffset,
        __in ULONG ModuleSize,
        __in_opt PCWSTR ModuleName,
        __in_opt PCWSTR ImageName,
        __in ULONG CheckSum,
        __in ULONG TimeDateStamp,
        __in ULONG64 InitialThreadHandle,
        __in ULONG64 ThreadDataOffset,
        __in ULONG64 StartOffset,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize )
    {
        auto pContext = _ValidateContext( Context, ContextSize );
        return m_managedCallbacks->CreateProcess( ImageFileHandle,
                                                  Handle,
                                                  BaseOffset,
                                                  ModuleSize,
                                                  ModuleName ? gcnew String( ModuleName ) : nullptr,
                                                  ImageName ? gcnew String( ImageName ) : nullptr,
                                                  CheckSum,
                                                  TimeDateStamp,
                                                  InitialThreadHandle,
                                                  ThreadDataOffset,
                                                  StartOffset,
                                                  pContext );

    }

    virtual HRESULT ExitProcess(
        __in ULONG ExitCode,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize )
    {
        auto pContext = _ValidateContext( Context, ContextSize );
        return m_managedCallbacks->ExitProcess( ExitCode, pContext );
    }

    virtual HRESULT LoadModule(
        __in ULONG64 ImageFileHandle,
        __in ULONG64 BaseOffset,
        __in ULONG ModuleSize,
        __in_opt PCWSTR ModuleName,
        __in_opt PCWSTR ImageName,
        __in ULONG CheckSum,
        __in ULONG TimeDateStamp,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize )
    {
        auto pContext = _ValidateContext( Context, ContextSize );
        return m_managedCallbacks->LoadModule( ImageFileHandle,
                                               BaseOffset,
                                               ModuleSize,
                                               ModuleName ? gcnew String( ModuleName ) : nullptr,
                                               ImageName ? gcnew String( ImageName ) : nullptr,
                                               CheckSum,
                                               TimeDateStamp,
                                               pContext );
    }

    virtual HRESULT UnloadModule(
        __in_opt PCWSTR ImageBaseName,
        __in ULONG64 BaseOffset,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize )
    {
        auto pContext = _ValidateContext( Context, ContextSize );
        return m_managedCallbacks->UnloadModule( ImageBaseName ? gcnew String( ImageBaseName ) : nullptr,
                                                 BaseOffset,
                                                 pContext );
    }

    virtual HRESULT SystemError(
        __in ULONG Error,
        __in ULONG Level,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize )
    {
        auto pContext = _ValidateContext( Context, ContextSize );
        return m_managedCallbacks->SystemError( Error, Level, pContext );
    }

    virtual HRESULT SessionStatus(
        __in ULONG Status )
    {
        return m_managedCallbacks->SessionStatus( (Microsoft::Diagnostics::Runtime::Interop::DEBUG_SESSION) Status );
    }

    virtual HRESULT ChangeDebuggeeState(
        __in ULONG Flags,
        __in ULONG64 Argument,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize )
    {
        auto pContext = _ValidateContext( Context, ContextSize );
        return m_managedCallbacks->ChangeDebuggeeState( (Microsoft::Diagnostics::Runtime::Interop::DEBUG_CDS) Flags, Argument, pContext );
    }

    virtual HRESULT ChangeEngineState(
        __in ULONG Flags,
        __in ULONG64 Argument,
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize )
    {
        auto pContext = _ValidateContext( Context, ContextSize );
        return m_managedCallbacks->ChangeEngineState( (Microsoft::Diagnostics::Runtime::Interop::DEBUG_CES) Flags, Argument, pContext );
    }

    virtual HRESULT ChangeSymbolState(
        __in ULONG Flags,
        __in ULONG64 Argument )
    {
        return m_managedCallbacks->ChangeSymbolState( (Microsoft::Diagnostics::Runtime::Interop::DEBUG_CSS) Flags, Argument );
    }


    DbgEngEventContextCallbacksAdapter( IDebugEventContextCallbacksImp^ managedCallbacks )
        : DbgEngCallbacksAdapter( managedCallbacks )
    {
    }

private:

    static Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT* _ValidateContext(
        __in PDEBUG_EVENT_CONTEXT Context,
        __in ULONG ContextSize )
    {
        if( Marshal::SizeOf( Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT::typeid ) != ContextSize )
        {
            throw gcnew System::Exception( L"Unexpected context size." );
        }
        return (Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT_CONTEXT*) Context;
    }
}; // end class DbgEngEventContextCallbacksAdapter


public ref class DbgEngInputCallbacksAdapter : DbgEngCallbacksAdapter<IDebugInputCallbacksImp, &IID_IDebugInputCallbacks>, IComDbgEngInputCallbacks
{
public:
    // IDebugInputCallbacks.

    virtual HRESULT StartInput(
        __in ULONG BufferSize )
    {
        return m_managedCallbacks->StartInput( BufferSize );
    }

    virtual HRESULT EndInput()
    {
        return m_managedCallbacks->EndInput();
    }

    DbgEngInputCallbacksAdapter( IDebugInputCallbacksImp^ managedCallbacks )
        : DbgEngCallbacksAdapter( managedCallbacks )
    {
    }
}; // end class DbgEngInputCallbacksAdapter


public ref class DbgEngOutputCallbacksAdapter : DbgEngCallbacksAdapter<IDebugOutputCallbacksImp, &IID_IDebugOutputCallbacksWide>, IComDbgEngOutputCallbacks
{
public:
    // IDebugOutputCallbacksWide.

    virtual HRESULT Output(
        __in ULONG Mask,
        __in PCWSTR Text )
    {
        return m_managedCallbacks->Output( (DEBUG_OUTPUT) Mask,
                                           Text ? gcnew String( Text ) : nullptr );
    }

    DbgEngOutputCallbacksAdapter( IDebugOutputCallbacksImp^ managedCallbacks )
        : DbgEngCallbacksAdapter( managedCallbacks )
    {
    }
}; // end class DbgEngOutputCallbacksAdapter



}
