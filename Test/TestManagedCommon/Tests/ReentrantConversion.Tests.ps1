
Describe "ReentrantConversion" {

    Register-DbgValueConverterInfo {
        New-DbgValueConverterInfo -TypeName 'TestNativeConsoleApp!Base' -Converter {
            try
            {
                $symbol = $_
              # $oldVal = Get-Variable 'stockValue*' -ValueOnly
              # if( $null -ne $oldVal )
              # {
              #     Write-Host "In Base converter: old `$stockValue members:"
              #     Write-Host ($stockValue | gm | Out-String)
              # }
              # else
              # {
              #     Write-Host "In Base converter: no old `$stockValue"
              # }

                $stockValue = $_.GetStockValue()

              # Write-Host "In Base converter: new `$stockValue members:"
              # Write-Host ($stockValue | gm | Out-String)

              # Write-Host "Hi! The type is $($stockValue.m_typeTag)"
              # Write-Host ($_ | gm | Out-String)
                switch( $stockValue.m_typeTag )
                {
                    0  { # Type1
                        return (Get-DbgSymbolValue -Address $symbol.Address -TypeName 'TestNativeConsoleApp!Type1')
                    }
                    1  { # Type2
                        return (Get-DbgSymbolValue -Address $symbol.Address -TypeName 'TestNativeConsoleApp!Type2')
                    }
                    2  { # Type3
                        return (Get-DbgSymbolValue -Address $symbol.Address -TypeName 'TestNativeConsoleApp!Type3')
                    }
                    default {
                        throw "Type tag looks wrong; something's broken."
                        return $stockValue
                    }
                } # end switch( m_typeTag )
            }
            finally { }
        } # end 'TestNativeConsoleApp!Base' converter
    }


    Register-AltTypeFormatEntries {

        New-AltTypeFormatEntry -TypeName 'TestNativeConsoleApp!Base' {
            New-AltSingleLineViewDefinition {
              # $oldVal = Get-Variable 'cs*' -ValueOnly
              # if( $null -ne $oldVal )
              # {
              #     Write-Host "In Base custom view definition: old `$cs value: $($oldVal.ToString( $false ))"
              # }
              # else
              # {
              #     Write-Host "In Base custom view definition: no old `$cs"
              # }

                #$cs = New-ColorString -Content $_.context.Name
                $cs = New-ColorString -Content $_.ToString()
                $null = $cs.Append( " " ).AppendPushPopFgBg( [ConsoleColor]::White, [ConsoleColor]::DarkMagenta, $_.m_name )

                $cs
            }.GetNewClosure() # end AltSingleLineViewDefinition
        } # end Type TestNativeConsoleApp!Base
    }

    pushd
    New-TestApp -TestApp TestNativeConsoleApp -Attach -TargetName testApp -HiddenTargetWindow

    It "can handle reentrant symbol value conversion" {

        $g = Get-DbgSymbol TestNativeConsoleApp!g_polymorphicThings
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        'Polymorphic thing 1' | Should Be ($gv[0].m_name)
        'Polymorphic thing 2' | Should Be ($gv[1].m_name)
        'Polymorphic thing 3' | Should Be ($gv[2].m_name)

        'zero' | Should Be ($gv[0].m_map[0])
        'one'  | Should Be ($gv[0].m_map[1])
        'two'  | Should Be ($gv[0].m_map[2])

        'zero' | Should Be ($gv[1].m_vector[0])
        'one'  | Should Be ($gv[1].m_vector[1])
        'two'  | Should Be ($gv[1].m_vector[2])

        # Uh... why did I do this 3 times?
        42 | Should Be ($gv[2].m_i)
        42 | Should Be ($gv[2].m_i)
        42 | Should Be ($gv[2].m_i)

        #
        # Make sure we don't have crazy aliasing trouble when a converter returns
        # one of its children.
        #
        $g = Get-DbgSymbol TestNativeConsoleApp!g_uniquePtr
        $g -ne $null | Should Be $true

        $gv = $g.get_Value()
        $gv.PSObject -ne $null | Should Be $true

        'abcdefghi' | Should Be $gv
    }

    Remove-DbgValueConverterInfo -TypeName 'TestNativeConsoleApp!Base'
    Remove-AltTypeFormatEntry -TypeName 'TestNativeConsoleApp!Base'
    .kill
    PostTestCheckAndResetCacheStats
    popd
}

