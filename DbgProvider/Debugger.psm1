Set-StrictMode -Version Latest

<#
.Synopsis
    Turns on debug tracing of the Debugger provider and select PowerShell trace sources.

.Description
    Creates a TextWriterTraceListener which traces to the file specified by the TraceFilePath parameter (default: $env:temp\DbgProviderTracing.txt). This listener is added to the Debugger provider's trace source listener collection, as well as the listener collection of any specified PowerShell trace sources (default: 'LocationGlobber', 'PathResolution', 'PSDriveInfo', 'CmdletProviderClasses').
#>
function Enable-ProviderTracingStuff
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $false, Position = 0 )]
           [ValidateNotNullOrEmpty()]
           [string] $TraceFilePath = "$env:temp\DbgProviderTracing.txt",

           [Parameter( Mandatory = $false, Position = 1 )]
           [string[]] $PsTraceSources = @( 'LocationGlobber', 'PathResolution', 'PSDriveInfo', 'CmdletProviderClasses' )
         )

    begin { }
    end { }
    process
    {
        $listener = New-Object "System.Diagnostics.TextWriterTraceListener" -ArgumentList @( $TraceFilePath, "DbgProviderTraceListener" )
        $listener.TraceOutputOptions = 'DateTime,ProcessId,ThreadId'

        if( ($null -ne $PsTraceSources) -and ($PsTraceSources.Count -gt 0) )
        {
            #Set-TraceSource -Name $PsTraceSources -Option All -ListenerOption Timestamp -PassThru | % { [void] $_.Listeners.Add( $listener ) }
            Set-TraceSource -Name $PsTraceSources -Option All -PassThru | % { [void] $_.Listeners.Add( $listener ) }
        }

        [MS.Dbg.DbgProvider]::AddListener( $listener )
    }
} # end Enable-ProviderTracingStuff


<#
.Synopsis
    Gets the path bugginess style used by path manipulation functions in the provider.

.Description
    There are currently path manipulation bugs inherent in the PowerShell provider system--it is difficult if not impossible to write a provider without some set of path manipulation bugs. This command allows you to get a value indicating the set of path manipulation bugs exhibited by the provider. The provider can exhibit either Registry provider-style bugs or Certificate provider-style bugs.

.Link
    Set-PathBugginessStyle
#>
function Get-PathBugginessStyle
{
    [CmdletBinding()]
    param( )

    begin { }
    end { }
    process
    {
        try
        {
            [MS.Dbg.DbgProvider]::GetPathBugginessStyle()
        }
        finally { } # needed in order to get the error-processing semantics we want.
    }
} # end Get-PathBugginessStyle


<#
.Synopsis
    Sets the path bugginess style used by path manipulation functions in the provider.

.Description
    There are currently path manipulation bugs inherent in the PowerShell provider system--it is difficult if not impossible to write a provider without some set of path manipulation bugs. This command allows you to select the set of path manipulation bugs exhibited by the provider. You can choose between Registry provider-style bugs or Certificate provider-style bugs.

.Link
    Get-PathBugginessStyle
#>
function Set-PathBugginessStyle
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [MS.Dbg.PathBugginessStyle] $PathBugginessStyle
         )

    begin { }
    end { }
    process
    {
        try
        {
            [MS.Dbg.DbgProvider]::SetPathBugginessStyle( $PathBugginessStyle )
        }
        finally { } # needed in order to get the error-processing semantics we want.
    }
} # end Set-PathBugginessStyle


function Assert
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [bool] $Condition,

           [Parameter( Mandatory = $false, Position = 1 )]
           [ValidateNotNullOrEmpty()]
           [string] $Message
         )

    begin { }
    end { }
    process
    {
        try
        {
            if( $Condition )
            {
                return
            }

            $stack = [System.Management.Automation.CallStackFrame[]] (Get-CallersStack)
            [string] $msg = $null
            if( !$stack )
            {
                throw "Assertion failed (no stack): $Message"
            }

            # We don't actually need to use $Message, because it will show up in $stack[ 0 ].Position below.
            $msg = "Assertion failed: $($stack[ 0 ].Location) : $($stack[ 0 ].Position.ToString())"

            $ex = New-Object 'System.Exception' -ArgumentList @( $msg )
            $ex.Data[ $PSCallstackKey ] = $stack
            $assertErrorRecord = New-ErrorRecord $ex 'FailedAssert'

            throw $assertErrorRecord
        }
        finally { }
    }
} # end Assert()


<#
.Synopsis
    Saves the current virtual namespace to a stack and creates a new one.

.Description
    Saves the current virtual namespace, including drives, to a stack, and replaces the current virtual namespace with a new one. This can be used for testing--when testing is finished, you can call Pop-Namespace, which will discard any changes you have made, restoring the namespace that was saved by Push-Namespace.

.Link
    Pop-Namespace
#>
function Push-Namespace
{
    [CmdletBinding()]
    param( )

    begin { }
    end { }
    process
    {
        try
        {
            [MS.Dbg.DbgProvider]::PushNamespace()
            # Current working directory may not exist. We'll move to the root to be consistent.
            [System.Diagnostics.Debug]::Assert( $? )
        }
        finally { } # needed in order to get the error-processing semantics we want.
    }
} # end Push-Namespace


<#
.Synopsis
    Restores the most recent virtual namespace from the stack, discarding the current namespace.

.Description
    Restores the current virtual namespace, including drives, from a stack, and discards the current namespace. Useful for testing. See also: Push-Namespace.

.Link
    Push-Namespace
#>
function Pop-Namespace
{
    [CmdletBinding()]
    param( )

    begin { }
    end { }
    process
    {
        try
        {
            [MS.Dbg.DbgProvider]::PopNamespace()
        }
        finally { } # needed in order to get the error-processing semantics we want.
    }
} # end Pop-Namespace


<#
.SYNOPSIS
    If a symbol name contains special characters, it quotes and escapes it.

    Example: ole32!GenericStream::`vftable' --> 'ole32!GenericStream::`vftable'''
#>
function QuoteAndEscapeSymbolNameIfNeeded
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0, ValueFromPipeline = $true )]
           [string] $SymName )

    begin { }
    end { }
    process
    {
        try
        {
            if( [string]::IsNullOrEmpty( $SymName ) )
            {
                return $SymName
            }

            if( $SymName.Contains( "``" ) -or
                $SymName.Contains( "'" ) -or
                $SymName.Contains( '$' ) )
            {
                $SymName = [String]::Format( "'{0}'", $SymName.Replace( "'", "''" ) )
            }
            return $SymName
        }
        finally { }
    }
} # end QuoteAndEscapeSymbolNameIfNeeded


<#
.Synopsis
    Disconnects from a dump target.
.Description
    Provided for parity with 'Mount-DbgDumpFile': you can also just use '.detach' or 'Disconnect-DbgProcess'.
#>
function Dismount-DbgDumpFile()
{
    .detach
} # end Dismount-DbgDumpFile


<#
.SYNOPSIS
    Converts a CSV trace file (as emitted by "tracerpt -of csv" or Convert-EtlToCsv) to plain text.
#>
function Convert-CsvTraceToText( [CmdletBinding()]
                                 [Parameter( Mandatory = $true, Position = 0, ValueFromPipeline = $true )]
                                 [ValidateNotNullOrEmpty()]
                                 [string] $inputCsvFile )
{
    Begin { }
    End { }

    Process
    {
        $ErrorActionPreference = [System.Management.Automation.ActionPreference]::Stop

        # Needed to support relative paths in PS.
        $inputCsvFile = $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath( $inputCsvFile )

        [System.IO.StreamReader] $reader = $null
        [System.IO.StreamWriter] $writer = $null
        try
        {
            #[string] $outputTxtFile = $inputCsvFile + ".txt"
            [string] $outputTxtFile = $null
            if( $inputCsvFile.EndsWith( ".csv", [StringComparison]::OrdinalIgnoreCase ) )
            {
                $outputTxtFile = $inputCsvFile.Substring( 0, $inputCsvFile.Length - 4 ) + ".txt"
            }
            else
            {
                $outputTxtFile = $inputCsvFile + ".txt"
            }

            if( [System.IO.File]::Exists( $outputTxtFile ) )
            {
                Write-Warning ('File "{0}" already exists; skipping it.' -f $outputTxtFile)
                return
            }
            $reader = New-Object "System.IO.StreamReader" -ArgumentList @( $inputCsvFile )
            [System.IO.FileStream] $outStream = New-Object "System.IO.FileStream" -ArgumentList @( $outputTxtFile, [System.IO.FileMode]::CreateNew )
            $writer = New-Object "System.IO.StreamWriter" -ArgumentList @( $outStream )

            # Skip the first two lines (CSV header stuff).
            $line = $reader.ReadLine()
            $line = $reader.ReadLine()
            if( $null -eq $line )
            {
                Write-Error "There weren't even two lines?" -Category InvalidData -TargetObject $inputCsvFile
                return;
            }

            New-Variable -Name Quote -Value 34 -Option ReadOnly -Visibility Private

            function EatPastQuote()
            {
                [int] $intChar = 0
                while( ($intChar -ne -1) -and ($intChar -ne $Quote) )
                {
                    $intChar = $reader.Read()
                }
                return $intChar -ne -1
            } # end EatPastQuote()

            while( $true )
            {
                if( !(EatPastQuote) )
                {
                    break
                }

                $line = $reader.ReadLine()
                if( $line -eq $null )
                {
                    break
                }

                $dropLastQuote = $line.Substring( 0, $line.Length - 1 )
                $writer.WriteLine( $dropLastQuote )
            }

            return $outputTxtFile
        }
        finally
        {
            if( $null -ne $reader )
            {
                $reader.Dispose()
            }

            if( $null -ne $writer )
            {
                $writer.Dispose()
            }
        }
    } # end Process block
} # end Convert-CsvTraceToText()


<#
.SYNOPSIS
    Converts an ETL trace file to CSV using "tracerpt -of csv ...".
#>
function Convert-EtlToCsv( [CmdletBinding()]
                           [Parameter( Mandatory = $true, Position = 0, ValueFromPipeline = $true )]
                           [ValidateNotNullOrEmpty()]
                           [string] $inputEtlFile )
{
    Begin { }
    End { }

    Process
    {
        $ErrorActionPreference = [System.Management.Automation.ActionPreference]::Stop

        # Needed to support relative paths in PS.
        $inputEtlFile = $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath( $inputEtlFile )

        $csvFile = $inputEtlFile + '.csv'
        tracerpt -y -of csv -o $csvFile $inputEtlFile | Out-Null
        return $csvFile
    }
} # end Convert-EtlToCsv


<#
.SYNOPSIS
    Converts an ETL trace file to a plain text file.
#>
function Convert-EtlToText( [CmdletBinding()]
                            [Parameter( Mandatory = $true, Position = 0, ValueFromPipeline = $true )]
                            [ValidateNotNullOrEmpty()]
                            [string] $inputEtlFile )
{
    Begin { }
    End { }

    Process
    {
        $ErrorActionPreference = [System.Management.Automation.ActionPreference]::Stop

        # Needed to support relative paths in PS.
        $inputEtlFile = $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath( $inputEtlFile )

        $csvFile = Convert-EtlToCsv $inputEtlFile
        $txtFile = Convert-CsvTraceToText $csvFile
        if( $? )
        {
            # Don't need to keep the CSV lying around...
            Remove-Item $csvFile
        }
        return $txtFile
    }
} # end Convert-EtlToText


<#
.SYNOPSIS
   Converts a fully-qualified path to a PS-relative path.

.DESCRIPTION
    If the specified $path starts with $PWD, then the $PWD path is replaced with ".", yielding a path relative to the current PS working directory.
#>
function ConvertTo-RelativePath( [string] $path )
{
    if( $path.StartsWith( $PWD ) )
    {
        return "." + $path.Substring( $PWD.ToString().Length )
    }
    else
    {
        return $path
    }
}


<#
.SYNOPSIS
    Pops open a text file with gvim if installed, else notepad.
#>
function Open-TextFile( [string] $file )
{
    $ErrorActionPreference = [System.Management.Automation.ActionPreference]::Stop
    # Needed to support relative paths in PS.
    $file = $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath( $file )

    $command = Get-Command -Name "gvim.exe*" # using a wildcard pattern so it returns nothing if none found (instead of an error)
    if( !$command )
    {
        $command = Get-Command -Name "notepad.exe"
    }
    & $command $file
} # end Open-TextFile()


<#
.SYNOPSIS
   Pads the specified string with leading zeros. (i.e. "ZeroPad '1' 3" yields "001")
#>
function ZeroPad( [string] $s, [int] $len )
{
    if( $s.Length -ge $len ) { return $s }

    $numZeroesNeeded = $len - $s.Length
    return ((New-Object 'System.String' -Arg @( ([char] '0'), $numZeroesNeeded )) + $s)
} # end ZeroPad()


<#
.SYNOPSIS
    Saves a copy of the specified (or current) DbgShell debug trace file, converts it to text, and opens it.
#>
function Open-DbgShellLog
{
    [CmdletBinding( DefaultParameterSetName = 'DefaultParameterSetName' )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ParameterSetName = 'DefaultParameterSetName' )]
           [string] $DbgShellLog,

           [Parameter( Mandatory = $false, Position = 1 )]
           [string] $DestFileName,

           [Parameter( Mandatory = $false,
                       ParameterSetName = 'PenultimateParameterSetName' )]
           [switch] $Penultimate
         )

    begin { }
    end { }

    process
    {
        [bool] $needToPopLocation = $false
        try
        {
            $ErrorActionPreference = [System.Management.Automation.ActionPreference]::Stop

            if( "FileSystem" -ne (Get-Location).Provider.Name )
            {
                $needToPopLocation = $true
                Push-Location C:
            }

            [bool] $isCurrentLog = $false

            if( $Penultimate )
            {
                $DbgShellLog = Get-DbgShellLog | Select-Object -Skip 1 -First 1
            }
            elseif( !$DbgShellLog )
            {
                $isCurrentLog = $true
                $DbgShellLog = Get-DbgShellLog -Current
            }

            if( !$DestFileName )
            {
                $DestFileName = [System.IO.Path]::GetFileNameWithoutExtension( $DbgShellLog )
            }

            $destDir = [System.IO.Path]::GetDirectoryName( $DbgShellLog )
            $destDir = [System.IO.Path]::Combine( $destDir, 'Converted' )

            $destEtlFile = $DestFileName + '.etl'
            $destEtlFile = [System.IO.Path]::Combine( $destDir, $destEtlFile )

            if( [System.IO.File]::Exists( $destEtlFile ) )
            {
                [string] $candidate = ''

                for( [int] $i = 1 ; $i -lt 1000 ; $i++ )
                {
                    $candidate = $destEtlFile + "_" + (ZeroPad $i 3)
                    if( ![System.IO.File]::Exists( $candidate ) )
                    {
                        $destEtlFile = $candidate
                        break
                    }
                }

                if( [System.IO.File]::Exists( $destEtlFile ) )
                {
                    throw "The file '$destEtlFile' already exists, and we've hit the limit for tacking on a distinguisher. You've saved a copy of this log a lot, haven't you?"
                }
            }

            if( ![System.IO.Directory]::Exists( $destDir ) )
            {
                $null = mkdir $destDir
            }

            if( $isCurrentLog )
            {
                $asm = [System.Reflection.Assembly]::GetAssembly( [MS.Dbg.DbgProvider] )
                $LogManagerType = $asm.GetType( "MS.Dbg.LogManager" )
                [System.Reflection.BindingFlags] $bf = "Static,Public"
                $mi = $LogManagerType.GetMethod( "SaveLogFile", $bf );
                if( $null -eq $mi ) { throw "Unexpected: no LogManager.SaveLogFile method." }
                [void] $mi.Invoke( $null, @( $destEtlFile, $false ) )
            }
            else
            {
                copy $DbgShellLog $destEtlFile
            }

            $txtFile = Convert-EtlToText $destEtlFile

            Open-TextFile $txtFile
        }
        finally
        {
            if( $needToPopLocation )
            {
                Pop-Location
            }
        }
    }
} # end Open-DbgShellLog()


<#
.SYNOPSIS
    Converts all ETL trace files in a given directory to plain text.
#>
function Convert-AllEtlToText( [CmdletBinding()]
                               [Parameter( Mandatory = $true, Position = 0 )]
                               [ValidateNotNullOrEmpty()]
                               [string] $InputDir )
{
    Begin { }
    End { }

    Process
    {
        $ErrorActionPreference = [System.Management.Automation.ActionPreference]::Stop

        # Needed to support relative paths in PS.
        $InputDir = $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath( $InputDir )

        [int] $RootProgressId = 1

        Write-Progress -Id $RootProgressId -Activity "Converting trace files to plain text" -Status "Searching for files in $InputDir..."

        $files = Get-ChildItem -Path $InputDir -Filter *.etl -Recurse
        [int] $curFile = 0
        foreach( $file in $files )
        {
            Write-Progress -Id $RootProgressId -Activity "Converting trace files to plain text" -Status "Converting $($file.Name)..." -PercentComplete ([int](($curFile / $files.Count) * 100))
            foreach( $csvFile in Convert-EtlToCsv $file.FullName )
            {
                Convert-CsvTraceToText $csvFile
                if( $? ) # TODO: I set $ErrorActionPreference to 'Stop'... so if it failed, we should throw, so this check should be pointless.
                {
                    # Don't need to keep the CSV lying around...
                    Remove-Item $csvFile
                }
            }
            $curFile++
        }
        Write-Progress -Id $RootProgressId -Activity "Converting trace files to plain text" -Status "Done." -Completed
    } # end Process
} # end Convert-AllEtlToText


<#
.SYNOPSIS
    "Go": causes the current target to resume execution (wraps Resume-Process).
#>
function g
{
    [CmdletBinding()]
    param()

    Begin { }
    End { }

    Process
    {
        try
        {
            Resume-Process
        }
        finally { } # needed in order to get the error-processing semantics we want.
    } # end Process
} # end g()


<#
.SYNOPSIS
    "steP": causes the current target to single-step (wraps Resume-Process).
#>
function p
{
    [CmdletBinding()]
    param()

    Begin { }
    End { }

    Process
    {
        try
        {
            Resume-Process -ResumeType StepOver
        }
        finally { } # needed in order to get the error-processing semantics we want.
    } # end Process
} # end p()


<#
.SYNOPSIS
    "step inTo": causes the current target to single-step, into a call (wraps Resume-Process).
#>
function t
{
    [CmdletBinding()]
    param()

    Begin { }
    End { }

    Process
    {
        try
        {
            Resume-Process -ResumeType StepInto
        }
        finally { } # needed in order to get the error-processing semantics we want.
    } # end Process
} # end t()


<#
.SYNOPSIS
    "steP to Call": causes the current target to execute until the next call instruction (wraps Resume-Process).
#>
function pc
{
    [CmdletBinding()]
    param()

    Begin { }
    End { }

    Process
    {
        try
        {
            Resume-Process -ResumeType StepToCall
        }
        finally { } # needed in order to get the error-processing semantics we want.
    } # end Process
} # end pc()


<#
.SYNOPSIS
    "Go Up": causes the current target to resume execution until it returns from the current function.
#>
function gu
{
    [CmdletBinding()]
    param()

    Begin { }
    End { }

    Process
    {
        try
        {
            Resume-Process -ResumeType StepOut
        }
        finally { } # needed in order to get the error-processing semantics we want.
    } # end Process
} # end gu()


<#
                     Aliases that override built-in aliases

                                 ("both scopes")

 Aliases that override built-in aliases must be set in both 'Global' AND
 'Local' scopes. If not also set in local scope, then functions in this module
 that attempt to use the alias will see the built-in instead of our override.

#>


# "gu" is traditionally aliased to Get-Unique, which is handy, but I think the debugger
# command is more important.
Set-Alias -Name gu -Value 'Debugger\gu' -Option AllScope -Scope Local -Force  # see comment about setting in "both scopes"
Set-Alias -Name gu -Value 'Debugger\gu' -Option AllScope -Scope Global -Force # see comment about setting in "both scopes"


<#
.SYNOPSIS
    If no parameters specified: "Go Conditional": causes the current target to resume execution from a conditional breakpoint in the same fashion that was used to hit the breakpoint (stepping, tracing, or freely executing).

    Else: a proxy for Get-Content. (Run "Get-Content -?" for more information about
    Get-Content parameters.)
#>
function gc
{
    [CmdletBinding( DefaultParameterSetName = 'GoConditional', SupportsTransactions = $true )]
    param( [Parameter( ValueFromPipelineByPropertyName = $true )]
           [long]
           ${ReadCount},

           [Parameter( ValueFromPipelineByPropertyName = $true )]
           [Alias( 'First', 'Head' )]
           [long]
           ${TotalCount},

           [Parameter( ValueFromPipelineByPropertyName = $true )]
           [Alias( 'Last' )]
           [int]
           ${Tail},

           [Parameter( ParameterSetName = 'Path',
                       Mandatory = $true,
                       Position = 0,
                       ValueFromPipelineByPropertyName = $true )]
           [string[]]
           ${Path},

           [Parameter( ParameterSetName = 'LiteralPath',
                       Mandatory = $true,
                       ValueFromPipelineByPropertyName = $true)]
           [Alias( 'PSPath' )]
           [string[]]
           ${LiteralPath},

           [string]
           ${Filter},

           [string[]]
           ${Include},

           [string[]]
           ${Exclude},

           [switch]
           ${Force},

           [Parameter( ValueFromPipelineByPropertyName = $true )]
           [pscredential]
           [System.Management.Automation.CredentialAttribute()]
           ${Credential}
         )

    begin
    {
        try
        {
            $steppablePipeline = $null
            if( $PSCmdlet.ParameterSetName -eq 'GoConditional' )
            {
                # Unfortunately this operation can't be performed by using a proper API--it
                # depends on knowing internal dbgeng state. So we'll just have to ask dbgeng
                # to do it for us.
                # TODO: I think running Invoke-DbgEng here (as opposed to letting
                # DbgEngDebugger call InvokeDbgEngCommand) has some behavior differences.
                # But... there's too much else currently in play (conditional bp / message
                # loop stuff) to worry about digging into it right now.
                #Invoke-DbgEng 'gc'
                Resume-Process -ResumeType GoConditional
            }
            else
            {
                $outBuffer = $null
                if ($PSBoundParameters.TryGetValue('OutBuffer', [ref]$outBuffer))
                {
                    $PSBoundParameters['OutBuffer'] = 1
                }
                $wrappedCmd = $ExecutionContext.InvokeCommand.GetCommand('Microsoft.PowerShell.Management\Get-Content', [System.Management.Automation.CommandTypes]::Cmdlet)
                $scriptCmd = {& $wrappedCmd @PSBoundParameters }
                $steppablePipeline = $scriptCmd.GetSteppablePipeline($myInvocation.CommandOrigin)
                $steppablePipeline.Begin($PSCmdlet)
            }
        }
        finally { }
    }

    process
    {
        try
        {
            if( $steppablePipeline )
            {
                $steppablePipeline.Process($_)
            }
        }
        finally { }
    }

    end
    {
        try
        {
            if( $steppablePipeline )
            {
                $steppablePipeline.End()
            }
        }
        finally { }
    }
} # end gc()

# "gc" is traditionally aliased to Get-Content, which is handy, but I think the debugger
# command is more important. We've mitigated this by making "gc" invoke Get-Content if
# called with parameters.
Set-Alias -Name gc -Value 'Debugger\gc' -Option AllScope -Scope Local -Force  # see comment about setting in "both scopes"
Set-Alias -Name gc -Value 'Debugger\gc' -Option AllScope -Scope Global -Force # see comment about setting in "both scopes"


Set-Alias kc Get-DbgStack

<#
.SYNOPSIS
    Displays the "stacK" (like windbg 'k' command). Note that this does not return the actual DbgStackInfo objects (it outputs formatted strings). Use "kc" to get actual stack objects (or Get-DbgStack).
#>
function k
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $false,
                       Position = 0 )]
           [int] $MaxFrameCount,

           [Parameter( Mandatory = $false,
                       Position = 1,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.ThreadTransformation()] # so we can take thread IDs
           [MS.Dbg.DbgUModeThreadInfo[]] $Thread
         )

    Begin { }
    End { }

    Process
    {
        try
        {
            Get-DbgStack -MaxFrameCount $MaxFrameCount -Thread $Thread | %{ $_.Frames | Format-AltTable }
        }
        finally { } # needed in order to get the error-processing semantics we want.
    } # end Process
} # end k()


<#
.Synopsis
    "Thread command": used to handle various dbgeng-style thread commands ("~4 s", etc.).

.Description
    A CommandNotFound handler is used to intercept things like "~* k" and send them to this function (with a little argument massaging).
#>
function ~x
{
    [CmdletBinding()]
    # TODO: This attribute does not seem to get me tab completion
    # (as in "~ | %{ $_.Sta[TAB]"). Bug?
    [OutputType( [MS.Dbg.DbgUModeThreadInfo] )]
    param( [Parameter( Mandatory = $false, Position = 0 )]
           [ValidateNotNullOrEmpty()]
           [string] $threadId,

           [Parameter( Mandatory = $false, Position = 1, ValueFromRemainingArguments = $true )]
           [string] $command
         )

    process
    {
        [bool] $needToSetContextBack = $false
        $originalContext = $Debugger.GetCurrentDbgEngContext()

        try
        {
            [bool] $moreThanOne = $false

            . {
                if( !$threadId )
                {
                    # This is a little odd, but it seems to match windbg.
                    if( $command ) {
                        # Although actually, I don't think we can end up in this case if you
                        # are using windbg-style syntax (which gets intercepted by the
                        # CommandNotFound handler), because the CommandNotFound handler
                        # doesn't handle it. (ex: "~ k" works in windbg, but I don't think
                        # that will get us here in DbgShell)
                        $threadId = '.'
                    } else {
                        $threadId = '*'
                    }
                }

                if( '*' -eq $threadId )
                {
                    $moreThanOne = $true
                    Get-DbgUModeThreadInfo
                }
                elseif( '#' -eq $threadId )
                {
                    Get-DbgUModeThreadInfo -LastEvent
                }
                elseif( '.' -eq $threadId )
                {
                    Get-DbgUModeThreadInfo -Current
                }
                else
                {
                    [UInt32] $uid = 0
                    if( ![UInt32]::TryParse( $threadId, [ref] $uid ) )
                    {
                        throw "Invalid thread id: $threadId"
                    }
                    Get-DbgUModeThreadInfo -DebuggerId $uid
                }
            } | %{
                $thread = $_

                if( !$command )
                {
                  # if( !$moreThanOne )
                  # {
                  #     $thread | Format-List
                  # }
                  # else
                  # {
                        $thread
                  # }
                }
                else
                {
                    $needToSetContextBack = $true

                    if( $moreThanOne -and $command )
                    {
                        $thread | Format-AltSingleLine
                    }

                    # TODO: handle f/u, s/r, maybe even e
                    if( 's' -eq $command )
                    {
                        if( $moreThanOne )
                        {
                            Write-Warning "No... I'm not going to switch to each thread's context."
                            break
                        }

                        $needToSetContextBack = $false
                        Switch-DbgUModeThreadInfo -Thread $thread
                        break
                    }

                    Switch-DbgUModeThreadInfo -Thread $thread -Quiet

                    #
                    # TODO: Invoke-Expression is generally the wrong thing to do... really
                    # I ought to have the caller pass in a ScriptBlock. But this command
                    # is designed to make things seem just like in windbg...
                    #
                    Invoke-Expression $command
                }
            }
        }
        finally
        {
            if( $needToSetContextBack )
            {
                $Debugger.SetCurrentDbgEngContext( $originalContext )
            }
        }
    } # end process
} # end ~x()


<#
.Synopsis
    Formats and colorizes an address. NOTE: returns "(null)" for zero.
#>
function Format-DbgAddress
{
    [CmdletBinding( DefaultParameterSetName = 'DebuggerParamSet' )]
    [OutputType( [MS.Dbg.ColorString] )]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true )]
           [UInt64] $Address,

           [Parameter( Mandatory = $false,
                       Position = 1,
                       ParameterSetName = 'DebuggerParamSet' )]
           [MS.Dbg.DbgEngDebugger] $Debugger,

           [Parameter( Mandatory = $true,
                       Position = 1,
                       ParameterSetName = 'Is32BitParamSet' )]
           [bool] $Is32Bit,

           [Parameter( Mandatory = $false )]
           [System.ConsoleColor] $DefaultFgColor = [System.ConsoleColor]::Cyan
         )

    if( $PSCmdlet.ParameterSetName -eq 'DebuggerParamSet' )
    {
        if( $null -eq $Debugger )
        {
            $parentVar = Get-Variable 'Debugger*' -Scope Global -ValueOnly # '*' to prevent an error if it doesn't exist
            $Debugger = $parentVar
        }

        if( $null -eq $Debugger )
        {
            # Still don't have one? Hm... default to current process bitness, I suppose.
            # TODO: I could at least check if the high DWORD is zero or not...
            $Is32Bit = ![Environment]::Is64BitProcess
        }
        else
        {
            $Is32Bit = $Debugger.get_TargetIs32Bit()
        }
    }

    [MS.Dbg.DbgProvider]::FormatAddress( $Address,
                                         $Is32Bit,
                                         $true,  # useTick
                                         $false, # numericNull
                                         $DefaultFgColor )
} # end Format-DbgAddress


<#
.Synopsis
    Formats a 64-bit unsigned integer. If the high DWORD is non-zero, it will show both complete DWORDs with a tick ('`') in between.
#>
function Format-DbgUInt64
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true )]
           [UInt64] $u
         )

    [MS.Dbg.DbgProvider]::FormatUInt64( $u, $true )
} # end Format-DbgUInt64


<#
.Synopsis
    Given a number of bytes, returns a string formatted for pleasant human consumption (like "1.1 MB").
#>
function Format-DbgByteSize
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true )]
           [UInt64] $NumberOfBytes
         )

    [MS.Dbg.DbgProvider]::FormatByteSize( $NumberOfBytes )
} # end Format-DbgByteSize


<#
.Synopsis
    Colorizes a symbol string (which includes both a module and type name, e.g. "ntdll!_RTL_CRITICAL_SECTION").
#>
function Format-DbgSymbol
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.ColorString] )]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true )]
           [string] $SymbolName
         )

    [MS.Dbg.DbgProvider]::ColorizeSymbol( $SymbolName )
} # end Format-DbgSymbol


<#
.Synopsis
    Colorizes a type name.
#>
function Format-DbgTypeName
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.ColorString] )]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true )]
           [string] $TypeName
         )

    try
    {
        [MS.Dbg.DbgProvider]::ColorizeTypeName( $TypeName )
    } finally { }
} # end Format-DbgTypeName


<#
.Synopsis
    Colorizes a module name.
#>
function Format-DbgModuleName
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.ColorString] )]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true )]
           [string] $ModuleName
         )

    [MS.Dbg.DbgProvider]::ColorizeModuleName( $ModuleName )
} # end Format-DbgModuleName


<#
.Synopsis
    Colorizes a register name.
#>
function Format-DbgRegisterName
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.ColorString] )]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true )]
           [string] $RegisterName
         )

    [MS.Dbg.DbgProvider]::ColorizeRegisterName( $RegisterName )
} # end Format-DbgRegisterName


function .sympath
{
    [CmdletBinding()]
    [OutputType( [System.String] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true )]
           [string] $Path
         )
    begin { }
    end { }
    process
    {
        # PS likes to convert null strings to empty strings, so this is how we
        # check if user passed a path or not.
        if( $PSBoundParameters.ContainsKey( 'Path' ) )
        {
            # Splatting $PSBoundParameters so that -Verbose, et al get passed along too.
            Set-DbgSymbolPath @PSBoundParameters
        }
        else
        {
            Get-DbgSymbolPath @PSBoundParameters
        }
    }
} # end .sympath


function .sympath+
{
    [CmdletBinding()]
    [OutputType( [System.String] )]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true )]
           [ValidateNotNullOrEmpty()]
           [string] $Path
         )
    begin { }
    end { }
    process
    {
        $origPath = Get-DbgSymbolPath
        if( (0 -ne $origPath.Length) -and (';' -ne $origPath[ $origPath.Length - 1 ]) )
        {
            $origPath += ';'
        }
        Set-DbgSymbolPath -Path ($origPath + $Path)
    }
} # end .sympath+


function .sympath_minus
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true )]
           [ValidateNotNullOrEmpty()]
           [string] $Path
         )
    begin { }
    end { }
    process
    {
        $origPath = Get-DbgSymbolPath

        if($Path.Contains(";"))
        {
            Write-Error "Path is invalid with ';' seperator."
            return
        }

        if($origPath.Length -gt 0 -and $origPath.Length -ge $Path.Length)
        {
            $nextPath = ""
            $splitStrings = $origPath.Split( ';' )

            if(-not $Path)
            {
                $Path = $splitStrings[$splitStrings.Length - 1]
            }

            for($i = 0; $i -lt $splitStrings.Length; $i++)
            {
                if($Path -ne $splitStrings[$i])
                {
                    $nextPath += $splitStrings[$i] + ";"
                }
            }

            $nextPath = $nextPath.TrimEnd(";")
			
            Set-DbgSymbolPath -Path $nextPath
        }
    }
}

Set-Alias .sympath- .sympath_minus


<#
.SYNOPSIS
    Creates a DbgValueConverterInfo object for a script block that defines a converter for the specified type.
    TODO: more doc
.Link
    Register-DbgValueConverterInfo
#>
function New-DbgValueConverterInfo
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgValueConverterInfo] )]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNullOrEmpty()]
           [string[]] $TypeName,

           [Parameter( Mandatory = $true, Position = 1 )]
           [ValidateNotNull()]
           [ScriptBlock] $Converter,

           [Parameter( Mandatory = $false, Position = 2 )]
           [ValidateNotNull()]
           [string] $SourceScript,

           [Parameter( Mandatory = $false )]
           [switch] $CaptureContext
           # If we want to support C# converters, we could have another parameter set for
           # that.
         )
    begin { }
    end { }
    process
    {
        try
        {
            if( !$PSBoundParameters.ContainsKey( 'SourceScript' ) )
            {
                $SourceScript = (Get-PSCallStack)[1].ScriptName
            }
            foreach( $private:tn in $TypeName )
            {
                $private:sc = New-Object "MS.Dbg.DbgValueScriptConverter" -ArgumentList @( $tn, $Converter, $CaptureContext )
                $private:cdi = New-Object "MS.Dbg.DbgValueConverterInfo" -ArgumentList @( $tn, $sc, $SourceScript )
                $cdi
            }
        }
        finally { }
    }
} # end New-DbgValueConverterInfo


<#
.SYNOPSIS
    Registers one or more DbgValueScriptConverter objects.
.Link
    New-DbgValueConverterInfo
#>
function Register-DbgValueConverterInfo
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNull()]
           [ScriptBlock] $ConvertersProducer
         )
    begin { }
    end { }
    process
    {
        $private:disposable = $null
        try
        {
            # If any entries need to capture context (variables and functions), we'll only
            # let them capture stuff defined in the $ConvertersProducer script block and
            # below.
            $disposable = [MS.Dbg.DbgProvider]::SetContextStartMarker()
            [int] $private:numConverters = 0
            # Q. Why do we need to execute the $ConvertersProducer in the context of the
            #    Debugger.psm1 module?
            #
            # A: If we don't, then any converter that wants to -CaptureContext won't work.
            #    This is because Get-PsContext is going to end up executing in the
            #    Debugger.psm1 context, and it won't be able to "see out of" that context.
            & ($ExecutionContext.SessionState.Module) $ConvertersProducer | %{
                if( $_ -is [MS.Dbg.DbgValueConverterInfo] )
                {
                    $numConverters++
                    [MS.Dbg.DbgValueConversionManager]::RegisterConverter( $_ )
                }
                else
                {
                    Write-Warning "The `$ConvertersProducer ScriptBlock produced some output that was not DbgValueConverterInfo ($($_))."
                }
            }
            if( 0 -eq $numConverters )
            {
                Write-Warning "No converters produced."
            }
        }
        finally
        {
            if( $disposable ) { $disposable.Dispose() }
        }
    }
} # end Register-DbgValueConverterInfo


<#
.SYNOPSIS
    Allows you to write a collection object to the pipeline without unrolling it.
#>
function Write-Collection
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )] # Can't pipeline; would defeat the purpose
           [object] $Collection,

           [Parameter( Mandatory = $false )]
           [switch] $Unroll
         )

    begin { }
    end { }
    process
    {
        try
        {
            $PSCmdlet.WriteObject( $Collection, $Unroll )
        }
        finally { }
    }
} # end Write-Collection


function Invoke-InAlternateScope
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromRemainingArguments = $true )]
           [ValidateNotNullOrEmpty()]
           [ScriptBlock[]] $ScriptBlock,

           [Parameter( Mandatory = $false,
                       Position = 1 )]
           [object[]] $Arguments,

           [Parameter( Mandatory = $false, Position = 2 )]
           [ValidateNotNull()]
           [System.Management.Automation.PSModuleInfo] $ScopingModule,

           [Parameter()]
           [switch] $UseIsolatedChildScope
         )

    begin
    {
        try
        {
            if( !$ScopingModule )
            {
                $ScopingModule = New-Module { }
            }
        }
        finally { }
    }

    process
    {
        try
        {
            if( $UseIsolatedChildScope )
            {
                $ScriptBlock | % { & $ScopingModule $_ @Arguments }
            }
            else
            {
                $ScriptBlock | % { . $ScopingModule $_ @Arguments }
            }

            # Here is a really awkward alternative. Not only is it awkward, but it
            # means that the child scopes can see $ScriptBlock and $sb. What makes
            # the difference is using '.' versus '&' (dot-sourcing operator versus
            # call operator).
         ## & $ScopingModule {
         ##     param( [ScriptBlock[]] $ScriptBlock )
         ##     foreach( $sb in $ScriptBlock )
         ##     {
         ##         . $sb @Arguments
         ##     }
         ## } $ScriptBlock
        }
        finally { }
    }

    end { }
} # end Invoke-InAlternateScope()


<#
.SYNOPSIS
    Use this wherever you use a [MS.Dbg.Commands.RangeTransformation()] attribute.
#>
function GetByteLengthFromRangeArgument
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ref] [UInt64] $AddressRef,

           [Parameter( Mandatory = $true, Position = 1 )]
           [object] $CountOrEndAddress,

           [Parameter( Mandatory = $true, Position = 3 )]
           [UInt32] $ElementSize
         )
    process
    {
        try
        {
            [UInt32] $lengthInBytes = 0
            if( $CountOrEndAddress.IsNegative )
            {
                #Write-Host "case 1" -Fore Magenta
                [System.Diagnostics.Debug]::Assert( $CountOrEndAddress.HasLengthPrefix )
                $lengthInBytes = $CountOrEndAddress * $ElementSize
                $AddressRef.Value = $AddressRef.Value - $lengthInBytes
            }
            elseif( !$CountOrEndAddress.HasLengthPrefix )
            {
              # [Console]::WriteLine( "GetByteLengthFromRangeArgument:  `$AddressRef.Value: {0}", $AddressRef.Value )
              # [Console]::WriteLine( "GetByteLengthFromRangeArgument: `$CountOrEndAddress: {0}", $CountOrEndAddress )
              # [Console]::WriteLine( "GetByteLengthFromRangeArgument:          Subtracted: {0}", ($CountOrEndAddress - $AddressRef.Value) )
              # [Console]::WriteLine( "GetByteLengthFromRangeArgument:           Condition: {0}", (($CountOrEndAddress - $AddressRef.Value) -lt 256kb) )

                # Is it a count, or is it an end address? This is a pretty simple
                # heuristic, but I bet it mostly works.
                if( ($AddressRef.Value -ne $null) -and
                    ($AddressRef.Value -ne 0) -and
                    ($CountOrEndAddress -gt $AddressRef.Value) -and
                    (($CountOrEndAddress - $AddressRef.Value) -lt 256kb) )
                {
                    #Write-Host "case 2a" -Fore Magenta
                    $lengthInBytes = $CountOrEndAddress - $AddressRef.Value
                }
                elseif( ($CountOrEndAddress -lt 256kb) -and
                        (($CountOrEndAddress * $ElementSize) -lt 256kb) )
                {
                    #Write-Host "case 2b" -Fore Magenta
                    $lengthInBytes = $CountOrEndAddress * $ElementSize
                }
                else
                {
                    throw "The value '$($CountOrEndAddress.ToString( 'x' ))' is too large to be a valid range. If you really want that much, use the size override syntax (`"L?<size>`")."
                }
            }
            else
            {
                #Write-Host "case 3" -Fore Magenta
                # Size already validated by the RangeTransformation attribute as not too long.
                $lengthInBytes = $CountOrEndAddress * $ElementSize
            }
            #Write-Host "returning $lengthInBytes" -Fore Green
            return $lengthInBytes
        }
        finally { }
    } # end 'process' block
} # end GetByteLengthFromRangeArgument


<#
.SYNOPSIS
    Gets the current target.
#>
function Get-DbgCurrentTarget
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgUModeProcess] )]
    param()

    process
    {
        try
        {
            return $debugger.GetCurrentTarget()
        }
        finally { }
    } # end 'process' block
} # end Get-DbgCurrentTarget


<#
.Synopsis
    Like the windbg "db" command (Dump Bytes). Includes ASCII characters.

.Link
    Get-Help about_MemoryCommands
#>
function db
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgMemory] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.Commands.RangeTransformation( ElementSize = 1 )]
           [UInt64] $CountOrEndAddress = 128,

           [Parameter( Mandatory = $false )]
           [Alias( 'c' )]
           [UInt32] $NumColumns
         )
    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                if( $PSBoundParameters.ContainsKey( 'CountOrEndAddress' ) )
                {
                    Write-Warning 'Re-dumping an existing DbgMemory object does not support the -CountOrEndAddress parameter.'
                }
                $newMem = $Address.Clone()
                $newMem.DefaultDisplayFormat = 'Bytes'
                $newMem.DefaultDisplayColumns = $NumColumns
                return $newMem
            }

            [UInt32] $lengthInBytes = GetByteLengthFromRangeArgument ([ref] $Address) $CountOrEndAddress -ElementSize 1
            Read-DbgMemory -Address $Address -LengthInBytes $lengthInBytes -DefaultDisplayColumns $NumColumns -DefaultDisplayFormat 'Bytes'
        }
        finally { }
    }
} # end db


<#
.Synopsis
    Like the windbg "da" command (Dump ASCII): interprets memory as an ASCII string.

.Description
    If you want to get an actual System.String object (instead of just an object representing a chunk of memory, with an ASCII string display), use the 'dsz' command instead.

.Link
    dsz
    dwsz
    du
    Get-Help about_MemoryCommands
#>
function da
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgMemory] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.Commands.RangeTransformation( ElementSize = 1 )]
           [UInt64] $CountOrEndAddress = 704, # It's a strange default, but it matches windbg

           [Parameter( Mandatory = $false )]
           [Alias( 'c' )]
           [UInt32] $NumColumns
         )
    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                if( $PSBoundParameters.ContainsKey( 'CountOrEndAddress' ) )
                {
                    Write-Warning 'Re-dumping an existing DbgMemory object does not support the -CountOrEndAddress parameter.'
                }
                $newMem = $Address.Clone()
                $newMem.DefaultDisplayFormat = 'AsciiOnly'
                $newMem.DefaultDisplayColumns = $NumColumns
                return $newMem
            }

            [UInt32] $lengthInBytes = GetByteLengthFromRangeArgument ([ref] $Address) $CountOrEndAddress -ElementSize 1
            Read-DbgMemory -Address $Address -LengthInBytes $lengthInBytes -DefaultDisplayColumns $NumColumns -DefaultDisplayFormat 'AsciiOnly'
        }
        finally { }
    }
} # end da


<#
.Synopsis
    Like the windbg "du" command.

.Description
    If you want to get an actual System.String object (instead of just an object representing a chunk of memory, with a string display), use the 'dwsz' command instead.

.Link
    dwsz
    dsz
    da
    Get-Help about_MemoryCommands
#>
function du
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgMemory] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.Commands.RangeTransformation( ElementSize = 2 )]
           [UInt64] $CountOrEndAddress = 704, # It's a strange default, but it matches windbg

           [Parameter( Mandatory = $false )]
           [Alias( 'c' )]
           [UInt32] $NumColumns
         )
    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                if( $PSBoundParameters.ContainsKey( 'CountOrEndAddress' ) )
                {
                    Write-Warning 'Re-dumping an existing DbgMemory object does not support the -CountOrEndAddress parameter.'
                }
                $newMem = $Address.Clone()
                $newMem.DefaultDisplayFormat = 'UnicodeOnly'
                $newMem.DefaultDisplayColumns = $NumColumns
                return $newMem
            }

            [UInt32] $lengthInBytes = GetByteLengthFromRangeArgument ([ref] $Address) $CountOrEndAddress -ElementSize 2
            Read-DbgMemory -Address $Address -LengthInBytes $lengthInBytes -DefaultDisplayColumns $NumColumns -DefaultDisplayFormat 'UnicodeOnly'
        }
        finally { }
    }
} # end du


<#
.Synopsis
    Similar to the windbg "du" command, but it reads until it finds a terminating NULL, and instead of returning a chunk of memory, it returns just a plain System.String.
.Description
    The advantage over "du" is that it returns a System.String object, instead of a DbgMemory object. The advantage over $Debugger.ReadMemAs_wszString() is the AddressTransformationAttribute that lets you pass hex values in a more idiomatic way (without leading 0x), plus it's much shorter.

.Link
    du
    da
    dsz
    Get-Help about_MemoryCommands
#>
function dwsz
{
    [CmdletBinding()]
    [OutputType( [System.String] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [UInt32] $MaxCharacters = 2048
         )
    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                $mem = $Address

                # Find the null terminator.
                [int] $i = 0
                for( $i = 0; ($i -lt $mem.Words.Count) -and ($i -lt $MaxCharacters); $i++ )
                {
                    if( 0 -eq $mem.Words[ $i ] )
                    {
                        break
                    }
                }

                return [System.Text.Encoding]::Unicode.GetString( $mem.Bytes, 0, ($i * 2) )
            }

            # TODO: Support existing DbgMemory objects here
            $Debugger.ReadMemAs_wszString( $Address, $MaxCharacters )
        }
        finally { }
    }
} # end dwsz


<#
.Synopsis
    Similar to the windbg "da" command (Dump ASCII), but it reads until it finds a terminating NULL, and instead of returning a chunk of memory, it returns just a plain System.String.
.Description
    The advantage over "da" is that it returns a System.String object, instead of a DbgMemory object. The advantage over $Debugger.ReadMemAs_szString() is the AddressTransformationAttribute that lets you pass hex values in a more idiomatic way (without leading 0x), plus it's much shorter.

.Link
    da
    du
    dwsz
    Get-Help about_MemoryCommands
#>
function dsz
{
    [CmdletBinding()]
    [OutputType( [System.String] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [UInt32] $MaxCharacters = 2048
         )
    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                $mem = $Address

                # Find the null terminator.
                [int] $i = 0
                for( $i = 0; ($i -lt $mem.Bytes.Count) -and ($i -lt $MaxCharacters); $i++ )
                {
                    if( 0 -eq $mem.Bytes[ $i ] )
                    {
                        break
                    }
                }

                return [System.Text.Encoding]::UTF8.GetString( $mem.Bytes, 0, $i )
            }

            # TODO: Support existing DbgMemory objects here
            $Debugger.ReadMemAs_szString( $Address, $MaxCharacters )
        }
        finally { }
    }
} # end dsz


<#
.Synopsis
    Like the windbg "dw" command (Dump WORDs). Also supports "dW", which dumps WORDs along with ASCII characters.

.Link
    Get-Help about_MemoryCommands
#>
function dw
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgMemory] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.Commands.RangeTransformation( ElementSize = 2 )]
           [UInt64] $CountOrEndAddress = 64,

           [Parameter( Mandatory = $false )]
           [Alias( 'c' )]
           [UInt32] $NumColumns
         )
    process
    {
        try
        {
            [MS.Dbg.DbgMemoryDisplayFormat] $fmt = [MS.Dbg.DbgMemoryDisplayFormat]::Words
            # N.B. Case-sensitive!
            if( 0 -eq [string]::Compare( $PSCmdlet.MyInvocation.InvocationName, 'dW', [System.StringComparison]::Ordinal ) )
            {
                $fmt = [MS.Dbg.DbgMemoryDisplayFormat]::WordsWithAscii
            }

            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                if( $PSBoundParameters.ContainsKey( 'CountOrEndAddress' ) )
                {
                    Write-Warning 'Re-dumping an existing DbgMemory object does not support the -CountOrEndAddress parameter.'
                }
                $newMem = $Address.Clone()
                $newMem.DefaultDisplayFormat = $fmt
                $newMem.DefaultDisplayColumns = $NumColumns
                return $newMem
            }

            [UInt32] $lengthInBytes = GetByteLengthFromRangeArgument ([ref] $Address) $CountOrEndAddress -ElementSize 2
            Read-DbgMemory -Address $Address -LengthInBytes $lengthInBytes -DefaultDisplayColumns $NumColumns -DefaultDisplayFormat $fmt
        }
        finally { }
    }
} # end dw


<#
.Synopsis
    Like the windbg "dd" command (Dump DWORDs).

.Link
    Get-Help about_MemoryCommands
#>
function dd
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgMemory] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.Commands.RangeTransformation( ElementSize = 4 )]
           [UInt64] $CountOrEndAddress = 32,

           [Parameter( Mandatory = $false )]
           [Alias( 'c' )]
           [UInt32] $NumColumns
         )
    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                if( $PSBoundParameters.ContainsKey( 'CountOrEndAddress' ) )
                {
                    Write-Warning 'Re-dumping an existing DbgMemory object does not support the -CountOrEndAddress parameter.'
                }
                $newMem = $Address.Clone()
                $newMem.DefaultDisplayFormat = 'DWords'
                $newMem.DefaultDisplayColumns = $NumColumns
                return $newMem
            }

            [UInt32] $lengthInBytes = GetByteLengthFromRangeArgument ([ref] $Address) $CountOrEndAddress -ElementSize 4
            Read-DbgMemory -Address $Address -LengthInBytes $lengthInBytes -DefaultDisplayColumns $NumColumns -DefaultDisplayFormat 'DWords'
        }
        finally { }
    }
} # end dd


<#
.Synopsis
    Like the windbg "dyd" command (Dump Binary DWORDs).

.Link
    Get-Help about_MemoryCommands
#>
function dyd
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgMemory] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.Commands.RangeTransformation( ElementSize = 4 )]
           [UInt64] $CountOrEndAddress = 8
         )
    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                if( $PSBoundParameters.ContainsKey( 'CountOrEndAddress' ) )
                {
                    Write-Warning 'Re-dumping an existing DbgMemory object does not support the -CountOrEndAddress parameter.'
                }
                $newMem = $Address.Clone()
                $newMem.DefaultDisplayFormat = 'DWordsWithBits'
                return $newMem
            }

            [UInt32] $lengthInBytes = GetByteLengthFromRangeArgument ([ref] $Address) $CountOrEndAddress -ElementSize 4
            Read-DbgMemory -Address $Address -LengthInBytes $lengthInBytes -DefaultDisplayFormat 'DWordsWithBits'
        }
        finally { }
    }
} # end dyd


<#
.Synopsis
    Like the windbg "dc" command (Dump [DWORDs], with [ASCII] Characters).

.Link
    Get-Help about_MemoryCommands
#>
function dc
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgMemory] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.Commands.RangeTransformation( ElementSize = 4 )]
           [UInt64] $CountOrEndAddress = 32,

           [Parameter( Mandatory = $false )]
           [Alias( 'c' )]
           [UInt32] $NumColumns
         )
    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                if( $PSBoundParameters.ContainsKey( 'CountOrEndAddress' ) )
                {
                    Write-Warning 'Re-dumping an existing DbgMemory object does not support the -CountOrEndAddress parameter.'
                }
                $newMem = $Address.Clone()
                $newMem.DefaultDisplayFormat = 'DWordsWithAscii'
                $newMem.DefaultDisplayColumns = $NumColumns
                return $newMem
            }

            [UInt32] $lengthInBytes = GetByteLengthFromRangeArgument ([ref] $Address) $CountOrEndAddress -ElementSize 4
            Read-DbgMemory -Address $Address -LengthInBytes $lengthInBytes -DefaultDisplayColumns $NumColumns -DefaultDisplayFormat 'DWordsWithAscii'
        }
        finally { }
    }
} # end dc


<#
.Synopsis
    Like the windbg "dq" command (Dump QWORDs).

.Link
    Get-Help about_MemoryCommands
#>
function dq
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgMemory] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.Commands.RangeTransformation( ElementSize = 8 )]
           [UInt64] $CountOrEndAddress = 16,

           [Parameter( Mandatory = $false )]
           [Alias( 'c' )]
           [UInt32] $NumColumns
         )
    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                if( $PSBoundParameters.ContainsKey( 'CountOrEndAddress' ) )
                {
                    Write-Warning 'Re-dumping an existing DbgMemory object does not support the -CountOrEndAddress parameter.'
                }
                $newMem = $Address.Clone()
                $newMem.DefaultDisplayFormat = 'QWords'
                $newMem.DefaultDisplayColumns = $NumColumns
                return $newMem
            }

            [UInt32] $lengthInBytes = GetByteLengthFromRangeArgument ([ref] $Address) $CountOrEndAddress -ElementSize 8
            Read-DbgMemory -Address $Address -LengthInBytes $lengthInBytes -DefaultDisplayColumns $NumColumns -DefaultDisplayFormat 'QWords'
        }
        finally { }
    }
} # end dq


<#
.Synopsis
    Like the windbg "dp" command (Dump Pointers).

.Link
    Get-Help about_MemoryCommands
#>
function dp
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgMemory] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.Commands.RangeTransformation( ElementSize = 0 )]
           [UInt64] $CountOrEndAddress,

           [Parameter( Mandatory = $false )]
           [Alias( 'c' )]
           [UInt32] $NumColumns
         )
    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                if( $PSBoundParameters.ContainsKey( 'CountOrEndAddress' ) )
                {
                    Write-Warning 'Re-dumping an existing DbgMemory object does not support the -CountOrEndAddress parameter.'
                }
                $newMem = $Address.Clone()
                $newMem.DefaultDisplayFormat = 'Pointers'
                $newMem.DefaultDisplayColumns = $NumColumns
                return $newMem
            }

            [int] $elemSize = 4
            if( $Debugger.TargetIs32Bit )
            {
                if( $CountOrEndAddress -eq 0 )
                {
                    $CountOrEndAddress = 32
                }
            }
            else
            {
                $elemSize = 8
                if( $CountOrEndAddress -eq 0 )
                {
                    $CountOrEndAddress = 16
                }
            }
            [UInt32] $lengthInBytes = GetByteLengthFromRangeArgument ([ref] $Address) $CountOrEndAddress -ElementSize $elemSize
            Read-DbgMemory -Address $Address -LengthInBytes $lengthInBytes -DefaultDisplayColumns $NumColumns -DefaultDisplayFormat 'Pointers'
        }
        finally { }
    }
} # end dp


<#
.Synopsis
    Like the windbg "dp" command (Dump Pointers), but also displays characters.

.Link
    Get-Help about_MemoryCommands
#>
function dpc
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgMemory] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.Commands.RangeTransformation( ElementSize = 0 )]
           [UInt64] $CountOrEndAddress,

           [Parameter( Mandatory = $false )]
           [Alias( 'c' )]
           [UInt32] $NumColumns
         )
    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                if( $PSBoundParameters.ContainsKey( 'CountOrEndAddress' ) )
                {
                    Write-Warning 'Re-dumping an existing DbgMemory object does not support the -CountOrEndAddress parameter.'
                }
                $newMem = $Address.Clone()
                $newMem.DefaultDisplayFormat = 'PointersWithAscii'
                $newMem.DefaultDisplayColumns = $NumColumns
                return $newMem
            }

            [int] $elemSize = 4
            if( $Debugger.TargetIs32Bit )
            {
                if( $CountOrEndAddress -eq 0 )
                {
                    $CountOrEndAddress = 32
                }
            }
            else
            {
                $elemSize = 8
                if( $CountOrEndAddress -eq 0 )
                {
                    $CountOrEndAddress = 16
                }
            }
            [UInt32] $lengthInBytes = GetByteLengthFromRangeArgument ([ref] $Address) $CountOrEndAddress -ElementSize $elemSize
            Read-DbgMemory -Address $Address -LengthInBytes $lengthInBytes -DefaultDisplayColumns $NumColumns -DefaultDisplayFormat 'PointersWithAscii'
        }
        finally { }
    }
} # end dpc


<#
.Synopsis
    Like the windbg "dps" command (Dump Pointers, with Symbols).

.Link
    Get-Help about_MemoryCommands
#>
function dps
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgMemory] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.Commands.RangeTransformation( ElementSize = 0 )]
           [UInt64] $CountOrEndAddress = 16,

           [Parameter( Mandatory = $false )]
           [Alias( 'c' )]
           [UInt32] $NumColumns
         )
    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                if( $PSBoundParameters.ContainsKey( 'CountOrEndAddress' ) )
                {
                    Write-Warning 'Re-dumping an existing DbgMemory object does not support the -CountOrEndAddress parameter.'
                }
                $newMem = $Address.Clone()
                $newMem.DefaultDisplayFormat = 'PointersWithSymbols'
                $newMem.DefaultDisplayColumns = $NumColumns
                return $newMem
            }

            [int] $elemSize = 4
            if( $Debugger.TargetIs32Bit )
            {
                if( $CountOrEndAddress -eq 0 )
                {
                    $CountOrEndAddress = 32
                }
            }
            else
            {
                $elemSize = 8
                if( $CountOrEndAddress -eq 0 )
                {
                    $CountOrEndAddress = 16
                }
            }

            [UInt32] $lengthInBytes = GetByteLengthFromRangeArgument ([ref] $Address) $CountOrEndAddress -ElementSize $elemSize
            Read-DbgMemory -Address $Address -LengthInBytes $lengthInBytes -DefaultDisplayColumns $NumColumns -DefaultDisplayFormat 'PointersWithSymbols'
        }
        finally { }
    }
} # end dps


<#
.Synopsis
    Like the windbg "dps" command (Dump Pointers with Symbols), but also displays ASCII characters a la dc.

.Link
    Get-Help about_MemoryCommands
#>
function dpsc
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgMemory] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation( DbgMemoryPassThru = $true )]
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.Commands.RangeTransformation( ElementSize = 0 )]
           [UInt64] $CountOrEndAddress = 16,

           [Parameter( Mandatory = $false )]
           [Alias( 'c' )]
           [UInt32] $NumColumns
         )
    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgMemory] )
            {
                if( $PSBoundParameters.ContainsKey( 'CountOrEndAddress' ) )
                {
                    Write-Warning 'Re-dumping an existing DbgMemory object does not support the -CountOrEndAddress parameter.'
                }
                $newMem = $Address.Clone()
                $newMem.DefaultDisplayFormat = 'PointersWithSymbolsAndAscii'
                $newMem.DefaultDisplayColumns = $NumColumns
                return $newMem
            }

            [int] $elemSize = 4
            if( $Debugger.TargetIs32Bit )
            {
                if( $CountOrEndAddress -eq 0 )
                {
                    $CountOrEndAddress = 32
                }
            }
            else
            {
                $elemSize = 8
                if( $CountOrEndAddress -eq 0 )
                {
                    $CountOrEndAddress = 16
                }
            }

            [UInt32] $lengthInBytes = GetByteLengthFromRangeArgument ([ref] $Address) $CountOrEndAddress -ElementSize $elemSize
            Read-DbgMemory -Address $Address -LengthInBytes $lengthInBytes -DefaultDisplayColumns $NumColumns -DefaultDisplayFormat 'PointersWithSymbolsAndAscii'
        }
        finally { }
    }
} # end dpsc


<#
.Synopsis
    Similar to the windbg "d" command: repeats the most recent memory dumping command.

.Description
    When a memory-dumping command is repeated, it continues to dump the next chunk of memory. Note that unlike windbg, it does not repeat other commands that also happen to start with "d", like "dv".

.Link
    Get-Help about_MemoryCommands
#>
function d
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgMemory] )]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation()]
           # TODO: The workaround for INTf423c7e8 is to relax the parameter
           # type (to "object").
           #[UInt64] $Address,
           [object] $Address,

           [Parameter( Mandatory = $false )]
           [Alias( 'c' )]
           [UInt32] $NumColumns
         )
    process
    {
        try
        {
            # Length of 0 means "default size".
            Read-DbgMemory -Address $Address
        }
        finally { }
    }
} # end d


function script:_PromptForNewMemoryValue
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [UInt64] $Address,

           [Parameter( Mandatory = $true, Position = 1 )]
           [ScriptBlock] $WriteNewVal,

           [Parameter( Mandatory = $true, Position = 2 )]
           [int] $Stride,

           [Parameter( Mandatory = $true, Position = 3 )]
           [ScriptBlock] $ProduceOldVal
         )

    process
    {
        try
        {
            [UInt64] $curAddr = $Address

            [string] $entry = ''
            do
            {
                if( $entry )
                {
                    $entry = $entry.Trim()
                    if( !$entry )
                    {
                        break
                    }
                    [UInt64] $ulong = 0
                    if( ![MS.Dbg.DbgProvider]::TryParseHexOrDecimalNumber( $entry, ([ref] $ulong) ) )
                    {
                        Write-Error "Did not understand input: $entry"
                        break
                    }

                    & $WriteNewVal $curAddr $ulong
                    $curAddr += $Stride
                }

                $strOldVal = (& $ProduceOldVal $curAddr) + ' < '

                $cs = (New-ColorString).Append( (Format-DbgAddress $curAddr) ).
                                        Append( ' ' ).
                                        AppendPushPopFg( [ConsoleColor]::DarkRed, $strOldVal ).
                                        AppendPushFg( [ConsoleColor]::Yellow ).
                                        Append( '> ' )

                $host.UI.Write( $cs.ToString( $HostSupportsColor ) )
                $entry = $host.UI.ReadLine()
                $host.UI.Write( ((New-ColorString).AppendPop()).ToString( $HostSupportsColor ) )
            } while( $entry )
        }
        finally { }
    } # end 'process' block
} # end _PromptForNewMemoryValue()


<#
.Synopsis
    Similar to the windbg "eb" command ("Enter Bytes"): Writes byte values into memory.
    See important note about argument parsing in the full help description.

.Description
    IMPORTANT NOTE ABOUT ARGUMENT PARSING:

    PowerShell parses numbers as decimal (base 10), but debugger users are accustomed to
    using hexadecimal (base 16). This can lead to problems.

    DbgShell uses some tricks to allow you to run this command very simalarly to how you
    would in a debugger. Notice that the default parameter set is the
    'HexStringsParamSet', which can be bound positionally and can gather its value from
    remaining arguments. That lets you run commands like:

       eb $rsp 90 90 90

    Just like you would in windbg. This command is equivalent:

       eb $rsp 0x90 0x90 0x90

    However, say you have a byte[] in $myBytes. You might try something like:

       eb $rsp $myBytes  # <-- BAD

    But that won't work (you'll get an error). If you are passing actual data objects (as
    opposed to string literals) on the command line, you need to use the -Value parameter
    set:

       eb $rsp -Value $myBytes

    Similarly for array literals, but remember that PowerShell parses the array literal,
    not the DbgShell command:

       eb $rsp @( 90, 90, 90 )  # <-- BAD, will fail with an error
       eb $rsp -Value @( 90, 90, 90 )  # <-- Will not fail, but those are 0n90!

.Link
    Get-Help about_MemoryCommands
#>
function eb
{
    [CmdletBinding( DefaultParameterSetName = 'HexStringsParamSet' )]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation()]
           # TODO: The workaround for INTf423c7e8 is to relax the parameter
           # type (to "object").
           #[UInt64] $Address,
           [object] $Address,

           [Parameter( Mandatory = $false,
                       Position = 1,
                       ValueFromRemainingArguments = $true,
                       ParameterSetName = 'HexStringsParamSet' )]
           [string[]] $HexStrings,

           [Parameter( Mandatory = $false,
                       ParameterSetName = 'ObjectParamSet' )]
           [MS.Dbg.Commands.AddressTransformation( AllowList = $true )]
           [object] $Value
         )
    process
    {
        try
        {
            $curAddr = $Address

            if( ($PSCmdlet.ParameterSetName -eq 'HexStringsParamSet') -and
                (($HexStrings -eq $null) -or ($HexStrings.Count -eq 0)) )
            {
                #
                # Interactive mode: we'll allow the user to input one value at
                # a time, stopping at the first empty entry.
                #

                $writeNewVal = {
                    param( $curAddr, $ulong )

                    $valueAs8bits = [MS.Dbg.DbgProvider]::ConvertToUInt8( $ulong )
                    Write-DbgMemory -Address $curAddr -Bytes $valueAs8bits
                }

                $produceOldVal = {
                    param( $curAddr )

                    $curVal = (db $curAddr L1)[ 0 ]
                    return (ZeroPad $curVal.ToString( 'x' ) 2)
                }

                _PromptForNewMemoryValue $curAddr -WriteNewVal $writeNewVal -Stride 1 -ProduceOldVal $produceOldVal

                return
            }

            if( $Value )
            {
                # The [AddressTransformation()] attribute already did the work.
                $asUlongs = $Value
            }
            else
            {
                $asUlongs = [MS.Dbg.Commands.AddressTransformationAttribute]::Transform(
                                $ExecutionContext,
                                $null,  # dbgProviderPath
                                $true,  # skipGlobalSymbolTest
                                $false, # throwOnFailure
                                $false, # dbgMemoryPassThru
                                $true,  # allowList
                                ([object] $HexStrings) )

                if( $null -eq $asUlongs )
                {
                    Write-Error "I didn't understand the input: $($HexStrings)"
                    return
                }
            }

            foreach( $v in $asUlongs )
            {
                # The AddressTransformation will give us back a UInt64 (or a UInt64[]). We
                # will need to convert it to 8 bits, which will throw an exception if
                # that's not possible (such as if the high bits are set).

                $valueAs8bits = [MS.Dbg.DbgProvider]::ConvertToUInt8( $v )
                Write-DbgMemory -Address $curAddr -Bytes $valueAs8bits
                $curAddr += 1
            }
        }
        finally { }
    }
} # end eb


<#
.Synopsis
    Similar to the windbg "ed" command ("Enter DWORDs"): writes DWORD values into memory.
    See important note about argument parsing in the full help description.

.Description
    IMPORTANT NOTE ABOUT ARGUMENT PARSING:

    PowerShell parses numbers as decimal (base 10), but debugger users are accustomed to
    using hexadecimal (base 16). This can lead to problems.

    DbgShell uses some tricks to allow you to run this command very simalarly to how you
    would in a debugger. Notice that the default parameter set is the
    'HexStringsParamSet', which can be bound positionally and can gather its value from
    remaining arguments. That lets you run commands like:

       ed $rsp 90 90 90

    Just like you would in windbg. This command is equivalent:

       ed $rsp 0x90 0x90 0x90

    However, say you have a byte[] in $myBytes. You might try something like:

       ed $rsp $myBytes  # <-- BAD

    But that won't work (you'll get an error). If you are passing actual data objects (as
    opposed to string literals) on the command line, you need to use the -Value parameter
    set:

       ed $rsp -Value $myBytes

    Similarly for array literals, but remember that PowerShell parses the array literal,
    not the DbgShell command:

       ed $rsp @( 90, 90, 90 )  # <-- BAD, will fail with an error
       ed $rsp -Value @( 90, 90, 90 )  # <-- Will not fail, but those are 0n90!

.Link
    Get-Help about_MemoryCommands
#>
function ed
{
    [CmdletBinding( DefaultParameterSetName = 'HexStringsParamSet' )]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation()]
           # TODO: The workaround for INTf423c7e8 is to relax the parameter
           # type (to "object").
           #[UInt64] $Address,
           [object] $Address,

           [Parameter( Mandatory = $false,
                       Position = 1,
                       ValueFromRemainingArguments = $true,
                       ParameterSetName = 'HexStringsParamSet' )]
           [string[]] $HexStrings,

           [Parameter( Mandatory = $false,
                       ParameterSetName = 'ObjectParamSet' )]
           [MS.Dbg.Commands.AddressTransformation( AllowList = $true )]
           [object] $Value
         )
    process
    {
        try
        {
            $curAddr = $Address

            if( ($PSCmdlet.ParameterSetName -eq 'HexStringsParamSet') -and
                (($HexStrings -eq $null) -or ($HexStrings.Count -eq 0)) )
            {
                #
                # Interactive mode: we'll allow the user to input one value at
                # a time, stopping at the first empty entry.
                #

                $writeNewVal = {
                    param( $curAddr, $ulong )

                    $valueAs32Bits = [MS.Dbg.DbgProvider]::ConvertToUInt32( $ulong )
                    Write-DbgMemory -Address $curAddr -DWords $valueAs32Bits
                }

                $produceOldVal = {
                    param( $curAddr )

                    $curVal = (dd $curAddr L1)[ 0 ]
                    return (ZeroPad $curVal.ToString( 'x' ) 8)
                }

                _PromptForNewMemoryValue $curAddr -WriteNewVal $writeNewVal -Stride 4 -ProduceOldVal $produceOldVal

                return
            }

            if( $Value )
            {
                # The [AddressTransformation()] attribute already did the work.
                $asUlongs = $Value
            }
            else
            {
                $asUlongs = [MS.Dbg.Commands.AddressTransformationAttribute]::Transform(
                                $ExecutionContext,
                                $null,  # dbgProviderPath
                                $true,  # skipGlobalSymbolTest
                                $false, # throwOnFailure
                                $false, # dbgMemoryPassThru
                                $true,  # allowList
                                ([object] $HexStrings) )

                if( $null -eq $asUlongs )
                {
                    Write-Error "I didn't understand the input: $($HexStrings)"
                    return
                }
            }

            foreach( $v in $asUlongs )
            {
                # The AddressTransformation will give us back a UInt64 (or a UInt64[]). We will
                # need to convert it to 32 bits, which will throw an exception if that's not
                # possible (such as if the high bits are set).

                $valueAs32bits = [MS.Dbg.DbgProvider]::ConvertToUInt32( $v )
                Write-DbgMemory -Address $curAddr -DWords $valueAs32bits
                $curAddr += 4
            }
        }
        finally { }
    }
} # end ed


<#
.Synopsis
    Similar to the windbg "eq" command ("Enter QWORDs"): writes QWORD values into memory.
    See important note about argument parsing in the full help description.

.Description
    IMPORTANT NOTE ABOUT ARGUMENT PARSING:

    PowerShell parses numbers as decimal (base 10), but debugger users are accustomed to
    using hexadecimal (base 16). This can lead to problems.

    DbgShell uses some tricks to allow you to run this command very simalarly to how you
    would in a debugger. Notice that the default parameter set is the
    'HexStringsParamSet', which can be bound positionally and can gather its value from
    remaining arguments. That lets you run commands like:

       eq $rsp 90 90 90

    Just like you would in windbg. This command is equivalent:

       eq $rsp 0x90 0x90 0x90

    However, say you have a byte[] in $myBytes. You might try something like:

       eq $rsp $myBytes  # <-- BAD

    But that won't work (you'll get an error). If you are passing actual data objects (as
    opposed to string literals) on the command line, you need to use the -Value parameter
    set:

       eq $rsp -Value $myBytes

    Similarly for array literals, but remember that PowerShell parses the array literal,
    not the DbgShell command:

       eq $rsp @( 90, 90, 90 )  # <-- BAD, will fail with an error
       eq $rsp -Value @( 90, 90, 90 )  # <-- Will not fail, but those are 0n90!

.Link
    Get-Help about_MemoryCommands
#>
function eq
{
    [CmdletBinding( DefaultParameterSetName = 'HexStringsParamSet' )]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation()]
           # TODO: The workaround for INTf423c7e8 is to relax the parameter
           # type (to "object").
           #[UInt64] $Address,
           [object] $Address,

           [Parameter( Mandatory = $false,
                       Position = 1,
                       ValueFromRemainingArguments = $true,
                       ParameterSetName = 'HexStringsParamSet' )]
           [string[]] $HexStrings,

           [Parameter( Mandatory = $false,
                       ParameterSetName = 'ObjectParamSet' )]
           [MS.Dbg.Commands.AddressTransformation( AllowList = $true )]
           [object] $Value
         )
    process
    {
        try
        {
            $curAddr = $Address

            if( ($PSCmdlet.ParameterSetName -eq 'HexStringsParamSet') -and
                (($HexStrings -eq $null) -or ($HexStrings.Count -eq 0)) )
            {
                #
                # Interactive mode: we'll allow the user to input one value at
                # a time, stopping at the first empty entry.
                #

                $writeNewVal = {
                    param( $curAddr, $ulong )

                    Write-DbgMemory -Address $curAddr -QWords $ulong
                }

                $produceOldVal = {
                    param( $curAddr )

                    $curVal = (dq $curAddr L1)[ 0 ]
                    return ([MS.Dbg.DbgProvider]::FormatUInt64( $curVal, $true ))
                }

                _PromptForNewMemoryValue $curAddr -WriteNewVal $writeNewVal -Stride 8 -ProduceOldVal $produceOldVal

                return
            }

            if( $Value )
            {
                # The [AddressTransformation()] attribute already did the work.
                $asUlongs = $Value
            }
            else
            {
                $asUlongs = [MS.Dbg.Commands.AddressTransformationAttribute]::Transform(
                                $ExecutionContext,
                                $null,  # dbgProviderPath
                                $true,  # skipGlobalSymbolTest
                                $false, # throwOnFailure
                                $false, # dbgMemoryPassThru
                                $true,  # allowList
                                ([object] $HexStrings) )

                if( $null -eq $asUlongs )
                {
                    Write-Error "I didn't understand the input: $($HexStrings)"
                    return
                }
            }

            foreach( $v in $asUlongs )
            {
                Write-DbgMemory -Address $curAddr -QWords $v
                $curAddr += 8
            }
        }
        finally { }
    }
} # end eq


<#
.Synopsis
    Similar to the windbg "ep" command ("Enter Pointers"): writes Pointer-sized values into memory.
    See important note about argument parsing in the full help description.

.Description
    IMPORTANT NOTE ABOUT ARGUMENT PARSING:

    PowerShell parses numbers as decimal (base 10), but debugger users are accustomed to
    using hexadecimal (base 16). This can lead to problems.

    DbgShell uses some tricks to allow you to run this command very simalarly to how you
    would in a debugger. Notice that the default parameter set is the
    'HexStringsParamSet', which can be bound positionally and can gather its value from
    remaining arguments. That lets you run commands like:

       ep $rsp 90 90 90

    Just like you would in windbg. This command is equivalent:

       ep $rsp 0x90 0x90 0x90

    However, say you have a byte[] in $myBytes. You might try something like:

       ep $rsp $myBytes  # <-- BAD

    But that won't work (you'll get an error). If you are passing actual data objects (as
    opposed to string literals) on the command line, you need to use the -Value parameter
    set:

       ep $rsp -Value $myBytes

    Similarly for array literals, but remember that PowerShell parses the array literal,
    not the DbgShell command:

       ep $rsp @( 90, 90, 90 )  # <-- BAD, will fail with an error
       ep $rsp -Value @( 90, 90, 90 )  # <-- Will not fail, but those are 0n90!

.Link
    Get-Help about_MemoryCommands
#>
function ep
{
    [CmdletBinding( DefaultParameterSetName = 'HexStringsParamSet' )]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation()]
           # TODO: The workaround for INTf423c7e8 is to relax the parameter
           # type (to "object").
           #[UInt64] $Address,
           [object] $Address,

           [Parameter( Mandatory = $false,
                       Position = 1,
                       ValueFromRemainingArguments = $true,
                       ParameterSetName = 'HexStringsParamSet' )]
           [string[]] $HexStrings,

           [Parameter( Mandatory = $false,
                       ParameterSetName = 'ObjectParamSet' )]
           [MS.Dbg.Commands.AddressTransformation( AllowList = $true )]
           [object] $Value
         )
    process
    {
        try
        {
            if( $Debugger.TargetIs32Bit ) {
                ed @PSBoundParameters
            } else {
                eq @PSBoundParameters
            }
        }
        finally { }
    }
} # end ep


<#
.Synopsis
    Detaches the debugger from the current process, similar to .detach, but leaves the target process suspended.
.Link
    '.detach'
    '.kill'
#>
function .abandon()
{
    Disconnect-DbgProcess -LeaveSuspended
}

<#
.Synopsis
    Forcibly ends the current target process and detaches the debugger.
.Link
    '.abandon'
    '.detach'
#>
function .kill()
{
    Disconnect-DbgProcess -Kill
}

<#
.Synopsis
    Disconnects the debugger from the current target process, letting it resume execution.
.Link
    '.abandon'
    '.kill'
#>
function .detach()
{
    Disconnect-DbgProcess
}


<#
.SYNOPSIS
    Sets the state whether debuggers should unwind the stack solely based on frame pointers. (This is known to be useful on ARM/THUMB2.)
.Link
    Get-ForceFramePointerStackWalks
    '.stkwalk_force_frame_pointer'
#>
function Set-ForceFramePointerStackWalks
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [bool] $Enabled )

    begin { }
    end { }
    process
    {
        try
        {
            [string] $p = '1'
            if( !$Enabled )
            {
                Write-Verbose "Disabling 'force frame pointer stackwalks'."
                $p = '0'
            }
            else
            {
                Write-Verbose "Enabling 'force frame pointer stackwalks'."
            }
            $output = Invoke-DbgEng ".stkwalk_force_frame_pointer $p" -OutputPrefix ''
            Write-Verbose $output
        }
        finally { }
    }
} # end Set-ForceFramePointerStackWalks


<#
.SYNOPSIS
    Queries the state whether debuggers should unwind the stack solely based on frame pointers.
.Link
    Set-ForceFramePointerStackWalks
    '.stkwalk_force_frame_pointer'
#>
function Get-ForceFramePointerStackWalks
{
    [CmdletBinding()]
    [OutputType( 'System.Boolean' )]
    param()

    begin { }
    end { }
    process
    {
        try
        {
            $output = Invoke-DbgEng '.stkwalk_force_frame_pointer' -OutputPrefix ''
            Write-Verbose $output
            if( $output.Contains( 'disabled' ) )
            {
                return $false
            }
            else
            {
                if( !$output.Contains( 'enabled' ) )
                {
                    throw "Unexpected output from .stkwalk_force_frame_pointer: $output"
                }
                return $true
            }
        }
        finally { }
    }
} # end Get-ForceFramePointerStackWalks


<#
.SYNOPSIS
    Query or set the state whether debuggers should unwind the stack solely based on frame pointers. (This is known to be useful on ARM/THUMB2.)
.Link
    Get-ForceFramePointerStackWalks
    Set-ForceFramePointerStackWalks
#>
function .stkwalk_force_frame_pointer
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $false, Position = 0 )]
           [object] $Enabled )

    begin { }
    end { }
    process
    {
        try
        {
            if( $null -eq $Enabled )
            {
                Get-ForceFramePointerStackWalks
            }
            else
            {
                if( (0 -eq $Enabled) -or
                    ('0' -eq $Enabled) -or
                    ($false -eq $Enabled) )
                {
                    Set-ForceFramePointerStackWalks -Enabled $false
                }
                else
                {
                    Set-ForceFramePointerStackWalks -Enabled $true
                }
            }
        }
        finally { }
    }
} # end .stkwalk_force_frame_pointer


<#
.SYNOPSIS
    Gets ClrRuntime objects for the current process. (Note that there can be more than one CLR runtime loaded in a process.)
#>
function Get-ClrRuntime
{
    [CmdletBinding()]
    [OutputType( [Microsoft.Diagnostics.Runtime.ClrRuntime] )]
    param()

    begin { }
    end { }
    process
    {
        try
        {
            $Debugger.GetCurrentTarget().ClrRuntimes
        }
        finally { }
    }
} # end Get-ClrRuntime


<#
.SYNOPSIS
    Gets ClrAppDomain objects for the current process.
#>
function Get-ClrAppDomain
{
    [CmdletBinding()]
    [OutputType( [Microsoft.Diagnostics.Runtime.ClrAppDomain] )]
    param( [Parameter( Mandatory = $false )]
           [switch] $IncludeSystemAndShared
         )

    begin { }
    end { }
    process
    {
        try
        {
            foreach( $clrRuntime in Get-ClrRuntime )
            {
                $clrRuntime.AppDomains

                if( $IncludeSystemAndShared )
                {
                    $clrRuntime.SystemDomain
                    $clrRuntime.SharedDomain
                }
            }
        }
        finally { }
    }
} # end Get-ClrAppDomain


<#
.SYNOPSIS
    Gets ClrThread objects for the current process.
#>
function Get-ClrThread
{
    # N.B. these params must be kept in sync with Get-ClrStack

    [CmdletBinding( DefaultParameterSetName = 'NoParamSet' )]
    [OutputType( [Microsoft.Diagnostics.Runtime.ClrThread] )]
    param( [Parameter( Mandatory = $false, ParameterSetName = 'CurrentParamSet' )]
           [switch] $Current,

           [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ParameterSetName = 'ManagedIdParamSet' )]
           [int] $ManagedId,

           [Parameter( Mandatory = $true,
                       Position = 0,
                       ParameterSetName = 'OsIdParamSet' )]
           [MS.Dbg.Commands.AddressTransformationAttribute()] # These are usually provided in hex
           [int] $OsTid,

           [Parameter( Mandatory = $true,
                       Position = 0,
                       ParameterSetName = 'DbgEngIdParamSet' )]
           [int] $DbgEngId
         )

    begin { }
    end { }
    process
    {
        try
        {
            $filter = { $true }

            if( $Current )
            {
                $nativeThread = Get-DbgUModeThreadInfo -Current
                $filter = { $_.OSThreadId -eq $nativeThread.Tid }
            }
            elseif( $PSCmdlet.ParameterSetName -eq 'ManagedIdParamSet' )
            {
                $filter = { $_.ManagedThreadId -eq $ManagedId }
            }
            elseif( $PSCmdlet.ParameterSetName -eq 'OsIdParamSet' )
            {
                $nativeThread = Get-DbgUModeThreadInfo -SystemId $OsTid -ErrorAction Ignore
                if( $null -eq $nativeThread )
                {
                    Write-Warning "No current native thread with system ID $($OsTid)."
                    # We don't return here. The CLR may still have a record of
                    # a managed thread that corresponds to a now-gone OS
                    # thread.
                }
                $filter = { $_.OSThreadId -eq $OsTid }
            }
            elseif( $PSCmdlet.ParameterSetName -eq 'DbgEngIdParamSet' )
            {
                $nativeThread = Get-DbgUModeThreadInfo -DebuggerId $DbgEngId -ErrorAction Ignore
                if( $null -eq $nativeThread )
                {
                    Write-Error "No thread with debugger ID $($DbgEngId)."
                    return
                }
                $filter = { $_.OSThreadId -eq $nativeThread.Tid }
            }

            foreach( $clrRuntime in Get-ClrRuntime )
            {
                $clrRuntime.Threads | where $filter
            }
        }
        finally { }
    }
} # end Get-ClrThread


<#
.SYNOPSIS
    Gets a set of ClrStackFrame objects for the current thread. (The default formatting for ClrStackFrame objects is a table format, so this will look like a managed stack trace.)
#>
function Get-ClrStack
{
    # N.B. these params must be kept in sync with Get-ClrThread

    [CmdletBinding( DefaultParameterSetName = 'CurrentParamSet' )]
    [OutputType( [Microsoft.Diagnostics.Runtime.ClrStackFrame[]] )]
    param( [Parameter( Mandatory = $false, ParameterSetName = 'AllParamSet' )]
           [switch] $All,

           [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ParameterSetName = 'ManagedIdParamSet' )]
           [int] $ManagedId,

           [Parameter( Mandatory = $true,
                       Position = 0,
                       ParameterSetName = 'OsIdParamSet' )]
           [MS.Dbg.Commands.AddressTransformationAttribute()] # These are usually provided in hex
           [int] $OsTid,

           [Parameter( Mandatory = $true,
                       Position = 0,
                       ParameterSetName = 'DbgEngIdParamSet' )]
           [int] $DbgEngId
         )

    begin { }
    end { }
    process
    {
        try
        {
            # We can't seem to access $PSBoundParameters when in a child pipeline, so we
            # need to save them to another variable here:
            $psb = $PSBoundParameters

            [ScriptBlock] $addtlPerFrameFilter = { $_ }

            # Rather than dot-source this scriptblock right here, we need to assign it to
            # a variable and then dot-source that. (Workaround for a PS bug, which caused
            # PS-native formatting data to get dropped and constantly reloaded.)

            $thisCrazySb = {
                if( $All )
                {
                    # This is pretty awkward, but...
                    #
                    # I'd like to keep the output of this function as just pure
                    # ClrStackFrame objects. But if we are spitting out frames for
                    # multiple threads in one go, they'll all get formatted into one giant
                    # table, which doesn't make sense.
                    #
                    # What we want is for the frames to be grouped by thread (-GroupBy).
                    # But there's trouble with that...
                    #
                    #    1) The ClrStackFrame objects don't have a thread property by
                    #       which they can be grouped.
                    #
                    #    2) We don't want the default table view for a single thread's
                    #       stack to have thread spew.
                    #
                    # I originally planned to get around this by:
                    #
                    #    1) attaching a thread property to each frame, and
                    #
                    #    2) attaching an object-specific default view which includes the
                    #       -GroupBy info.
                    #
                    # (ONLY for the case where we are showing stacks from multiple
                    # threads.)
                    #
                    # But there was trouble with that: attaching stuff to a ClrStackFrame
                    # object actually attaches it to the PSObject that wraps it, and the
                    # PSObject for a given ClrStackFrame object "follows" it. That is, if
                    # we later call Get-ClrStack for just a single thread, the frames will
                    # /still/ have the extra stuff attached to them. We don't want that.
                    #
                    # One idea to get around the problem was to copy the PSObject
                    # (PSObject.Copy()). But that only works if the wrapped object is
                    # ICloneable, and ClrStackFrame isn't (and it didn't seem like a
                    # reasonable request for ClrMd).
                    #
                    # Another idea to get around it was to wrap the ClrStackFrame in a
                    # dynamic object. That would work fine for dynamic binding
                    # scenarios... but the alternate formatting engine does not always do
                    # dynamic binding (for instance, with property columns, it just uses
                    # the column name to look up the property in the PSObject.Properties
                    # collection).
                    #
                    # Another idea was to just create a new custom PSObject with
                    # properties mirroring the ClrStackFrame. But that had a problem, too:
                    # the formatting view for ClrStackFrame accesses some properties using
                    # "method syntax" (like "$_.get_InstructionPointer()") (this is better
                    # for exception behavior).
                    #
                    # So I've thrown in the towel and just created a separate class,
                    # ClrStackFrameCloneable.
                    #
                    # Now that we have a different type, we don't even really need the
                    # whole WithGroupBy/Set-AltFormatDefaultViewDef stuff; we could just
                    # have a view that targets that type specifically. *sigh* But then I
                    # wouldn't have any code to exercise those new formatting features. :/
                    #
                    $fmt = (Get-AltTypeFormatEntry -TypeName 'Microsoft.Diagnostics.Runtime.ClrStackFrame' -FormatInfoType ([MS.Dbg.Formatting.AltTableViewDefinition])).ViewDefinitionInfo.ViewDefinition

                    $fmt = $fmt.WithGroupBy( 'Thread', <# produceGroupByHeader #> {
                        $_ | Format-AltSingleLine
                    } )

                    $addtlPerFrameFilter = {
                        # We don't want the added properties to be permanently "stuck" to
                        # the ClrStackFrame objects, so we need to copy them. If
                        # ClrStackFrame was ICloneable, we could just call PSObject.Copy()
                        # on it, but I don't feel like trying to justify that change.

                        $obj = New-Object 'MS.Dbg.ClrStackFrameCloneable' -Arg @( $_ )
                        Set-AltFormatDefaultViewDef -ForObject $obj -FormatInfo $fmt
                        return $obj
                    }

                    Get-ClrThread
                }
                elseif( $PSCmdlet.ParameterSetName -eq 'CurrentParamSet' )
                {
                    Get-ClrThread -Current
                }
                else
                {
                    Get-ClrThread @psb
                }

            }

            . $thisCrazySb | ForEach-Object {  # <-- Pipe!

                $thread = $_

                $_.StackTrace | ForEach-Object $addtlPerFrameFilter
            }
        }
        finally { }
    }
} # end Get-ClrStack


function Dbg:
{
    Set-Location Dbg:
}


Set-Alias ide Invoke-DbgEng

Set-Alias Get-Breakpoint Get-DbgBreakpoint
Set-Alias Set-Breakpoint Set-DbgBreakpoint
Set-Alias Remove-Breakpoint Remove-DbgBreakpoint
Set-Alias Enable-Breakpoint Enable-DbgBreakpoint
Set-Alias Disable-Breakpoint Disable-DbgBreakpoint
Set-Alias bl Get-DbgBreakpoint
Set-Alias be Enable-DbgBreakpoint
Set-Alias bd Disable-DbgBreakpoint
Set-Alias bc Remove-DbgBreakpoint
Set-Alias bp Set-DbgBreakpoint

Set-Alias Get-ModuleInfo Get-DbgModuleInfo
Set-Alias lm Get-DbgModuleInfo

Set-Alias Get-RegisterSet Get-DbgRegisterSet
Set-Alias r Get-DbgRegisterSet -Option AllScope -Scope Local -Force  # see comment about setting in "both scopes"
Set-Alias r Get-DbgRegisterSet -Option AllScope -Scope Global -Force # see comment about setting in "both scopes"

Set-Alias Get-Stack Get-DbgStack

Set-Alias Mount-DumpFile Mount-DbgDumpFile
Set-Alias -Name 'z' -Value Mount-DbgDumpFile
Set-Alias -Name '-z' -Value Mount-DbgDumpFile

Set-Alias dv Get-DbgLocalSymbol
Set-Alias ln Get-DbgNearSymbol

Set-Alias .dump Write-DbgDumpFile

<#
.SYNOPSIS
    Similar to the windbg command "dt", but with the augmented ability to deduce types for
    bare addresses when there is a vtable present.

.DESCRIPTION
#>
function dt
{
    [CmdletBinding( DefaultParameterSetName = 'JustTypeNameParamSet' )]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ParameterSetName = 'JustTypeNameParamSet' )]
           [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ParameterSetName = 'TypeNameAndAddressParamSet' )]
           [ValidateNotNullOrEmpty()]
           [object] $TypeNameOrAddress,

           [Parameter( Mandatory = $false,
                       Position = 1,
                       ParameterSetName = 'TypeNameAndAddressParamSet' )]
           [ValidateNotNullOrEmpty()]
           [object] $TypeNameOrAddress2,

           [Parameter( Mandatory = $false, ParameterSetName = 'JustTypeNameParamSet' )]
           [switch] $NoFollowTypeDefs,

           [Parameter( Mandatory = $false, ParameterSetName = 'JustTypeNameParamSet' )]
           [switch] $Raw,

           [Parameter( Mandatory = $false, ParameterSetName = 'JustTypeNameParamSet' )]
           [Alias( 'e' )] # like dt -e
           [Alias( 't' )] # like dt -t
           # TODO: What is the difference between "dt -t" and "dt -e"???
           [switch] $TypesOnly
          )

    begin { }
    end { }
    process
    {
        try
        {
            $dbgPath = $ExecutionContext.SessionState.Path.CurrentProviderLocation( [MS.Dbg.DbgProvider]::ProviderId )

            # Windbg's 'dt' is very flexible--you can pass an address and a typename, or
            # a typename and an address (either order works); or a global symbol; or a
            # type name. Hence the craziness here.
            #
            # In addition to windbg dt's flexibility, I've also added the ability for dt
            # to deduce the type of a naked address--i.e. if you run "dt 123`456789ab",
            # and there is a vtable pointer there, we can figure out the type and dump the
            # object.

            if( !$TypeNameOrAddress2 )
            {
                #
                # Check if it looks like an address first, else 0x123`456789ab looks like
                # a string to PS, and we'll end up trying to look for a type with that
                # "name" in every single module.
                #

                $addr = [MS.Dbg.Commands.AddressTransformationAttribute]::Transform( $null,  # no way to get engineIntrinsics ... or does $ExecutionContext work?
                                                                                     $dbgPath,
                                                                                     $true,  # skipGlobalSymbolTest
                                                                                     $false, # throwOnFailure
                                                                                     $TypeNameOrAddress )

                if( $addr )
                {
                    #
                    # This is the "please try to figure out the type for me" scenario.
                    #

                    if( $addr -isnot [UInt64] ) { throw "I expected a ulong..." }

                    $udt = $Debugger.TryGuessTypeForAddress( $addr )

                    if( $null -ne $udt )
                    {
                        Get-DbgSymbolValue -Address $addr -Type $udt
                    }
                    else
                    {
                        #
                        # Maybe it's the address of a global? This seems like a stretch...
                        # maybe it's not worth doing?
                        #

                        $nearSym = Get-DbgNearSymbol -Delta 0 -Address $addr
                        if( $nearSym.IsExactMatch )
                        {
                            $nearSym.Symbol.Value
                        }
                        else
                        {
                            Write-Error "Could not deduce type at address: $(Format-DbgAddress $addr)"
                        }
                    }

                    return
                } # end if( $addr )


                if( $TypeNameOrAddress -isnot [string] )
                {
                    throw "I don't know what to do with a $($TypeNameOrAddress.GetType().FullName)."
                }

                $sym = $null

                if( $Raw -or $NoFollowTypeDefs )
                {
                    Write-Verbose "-Raw and -NoFollowTypeDefs are only valid for dumping types."
                    $TypesOnly = $true # it might already be set
                }

                if( !$TypesOnly )
                {
                    [MS.Dbg.GlobalSymbolCategory] $Category = 'Data,Function'

                    # There could be members too (like foo!sym.blah.bar). That's not
                    # supported by windbg, but I like it.
                    [string[]] $tokens = $TypeNameOrAddress.Split( '.' )

                    # It could be a local symbol, global symbol, or a type.
                    #
                    # If it looks like it could be a local, we'll check locals first.
                    # Because if somebody does "dt this", we don't want to start out by
                    # loading all the symbols for all the modules so that we can look for
                    # a global "this" in each one.
                    if( $tokens[ 0 ].IndexOf( ([char] '!') ) -lt 0 )
                    {
                        $sym = Get-DbgLocalSymbol -Name $tokens[ 0 ]
                        if( $sym )
                        {
                            $val = $sym.Value
                            for( [int] $i = 1; $i -lt $tokens.Count; $i++ )
                            {
                                $prop = $val.PSObject.Properties[ $tokens[ $i ] ]
                                if( !$prop )
                                {
                                    # Or should this throw?
                                    Write-Error "No such '$($tokens[ $i ])' property on '$($tokens[ $i - 1 ])'."
                                    return
                                }
                                $val = $prop.Value
                            }
                            $val
                        }
                    }

                    if( !$sym )
                    {
                        # Maybe it's a global.

                        $sym = Get-DbgSymbol -Name $tokens[ 0 ] -Category $category
                        # N.B. I'm depending on the fact that Get-DbgSymbol does not yield an
                        # error if the symbol was not found, even if there were no wildcards.
                        if( $sym )
                        {
                            # Note that I'm not just returning $sym.Value, since there could
                            # be members included in $TypeNameOrAddress.
                            Get-DbgSymbolValue -Name $TypeNameOrAddress -Category $category
                        }
                    }
                }

                # It could cover type names, too, if there is a wildcard involved.
                if( (!$sym) -or
                    ([System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters( $TypeNameOrAddress )) )
                {
                    Get-DbgTypeInfo -Raw:$Raw -NoFollowTypeDefs:$NoFollowTypeDefs -Name $TypeNameOrAddress
                }

                return
            } # end if( only one parameter )

            # One is (should be) an address, and the other a type/typename.
            #
            # But which is which?


            $typeThing = $null
            $addr = [MS.Dbg.Commands.AddressTransformationAttribute]::Transform( $null,  # no way to get engineIntrinsics
                                                                                 $dbgPath,
                                                                                 $true,  # skipGlobalSymbolTest
                                                                                 $false, # throwOnFailure
                                                                                 $TypeNameOrAddress )
            if( $addr )
            {
                if( $addr -isnot [UInt64] ) { throw "I expected a ulong..." }
                $typeThing = $TypeNameOrAddress2
            }
            else
            {
                $typeThing = $TypeNameOrAddress
                $addr = $TypeNameOrAddress2
            }

            return Get-DbgSymbolValue -Address $addr -Type $typeThing
        }
        finally { }
    } # end 'process' block
} # end function dt

<#
.SYNOPSIS
    Similar to the windbg command "x": gets information for local or global symbols.

.DESCRIPTION
    "x" just forwards to Get-DbgLocalSymbol ("dv") or Get-DbgSymbol, depending on if there is a '!' in the $Name.
#>
function x
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0, ValueFromPipeline = $true )]
           [ValidateNotNullOrEmpty()]
           [string[]] $Name,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.GlobalSymbolCategory] $Category = [MS.Dbg.GlobalSymbolCategory]::All )

    begin { }
    end { }
    process
    {
        try
        {
            foreach( $n in $Name )
            {
                if( $n.Contains( ([char] '!') ) )
                {
                    Get-DbgSymbol -Name $n -Category $Category
                }
                else
                {
                    if( $Category -ne 'All' )
                    {
                        Write-Warning "The '-Category' parameter is not supported for local variables."
                    }
                    Get-DbgLocalSymbol -Name $n
                }
            }
        }
        finally { }
    } # end 'process' block
} # end function x

<#
.Synopsis
    Like the windbg "u" command.
#>
function u
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation()]
           # TODO: The workaround for INTf423c7e8 is to relax the parameter
           # type (to "object").
           #[UInt64] $Address,
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.Commands.RangeTransformation()]
           [UInt64] $CountOrEndAddress
         )
    process
    {
        try
        {
            $disasmArgs = @{ 'Address' = $Address }

            if( $CountOrEndAddress.IsNegative )
            {
                [System.Diagnostics.Debug]::Assert( $CountOrEndAddress.HasLengthPrefix )
                $disasmArgs[ 'InstructionCount' ] = [int] (0 - $CountOrEndAddress)
            }
            elseif( $CountOrEndAddress -ne 0 )
            {
                if( !$CountOrEndAddress.HasLengthPrefix )
                {
                    # Is it a count, or is it an end address? This is a pretty simple
                    # heuristic, but I bet it mostly works.
                    if( ($Address -ne 0) -and
                        ($CountOrEndAddress -gt $Address) -and
                        (($CountOrEndAddress - $Address) -lt 256kb) )
                    {
                        $disasmArgs[ 'EndAddress' ] = $CountOrEndAddress
                    }
                    elseif( ($CountOrEndAddress -lt 256kb) -and
                            ($CountOrEndAddress -ne 0) )
                    {
                        $disasmArgs[ 'InstructionCount' ] = [int] $CountOrEndAddress
                    }
                    else
                    {
                        throw "The value '$($CountOrEndAddress.ToString( 'x' ))' is too large to be a valid range. If you really want that much, use the size override syntax (`"L?<size>`")."
                    }
                }
                else
                {
                    $disasmArgs[ 'InstructionCount' ] = [int] $CountOrEndAddress
                }
            } # end if( $CountOrEndAddress -ne 0 )
            Read-DbgDisassembly @disasmArgs
        }
        finally { }
    }
} # end u

<#
.Synopsis
    Let's you type "u." instead of "u .".
#>
function u.() { u .  }
<#
.Synopsis
    Let's you type "uf." instead of "uf .".
#>
function uf.() { uf .  }


<#
.Synopsis
    Like the windbg "ub" command.
#>
function ub
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation()]
           # TODO: The workaround for INTf423c7e8 is to relax the parameter
           # type (to "object").
           #[UInt64] $Address,
           [object] $Address,

           [Parameter( Mandatory = $false, Position = 1 )]
           [UInt32] $Count = 8
         )
    process
    {
        try
        {
            Read-DbgDisassembly $Address -InstructionCount ([int] 0 - $Count)
        }
        finally { }
    }
} # end ub


<#
.Synopsis
    Like the windbg "uf" command.
#>
function uf
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $false,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [MS.Dbg.Commands.AddressTransformation()]
           # TODO: The workaround for INTf423c7e8 is to relax the parameter
           # type (to "object").
           #[UInt64] $Address,
           [object] $Address
         )
    process
    {
        try
        {
            Read-DbgDisassembly -Address $Address -WholeFunction
        }
        finally { }
    }
} # end uf


<#
.Synopsis
    Similar to the windbg "poi" command (MASM operator).
.Description
    The 'poi' command dereferences a pointer.

    Similar to windbg, using 'poi' on a numeric address will read memory from the specified address.

    If you pass a DbgPointerValue instead of a raw address, then 'poi' will return the "pointee" value (equivalent to calling the DbgGetPointee() method on it).

    Note that you will need to use this much differently than you use "poi" in windbg. In windbg, you call it as if it were a function call, like "dd poi( 07ef0000 )". This syntax will not work in PowerShell. First, you don't use parentheses when calling commands (parentheses only serve to group sub-things); and you'll also need to wrap the whole thing in parentheses if you are passing the result to something else, like this: "dd (poi 0x07ef0000)".

        Windbg: "dd poi( 07ef0000 )

    PowerShell: "dd (poi 0x07ef0000)"
#>
function poi
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [object] $Address
         )

    begin
    {
        function _PoiForAddress
        {
            [CmdletBinding()]
            param( [Parameter( Mandatory = $true,
                               Position = 0,
                               ValueFromPipeline = $true,
                               ValueFromPipelineByPropertyName = $true )]
                   [MS.Dbg.Commands.AddressTransformation()]
                   # TODO: The workaround for INTf423c7e8 is to relax the parameter
                   # type (to "object").
                   #[UInt64] $Address,
                   [object] $Address
                 )
            process
            {
                $Debugger.ReadMemAs_pointer( $Address )
            }
        } # end _PoiForAddress
    } # end 'begin' block

    process
    {
        try
        {
            if( $Address -is [MS.Dbg.DbgPointerValue] )
            {
                $Address.DbgGetPointee()
            }
            else
            {
                # Note that other objects deriving from DbgPointerValueBase will come in
                # here. They are implicitliy convertable to UInt64, so the
                # [AddressTransformation()] attribute will work fine on them.
                _PoiForAddress $Address
            }
        }
        finally { }
    } # end 'process' block
} # end poi


<#
.Synopsis
    "Safe 'poi'": try to dereference a pointer, but don't blow up if it's NULL.
.Link
    poi
#>
function spoi
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
           [object] $Address
         )

    begin
    {
        function _PoiForAddress
        {
            [CmdletBinding()]
            param( [Parameter( Mandatory = $true,
                               Position = 0,
                               ValueFromPipeline = $true,
                               ValueFromPipelineByPropertyName = $true )]
                   [MS.Dbg.Commands.AddressTransformation()]
                   # TODO: The workaround for INTf423c7e8 is to relax the parameter
                   # type (to "object").
                   #[UInt64] $Address,
                   [object] $Address
                 )
            process
            {
                if( $Address -gt 4095 )
                {
                    $Debugger.ReadMemAs_pointer( $Address )
                }
            }
        } # end _PoiForAddress
    } # end 'begin' block

    process
    {
        try
        {
            if( $null -eq $Address )
            {
                return
            }

            if( $Address -is [MS.Dbg.DbgPointerValue] )
            {
                if( $Address -ne 0 )
                {
                    $Address.DbgGetPointee()
                }
            }
            else
            {
                # Note that other objects deriving from DbgPointerValueBase will come in
                # here. They are implicitliy convertable to UInt64, so the
                # [AddressTransformation()] attribute will work fine on them.
                _PoiForAddress $Address
            }
        }
        finally { }
    } # end 'process' block
} # end spoi


Set-Alias al Get-DbgAlias

function as
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNullOrEmpty()]
           [string] $Name,

           [Parameter( Mandatory = $true, Position = 1, ValueFromRemainingArguments = $true )]
           [AllowNull()]
           [AllowEmptyString()]
           [string] $EquivalentPhrase

           # There are other parameter sets that we could implement (like "as /e", "as
           # /ma", "as /f", etc. But I don't think those are very important... since you
           # have PowerShell to do fancy stuff, you shouldn't need all that jazz.
         )
    process
    {
        try
        {
           Set-DbgAlias -Name $Name -Value $EquivalentPhrase
        }
        finally { }
    }
}


Set-Alias ad Remove-DbgAlias

Set-Alias .frame Set-DbgStackFrame

<#
.SYNOPSIS
    Like the windbg ".f+" function: sets the context to the next stack frame.
#>
function .f+
{
    [CmdletBinding()] param()
    process { try { .frame -Increment } finally { } }
}

<#
.SYNOPSIS
    Like the windbg ".f-" function: sets the context to the previous stack frame.
.DESCRIPTION
    Note that there is a ".f-" alias; this function is named ".f_minus" to avoid a warning about an unapproved verb, thanks to the "-".
#>
function .f_minus
{
    [CmdletBinding()] param()
    process { try { .frame -Decrement } finally { } }
}
Set-Alias .f- .f_minus


<#
.Synopsis
    Converts hex addresses into numbers, so you can do math on them.
#>
function ConvertTo-Number
{
    [CmdletBinding()]
    [OutputType( [System.UInt64] )]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true )]
           [MS.Dbg.Commands.AddressTransformation()]
           # TODO: The workaround for INTf423c7e8 is to relax the parameter
           # type (to "object").
           #[UInt64] $Address,
           [object] $Numberish
         )
    process
    {
        try
        {
            # Nothing to do; all the work was done by the AddressTransformation attribute.
            return $Numberish
        }
        finally { }
    }
} # end ConvertTo-Number

Set-Alias n ConvertTo-Number


# TODO: muscle memory requests that I implement something that allows me to type ".reload /f" instead of ".reload -f".
Set-Alias .reload Initialize-DbgSymbols


Set-Alias .load Add-DbgExtension
Set-Alias .unload Remove-DbgExtension

<#
.Synopsis
    Like the windbg ".loadby" function: load an extension DLL from the same directory as a loaded module.
#>
function .loadby
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ValueFromPipeline = $true )]
           [string] $ExtensionName,

           [Parameter( Mandatory = $true, Position = 1 )]
           [string] $ByLoadedDll
         )
    process
    {
        try
        {
            $m = Get-DbgModuleInfo $ByLoadedDll -ErrorAction Stop
            if( $null -eq $m )
            {
                throw "No such module: $ByLoadedDll"
            }
            $path = [System.IO.Path]::GetDirectoryName( $m.ImageName )
            $path = [System.IO.Path]::Combine( $path, $ExtensionName )
            .load $path
        }
        finally { }
    }
} # end .loadby


<#
.Synopsis
    Like the windbg ".effmach" function: view or change the current effective processor type.
#>
function .effmach
{
    [CmdletBinding()]
    [OutputType( [Microsoft.Diagnostics.Runtime.Interop.IMAGE_FILE_MACHINE] )]
    param( [Parameter( Mandatory = $false, Position = 0 )]
           [ValidateNotNullOrEmpty()]
           [string] $Platform
         )
    process
    {
        try
        {
            if( $Platform )
            {
                Set-DbgEffectiveProcessorType $Platform
            }
            else
            {
                Get-DbgEffectiveProcessorType
            }
        }
        finally { }
    }
} # end .effmach


<#
.Synopsis
    Like the windbg ".lastevent" function: displays the most recent exception or event that occurred
#>
function .lastevent
{
    [CmdletBinding()]
    [OutputType( [MS.Dbg.DbgLastEventInfo] )]
    param()

    process
    {
        try
        {
            $debugger.GetLastEventInfo()
        }
        finally { }
    }
} # end .lastevent


<#
.SYNOPSIS
    Finds a thread matching the specified criteria.
#>
function Find-DbgThread
{
    [CmdletBinding( DefaultParameterSetName = 'WithStackTextParamSet' )]
    [OutputType( [MS.Dbg.DbgUModeThreadInfo] )]
    param( [Parameter( Mandatory = $true,
                       Position = 0,
                       ParameterSetName = 'WithStackTextParamSet' )]
           [ValidateNotNullOrEmpty()]
           [string] $WithStackText
         )

    process
    {
        try
        {
            if( $PSCmdlet.ParameterSetName -eq 'WithStackTextParamSet' )
            {
                Get-DbgUModeThreadInfo | %{
                    $thread = $_
                    foreach( $frame in $thread.Stack.Frames )
                    {
                        if( $frame.ToString().IndexOf( $WithStackText, [StringComparison]::OrdinalIgnoreCase ) -ge 0 )
                        {
                            Write-Output $thread
                            break;
                        }
                    } # end foreach( $frame )
                } # end per-thread block
            } # end if( WithStackTextParamSet )
            else
            {
                throw "what?"
            }
        }
        finally { }
    }
} # end Find-DbgThread


<#
.SYNOPSIS
    Inserts a 'TypeName' into an object's TypeNames list at the location just before the specified TypeName. Note that the TypeName can be anything (it doesn't have to be the name of any actual "type").

    This is useful for symbol value converter scripts who want to let others know that they were applied to a particular symbol value. For instance, you could use the TypeName applied by a converter script to apply a custom view definition to only objects that had that converter applied.
#>
function InsertTypeNameBefore
{
    [CmdletBinding()]
    param( [Parameter( Position = 0, Mandatory = $true, ValueFromPipeline = $true )]
           [ValidateNotNull()]
           [object] $InputObject,

           [Parameter( Position = 1, Mandatory = $true )]
           [ValidateNotNullOrEmpty()]
           [string] $NewTypeName,

           [Parameter( Position = 2, Mandatory = $true )]
           [ValidateNotNullOrEmpty()]
           [string] $Before
         )

    begin { }
    end { }

    process
    {
        try
        {
            for( [int] $i = 0; $i -lt $InputObject.PSObject.TypeNames.Count; $i++ )
            {
                if( $InputObject.PSObject.TypeNames[ $i ] -eq $Before )
                {
                    $InputObject.PSObject.TypeNames.Insert( $i, $NewTypeName )
                    return
                }
            }
            # I don't know if this is actually the behavior I want. But it seems safe to
            # start with.
            Write-Error "Did not find existing TypeName '$Before'."
        }
        finally { }
    } # end 'process' block
} # end InsertTypeNameBefore


<#
.SYNOPSIS
    Similar to !teb in windbg, but returns a TEB object instead of just printing it out. It's an alternative to "(~.).Teb".
#>
function !teb
{
    [CmdletBinding()]
    param()

    begin { }
    end { }

    process
    {
        try
        {
		    $currentThread = Get-DbgUModeThreadInfo -Current
            if( $currentThread )
            {
                return $currentThread.Teb
            }
        }
        finally { }
    } # end 'process' block
} # end function !teb


<#
.SYNOPSIS
    Opens the current dump in windbg. TBD: support for live targets, remotes
#>
function Open-InWindbg
{
    [CmdletBinding()]
    param()

    begin { }
    end { }

    process
    {
        try
        {
            $windbgDir = Find-WindbgDir
            if( !$windbgDir )
            {
                Write-Error "Could not find windbg."
                return
            }

            [string] $dump = $null
            if( $CurrentDumpArchiveFile )
            {
                $dump = $CurrentDumpArchiveFile
            }
            elseif( $CurrentDumpFile )
            {
                $dump = $CurrentDumpFile
            }

            if( !$dump )
            {
                Write-Error "Not attached to a dump file? (live targets and remotes are TBD)"
                return
            }

            & (Join-Path $windbgDir 'windbg.exe') -z $dump
        }
        finally { }
    } # end 'process' block
} # end function Open-InWindbg


function Resolve-Error
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $false, Position = 0, ValueFromPipeline = $true )]
           [System.Management.Automation.ErrorRecord] $ErrorRecord )

    process
    {
        try
        {
            if( !$ErrorRecord )
            {
                if( 0 -eq $global:Error.Count )
                {
                    return
                }

                $thing = $global:Error[ 0 ]

                #
                # $thing might not be an ErrorRecord...
                #

                if( $thing -is [System.Management.Automation.ErrorRecord] )
                {
                    $ErrorRecord = $thing
                }
                elseif( $thing -is [System.Management.Automation.IContainsErrorRecord] )
                {
                    "(Error is an $($thing.GetType().FullName), but contains an ErrorRecord)"
                    $ErrorRecord = $thing.ErrorRecord
                }
                else
                {
                    Write-Warning "The thing I got from `$global:Error is not an ErrorRecord..."
                    Write-Warning "(it's a $($thing.GetType().FullName))"
                    $thing | Format-List * -Force
                    return
                }
            }

            # It's convenient to store the error we looked at in the global variable $e.
            # But we don't "own" that... so we'll only set it if it hasn't already been
            # set (unless it's been set by us).
            $e_var = Get-Variable 'e' -ErrorAction Ignore
            if( ($null -eq $e_var) -or
                (($e_var.Value -ne $null) -and
                 (0 -ne $e_var.Value.PSObject.Properties.Match( 'AddedByResolveError').Count)))
            {
                $global:e = $ErrorRecord
                Add-Member -InputObject $global:e `
                           -MemberType 'NoteProperty' `
                           -Name 'AddedByResolveError' `
                           -Value $true `
                           -Force # overwrite if we're re-doing one
            }

            $ErrorRecord | Format-List * -Force
            $ErrorRecord.InvocationInfo | Format-List *
            $Exception = $ErrorRecord.Exception
            for( $i = 0; $Exception; $i++, ($Exception = $Exception.InnerException) )
            {   "$i" * 80
                "ExceptionType : $($Exception.GetType().FullName)"
                $Exception | Format-List * -Force
            }
        }
        finally { }
    }
}
Set-Alias rver Resolve-Error


<#
.SYNOPSIS
    Gets version information about the currently-running instance of DbgShell.
#>
function Get-DbgShellVersionInfo
{
    [CmdletBinding()]
    param()

    begin { }
    end { }
    process
    {
        try
        {
            $p = Get-Process -id $pid

            "Host process is: $($p.MainModule.ModuleName)"
            "($($p.MainModule.FileName))"

            ''

            "64-bit process? $([System.Environment]::Is64BitProcess)"
            "64-bit OS? $([System.Environment]::Is64BitOperatingSystem)"

            $versionInfoProps = @( 'FileName'
                                   'FileDescription'
                                   'ProductVersion'
                                   'FileVersion'
                                   'IsDebug'
                                   'IsPatched'
                                   'IsPreRelease'
                                   'IsPrivateBuild'
                                   'IsSpecialBuild'
                                   'PrivateBuild'
                                 )

            $files = @( 'bin:\DbgShell.exe'
                        'bin:\Debugger\DbgProvider.dll'
                        'bin:\Debugger\Microsoft.Diagnostics.Runtime.dll'
                      )

            foreach( $file in $files )
            {
                (dir $file).VersionInfo | Select $versionInfoProps
            }

            # Not all of these modules will be present. That's fine.
            $p.Modules | where {
                ($_.ModuleName -eq 'DbgShellExt.dll') -or
                ($_.ModuleName -eq 'dbgeng.dll') -or
                ($_.ModuleName -eq 'dbghelp.dll') -or
                ($_.ModuleName -eq 'dbgcore.dll') -or
                ($_.ModuleName -eq 'dbgmodel.dll') -or
                ($_.ModuleName -eq 'windbg.exe') -or
                ($_.ModuleName -eq 'ntsd.exe') -or
                ($_.ModuleName -eq 'cdb.exe') -or
                ($_.ModuleName -eq 'kd.exe') -or
                ($_.ModuleName -like 'mscordac*') -or
                ($_.ModuleName -eq 'System.Management.Automation.ni.dll') -or
                ($_.ModuleName -eq 'System.Management.Automation.dll')
            } | ForEach-Object {
                $_.FileVersionInfo | Select $versionInfoProps
            }

            'PSVersionTable:'
            $PSVersionTable

            "Current log file is: $(Get-DbgShellLog -Current)"
            'Use Open-DbgShellLog to view it.'
        }
        finally { }
    }
} # end Get-DbgShellVersionInfo



# Trivia: 'exit' is a keyword, not a command, so using an alias (q --> exit) doesn't work.
function q { exit }
function :q { exit } # for entrenched vim users

function testall()
{
    # Q: Why not just use the module names? (We've properly configured the PSModulePath)
    #
    # A: In case we are run from a UNC path (PS does not like having a UNC path in the
    #    PSModulePath).
    #
    # That's also why we pre-load Pester, because the prereq specified in
    # DbgShellTest.psd1 might not work if we leave it to PS to fulfill it.
    Import-Module -Global "filesystem::$PSScriptRoot\..\DbgShellTest\Pester\Pester.psd1"
    Import-Module -Global "filesystem::$PSScriptRoot\..\DbgShellTest\DbgShellTest.psd1"
    Invoke-Pester Tests:\*.ps1
}


$private:defaultPrompt1 = '"PS $($executionContext.SessionState.Path.CurrentLocation)$(''>'' * ($nestedPromptLevel + 1)) "' `
                        + "`n" `
                        + '# .Link' `
                        + "`n" `
                        + '# http://go.microsoft.com/fwlink/?LinkID=225750' `
                        + "`n" `
                        + '# .ExternalHelp System.Management.Automation.dll-help.xml' `
                        + "`n"

$private:defaultPrompt2 = "`n" `
                        + '"PS $($executionContext.SessionState.Path.CurrentLocation)$(''>'' * ($nestedPromptLevel + 1)) ";' `
                        + "`n" `
                        + '# .Link' `
                        + "`n" `
                        + '# http://go.microsoft.com/fwlink/?LinkID=225750' `
                        + "`n" `
                        + '# .ExternalHelp System.Management.Automation.dll-help.xml' `
                        + "`n"

# What's different in defaultPrompt3? "http" --> "https"
$private:defaultPrompt3 = "`n" `
                        + '"PS $($executionContext.SessionState.Path.CurrentLocation)$(''>'' * ($nestedPromptLevel + 1)) ";' `
                        + "`n" `
                        + '# .Link' `
                        + "`n" `
                        + '# https://go.microsoft.com/fwlink/?LinkID=225750' `
                        + "`n" `
                        + '# .ExternalHelp System.Management.Automation.dll-help.xml' `
                        + "`n"


# We'll install a different prompt function, but only if the user has not already
# customized it.
#
# IDEA: Instead of remembering the exact text of each default prompt, maybe keep hashes of
# somewhat-sanitized versions? (such as trimming whitespace, replacing 'https' with
# 'http', etc.)
$private:currPrompt = (gcm prompt).Definition.Replace( "`r", "" )
if( ($currPrompt -eq $defaultPrompt3) -or
    ($currPrompt -eq $defaultPrompt2) -or
    ($currPrompt -eq $defaultPrompt1) )
{
    function prompt
    {
        $sb = New-Object 'System.Text.StringBuilder' -Arg @( 120 )
        if( test-path variable:/PSDebugContext )
        {
            $null = $sb.Append( '[DBG]: ' )
        }
        $null = $sb.Append( 'PS ' )
        $null = $sb.AppendLine( $executionContext.SessionState.Path.CurrentLocation )

        if( $nestedpromptlevel -ge 1 )
        {
            $null = $sb.Append( (New-Object 'System.String' -Arg @( ([char] '>'), $nestedpromptlevel )) )
        }

        $null = $sb.Append( '> ' )
        return $sb.ToString()
    }
}
# else user has custom prompt already


. $PSScriptRoot\OtherUtils.ps1


. $PSScriptRoot\KernelMode.ps1


#
# These commands must come last.
#
Export-ModuleMember -Alias '*'
Export-ModuleMember -Function '*'

