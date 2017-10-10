# How to inovke in post-build step: powershell.exe -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -Command ". '$(SolutionDir)PostBuild.ps1' '$(ProjectDir)' '$(PlatformName)' '$(TargetDir)'; exit $LastExitCode"

[CmdletBinding()]
param(
    [parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string] $ProjectDir,

    [parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string] $PlatformName,

    [parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string] $DestDir
)

Set-StrictMode -Version Latest

function Unquote( [string] $s )
{
    if( $s[ 0 ] -eq '"' )
    {
        return $s.Substring( 1, $s.Length - 2 )
    }
    else
    {
        return $s
    }
}

function EnsureQuoted( [string] $s )
{
    if( $s[ 0 ] -eq '"' )
    {
        return $s
    }
    else
    {
        return '"' + $s + '"'
    }
}

function EnsureDirectoryExists( [string] $dir )
{
    if( ![System.IO.Directory]::Exists( $dir ) )
    {
        $dirInfo = [System.IO.Directory]::CreateDirectory( $dir )
        "Created directory: " + $dirInfo.FullName | Out-Host
    }
}

function RemoveTrailingWhack( [string] $s )
{
    if( $s[ $s.Length - 1 ] -eq '\' )
	{
	    return $s.Substring( 0, $s.Length - 1 );
	}
	else
	{
	    return $s
	}
}

[Console]::WriteLine( "ProjectDir is: {0}", $ProjectDir )
[Console]::WriteLine( "PlatformName is: {0}", $PlatformName )
[Console]::WriteLine( "DestDir is: {0}", $DestDir )

EnsureDirectoryExists $DestDir

$uqProjectDir = RemoveTrailingWhack (Unquote $ProjectDir)
$uqDestDir = RemoveTrailingWhack (Unquote $DestDir)


[Console]::WriteLine( "Copying Help stuff..." )
robocopy "$uqProjectDir\Help" "$uqDestDir" /E /XF *.swp /XX /NP

[Console]::WriteLine( "Copying TypeInfoDebugging stuff..." )
xcopy "$uqProjectDir\TypeInfoDebugging.psm1" "$uqDestDir\..\TypeInfoDebugging\" /D /Y

# Markdown docs. TBD: build powershell-native help based on them
[Console]::WriteLine( "Copying Markdown docs..." )
robocopy "$uqProjectDir\..\doc" "$uqDestDir\..\doc\" /E /XF *.swp /XX /NP

$LastExitCode = 0

