
Describe "AddressTransformation" {

    It "can undo PowerShell parsing in most^H^H^H^H many cases" {

        # N.B. IMPORTANT
        #
        # You might see some of the repeated constants below and think "I know; I'll just
        # factor those out into a variable real quick". /Don't do it./ Whether or not the
        # value is passed in as a literal or a variable is important to the tests.

        # In these cases, the value will be passed as a PSObject.

        ConvertTo-Number -Numberish 0x01234000 | Should Be 0x01234000
        ConvertTo-Number -Numberish   01234000 | Should Be 0x01234000

        # In these cases, the value will be passed as a PSObjects, but we won't be able to
        # retrieve the originally-typed string. Until we are able to do that, we'll have
        # to comment out the first test, because DbgShell can't have any way of knowing
        # if it needs to re-do the parsing or not.

        # 01234000 | ConvertTo-Number | Should Be 0x01234000
        0x01234000 | ConvertTo-Number | Should Be 0x01234000

        $var = 0x01234000 

        # In this case, it will be passed as a boxed int.
        ConvertTo-Number -Numberish $var | Should Be 0x01234000

        # In this case, it will be passed as a PSObject.
        $var | ConvertTo-Number          | Should Be 0x01234000


        # Make sure we also handle things that get parsed as float/double (scientific
        # notation), things with the top bit set, and things manually specified as
        # decimal:
        ConvertTo-Number -Numberish   012340e0 | Should Be 0x012340e0
        ConvertTo-Number -Numberish 0x012340e0 | Should Be 0x012340e0

        ConvertTo-Number -Numberish 0x81234000 | Should Be ([UInt64]::Parse( '081234000', 'AllowHexSpecifier' ))
        # PROBLEM: If there is not a leading 0, the argument will be passed as a straight
        # boxed int, as opposed to the PSObject we would get if there /were/ a leading 0.
        # And in the case of a boxed int, we can't tell where it came from, or how it was
        # typed (if it was typed at all). TODO: Investigate why PS behaves this way.
        # ConvertTo-Number -Numberish   81234000 | Should Be ([UInt64]::Parse( '081234000', 'AllowHexSpecifier' ))

        ConvertTo-Number -Numberish 0n2147483647 | Should Be 0x7fffffff
        ConvertTo-Number -Numberish 0n4294967295 | Should Be ([UInt64]::Parse(  'ffffffff', 'AllowHexSpecifier' ))
        ConvertTo-Number -Numberish 0n4294967296 | Should Be ([UInt64]::Parse( '100000000', 'AllowHexSpecifier' ))

        # Make sure that not having a leading zero does not mess up undoing of scientific
        # notation parsing:
        ConvertTo-Number -Numberish   812340e0 | Should Be ([UInt64]::Parse(  '812340e0', 'AllowHexSpecifier' ))
        ConvertTo-Number -Numberish 0x812340e0 | Should Be ([UInt64]::Parse(  '812340e0', 'AllowHexSpecifier' ))

        # (This case cannot work, similar to the case above, until we are able to get the
        # originally-typed string.)
        # 812340e0 | ConvertTo-Number | Should Be ([UInt64]::Parse( '812340e0', 'AllowHexSpecifier' ))
        0x812340e0 | ConvertTo-Number | Should Be ([UInt64]::Parse( '812340e0', 'AllowHexSpecifier' ))


        # Presence of a backtick should cause PS to parse it as a string, which we should
        # always be able to parse how we like.
        ConvertTo-Number -Numberish 0`01234000 | Should Be ([UInt64]::Parse( '01234000', 'AllowHexSpecifier' ))

        # But unfortunately, that means we cannot pipe such values in directly, because PS
        # will interpret the bare literal string as a command it should try to run:
        # WILL NOT WORK: 0`01234000 | ConvertTo-Number | Should Be ([UInt64]::Parse( '01234000', 'AllowHexSpecifier' ))
        # But this should:
        '0`01234000' | ConvertTo-Number | Should Be ([UInt64]::Parse( '01234000', 'AllowHexSpecifier' ))

        # As should this, even though the backtick is an escape character in double-quoted
        # strings, we can reverse-engineer what the string /was/ and then re-parse:
        "0`01234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( '01234000', 'AllowHexSpecifier' ))
        "0`11234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( '11234000', 'AllowHexSpecifier' ))
        "0`21234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( '21234000', 'AllowHexSpecifier' ))
        "0`31234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( '31234000', 'AllowHexSpecifier' ))
        "0`41234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( '41234000', 'AllowHexSpecifier' ))
        "0`51234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( '51234000', 'AllowHexSpecifier' ))
        "0`61234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( '61234000', 'AllowHexSpecifier' ))
        "0`71234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( '71234000', 'AllowHexSpecifier' ))
        "0`81234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( '81234000', 'AllowHexSpecifier' ))
        "0`91234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( '91234000', 'AllowHexSpecifier' ))
        "0`a1234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( 'a1234000', 'AllowHexSpecifier' ))
        "0`b1234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( 'b1234000', 'AllowHexSpecifier' ))
        "0`c1234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( 'c1234000', 'AllowHexSpecifier' ))
        "0`d1234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( 'd1234000', 'AllowHexSpecifier' ))
        "0`e1234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( 'e1234000', 'AllowHexSpecifier' ))
        "0`f1234000" | ConvertTo-Number | Should Be ([UInt64]::Parse( 'f1234000', 'AllowHexSpecifier' ))
    }
}

