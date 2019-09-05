// DbgEngWrapper.h

#pragma once
#define INITGUID
#include <windows.h>
#include <vcclr.h>
#include <msclr\marshal.h>
#include <memory>
#undef CreateProcess // We define some functions that we want called "CreateProcess", not "CreateProcessW".
#include "dbgeng.h"
#include "dbgmodel.h"
#undef DEBUG_PROCESS // We want to use the managed enum definition
//#include "EventCallbacks.h"

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace msclr::interop;
using namespace Microsoft::Diagnostics::Runtime::Interop;
using namespace System::Diagnostics::Tracing;

namespace DbgEngWrapper
{
    public delegate void NotifySomethingReallyBadHappened( String^ msg );

    // A custom deleter to let us use malloc-allocated pointers with unique_ptr.
    struct free_delete
    {
        void operator() (void* x) { free( x ); }
    };

    interface class IDebugEventCallbacksWideImp;
    interface class IDebugEventContextCallbacksImp;
    interface class IDebugInputCallbacksImp;
    interface class IDebugOutputCallbacksImp;
    class DbgEngEventCallbacks;

    ref class WDebugClient;
    ref class WDebugControl;
    ref class WDebugSystemObjects;
    ref class WDebugSymbols;
    ref class WDebugDataSpaces;
    ref class WDebugRegisters;
    ref class WDebugAdvanced;
    ref class WHostDataModelAccess;
    ref class WDataModelManager;
    ref class WDebugHost;


    public enum class ModelObjectKind : int
    {
        /// <summary>
        /// The model object is a property accessor which can be called to retrieve value, etc...
        ///
        /// Calling GetIntrinsicValue on the object will yield a variant in which the punkVal
        /// IUnknown pointer is an IModelPropertyAccessor.
        /// </summary>
        ObjectPropertyAccessor,

        /// <summary>
        /// The model object is a wrapped host context (allowing such to be used as an indexer, etc...)
        ///
        /// Calling GetIntrinsicValue on the object will yield a variant in which the punkVal
        /// IUnknown pointer is an IDebugHostContext.
        /// </summary>
        ObjectContext,

        /// <summary>
        /// It's a typed object within the debuggee.  It may or may not have a model associated with it.
        /// If it has a model, key/value pairs may be associated.
        ///
        /// This object type has no "intrinsic value".  It always has a location which can be acquired.
        /// </summary>
        ObjectTargetObject,

        /// <summary>
        /// It's a reference to an object within the debuggee (e.g.: the object *REFERS TO* a "target int" or a
        /// "target int&").  This is distinct from an object within the debuggee which is a reference (e.g.:
        /// the object *IS* a "target int&").
        ///
        /// This allows an evaluator or other client of the model to take a "reference" to a "reference"
        /// (object reference to a int&) or to take a "reference" to an object which is enregistered.
        ///
        /// This object type has no "intrinsic value".  It always has a location which can be acquired.
        /// The underlying object can be acquired through the Dereference method.
        /// </summary>
        ObjectTargetObjectReference,

        /// <summary>
        /// The model object is a synthetic object (a key/value/metadata store)
        ///
        /// This object type has no "intrinsic value" or location.  It is purely a key/value/metadata store.
        /// </summary>
        ObjectSynthetic,

        /// <summary>
        /// The model object represents no value.  If a given key exists but only has a value conditionally (e.g.: it's
        /// a property accessor), it can return NoValue to indicate this.  The caller should treat this appropriately (e.g.:
        /// not displaying the key/value in a visualization, etc...)
        ///
        /// This object type has no "intrinsic value" or location.
        /// </summary>
        ObjectNoValue,

        /// <summary>
        /// The model object represents an error.
        ///
        /// This object type has no "intrinsci value" or location.  It can always be converted to a string to determine
        /// the error message.
        /// </summary>
        ObjectError,

        /// <summary>
        /// The type is an intrinsic which is communicated through a variant (and the resulting variant type)
        ///
        /// Calling GetIntrinsicValue on this type will yield a variant in which the value has been packed in
        /// its natural form.  String objects are packed as VT_BSTR.
        /// </summary>
        ObjectIntrinsic,

        /// <summary>
        /// The model object is a method which can be called.
        ///
        /// Calling GetIntrinsicValue on the object will yield a variant in which the punkVal
        /// IUnknown pointer is an IModelMethod.
        /// </summary>
        ObjectMethod,

        /// <summary>
        /// The model object is a key reference.
        ///
        /// Calling GetIntrinsicValue on the object will yield a variant in which the punkVal
        /// IUnknown pointer is an IKeyReference.
        /// </summary>
        ObjectKeyReference,
    };

    static_assert( ::ObjectPropertyAccessor == (int) ModelObjectKind::ObjectPropertyAccessor, "enum should line up" );
    static_assert( ::ObjectKeyReference == (int) ModelObjectKind::ObjectKeyReference, "enum should line up" );


    // This contains the code common to all the IDebug* wrappers: a pointer to the
    // native interface, and explicit conversion operators for casting.
    template< typename TNativeInterface >
    public ref class WDebugEngInterface
    {
    protected:
        TNativeInterface* m_pNative;

        typedef TNativeInterface TN;

        WDebugEngInterface( TNativeInterface* pNative )
        {
            if( !pNative )
                throw gcnew ArgumentNullException( "pNative" );

            m_pNative = pNative;
        }

        WDebugEngInterface( IntPtr pNative )
        {
            if( (void*) pNative == (void*) nullptr )
                throw gcnew ArgumentNullException( "pNative" );

            m_pNative = (TNativeInterface*) (void*) pNative;
        }


        // This simple exception filter just saves the exception code for the caller and
        // then requests that the handler be executed.
        static int MyExceptionFilter( EXCEPTION_POINTERS* pEp, HRESULT* pCode )
        {
            *pCode = pEp->ExceptionRecord->ExceptionCode;
            return EXCEPTION_EXECUTE_HANDLER;
        }

        // My first attempt to wrap calls to dbgeng in __try/__except was to use macros to
        // stamp it out. Unfortunately some functions were not able to have __try (C2712:
        // "cannot use __try in functions that require object unwinding"). So that's why
        // the fancy template: a bunch of the calls need to be in separate functions
        // anyway, and I didn't want to have two ways to do it.
        //
        // One of the downsides of this template method, though, is that I had to add in a
        // lot of explicit casts: where before things like pin_ptr<ULONG> were implicitly
        // convertible to PULONG, and would match up with the call signature, suddenly I
        // was getting a lot of C2893: "Failed to specialize function template 'template
        // name'". You have to get the types just right /before/ the template stuff will
        // work out. Oh well.
        template<typename Method, typename ... Arguments>
        HRESULT
        CallMethodWithSehProtection(
            Method pfn,
            Arguments... args)
        {
            HRESULT hr = 0;
            __try
            {
                return (m_pNative->*pfn)(args...);
            }
            __except( MyExceptionFilter( GetExceptionInformation(), &hr ) )
            {
                String^ msg = String::Format( "SEH exception from dbgeng: 0x{0:x}", hr );
                WDebugClient::g_notifyBadThingCallback( msg );
                return hr;
            }
        }


        template<typename TInterface, typename Method, typename ... Arguments>
        static HRESULT
        CallMethodWithSehProtection(
            TInterface* t,
            Method pfn,
            Arguments... args)
        {
            HRESULT hr = 0;
            __try
            {
                return (t->*pfn)(args...);
            }
            __except( MyExceptionFilter( GetExceptionInformation(), &hr ) )
            {
                String^ msg = String::Format( "SEH exception from dbgeng: 0x{0:x}", hr );
                WDebugClient::g_notifyBadThingCallback( msg );
                return hr;
            }
        }

    public:
        IntPtr GetRaw()
        {
            return (IntPtr) m_pNative;
        }

        ~WDebugEngInterface()
        {
            // This calls the finalizer to perform native cleanup tasks, and also causes
            // CLR finalization to be suppressed.
            this->!WDebugEngInterface();
        }

        !WDebugEngInterface()
        {
            if( m_pNative )
            {
                int remainingRefs = m_pNative->Release();
                //Console::WriteLine( "Remaining refs: {0}", remainingRefs );
                m_pNative = nullptr;
            }
        }

        // Probably shouldn't ever use this, in case some references came from elsewhere.
     // void ReleaseAll()
     // {
     //     if( m_pNative )
     //     {
     //         while( m_pNative->Release() > 0 )
     //         {
     //         }

     //         m_pNative = nullptr;
     //     }
     // }


        static explicit operator WDebugClient^( WDebugEngInterface^ ptr )
        {
            ::IDebugClient6* pNative = nullptr;
            HRESULT hr = ptr->m_pNative->QueryInterface( IID_IDebugClient5, (PVOID*) &pNative );
            if( S_OK != hr )
            {
                Marshal::ThrowExceptionForHR( hr );
            }
            System::Diagnostics::Debug::Assert( nullptr != pNative );
            return gcnew WDebugClient( pNative );
        }

        static explicit operator WDebugControl^( WDebugEngInterface^ ptr )
        {
            ::IDebugControl6* pNative = nullptr;
            HRESULT hr = ptr->m_pNative->QueryInterface( IID_IDebugControl6, (PVOID*) &pNative );
            if( S_OK != hr )
            {
                Marshal::ThrowExceptionForHR( hr );
            }
            System::Diagnostics::Debug::Assert( nullptr != pNative );
            return gcnew WDebugControl( pNative );
        }

        static explicit operator WDebugSystemObjects^( WDebugEngInterface^ ptr )
        {
            ::IDebugSystemObjects4* pNative = nullptr;
            HRESULT hr = ptr->m_pNative->QueryInterface( IID_IDebugSystemObjects4, (PVOID*) &pNative );
            if( S_OK != hr )
            {
                Marshal::ThrowExceptionForHR( hr );
            }
            System::Diagnostics::Debug::Assert( nullptr != pNative );
            return gcnew WDebugSystemObjects( pNative );
        }

        static explicit operator WDebugSymbols^( WDebugEngInterface^ ptr )
        {
            ::IDebugSymbols5* pNative = nullptr;
            HRESULT hr = ptr->m_pNative->QueryInterface( IID_IDebugSymbols5, (PVOID*) &pNative );
            if( S_OK != hr )
            {
                Marshal::ThrowExceptionForHR( hr );
            }
            System::Diagnostics::Debug::Assert( nullptr != pNative );
            return gcnew WDebugSymbols( pNative );
        }

        static explicit operator WDebugDataSpaces^( WDebugEngInterface^ ptr )
        {
            ::IDebugDataSpaces4* pNative = nullptr;
            HRESULT hr = ptr->m_pNative->QueryInterface( IID_IDebugDataSpaces4, (PVOID*) &pNative );
            if( S_OK != hr )
            {
                Marshal::ThrowExceptionForHR( hr );
            }
            System::Diagnostics::Debug::Assert( nullptr != pNative );
            return gcnew WDebugDataSpaces( pNative );
        }

        static explicit operator WDebugRegisters^( WDebugEngInterface^ ptr )
        {
            ::IDebugRegisters2* pNative = nullptr;
            HRESULT hr = ptr->m_pNative->QueryInterface( IID_IDebugRegisters2, (PVOID*) &pNative );
            if( S_OK != hr )
            {
                Marshal::ThrowExceptionForHR( hr );
            }
            System::Diagnostics::Debug::Assert( nullptr != pNative );
            return gcnew WDebugRegisters( pNative );
        }

        static explicit operator WDebugAdvanced^( WDebugEngInterface^ ptr )
        {
            ::IDebugAdvanced3* pNative = nullptr;
            HRESULT hr = ptr->m_pNative->QueryInterface( IID_IDebugAdvanced3, (PVOID*) &pNative );
            if( S_OK != hr )
            {
                Marshal::ThrowExceptionForHR( hr );
            }
            System::Diagnostics::Debug::Assert( nullptr != pNative );
            return gcnew WDebugAdvanced( pNative );
        }

        static explicit operator WHostDataModelAccess^( WDebugEngInterface^ ptr )
        {
            ::IHostDataModelAccess* pNative = nullptr;
            HRESULT hr = ptr->m_pNative->QueryInterface( IID_IHostDataModelAccess, (PVOID*) &pNative );
            if( S_OK != hr )
            {
                Marshal::ThrowExceptionForHR( hr );
            }
            System::Diagnostics::Debug::Assert( nullptr != pNative );
            return gcnew WHostDataModelAccess( pNative );
        }
    }; // end WDebugEngInterface

    // This line forces the compiler to not throw away all the public static conversion
    // operators. You'd think that since it's a public "ref" class (managed), the
    // compiler would know that it shouldn't toss those out, but apparently not.
    template ref class WDebugEngInterface< ::IDebugClient6 >; // the template parameter doesn't actually matter

    [EventDataAttribute()]
    public ref class TlPayload_Int
    {
    public:
        property int HResult;

        TlPayload_Int( int hr ) { HResult = hr; }
    };

    public ref class WDebugClient : WDebugEngInterface< ::IDebugClient6 >
    {
    public:
        static EventSource^ g_log = gcnew EventSource("DbgEngWrapperTraceLoggingProvider");
        static NotifySomethingReallyBadHappened^ g_notifyBadThingCallback;


        WDebugClient( ::IDebugClient6* pDc );


        WDebugClient( IntPtr pDc );


        static int DebugCreate( NotifySomethingReallyBadHappened^ notifyBadThingCallback,
                                [Out] WDebugClient^% dc )
        {
            dc = nullptr;
            // TODO: Investigate DebugCreateEx
            // TODO: Upgrade to IDebugClient7
            ::IDebugClient6* pdc = nullptr;
            HRESULT hr = ::DebugCreate( IID_IDebugClient5, (PVOID*) &pdc );
            if( S_OK == hr )
            {
                dc = gcnew WDebugClient( pdc );
            }

// Seems to be a compiler bug; see:
// https://stackoverflow.com/questions/12151060/seemingly-inappropriate-compilation-warning-with-c-cli
//
// I don't know if it has been filed officially, but seeing as it's C++/CLI, I doubt it
// would get any attention.
#pragma warning (push)
#pragma warning (disable: 4538)
            g_log->Write( L"Created IDebugClient5", gcnew TlPayload_Int( hr ) );
#pragma warning (pop)

            g_notifyBadThingCallback = notifyBadThingCallback;
            return hr;
        }

        static int DebugConnect( String^ remoteOptions,
                                 [Out] WDebugClient^% dc )
        {
            dc = nullptr;
            ::IDebugClient6* pdc = nullptr;
            marshal_context^ mc = gcnew marshal_context();
            HRESULT hr = ::DebugConnectWide( mc->marshal_as<const wchar_t*>( remoteOptions ),
                                             IID_IDebugClient5,
                                             (PVOID*) &pdc );
            if( S_OK == hr )
            {
                dc = gcnew WDebugClient( pdc );
            }
            return hr;
        }

        // Use AttachKernelWide instead.
     // int AttachKernel


     // Use GetKernelConnectionOptionsWide instead.
     // int GetKernelConnectionOptions


        // Use SetKernelConnectionOptionsWide instead.
     // int SetKernelConnectionOptions


        // Use StartProcessServerWide instead.
     // int StartProcessServer


        // Use ConnectProcessServerWide instead.
     // int ConnectProcessServer


        int DisconnectProcessServer(
             UInt64 Server);


        int GetRunningProcessSystemIds(
             UInt64 Server,
             [Out] array<ULONG>^% Ids);
           //array<ULONG>^ Ids,
           //ULONG Count,
           //[Out] ULONG% ActualCount);


     // Use GetRunningProcessSystemIdByExecutableNameWide instead.
     // int GetRunningProcessSystemIdByExecutableName


        // Use GetRunningProcessDescriptionWide instead.
     // int GetRunningProcessDescription


        int AttachProcess(
             UInt64 Server,
             ULONG ProcessID,
             DEBUG_ATTACH AttachFlags);


        // Use CreateProcessWide instead.
     // int CreateProcess


        // Use CreateProcessAndAttachWide instead.
     // int CreateProcessAndAttach


        int GetProcessOptions(
             [Out] DEBUG_PROCESS% Options);


        int AddProcessOptions(
             DEBUG_PROCESS Options);


        int RemoveProcessOptions(
             DEBUG_PROCESS Options);


        int SetProcessOptions(
             DEBUG_PROCESS Options);


        // Use OpenDumpFileWide instead.
     // int OpenDumpFile


        // Use WriteDumpFileWide instead
     // int WriteDumpFile

        int ConnectSession(
             DEBUG_CONNECT_SESSION Flags,
             ULONG HistoryLimit);


        // Use StartServerWide instead.
     // int StartServer


        // Use OutputServersWide instead.
     // int OutputServer


        int TerminateProcesses();


        int DetachProcesses();


        int EndSession(
             DEBUG_END Flags);


        int GetExitCode(
             [Out] unsigned long% Code);


        int DispatchCallbacks(
             ULONG Timeout);


        // TODO: This is a little odd... could we get rid of the parameter? Or do you have
        // to have a separate IDebugClient to call this?
        int ExitDispatch(
             WDebugClient^ Client);


        int CreateClient(
             [Out] WDebugClient^% Client);


        int GetInputCallbacks(
            // TODO: what?
            [Out] IntPtr% Callbacks );
             //[Out] WDebugInputCallbacks^% Callbacks);


        int SetInputCallbacks(
             IDebugInputCallbacksImp^ Callbacks );

        /* GetOutputCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */


        // Use GetOutputCallbacksWide instead.
     // int GetOutputCallbacks(
     //      [Out] IntPtr% Callbacks);

        /* We may have to pass a debugger engine conversion thunk back in so we can't specify a specific interface */


        // Use SetOutputCallbacksWide instead.
     // int SetOutputCallbacks(
     //      IntPtr Callbacks);


        int GetOutputMask(
             [Out] DEBUG_OUTPUT% Mask);


        int SetOutputMask(
             DEBUG_OUTPUT Mask);


        int GetOtherOutputMask(
             WDebugClient^ Client,
             [Out] DEBUG_OUTPUT% Mask);


        int SetOtherOutputMask(
             WDebugClient^ Client,
             DEBUG_OUTPUT Mask);


        int GetOutputWidth(
             [Out] ULONG% Columns);


        int SetOutputWidth(
             ULONG Columns);


        // Use GetOutputLinePrefixWide instead.
     // int GetOutputLinePrefix


        // Use SetOutputLinePrefixWide instead.
     // int SetOutputLinePrefix


        // Use GetIdentityWide instead.
     // int GetIdentity


        // Use OutputIdentityWide instead.
     // int OutputIdentity

        /* GetEventCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */


     // int GetEventCallbacks(
     //      [Out] IntPtr% Callbacks);

        /* We may have to pass a debugger engine conversion thunk back in so we can't specify a specific interface */


      //int SetEventCallbacks(
      //     IntPtr Callbacks);


        int FlushCallbacks();


        /* IDebugClient2 */


        // Use WriteDumpFileWide instead.
     // int WriteDumpFile2(
     //      String^ DumpFile,
     //      DEBUG_DUMP Qualifier,
     //      DEBUG_FORMAT FormatFlags,
     //      String^ Comment);


     // Use AddDumpInformationFileWide.
     // int AddDumpInformationFile(
     //      String^ InfoFile,
     //      DEBUG_DUMP_FILE Type);


        int EndProcessServer(
             UInt64 Server);


        int WaitForProcessServerEnd(
             ULONG Timeout);


        int IsKernelDebuggerEnabled();


        int TerminateCurrentProcess();


        int DetachCurrentProcess();


        int AbandonCurrentProcess();

        /* IDebugClient3 */


        int GetRunningProcessSystemIdByExecutableNameWide(
             UInt64 Server,
             String^ ExeName,
             DEBUG_GET_PROC Flags,
             [Out] ULONG% Id);


        int GetRunningProcessDescriptionWide(
             UInt64 Server,
             ULONG SystemId,
             DEBUG_PROC_DESC Flags,
             [Out] String^% ExeName,
             [Out] String^% Description);


        // TODO: Should I deprecate this in favor of CreateProcess2
        int CreateProcessWide(
             UInt64 Server,
             String^ CommandLine,
             DEBUG_CREATE_PROCESS CreateFlags);


        int CreateProcessAndAttachWide(
             UInt64 Server,
             String^ CommandLine,
             DEBUG_CREATE_PROCESS CreateFlags,
             ULONG ProcessId,
             DEBUG_ATTACH AttachFlags);

        /* IDebugClient4 */


        int OpenDumpFileWide(
             String^ FileName,
             UInt64 FileHandle);


        int WriteDumpFileWide(
             String^ DumpFile,
             UInt64 FileHandle,
             DEBUG_DUMP Qualifier,
             DEBUG_FORMAT FormatFlags,
             String^ Comment);


        int AddDumpInformationFileWide(
             String^ FileName,
             UInt64 FileHandle,
             DEBUG_DUMP_FILE Type);


        int GetNumberDumpFiles(
             [Out] ULONG% Number);


      //int GetDumpFile(
      //     ULONG Index,
      //     StringBuilder^ Buffer,
      //     Int32 BufferSize,
      //     [Out] ULONG% NameSize,
      //     [Out] UInt64% Handle,
      //     [Out] ULONG% Type);


        int GetDumpFileWide(
             ULONG Index,
             [Out] String^% Name,
          // StringBuilder^ Buffer,
          // Int32 BufferSize,
          // [Out] ULONG% NameSize,
             [Out] UInt64% Handle,
             [Out] ULONG% Type);

        /* IDebugClient5 */


        int AttachKernelWide(
             DEBUG_ATTACH Flags,
             String^ ConnectOptions);


        int GetKernelConnectionOptionsWide(
             [Out] String^% Options);
          // StringBuilder^ Buffer,
          // Int32 BufferSize,
          // [Out] ULONG% OptionsSize);


        int SetKernelConnectionOptionsWide(
             String^ Options);


        int StartProcessServerWide(
             DEBUG_CLASS Flags,
             String^ Options,
             IntPtr Reserved);


        int ConnectProcessServerWide(
             String^ RemoteOptions,
             [Out] UInt64% Server);


        int StartServerWide(
             String^ Options);


        int OutputServersWide(
             DEBUG_OUTCTL OutputControl,
             String^ Machine,
             DEBUG_SERVERS Flags);

        /* GetOutputCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */


        int GetOutputCallbacksWide(
             [Out] IntPtr% Callbacks);

        /* We may have to pass a debugger engine conversion thunk back in so we can't specify a specific interface */


        int SetOutputCallbacksWide(
             [In] IDebugOutputCallbacksImp^ Callbacks);


        int GetOutputLinePrefixWide(
             [Out] String^% Prefix);
          // StringBuilder^ Buffer,
          // Int32 BufferSize,
          // [Out] ULONG% PrefixSize);


        int SetOutputLinePrefixWide(
             String^ Prefix);


        int GetIdentityWide(
             [Out] String^% Identity);
          // StringBuilder^ Buffer,
          // Int32 BufferSize,
          // [Out] ULONG% IdentitySize);;


        int OutputIdentityWide(
             DEBUG_OUTCTL OutputControl,
             ULONG Flags,
             String^ Format);

        /* GetEventCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */


        int GetEventCallbacksWide(
             [Out] IntPtr% Callbacks);

        /* We may have to pass a debugger engine conversion thunk back in so we can't specify a specific interface */


        int SetEventCallbacksWide(
             //IntPtr Callbacks)
             IDebugEventCallbacksWideImp^ Callbacks );


     // int CreateProcess2(
     //      UInt64 Server,
     //      String^ CommandLine,
     //      [Out] DEBUG_CREATE_PROCESS_OPTIONS% OptionsBuffer,
     //      ULONG OptionsBufferSize,
     //      String^ InitialDirectory,
     //      String^ Environment);


        int CreateProcess2Wide(
             UInt64 Server,
             String^ CommandLine,
             Microsoft::Diagnostics::Runtime::Interop::DEBUG_CREATE_PROCESS_OPTIONS% OptionsBuffer,
             String^ InitialDirectory,
             String^ Environment);


      //int CreateProcessAndAttach2(
      //     UInt64 Server,
      //     String^ CommandLine,
      //     [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_CREATE_PROCESS_OPTIONS% OptionsBuffer,
      //     ULONG OptionsBufferSize,
      //     String^ InitialDirectory,
      //     String^ Environment,
      //     ULONG ProcessId,
      //     DEBUG_ATTACH AttachFlags);


        int CreateProcessAndAttach2Wide(
             UInt64 Server,
             String^ CommandLine,
             Microsoft::Diagnostics::Runtime::Interop::DEBUG_CREATE_PROCESS_OPTIONS* OptionsBuffer,
             //ULONG OptionsBufferSize,
             String^ InitialDirectory,
             String^ Environment,
             ULONG ProcessId,
             DEBUG_ATTACH AttachFlags);


     // int PushOutputLinePrefix(
     //      String^ NewPrefix,
     //      [Out] UInt64% Handle);


        int PushOutputLinePrefixWide(
             String^ NewPrefix,
             [Out] UInt64% Handle);


        int PopOutputLinePrefix(
             UInt64 Handle);


        int GetNumberInputCallbacks(
             [Out] ULONG% Count);


        int GetNumberOutputCallbacks(
             [Out] ULONG% Count);


        int GetNumberEventCallbacks(
             Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT Flags,
             [Out] ULONG% Count);


     // int GetQuitLockString(
     //      StringBuilder^ Buffer,
     //      Int32 BufferSize,
     //      [Out] ULONG% StringSize);

     //
     // int SetQuitLockString(
     //      String^ LockString);


        int GetQuitLockStringWide(
             [Out] String^% QuitLockString);
           //StringBuilder^ Buffer,
           //Int32 BufferSize,
           //[Out] ULONG% StringSize);


        int SetQuitLockStringWide(
             String^ LockString);


        /* IDebugClient6 */

        int SetEventContextCallbacks(
             IDebugEventContextCallbacksImp^ Callbacks );
    };

    public ref class WDebugBreakpoint : public WDebugEngInterface< ::IDebugBreakpoint3 >
    {
    private:
        //
        // The reference count on the native IDebugBreakpointX object is a sham (AddRef
        // is "return 1;" and Release is "return 0;"). Instead of using the reference
        // count to manage object lifetime, the object is deleted when the breakpoint is
        // removed. To deal with this, we will:
        //
        //   1. When the native object is deleted, mark the managed object as being
        //      invalid (by nulling out the native interface pointer).
        //   2. Make sure that there are not multiple managed wrapper objects aliasing
        //      the same native object.
        //

        static System::Collections::Generic::Dictionary< IntPtr, WeakReference< WDebugBreakpoint^ >^ >^ sm_bps
                = gcnew System::Collections::Generic::Dictionary< IntPtr, WeakReference< WDebugBreakpoint^ >^ >();


        WDebugBreakpoint( ::IDebugBreakpoint3* pBp );

        WDebugBreakpoint( IntPtr pBp );

        void _CheckInterfaceAbandoned()
        {
            if( !m_pNative )
                throw gcnew ObjectDisposedException( L"The breakpoint has already been destroyed." );
        }

    internal:

        static WDebugBreakpoint^ GetBreakpoint( ::IDebugBreakpoint3* pBp )
        {
            return GetBreakpoint( (IntPtr) pBp );
        }

        static WDebugBreakpoint^ GetBreakpoint( IntPtr pBp )
        {
            WeakReference< WDebugBreakpoint^ >^ wrbp;
            WDebugBreakpoint^ bp = nullptr;
            if( sm_bps->TryGetValue( pBp, wrbp ) )
            {
                if( (wrbp->TryGetTarget( bp )) && (bp->m_pNative) )
                    return bp;
                else
                    sm_bps->Remove( pBp );
            } // end if( weakref found )
            bp = gcnew WDebugBreakpoint( pBp );
            sm_bps->Add( pBp, gcnew WeakReference< WDebugBreakpoint^ >( bp ) );
            return bp;
        } // end GetBreakpoint()

    public:
        // Retrieves debugger engine unique ID
        // for the breakpoint.  This ID is
        // fixed as long as the breakpoint exists
        // but after that may be reused.
        int GetId(
            [Out] ULONG% Id );

        // Retrieves the type of break and
        // processor type for the breakpoint.
        int GetType(
            [Out] ULONG% BreakType,
            [Out] ULONG% ProcType );

        // Returns the client that called AddBreakpoint.
        int GetAdder(
            [Out] WDebugClient^% Adder );

        int GetFlags(
            [Out] DEBUG_BREAKPOINT_FLAG% Flags );

        // Only certain flags can be changed.  Flags
        // are: GO_ONLY, ENABLE.
        // Sets the given flags.
        int AddFlags(
            DEBUG_BREAKPOINT_FLAG Flags );

        // Clears the given flags.
        int RemoveFlags(
            DEBUG_BREAKPOINT_FLAG Flags );

        // Sets the flags.
        int SetFlags(
            DEBUG_BREAKPOINT_FLAG Flags );


        // Controls the offset of the breakpoint.  The
        // interpretation of the offset value depends on
        // the type of breakpoint and its settings.  It
        // may be a code address, a data address, an
        // I/O port, etc.
        int GetOffset(
            [Out] UInt64% Offset );

        int SetOffset(
            UInt64 Offset );


        // Data breakpoint methods will fail if the
        // target platform does not support the
        // parameters used.
        // These methods only function for breakpoints
        // created as data breakpoints.
        int GetDataParameters(
            [Out] ULONG% Size,
            [Out] ULONG% AccessType );

        int SetDataParameters(
            ULONG Size,
            ULONG AccessType );


        // Pass count defaults to one.
        int GetPassCount(
            [Out] ULONG% Count );

        int SetPassCount(
            ULONG Count );

        // Gets the current number of times
        // the breakpoint has been hit since
        // it was last triggered.
        int GetCurrentPassCount(
            [Out] ULONG% Count );


        // If a match thread is set this breakpoint will
        // only trigger if it occurs on the match thread.
        // Otherwise it triggers for all threads.
        // Thread restrictions are not currently supported
        // in kernel mode.
        int GetMatchThreadId(
            [Out] ULONG% Id );

        int SetMatchThreadId(
            ULONG Thread );


        // Use GetCommandWide instead
        //int GetCommand

        // Uset SetCommandWide instead.
        //int SetCommand


        // Use GetOffsetExpressionWide instead.
        //int GetOffsetExpression

        // Uset SetOffsetExpressionWide instead.
        //int SetOffsetExpression


        int GetParameters(
            //[Out] PDEBUG_BREAKPOINT_PARAMETERS Params
            [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_BREAKPOINT_PARAMETERS% Params );


        // IDebugBreakpoint2.

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
        int GetCommandWide(
            [Out] String^% Command );
         // _Out_writes_opt_(BufferSize) PWSTR Buffer,
         // ULONG BufferSize,
         // _Out_opt_ ULONG% CommandSize );

        int GetCommandWide(
            [In] ULONG SizeHint,
            [Out] String^% Command );

        int SetCommandWide(
            String^ Command
            );


        // Offset expressions are evaluated immediately
        // and at module load and unload events.  If the
        // evaluation is successful the breakpoints
        // offset is updated and the breakpoint is
        // handled normally.  If the expression cannot
        // be evaluated the breakpoint is deferred.
        // Currently the only offset expression
        // supported is a module-relative symbol
        // of the form <Module>!<Symbol>.
        int GetOffsetExpressionWide(
            [Out] String^% Expression );
         // _Out_writes_opt_(BufferSize) PWSTR Buffer,
         // ULONG BufferSize,
         // _Out_opt_ ULONG% ExpressionSize );

        int GetOffsetExpressionWide(
            [In] ULONG SizeHint,
            [Out] String^% Expression );

        int SetOffsetExpressionWide(
            String^ Expression );


        // IDebugBreakpoint3.

        int GetGuid(
            [Out] System::Guid% Guid
            //[Out] LPGUID Guid
            );


        // Once a breakpoint has been "cleared", you don't want to touch it--even to call
        // Release() on the interface.
        void AbandonInterface()
        {
            m_pNative = nullptr;
        }
    };


    public ref class WDebugControl : WDebugEngInterface< ::IDebugControl6 >
    {
    public:

        WDebugControl( ::IDebugControl6* pDc );

        WDebugControl( IntPtr pDc );


        int GetInterrupt();

        int SetInterrupt(
            [In] DEBUG_INTERRUPT Flags);

        int GetInterruptTimeout(
            [Out] ULONG% Seconds);

        int SetInterruptTimeout(
            [In] ULONG Seconds);

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

        // Use ControlledOutputWide instead.
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

        // For this to work, you need to pass DEBUG_DISASM_EFFECTIVE_ADDRESS to the disassemble function.
        int GetDisassembleEffectiveOffset(
            [Out] UInt64% Offset);

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

        int GetNearInstruction(
            [In] UInt64 Offset,
            [In] int Delta,
            [Out] UInt64% NearOffset);

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

        int GetDebuggeeType(
            [Out] DEBUG_CLASS% Class,
            [Out] DEBUG_CLASS_QUALIFIER% Qualifier);

        int GetActualProcessorType(
            [Out] IMAGE_FILE_MACHINE% Type);

        int GetExecutingProcessorType(
            [Out] IMAGE_FILE_MACHINE% Type);

     // int GetNumberPossibleExecutingProcessorTypes(
     //     [Out] ULONG% Number);

        int GetPossibleExecutingProcessorTypes(
         // [In] ULONG Start,
         // [In] ULONG Count,
         // [Out, MarshalAs(UnmanagedType.LPArray)] IMAGE_FILE_MACHINE[] Types);
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::IMAGE_FILE_MACHINE>^% Types);

        int GetNumberProcessors(
            [Out] ULONG% Number);

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

        int GetSystemVersion(
            [Out] ULONG% PlatformId,
            [Out] ULONG% Major,
            [Out] ULONG% Minor,
            [Out] String^% ServicePackString,
            [Out] ULONG% ServicePackNumber,
            [Out] String^% BuildString);

  //    int GetPageSize(
  //        [Out] ULONG% Size);

        int IsPointer64Bit();

  //    int ReadBugCheckData(
  //        [Out] ULONG% Code,
  //        [Out] out UInt64 Arg1,
  //        [Out] out UInt64 Arg2,
  //        [Out] out UInt64 Arg3,
  //        [Out] out UInt64 Arg4);

     // int GetNumberSupportedProcessorTypes(
     //     [Out] ULONG% Number);

        int GetSupportedProcessorTypes(
         // [In] ULONG Start,
         // [In] ULONG Count,
         // [Out, MarshalAs(UnmanagedType.LPArray)] IMAGE_FILE_MACHINE[] Types);
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::IMAGE_FILE_MACHINE>^% Types);

  //    int GetProcessorTypeNames(
  //        [In] IMAGE_FILE_MACHINE Type,
  //        [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder FullNameBuffer,
  //        [In] Int32 FullNameBufferSize,
  //        [Out] ULONG% FullNameSize,
  //        [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder AbbrevNameBuffer,
  //        [In] Int32 AbbrevNameBufferSize,
  //        [Out] ULONG% AbbrevNameSize);

        int GetEffectiveProcessorType(
            [Out] IMAGE_FILE_MACHINE% Type);

        int SetEffectiveProcessorType(
            [In] IMAGE_FILE_MACHINE Type);

        int GetExecutionStatus(
            [Out] DEBUG_STATUS% Status);

        int SetExecutionStatus(
            [In] DEBUG_STATUS Status);

        int GetCodeLevel(
            [Out] DEBUG_LEVEL% Level);

        int SetCodeLevel(
            [In] DEBUG_LEVEL Level);

        int GetEngineOptions(
            [Out] DEBUG_ENGOPT% Options);

        int AddEngineOptions(
            [In] DEBUG_ENGOPT Options);

        int RemoveEngineOptions(
            [In] DEBUG_ENGOPT Options);

        int SetEngineOptions(
            [In] DEBUG_ENGOPT Options);

        int GetSystemErrorControl(
            [Out] ERROR_LEVEL% OutputLevel,
            [Out] ERROR_LEVEL% BreakLevel);

        int SetSystemErrorControl(
            [In] ERROR_LEVEL OutputLevel,
            [In] ERROR_LEVEL BreakLevel);

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

        int GetNumberBreakpoints(
            [Out] ULONG% Number);

  //    int GetBreakpointByIndex(
  //        [In] ULONG Index,
  //        [Out, MarshalAs(UnmanagedType.Interface)] out IDebugBreakpoint bp);

  //    int GetBreakpointById(
  //        [In] ULONG Id,
  //        [Out, MarshalAs(UnmanagedType.Interface)] out IDebugBreakpoint bp);

        int GetBreakpointParameters(
            //[In] ULONG Count,
            [In] array<ULONG>^ Ids,
            //[In] ULONG Start,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_BREAKPOINT_PARAMETERS>^% Params);

        int GetBreakpointParameters(
            [In] ULONG Count,
            //[In] array<ULONG>^ Ids,
            [In] ULONG Start,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_BREAKPOINT_PARAMETERS>^% Params);

  //    int AddBreakpoint(
  //        [In] DEBUG_BREAKPOINT_TYPE Type,
  //        [In] ULONG DesiredId,
  //        [Out, MarshalAs(UnmanagedType.Interface)] out IDebugBreakpoint Bp);

  //    int RemoveBreakpoint(
  //        [In, MarshalAs(UnmanagedType.Interface)] IDebugBreakpoint Bp);

        // Use AddExtensionWide instead.
  //    int AddExtension(
  //        [In, MarshalAs(UnmanagedType.LPStr)] string Path,
  //        [In] ULONG Flags,
  //        [Out] out UInt64 Handle);

        int RemoveExtension(
            [In] UInt64 Handle);

        // Use GetExtensionByPathWide instead.
  //    int GetExtensionByPath(
  //        [In, MarshalAs(UnmanagedType.LPStr)] string Path,
  //        [Out] out UInt64 Handle);

        // Use CallExtensionWide instead.
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

        int GetNumberEventFilters(
            [Out] ULONG% SpecificEvents,
            [Out] ULONG% SpecificExceptions,
            [Out] ULONG% ArbitraryExceptions);

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

        int GetSpecificFilterParameters(
            [In] ULONG Start,
            [In] ULONG Count,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_SPECIFIC_FILTER_PARAMETERS>^% Params);

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

        int GetExceptionFilterParameters(
            //[In] ULONG Count,
            [In] array<ULONG>^ Codes,
            //[In] ULONG Start,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_EXCEPTION_FILTER_PARAMETERS>^% Params);

        int GetExceptionFilterParameters(
            [In] ULONG Count,
            //[In] array<ULONG>^ Codes,
            [In] ULONG Start,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_EXCEPTION_FILTER_PARAMETERS>^% Params);

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

        int WaitForEvent(
            [In] DEBUG_WAIT Flags,
            [In] ULONG Timeout);

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

        int GetDumpFormatFlags(
            [Out] DEBUG_FORMAT% FormatFlags);

        int GetNumberTextReplacements(
            [Out] ULONG% NumRepl);

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

        int RemoveTextReplacements();

   //   int OutputTextReplacements(
   //       [In] DEBUG_OUTCTL OutputControl,
   //       [In] DEBUG_OUT_TEXT_REPL Flags);

   //   /* IDebugControl3 */

        int GetAssemblyOptions(
            [Out] DEBUG_ASMOPT% Options);

        int AddAssemblyOptions(
            [In] DEBUG_ASMOPT Options);

        int RemoveAssemblyOptions(
            [In] DEBUG_ASMOPT Options);

        int SetAssemblyOptions(
            [In] DEBUG_ASMOPT Options);

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

        int ControlledOutputWide(
            [In] DEBUG_OUTCTL OutputControl,
            [In] DEBUG_OUTPUT Mask,
            [In] String^ Message);

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

        int DisassembleWide(
            [In] UInt64 Offset,
            [In] DEBUG_DISASM Flags,
            [Out] String^% Disassembly,
            //[Out] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% DisassemblySize,
            [Out] UInt64% EndOffset);

        int GetProcessorTypeNamesWide(
            //[In] IMAGE_FILE_MACHINE Type,
            //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder FullNameBuffer,
            //[In] Int32 FullNameBufferSize,
            //[Out] ULONG% FullNameSize,
            //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder AbbrevNameBuffer,
            //[In] Int32 AbbrevNameBufferSize,
            //[Out] ULONG% AbbrevNameSize);
			[In] IMAGE_FILE_MACHINE Type,
			[Out] String^% FullName,
			[Out] String^% AbbrevName);

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

        int ExecuteWide(
            [In] DEBUG_OUTCTL OutputControl,
            [In] String^ Command,
            [In] DEBUG_EXECUTE Flags);

   //   int ExecuteCommandFileWide(
   //       [In] DEBUG_OUTCTL OutputControl,
   //       [In, MarshalAs(UnmanagedType.LPWStr)] string CommandFile,
   //       [In] DEBUG_EXECUTE Flags);

        int GetBreakpointByIndex2(
            [In] ULONG Index,
            [Out] WDebugBreakpoint^% bp);

        int GetBreakpointById2(
            [In] ULONG Id,
            [Out] WDebugBreakpoint^% bp);

        int AddBreakpoint2(
            [In] DEBUG_BREAKPOINT_TYPE Type,
            [In] ULONG DesiredId,
            [Out] WDebugBreakpoint^% Bp);

        int RemoveBreakpoint2(
            [In] WDebugBreakpoint^ Bp);

        int AddExtensionWide(
            [In] String^ Path,
            [In] ULONG Flags,
            [Out] UInt64% Handle);

        int GetExtensionByPathWide(
            [In] String^ Path,
            [Out] UInt64% Handle);

        int CallExtensionWide(
            [In] UInt64 Handle,
            [In] String^ Function,
            [In] String^ Arguments);

        int GetExtensionFunctionWide(
            [In] UInt64 Handle,
            [In] String^ FuncName,
            [Out] IntPtr% Function);

        int GetEventFilterTextWide(
            [In] ULONG Index,
            [Out] String^% Text );
            //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% TextSize);

        int GetEventFilterCommandWide(
            [In] ULONG Index,
            [Out] String^% Command );
            //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% CommandSize);

        int SetEventFilterCommandWide(
            [In] ULONG Index,
            [In] String^ Command);

        int GetSpecificFilterArgumentWide(
            [In] ULONG Index,
            [Out] String^% Argument );
            //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% ArgumentSize);

        int SetSpecificFilterArgumentWide(
            [In] ULONG Index,
            [In] String^ Argument);

        int GetExceptionFilterSecondCommandWide(
            [In] ULONG Index,
            [Out] String^% Command );
            //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% CommandSize);

   //   int SetExceptionFilterSecondCommandWide(
   //       [In] ULONG Index,
   //       [In, MarshalAs(UnmanagedType.LPWStr)] string Command);

        int GetLastEventInformationWide(
            [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_EVENT% Type,
            [Out] ULONG% ProcessId,
            [Out] ULONG% ThreadId,
            [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_LAST_EVENT_INFO% ExtraInformation,
            [Out] String^% Description);

   //   int GetTextReplacementWide(
   //       [In, Out] String^% AliasName, // "SrcText" is lousy param name.
   //       [In] ULONG Index,             // only used if AliasName is null, in which case AliasName gets filled in
   //    // [Out] String^% SrcBuffer,
   //    // [In] Int32 SrcBufferSize,
   //    // [Out] ULONG% SrcSize,
   //       [Out] String^% AliasValue);   // "DstBuffer" is a lousy param name.
   //    // [In] Int32 DstBufferSize,
   //    // [Out] ULONG% DstSize);

        int GetTextReplacementWide(
            [In] ULONG Index,
            [Out] String^% AliasName,
            [Out] String^% AliasValue);

        int GetTextReplacementWide(
            [In] String^ AliasName,
            [Out] String^% AliasValue);

        int SetTextReplacementWide(
            [In] String^ AliasName,    // "SrcText" is a lousy param name
            [In] String^ AliasValue);  // "DrcText" is a lousy param name

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

        int GetSystemVersionValues(
            [Out] ULONG% PlatformId,
            [Out] ULONG% Win32Major,
            [Out] ULONG% Win32Minor,
            [Out] ULONG% KdMajor,
            [Out] ULONG% KdMinor);

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

        int GetSystemVersionStringWide(
            [In] DEBUG_SYSVERSTR Which,
            [Out] String^% VersionString);

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

        int GetStackTraceEx(
            [In] UInt64 FrameOffset,
            [In] UInt64 StackOffset,
            [In] UInt64 InstructionOffset,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME_EX>^% Frames );
            //[In] Int32 FramesSize,
            //[Out] ULONG% FramesFilled);

        int GetStackTraceEx(
            [In] UInt64 FrameOffset,
            [In] UInt64 StackOffset,
            [In] UInt64 InstructionOffset,
            [In] Int32 MaxFrames,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME_EX>^% Frames );
            //[In] Int32 FramesSize,
            //[Out] ULONG% FramesFilled);

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

        int GetBreakpointByGuid(
            [In] System::Guid Guid,
            [Out] WDebugBreakpoint^% Bp);

        /* IDebugControl6 */

        int GetExecutionStatusEx([Out] DEBUG_STATUS% Status);

        int GetSynchronizationStatus(
            [Out] ULONG% SendsAttempted,
            [Out] ULONG% SecondsSinceLastResponse);
    };


    public ref class WDebugSystemObjects : WDebugEngInterface< ::IDebugSystemObjects4 >
    {
    public:

        WDebugSystemObjects( ::IDebugSystemObjects4* pDc );

        WDebugSystemObjects( IntPtr pDc );

        int GetEventThread(
            [Out] ULONG% Id);

        int GetEventProcess(
            [Out] ULONG% Id);

        int GetCurrentThreadId(
            [Out] ULONG% Id);

        int SetCurrentThreadId(
            [In] ULONG Id);

        int GetCurrentProcessId(
            [Out] ULONG% Id);

        int SetCurrentProcessId(
            [In] ULONG Id);

        int GetNumberThreads(
            [Out] ULONG% Number);

        int GetTotalNumberThreads(
            [Out] ULONG% Total,
            [Out] ULONG% LargestProcess);

        int GetThreadIdsByIndex(
            [In] ULONG Start,
            [In] ULONG Count,
            [Out] array<ULONG>^% Ids,
            [Out] array<ULONG>^% SysIds);

     // int GetThreadIdByProcessor(
     //     [In] ULONG Processor,
     //     [Out] ULONG% Id);

        int GetCurrentThreadDataOffset(
            [Out] UInt64% Offset);

     // int GetThreadIdByDataOffset(
     //     [In] UInt64 Offset,
     //     [Out] ULONG% Id);

        int GetCurrentThreadTeb(
            [Out] UInt64% Offset);

        int GetThreadIdByTeb(
            [In] UInt64 Offset,
            [Out] ULONG% Id);

        int GetCurrentThreadSystemId(
            [Out] ULONG% SysId);

        int GetThreadIdBySystemId(
            [In] ULONG SysId,
            [Out] ULONG% Id);

     // int GetCurrentThreadHandle(
     //     [Out] UInt64 Handle);

     // int GetThreadIdByHandle(
     //     [In] UInt64 Handle,
     //     [Out] ULONG% Id);

        int GetNumberProcesses(
            [Out] ULONG% Number);

        int GetProcessIdsByIndex(
            [In] ULONG Start,
            [In] ULONG Count,
            [Out] array<ULONG>^% Ids,
            [Out] array<ULONG>^% SysIds);

        int GetCurrentProcessDataOffset(
            [Out] UInt64% Offset);

     // int GetProcessIdByDataOffset(
     //     [In] UInt64 Offset,
     //     [Out] ULONG% Id);

        int GetCurrentProcessPeb(
            [Out] UInt64% Offset);

     // int GetProcessIdByPeb(
     //     [In] UInt64 Offset,
     //     [Out] ULONG% Id);

        int GetCurrentProcessSystemId(
            [Out] ULONG% SysId);

        int GetProcessIdBySystemId(
            [In] ULONG SysId,
            [Out] ULONG% Id);

        int GetCurrentProcessHandle(
            [Out] UInt64% Handle);

     // int GetProcessIdByHandle(
     //     [In] UInt64 Handle,
     //     [Out] ULONG% Id);

        // Use GetCurrentProcessExecutableNameWide
    //  int GetCurrentProcessExecutableName(
    //      [Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
    //      [In] Int32 BufferSize,
    //      [Out] ULONG% ExeSize);

    //  /* IDebugSystemObjects2 */

        int GetCurrentProcessUpTime(
            [Out] ULONG% UpTime);

        int GetImplicitThreadDataOffset(
            [Out] UInt64% Offset);

        int SetImplicitThreadDataOffset(
            [In] UInt64 Offset);

        int GetImplicitProcessDataOffset(
            [Out] UInt64% Offset);

        int SetImplicitProcessDataOffset(
            [In] UInt64 Offset);

        // IDebugSystemObjects3.

        int GetEventSystem(
            [Out] ULONG% Id);

        int GetCurrentSystemId(
            [Out] ULONG% Id);

        int SetCurrentSystemId(
            [In] ULONG Id);

        int GetNumberSystems(
            [Out] ULONG% Number);

        int GetSystemIdsByIndex(
            [In] ULONG Start,
            [In] ULONG Count,
            [Out] array<ULONG>^% Ids);

    //  int GetTotalNumberThreadsAndProcesses(
    //      [Out] PULONG TotalThreads,
    //      [Out] PULONG TotalProcesses,
    //      [Out] PULONG LargestProcessThreads,
    //      [Out] PULONG LargestSystemThreads,
    //      [Out] PULONG LargestSystemProcesses);

    //  int GetCurrentSystemServer(
    //      [Out] PULONG64 Server);

    //  int GetSystemByServer(
    //      [In] ULONG64 Server,
    //      [Out] PULONG Id);

    //  int GetCurrentSystemServerName(
    //      _Out_writes_opt_(BufferSize) PSTR Buffer,
    //      [In] ULONG BufferSize,
    //      _Out_opt_ PULONG NameSize);

    //  // IDebugSystemObjects4.

        int GetCurrentProcessExecutableNameWide(
    //      _Out_writes_opt_(BufferSize) PWSTR Buffer,
    //      [In] ULONG BufferSize,
    //      _Out_opt_ PULONG ExeSize);
            [Out] String^% Name);

    //  int GetCurrentSystemServerNameWide(
    //      _Out_writes_opt_(BufferSize) PWSTR Buffer,
    //      [In] ULONG BufferSize,
    //      _Out_opt_ PULONG NameSize);
    };

    ref class WDebugSymbolGroup;


    public ref class WDebugSymbols : WDebugEngInterface< ::IDebugSymbols5 >
    {
    public:

        WDebugSymbols( ::IDebugSymbols5* pDs );

        WDebugSymbols( IntPtr pDs );


        int GetSymbolOptions(
            [Out] SYMOPT% Options);

        int AddSymbolOptions(
            [In] SYMOPT Options);

        int RemoveSymbolOptions(
            [In] SYMOPT Options);

        int SetSymbolOptions(
            [In] SYMOPT Options);

        int GetNumberModules(
            [Out] ULONG% Loaded,
            [Out] ULONG% Unloaded);

        int GetModuleByIndex(
            [In] ULONG Index,
            [Out] UInt64% Base);

        int GetModuleByOffset(
            [In] UInt64 Offset,
            [In] ULONG StartIndex,
            [Out] ULONG% Index,
            [Out] UInt64% Base);

        int GetModuleParameters(
            [In] ULONG Count,
            [In] array<UInt64>^ Bases,
            [In] ULONG Start,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_PARAMETERS>^% Params);


        int GetTypeSize(
            [In] UInt64 Module,
            [In] ULONG TypeId,
            [Out] ULONG% Size);

        int GetOffsetTypeId(
            [In] UInt64 Offset,
            [Out] ULONG% TypeId,
            [Out] UInt64% Module);

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

        int ResetScope();

        int EndSymbolMatch(
            [In] UInt64 Handle);


        /* IDebugSymbols2 */

        int GetTypeOptions(
            [Out] DEBUG_TYPEOPTS% Options);

        int AddTypeOptions(
            [In] DEBUG_TYPEOPTS Options);

        int RemoveTypeOptions(
            [In] DEBUG_TYPEOPTS Options);

        int SetTypeOptions(
            [In] DEBUG_TYPEOPTS Options);

        /* IDebugSymbols3 */

        int GetNameByOffsetWide(
            [In] UInt64 Offset,
            [Out] String^% Name,
            //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder NameBuffer,
            //[In] Int32 NameBufferSize,
            //[Out] ULONG% NameSize,
            [Out] UInt64% Displacement);

        int GetOffsetByNameWide(
            [In] String^ Symbol,
            [Out] UInt64% Offset);

        int GetNearNameByOffsetWide(
            [In] UInt64 Offset,
            [In] int Delta,
            [Out] String^% Name,
            //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder NameBuffer,
            //[In] Int32 NameBufferSize,
            //[Out] ULONG% NameSize,
            [Out] UInt64% Displacement);

        int GetLineByOffsetWide(
            [In] UInt64 Offset,
            [Out] ULONG% Line,
            [Out] String^% File,
            //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder FileBuffer,
            //[In] Int32 FileBufferSize,
            //[Out] ULONG% FileSize,
            [Out] UInt64% Displacement);

        int GetOffsetByLineWide(
            [In] ULONG Line,
            [In] String^ File,
            [Out] UInt64% Offset);

        int GetModuleByModuleNameWide(
            [In] String^ Name,
            [In] ULONG StartIndex,
            [Out] ULONG% Index,
            [Out] UInt64% Base);

        int GetSymbolModuleWide(
            [In] String^ Symbol,
            [Out] UInt64% Base);

        int GetTypeNameWide(
            [In] UInt64 Module,
            [In] ULONG TypeId,
            [Out] String^% Name);
            //[Out] StringBuilder NameBuffer,
            //[In] Int32 NameBufferSize,
            //[Out] ULONG% NameSize);

        int GetTypeIdWide(
            [In] UInt64 Module,
            [In] String^ Name,
            [Out] ULONG% TypeId);

        int GetFieldOffsetWide(
            [In] UInt64 Module,
            [In] ULONG TypeId,
            [In] String^ Field,
            [Out] ULONG% Offset);

        int GetSymbolTypeIdWide(
            [In] String^ Symbol,
            [Out] ULONG% TypeId,
            [Out] UInt64% Module);

        int GetScopeSymbolGroup2(
            [In] DEBUG_SCOPE_GROUP Flags,
            [In] WDebugSymbolGroup^ Update,
            [Out] WDebugSymbolGroup^% Symbols);

        int CreateSymbolGroup2(
            [Out] WDebugSymbolGroup^% Group);

        int StartSymbolMatchWide(
            [In] String^ Pattern,
            [Out] UInt64% Handle);

        int GetNextSymbolMatchWide(
            [In] UInt64 Handle,
            [Out] String^% Match,
            //[Out] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% MatchSize,
            [Out] UInt64% Offset);

        int ReloadWide(
            [In] String^ Module);

        int GetSymbolPathWide(
            [Out] String^% Path);
            //[Out] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% PathSize);

        int SetSymbolPathWide(
            [In] String^ Path);

        int AppendSymbolPathWide(
            [In] String^ Addition);

        int GetImagePathWide(
            [Out] String ^% ImagePath);
            //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% PathSize);

        int SetImagePathWide(
            [In] String^ Path);

        int AppendImagePathWide(
            [In] String^ Addition);

        int GetSourcePathWide(
            [Out] String^% SourcePath);
            //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% PathSize);

        int GetSourcePathElementWide(
            [In] ULONG Index,
            [Out] String^% Element);
            //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% ElementSize);

        int SetSourcePathWide(
            [In] String^ Path);

        int AppendSourcePathWide(
            [In] String^ Addition);

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

     // int GetModuleVersionInformationWide(
     //     [In] ULONG Index,
     //     [In] String^ Item,
     //     [In] IntPtr Buffer,
     //     [In] Int32 BufferSize,
     //     [Out] ULONG% VerInfoSize);

     // int GetModuleVersionInformationWide(
     //     [In] UInt64 Base,
     //     [In] String^ Item,
     //     [In] IntPtr Buffer,
     //     [In] Int32 BufferSize,
     //     [Out] ULONG% VerInfoSize);

        int GetModuleVersionInformationWide_VS_FIXEDFILEINFO(
            [In] UInt64 Base,
            [Out] Microsoft::Diagnostics::Runtime::Interop::VS_FIXEDFILEINFO% fixedFileInfo );

        int GetModuleVersionInformationWide_VS_FIXEDFILEINFO(
            [In] ULONG Index,
            [Out] Microsoft::Diagnostics::Runtime::Interop::VS_FIXEDFILEINFO% fixedFileInfo );

        int GetModuleVersionInformationWide_Translations(
            [In] UInt64 Base,
            [Out] array<DWORD>^% LangCodepagePairs );

        int GetModuleVersionInformationWide_Translations(
            [In] ULONG Index,
            [Out] array<DWORD>^% LangCodepagePairs );

        int GetModuleVersionInformationWide_StringInfo(
            [In] UInt64 Base,
            [In] DWORD LangCodepagePair,
            [In] String^ StringName,
            [Out] String^% StringValue );

        int GetModuleVersionInformationWide_StringInfo(
            [In] ULONG Index,
            [In] DWORD LangCodepagePair,
            [In] String^ StringName,
            [Out] String^% StringValue );

        int GetModuleNameStringWide(
            [In] DEBUG_MODNAME Which,
            [In] ULONG Index,
            [In] UInt64 Base,
            [Out] String^% Name);
            //[Out] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% NameSize);

        int GetModuleNameStringWide(
            [In] DEBUG_MODNAME Which,
            [In] ULONG Index,
            [In] UInt64 Base,
            [In] ULONG NameSizeHint,
            [Out] String^% Name);

        int GetConstantNameWide(
            [In] UInt64 Module,
            [In] ULONG TypeId,
            [In] UInt64 Value,
            [Out] String^% Name);
            //[Out] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% NameSize);

        int GetFieldNameWide(
            [In] UInt64 Module,
            [In] ULONG TypeId,
            [In] ULONG FieldIndex,
            [Out] String^% Name);
            //[Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% NameSize);

        int IsManagedModule(
            [In] ULONG Index,
            [In] UInt64 Base);

        int GetModuleByModuleName2Wide(
            [In] String^ Name,
            [In] ULONG StartIndex,
            [In] DEBUG_GETMOD Flags,
            [Out] ULONG% Index,
            [Out] UInt64% Base);

        int GetModuleByOffset2(
            [In] UInt64 Offset,
            [In] ULONG StartIndex,
            [In] DEBUG_GETMOD Flags,
            [Out] ULONG% Index,
            [Out] UInt64% Base);

        int AddSyntheticModuleWide(
            [In] UInt64 Base,
            [In] ULONG Size,
            [In] String^ ImagePath,
            [In] String^ ModuleName,
            [In] DEBUG_ADDSYNTHMOD Flags);

        int RemoveSyntheticModule(
            [In] UInt64 Base);

        int GetCurrentScopeFrameIndex(
            [Out] ULONG% Index);

        int SetScopeFrameByIndex(
            [In] ULONG Index);

        int SetScopeFromJitDebugInfo(
            [In] ULONG OutputControl,
            [In] UInt64 InfoOffset);

        int SetScopeFromStoredEvent();

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

        int GetFieldTypeAndOffsetWide(
            [In] UInt64 Module,
            [In] ULONG ContainerTypeId,
            [In] String^ Field,
            [Out] ULONG% FieldTypeId,
            [Out] ULONG% Offset);

        int AddSyntheticSymbolWide(
            [In] UInt64 Offset,
            [In] ULONG Size,
            [In] String^ Name,
            [In] DEBUG_ADDSYNTHSYM Flags,
            [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID% Id);

        int RemoveSyntheticSymbol([In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID Id);

        int GetSymbolEntriesByOffset(
            [In] UInt64 Offset,
            [In] ULONG Flags,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID>^% Ids,
            [Out] array<UInt64>^% Displacements,
            [In] ULONG IdsCount);
            //[Out] ULONG% Entries);

        int GetSymbolEntriesByNameWide(
            [In] String^ Symbol,
            [In] ULONG Flags,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID>^% Ids);
            //[In] ULONG IdsCount,
            //[Out] ULONG% Entries);

        int GetSymbolEntryByToken(
            [In] UInt64 ModuleBase,
            [In] ULONG Token,
            [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID% Id);

        int GetSymbolEntryInformation(
            [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_MODULE_AND_ID* Id,
            [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_ENTRY% Info);

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

        int GetScopeEx(
            [Out] UInt64% InstructionOffset,
            [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME_EX% ScopeFrame,
            [In] IntPtr ScopeContext,
            [In] ULONG ScopeContextSize);

        int SetScopeEx(
            [In] UInt64 InstructionOffset,
            [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_STACK_FRAME_EX ScopeFrame,
            [In] IntPtr ScopeContext,
            [In] ULONG ScopeContextSize);

        int GetNameByInlineContextWide(
            [In] UInt64 Offset,
            [In] ULONG InlineContext,
            [Out] String^% Name,
            //[Out] StringBuilder NameBuffer,
            //[In] Int32 NameBufferSize,
            //[Out] ULONG% NameSize,
            [Out] UInt64% Displacement);

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

        int GetCurrentScopeFrameIndexEx(
            [In] DEBUG_FRAME Flags,
            [Out] ULONG% Index);

        int SetScopeFrameByIndexEx(
            [In] DEBUG_FRAME Flags,
            [In] ULONG Index);


    private:
        int _GetModuleVersionInformationWide_VS_FIXEDFILEINFO(
            [In] ULONG Index,
            [In] UInt64 Base,
            [Out] Microsoft::Diagnostics::Runtime::Interop::VS_FIXEDFILEINFO% fixedFileInfo );

        int _GetModuleVersionInformationWide_Translations(
            [In] ULONG Index,
            [In] UInt64 Base,
            [Out] array<DWORD>^% LangCodepagePairs );


        int _GetModuleVersionInformationWide_StringInfo(
            [In] ULONG Index,
            [In] UInt64 Base,
            [In] DWORD LangCodepagePair,
            [In] String^ StringName,
            [Out] String^% StringValue );
    }; // end WDebugSymbols



    public ref class WDebugSymbolGroup
    {
    private:
        ::IDebugSymbolGroup2* m_pDsg;

    public:

        WDebugSymbolGroup( ::IDebugSymbolGroup2* pDsg );

        WDebugSymbolGroup( IntPtr pDsg );

        IntPtr GetRaw()
        {
            return (IntPtr) m_pDsg;
        }


        int GetNumberSymbols(
            [Out] ULONG% Number);

        int RemoveSymbolByIndex(
            [In] ULONG Index);

        int GetSymbolParameters(
            [In] ULONG Start,
            [In] ULONG Count,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_PARAMETERS>^% Params);

        int ExpandSymbol(
            [In] ULONG Index,
            [In] Boolean Expand);

// #pragma push_macro("DEBUG_OUTPUT_SYMBOLS")
// #undef DEBUG_OUTPUT_SYMBOLS
//         int OutputSymbols(
//             [In] DEBUG_OUTCTL OutputControl,
//             [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_OUTPUT_SYMBOLS Flags,
//             [In] ULONG Start,
//             [In] ULONG Count);
// #pragma pop_macro("DEBUG_OUTPUT_SYMBOLS")

        /* IDebugSymbolGroup2 */

        int AddSymbolWide(
            [In] String^ Name,
            [In, Out] ULONG% Index);

        int RemoveSymbolByNameWide(
            [In] String^ Name);

        int GetSymbolNameWide(
            [In] ULONG Index,
            [Out] String^% Name);
            //[Out] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% NameSize);

        int WriteSymbolWide(
            [In] ULONG Index,
            [In] String^ Value);

        int OutputAsTypeWide(
            [In] ULONG Index,
            [In] String^ Type);

        int GetSymbolTypeNameWide(
            [In] ULONG Index,
            [Out] String^% Name);
            //[Out] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% NameSize);

        int GetSymbolSize(
            [In] ULONG Index,
            [Out] ULONG% Size);

        int GetSymbolOffset(
            [In] ULONG Index,
            [Out] UInt64% Offset);

        int GetSymbolRegister(
            [In] ULONG Index,
            [Out] ULONG% Register);

        int GetSymbolValueTextWide(
            [In] ULONG Index,
            [Out] String^% Text);
            //[Out] StringBuilder Buffer,
            //[In] Int32 BufferSize,
            //[Out] ULONG% NameSize);

        int GetSymbolEntryInformation(
            [In] ULONG Index,
            [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_SYMBOL_ENTRY% Info);
    }; // end WDebugSymbolGroup



    public ref class WDebugDataSpaces : WDebugEngInterface< ::IDebugDataSpaces4 >
    {
    public:

        WDebugDataSpaces( ::IDebugDataSpaces4* pDds );

        WDebugDataSpaces( IntPtr pDds );

        // Note that fewer than BytesRequested bytes may be returned!
        int ReadVirtual(
            [In] UInt64 Offset,
            [In] ULONG BytesRequested,
            [Out] array<byte>^% buffer);
            //[In] ULONG BufferSize,
            //[Out] ULONG% BytesRead);

        int ReadVirtualDirect(
            [In] UInt64 Offset,
            [In] ULONG BytesRequested,
            [In] BYTE* buffer,
            [Out] ULONG% BytesRead);

        // This is a convenience wrapper for ReadVirtual for reading a single discrete value
        // without needing an extra byte array or copies
        generic <typename TValue>
            where TValue : value class // should be `unmanaged` but that's not a choice we have
            int ReadVirtualValue(
                [In] UInt64 Offset,
                [Out] TValue% value);

        // Note that not all bytes may be written!
        int WriteVirtual(
            [In] UInt64 Offset,
            [In] array<byte>^ buffer,
            //[In] ULONG BufferSize,
            [Out] ULONG% BytesWritten);

        // Note that not all bytes may be written!
        int WriteVirtual(
            [In] UInt64 Offset,
            [In] BYTE* Buffer,
            [In] ULONG BufferSize,
            [Out] ULONG% BytesWritten);

        int SearchVirtual(
            [In] UInt64 Offset,
            [In] UInt64 Length,
            [In] array<byte>^ pattern,
            //[In] ULONG PatternSize,
            [In] ULONG PatternGranularity,
            [Out] UInt64% MatchOffset);

        int ReadVirtualUncached(
            [In] UInt64 Offset,
            [In] ULONG BytesRequested,
            [Out] array<byte>^% buffer);
            //[In] ULONG BufferSize,
            //[Out] ULONG% BytesRead);

        int WriteVirtualUncached(
            [In] UInt64 Offset,
            [In] array<byte>^ buffer,
            //[In] ULONG BufferSize,
            [Out] ULONG% BytesWritten);

        int ReadPointersVirtual(
            [In] ULONG Count,
            [In] UInt64 Offset,
            [Out] array<UInt64>^% Ptrs);

        int WritePointersVirtual(
            //[In] ULONG Count,
            [In] UInt64 Offset,
            [In] array<UInt64>^ Ptrs);

        int ReadPhysical(
            [In] UInt64 Offset,
            [In] ULONG BytesRequested,
            [Out] array<byte>^% buffer);
            //[In] ULONG BufferSize,
            //[Out] ULONG% BytesRead);

        int WritePhysical(
            [In] UInt64 Offset,
            [In] array<byte>^ buffer,
            //[In] ULONG BufferSize,
            [Out] ULONG% BytesWritten);

        int ReadControl(
            [In] ULONG Processor,
            [In] UInt64 Offset,
            [In] ULONG BytesRequested,
            [Out] array<byte>^% buffer);
            //[In] Int32 BufferSize,
            //[Out] ULONG% BytesRead);

        int WriteControl(
            [In] ULONG Processor,
            [In] UInt64 Offset,
            [In] array<byte>^ buffer,
            //[In] Int32 BufferSize,
            [Out] ULONG% BytesWritten);

        int ReadIo(
            [In] INTERFACE_TYPE InterfaceType,
            [In] ULONG BusNumber,
            [In] ULONG AddressSpace,
            [In] UInt64 Offset,
            [In] ULONG BytesRequested,
            [Out] array<byte>^% buffer);
            //[In] ULONG BufferSize,
            //[Out] ULONG% BytesRead);

        int WriteIo(
            [In] INTERFACE_TYPE InterfaceType,
            [In] ULONG BusNumber,
            [In] ULONG AddressSpace,
            [In] UInt64 Offset,
            [In] array<byte>^ buffer,
            //[In] ULONG BufferSize,
            [Out] ULONG% BytesWritten);

        int ReadMsr(
            [In] ULONG Msr,
            [Out] UInt64% MsrValue);

        int WriteMsr(
            [In] ULONG Msr,
            [In] UInt64 MsrValue);

        int ReadBusData(
            [In] BUS_DATA_TYPE BusDataType,
            [In] ULONG BusNumber,
            [In] ULONG SlotNumber,
            [In] ULONG Offset,
            [In] ULONG BytesRequested,
            [Out] array<byte>^% buffer);
            //[In] ULONG BufferSize,
            //[Out] ULONG% BytesRead);

        int WriteBusData(
            [In] BUS_DATA_TYPE BusDataType,
            [In] ULONG BusNumber,
            [In] ULONG SlotNumber,
            [In] ULONG Offset,
            [In] array<byte>^ buffer,
            //[In] ULONG BufferSize,
            [Out] ULONG% BytesWritten);

        int CheckLowMemory();

        int ReadDebuggerData(
            [In] ULONG Index,
            [In,Out] array<byte>^ buffer, // I'm going to punt on this one and make the caller decide how much to allocate
            //[In] ULONG BufferSize,
            [Out] ULONG% DataSize);

        int ReadProcessorSystemData(
            [In] ULONG Processor,
            [In] DEBUG_DATA Index,
            [In,Out] array<byte>^ buffer, // I'm going to punt on this one and make the caller decide how much to allocate
            //[In] ULONG BufferSize,
            [Out] ULONG% DataSize);

        /* IDebugDataSpaces2 */

        int VirtualToPhysical(
            [In] UInt64 Virtual,
            [Out] UInt64% Physical);

        int GetVirtualTranslationPhysicalOffsets(
            [In] UInt64 Virtual,
            [In,Out] array<UInt64>^ Offsets, // I'm going to punt on this one and make the caller decide how much to allocate
            //[In] ULONG OffsetsSize,
            [Out] ULONG% Levels);

        int ReadHandleData(
            [In] UInt64 Handle,
            [In] DEBUG_HANDLE_DATA_TYPE DataType,
            [In,Out] array<byte>^% buffer, // I'm going to punt on this one and make the caller decide how much to allocate
            //[In] ULONG BufferSize,
            [Out] ULONG% DataSize);

        int FillVirtual(
            [In] UInt64 Start,
            [In] ULONG Size,
            [In] array<byte>^ Pattern,
            //[In] ULONG PatternSize,
            [Out] ULONG% Filled);

        int FillPhysical(
            [In] UInt64 Start,
            [In] ULONG Size,
            [In] array<byte>^ Pattern,
            //[In] ULONG PatternSize,
            [Out] ULONG% Filled);

        int QueryVirtual(
            [In] UInt64 Offset,
            [Out] Microsoft::Diagnostics::Runtime::Interop::MEMORY_BASIC_INFORMATION64% Info);

        /* IDebugDataSpaces3 */

        int ReadImageNtHeaders(
            [In] UInt64 ImageBase,
            [Out] Microsoft::Diagnostics::Runtime::Interop::IMAGE_NT_HEADERS64% Headers);

        int ReadTagged(
            [In] Guid Tag,
            [In] ULONG Offset,
            [In,Out] array<byte>^% buffer,
            //[In] ULONG BufferSize,
            [Out] ULONG% TotalSize);

        int StartEnumTagged(
            [Out] UInt64% Handle);

        int GetNextTagged(
            [In] UInt64 Handle,
            [Out] Guid% Tag,
            [Out] ULONG% Size);

        int EndEnumTagged(
            [In] UInt64 Handle);

        /* IDebugDataSpaces4 */

        int GetOffsetInformation(
            [In] DEBUG_DATA_SPACE Space,
            [In] DEBUG_OFFSINFO Which,
            [In] UInt64 Offset,
            [In,Out] array<byte>^ buffer,
            //[In] ULONG BufferSize,
            [Out] ULONG% InfoSize);

        int GetNextDifferentlyValidOffsetVirtual(
            [In] UInt64 Offset,
            [Out] UInt64% NextOffset);

        int GetValidRegionVirtual(
            [In] UInt64 Base,
            [In] ULONG Size,
            [Out] UInt64% ValidBase,
            [Out] ULONG% ValidSize);

        int SearchVirtual2(
            [In] UInt64 Offset,
            [In] UInt64 Length,
            [In] DEBUG_VSEARCH Flags,
            [In] array<byte>^ Pattern,
            //[In] ULONG PatternSize,
            [In] ULONG PatternGranularity,
            [Out] UInt64% MatchOffset);

        /*
        int ReadMultiByteStringVirtual(
            [In] UInt64 Offset,
            [In] ULONG MaxBytes,
            [Out] String^% Result);
        */

        int ReadMultiByteStringVirtualWide(
            [In] UInt64 Offset,
            [In] ULONG MaxBytes,
            [In] CODE_PAGE CodePage,
            [Out] String^% Result);

        int ReadUnicodeStringVirtualWide(
            [In] UInt64 Offset,
            [In] ULONG MaxBytes,
            [Out] String^% Result);

        int ReadPhysical2(
            [In] UInt64 Offset,
            [In] ULONG BytesRequested,
            [In] DEBUG_PHYSICAL Flags,
            [Out] array<byte>^% buffer);
            //[In] ULONG BufferSize,
            //[Out] ULONG% BytesRead);

        int WritePhysical2(
            [In] UInt64 Offset,
            [In] DEBUG_PHYSICAL Flags,
            [In] array<byte>^ buffer,
            //[In] ULONG BufferSize,
            [Out] ULONG% BytesWritten);
    }; // end WDebugDataSpaces


    public ref class WDebugRegisters : WDebugEngInterface< ::IDebugRegisters2 >
    {
    public:

        WDebugRegisters( ::IDebugRegisters2* pDr );

        WDebugRegisters( IntPtr pDr );

        int GetNumberRegisters(
            [Out] ULONG% Number);

        int GetValue(
            [In] ULONG Register,
            [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE% Value);

        int SetValue(
            [In] ULONG Register,
            [In] Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE Value);

    //  int GetValues( //FIX ME!!! This needs to be tested // [danthom] <-- Wha?
    //      [In] ULONG Count,
    //      [In] array<ULONG>^ Indices,
    //      [In] ULONG Start,
    //      [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^% Values);

    //  int SetValues(
    //      [In] ULONG Count,
    //      [In] array<ULONG>^ Indices,
    //      [In] ULONG Start,
    //      [In] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^ Values);

    //  int OutputRegisters(
    //      [In] DEBUG_OUTCTL OutputControl,
    //      [In] DEBUG_REGISTERS Flags);

        int GetInstructionOffset(
            [Out] UInt64% Offset);

        int GetStackOffset(
            [Out] UInt64% Offset);

        int GetFrameOffset(
            [Out] UInt64% Offset);

        /* IDebugRegisters2 */

        int GetDescriptionWide(
            [In] ULONG Register,
            [Out] String^% Name,
            //[Out] StringBuilder NameBuffer,
            //[In] Int32 NameBufferSize,
            //[Out] ULONG% NameSize,
            [Out] Microsoft::Diagnostics::Runtime::Interop::DEBUG_REGISTER_DESCRIPTION% Desc);

        int GetIndexByNameWide(
            [In] String^ Name,
            [Out] ULONG% Index);

        int GetNumberPseudoRegisters(
            [Out] ULONG% Number );

        int GetPseudoDescriptionWide(
            [In] ULONG Register,
            [Out] String^% Name,
            //[Out] StringBuilder NameBuffer,
            //[In] Int32 NameBufferSize,
            //[Out] ULONG% NameSize,
            [Out] UInt64% TypeModule,
            [Out] ULONG% TypeId );

        int GetPseudoIndexByNameWide(
            [In] String^ Name,
            [Out] ULONG% Index );

        int GetPseudoValues(
            [In] DEBUG_REGSRC Source,
            [In] ULONG Count,
            [In] ULONG Start,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^% Values );

        int GetPseudoValues(
            [In] DEBUG_REGSRC Source,
            [In] ULONG Count,
            [In] array<ULONG>^ Indices,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^% Values );

     // int SetPseudoValues(
     //     [In] ULONG Source,
     //     [In] ULONG Count,
     //     [In] array<ULONG>^ Indices,
     //     [In] ULONG Start,
     //     [In] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^ Values );

        int GetValues2(
            [In] DEBUG_REGSRC Source,
            //[In] ULONG Count,
            [In] array<ULONG>^ Indices,
            //[In] ULONG Start,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^% Values );

        int GetValues2(
            [In] DEBUG_REGSRC Source,
            [In] ULONG Count,
            //[In] array<ULONG>^ Indices,
            [In] ULONG Start,
            [Out] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^% Values );

        int SetValues2(
            [In] ULONG Source,
            //[In] ULONG Count,
            [In] array<ULONG>^ Indices,
            //[In] ULONG Start,
            [In] array<Microsoft::Diagnostics::Runtime::Interop::DEBUG_VALUE>^ Values );

    //  int OutputRegisters2(
    //      [In] ULONG OutputControl,
    //      [In] ULONG Source,
    //      [In] ULONG Flags );

        int GetInstructionOffset2(
            [In] ULONG Source,
            [Out] UInt64% Offset );

        int GetStackOffset2(
            [In] ULONG Source,
            [Out] UInt64% Offset );

        int GetFrameOffset2(
            [In] ULONG Source,
            [Out] UInt64% Offset );
    }; // end class WDebugRegisters


    public ref class WDebugAdvanced : WDebugEngInterface< ::IDebugAdvanced3 >
    {
    public:

        WDebugAdvanced( ::IDebugAdvanced3* pDA );

        WDebugAdvanced( IntPtr pDA );

        // IDebugAdvanced.

        // Get/SetThreadContext offer control over
        // the full processor context for a thread.
        // Higher-level functions, such as the
        // IDebugRegisters interface, allow similar
        // access in simpler and more generic ways.
        // Get/SetThreadContext are useful when
        // large amounts of thread context must
        // be changed and processor-specific code
        // is not a problem.
        int GetThreadContext(
            [In] BYTE* Context,
            [In] ULONG ContextSize );

        int SetThreadContext(
            [In] BYTE* Context,
            [In] ULONG ContextSize );

        // IDebugAdvanced2.

        //
        // Generalized open-ended methods for querying
        // and manipulation.  The open-ended nature of
        // these methods makes it easy to add new requests,
        // although at a cost in convenience of calling.
        // Sufficiently common requests may have more specific,
        // simpler methods elsewhere.
        //

        int Request(
            [In] DEBUG_REQUEST Request,
            [In] BYTE* InBuffer,
            [In] ULONG InBufferSize,
            [In] BYTE* OutBuffer,
            [In] ULONG OutBufferSize,
            [Out] ULONG% OutSize );

     // int GetSourceFileInformation(
     //     [In] ULONG Which,
     //     [In] PSTR SourceFile,
     //     [In] ULONG64 Arg64,
     //     [In] ULONG Arg32,
     //     _Out_writes_bytes_opt_(BufferSize) IntPtr Buffer,
     //     [In] ULONG BufferSize,
     //     _Out_opt_ PULONG InfoSize
     //     );
     // int FindSourceFileAndToken(
     //     [In] ULONG StartElement,
     //     [In] ULONG64 ModAddr,
     //     [In] PCSTR File,
     //     [In] ULONG Flags,
     //     _In_reads_bytes_opt_(FileTokenSize) IntPtr FileToken,
     //     [In] ULONG FileTokenSize,
     //     _Out_opt_ PULONG FoundElement,
     //     _Out_writes_opt_(BufferSize) PSTR Buffer,
     //     [In] ULONG BufferSize,
     //     _Out_opt_ PULONG FoundSize
     //     );

     // int GetSymbolInformation(
     //     [In] ULONG Which,
     //     [In] ULONG64 Arg64,
     //     [In] ULONG Arg32,
     //     _Out_writes_bytes_opt_(BufferSize) IntPtr Buffer,
     //     [In] ULONG BufferSize,
     //     _Out_opt_ PULONG InfoSize,
     //     _Out_writes_opt_(StringBufferSize) PSTR StringBuffer,
     //     [In] ULONG StringBufferSize,
     //     _Out_opt_ PULONG StringSize
     //     );

     // int GetSystemObjectInformation(
     //     [In] ULONG Which,
     //     [In] ULONG64 Arg64,
     //     [In] ULONG Arg32,
     //     _Out_writes_bytes_opt_(BufferSize) IntPtr Buffer,
     //     [In] ULONG BufferSize,
     //     _Out_opt_ PULONG InfoSize
     //     );

     // // IDebugAdvanced3.

     // int GetSourceFileInformationWide(
     //     [In] ULONG Which,
     //     [In] PWSTR SourceFile,
     //     [In] ULONG64 Arg64,
     //     [In] ULONG Arg32,
     //     _Out_writes_bytes_opt_(BufferSize) IntPtr Buffer,
     //     [In] ULONG BufferSize,
     //     _Out_opt_ PULONG InfoSize
     //     );
     // int FindSourceFileAndTokenWide(
     //     [In] ULONG StartElement,
     //     [In] ULONG64 ModAddr,
     //     [In] PCWSTR File,
     //     [In] ULONG Flags,
     //     _In_reads_bytes_opt_(FileTokenSize) IntPtr FileToken,
     //     [In] ULONG FileTokenSize,
     //     _Out_opt_ PULONG FoundElement,
     //     _Out_writes_opt_(BufferSize) PWSTR Buffer,
     //     [In] ULONG BufferSize,
     //     _Out_opt_ PULONG FoundSize
     //     );

     // int GetSymbolInformationWide(
     //     [In] ULONG Which,
     //     [In] ULONG64 Arg64,
     //     [In] ULONG Arg32,
     //     _Out_writes_bytes_opt_(BufferSize) IntPtr Buffer,
     //     [In] ULONG BufferSize,
     //     _Out_opt_ PULONG InfoSize,
     //     _Out_writes_opt_(StringBufferSize) PWSTR StringBuffer,
     //     [In] ULONG StringBufferSize,
     //     _Out_opt_ PULONG StringSize
     //     );
    }; // end class WDebugAdvanced


    public ref class WDataModelManager : WDebugEngInterface< ::IDataModelManager2 >
    {
    public:

        WDataModelManager( ::IDataModelManager2* pDMM );

        WDataModelManager( IntPtr pDMM );

        int GetRootNamespace( [Out] IntPtr% rootNamespace );
    }; // end class WDataModelManager


    public ref class WDebugHost : WDebugEngInterface< ::IDebugHost >
    {
    public:

        WDebugHost( ::IDebugHost* pDH );

        WDebugHost( IntPtr pDH );
    }; // end class WDebugHost


    public ref class WHostDataModelAccess : WDebugEngInterface< ::IHostDataModelAccess >
    {
    public:

        WHostDataModelAccess( ::IHostDataModelAccess* pHDMA );

        WHostDataModelAccess( IntPtr pHDMA );

        int GetDataModel(
            [Out] WDataModelManager^% manager,
            [Out] WDebugHost^% host);
    }; // end class WHostDataModelAccess


    public ref class WModelObject : WDebugEngInterface< ::IModelObject >
    {
    private:
        WModelObject() : WDebugEngInterface( nullptr ) { throw gcnew Exception( L"should never be called" ); }

     // WModelObject( ::IModelObject* pMO );

     // WModelObject( IntPtr pMO );

    public:
        static int GetKind( IntPtr pModelObject, [Out] ModelObjectKind% kind );
    }; // end class WModelObject
}
