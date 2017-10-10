// v2Shim.cpp : Defines the exported functions for the DLL application.
//

#include "stdafx.h"
#include <objbase.h>

extern "C"
{
    HRESULT CallCoCreateInstance( _In_   REFCLSID rclsid,
                                  _In_   LPUNKNOWN pUnkOuter,
                                  _In_   DWORD dwClsContext,
                                  _In_   REFIID riid,
                                  _Out_  LPVOID *ppv )
    {
        return CoCreateInstance( rclsid,
                                 pUnkOuter,
                                 dwClsContext,
                                 riid,
                                 ppv );
    } // end CallCoCreateInstance()
}
