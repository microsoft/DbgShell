
Describe "TrickySymbolValueConversions" {

    Register-DbgValueConverterInfo {

        # First we're going to define converters for a set of nested types--each converter
        # just returns the single member of the next type in the nesting.

        New-DbgValueConverterInfo -TypeName 'TestNativeConsoleApp!NestingThing4' -Converter {
            try
            {
                $symbol = $_

                $stockValue = $_.GetStockValue()

                return $stockValue.m_n3
            }
            finally { }
        } # end 'TestNativeConsoleApp!NestingThing4' converter

        New-DbgValueConverterInfo -TypeName 'TestNativeConsoleApp!NestingThing3' -Converter {
            try
            {
                $symbol = $_

                $stockValue = $_.GetStockValue()

                return $stockValue.m_n2
            }
            finally { }
        } # end 'TestNativeConsoleApp!NestingThing3' converter

        New-DbgValueConverterInfo -TypeName 'TestNativeConsoleApp!NestingThing2' -Converter {
            try
            {
                $symbol = $_

                $stockValue = $_.GetStockValue()

                return $stockValue.m_n1
            }
            finally { }
        } # end 'TestNativeConsoleApp!NestingThing2' converter

        New-DbgValueConverterInfo -TypeName 'TestNativeConsoleApp!NestingThing1' -Converter {
            try
            {
                $symbol = $_

                $stockValue = $_.GetStockValue()

                return $stockValue.m_blah
            }
            finally { }
        } # end 'TestNativeConsoleApp!NestingThing1' converter


        # Now we'll do something similar, but for a set of types where each nesting type
        # also involves inheritance (so we intermix Derived Type Detection with the Symbol
        # Value Conversion).

        New-DbgValueConverterInfo -TypeName 'TestNativeConsoleApp!DtdNestingThing4Base' -Converter {
            try
            {
                $symbol = $_

                $stockValue = $_.GetStockValue()

                # We follow pointers, else the pointerness sort of breaks up the symbol
                # history.

                return $stockValue.m_p3.DbgFollowPointers()
            }
            finally { }
        } # end 'TestNativeConsoleApp!DtdNestingThing4Base' converter

        New-DbgValueConverterInfo -TypeName 'TestNativeConsoleApp!DtdNestingThing3Base' -Converter {
            try
            {
                $symbol = $_

                $stockValue = $_.GetStockValue()

                return $stockValue.m_p2.DbgFollowPointers()
            }
            finally { }
        } # end 'TestNativeConsoleApp!DtdNestingThing3Base' converter

        New-DbgValueConverterInfo -TypeName 'TestNativeConsoleApp!DtdNestingThing2Base' -Converter {
            try
            {
                $symbol = $_

                $stockValue = $_.GetStockValue()

                return $stockValue.m_p1.DbgFollowPointers()
            }
            finally { }
        } # end 'TestNativeConsoleApp!DtdNestingThing2Base' converter

        New-DbgValueConverterInfo -TypeName 'TestNativeConsoleApp!DtdNestingThing1Base' -Converter {
            try
            {
                $symbol = $_

                $stockValue = $_.GetStockValue()

                return $stockValue.m_myInt
            }
            finally { }
        } # end 'TestNativeConsoleApp!DtdNestingThing1Base' converter
    }


    pushd

    New-TestApp -TestApp TestNativeConsoleApp -Attach -TargetName testApp -HiddenTargetWindow


    # If we don't have symbols, none of the other stuff will work very well...
    #Verify-AreEqual (Get-DbgModuleInfo TestNativeConsoleApp).SymbolType.ToString() 'PDB'

    try
    {
        It "can do nested Symbol Value Conversions" {

            $g = Get-DbgSymbol TestNativeConsoleApp!g_n4
            $g -ne $null | Should Be $true

            $gv = $g.get_Value()
            $gv.PSObject -ne $null | Should Be $true

            $gv -eq 0x42 | Should Be $true

            $history = $gv.DbgGetSymbolHistory()
            $history.Count | Should Be 5
            $history[ 1..4 ] | %{ ($_ -is [MS.Dbg.SvcRecord]) | Should Be $true }

            $history[ 0 ].OriginalSymbol.Name | Should Be 'm_blah'
            $history[ 1 ].OriginalSymbol.Name | Should Be 'm_n1'
            $history[ 2 ].OriginalSymbol.Name | Should Be 'm_n2'
            $history[ 3 ].OriginalSymbol.Name | Should Be 'm_n3'
            $history[ 4 ].OriginalSymbol.Name | Should Be 'g_n4'

            $history[ 0 ].OriginalSymbol.Type.Name | Should Be 'UInt4B'
            $history[ 1 ].OriginalSymbol.Type.Name | Should Be 'NestingThing1'
            $history[ 2 ].OriginalSymbol.Type.Name | Should Be 'NestingThing2'
            $history[ 3 ].OriginalSymbol.Type.Name | Should Be 'NestingThing3'
            $history[ 4 ].OriginalSymbol.Type.Name | Should Be 'NestingThing4'
        }


        It "can do nested Symbol Value Conversions mixed with Derived Type Detection" {

            $g = Get-DbgSymbol TestNativeConsoleApp!g_pDtdNestingThing4 
            $g -ne $null | Should Be $true

            $gv = $g.get_Value()
            $gv.PSObject -ne $null | Should Be $true

            $followed = $gv.DbgFollowPointers()
            $followed | Should Be 0x99

            $history = $followed.DbgGetSymbolHistory()

            $history.Count | Should Be 9

            $history[ 0 ].OriginalSymbol.Name | Should Be 'm_myInt'
            $history[ 1 ].OriginalSymbol.Name | Should Be '(*m_p1)'
            $history[ 2 ].OriginalSymbol.Name | Should Be '(*m_p1)'
            $history[ 3 ].OriginalSymbol.Name | Should Be '(*m_p2)'
            $history[ 4 ].OriginalSymbol.Name | Should Be '(*m_p2)'
            $history[ 5 ].OriginalSymbol.Name | Should Be '(*m_p3)'
            $history[ 6 ].OriginalSymbol.Name | Should Be '(*m_p3)'
            $history[ 7 ].OriginalSymbol.Name | Should Be '(*g_pDtdNestingThing4)'
            $history[ 8 ].OriginalSymbol.Name | Should Be '(*g_pDtdNestingThing4)'

            $history[ 0 ].OriginalSymbol.Type.Name | Should Be 'UInt4B'
            $history[ 1 ].OriginalSymbol.Type.Name | Should Be 'DtdNestingThing1Derived'
            $history[ 2 ].OriginalSymbol.Type.Name | Should Be 'DtdNestingThing1Base'
            $history[ 3 ].OriginalSymbol.Type.Name | Should Be 'DtdNestingThing2Derived'
            $history[ 4 ].OriginalSymbol.Type.Name | Should Be 'DtdNestingThing2Base'
            $history[ 5 ].OriginalSymbol.Type.Name | Should Be 'DtdNestingThing3Derived'
            $history[ 6 ].OriginalSymbol.Type.Name | Should Be 'DtdNestingThing3Base'
            $history[ 7 ].OriginalSymbol.Type.Name | Should Be 'DtdNestingThing4Derived'
            $history[ 8 ].OriginalSymbol.Type.Name | Should Be 'DtdNestingThing4Base'
        }
    }
    finally
    {
        .kill
    }

    PostTestCheckAndResetCacheStats
    popd
}

