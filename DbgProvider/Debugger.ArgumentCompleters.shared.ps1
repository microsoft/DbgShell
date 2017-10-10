# This file is intended to be dot-sourced by functions in Debugger.ArgumentCompleters.ps1.

function New-WildcardPattern( [string] $pattern )
{
    return New-Object 'System.Management.Automation.WildcardPattern' -ArgumentList @( $pattern, ([System.Management.Automation.WildcardOptions]::CultureInvariant -bor [System.Management.Automation.WildcardOptions]::IgnoreCase) )
}

function priv_GlobalSymbolNameCompletion
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0, ValueFromPipeline = $true )]
           [string] $wordToComplete,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.GlobalSymbolCategory] $category = [MS.Dbg.GlobalSymbolCategory]::All
         )

    begin { }
    end { }
    process
    {
        try
        {
            $tokens = $wordToComplete.Split( '!' )
            if( $tokens.Count -eq 1 )
            {
                # Complete the module name:
                Get-DbgModuleInfo -Name "$($tokens[ 0 ])*" | ForEach-Object { $_.Name + '!' } | QuoteAndEscapeSymbolNameIfNeeded
            }
            elseif( $tokens.Count -eq 2 )
            {
                # Complete the symbol name within the module:
                Get-DbgSymbol -Name "$($wordToComplete)*" -Category $category | ForEach-Object { $_.PathString } | QuoteAndEscapeSymbolNameIfNeeded
            }
            else
            {
                # Multiple bangs? I don't know what to do with this.
                return
            }
        }
        finally { }
    } # end 'process' block
} # end priv_GlobalSymbolNameCompletion

