# DbgEngWrapper

The dbgeng.dll API (`IDebugClient`, `IDebugEtc.`) implements a subset of COM: each
`IDebug*` interface implements `IUnknown`, and given one interface, you can QueryInterface
it to get a different interface. But there is no IDL, no type library, no proxies/stubs,
no apartment or security concerns, etc.

So one way to call the `IDebug*` interfaces from managed code is via COM Interop. This
works okay for a lot of simple cases, but because the interfaces are not "full" COM, there
are some problems:

1. **Threading concerns:** if you happen to try to call an interface from a different
   apartment, the CLR's COM Interop mechanism will search around for a proxy/stub to
   transfer the call between apartments, but there are no such registered proxies/stubs,
   so that blows up. One way around that is to make sure you only use threads in the MTA,
   and/or never smuggle interface pointers across thread boundaries.
1. The dbgeng COM implementation does not always meet the CLR's COM Interop expectations.
   For instance, if you get an `IDebugClient` interface that is connected to a remote
   debugger (via `DebugConnect`), when creating the RCW for the interface, the CLR will
   QueryInterface for something which the interface does not implement, and then it fails
   to create the RCW.
1. Probably more.

You can work around (1) if you can control all the threads' COM state, but there is no
workaround for (2). Thus the need for the DbgEngWrapper library: a C++/CLI wrapper, which
bridges the managed-to-native gap via "IJW" (It Just Works) interop.

The DbgEngWrapper is not *even close to* complete, and is not a direct translation of the
native dbgeng API. For instance, for methods that return a string: rather than taking a
buffer, a buffer size, and a pointer to a variable to hold how much of the buffer was
filled (or how much is required); the DbgEngWrapper usually exposes a more
managed-friendly signature that simply returns the string, and handles the buffer stuff
(including re-allocating a larger buffer when necessary) under the hood.

