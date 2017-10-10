#pragma once

extern "C"
{

HRESULT CALLBACK DebugExtensionInitialize( _Out_ PULONG Version,
                                           _Out_ PULONG Flags );

void CALLBACK DebugExtensionUninitialize();


void CALLBACK DebugExtensionNotify( _In_ ULONG Notify,
                                    _In_ ULONG64 Argument );


HRESULT CALLBACK DebugExtensionQueryValueNames( _In_  PDEBUG_CLIENT Client,
                                                _In_  ULONG Flags,
                                                _Out_ PWSTR Buffer,
                                                _In_  ULONG BufferChars,
                                                _Out_ PULONG BufferNeeded );

HRESULT CALLBACK DebugExtensionProvideValue( _In_  PDEBUG_CLIENT Client,
                                             _In_  ULONG Flags,
                                             _In_  PCWSTR Name,
                                             _Out_ PULONG64 Value,
                                             _Out_ PULONG64 TypeModBase,
                                             _Out_ PULONG TypeId,
                                             _Out_ PULONG TypeFlags );


HRESULT CALLBACK dbgshell( IDebugClient* debugClient, PCSTR args );

HRESULT CALLBACK help( IDebugClient* debugClient, PCSTR args );

}
