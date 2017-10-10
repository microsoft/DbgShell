
Set-StrictMode -Version Latest

<#
   This module defines the functions necessary to create, register, and use the
   view definitions that are used by DbgShell's alternate formatting engine.
#>


[bool] $__outStringColorShimProxyDebugSpew = ![string]::IsNullOrEmpty( $env:OutStringColorShimProxyDebugSpew )
[bool] $__formatDefaultProxyDebugSpew = ![string]::IsNullOrEmpty( $env:FormatDefaultProxyDebugSpew )
[bool] $__formatStringProxyDebugSpew = ![string]::IsNullOrEmpty( $env:FormatStringProxyDebugSpew )
[bool] $__formatTableProxyDebugSpew = ![string]::IsNullOrEmpty( $env:FormatTableProxyDebugSpew )
[bool] $__formatListProxyDebugSpew = ![string]::IsNullOrEmpty( $env:FormatListProxyDebugSpew )
[bool] $__formatCustomProxyDebugSpew = ![string]::IsNullOrEmpty( $env:FormatCustomProxyDebugSpew )

<#
.SYNOPSIS
    Allows you to preserve a null string when passing it to a .NET API.

.DESCRIPTION
    In PowerShell, null strings get auto-converted to empty strings. Perhaps useful for some scripting scenarios, but in managed code, it is possible to distinguish between null and empty strings, and some APIs do. Therefore, PowerShell script that calls such an API needs a way to pass a true null versus just an empty string. This function is PART of that.

    To use this with a parameter of a function, say $s, first use the $PSBoundParameters dictionary to determine if the parameter is bound--if not, assign [NullString]::Value to it. That will cause "$s -eq $null" to evaluate to true. Next, when passing that parameter to a .NET API, don't pass it directly--instead, pass the output of (Protect-NullString $s).

    Note that giving a parameter a default value of [NullString]::Value will not have any effect (you still need to use $PSBoundParameters to detect if the parameter was passed or not). (as of PSv3; INTe0ccf1d7)

    The trick that makes Protect-NullString work is that its parameter is not marked with a "[string]" type qualifier (if it were, any null passed in would get converted to empty).
#>
function Protect-NullString( $s )
{
    if( $null -eq $s )
    {
        [NullString]::Value
    }
    else
    {
        $s
    }
} # end Protect-NullString


<#
.SYNOPSIS
    If an object implements IEnumerable, returns it.

.DESCRIPTION
    This is similar to PSObjectHelper.GetEnumerable, which counts IDictionaries as
    IEnumerable even though LanguagePrimitives does not.
#>
function GetEnumerable( [object] $obj )
{
    $enumerable = [System.Management.Automation.LanguagePrimitives]::GetEnumerable( $obj )
    if( $null -ne $enumerable )
    {
        Write-Collection $enumerable
        return
    }
    # This is similar to PSObjectHelper.GetEnumerable.
    if( $obj -is [System.Collections.IDictionary] )
    {
        Write-Collection ([System.Collections.IDictionary] $obj)
        return
    }
    return $null
}


<#
.SYNOPSIS
    Enumerates an IEnumerable.

.DESCRIPTION
    This is needed because PowerShell does not automatically enumerate some IEnumerable
    things--notably IDictionaries.
#>
function Enumerate( [object] $obj )
{
    $iter = [System.Management.Automation.LanguagePrimitives]::GetEnumerator( $obj )
    # Apparently LanguagePrimitives.GetEnumerator does not work for some things (like
    # $PSVersionTable)... weird. So if that doesn't work, let's fall back to just calling
    # GetEnumerator() directly.
    if( !$iter )
    {
        # This can fail for some things. For instance, PsIndexedDictionary has two
        # GetEnumerator() methods, and both are explicit interface implementations, and
        # PowerShell ends up complaining that "[it can't find a method 'GetEnumerator()'
        # with argument count 0]". So if both LanguagePrimitives.GetEnumerator and this
        # don't work, we'll have to go dig for the method ourselves.
        $iter = $obj.GetEnumerator()
    }

    try
    {
        while( $iter.MoveNext() )
        {
            $iter.Current
        }
    }
    finally
    {
        if( $iter -is [System.IDisposable] )
        {
            $iter.Dispose()
        }
    }
} # end Enumerate


<#
.SYNOPSIS
    This function echoes an incoming stream of strings, but elides the first and last strings if they are empty.
.DESCRIPTION
    Sometimes it would be nice to send some stuff to Out-String and then use the output as part of something larger. But Out-String (and all the built-in formatting commands) tend to put in some extra newlines at the beginning and end. That's fine when running the command stand-alone, but the extra whitespace can be too much when composing. The purpose of this command is to help make things look nicer.

    Note that this function removes /only/ up to two lines from the beginning and up to two lines from the end (if they are empty). If, for example, the input stream ends with THREE empty lines, this function will only remove the last two. TODO: It might be nice to use a circular buffer of dynamic size so that you can choose the maximum amount of trimming. For now I think two lines should be enough.

    To use it, pipe your object to "Out-String -Stream" and then to this command. Don't forget the "-Stream"!
#>
function TrimStream
{
    [CmdletBinding( RemotingCapability = 'None' )]
    param( [Parameter( Mandatory = $true, ValueFromPipeline = $true, Position = 0 )]
           [AllowEmptyString()]
           #[string] $InputLine
           [object] $InputLine
         )

    begin
    {
        [int] $curIdx = 0
        [object] $lastSeen = $null
        [object] $lastLastSeen = $null
        [bool] $seenNonEmpty = $false
    }

    process
    {
        $objToDealWith = $null
        try
        {
            if( $null -eq $_ )
            {
                $objToDealWith = $InputLine
            }
            else
            {
                $objToDealWith = $_
            }

            if( $null -ne $lastLastSeen )
            {
                #if( ($curIdx -le 3) -and (0 -eq $lastLastSeen.Length) )
                if( $curIdx -le 3 )
                {
                    if( (0 -eq $lastLastSeen.Length) -and !$seenNonEmpty )
                    {
                        # Do nothing! This trims the beginning.
                    }
                    else
                    {
                        $lastLastSeen
                        $seenNonEmpty = $true
                    }
                }
                else
                {
                    $lastLastSeen
                }
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "OH NOES: $e" )
            throw
        }
        finally
        {
            $curIdx++
            $lastLastSeen = $lastSeen
            $lastSeen = $objToDealWith
        }
    } # end 'process' block

    end
    {
        try
        {
            [bool] $alreadySentLastLast = $false
            if( ($null -ne $lastLastSeen) -and (0 -ne $lastLastSeen.Length) )
            {
                $alreadySentLastLast = $true
                $lastLastSeen
            }

            if( ($null -ne $lastSeen) -and (0 -ne $lastSeen.Length) )
            {
                if( !$alreadySentLastLast )
                {
                    $lastLastSeen
                }
                $lastSeen
            }
        }
        finally { }
    }
} # end TrimStream



#
# Table stuff
#


<#
.SYNOPSIS
    Produces an object that defines a table column whose value is based on the specified property.

.Link
    New-AltScriptColumn
    New-AltColumns
    New-AltTableFooter
    New-AltTableViewDefinition
#>
function New-AltPropertyColumn
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNullOrEmpty()]
           [string] $PropertyName,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.ColorString] $FormatString = $null,

           [Parameter( Mandatory = $false, Position = 2 )]
           [string] $Label,

           [Parameter( Mandatory = $false, Position = 3 )]
           [MS.Dbg.Formatting.ColumnAlignment] $Alignment = 'Default',

           [Parameter( Mandatory = $false, Position = 4 )]
           [int] $Width,

           [Parameter( Mandatory = $false, Position = 5 )]
           [string] $Tag,

           [Parameter( Mandatory = $false )]
           [MS.Dbg.TrimLocation] $TrimLocation = [MS.Dbg.TrimLocation]::Right
         )
    begin { }
    end { }
    process
    {
        # Workaround for INTe0ccf1d7: default parameter value
        # assignment of [NullString]::Value doesn't "take".
        if( !$PSBoundParameters.ContainsKey( 'Label' ) )
        {
            $Label = [NullString]::Value
        }

        # Workaround for INTe0ccf1d7: default parameter value
        # assignment of [NullString]::Value doesn't "take".
        if( !$PSBoundParameters.ContainsKey( 'Tag' ) )
        {
            $Tag = [NullString]::Value
        }

        New-Object "MS.Dbg.Formatting.PropertyColumn" -ArgumentList @(
                $PropertyName,
                $FormatString,
                (Protect-NullString $Label),
                $Alignment,
                $Width,
                (Protect-NullString $Tag),
                $TrimLocation )
    }
} # end New-AltPropertyColumn


<#
.SYNOPSIS
    Produces an object that defines a table column whose value is based on the specified script.

.Link
    New-AltPropertyColumn
    New-AltTableFooter
    New-AltColumns
    New-AltTableViewDefinition
#>
function New-AltScriptColumn
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNullOrEmpty()]
           [string] $Label,

           [Parameter( Mandatory = $true, Position = 1 )]
           [ValidateNotNull()]
           [ScriptBlock] $Script,

           [Parameter( Mandatory = $false )]
           [MS.Dbg.Formatting.ColumnAlignment] $Alignment = 'Default',

           [Parameter( Mandatory = $false )]
           [int] $Width,

           [Parameter( Mandatory = $false )]
           [string] $Tag,

           [Parameter( Mandatory = $false )]
           [MS.Dbg.TrimLocation] $TrimLocation = [MS.Dbg.TrimLocation]::Right,

           [Parameter( Mandatory = $false )]
           [switch] $CaptureContext
         )
    begin { }
    end { }
    process
    {
        # Workaround for INTe0ccf1d7: default parameter value
        # assignment of [NullString]::Value doesn't "take".
        if( !$PSBoundParameters.ContainsKey( 'Tag' ) )
        {
            $Tag = [NullString]::Value
        }

      # if( (Test-Path 'Variable:\enabled') ) {
      #     [Console]::WriteLine( "Debugger.Formatting.psm1, New-AltScriptColumn: The 'enabled' variable exists." )
      # } else {
      #     [Console]::WriteLine( "Debugger.Formatting.psm1, New-AltScriptColumn: The 'enabled' variable does NOT exist." )
      # }

        New-Object "MS.Dbg.Formatting.ScriptColumn" -ArgumentList @(
                $Label,
                $Script,
                $Alignment,
                $Width,
                (Protect-NullString $Tag),
                $TrimLocation,
                $CaptureContext )
    }
} # end New-AltScriptColumn


<#
.SYNOPSIS
    Produces an object that defines a table footer.

.Link
    New-AltPropertyColumn
    New-AltScriptColumn
    New-AltColumns
    New-AltTableViewDefinition
#>
function New-AltTableFooter
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [MS.Dbg.Formatting.ColumnAlignment] $Alignment,

           [Parameter( Mandatory = $true, Position = 1 )]
           [ValidateNotNull()]
           [ScriptBlock] $Script,

           [Parameter( Mandatory = $false )]
           [switch] $CaptureContext
         )
    begin { }
    end { }
    process
    {
      # if( (Test-Path 'Variable:\enabled') ) {
      #     [Console]::WriteLine( "Debugger.Formatting.psm1, New-AltTableFooter: The 'enabled' variable exists." )
      # } else {
      #     [Console]::WriteLine( "Debugger.Formatting.psm1, New-AltTableFooter: The 'enabled' variable does NOT exist." )
      # }

        New-Object 'MS.Dbg.Formatting.Footer' -ArgumentList( $Alignment, $Script, $CaptureContext )
    }
} # end New-AltTableFooter


<#
.SYNOPSIS
    Defines a script block that produces column definitions, and optionally a single footer definition.

.Link
    New-AltPropertyColumn
    New-AltScriptColumn
    New-AltTableFooter
    New-AltTableViewDefinition
#>
function New-AltColumns
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNull()]
           [ScriptBlock] $ColumnProducer
         )
    begin { }
    end { }
    process
    {
        # TODO: This seems... really simple. Should I do anything extra, like
        # filter output to just column objects, or anything else?
        & $ColumnProducer
    }
} # end New-AltColumns


<#
.SYNOPSIS
    Produces a table view definition object with column (and footer) definitions supplied by the specified script block.

.Link
    New-AltPropertyColumn
    New-AltScriptColumn
    New-AltTableFooter
    New-AltColumns
    New-AltListViewDefinition
    New-AltCustomViewDefinition
    New-AltSingleLineViewDefinition
#>
function New-AltTableViewDefinition
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNull()]
           [ScriptBlock] $Columns,

           [Parameter( Mandatory = $false, Position = 1 )]
           [switch] $ShowIndex,

           [Parameter( Mandatory = $false, Position = 2 )]
           [ValidateNotNull()]
           [ScriptBlock] $ProduceGroupByHeader,

           [Parameter( Mandatory = $false, Position = 3 )]
           [ValidateNotNull()]
           [object] $GroupBy,

           [Parameter( Mandatory = $false )]
           [switch] $CaptureContext,

           [Parameter( Mandatory = $false )]
           [switch] $PreserveHeaderContext
         )
    begin { }
    end { }
    process
    {
        $private:columnList = New-Object System.Collections.Generic.List[MS.Dbg.Formatting.Column]
        [MS.Dbg.Formatting.Footer] $private:footer = $null
        & $Columns | % {
            if( $_ -is [MS.Dbg.Formatting.Column] )
            {
                $columnList.Add( $_ )
            }
            elseif( $_ -is [MS.Dbg.Formatting.Footer] )
            {
                if( $null -ne $footer )
                {
                    $private:SourceLineNumber = (Get-PSCallStack)[2].ScriptLineNumber
                    Write-Error -Message ([string]::Format( '{0}:{1} While registering a table view definition for type ''{2}'': The -Columns script block yielded more than one Footer.', $SourceScript, $SourceLineNumber, $TypeName )) -Category InvalidOperation -ErrorId 'ExtraFooters' -TargetObject $_
                }
                else
                {
                    $footer = $_
                }
            }
            else
            {
                $private:SourceLineNumber = (Get-PSCallStack)[2].ScriptLineNumber
                Write-Warning ([string]::Format( '{0}:{1} While registering a table view definition for type ''{2}'': The -Columns script block yielded an item that was not a column definition: {3}', $SourceScript, $SourceLineNumber, $TypeName, $_ ))
            }
        }

        if( $CaptureContext -and (!$ProduceGroupByHeader -or !$footer) )
        {
            Write-Error "New-AltTableViewDefinition: it does not make sense to use -CaptureContext if there is no -ProduceGroupByHeader or footer to use that context."
            $CaptureContext = $false
        }

        if( $PreserveHeaderContext -and !$ProduceGroupByHeader )
        {
            Write-Error "New-AltTableViewDefinition: it does not make sense to use -PreserveHeaderContext if there is no -ProduceGroupByHeader from which to preserve context."
            $PreserveHeaderContext = $false
        }

        New-Object "MS.Dbg.Formatting.AltTableViewDefinition" -ArgumentList @( $ShowIndex, $columnList, $footer, $ProduceGroupByHeader, $GroupBy, $CaptureContext, $PreserveHeaderContext )
    }
} # end New-AltTableViewDefinition


#
# Custom view stuff
#


<#
.SYNOPSIS
    Produces a custom view definition object.

.Link
    New-AltTableViewDefinition
    New-AltListViewDefinition
    New-AltSingleLineViewDefinition
#>
function New-AltCustomViewDefinition
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNull()]
           [ScriptBlock] $Script,

           [Parameter( Mandatory = $false, Position = 1 )]
           [ValidateNotNull()]
           [ScriptBlock] $ProduceGroupByHeader,

           [Parameter( Mandatory = $false, Position = 2 )]
           [ValidateNotNull()]
           [object] $GroupBy,

           [Parameter( Mandatory = $false, Position = 3 )]
           [ValidateNotNull()]
           [ScriptBlock] $End,

           [Parameter( Mandatory = $false )]
           [switch] $CaptureContext,

           [Parameter( Mandatory = $false )]
           [switch] $PreserveHeaderContext,

           [Parameter( Mandatory = $false )]
           [switch] $PreserveScriptContext
         )
    begin { }
    end { }
    process
    {
        # Note: unlike the other view definitions (table, list), it /does/ make sense to
        # have -CaptureContext here (to be used by $Script).

        if( $PreserveHeaderContext -and !$ProduceGroupByHeader )
        {
            Write-Error "New-AltCustomViewDefinition: it does not make sense to use -PreserveHeaderContext if there is no -ProduceGroupByHeader from which to preserve context."
            $PreserveHeaderContext = $false
        }

        New-Object "MS.Dbg.Formatting.AltCustomViewDefinition" -ArgumentList @( $Script, $ProduceGroupByHeader, $GroupBy, $End, $CaptureContext, $PreserveHeaderContext, $PreserveScriptContext )
    }
} # end New-AltCustomViewDefinition


#
# List stuff
#


<#
.SYNOPSIS
    Produces an object that defines a list view item whose value is based on the specified property.

.Link
    New-AltScriptListItem
    New-AltListViewDefinition
#>
function New-AltPropertyListItem
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNullOrEmpty()]
           [string] $PropertyName,

           [Parameter( Mandatory = $false, Position = 1 )]
           [MS.Dbg.ColorString] $FormatString,

           [Parameter( Mandatory = $false, Position = 2 )]
           [string] $Label
         )
    begin { }
    end { }
    process
    {
        # Workaround for INTe0ccf1d7: default parameter value
        # assignment of [NullString]::Value doesn't "take".
        if( !$PSBoundParameters.ContainsKey( 'Label' ) )
        {
            $Label = [NullString]::Value
        }

        New-Object "MS.Dbg.Formatting.PropertyListItem" -ArgumentList @(
                $PropertyName,
                $FormatString,
                (Protect-NullString $Label) )
    }
} # end New-AltPropertyListItem


<#
.SYNOPSIS
    Produces an object that defines a list view item whose value is based on the specified script.

.Link
    New-AltPropertyListItem
    New-AltListViewDefinition
#>
function New-AltScriptListItem
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNullOrEmpty()]
           [string] $Label,

           [Parameter( Mandatory = $true, Position = 1 )]
           [ValidateNotNull()]
           [ScriptBlock] $Script,

           [Parameter( Mandatory = $false )]
           [switch] $CaptureContext
         )
    begin { }
    end { }
    process
    {
        New-Object "MS.Dbg.Formatting.ScriptListItem" -ArgumentList @( $Label, $Script, $CaptureContext )
    }
} # end New-AltScriptListItem


<#
.SYNOPSIS
    Produces a list view definition object with list item definitions supplied by the specified script block.

.Link
    New-AltPropertyListItem
    New-AltScriptListItem
    New-AltTableViewDefinition
    New-AltCustomViewDefinition
#>
function New-AltListViewDefinition
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNull()]
           [ScriptBlock] $ListItems,

           [Parameter( Mandatory = $false, Position = 1 )]
           [ValidateNotNull()]
           [ScriptBlock] $ProduceGroupByHeader,

           [Parameter( Mandatory = $false, Position = 2 )]
           [ValidateNotNull()]
           [object] $GroupBy,

           [Parameter( Mandatory = $false )]
           [switch] $CaptureContext,

           [Parameter( Mandatory = $false )]
           [switch] $PreserveHeaderContext
         )
    begin { }
    end { }
    process
    {
        $private:listItemList = New-Object System.Collections.Generic.List[MS.Dbg.Formatting.ListItem]
        & $ListItems | % {
            if( $_ -is [MS.Dbg.Formatting.ListItem] )
            {
                $listItemList.Add( $_ )
            }
            else
            {
                $SourceLineNumber = (Get-PSCallStack)[2].ScriptLineNumber
                # TODO: Get a PS dev to investigate: When I remove the () from
                # around the [string]::Format expression, I get an error about
                # no 'positional parameter cannot be found that accepts
                # argument 'System.Object[]'. But the error complains about the
                # ForEach-Object cmdlet being called ~15 lines above (the "&
                # $ListItems | % {" line). I have been unable to construct a
                # short repro of this problem.
                Write-Warning ([string]::Format( '{0}:{1} While registering a list view definition for type ''{2}'': The -ListItems script block yielded an item that was not a list item: {3}', $SourceScript, $SourceLineNumber, $TypeName, $_ ))
            }
        }

        if( $CaptureContext -and !$ProduceGroupByHeader )
        {
            Write-Error "New-AltListViewDefinition: it does not make sense to use -CaptureContext if there is no -ProduceGroupByHeader to use that context."
            $CaptureContext = $false
        }

        if( $PreserveHeaderContext -and !$ProduceGroupByHeader )
        {
            Write-Error "New-AltListViewDefinition: it does not make sense to use -PreserveHeaderContext if there is no -ProduceGroupByHeader from which to preserve context."
            $PreserveHeaderContext = $false
        }

        New-Object "MS.Dbg.Formatting.AltListViewDefinition" -ArgumentList @( $listItemList, $ProduceGroupByHeader, $GroupBy, $CaptureContext, $PreserveHeaderContext )
    }
} # end New-AltListViewDefinition


#
# Single-line view stuff
#


<#
.SYNOPSIS
    Produces a single-line view definition object.

.DESCRIPTION
    A single-line view definition script must produce a single line of output--additional lines will be truncated. Single-line views are generally used as part of other views, and can only be used by explicitly calling Format-AltSingleLine--Out-Default will not use a single-line view definition.

.Link
    New-AltTableViewDefinition
    New-AltListViewDefinition
    New-AltCustomViewDefinition
#>
function New-AltSingleLineViewDefinition
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNull()]
           [ScriptBlock] $Script,

           [Parameter( Mandatory = $false )]
           [switch] $CaptureContext
         )
    begin { }
    end { }
    process
    {
        New-Object "MS.Dbg.Formatting.AltSingleLineViewDefinition" -ArgumentList @( $Script, $CaptureContext )
    }
} # end New-AltSingleLineViewDefinition


#
# General stuff
#


<#
.SYNOPSIS
    Produces an object that defines a set of format view definitions for the specified type(s).

.Link
    New-AltTableViewDefinition
    New-AltListViewDefinition
    New-AltCustomViewDefinition
    New-AltSingleLineViewDefinition
#>
function New-AltTypeFormatEntry
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNullOrEmpty()]
           [string[]] $TypeName,

           [Parameter( Mandatory = $true, Position = 1 )]
           [ValidateNotNull()]
           [ScriptBlock] $ViewProducer,

           [Parameter( Mandatory = $false, Position = 2 )]
           [ValidateNotNull()]
           [string] $SourceScript
         )
    begin { }
    end { }
    process
    {
        if( !$PSBoundParameters.ContainsKey( 'SourceScript' ) )
        {
            $SourceScript = (Get-PSCallStack)[1].ScriptName
        }

        $private:viewList = New-Object System.Collections.Generic.List[MS.Dbg.Formatting.IFormatInfo]
        & $ViewProducer | % { $viewList.Add( $_ ) }

        foreach( $private:tn in $TypeName )
        {
            New-Object "MS.Dbg.Formatting.AltTypeFormatEntry" -ArgumentList @( $tn, $viewList, $SourceScript )
        }
    }
}


<#
.SYNOPSIS
    Registers one or more AltTypeFormatEntry objects with the alternate formatting engine provider.

.Link
    New-AltTypeFormatEntry
#>
function Register-AltTypeFormatEntries
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNull()]
           [ScriptBlock] $EntriesProducer
         )
    begin { }
    end { }
    process
    {
        $private:disposable = $null
        try
        {
            # If any entries need to capture context (variables and functions), we'll only
            # let them capture stuff defined in the $EntriesProducer script block and
            # below.
            $disposable = [MS.Dbg.DbgProvider]::SetContextStartMarker()
            & $EntriesProducer | Register-AltTypeFormatEntry
        }
        finally
        {
            if( $disposable ) { $disposable.Dispose() }
        }
    }
}


<#
.SYNOPSIS
    Produces a new ColorString object (which uses ISO/IEC 6429 color markup).
#>
function New-ColorString
{
    [CmdletBinding()]
    [OutputType( ([MS.Dbg.ColorString]) )]
    param( [Parameter( Mandatory = $false, Position = 0 )]
           [ConsoleColor] $Foreground,

           [Parameter( Mandatory = $false, Position = 1 )]
           [ConsoleColor] $Background,

           [Parameter( Mandatory = $false, Position = 2 )]
           [string] $Content
         )
    begin { }
    end { }
    process
    {
        $cs = New-Object "MS.Dbg.ColorString"
        [bool] $pushed = $false

        if( $PSBoundParameters.ContainsKey( 'Foreground' ) )
        {
            if( $PSBoundParameters.ContainsKey( 'Background' ) )
            {
                [void] $cs.AppendPushFgBg( $Foreground, $Background )
            }
            else
            {
                [void] $cs.AppendPushFg( $Foreground )
            }
            $pushed = $true
        }
        else
        {
            if( $PSBoundParameters.ContainsKey( 'Background' ) )
            {
                [void] $cs.AppendPushBg( $Background )
                $pushed = $true
            }
        }

        if( $PSBoundParameters.ContainsKey( 'Content' ) )
        {
            [void] $cs.Append( $Content )
        }

        if( $pushed )
        {
            [void] $cs.AppendPop()
        }

        return $cs
    }
} # end New-ColorString


Set-Alias fal Format-AltList        -Scope global
Set-Alias fat Format-AltTable       -Scope global
Set-Alias fac Format-AltCustom      -Scope global
Set-Alias fas Format-AltSingleLine  -Scope global


#
# Proxy function stuff
#


function Update-FormatData
{
    [CmdletBinding( DefaultParameterSetName = 'FileSet',
                    SupportsShouldProcess = $true,
                    ConfirmImpact = 'Medium',
                    HelpUri = 'http://go.microsoft.com/fwlink/?LinkID=113420' )]
    param( [Parameter( ParameterSetName = 'FileSet',
                       Position = 0,
                       ValueFromPipeline = $true,
                       ValueFromPipelineByPropertyName = $true )]
            [Alias( 'PSPath', 'Path' )]
            [ValidateNotNull()]
            [string[]] ${AppendPath},

            [Parameter( ParameterSetName = 'FileSet' )]
            [ValidateNotNull()]
            [string[]] ${PrependPath}
         )
# This proxy function actually proxies two commands: the built-in
# Update-FormatData, and our alternate formatting engine's
# Update-AltFormatData. You can pass both .ps1xml (traditional formatting info
# file) and .psfmt (alternate formatting info file) parameters, and this splits
# out the params as appropriate.
#
# Q: This is pretty insane!
# A: I know, right?
#
# Note: I haven't really tested the PrependPath stuff.
#
# Could I have done something simpler? Sure. But you don't understand proxy
# functions and pipelines until you can write this function. (Of course, I will
# have forgotten it all in a few days... but then I can read this function!)
    begin
    {
        #TODO: try globbing/wildards
        try
        {
            #[console]::WriteLine( "begin" )
            $private:traditionalAppendPaths = New-Object 'System.Collections.Generic.List[System.String]'
            $private:altAppendPaths = New-Object 'System.Collections.Generic.List[System.String]'
            $private:traditionalPrependPaths = New-Object 'System.Collections.Generic.List[System.String]'
            $private:altPrependPaths = New-Object 'System.Collections.Generic.List[System.String]'

            function private:SeparateParams( $psb,
                                             $traditionalAppendPaths,
                                             $altAppendPaths,
                                             $traditionalPrependPaths,
                                             $altPrependPaths )
            {
                $traditionalAppendPaths.Clear()
                $altAppendPaths.Clear()
                $traditionalPrependPaths.Clear()
                $altPrependPaths.Clear()
                if( $psb.ContainsKey( 'AppendPath' ) )
                {
                    foreach( $path in $AppendPath )
                    {
                        if( ![string]::IsNullOrEmpty( $path ) )
                        {
                            if( $path.EndsWith( '.psfmt', [StringComparison]::OrdinalIgnoreCase ) )
                            {
                                #[console]::WriteLine( 'Adding alt append path: {0}', $path )
                                $altAppendPaths.Add( $path )
                            }
                            else
                            {
                                #[console]::WriteLine( 'Adding traditional append path: {0}', $path )
                                $traditionalAppendPaths.Add( $path )
                            }
                        }
                    } # end foreach( $AppendPath )
                } # end if( AppendPath )

                if( $psb.ContainsKey( 'PrependPath' ) )
                {
                    foreach( $path in $PrependPath )
                    {
                        if( ![string]::IsNullOrEmpty( $path ) )
                        {
                            if( $path.EndsWith( '.psfmt', [StringComparison]::OrdinalIgnoreCase ) )
                            {
                                #[console]::WriteLine( 'Adding alt prepend path: {0}', $path )
                                $altPrependPaths.Add( $path )
                            }
                            else
                            {
                                #[console]::WriteLine( 'Adding traditional prepend path: {0}', $path )
                                $traditionalPrependPaths.Add( $path )
                            }
                        }
                    } # end foreach( $PrependPath )
                } # end if( PrependPath )
            } # end function SeparateParams()

            function private:PrepParams( $psb, $appendPaths, $prependPaths )
            {
                if( $psb.ContainsKey( 'AppendPath' ) )
                {
                    $a = $appendPaths.ToArray()
                    Set-Variable -Name 'AppendPath' -Scope 1 -Value $a
                    $psb[ 'AppendPath' ] = $a
                    #[console]::WriteLine( 'Set AppendPath to ({0}): {1}', $a.Length, [string]::Join( ', ', $a ) )
                }
                if( $psb.ContainsKey( 'PrependPath' ) )
                {
                    $a = $prependPaths.ToArray()
                    Set-Variable -Name 'PrependPath' -Scope 1 -Value $a
                    $psb[ 'PrependPath' ] = $a
                    #[console]::WriteLine( 'Set PrependPath to: {0}', [string]::Join( ', ', $a ) )
                }
            } # end function PrepParams()


            $private:outBuffer = $null
            if( $PSBoundParameters.TryGetValue( 'OutBuffer', [ref] $outBuffer ) )
            {
                $PSBoundParameters[ 'OutBuffer' ] = 1
            }

            SeparateParams $PSBoundParameters $traditionalAppendPaths $altAppendPaths $traditionalPrependPaths $altPrependPaths

            PrepParams $PSBoundParameters $traditionalAppendPaths $traditionalPrependPaths

            $private:wrappedCmd = $ExecutionContext.InvokeCommand.GetCommand( 'Update-FormatData', [System.Management.Automation.CommandTypes]::Cmdlet )
            $private:scriptCmd = { & $wrappedCmd @PSBoundParameters }
            $private:builtinCmdSteppablePipeline = $scriptCmd.GetSteppablePipeline( $myInvocation.CommandOrigin )
            $builtinCmdSteppablePipeline.Begin( $PSCmdlet )

            PrepParams $PSBoundParameters $altAppendPaths $altPrependPaths

            $wrappedCmd = $ExecutionContext.InvokeCommand.GetCommand( 'Update-AltFormatData', [System.Management.Automation.CommandTypes]::Cmdlet )
            $scriptCmd = { & $wrappedCmd @PSBoundParameters }
            $private:altCmdSteppablePipeline = $scriptCmd.GetSteppablePipeline( $myInvocation.CommandOrigin )
            $altCmdSteppablePipeline.Begin( $PSCmdlet )
        }
        catch
        {
            [console]::WriteLine( "Update-FormatData proxy: PROBLEM in begin: {0}", $_ )
            throw
        }
        finally { }
    } # end 'begin' block

    process
    {
        try
        {
            if( $null -eq $_ )
            {
                # This is what happens when you just pass parameters in the normal way.
                #[console]::WriteLine( "process, `$_ is null" )

                SeparateParams $PSBoundParameters $traditionalAppendPaths $altAppendPaths $traditionalPrependPaths $altPrependPaths

                PrepParams $PSBoundParameters $traditionalAppendPaths $traditionalPrependPaths

                $builtinCmdSteppablePipeline.Process( $_ )

                PrepParams $PSBoundParameters $altAppendPaths $altPrependPaths

                $altCmdSteppablePipeline.Process( $_ )
            }
            else
            {
                # This is what happens when you pipe input in.
                #[console]::WriteLine( "process, type {0}: {1}", $_.GetType().FullName, $_ )

                SeparateParams $PSBoundParameters $traditionalAppendPaths $altAppendPaths $traditionalPrependPaths $altPrependPaths

                # We only want to step the relevant pipeline
                if( $_.EndsWith( '.psfmt', [StringComparison]::OrdinalIgnoreCase ) )
                {
                    PrepParams $PSBoundParameters $altAppendPaths $altPrependPaths
                    $altCmdSteppablePipeline.Process( $_ )
                }
                else
                {
                    PrepParams $PSBoundParameters $traditionalAppendPaths $traditionalPrependPaths
                    $builtinCmdSteppablePipeline.Process( $_ )

                }
            }
        }
        catch
        {
            [console]::WriteLine( "Update-FormatData proxy: PROBLEM in process: {0}", $_ )
            throw
        }
        finally { }
    } # end 'process' block

    end
    {
        try
        {
            SeparateParams $PSBoundParameters $traditionalAppendPaths $altAppendPaths $traditionalPrependPaths $altPrependPaths

            PrepParams $PSBoundParameters $traditionalAppendPaths $traditionalPrependPaths

            $builtinCmdSteppablePipeline.End()

            PrepParams $PSBoundParameters $altAppendPaths $altPrependPaths

            $altCmdSteppablePipeline.End()
        }
        catch
        {
            [console]::WriteLine( "Update-FormatData proxy: PROBLEM in end: {0}", $_ )
            throw
        }
        finally { }
    } # end 'end' block
    <#
    .ForwardHelpTargetName Update-FormatData
    .ForwardHelpCategory Cmdlet
    #>
}


<#
We already have an Out-String proxy function. But we still have a problem with
ColorStrings that end up getting passed to the built-in Out-String. (For instance, if you
have an AltTableViewDefinition for a particular object, then in our Out-String proxy, we
will format it, and send that output, which will be ColorString objects, to the built-in
Out-String.) So we want to make sure never to pass ColorString objects to the built-in
Out-String, because it will probably just mess them up (by truncating them early, etc.).
So we use this mini-proxy to do that.


TODO: This assumes that our host will know what to do with the raw ColorStrings we give
it... What if we are in a different host?
#>
function script:OutStringColorShim
{
    [CmdletBinding()]
    param( [Parameter()]
           [switch] ${Stream},

           [Parameter()]
           [ValidateRange( 2, 2147483647 )]
           [int] ${Width},

           # TODO: Implement Stream and Width for ColorStrings!
           #
           # TODO: We should probably also have a -StripColor feature.

           [Parameter( ValueFromPipeline = $true )]
           [PSObject] ${InputObject}
         )

    begin
    {
        [bool] $private:enableDebugSpew = $false
        if( $__outStringColorShimProxyDebugSpew )
        {
            $enableDebugSpew = $true
        }

        #[Console]::WriteLine( "OutStringColorShim proxy begin" )
        try
        {
            $private:currentSteppablePipeline = $null
            # TODO: What if someone else is also trying to proxy Out-String?
            $private:outStringWrappedCmd = $ExecutionContext.InvokeCommand.GetCommand( 'Out-String', [System.Management.Automation.CommandTypes]::Cmdlet )

        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "OutStringColorShim begin: OH NOES: $e" )
            throw
        }
        finally { }
    } # end begin block

    process
    {
        if( $enableDebugSpew )
        {
            [Console]::WriteLine( "        ======== OutStringColorShim thing: process ========" )
            [Console]::WriteLine( '        $PSBoundParameters:' )
            foreach( $key in $PSBoundParameters.Keys )
            {
                $val = $PSBoundParameters[ $key ]
                if( $null -eq $val ) { $val = "<null>" }
                [Console]::WriteLine( "            [$($key)]: $($val)" )
            }
        }
        try
        {
            $private:objToDealWith = $null
            [bool] $private:bindUsingInputObject = $false
            if( $null -eq $_ )
            {
                if( $enableDebugSpew )
                {
                    [Console]::WriteLine( "        OsProxy: Dollar-underbar is null." )
                }
                $objToDealWith = $InputObject
                $bindUsingInputObject = $true
            }
            else
            {
                if( $enableDebugSpew )
                {
                    [Console]::WriteLine( "        OsProxy: Dollar-underbar object of type: {0}", $_.GetType().FullName )
                }
                $objToDealWith = $_
                # Things get messed up in the child steppable pipeline if both $_ and
                # $InputObject are set. (I don't know why; they're both set here...)
                [void] $PSBoundParameters.Remove( 'InputObject' )
            }

            if( $null -eq $objToDealWith ) # TODO: Do I need to handle [System.Management.Automation.Internal.AutomationNull]::Value?
            {
                return
            }

            if( $objToDealWith -is [MS.Dbg.ColorString] )
            {
                #[Console]::WriteLine( "OutStringColorShim: It's just a ColorString..." )
                return $objToDealWith
            }

            if( $null -eq $currentSteppablePipeline )
            {
                $private:outStringScriptCmd = { & $outStringWrappedCmd @PSBoundParameters }
                $currentSteppablePipeline = $outStringScriptCmd.GetSteppablePipeline( $myInvocation.CommandOrigin )
                $currentSteppablePipeline.Begin( $PSCmdlet )
            }

            # TODO: Assert $null -ne $currentSteppablePipeline
            if( $bindUsingInputObject )
            {
                $currentSteppablePipeline.Process( $null )
            }
            else
            {
                $currentSteppablePipeline.Process( $objToDealWith )
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "OutStringColorShim: OH NOES: $e" )
            throw
        }
        finally { }
    } # end process block

    end
    {
        try
        {
            if( $null -ne $currentSteppablePipeline )
            {
                $currentSteppablePipeline.End()
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "OutStringColorShim end: OH NOES: $e" )
            throw
        }
        finally { }
    } # end end block
} # end OutStringColorShim


<#
.SYNOPSIS
    Used to work around a bug/deficiency in setting $HostSupportsColor.
.DESCRIPTION
    The Out-Default proxy dynamically toggles HostSupportsColor, and then tries to restore the original value. The problem is that every once in a while, if you CTRL-C at just the right time, Out-Default does not get a chance to restore the old value (there's no way to implement a "Dispose" method for a script function, and the 'end' block doesn't get to run). So now you're stuck, because now Out-Default thinks the "original" value is something else, and it always sets it back to that--even if you run "$HostSupportsColor = $true" or "[MS.Dbg.DbgProvider]::HostSupportsColor = $true", the Out-Default proxy's 'end' block will run and set it back.

    This method queues a work item to a separate thread, which does a "sleep" and then sets the property directly, hopefully after the Out-Default proxy has ended.
#>
function Reset-HostSupportsColor
{
    [CmdletBinding()]
    [OutputType( ([MS.Dbg.ColorString]) )]
    param( [Parameter( Mandatory = $false, Position = 0 )]
           [bool] $Value = $true
         )
    begin { }
    end { }
    process
    {
        # We can't just set $HostSupportsColor, because our intrepid Out-Default proxy
        # will always get the last word and set it back. This is a HACKY workaround to let
        # us get it set back the way we want.
        [MS.Dbg.DbgProvider]::ResetHostSupportsColor( $Value )
    }
} # end Reset-HostSupportsColor



# This code helps demonstrate why we need both an Out-Default proxy and an
# Out-String proxy. You can put a Console.WriteLine in the 'begin' block of our
# Out-String proxy in order to see that piping $things to Out-Default does not
# call the Out-String command (if you look at the implementation, the built-in
# Out-Default command shares the same base class with the built-in Out-String
# command, which is also the base class of the other formatting commands).
<#
Add-Type -TypeDef @'
using System;
public class Thing
{
    public string Str;
    public int Num;
    public object Whatev;
    public Thing() { }
    public Thing( string str ) { Str = str; }
    public Thing( string str, int num ) : this( str ) { Num = num; }
    public Thing( string str, int num, object whatev ) : this( str, num ) { Whatev = whatev; }
}
'@
$t1 = New-Object 'Thing' -Arg @( "hi", 1 )
$t2 = New-Object 'Thing' -Arg @( "aasfd;asd", 12 )
$t3 = New-Object 'Thing' -Arg @( "fourscore", 20 )
$things = @( $t1, $t2, $t3 )
$things
$things | Out-Default
$things | Out-String
#>

#
# The functions below have a lot in common with each other. So much so that they are
# generated by a script in the AfeProxies directly (don't edit these here; edit the source
# files under AfeProxy).
#


function Out-Default
{
    [CmdletBinding( HelpUri = 'http://go.microsoft.com/fwlink/?LinkID=113362', RemotingCapability = 'None' )]
    param( [Parameter( ValueFromPipeline = $true )]
           [PSObject] ${InputObject}
         )

    begin
    {
        [bool] $private:originalColorSupport = $HostSupportsColor
        [bool] $private:enableDebugSpew = $false
        if( $__formatDefaultProxyDebugSpew )
        {
            $enableDebugSpew = $true
        }

        #[Console]::WriteLine( "Out-Default proxy begin" )
        try
        {
            $private:currentFormatInfo = $null
            $private:currentSteppablePipeline = $null
            # TODO: What if someone else is also trying to proxy Out-Default?
            $private:outDefaultWrappedCmd = $ExecutionContext.InvokeCommand.GetCommand( 'Out-Default', [System.Management.Automation.CommandTypes]::Cmdlet )

            $private:outBuffer = $null
            if( $PSBoundParameters.TryGetValue( 'OutBuffer', [ref] $outBuffer ) )
            {
                $PSBoundParameters[ 'OutBuffer' ] = 1
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "Out-Default begin: OH NOES: $e" )
            throw
        }
        finally { }
    } # end begin block

    process
    {
        if( $enableDebugSpew )
        {
            [Console]::WriteLine( "        ======== Out-Default proxy: process ========" )
            [Console]::WriteLine( '        $PSBoundParameters:' )
            foreach( $key in $PSBoundParameters.Keys )
            {
                $val = $PSBoundParameters[ $key ]
                if( $null -eq $val ) { $val = "<null>" }
                [Console]::WriteLine( "            [$($key)]: $($val)" )
            }
        }
        try
        {
            $private:objToDealWith = $null
            [bool] $private:bindUsingInputObject = $false
            if( $null -eq $_ )
            {
                if( $enableDebugSpew )
                {
                    [Console]::WriteLine( "        OdProxy: Dollar-underbar is null." )
                }
                $objToDealWith = $InputObject
                $bindUsingInputObject = $true
            }
            else
            {
                if( $enableDebugSpew )
                {
                    [Console]::WriteLine( "        OdProxy: Dollar-underbar object of type: {0}", $_.GetType().FullName )
                }
                $objToDealWith = $_
                # Things get messed up in the child steppable pipeline if both $_ and
                # $InputObject are set. (I don't know why; they're both set here...)
                [void] $PSBoundParameters.Remove( 'InputObject' )
            }

            if( $null -eq $objToDealWith ) # TODO: Do I need to handle [System.Management.Automation.Internal.AutomationNull]::Value?
            {
                return
            }

            $private:enumerable = GetEnumerable $objToDealWith
            if( $null -ne $enumerable )
            {
                $null = $PSBoundParameters.Remove( 'InputObject' )
                Enumerate $enumerable | Out-Default @PSBoundParameters
                return
            } # end if( it's enumerable )


            $private:formatInfo = Get-AltFormatViewDef -ForObject $objToDealWith

            if( $null -eq $formatInfo )
            {
                #[console]::WriteLine( "Did not find a formatInfo." )
                if( ($null -ne $currentFormatInfo) -or ($null -eq $currentSteppablePipeline) )
                {
                    # Need to start up an Out-Default pipeline
                    if( $null -ne $currentSteppablePipeline )
                    {
                        $currentSteppablePipeline.End()
                    }

                    $global:HostSupportsColor = $false
                    $private:outDefaultScriptCmd = { & $private:outDefaultWrappedCmd @PSBoundParameters  }
                    $currentSteppablePipeline = $private:outDefaultScriptCmd.GetSteppablePipeline( $myInvocation.CommandOrigin )
                    $currentSteppablePipeline.Begin( $PSCmdlet )
                }
                # else we keep using the $currentSteppablePipeline
            }
            else
            {
                if( ($null -eq $currentFormatInfo) -or
                    ($currentFormatInfo -ne $formatInfo) )
                {
                    #[console]::WriteLine( "Starting a new pipeline for a known formatInfo of type {0}.", $formatInfo.GetType().FullName )
                    # Need to start up a new pipeline for the new formatInfo.
                    if( $null -ne $currentSteppablePipeline )
                    {
                        $currentSteppablePipeline.End()
                        $global:HostSupportsColor = $originalColorSupport
                    }

                    $private:fullCmdName = $formatInfo.FormatCommand
                    if( $null -ne $formatInfo.Module )
                    {
                        $fullCmdName = $formatInfo.Module + '\' + $fullCmdName
                    }

                    $private:formatCmd = $ExecutionContext.InvokeCommand.GetCommand( $fullCmdName, [System.Management.Automation.CommandTypes]::All )
                    # TODO: what if we can't find it

                    # TODO: comment below accurate?

                    # Notice that we pipe the results to the original
                    # Out-Default, else we wouldn't see any output.
                    $private:scriptCmd = { & $formatCmd -FormatInfo $formatInfo @PSBoundParameters | & $private:outDefaultWrappedCmd  }
                    $currentSteppablePipeline = $scriptCmd.GetSteppablePipeline( $myInvocation.CommandOrigin )
                    $currentSteppablePipeline.Begin( $PSCmdlet )
                }
                else
                {
                    #[console]::WriteLine( "Using existing pipeline for a known formatInfo of type {0}.", $currentFormatInfo.GetType().FullName )
                }
            }

            # TODO: Assert $null -ne $currentSteppablePipeline
            $private:currentFormatInfo = $formatInfo
            if( $bindUsingInputObject )
            {
                $currentSteppablePipeline.Process( $null )
            }
            else
            {
                $currentSteppablePipeline.Process( $objToDealWith )
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "Out-Default: OH NOES: $e" )
            $global:HostSupportsColor = $originalColorSupport
            throw
        }
        finally { }
    } # end process block

    end
    {
        try
        {
            if( $null -ne $currentSteppablePipeline )
            {
                $currentSteppablePipeline.End()
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "Out-Default end: OH NOES: $e" )
            throw
        }
        finally
        {
            $global:HostSupportsColor = $originalColorSupport
        }
    } # end end block
<#
.ForwardHelpTargetName Out-Default
.ForwardHelpCategory Cmdlet
#>
} # end Out-Default proxy


function Out-String
{
    [CmdletBinding( HelpUri = 'http://go.microsoft.com/fwlink/?LinkID=113368', RemotingCapability = 'None' )]
    param( [switch] ${Stream},

           [ValidateRange( 2, 2147483647 )]
           [int] ${Width},

           [Parameter( ValueFromPipeline = $true )]
           [PSObject] ${InputObject}
         )

    begin
    {
        [bool] $private:originalColorSupport = $HostSupportsColor
        [bool] $private:enableDebugSpew = $false
        if( $__formatStringProxyDebugSpew )
        {
            $enableDebugSpew = $true
        }

        #[Console]::WriteLine( "Out-String proxy begin" )
        try
        {
            $private:currentFormatInfo = $null
            $private:currentSteppablePipeline = $null
            # TODO: What if someone else is also trying to proxy Out-String?
            $private:outStringWrappedCmd = Get-Command 'OutStringColorShim'

            $private:outBuffer = $null
            if( $PSBoundParameters.TryGetValue( 'OutBuffer', [ref] $outBuffer ) )
            {
                $PSBoundParameters[ 'OutBuffer' ] = 1
            }
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
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "Out-String begin: OH NOES: $e" )
            throw
        }
        finally { }
    } # end begin block

    process
    {
        if( $enableDebugSpew )
        {
            [Console]::WriteLine( "        ======== Out-String proxy: process ========" )
            [Console]::WriteLine( '        $PSBoundParameters:' )
            foreach( $key in $PSBoundParameters.Keys )
            {
                $val = $PSBoundParameters[ $key ]
                if( $null -eq $val ) { $val = "<null>" }
                [Console]::WriteLine( "            [$($key)]: $($val)" )
            }
        }
        try
        {
            $private:objToDealWith = $null
            [bool] $private:bindUsingInputObject = $false
            if( $null -eq $_ )
            {
                if( $enableDebugSpew )
                {
                    [Console]::WriteLine( "        OsProxy: Dollar-underbar is null." )
                }
                $objToDealWith = $InputObject
                $bindUsingInputObject = $true
            }
            else
            {
                if( $enableDebugSpew )
                {
                    [Console]::WriteLine( "        OsProxy: Dollar-underbar object of type: {0}", $_.GetType().FullName )
                }
                $objToDealWith = $_
                # Things get messed up in the child steppable pipeline if both $_ and
                # $InputObject are set. (I don't know why; they're both set here...)
                [void] $PSBoundParameters.Remove( 'InputObject' )
            }

            if( $null -eq $objToDealWith ) # TODO: Do I need to handle [System.Management.Automation.Internal.AutomationNull]::Value?
            {
                return
            }

            $private:enumerable = GetEnumerable $objToDealWith
            if( $null -ne $enumerable )
            {
                $null = $PSBoundParameters.Remove( 'InputObject' )
                Enumerate $enumerable | Out-String @PSBoundParameters
                return
            } # end if( it's enumerable )


            $private:formatInfo = Get-AltFormatViewDef -ForObject $objToDealWith

            if( $null -eq $formatInfo )
            {
                #[console]::WriteLine( "Did not find a formatInfo." )
                if( ($null -ne $currentFormatInfo) -or ($null -eq $currentSteppablePipeline) )
                {
                    # Need to start up an Out-String pipeline
                    if( $null -ne $currentSteppablePipeline )
                    {
                        $currentSteppablePipeline.End()
                    }

                    $global:HostSupportsColor = $false
                    $private:outStringScriptCmd = { & $private:outStringWrappedCmd @PSBoundParameters @OutStringOnlyParams }
                    $currentSteppablePipeline = $private:outStringScriptCmd.GetSteppablePipeline( $myInvocation.CommandOrigin )
                    $currentSteppablePipeline.Begin( $PSCmdlet )
                }
                # else we keep using the $currentSteppablePipeline
            }
            else
            {
                if( ($null -eq $currentFormatInfo) -or
                    ($currentFormatInfo -ne $formatInfo) )
                {
                    #[console]::WriteLine( "Starting a new pipeline for a known formatInfo of type {0}.", $formatInfo.GetType().FullName )
                    # Need to start up a new pipeline for the new formatInfo.
                    if( $null -ne $currentSteppablePipeline )
                    {
                        $currentSteppablePipeline.End()
                        $global:HostSupportsColor = $originalColorSupport
                    }

                    $private:fullCmdName = $formatInfo.FormatCommand
                    if( $null -ne $formatInfo.Module )
                    {
                        $fullCmdName = $formatInfo.Module + '\' + $fullCmdName
                    }

                    $private:formatCmd = $ExecutionContext.InvokeCommand.GetCommand( $fullCmdName, [System.Management.Automation.CommandTypes]::All )
                    # TODO: what if we can't find it

                    # TODO: comment below accurate?

                    # Notice that we pipe the results to the original
                    # Out-String, else we wouldn't see any output.
                    $private:scriptCmd = { & $formatCmd -FormatInfo $formatInfo @PSBoundParameters | & $private:outStringWrappedCmd @OutStringOnlyParams }
                    $currentSteppablePipeline = $scriptCmd.GetSteppablePipeline( $myInvocation.CommandOrigin )
                    $currentSteppablePipeline.Begin( $PSCmdlet )
                }
                else
                {
                    #[console]::WriteLine( "Using existing pipeline for a known formatInfo of type {0}.", $currentFormatInfo.GetType().FullName )
                }
            }

            # TODO: Assert $null -ne $currentSteppablePipeline
            $private:currentFormatInfo = $formatInfo
            if( $bindUsingInputObject )
            {
                $currentSteppablePipeline.Process( $null )
            }
            else
            {
                $currentSteppablePipeline.Process( $objToDealWith )
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "Out-String: OH NOES: $e" )
            $global:HostSupportsColor = $originalColorSupport
            throw
        }
        finally { }
    } # end process block

    end
    {
        try
        {
            if( $null -ne $currentSteppablePipeline )
            {
                $currentSteppablePipeline.End()
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "Out-String end: OH NOES: $e" )
            throw
        }
        finally
        {
            $global:HostSupportsColor = $originalColorSupport
        }
    } # end end block
<#
.ForwardHelpTargetName Out-String
.ForwardHelpCategory Cmdlet
#>
} # end Out-String proxy


function Format-Table
{
    [CmdletBinding( HelpUri = 'http://go.microsoft.com/fwlink/?LinkID=113303' )]
    param( [switch] ${AutoSize},

           [switch] ${HideTableHeaders},

           [switch] ${Wrap},

           [Parameter( Position = 0 )]
           [System.Object[]] ${Property},

           [System.Object] ${GroupBy},

           [string] ${View},

           [switch] ${ShowError},

           [switch] ${DisplayError},

           [Alias( 'f' )]
           [switch] ${Force},

           [ValidateSet( 'CoreOnly', 'EnumOnly', 'Both' )]
           [string] ${Expand},

           [Parameter( ValueFromPipeline = $true )]
           [psobject] ${InputObject},

           [Parameter( Mandatory = $false )]
           [MS.Dbg.Formatting.IFormatInfo] ${FormatInfo}
         )

    begin
    {
        [bool] $private:originalColorSupport = $HostSupportsColor
        [bool] $private:useSuppliedView = $false
        [bool] $private:enableDebugSpew = $false
        if( $__formatTableProxyDebugSpew )
        {
            $enableDebugSpew = $true
        }

        try
        {
            if( [string]::IsNullOrEmpty( $Expand ) )
            {
                $Expand = 'EnumOnly'
            }

            $private:currentTableViewDef = $null
            $private:currentSteppablePipeline = $null

            $private:outBuffer = $null
            if( $PSBoundParameters.TryGetValue( 'OutBuffer', [ref] $outBuffer ) )
            {
                $PSBoundParameters[ 'OutBuffer' ] = 1
            }

            $private:tmp = $null
            if( $PSBoundParameters.TryGetValue( 'Expand', [ref] $tmp ) )
            {
                # Don't do enumeration recursively.
                $PSBoundParameters[ 'Expand' ] = 'CoreOnly'
            }

            if( $null -ne $FormatInfo )
            {
                $useSuppliedView = $true
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "OH NOES (Format-Table begin): $e" )
            throw
        }
        finally { }
    } # end 'begin' block

    process
    {
        if( $enableDebugSpew )
        {
            [Console]::WriteLine( "        ======== Format-Table proxy: process ========" )
            [Console]::WriteLine( '        $PSBoundParameters:' )
            foreach( $key in $PSBoundParameters.Keys )
            {
                $val = $PSBoundParameters[ $key ]
                if( $null -eq $val ) { $val = "<null>" }
                [Console]::WriteLine( "            [$($key)]: $($val)" )
            }
        }
        try
        {
            $private:objToDealWith = $null
            [bool] $private:bindUsingInputObject = $false
            if( $null -eq $_ )
            {
                if( $enableDebugSpew )
                {
                    [Console]::WriteLine( "        FtProxy: Dollar-underbar is null." )
                    [Console]::WriteLine( "        FtProxy: `$InputObject is type: {0}", $InputObject.GetType().FullName )
                }
                $objToDealWith = $InputObject
                $bindUsingInputObject = $true
            }
            else
            {
                if( $enableDebugSpew )
                {
                    [Console]::WriteLine( "        FtProxy: Dollar-underbar object of type: {0}", $_.GetType().FullName )
                }
                $objToDealWith = $_
                # Things get messed up in the child steppable pipeline if both $_ and
                # $InputObject are set. (I don't know why; they're both set here...)
                [void] $PSBoundParameters.Remove( 'InputObject' )
            }

            if( $null -eq $objToDealWith ) # TODO: Do I need to handle [System.Management.Automation.Internal.AutomationNull]::Value?
            {
                return
            }

            # If -Expand Both, then we always format the current object as a list, then
            # enumerate it and apply the desired formatting to those.
            [bool] $private:skipNormalFormatting = $false

            $private:enumerable = GetEnumerable $objToDealWith
            if( $null -ne $enumerable )
            {
                if( $Expand -eq 'CoreOnly' )
                {
                    # Nothing to do. Proceed as normal.
                }
                elseif( $Expand -eq 'EnumOnly' )
                {
                    $null = $PSBoundParameters.Remove( 'InputObject' )
                    $PSBoundParameters[ 'Expand' ] = 'CoreOnly'
                    Enumerate $enumerable | Format-Table @PSBoundParameters
                    return
                }
                elseif( $Expand -eq 'Both' )
                {
                    "The following object supports IEnumerable:`n"
                    # This matches the behavior of built-in F+O.
                    $skipNormalFormatting = $true
                    Format-List -InputObject $objToDealWith -Expand CoreOnly
                }
                else
                {
                    # Should be impossible to get here.
                    throw "Unexpected `$Expand value: $($Expand)."
                }
            } # end if( it's enumerable )


            if( !$skipNormalFormatting )
            {
                $private:_formatInfo = $null

                if( $useSuppliedView )
                {
                    ${_formatInfo} = $FormatInfo
                }
                else
                {
                    ${_formatInfo} = Get-AltFormatViewDef -ForObject $objToDealWith `
                                                          -FormatInfoType ([MS.Dbg.Formatting.AltTableViewDefinition])
                }

                if( $null -eq ${_formatInfo} )
                {
                    if( ($null -ne $private:currentTableViewDef) -or ($null -eq $currentSteppablePipeline) )
                    {
                        # Need to start up a Format-Table pipeline
                        if( $null -ne $currentSteppablePipeline )
                        {
                            $currentSteppablePipeline.End()
                        }

                        $global:HostSupportsColor = $false
                        $private:formatTableWrappedCmd = $ExecutionContext.InvokeCommand.GetCommand( 'Format-Table', [System.Management.Automation.CommandTypes]::Cmdlet )

                        $private:formatTableScriptCmd = { & $private:formatTableWrappedCmd @PSBoundParameters | OutStringColorShim -Stream | TrimStream }
                        #$private:formatTableScriptCmd = { & $private:formatTableWrappedCmd @PSBoundParameters }
                        $currentSteppablePipeline = $private:formatTableScriptCmd.GetSteppablePipeline( $myInvocation.CommandOrigin )
                        $currentSteppablePipeline.Begin( $PSCmdlet )
                    }
                    # else we keep using the $currentSteppablePipeline
                }
                else
                {
                    if( ($null -eq $private:currentTableViewDef) -or
                        ($private:currentTableViewDef -ne ${_formatInfo}) )
                    {
                        # Need to start up a new pipeline for the new _formatInfo.
                        if( $null -ne $currentSteppablePipeline )
                        {
                            $currentSteppablePipeline.End()
                            $global:HostSupportsColor = $originalColorSupport
                        }

                        $private:fullCmdName = 'Debugger\Format-AltTable'

                        $private:formatCmd = $ExecutionContext.InvokeCommand.GetCommand( $fullCmdName, [System.Management.Automation.CommandTypes]::All )

                        [void] $PSBoundParameters.Remove( 'FormatInfo' ) # in case it was explicitly passed.
                        $private:scriptCmd = { & $formatCmd -FormatInfo ${_formatInfo} @PSBoundParameters }
                        $currentSteppablePipeline = $scriptCmd.GetSteppablePipeline( $myInvocation.CommandOrigin )
                        $currentSteppablePipeline.Begin( $PSCmdlet )
                    }
                }

                # TODO: Assert $null -ne $currentSteppablePipeline
                $private:currentTableViewDef = ${_formatInfo}
                if( $bindUsingInputObject )
                {
                    $currentSteppablePipeline.Process( $null )
                }
                else
                {
                    $currentSteppablePipeline.Process( $objToDealWith )
                }
            } # end if( !$skipNormalFormatting )

            if( ($null -ne $enumerable) -and ($Expand -eq 'Both') )
            {
                if( $null -ne $currentSteppablePipeline )
                {
                    # We need to end this first. The built-in Out-Default does not like to
                    # be recursive, and if we wait until after we do the enumeration to
                    # end this pipeline, when we end the Out-Default, it sends out some
                    # GroupEnd and FormatEnd formatting objects, and they get unhappy.
                    $currentSteppablePipeline.End()
                    $currentSteppablePipeline = $null
                }

                # The built-in F+O counts how many objects in the enumerable to put into this message. Why do that?
                "`nThe IEnumerable contains the following objects:`n"
                #foreach( $eItem in $enumerable )
                #{
                #    $PSBoundParameters[ 'InputObject' ] = $eItem
                #    $PSBoundParameters[ 'Expand' ] = 'CoreOnly'
                #    Format-Table @PSBoundParameters
                #}

                [void] $PSBoundParameters.Remove( 'InputObject' )
                $PSBoundParameters[ 'Expand' ] = 'CoreOnly'
                Enumerate $enumerable | Format-Table @PSBoundParameters
            } # end if( $enumerable )
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "OH NOES (Format-Table process): $e" )
            $global:HostSupportsColor = $originalColorSupport
            throw
        }
        finally { }
    } # end 'process' block

    end
    {
        try
        {
            if( $null -ne $currentSteppablePipeline )
            {
                $currentSteppablePipeline.End()
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "OH NOES (Format-Table end): $e" )
            throw
        }
        finally
        {
            $global:HostSupportsColor = $originalColorSupport
        }
    } # end 'end' block
<#
.ForwardHelpTargetName Format-Table
.ForwardHelpCategory Cmdlet
#>
} # end Format-Table proxy


function Format-List
{
    [CmdletBinding( HelpUri = 'http://go.microsoft.com/fwlink/?LinkID=113302' )]
    param( [Parameter( Position = 0 )]
           [System.Object[]] ${Property},

           [System.Object] ${GroupBy},

           [string] ${View},

           [switch] ${ShowError},

           [switch] ${DisplayError},

           [Alias( 'f' )]
           [switch] ${Force},

           [ValidateSet( 'CoreOnly', 'EnumOnly', 'Both' )]
           [string] ${Expand},

           [Parameter( ValueFromPipeline = $true )]
           [psobject] ${InputObject},

           [Parameter( Mandatory = $false )]
           [MS.Dbg.Formatting.IFormatInfo] ${FormatInfo}
         )

    begin
    {
        [bool] $private:originalColorSupport = $HostSupportsColor
        [bool] $private:useSuppliedView = $false
        [bool] $private:enableDebugSpew = $false
        if( $__formatListProxyDebugSpew )
        {
            $enableDebugSpew = $true
        }

        try
        {
            if( [string]::IsNullOrEmpty( $Expand ) )
            {
                $Expand = 'EnumOnly'
            }

            $private:currentListViewDef = $null
            $private:currentSteppablePipeline = $null

            $private:outBuffer = $null
            if( $PSBoundParameters.TryGetValue( 'OutBuffer', [ref] $outBuffer ) )
            {
                $PSBoundParameters[ 'OutBuffer' ] = 1
            }

            $private:tmp = $null
            if( $PSBoundParameters.TryGetValue( 'Expand', [ref] $tmp ) )
            {
                # Don't do enumeration recursively.
                $PSBoundParameters[ 'Expand' ] = 'CoreOnly'
            }

            if( $null -ne $FormatInfo )
            {
                $useSuppliedView = $true
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "OH NOES (Format-List begin): $e" )
            throw
        }
        finally { }
    } # end 'begin' block

    process
    {
        if( $enableDebugSpew )
        {
            [Console]::WriteLine( "        ======== Format-List proxy: process ========" )
            [Console]::WriteLine( '        $PSBoundParameters:' )
            foreach( $key in $PSBoundParameters.Keys )
            {
                $val = $PSBoundParameters[ $key ]
                if( $null -eq $val ) { $val = "<null>" }
                [Console]::WriteLine( "            [$($key)]: $($val)" )
            }
        }
        try
        {
            $private:objToDealWith = $null
            [bool] $private:bindUsingInputObject = $false
            if( $null -eq $_ )
            {
                if( $enableDebugSpew )
                {
                    [Console]::WriteLine( "        FlProxy: Dollar-underbar is null." )
                    [Console]::WriteLine( "        FlProxy: `$InputObject is type: {0}", $InputObject.GetType().FullName )
                }
                $objToDealWith = $InputObject
                $bindUsingInputObject = $true
            }
            else
            {
                if( $enableDebugSpew )
                {
                    [Console]::WriteLine( "        FlProxy: Dollar-underbar object of type: {0}", $_.GetType().FullName )
                }
                $objToDealWith = $_
                # Things get messed up in the child steppable pipeline if both $_ and
                # $InputObject are set. (I don't know why; they're both set here...)
                [void] $PSBoundParameters.Remove( 'InputObject' )
            }

            if( $null -eq $objToDealWith ) # TODO: Do I need to handle [System.Management.Automation.Internal.AutomationNull]::Value?
            {
                return
            }

            # If -Expand Both, then we always format the current object as a list, then
            # enumerate it and apply the desired formatting to those.
            [bool] $private:skipNormalFormatting = $false

            $private:enumerable = GetEnumerable $objToDealWith
            if( $null -ne $enumerable )
            {
                if( $Expand -eq 'CoreOnly' )
                {
                    # Nothing to do. Proceed as normal.
                }
                elseif( $Expand -eq 'EnumOnly' )
                {
                    $null = $PSBoundParameters.Remove( 'InputObject' )
                    $PSBoundParameters[ 'Expand' ] = 'CoreOnly'
                    Enumerate $enumerable | Format-List @PSBoundParameters
                    return
                }
                elseif( $Expand -eq 'Both' )
                {
                    "The following object supports IEnumerable:`n"
                    # This matches the behavior of built-in F+O.
                    $skipNormalFormatting = $true
                    Format-List -InputObject $objToDealWith -Expand CoreOnly
                }
                else
                {
                    # Should be impossible to get here.
                    throw "Unexpected `$Expand value: $($Expand)."
                }
            } # end if( it's enumerable )


            if( !$skipNormalFormatting )
            {
                $private:_formatInfo = $null

                if( $useSuppliedView )
                {
                    ${_formatInfo} = $FormatInfo
                }
                else
                {
                    ${_formatInfo} = Get-AltFormatViewDef -ForObject $objToDealWith `
                                                          -FormatInfoType ([MS.Dbg.Formatting.AltListViewDefinition])
                }

                if( $null -eq ${_formatInfo} )
                {
                    if( ($null -ne $private:currentListViewDef) -or ($null -eq $currentSteppablePipeline) )
                    {
                        # Need to start up a Format-List pipeline
                        if( $null -ne $currentSteppablePipeline )
                        {
                            $currentSteppablePipeline.End()
                        }

                        $global:HostSupportsColor = $false
                        $private:formatListWrappedCmd = $ExecutionContext.InvokeCommand.GetCommand( 'Format-List', [System.Management.Automation.CommandTypes]::Cmdlet )

                        $private:formatListScriptCmd = { & $private:formatListWrappedCmd @PSBoundParameters | OutStringColorShim -Stream | TrimStream }
                        #$private:formatListScriptCmd = { & $private:formatListWrappedCmd @PSBoundParameters }
                        $currentSteppablePipeline = $private:formatListScriptCmd.GetSteppablePipeline( $myInvocation.CommandOrigin )
                        $currentSteppablePipeline.Begin( $PSCmdlet )
                    }
                    # else we keep using the $currentSteppablePipeline
                }
                else
                {
                    if( ($null -eq $private:currentListViewDef) -or
                        ($private:currentListViewDef -ne ${_formatInfo}) )
                    {
                        # Need to start up a new pipeline for the new _formatInfo.
                        if( $null -ne $currentSteppablePipeline )
                        {
                            $currentSteppablePipeline.End()
                            $global:HostSupportsColor = $originalColorSupport
                        }

                        $private:fullCmdName = 'Debugger\Format-AltList'

                        $private:formatCmd = $ExecutionContext.InvokeCommand.GetCommand( $fullCmdName, [System.Management.Automation.CommandTypes]::All )

                        [void] $PSBoundParameters.Remove( 'FormatInfo' ) # in case it was explicitly passed.
                        $private:scriptCmd = { & $formatCmd -FormatInfo ${_formatInfo} @PSBoundParameters }
                        $currentSteppablePipeline = $scriptCmd.GetSteppablePipeline( $myInvocation.CommandOrigin )
                        $currentSteppablePipeline.Begin( $PSCmdlet )
                    }
                }

                # TODO: Assert $null -ne $currentSteppablePipeline
                $private:currentListViewDef = ${_formatInfo}
                if( $bindUsingInputObject )
                {
                    $currentSteppablePipeline.Process( $null )
                }
                else
                {
                    $currentSteppablePipeline.Process( $objToDealWith )
                }
            } # end if( !$skipNormalFormatting )

            if( ($null -ne $enumerable) -and ($Expand -eq 'Both') )
            {
                if( $null -ne $currentSteppablePipeline )
                {
                    # We need to end this first. The built-in Out-Default does not like to
                    # be recursive, and if we wait until after we do the enumeration to
                    # end this pipeline, when we end the Out-Default, it sends out some
                    # GroupEnd and FormatEnd formatting objects, and they get unhappy.
                    $currentSteppablePipeline.End()
                    $currentSteppablePipeline = $null
                }

                # The built-in F+O counts how many objects in the enumerable to put into this message. Why do that?
                "`nThe IEnumerable contains the following objects:`n"
                #foreach( $eItem in $enumerable )
                #{
                #    $PSBoundParameters[ 'InputObject' ] = $eItem
                #    $PSBoundParameters[ 'Expand' ] = 'CoreOnly'
                #    Format-List @PSBoundParameters
                #}

                [void] $PSBoundParameters.Remove( 'InputObject' )
                $PSBoundParameters[ 'Expand' ] = 'CoreOnly'
                Enumerate $enumerable | Format-List @PSBoundParameters
            } # end if( $enumerable )
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "OH NOES (Format-List process): $e" )
            $global:HostSupportsColor = $originalColorSupport
            throw
        }
        finally { }
    } # end 'process' block

    end
    {
        try
        {
            if( $null -ne $currentSteppablePipeline )
            {
                $currentSteppablePipeline.End()
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "OH NOES (Format-List end): $e" )
            throw
        }
        finally
        {
            $global:HostSupportsColor = $originalColorSupport
        }
    } # end 'end' block
<#
.ForwardHelpTargetName Format-List
.ForwardHelpCategory Cmdlet
#>
} # end Format-List proxy


function Format-Custom
{
    [CmdletBinding( HelpUri = 'http://go.microsoft.com/fwlink/?LinkID=113301' )]
    param( [Parameter( Position = 0 )]
           [System.Object[]] ${Property},

           [ValidateRange( 1, 2147483647 )]
           [int] ${Depth},

           [System.Object] ${GroupBy},

           [string] ${View},

           [switch] ${ShowError},

           [switch] ${DisplayError},

           [Alias( 'f' )]
           [switch] ${Force},

           [ValidateSet( 'CoreOnly', 'EnumOnly', 'Both' )]
           [string] ${Expand},

           [Parameter( ValueFromPipeline = $true )]
           [psobject] ${InputObject},

           [Parameter( Mandatory = $false )]
           [MS.Dbg.Formatting.IFormatInfo] ${FormatInfo}
         )

    begin
    {
        [bool] $private:originalColorSupport = $HostSupportsColor
        [bool] $private:useSuppliedView = $false
        [bool] $private:enableDebugSpew = $false
        if( $__formatCustomProxyDebugSpew )
        {
            $enableDebugSpew = $true
        }

        try
        {
            if( [string]::IsNullOrEmpty( $Expand ) )
            {
                $Expand = 'EnumOnly'
            }

            $private:currentCustomViewDef = $null
            $private:currentSteppablePipeline = $null

            $private:outBuffer = $null
            if( $PSBoundParameters.TryGetValue( 'OutBuffer', [ref] $outBuffer ) )
            {
                $PSBoundParameters[ 'OutBuffer' ] = 1
            }

            $private:tmp = $null
            if( $PSBoundParameters.TryGetValue( 'Expand', [ref] $tmp ) )
            {
                # Don't do enumeration recursively.
                $PSBoundParameters[ 'Expand' ] = 'CoreOnly'
            }

            if( $null -ne $FormatInfo )
            {
                $useSuppliedView = $true
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "OH NOES (Format-Custom begin): $e" )
            throw
        }
        finally { }
    } # end 'begin' block

    process
    {
        if( $enableDebugSpew )
        {
            [Console]::WriteLine( "        ======== Format-Custom proxy: process ========" )
            [Console]::WriteLine( '        $PSBoundParameters:' )
            foreach( $key in $PSBoundParameters.Keys )
            {
                $val = $PSBoundParameters[ $key ]
                if( $null -eq $val ) { $val = "<null>" }
                [Console]::WriteLine( "            [$($key)]: $($val)" )
            }
        }
        try
        {
            $private:objToDealWith = $null
            [bool] $private:bindUsingInputObject = $false
            if( $null -eq $_ )
            {
                if( $enableDebugSpew )
                {
                    [Console]::WriteLine( "        FcProxy: Dollar-underbar is null." )
                    [Console]::WriteLine( "        FcProxy: `$InputObject is type: {0}", $InputObject.GetType().FullName )
                }
                $objToDealWith = $InputObject
                $bindUsingInputObject = $true
            }
            else
            {
                if( $enableDebugSpew )
                {
                    [Console]::WriteLine( "        FcProxy: Dollar-underbar object of type: {0}", $_.GetType().FullName )
                }
                $objToDealWith = $_
                # Things get messed up in the child steppable pipeline if both $_ and
                # $InputObject are set. (I don't know why; they're both set here...)
                [void] $PSBoundParameters.Remove( 'InputObject' )
            }

            if( $null -eq $objToDealWith ) # TODO: Do I need to handle [System.Management.Automation.Internal.AutomationNull]::Value?
            {
                return
            }

            # If -Expand Both, then we always format the current object as a list, then
            # enumerate it and apply the desired formatting to those.
            [bool] $private:skipNormalFormatting = $false

            $private:enumerable = GetEnumerable $objToDealWith
            if( $null -ne $enumerable )
            {
                if( $Expand -eq 'CoreOnly' )
                {
                    # Nothing to do. Proceed as normal.
                }
                elseif( $Expand -eq 'EnumOnly' )
                {
                    $null = $PSBoundParameters.Remove( 'InputObject' )
                    $PSBoundParameters[ 'Expand' ] = 'CoreOnly'
                    Enumerate $enumerable | Format-Custom @PSBoundParameters
                    return
                }
                elseif( $Expand -eq 'Both' )
                {
                    "The following object supports IEnumerable:`n"
                    # This matches the behavior of built-in F+O.
                    $skipNormalFormatting = $true
                    Format-List -InputObject $objToDealWith -Expand CoreOnly
                }
                else
                {
                    # Should be impossible to get here.
                    throw "Unexpected `$Expand value: $($Expand)."
                }
            } # end if( it's enumerable )


            if( !$skipNormalFormatting )
            {
                $private:_formatInfo = $null

                if( $useSuppliedView )
                {
                    ${_formatInfo} = $FormatInfo
                }
                else
                {
                    ${_formatInfo} = Get-AltFormatViewDef -ForObject $objToDealWith `
                                                          -FormatInfoType ([MS.Dbg.Formatting.AltCustomViewDefinition])
                }

                if( $null -eq ${_formatInfo} )
                {
                    if( ($null -ne $private:currentCustomViewDef) -or ($null -eq $currentSteppablePipeline) )
                    {
                        # Need to start up a Format-Custom pipeline
                        if( $null -ne $currentSteppablePipeline )
                        {
                            $currentSteppablePipeline.End()
                        }

                        $global:HostSupportsColor = $false
                        $private:formatCustomWrappedCmd = $ExecutionContext.InvokeCommand.GetCommand( 'Format-Custom', [System.Management.Automation.CommandTypes]::Cmdlet )

                        $private:formatCustomScriptCmd = { & $private:formatCustomWrappedCmd @PSBoundParameters | OutStringColorShim -Stream | TrimStream }
                        #$private:formatCustomScriptCmd = { & $private:formatCustomWrappedCmd @PSBoundParameters }
                        $currentSteppablePipeline = $private:formatCustomScriptCmd.GetSteppablePipeline( $myInvocation.CommandOrigin )
                        $currentSteppablePipeline.Begin( $PSCmdlet )
                    }
                    # else we keep using the $currentSteppablePipeline
                }
                else
                {
                    if( ($null -eq $private:currentCustomViewDef) -or
                        ($private:currentCustomViewDef -ne ${_formatInfo}) )
                    {
                        # Need to start up a new pipeline for the new _formatInfo.
                        if( $null -ne $currentSteppablePipeline )
                        {
                            $currentSteppablePipeline.End()
                            $global:HostSupportsColor = $originalColorSupport
                        }

                        $private:fullCmdName = 'Debugger\Format-AltCustom'

                        $private:formatCmd = $ExecutionContext.InvokeCommand.GetCommand( $fullCmdName, [System.Management.Automation.CommandTypes]::All )

                        [void] $PSBoundParameters.Remove( 'FormatInfo' ) # in case it was explicitly passed.
                        $private:scriptCmd = { & $formatCmd -FormatInfo ${_formatInfo} @PSBoundParameters }
                        $currentSteppablePipeline = $scriptCmd.GetSteppablePipeline( $myInvocation.CommandOrigin )
                        $currentSteppablePipeline.Begin( $PSCmdlet )
                    }
                }

                # TODO: Assert $null -ne $currentSteppablePipeline
                $private:currentCustomViewDef = ${_formatInfo}
                if( $bindUsingInputObject )
                {
                    $currentSteppablePipeline.Process( $null )
                }
                else
                {
                    $currentSteppablePipeline.Process( $objToDealWith )
                }
            } # end if( !$skipNormalFormatting )

            if( ($null -ne $enumerable) -and ($Expand -eq 'Both') )
            {
                if( $null -ne $currentSteppablePipeline )
                {
                    # We need to end this first. The built-in Out-Default does not like to
                    # be recursive, and if we wait until after we do the enumeration to
                    # end this pipeline, when we end the Out-Default, it sends out some
                    # GroupEnd and FormatEnd formatting objects, and they get unhappy.
                    $currentSteppablePipeline.End()
                    $currentSteppablePipeline = $null
                }

                # The built-in F+O counts how many objects in the enumerable to put into this message. Why do that?
                "`nThe IEnumerable contains the following objects:`n"
                #foreach( $eItem in $enumerable )
                #{
                #    $PSBoundParameters[ 'InputObject' ] = $eItem
                #    $PSBoundParameters[ 'Expand' ] = 'CoreOnly'
                #    Format-Custom @PSBoundParameters
                #}

                [void] $PSBoundParameters.Remove( 'InputObject' )
                $PSBoundParameters[ 'Expand' ] = 'CoreOnly'
                Enumerate $enumerable | Format-Custom @PSBoundParameters
            } # end if( $enumerable )
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "OH NOES (Format-Custom process): $e" )
            $global:HostSupportsColor = $originalColorSupport
            throw
        }
        finally { }
    } # end 'process' block

    end
    {
        try
        {
            if( $null -ne $currentSteppablePipeline )
            {
                $currentSteppablePipeline.End()
            }
        }
        catch
        {
            $e = $_
            [System.Console]::WriteLine( "OH NOES (Format-Custom end): $e" )
            throw
        }
        finally
        {
            $global:HostSupportsColor = $originalColorSupport
        }
    } # end 'end' block
<#
.ForwardHelpTargetName Format-Custom
.ForwardHelpCategory Cmdlet
#>
} # end Format-Custom proxy

