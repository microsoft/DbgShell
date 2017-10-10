
Register-DbgValueConverterInfo {

    Set-StrictMode -Version Latest

    New-DbgValueConverterInfo -TypeName '!tagVARIANT' -Converter {
        try
        {
            # $_ is the symbol
            $sym = $_
            $stockValue = $sym.GetStockValue()

            $vt       =        $stockValue.vt -band 0x0fff # [System.Runtime.InteropServices.VarEnum]::VT_TYPEMASK
            $isVector = 0 -ne ($stockValue.vt -band [System.Runtime.InteropServices.VarEnum]::VT_VECTOR)
            $isArray  = 0 -ne ($stockValue.vt -band [System.Runtime.InteropServices.VarEnum]::VT_ARRAY)
            $isByRef  = 0 -ne ($stockValue.vt -band [System.Runtime.InteropServices.VarEnum]::VT_BYREF)

            # N.B. Not all VT_* or combinations of VT_* and the modiftying flags are valid in
            # a VARIANT structure. From WTypes.h:
            #
            #  VARENUM usage key,
            #
            #  * [V] - may appear in a VARIANT
            #  * [T] - may appear in a TYPEDESC
            #  * [P] - may appear in an OLE property set
            #  * [S] - may appear in a Safe Array
            #
            #
            #   VT_EMPTY            [V]   [P]     nothing
            #   VT_NULL             [V]   [P]     SQL style Null
            #   VT_I2               [V][T][P][S]  2 byte signed int
            #   VT_I4               [V][T][P][S]  4 byte signed int
            #   VT_R4               [V][T][P][S]  4 byte real
            #   VT_R8               [V][T][P][S]  8 byte real
            #   VT_CY               [V][T][P][S]  currency
            #   VT_DATE             [V][T][P][S]  date
            #   VT_BSTR             [V][T][P][S]  OLE Automation string
            #   VT_DISPATCH         [V][T]   [S]  IDispatch *
            #   VT_ERROR            [V][T][P][S]  SCODE
            #   VT_BOOL             [V][T][P][S]  True=-1, False=0
            #   VT_VARIANT          [V][T][P][S]  VARIANT *
            #   VT_UNKNOWN          [V][T]   [S]  IUnknown *
            #   VT_DECIMAL          [V][T]   [S]  16 byte fixed point
            #   VT_RECORD           [V]   [P][S]  user defined type
            #   VT_I1               [V][T][P][s]  signed char
            #   VT_UI1              [V][T][P][S]  unsigned char
            #   VT_UI2              [V][T][P][S]  unsigned short
            #   VT_UI4              [V][T][P][S]  unsigned long
            #   VT_I8                  [T][P]     signed 64-bit int
            #   VT_UI8                 [T][P]     unsigned 64-bit int
            #   VT_INT              [V][T][P][S]  signed machine int
            #   VT_UINT             [V][T]   [S]  unsigned machine int
            #   VT_INT_PTR             [T]        signed machine register size width
            #   VT_UINT_PTR            [T]        unsigned machine register size width
            #   VT_VOID                [T]        C style void
            #   VT_HRESULT             [T]        Standard return type
            #   VT_PTR                 [T]        pointer type
            #   VT_SAFEARRAY           [T]        (use VT_ARRAY in VARIANT)
            #   VT_CARRAY              [T]        C style array
            #   VT_USERDEFINED         [T]        user defined type
            #   VT_LPSTR               [T][P]     null terminated string
            #   VT_LPWSTR              [T][P]     wide null terminated string
            #   VT_FILETIME               [P]     FILETIME
            #   VT_BLOB                   [P]     Length prefixed bytes
            #   VT_STREAM                 [P]     Name of the stream follows
            #   VT_STORAGE                [P]     Name of the storage follows
            #   VT_STREAMED_OBJECT        [P]     Stream contains an object
            #   VT_STORED_OBJECT          [P]     Storage contains an object
            #   VT_VERSIONED_STREAM       [P]     Stream with a GUID version
            #   VT_BLOB_OBJECT            [P]     Blob contains an object
            #   VT_CF                     [P]     Clipboard format
            #   VT_CLSID                  [P]     A Class ID
            #   VT_VECTOR                 [P]     simple counted array
            #   VT_ARRAY            [V]           SAFEARRAY*
            #   VT_BYREF            [V]           void* for local use
            #   VT_BSTR_BLOB                      Reserved for system use
            #

            if( $isVector )
            {
                Write-Warning "A VT_VECTOR is not supposed to show up in a VARIANT."
                return $stockValue
            }

            # VT_EMPTY is 0.
            if( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_EMPTY ) {
                if( $isByRef -and $isArray ) {
                    return $stockValue.pparray
                }
                if( $isByRef ) {
                    return $stockValue.byref # void*
                }
                elseif( $isArray ) {
                    return $stockValue.parray
                }
                else {
                    return New-Object 'MS.Dbg.DbgNoValue' -Arg @( $sym )
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_NULL ) {
                return [System.DBNull]::Value
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_I2 ) {
                if( $isByRef ) {
                    return $stockValue.piVal
                }
                else {
                    return $stockValue.iVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_I4 ) {
                if( $isByRef ) {
                    return $stockValue.pintVal
                }
                else {
                    return $stockValue.intVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_R4 ) {
                if( $isByRef ) {
                    return $stockValue.pfltVal
                }
                else {
                    return $stockValue.fltVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_R8 ) {
                if( $isByRef ) {
                    return $stockValue.pdblVal
                }
                else {
                    return $stockValue.dblVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_CY ) {
                if( $isByRef ) {
                    return $stockValue.pcyVal
                }
                else {
                    return $stockValue.cyVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_DATE ) {
                if( $isByRef ) {
                    return $stockValue.pdate
                }
                else {
                    return $stockValue.date
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_BSTR ) {
                # TODO: I don't think I treat BSTRs any differently than regular C strings...
                if( $isByRef ) {
                    return $stockValue.pbstrVal
                }
                else {
                    return $stockValue.bstrVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_DISPATCH ) {
                if( $isByRef ) {
                    return $stockValue.ppdispVal
                }
                else {
                    return $stockValue.pdispVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_ERROR ) {
                if( $isByRef ) {
                    return $stockValue.pscode
                }
                else {
                    return $stockValue.scode
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_BOOL ) {
                if( $isByRef ) {
                    # TODO
                }
                else {
                    if( 0xffff -eq $stockValue.boolVal ) {
                        return $true
                    } elseif( 0 -eq $stockValue.boolVal ) {
                        return $false
                    }
                    else {
                        # WARNING! all-bits or no-bits are supposed to be the only valid values.
                        return "Invalid VARIANT_BOOL: $($stockValue.boolVal)"
                    }
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_VARIANT ) {
                if( $isByRef ) {
                    return $stockValue.pvarVal
                }
                else {
                    Write-Warning "A VARIANT cannot contain a naked VARIANT."
                    return $stockValue
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_UNKNOWN ) {
                if( $isByRef ) {
                    return $stockValue.ppunkVal
                }
                else {
                    return $stockValue.punkVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_DECIMAL ) {
                if( $isByRef ) {
                    return $stockValue.pdecVal
                }
                else {
                    return $stockValue.decVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_I1 ) {
                if( $isByRef ) {
                    return $stockValue.pcVal
                }
                else {
                    return $stockValue.cVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_UI1 ) {
                if( $isByRef ) {
                    return $stockValue.pbVal
                }
                else {
                    return $stockValue.bVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_UI2 ) {
                if( $isByRef ) {
                    return $stockValue.puiVal
                }
                else {
                    return $stockValue.uiVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_UI4 ) {
                if( $isByRef ) {
                    return $stockValue.puintVal
                }
                else {
                    return $stockValue.uintVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_I8 ) {
                if( $isByRef ) {
                    return $stockValue.pllVal
                }
                else {
                    return $stockValue.llVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_UI8 ) {
                if( $isByRef ) {
                    return $stockValue.pullVal
                }
                else {
                    return $stockValue.ullVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_INT ) {
                # What is a "signed machine integer"? I guess I would need to detect the
                # target's machine type and make a decision of what field to use based on
                # that. But I'll just guess 4 bytes instead.
                if( $isByRef ) {
                    return $stockValue.pintVal
                }
                else {
                    return $stockValue.intVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_UINT ) {
                # What is an "unsigned machine integer"? I guess I would need to detect
                # the target's machine type and make a decision of what field to use based
                # on that. But I'll just guess 4 bytes instead.
                if( $isByRef ) {
                    return $stockValue.puintVal
                }
                else {
                    return $stockValue.uintVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_VOID ) {
                if( $isByRef ) {
                    # TODO
                }
                else {
                    return $stockValue.byref
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_HRESULT ) {
                if( $isByRef ) {
                    return $stockValue.puintVal
                }
                else {
                    return $stockValue.uintVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_PTR ) {
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_SAFEARRAY ) {
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_CARRAY ) {
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_USERDEFINED ) {
                return $stockValue
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_LPSTR ) {
                if( $isByRef ) {
                    # TODO
                }
                else {
                    return $stockValue.pcVal
                }
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_LPWSTR ) {
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_RECORD ) {
                # TODO assert: no $isByRef, etc.
                return $stockValue.pRecInfo
            }
            <# VT_INT_PTR is not defined on that enum, and it's not valid for a VARIANT anyway.
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_INT_PTR ) {
                if( $isByRef ) { # TODO }
                else { # TODO }
            }
            #>
            <# VT_UINT_PTR is not defined on that enum, and it's not valid for a VARIANT anyway.
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_UINT_PTR ) {
                if( $isByRef ) { # TODO }
                else { # TODO }
            }
            #>
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_FILETIME ) {
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_BLOB ) {
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_STREAM ) {
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_STORAGE ) {
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_STREAMED_OBJECT ) {
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_STORED_OBJECT ) {
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_BLOB_OBJECT ) {
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_CF ) {
            }
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_CLSID ) {
            }
            <# VT_VERSIONED_STREAM is not defined on that enum, and it's not valid for a VARIANT anyway.
            elseif( $vt -eq [System.Runtime.InteropServices.VarEnum]::VT_VERSIONED_STREAM ) {
            }
            #>

            return $stockValue
        }
        finally { }
    } # end VARIANT converter

}
