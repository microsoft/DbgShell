
Describe "VariantConversion" {

    pushd

    It "can convert VARIANTs" {
        New-TestApp -TestApp TestNativeConsoleApp -Attach -TargetName testApp -HiddenTargetWindow

        # If we don't have symbols, none of the other stuff will work very well...
        #Verify-AreEqual (Get-DbgModuleInfo TestNativeConsoleApp).SymbolType.ToString() 'PDB'

        try
        {
            $g = Get-DbgSymbol TestNativeConsoleApp!g_hasVariants
            $g -ne $null | Should Be $true

            $gv = $g.get_Value()
            $gv.PSObject -ne $null | Should Be $true

            21 | Should Be ($gv.v1)
            21 | Should Be ($gv.v2)
            0x123 | Should Be ($gv.v3.DbgGetPointee())
            0 | Should Be ([string]::Compare( $gv.v4, "This is my variant string.", [StringComparison]::Ordinal ))
            0 | Should Be ([string]::Compare( $gv.v5.GetType().Name, "DbgNoValue", [StringComparison]::Ordinal ))
        }
        finally
        {
            .kill
        }
    }

    PostTestCheckAndResetCacheStats
    popd
}

