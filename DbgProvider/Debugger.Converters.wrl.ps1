
Register-DbgValueConverterInfo {

    Set-StrictMode -Version Latest


    # We will use this "module" as a place to store stuff across invocations--namely, the
    # HSTRING_HEADER_INTERNAL type info.
    $wrlConverterCacheMod = New-Module { }
    & $wrlConverterCacheMod { $script:HSTRING_HEADER_INTERNAL_type = $null }

    #
    # TODO: BUGBUG: I should probably get rid of this caching stuff, because it won't work
    # for scenarios like 1) attach to x64 target, 2) dump an HSTRING, 3) detach, 4) attach
    # to an x86 target, 5) dump an HSTRING. But it's a really nice example of how to use
    # -CaptureContext and script-level stuff to store info.
    #

    # This function will get the type info out of the $wrlConverterCacheMod, or cache it
    # there if it hasn't been fetched yet.
    function _GetHstringHeaderType()
    {
        $headerType = & $wrlConverterCacheMod { $script:HSTRING_HEADER_INTERNAL_type }
        if( $null -eq $headerType )
        {
            # This commented-out line is handy to convince yourself that we really only
            # load it once.
            #[Console]::WriteLine( "Trying to get HSTRING_HEADER_INTERNAL type..." )
            $headerType = dt 'combase!HSTRING_HEADER_INTERNAL' -TypesOnly
            if( !$headerType )
            {
                # TODO: Will this happen for public symbols? Maybe people should be
                # able to read strings even without private symbols.
                return
            }
            else
            {
                & $wrlConverterCacheMod {
                    $script:HSTRING_HEADER_INTERNAL_type = $args[ 0 ]
                } $headerType
            }
        }
        return $headerType
    }


    New-DbgValueConverterInfo -TypeName @( '!HSTRING__', '!Platform::String' )-Converter {
        try
        {
            # $_ is the symbol
            $sym = $_

            $headerType = _GetHstringHeaderType
            if( $null -eq $headerType )
            {
                # TODO: Will this happen for public symbols? Maybe people should be able
                # to read strings even without private symbols.
                return $sym.StockValue
            }

            $header = dt $sym.Address $headerType

            $str = ''
            if( !$header.stringRef.DbgIsNull() )
            {
                # Just using $header.stringRef would work fine, except there might be
                # embedded nulls.
                $str = $Debugger.ReadMemAs_UnicodeString( $header.stringRef.DbgGetPointer(),
                                                          ($header.length * 2) )
            }

            $str = Add-Member -InputObj $str -MemberType 'NoteProperty' -Name 'Header' -Value $header -PassThru
            return $str
        }
        finally { }
    } -CaptureContext # end HSTRING__ converter
}

