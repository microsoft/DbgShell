

if (!(Get-Command -ea Ignore Register-ArgumentCompleter))
{
    return
}

#############################################################################
#
# Helper function to create a new completion results
#
# We can either make it a global function, or use GetNewClosure on the completer
# functions. I'll make it global for now, since I think it is a little more performant
# under a debugger (currently GetNewClosure results in exceptions internally).
#
function global:New-CompletionResult
{
    param( [Parameter( Position = 0,
                       ValueFromPipelineByPropertyName,
                       Mandatory,
                       ValueFromPipeline )]
           [ValidateNotNullOrEmpty()]
           [string]
           $CompletionText,

           [Parameter( Position = 1, ValueFromPipelineByPropertyName )]
           [string]
           $ToolTip,

           [Parameter( Position = 2, ValueFromPipelineByPropertyName )]
           [string]
           $ListItemText,


           [System.Management.Automation.CompletionResultType]
           $CompletionResultType = [System.Management.Automation.CompletionResultType]::ParameterValue,

           [Parameter(Mandatory = $false)]
           [switch] $NoQuotes = $false
         )

    process
    {
        $toolTipToUse = if ($ToolTip -eq '') { $CompletionText } else { $ToolTip }
        $listItemToUse = if ($ListItemText -eq '') { $CompletionText } else { $ListItemText }


        # If the caller explicitly requests that quotes
        # not be included, via the -NoQuotes parameter,
        # then skip adding quotes.


        if ($CompletionResultType -eq [System.Management.Automation.CompletionResultType]::ParameterValue -and -not $NoQuotes)
        {
            # Add single quotes for the caller in case they are needed.
            # We use the parser to robustly determine how it will treat
            # the argument.  If we end up with too many tokens, or if
            # the parser found something expandable in the results, we
            # know quotes are needed.


            $tokens = $null
            $null = [System.Management.Automation.Language.Parser]::ParseInput("echo $CompletionText", [ref]$tokens, [ref]$null)
            if ($tokens.Length -ne 3 -or
                ($tokens[1] -is [System.Management.Automation.Language.StringExpandableToken] -and
                 $tokens[1].Kind -eq [System.Management.Automation.Language.TokenKind]::Generic))
            {
                $CompletionText = "'$CompletionText'"
            }
        }
        return New-Object System.Management.Automation.CompletionResult `
            ($CompletionText,$listItemToUse,$CompletionResultType,$toolTipToUse.Trim())
    }
} # end New-CompletionResult


#
# .SYNOPSIS
#
#    Complete symbol names in the -Expression parameter to breakpoint cmdlets, or for the
#    -Name parameter to Get-DbgSymbol.
#
function GlobalSymbolNameCompletion
{
    param( $commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameter )

    . "$PSScriptRoot\Debugger.ArgumentCompleters.shared.ps1"

    #$Host.EnterNestedPrompt()
    # TODO: Would be nice to be able to use EnterNestedPrompt when debugging, but I can't
    # (the following shows up in $error):
    #
    #    Exception calling "EnterNestedPrompt" with "0" argument(s): "No pipeline is running."
    #    At C:\Projects\DbgShell\bin\Debug\x86\Debugger\Debugger.ArgumentCompleters.ps1:23 char:5
    #    +     $Host.EnterNestedPrompt()
    #    +     ~~~~~~~~~~~~~~~~~~~~~~~~~
    #        + CategoryInfo          : NotSpecified: (:) [], MethodInvocationException
    #        + FullyQualifiedErrorId : InvalidOperationException
    #
    # (A cheap trick workaround is to assign things to global variables for later inspection.)
    # $global:AstThing = $commandAst
    # $global:fbpThing = $fakeBoundParameter
    # $global:pnThing = $parameterName
    # $global:cnThing = $commandName

    [MS.Dbg.GlobalSymbolCategory] $category = [MS.Dbg.GlobalSymbolCategory]::All
    if( ($commandName -eq 'u') -or
        ($commandName -eq 'uf') -or
        ($commandName -eq 'ub') -or
        ($commandName -eq 'Read-DbgDisassembly') )
    {
        $category = 'Function,NoType'
    }
    elseif( $fakeBoundParameter[ 'Category' ] )
    {
        $category = [MS.Dbg.GlobalSymbolCategory] $fakeBoundParameter[ 'Category' ]
    }

    priv_GlobalSymbolNameCompletion $wordToComplete -category $category | New-CompletionResult
} # end GlobalSymbolNameCompletion


#
# .SYNOPSIS
#
#    Complete symbol names in the -Name argument to symbol-related cmdlets.
#
function LocalSymbolNameCompletion
{
    param( $commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameter )

    # TODO: What about options like -Scope?
    Get-DbgLocalSymbol -Name "$($wordToComplete)*" | ForEach-Object { New-CompletionResult ($_.Name | QuoteAndEscapeSymbolNameIfNeeded) }
} # end LocalSymbolNameCompletion


#
# .SYNOPSIS
#
#    Complete symbol names for Get-DbgSymbolValue.
#
function GlobalAndLocalSymbolNameCompletion
{
    param( $commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameter )

    . "$PSScriptRoot\Debugger.ArgumentCompleters.shared.ps1"

    if( !$wordToComplete -or !$wordToComplete.Contains( '.' ) )
    {
        if( $wordToComplete.IndexOf( [char] '!' ) -lt 0 )
        {
            Get-DbgLocalSymbol -Name "$($wordToComplete)*" | ForEach-Object { New-CompletionResult ($_.Name | QuoteAndEscapeSymbolNameIfNeeded) }
        }

        [MS.Dbg.GlobalSymbolCategory] $category = [MS.Dbg.GlobalSymbolCategory]::All
        if( $fakeBoundParameter[ 'Category' ] )
        {
            $category = [MS.Dbg.GlobalSymbolCategory] $fakeBoundParameter[ 'Category' ]
        }

        # Also cycle through 'module!' in case they are looking for a global.
        priv_GlobalSymbolNameCompletion $wordToComplete -category $category | New-CompletionResult
    }
    else
    {
        if( $commandName -eq 'x' )
        {
            # "x" does not support delving into fields.
            return
        }

        $tokens = $wordToComplete.Split( '.' )
        # TODO: I think I'm going to want to try to cache this stuff for perf reasons
        if( $wordToComplete.IndexOf( [char] '!' ) -lt 0 )
        {
            $val = Get-DbgLocalSymbol -Name $tokens[ 0 ]
        }
        else
        {
            $val = Get-DbgSymbol -Name $tokens[ 0 ]
        }
        if( $null -eq $val ) { return }
        $val = $val.Value
        if( $null -eq $val ) { return }

        [int] $i = 0;
        [string] $prefix = $tokens[ 0 ]
        for( $i = 1; $i -lt ($tokens.Count - 1); $i++ )
        {
            $prefix += ".$($tokens[ $i ])"
            $val = $val.PSObject.Properties[ $tokens[ $i ] ].Value
        }
        $matchToken = $tokens[ $i ]
        $prefix += "."

        if( !$matchToken )
        {
            $matchToken = '*'
        }
        else
        {
            $matchToken += '*'
        }
        $pat = New-WildcardPattern $matchToken
        $val.PSObject.Properties | ForEach-Object {
            if( $pat.IsMatch( $_.Name ) )
            {
                New-CompletionResult ($prefix + ($_.Name) | QuoteAndEscapeSymbolNameIfNeeded)
            }
        }
    }
} # end GlobalAndLocalSymbolNameCompletion


#
# .SYNOPSIS
#
#    Complete local symbol, global symbol and type names for 'dt'.
#
function GlobalAndLocalSymbolAndTypeNameCompletion
{
    param( $commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameter )

    . "$PSScriptRoot\Debugger.ArgumentCompleters.ps1" # This is awkward.

    if( !$wordToComplete -or !$wordToComplete.Contains( '.' ) )
    {
        GlobalAndLocalSymbolNameCompletion $commandName $parameterName $wordToComplete $commandAst $fakeBoundParameter
        TypeNameCompletion $commandName $parameterName $wordToComplete $commandAst $fakeBoundParameter
    }
    else
    {
        GlobalAndLocalSymbolNameCompletion $commandName $parameterName $wordToComplete $commandAst $fakeBoundParameter
    }
} # end GlobalAndLocalSymbolAndTypeNameCompletion


#
# .SYNOPSIS
#
#    Complete module names.
#
function ModuleNameCompletion
{
    param( $commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameter )

    # Complete the module name:
    Get-DbgModuleInfo -Name "$($wordToComplete)*" | ForEach-Object { New-CompletionResult ($_.Name) }
} # end ModuleNameCompletion


#
# .SYNOPSIS
#
#    Complete module names.
#
function FullModuleNameCompletion
{
    param( $commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameter )

    # TODO: Minor bug: if you type "foo.dl" and then [TAB] expecting it to complete to
    # "foo.dll", it won't work, since Get-DbgModuleInfo won't find a module with that
    # prefix.
    Get-DbgModuleInfo -Name "$($wordToComplete)*" | ForEach-Object { New-CompletionResult ([System.IO.Path]::GetFileName( $_.ImageName )) }
} # end FullModuleNameCompletion




#
# .SYNOPSIS
#
#    Complete type names.
#
function TypeNameCompletion
{
    param( $commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameter )

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
            # Complete the type name within the module:
            Get-DbgTypeInfo -NoFollowTypedefs -Name "$($wordToComplete)*" | ForEach-Object { $tokens[ 0 ] + '!' + $_.Name } | QuoteAndEscapeSymbolNameIfNeeded
        }
        else
        {
            # Multiple bangs? I don't know what to do with this.
            return
        }
    }
    finally { }
}


Register-ArgumentCompleter `
    -Command ('Set-DbgBreakpoint') `
    -Parameter 'Expression' `
    -ScriptBlock $function:GlobalSymbolNameCompletion


Register-ArgumentCompleter `
    -Command ('Get-DbgSymbol') `
    -Parameter 'Name' `
    -ScriptBlock $function:GlobalSymbolNameCompletion


Register-ArgumentCompleter `
    -Command ('Get-DbgNearSymbol', 'u', 'uf', 'ub', 'Read-DbgDisassembly') `
    -Parameter 'Address' `
    -ScriptBlock $function:GlobalSymbolNameCompletion


Register-ArgumentCompleter `
    -Command ('Get-DbgLocalSymbol') `
    -Parameter 'Name' `
    -ScriptBlock $function:LocalSymbolNameCompletion


Register-ArgumentCompleter `
    -Command ('Get-DbgSymbolValue', 'x') `
    -Parameter 'Name' `
    -ScriptBlock $function:GlobalAndLocalSymbolNameCompletion


Register-ArgumentCompleter `
    -Command ('dt') `
    -Parameter 'TypeNameOrAddress' `
    -ScriptBlock $function:GlobalAndLocalSymbolAndTypeNameCompletion


Register-ArgumentCompleter `
    -Command ('Get-DbgModuleInfo') `
    -Parameter 'Name' `
    -ScriptBlock $function:ModuleNameCompletion


Register-ArgumentCompleter `
    -Command ('Get-DbgTypeInfo') `
    -Parameter 'Module' `
    -ScriptBlock $function:ModuleNameCompletion


Register-ArgumentCompleter `
    -Command ('Initialize-DbgSymbols') `
    -Parameter 'Module' `
    -ScriptBlock $function:FullModuleNameCompletion


Register-ArgumentCompleter `
    -Command ('Get-DbgTypeInfo') `
    -Parameter 'Name' `
    -ScriptBlock $function:TypeNameCompletion


Register-ArgumentCompleter `
    -Command ('Get-DbgSymbolValue') `
    -Parameter 'Type' `
    -ScriptBlock $function:TypeNameCompletion
