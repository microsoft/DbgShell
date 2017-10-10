<#
.SYNOPSIS
    NOT CURRENTLY USED. But I'll leave this script around for a while in case
    we want to be able to pull down nuget packages independently from .csproj's
    built-in support.

    Called during build to copy debugger binaries into the bin directory.

.DESCRIPTION
    To call this from a .csproj post-build event, use something like:

        powershell.exe -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -Command ". '$(SolutionDir)CopyDebuggerBinaries.ps1' '$(PlatformName)' '$(ConfigurationName)'; exit $LastExitCode"
#>
[CmdletBinding()]
param( [Parameter( Mandatory = $true, Position = 0)]
       [ValidateSet( 'x86', 'x64' )]
       [string] $BuildArch, # AKA Platform

       [Parameter( Mandatory = $true, Position = 1)]
       [ValidateSet( 'Debug', 'Release' )]
       [string] $BuildFlavor # AKA Configuration
     )

begin
{
    $dbgBinsVersion = '10.0.16232.1000'
    #$packagesRoot = Join-Path $PSScriptRoot 'packages'
    $packagesRoot = "${env:LOCALAPPDATA}\PackageManagement\NuGet\Packages"
    $packageName = 'Unofficial.Microsoft.DebuggerBinaries'

    function GetDbgBinsSrcPath()
    {
        $downloadedPackageRoot = Join-Path $packagesRoot ($packageName + '.' + $dbgBinsVersion)

        if( !(Test-Path $downloadedPackageRoot) )
        {
            Write-Host "Package not found; will attempt to download it: $packageName"

            # TODO: If I can't figure out how to make Install-Package work, I could at least
            # download nuget.exe dynamically.
            #
          # if( !(Get-Command nuget -ErrorAction Ignore) )
          # {
          #     throw "Nuget.exe is required."
          # }
          #
          # $nugetOutput = nuget install -NonInteractive $packageName -Version $dbgBinsVersion -OutputDirectory $packagesRoot
          #
          # foreach( $line in $nugetOutput.Split( @("`r`n", "`n" ), [System.StringSplitOptions]::None ) )
          # {
          #     Write-Host $line
          # }
            #
            # Or... this seems to work... TODO: file a bug somewhere?
            #
            Get-PackageSource -ProviderName NuGet | Unregister-PackageSource
            $null = Register-PackageSource -Name Nuget -ProviderName NuGet -Location https://www.nuget.org/api/v2

            $nugetOutput = Install-Package $packageName -RequiredVersion $dbgBinsVersion `
                                                        -ProviderName NuGet `
                                                        -Scope CurrentUser `
                                                        -Force `
                                                        -Verbose *>&1

            foreach( $line in ($nugetOutput | out-string).Split( @("`r`n", "`n" ), [System.StringSplitOptions]::None ) )
            {
                Write-Host $line
            }

            #if( !$? -or !(Test-Path $downloadedPackageRoot) )
            if( !(Test-Path $downloadedPackageRoot) )
            {
                throw "Failed to acquire package: $packageName"
            }
        }

        return (Join-Path $downloadedPackageRoot "build\native\bin\$BuildArch")
    } # end GetDbgBinsSrcPath()
} # end 'begin' block

process { }

end
{
    try
    {
        # Internally, we may want to try out some private dbgeng binaries.
        $msInternalScriptPath = Join-Path (Split-Path $PSScriptRoot) "DbgShellMsInternal\CopyDebuggerBinaries.ps1"
        if( (Test-Path $msInternalScriptPath) )
        {
            return . $msInternalScriptPath @PSBoundParameters
        }
    }
    finally { }

    [bool] $succeeded = $false

    try
    {
        Set-StrictMode -Version Latest

        Write-Host 'Copying debugger binaries...'

        $dbgBinsSrcPath = GetDbgBinsSrcPath

        $destPath = Join-Path $PSScriptRoot "bin\$BuildFlavor\$BuildArch\Debugger"

        if( !(Test-Path $destPath) )
        {
            throw "Unexpected... `$destPath should exist: $destPath"
        }

        Write-Host "calling xcopy `"$dbgBinsSrcPath\*`" `"$destPath`" /D /Y"

        xcopy "$dbgBinsSrcPath\*" "$destPath" /D /Y

        if( !$? )
        {
            throw "xcopy failed?"
        }

        Write-Host 'Finished copying debugger binaries.'

        $succeeded = $true
    }
    finally
    {
        if( !$succeeded )
        {
            # If we didn't succeed, VS will notice that this build event failed, but if
            # you then try to build the project again, VS will see that DbgShell.exe
            # already exists and is up-to-date, and will not re-run this script. So to
            # ensure that building again will re-attempt running this script (if it had
            # previously failed), we'll wipe out DbgShell.exe.
            Remove-Item (Join-Path $PSScriptRoot "bin\$BuildFlavor\$BuildArch\DbgShell.exe") -ErrorAction Ignore
        }
    }
}

