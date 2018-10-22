
Describe "StringReading" {

    pushd
    New-TestApp -TestApp TestNativeConsoleApp -Attach -TargetName testApp -HiddenTargetWindow

    # The code under test here deals with reading multiple chunks of memory. To make sure
    # that future changes in the default chunk read size don't break test assumptions,
    # we'll explicitly set the chunk size here. That way the test code can test what
    # happens when the chunk read boundary occurs at certain positions within a string.
    $debugger.ZStringChunkReadSize = $pagesize / 4

    # We will also test reading "bad" strings (invalid encodings). Info about the BadElem
    # members below:
    #
    # UTF-8: For the first byte of a multibyte character, bit 7 and bit 6 are set
    # (0b11xxxxxx); the next bytes have bit 7 set and bit 6 unset (0b10xxxxxx). So to
    # corrupt things, we'll throw in a byte where the top bits are are set (but the
    # following byte won't be right).
    #
    # UTF-16: Handy site: http://www.russellcottrell.com/greek/utilities/SurrogatePairCalculator.htm
    # Here's a valid surrogate pair: D804 + DC04 (which is a circle-x thing). So we'll
    # just throw the first two bytes in.

    $symNameAndElemSizes = @( @{ Name      = 'g_narrowString' ;
                                 ElemSize  = 1
                                 ReadFunc  = 'ReadMemAs_szString'
                                 BadElem   = @( 0xd1 ) # used to deliberately corrupt string
                               },

                              @{ Name     = 'g_wideString'
                                 ElemSize = 2
                                 ReadFunc = 'ReadMemAs_wszString'
                                 # N.B. Little-endian!
                                 BadElem  = @( 0x04, 0xd8 ) # used to deliberately corrupt string
                               }
                            )

    It "can read a small portion of a large null-terminated string" {

        # (This seems like a pretty simple thing to be testing, but dbgeng's
        # ReadMultiByteStringVirtualWide / ReadUnicodeStringVirtualWide functions can't
        # currently do this, so this is actually exercising some code in DbgShell.)

        foreach( $thing in $symNameAndElemSizes )
        {
            $g = Get-DbgSymbol "TestNativeConsoleApp!$($thing.Name)"
            $g -ne $null | Should Be $true

            $smallPrefix = $debugger.($thing.ReadFunc)( (poi $g.Address), 10 ) # maxCch
            $smallPrefix | Should Be 'this is a '
        }
    }


    # Helper function for the following 'It' blocks.
    function ReadStringUpAgainstInvalidMemory( $cchStartOffset )
    {
        foreach( $thing in $symNameAndElemSizes )
        {
            $g = Get-DbgSymbol "TestNativeConsoleApp!$($thing.Name)"
            $g -ne $null | Should Be $true

            function WhackNullTerminator()
            {
                # It shouldn't matter what the element size is; we'll just set the last byte.
                eb ((poi $g.Address) + ($pagesize - 1)) -Value 0x58 # 'X'
            }

            function RestoreNullTerminator()
            {
                eb ((poi $g.Address) + ($pagesize - 1)) 0
            }

            $cchPage = $pagesize / $thing.ElemSize # count of characters in a page
            $cchSkip = $cchStartOffset
            $cbSkip = $cchSkip * $thing.ElemSize

            $str = $debugger.($thing.ReadFunc)( ((poi $g.Address) + $cbSkip), $cchPage ) # maxCch
            $str.Length | Should Be ($cchPage - ($cchSkip + 1)) # + 1 because of the null terminator

            $str = $debugger.($thing.ReadFunc)( ((poi $g.Address) + $cbSkip), $cchPage * 2 ) # maxCch
            $str.Length | Should Be ($cchPage - ($cchSkip + 1)) # + 1 because of the null terminator

            # Let's make sure it still works even without a null terminator.
            WhackNullTerminator

            try
            {
                $str = $debugger.($thing.ReadFunc)( ((poi $g.Address) + $cbSkip), $cchPage ) # maxCch
                $str.Length | Should Be ($cchPage - $cchSkip)

                $str = $debugger.($thing.ReadFunc)( ((poi $g.Address) + $cbSkip), $cchPage * 2 ) # maxCch
                $str.Length | Should Be ($cchPage - $cchSkip)
            }
            finally
            {
                RestoreNullTerminator
            }
        }
    } # end function ReadStringUpAgainstInvalidMemory()


    It "can read a string with or without a null terminator at the edge of valid memory" {

        # The scenario is that we have a null-terminated string, which ends right before
        # some invalid memory. We'll make sure we can read it okay, whether or not it has
        # null terminator.

        ReadStringUpAgainstInvalidMemory -cchStartOffset 0
    }


    It "can read a string at the edge of valid memory, where the read stride straddles the boundary" {

        # The scenario is similar to the previous one: we have a null-terminated string,
        # which ends right before some invalid memory. But we'll start reading it an some
        # weird offset into it, to ensure that the internal memory read stride covers that
        # boundary. (And we'll make sure it works with or without a null terminator.)

        ReadStringUpAgainstInvalidMemory -cchStartOffset 123
    }


    It "can deal with bad chars" {

        foreach( $thing in $symNameAndElemSizes )
        {
            $g = Get-DbgSymbol "TestNativeConsoleApp!$($thing.Name)"
            $g -ne $null | Should Be $true

            $originalBytes = $null

            $corruptAtOffset = 5 # we'll corrupt the fifth element

            function CorruptElement
            {
                if( $originalBytes ) { throw "we'll lose the currently-saved bytes" }

                $byteOffset = (poi $g.Address) + ($corruptAtOffset * $thing.ElemSize)

                $originalBytes = Read-DbgMemory $byteOffset -Length $thing.ElemSize

                Write-DbgMemory $byteOffset -Bytes $thing.BadElem
            }

            function RestoreElement
            {
                if( !$originalBytes ) { throw "no saved bytes" }

                $byteOffset = (poi $g.Address) + ($corruptAtOffset * $thing.ElemSize)

                Write-DbgMemory $byteOffset -Memory $originalBytes

                $originalBytes = $null
            }

            try
            {
                . CorruptElement # execute in current scope so that $originalBytes gets set in current scope

                # We should be able to read the string, despite it not being valid.

                $str = $debugger.($thing.ReadFunc)( (poi $g.Address), 10 ) # maxCch
                $str.StartsWith( 'this ' ) | Should Be $true
            }
            finally
            {
                . RestoreElement # execute in current scope so that $originalBytes gets set in current scope
            }
        }
    }

    .kill
    PostTestCheckAndResetCacheStats
    popd
}

