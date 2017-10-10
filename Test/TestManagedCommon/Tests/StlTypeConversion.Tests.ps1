
Describe "StlTypeConversion" {

    pushd
    New-TestApp -TestApp TestNativeConsoleApp -Attach -TargetName testApp -HiddenTargetWindow

    It "can handle vector<int>" {

        $g = Get-DbgSymbol TestNativeConsoleApp!g_intVector
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        4 | Should Be ($gv.size())
        4 | Should Be ($gv.Count)
        10 | Should Be ($gv.capacity())

        0 | Should Be ($gv[0])
        1 | Should Be ($gv[1])
        2 | Should Be ($gv[2])
        3 | Should Be ($gv[3])

        # It got converted to a System.Collections.ObjectModel.ReadOnlyCollection, but make
        # sure that .ToString() gives the symbol type name.
        #
        # Dev12: std::vector<int
        # Dev14: std::_Vector_val<std::_Simple_types<int
        ([MS.Dbg.ColorString]::StripPrerenderedColor( $gv.ToString() ).StartsWith( 'std::' )) | Should Be $true

        # Make sure we didn't bust something in symbol display.
        $g | ft
        $gv
    }

    It "can handle vector<wstring>" {

        $g = Get-DbgSymbol TestNativeConsoleApp!g_wsVector
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        4 | Should Be ($gv.size())
        4 | Should Be ($gv.Count)
        10 | Should Be ($gv.capacity())

        "zero"  | Should Be ($gv[0])
        "one"   | Should Be ($gv[1])
        "two"   | Should Be ($gv[2])
        "three" | Should Be ($gv[3])
    }

    It "can handle vector<string>" {

        $g = Get-DbgSymbol TestNativeConsoleApp!g_sVector
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        4 | Should Be ($gv.size())
        4 | Should Be ($gv.Count)
        10 | Should Be ($gv.capacity())

        "zero"  | Should Be ($gv[0])
        "one"   | Should Be ($gv[1])
        "two"   | Should Be ($gv[2])
        "three" | Should Be ($gv[3])
    }

    It "can handle vector<bool>" {

        $g = Get-DbgSymbol TestNativeConsoleApp!g_bVector
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        3 | Should Be ($gv.Count)

        ($gv[0]) | Should Be $true
        ($gv[1]) | Should Be $false
        ($gv[2]) | Should Be $true

        [bool] $itThrew = $false
        try
        {
            $gv[3]
        }
        catch
        {
            $itThrew = $true
            # Expected.
        }
        $itThrew | Should Be $true
    }

    It "can handle an empty vector<bool>" {

        $g = Get-DbgSymbol TestNativeConsoleApp!g_bVectorEmpty
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        0 | Should Be ($gv.Count)
    }

    It "can handle a map<int, string>" {

        $g = Get-DbgSymbol TestNativeConsoleApp!g_intStringMap
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        4 | Should Be ($gv.Count)

        "zero"  | Should Be ($gv[0])
        "one"   | Should Be ($gv[1])
        "two"   | Should Be ($gv[2])
        "three" | Should Be ($gv[3])
    }

    It "can handle a map<string, string>" {

        $g = Get-DbgSymbol TestNativeConsoleApp!g_stringStringMap
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        4 | Should Be ($gv.get_Count())

        "nothing"   | Should Be ($gv["zero" ])
        "something" | Should Be ($gv["one"  ])
        "a couple"  | Should Be ($gv["two"  ])
        "several"   | Should Be ($gv["three"])
    }

    It "can handle a multimap<wstring, wstring>" {

        $g = Get-DbgSymbol TestNativeConsoleApp!g_stringStringMultimap
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        4 | Should Be ($gv.get_Count())
        8 | Should Be ($gv.get_UncollapsedCount())

        ($gv.get_IsReadOnly()) | Should Be $true

        "nothing"   | Should Be ($gv["zero" ][ 0 ])
        "something" | Should Be ($gv["one"  ][ 0 ])
        "a couple"  | Should Be ($gv["two"  ][ 0 ])
        "several"   | Should Be ($gv["three"][ 0 ])

        "nothing again"   | Should Be ($gv["zero" ][ 1 ])
        "something again" | Should Be ($gv["one"  ][ 1 ])
        "a couple again"  | Should Be ($gv["two"  ][ 1 ])
        "several again"   | Should Be ($gv["three"][ 1 ])

        "one"   | Should Be ($gv.get_KeyByIndex()[ 0 ])
        "one"   | Should Be ($gv.get_KeyByIndex()[ 1 ])
        "three" | Should Be ($gv.get_KeyByIndex()[ 2 ])
        "three" | Should Be ($gv.get_KeyByIndex()[ 3 ])
        "two"   | Should Be ($gv.get_KeyByIndex()[ 4 ])
        "two"   | Should Be ($gv.get_KeyByIndex()[ 5 ])
        "zero"  | Should Be ($gv.get_KeyByIndex()[ 6 ])
        "zero"  | Should Be ($gv.get_KeyByIndex()[ 7 ])

        "something"       | Should Be ($gv.get_ValueByIndex()[ 0 ])
        "something again" | Should Be ($gv.get_ValueByIndex()[ 1 ])
        "several"         | Should Be ($gv.get_ValueByIndex()[ 2 ])
        "several again"   | Should Be ($gv.get_ValueByIndex()[ 3 ])
        "a couple"        | Should Be ($gv.get_ValueByIndex()[ 4 ])
        "a couple again"  | Should Be ($gv.get_ValueByIndex()[ 5 ])
        "nothing"         | Should Be ($gv.get_ValueByIndex()[ 6 ])
        "nothing again"   | Should Be ($gv.get_ValueByIndex()[ 7 ])

        $keys = @()
        $gv.get_Keys() | %{ $keys += $_ }
        "one"   | Should Be ($keys[0])
        "three" | Should Be ($keys[1])
        "two"   | Should Be ($keys[2])
        "zero"  | Should Be ($keys[3])

        $values = @()
        $gv.get_Values() | %{ $values += ,$_ }
        "something" | Should Be ($values[ 0 ][ 0 ])
        "several"   | Should Be ($values[ 1 ][ 0 ])
        "a couple"  | Should Be ($values[ 2 ][ 0 ])
        "nothing"   | Should Be ($values[ 3 ][ 0 ])

        "something again" | Should Be ($values[ 0 ][ 1 ])
        "several again"   | Should Be ($values[ 1 ][ 1 ])
        "a couple again"  | Should Be ($values[ 2 ][ 1 ])
        "nothing again"   | Should Be ($values[ 3 ][ 1 ])

        # Make sure we didn't break formatting
        $gv | Out-String
    }


    It "can handle sets" {

        $g = Get-DbgSymbol TestNativeConsoleApp!g_intSet0
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        0 | Should Be ($gv.Count)



        $g = Get-DbgSymbol TestNativeConsoleApp!g_wsSet1
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        1 | Should Be ($gv.Count)

        "zero" | Should Be ($gv[0])



        $g = Get-DbgSymbol TestNativeConsoleApp!g_wsSet2
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        2 | Should Be ($gv.Count)

        # alphabetical order
        "one"  | Should Be  ($gv[0])
        "zero" | Should Be  ($gv[1])



        $g = Get-DbgSymbol TestNativeConsoleApp!g_wsSet3
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        3 | Should Be ($gv.Count)

        # alphabetical order
        "one"  | Should Be ($gv[0])
        "two"  | Should Be ($gv[1])
        "zero" | Should Be ($gv[2])



        $g = Get-DbgSymbol TestNativeConsoleApp!g_wsSet4
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        4 | Should Be ($gv.Count)

        # alphabetical order
        "one"   | Should Be ($gv[0])
        "three" | Should Be ($gv[1])
        "two"   | Should Be ($gv[2])
        "zero"  | Should Be ($gv[3])



        $g = Get-DbgSymbol TestNativeConsoleApp!g_intSet50
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        50 | Should Be ($gv.Count)

        for( $i = 0; $i -lt 50; $i++ )
        {
            ($i) | Should Be ($gv[$i])
        }



        $g = Get-DbgSymbol TestNativeConsoleApp!g_intMultiset10
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        10 | Should Be ($gv.Count)

        for( $i = 0; $i -lt 10; $i++ )
        {
            # N.B. PowerShell seems to round or something (instead of
            # truncating like in C-family languages).
            ([Math]::Floor( $i / 2 )) | Should Be ($gv[ $i ])
        }
    }

    It "can handle lists" {

        $g = Get-DbgSymbol TestNativeConsoleApp!g_intList
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        10 | Should Be ($gv.Count)

        for( $i = 0; $i -lt 10; $i++ )
        {
            ($i) | Should Be ($gv[$i])
        }



        $g = Get-DbgSymbol TestNativeConsoleApp!g_emptyIntList
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        0 | Should Be ($gv.Count)



        $g = Get-DbgSymbol TestNativeConsoleApp!g_intForwardList
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        10 | Should Be ($gv.Count)

        for( $i = 0; $i -lt 10; $i++ )
        {
            # because I add the elements with push_front
            (9 - $i) | Should Be ($gv[$i])
        }



        $g = Get-DbgSymbol TestNativeConsoleApp!g_emptyIntForwardList
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        0 | Should Be ($gv.Count)
    }

    It "can handle hashmaps" {

        $g = Get-DbgSymbol TestNativeConsoleApp!g_hm_03
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        3 | Should Be ($gv.Count)

        $gv['0'].Count | Should Be 1
        $gv['1'].Count | Should Be 1
        $gv['2'].Count | Should Be 1

        $gv['0'][0] | Should Be ""
        $gv['1'][0] | Should Be "z"
        $gv['2'][0] | Should Be "zz"
    }

    It "can handle uniquePtr" {

        $g = Get-DbgSymbol TestNativeConsoleApp!g_uniquePtr
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        9 | Should Be ($gv.Length)
        "abcdefghi" | Should Be $gv
    }

    It "can handle the small string optimization" {

        # Test boundary conditions for "small string optimization".

        'g_wstrings', 'g_strings' | %{
            $g = Get-DbgSymbol "TestNativeConsoleApp!$_"
            $g -ne $null | Should Be $true

            $gv = $g.get_Value()
            $gv.PSObject -ne $null | Should Be $true

            for( [int] $i = 0; $i -lt 22; $i++ )
            {
                (New-Object 'System.String' -Arg @( ([char] 'a'), $i )) | Should Be $gv[ $i ]
            }
        }
    }

    .kill
    PostTestCheckAndResetCacheStats
    popd
}

