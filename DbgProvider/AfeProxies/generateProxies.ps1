<#
.SYNOPSIS
    This script generates PS1 script code for the proxy functions in Debugger.Formatting.psm1.

.DESCRIPTION
    The proxy functions for the various Formatting and Output (F+O) commands look very similar. They are also fairly complex, and require adjustment and refinement from time to time as the Alternate Formatting Engine gets closer to parity with the built-in F+O. The purpose of this script is to make these proxy functions easier to maintain and improve upon without quite so much worry about copy/paste errors.

    To use it, run this script, then copy the contents of .\ToPasteIn.ps1 into the appropriate spot at the end of Debugger.Formatting.psm1.

    There are two "source" files: FormatProxy.ps1.in and OutProxy.ps1.in, which provide the basis for {Format-Table, Format-List, Format-Custom} and {Out-Default, Out-String}, respectively.
#>
[CmdletBinding()]
param()

function DoReplacements( [string] $inputFile, [string] $outputFile, [hashtable] $baseInfo )
{
    try
    {
        Write-Host "Creating $($outputFile)..." -Fore Cyan
        if( Test-Path $outputFile )
        {
            del $outputFile
        }

        $repl = @{ }
        foreach( $key in $baseInfo.Keys )
        {
            #Write-Host "Adding $key $($baseInfo[ $key ]  )" -Fore Yellow
            $repl[ $key ] = $baseInfo[ $key ]
        }

        #$repl[ 'PROXY_NAME' ] = "Format-$($baseInfo[ 'PROXY_SHAPE' ])"
        $repl[ 'PROXY_NAME' ] = "$($baseInfo[ 'PROXY_PREFIX' ])-$($baseInfo[ 'PROXY_SHAPE' ])"
        $repl[ 'PROXY_SPEW_VAR' ] = "Format$($baseInfo[ 'PROXY_SHAPE' ])ProxyDebugSpew"
        $repl[ 'PROXY_VIEW_TYPE_NAME' ] = "Alt$($baseInfo[ 'PROXY_SHAPE' ])ViewDefinition"
        $repl[ 'CURRENT_VIEW_DEF' ] = "`$private:current$($baseInfo[ 'PROXY_SHAPE' ])ViewDef"
        $repl[ 'FORMAT_WRAPPED_COMMAND' ] = "`$private:format$($baseInfo[ 'PROXY_SHAPE' ])WrappedCmd"
        $repl[ 'FORMAT_SCRIPT_COMMAND' ] = "`$private:format$($baseInfo[ 'PROXY_SHAPE' ])ScriptCmd"
        $repl[ 'OUT_WRAPPED_COMMAND' ] = "`$private:out$($baseInfo[ 'PROXY_SHAPE' ])WrappedCmd"
        $repl[ 'OUT_SCRIPT_COMMAND' ] = "`$private:out$($baseInfo[ 'PROXY_SHAPE' ])ScriptCmd"

        gc $inputFile | %{
            $line = $_

            if( ('Out-String' -eq $repl[ 'PROXY_NAME' ]) -and
                ($line.Trim().StartsWith( 'OUT_WRAPPED_COMMAND' )) )
            {
                $line = '            OUT_WRAPPED_COMMAND = Get-Command ''OutStringColorShim'''
            }
            foreach( $key in $repl.Keys )
            {
                #    Write-Host "     Before replacing $($key): $line" -Fore Magenta
                $line = $line.Replace( $key, $repl[ $key ] )
                #Write-Host "      After replacing $($key): $line" -Fore Magenta
            }
            $line
        } >> $outputFile
    }
    finally { }
} # end DoReplacements()

$oldEap = $global:ErrorActionPreference
try
{
    $global:ErrorActionPreference = 'Stop'

    #
    # Format-* proxies
    #
    $proxyBaseInfos = @{
        'Custom' = @{ 'SPEW_PREFIX' = 'FcProxy'; 'HELP_URI' = 'http://go.microsoft.com/fwlink/?LinkID=113301' }
        'List'   = @{ 'SPEW_PREFIX' = 'FlProxy'; 'HELP_URI' = 'http://go.microsoft.com/fwlink/?LinkID=113302' }
        'Table'  = @{ 'SPEW_PREFIX' = 'FtProxy'; 'HELP_URI' = 'http://go.microsoft.com/fwlink/?LinkID=113303' }
    }

    foreach( $key in $proxyBaseInfos.Keys )
    {
        $proxyBaseInfos[ $key ][ 'NON_SHARED_PARAM_1' ] = ''
        $proxyBaseInfos[ $key ][ 'NON_SHARED_PARAM_2' ] = ''
    }

    $proxyBaseInfos[ 'Table' ][ 'NON_SHARED_PARAM_1' ] = @'

           [switch] ${AutoSize},

           [switch] ${HideTableHeaders},

           [switch] ${Wrap},

'@

    $proxyBaseInfos[ 'Custom' ][ 'NON_SHARED_PARAM_2' ] = @'
[ValidateRange( 1, 2147483647 )]
           [int] ${Depth},

'@

    if( !(Test-Path "$PSScriptRoot\tmp") )
    {
        $null = mkdir "$PSScriptRoot\tmp"
    }

    foreach( $key in $proxyBaseInfos.Keys )
    {
        $baseInfo = $proxyBaseInfos[ $key ]
        $baseInfo[ 'PROXY_SHAPE' ] = $key
        $baseInfo[ 'PROXY_PREFIX' ] = 'Format'

        DoReplacements "$PSScriptRoot\FormatProxy.ps1.in" "$PSScriptRoot\tmp\Format$($key)Proxy.ps1" $baseInfo
    }


    #
    # Out-* proxies
    #
    $proxyBaseInfos = @{
        'Default' = @{ 'SPEW_PREFIX' = 'OdProxy'; 'HELP_URI' = 'http://go.microsoft.com/fwlink/?LinkID=113362' }
        'String'  = @{ 'SPEW_PREFIX' = 'OsProxy'; 'HELP_URI' = 'http://go.microsoft.com/fwlink/?LinkID=113368' }
    }

    foreach( $key in $proxyBaseInfos.Keys )
    {
        $proxyBaseInfos[ $key ][ 'OUTSTRING_ONLY_PARAMS' ] = ''
        $proxyBaseInfos[ $key ][ 'OUTSTRING_ONLY_PARAMS_VAR' ] = ''
        $proxyBaseInfos[ $key ][ 'OUTSTRING_SPECIFIC_1' ] = ''
    }

    $proxyBaseInfos[ 'String' ][ 'OUTSTRING_ONLY_PARAMS' ] = @'
 [switch] ${Stream},

       [ValidateRange( 2, 2147483647 )]
       [int] ${Width},

'@

    $proxyBaseInfos[ 'String' ][ 'OUTSTRING_ONLY_PARAMS_VAR' ] = '@OutStringOnlyParams'

    $proxyBaseInfos[ 'String' ][ 'OUTSTRING_SPECIFIC_1' ] = @'
$private:OutStringOnlyParams = @{ }
            if( $PSBoundParameters.Remove( 'Width' ) )
            {
                $OutStringOnlyParams[ 'Width' ] = $Width
            }
            if( $PSBoundParameters.Remove( 'Stream' ) )
            {
                $OutStringOnlyParams[ 'Stream' ] = $Stream
            }
            # TODO: I don't think I handle -Stream properly when the alt formatting engine is in play.
            # TODO: Also, width. And can I game it for ColorStrings?
'@


    foreach( $key in $proxyBaseInfos.Keys )
    {
        $baseInfo = $proxyBaseInfos[ $key ]
        $baseInfo[ 'PROXY_SHAPE' ] = $key
        $baseInfo[ 'PROXY_PREFIX' ] = 'Out'

        DoReplacements "$PSScriptRoot\OutProxy.ps1.in" "$PSScriptRoot\tmp\Out$($key)Proxy.ps1" $baseInfo
    }


    Write-Host "Concatenating..." -Fore Magenta
    $outputFile = "$PSScriptRoot\tmp\ToPasteIn.ps1"
    if( Test-Path $outputFile )
    {
        del $outputFile
    }

    $inputFiles = @( "$PSScriptRoot\tmp\OutDefaultProxy.ps1",
                     "$PSScriptRoot\tmp\OutStringProxy.ps1",
                     "$PSScriptRoot\tmp\FormatTableProxy.ps1",
                     "$PSScriptRoot\tmp\FormatListProxy.ps1",
                     "$PSScriptRoot\tmp\FormatCustomProxy.ps1" )

    $inputFiles | %{ Get-Content $_ } >> $outputFile

    Write-Host ''
    Write-Host 'Finished.' -Fore Green
    Write-Host ''
    Write-Host "Now you can copy the contents of $outputFile into Debugger.Formatting.psm1." -Fore Green
    Write-Host ''
}
finally
{
    $global:ErrorActionPreference = $oldEap
}

