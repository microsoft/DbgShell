Set-StrictMode -Version Latest

[void] (New-PSDrive Tests FileSystem "$PSScriptRoot\Tests" -Scope Global)

$script:dumpsRelativePath = '..\..\..\..\Dumps'
if( [System.IO.Directory]::Exists( "$PSScriptRoot\$dumpsRelativePath" ) )
{
    [void] (New-PSDrive Dumps FileSystem "$PSScriptRoot\$dumpsRelativePath" -Scope Global)
}

$MyInvocation.MyCommand.ScriptBlock.Module.OnRemove =
{
    Remove-PSDrive -Name 'Tests' -Force
    $dd = Get-PSDrive 'Dumps*' # use a wildcard because I don't want an error if it's not there.
    foreach( $d in $dd )
    {
        if( $d.Name -eq 'Dumps' )
        {
            $d | Remove-PSDrive -Force
            break
        }
    }
} # end OnRemove handler

Write-Host ''
Write-Host "Tests are located under the Tests:\ drive. Type something like ""Invoke-Pester Tests:\*.ps1"" to run them. Or use DoFullTestPass to run them for all combinations of the optional test inputs (path bugginess style and provider-/drive-qualified working directory)."
Write-Host ''


Set-Alias Run-Test Invoke-Pester


function script:Find-TestApp
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNullOrEmpty()]
           [string] $TestApp,

           [Parameter( Mandatory = $false )]
           [ValidateSet( 'x86', 'x64', 'matching', 'cross' )]
           [string] $Platform = 'matching'
         )

    begin { }
    end { }
    process
    {
        try
        {
            [string] $path = $null
            if( $Platform -eq 'matching' )
            {
                $path = 'bin:\DbgShellTest\' + $TestApp + '.exe'
                $path = $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath( $path )
            }
            else
            {
                if( $Platform -eq 'cross' ) {
                    if( [System.Environment]::Is64BitProcess ) {
                        $Platform = 'x86'
                    } else {
                        $Platform = 'x64'
                    }
                }

                $platformRoot = [System.IO.Path]::GetDirectoryName( (Get-PSDrive 'bin').Root )
                $path = [System.IO.Path]::Combine( $platformRoot, $Platform, 'DbgShellTest', $TestApp )
                $path = $path + '.exe'
            }

            if( ![System.IO.File]::Exists( $path ) )
            {
                throw "There is no '$TestApp' test app. (at least not yet...) Path tried was: $path"
            }

            return $path
        }
        finally { }
    } # end process
} # end function Find-TestApp


function script:Find-Windbg
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNullOrEmpty()]
           [string] $which
         )

    begin { }
    end { }
    process
    {
        try
        {
            [string] $dir = Find-WindbgDir
            if( !$dir )
            {
                throw "Can't find windbg dir."
            }

            [string] $path = Join-Path $dir $which
            if( ![System.IO.File]::Exists( $path ) )
            {
                throw "Can't find '$path'. You may need to modify this function in DbgShellTest.psm1 (or make sure that windbg is installed in a common location)."
            }
            return $path
        }
        finally { }
    } # end process
} # end function Find-Windbg


<#
.Synopsis
    Starts a new process for debugging, either launching under the debugger, or launching it then attaching.
#>
function New-TestApp
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNullOrEmpty()]
           [ValidateSet( 'TestManagedConsoleApp', 'TestNativeConsoleApp' )] # TODO: implement other test apps!
           [string] $TestApp,

           [Parameter( Mandatory = $false )]
           [ValidateSet( 'x86', 'x64', 'matching', 'cross' )]
           [string] $Platform = 'matching',

           [Parameter( Mandatory = $false, Position = 3 )]
           [string] $Arguments,

           [Parameter( Mandatory = $false, Position = 4 )]
           [ValidateNotNullOrEmpty()]
           [string] $TargetName = "testApp",

           [Parameter( Mandatory = $false )]
           [switch] $Attach,

           [Parameter( Mandatory = $false )]
           [Alias( 'gi' )]
           [switch] $SkipInitialBreakpoint,

           [Parameter( Mandatory = $false )]
           [Alias( 'gf' )]
           [switch] $SkipFinalBreakpoint,

           [Parameter( Mandatory = $false )]
           [switch] $UseWindbgInstead,

           # Useful from test scripts, so that they aren' popping up windows all over.
           [Parameter( Mandatory = $false )]
           [switch] $HiddenTargetWindow
         )

    begin { }
    end { }
    process
    {
        if( $UseWindbgInstead -and $PSBoundParameters.ContainsKey( 'TargetName' ) )
        {
            Write-Warning "The -TargetName parameter doesn't make sense with -UseWindbgInstead."
        }

        if( (Test-Path "Dbg:\$TargetName") -and !$UseWindbgInstead )
        {
            throw "The name '$TargetName' is already in use; please use a different name."
        }

        [string] $path = Find-TestApp $TestApp -Platform $Platform

        $testAppReadyEvent = New-InheritableEvent
        $pauseEvent = New-InheritableEvent
        try
        {
            if( $Attach )
            {
                if( $SkipInitialBreakpoint -and !$UseWindbgInstead )
                {
                    Write-Warning "The -Attach option coordinates with the target test app, in order to guarantee that we always get attached at a consistent, deterministic spot (because this command is for testing). By using the -SkipInitialBreakpoint along with the -Attach option, this command will not get a chance to set the `"you may now continue execution`" event until the app breaks for some reason (for instance, you can cause this by typing CTRL+C in the target test app's window)."
                    # TODO: Perhaps instead we could not really skip the initial
                    # breakpoint, but instead issue a Resume-DbgProcess ("g") as soon as
                    # we hit it.
                }

                $arglist = ("SetEvent $($testAppReadyEvent.SafeWaitHandle.DangerousGetHandle().ToString()) ; WaitEvent $($pauseEvent.SafeWaitHandle.DangerousGetHandle().ToString())" + " ; " + $Arguments)
                # The -UseNewEnvironment is important; it causes Start-Process
                # to not use ShellExecute. (We don't want to use ShellExecute
                # because we need the child proc to inherit the handles to the
                # synchronization events.)

                $startProcArgs = @{ 'FilePath' = $path
                                    'ArgumentList' = $arglist
                                    'UseNewEnvironment' = $true
                                    'PassThru' = $true
                                  }
                if( $HiddenTargetWindow -and !(Test-Path Variable:\__globalForceShowTestWindows) )
                {
                    $startProcArgs[ 'WindowStyle' ] = 'Hidden'
                }

                $proc = Start-Process @startProcArgs
                [bool] $ready = $false
                while( !$proc.HasExited -and !$ready )
                {
                    # It could crash before we get attached. We poll for that so we won't
                    # get hung if that happens.
                    $ready = $testAppReadyEvent.WaitOne( 5000 )
                }
                if( $proc.HasExited )
                {
                    Assert (!$ready)
                    Write-Error "Hmmm... the test process died before we could attach (pid was $($proc.Id))."
                }
                else
                {
                  # Start-Sleep -Milli 500
                    if( $UseWindbgInstead ) {
                        [object[]] $flags = @()

                        if( $SkipInitialBreakpoint ) { $flags += '-g' }
                        if( $SkipFinalBreakpoint )   { $flags += '-G' }

                        & (Find-Windbg 'windbg.exe') @flags -p $proc.Id
                        # Need to wait for windbg to get attached. Hopefully this is long
                        # enough.
                        Write-Host "Waiting for windbg to get attached..." -Fore Cyan
                        Start-Sleep -Milli 5000
                    } else {
                        $proc | Connect-Process -TargetName $TargetName `
                                                -SkipInitialBreakpoint:$SkipInitialBreakpoint `
                                                -SkipFinalBreakpoint:$SkipFinalBreakpoint
                    }
                    [void] $pauseEvent.Set()
                }
            }
            else # !$Attach
            {

                if( $UseWindbgInstead ) {

                    if( $HiddenTargetWindow )
                    {
                        Write-Warning 'The -HiddenTargetWindow is ignored when launching windbg.'
                    }

                    [object[]] $flags = @()

                    if( $SkipInitialBreakpoint ) { $flags += '-g' }
                    if( $SkipFinalBreakpoint )   { $flags += '-G' }

                    $arglist = "$flags $path $Arguments"
                    Start-Process (Find-Windbg 'windbg.exe') -ArgumentList $arglist -UseNewEnvironment
                } else {
                    $startDbgProcOptions = @{ 'FilePath' = $path
                                              'TargetName' = $TargetName
                                              'SkipInitialBreakpoint' = $SkipInitialBreakpoint
                                              'SkipFinalBreakpoint' = $SkipFinalBreakpoint
                                              'ArgumentList' = $Arguments
                                            }

                    if( $HiddenTargetWindow )
                    {
                        $startDbgProcOptions[ 'ConsoleOption' ] = 'NoConsole'
                    }

                    Start-DbgProcess @startDbgProcOptions
                }
            }
        }
        finally
        {
            $testAppReadyEvent.Dispose()
            $pauseEvent.Dispose()
        }
    }
} # end function New-TestApp

<#
.Synopsis
    Convenient shortcut to attach to a new TestNativeConsoleApp.exe process.
#>
function ntna
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $false, Position = 0 )]
           [string] $Arguments,

           [Parameter( Mandatory = $false, Position = 1 )]
           [ValidateNotNullOrEmpty()]
           [string] $TargetName = "testApp"
         )

    begin { }
    end { }
    process
    {
        try
        {
            New-TestApp TestNativeConsoleApp -Attach -TargetName $TargetName -Arguments $Arguments
        }
        finally { }
    }
} # end function ntna


<#
.Synopsis
    Convenient shortcut to attach to a new TestManagedConsoleApp.exe process.
#>
function ntma
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $false, Position = 0 )]
           [string] $Arguments,

           [Parameter( Mandatory = $false, Position = 1 )]
           [ValidateNotNullOrEmpty()]
           [string] $TargetName = "testApp"
         )

    begin { }
    end { }
    process
    {
        try
        {
            New-TestApp TestManagedConsoleApp -Attach -TargetName $TargetName -Arguments $Arguments
        }
        finally { }
    }
} # end function ntma


<#
.Synopsis
    Runs all the tests (Tests:\*.ps1) multiple times for all combinations of PathBugginessStyle and using a provider-qualified working directory or not.
function DoFullTestPass()
{
    [string] $testName = "Full regression test pass"
    [bool] $alreadyRanGlobbing = $false

    Import-Module TE.PowerShell
    # Fortunately, the WEX logging state is re-entrant/refcounted.
    [WEX.Logging.Interop.LogController]::InitializeLogging()

    Log-StartGroup $testName
    foreach( $pbs in @( [MS.Dbg.PathBugginessStyle]::RegistryProvider, [MS.Dbg.PathBugginessStyle]::CertificateProvider ) )
    {
        foreach( $upqwd in @( $false, $true ) )
        {
            $testProps = Get-TestProperties
            $testProps[ "PathBugginessStyle" ] = $pbs
            $testProps[ "UseProviderQualifiedWorkingDirectory" ] = $upqwd

            if( $alreadyRanGlobbing )
            {
                Run-Test Tests:\*.ps1 -Exclude Tests:\Globbing.ps1
            }
            else
            {
                Run-Test Tests:\*.ps1
                $alreadyRanGlobbing = $true
            }
        }
    }
    Log-EndGroup $testName

    [WEX.Logging.Interop.LogController]::FinalizeLogging()
} # end DoFullTestPass
#>


function Get-DbgTypeCacheStats
{
    [CmdletBinding()]
    param()

    begin { }
    end { }
    process
    {
        try
        {
            $hits = [MS.Dbg.DbgTypeInfo]::CacheHits
            $misses = [MS.Dbg.DbgTypeInfo]::CacheMisses
            if( $misses -gt 0 )
            {
                $hitPercent = [int] (($hits / $misses) * 100)
            }
            else
            {
                $hitPercent = 'NaN'
            }
            return [PSCustomObject] @{ 'Hits' = $hits ;
                                       'Misses' = $misses ;
                                       'HitPercent' = $hitPercent }
        }
        finally { }
    }
}

function PostTestCheckAndResetCacheStats
{
    [CmdletBinding()]
    param()

    begin { }
    end { }
    process
    {
        try
        {
            $cs = Get-DbgTypeCacheStats
            Write-Host "DbgTypeInfo cache stats: $($cs.Hits) hits, $($cs.Misses) misses ($($cs.HitPercent)%)." -Fore Cyan
            if( 0 -ne ([MS.Dbg.DbgTypeInfo]::GetCacheSize()) )
            {
                Write-Warning "Cache size should currently be zero (because we should not be attached to anything)."
            }
            [MS.Dbg.DbgTypeInfo]::ResetCacheStatistics()
        }
        finally { }
    }
}

