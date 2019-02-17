
Describe "Formatting" {

    # Heterogenous objects: Notice that the second object has a 'PRA3' property instead of
    # 'PRA2' like the first.
    $ps1 = [pscustomobject] @{ PRA1PropName = 'ps1_pra1val' ; PRA2 = 'ps1_pra2val' ; PRB1 = 'ps1_prb1val' }
    $ps2 = [pscustomobject] @{ PRA1PropName = 'ps2_pra1val' ; PRA3 = 'ps2_pra3val' ; PRB1 = 'ps2_prb1val' }

    $FmtCmds = @( 'Format-AltTable', 'Format-AltList' )

    foreach( $cmd in $FmtCmds )
    {
        It "$cmd can handle heterogenous sets of objects" {

            $s = $ps1, $ps2 | & $cmd | Out-String

            $s.Contains( 'PRA2'    ) | Should Be $true
            $s.Contains( 'pra2val' ) | Should Be $true
            $s.Contains( 'PRA3'    ) | Should Be $true
            $s.Contains( 'pra3val' ) | Should Be $true
        }

        It "$cmd can handle wildcarded properties" {

            $s = $ps1, $ps2 | & $cmd pra* | Out-String

            $s.Contains( 'PRA2'    ) | Should Be $true
            $s.Contains( 'pra2val' ) | Should Be $true
            $s.Contains( 'PRA3'    ) | Should Be $true
            $s.Contains( 'pra3val' ) | Should Be $true

            $s.Contains( 'PRB1' ) | Should Be $false
        }

        It "$cmd can handle calculated properties" {

            $s = $ps1, $ps2 | & $cmd @{ Label = 'Calc'; Expression = '$_.PRA1PropName + $_.PRB1' } | Out-String

            $s.Contains( 'ps1_pra1valps1_prb1val' ) | Should Be $true

            # The same, but with a proper ScriptBlock
            $s2 = $ps1, $ps2 | & $cmd @{ Label = 'Calc'; Expression = {$_.PRA1PropName + $_.PRB1} } | Out-String

            $s | Should Be $s2
        }
    } # end foreach( $FmtCmds )

    # Some tests only really apply to (or are testable with) table views.

    $onlyOnePRA1PropNameHeader = {
        $idx = $s.IndexOf( 'PRA1PropName' )
        $idx | Should BeGreaterThan -1

        $idx = $s.IndexOf( 'PRA1PropName', $idx + 1 )
        $idx | Should BeLessThan 0
    }

    It "Format-AltTable doesn't start a new table for every new homogenous object" {

            # Note we are using two instances of the same object: ps1 and ps1 again:
            $s = $ps1, $ps1 | Format-AltTable | Out-String

            . $onlyOnePRA1PropNameHeader
    }

    It "Format-Table proxy, forwarding to Format-AltTable, doesn't start a new table for every new homogenous object, even with -Property" {

            # Make sure the Format-Table proxy actually is forwarding to Format-AltTable
            # in this case.
            Set-AltFormattingEngineOption -UseBuiltinFormattingForPropertyViews:$false

            # Note we are using two instances of the same object: ps1 and ps1 again:
            $s = $ps1, $ps1 | Format-Table PRA1PropName | Out-String

            . $onlyOnePRA1PropNameHeader

            # Now with a wildcard:
            $s = $ps1, $ps1 | Format-Table pra* | Out-String

            . $onlyOnePRA1PropNameHeader
    }

}

