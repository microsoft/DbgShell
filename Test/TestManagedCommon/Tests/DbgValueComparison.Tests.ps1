
Describe "DbgValueComparison" {

    pushd
    New-TestApp -TestApp TestNativeConsoleApp -TargetName testApp -HiddenTargetWindow

    It "can detect that execution happened in between inspecting values" {

        bp TestNativeConsoleApp!_Takes_a_ref
        g
        .f+
        $blahVal = dt blah
        $blahVal | Should Be 1

        # Now we're going to let the target run a little, out of the current function:
        gu

        $blahVal2 = dt blah
        $blahVal2 | Should Be 42

        $blahVal.DbgGetExecStatusCookie() -ne $blahVal2.DbgGetExecStatusCookie() | Should Be $true
    }

    It "can inspect const static members" {

        $s = Get-DbgSymbol TestNativeConsoleApp!g_polymorphicThings
        $s.Value[2].s_constStaticInt | Should Be 0xccc
    }

    .kill
    PostTestCheckAndResetCacheStats
    popd
}

