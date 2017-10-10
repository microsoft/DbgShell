
Describe "Disasm" {

    pushd

    It "can disassemble" {

        New-TestApp -TestApp TestManagedConsoleApp -Attach -TargetName testApp -HiddenTargetWindow

        try
        {
            Set-DbgSymbolPath '' 3>&1 | Out-Null # ignore "empty symbol path" warning

            # Note that we redirect both error and warning streams to the output stream.
            $disasm = uf ((lm mscorlib_ni).BaseAddress) 2>&1 3>&1

            $null -ne $disasm | Should Be $true

            # We should have gotten:
            #   * MAYBE: a warning about not being able to verify a checksum,
            #   * an error about not having symbols,
            #   * MAYBE: a warning about flow analysis being incomplete,
            #   * followed by disassembly

            [int] $idx = 0
            if( $disasm[0] -is 'System.Management.Automation.WarningRecord' )
            {
                $idx += 1
            }

            ($disasm[$idx++] -is 'System.Management.Automation.ErrorRecord')   | Should Be $true

            if( $disasm[$idx] -is 'System.Management.Automation.WarningRecord' )
            {
                $idx += 1
            }

            ($disasm[$idx++] -is 'MS.Dbg.DbgDisassembly')                      | Should Be $true

          # # That error should also be in $error.
          # Verify-AreEqual 1 ($global:error.Count)

          # $global:error.Clear()
        }
        finally
        {
            .kill
        }
    }

    popd
}

