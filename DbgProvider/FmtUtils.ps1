
#
# The following functions are common code shared by multiple format
# definitions (in .psfmt files).
#

$script:ValueNotAvailable = (New-ColorString -Foreground Yellow -Content '<value not available>').MakeReadOnly()

function script:FindFirstNonWhitespace( [string] $line )
{
    $line = $line.TrimEnd()
    #[console]::WriteLine( "checking ({0}): $line", $line.Length )
    if( 0 -eq $line.Length )
    {
        return -1
    }
    else
    {
        for( [int] $idx = 0; $idx -lt $line.Length; $idx++ )
        {
            if( ![Char]::IsWhiteSpace( $line, $idx ) )
            {
                return $idx
            }
        }
        # TODO: assert( false ): we trimmed the string so if there were no
        # non-whitespace characters we shouldn't be able to get here.
    }
} # end function FindFirstNonWhitespace


# Removes leading empty lines and common leading whitespace from each line.
function script:UnindentLines( [string[]] $lines )
{
    [int] $commonLeadingWhitespace = [Int32]::MaxValue
    for( [int] $i = 0; $i -lt $lines.Length; $i++ )
    {
        [string] $line = $lines[ $i ].TrimEnd()
        $lines[ $i ] = $line
        [int] $leadingWhitespace = FindFirstNonWhitespace $line
        if( -1 -ne $leadingWhitespace )
        {
            #[console]::WriteLine( '$leadingWhitespace is {0}', $leadingWhitespace )
            $commonLeadingWhitespace = [Math]::Min( $commonLeadingWhitespace, $leadingWhitespace )
        }
    }

    [bool] $foundNonEmptyLine = $false
    [System.Text.StringBuilder] $sb = New-Object 'System.Text.StringBuilder' -Arg @( $singleString.Length )
    foreach( $line in $lines )
    {
        if( 0 -eq $line.Length )
        {
            if( $foundNonEmptyLine )
            {
                # We'll preserve internal empty lines, but not leading ones.
                [void] $sb.AppendLine()
            }
        }
        else
        {
            #[console]::WriteLine( "Trimming $commonLeadingWhitespace off the front of: $line" )
            $foundNonEmptyLine = $true
            [void] $sb.AppendLine( $line.Substring( $commonLeadingWhitespace ).TrimEnd() )
        }
    }
    return $sb.ToString()
} # end function UnindentLines


function script:RenderScript( [ScriptBlock] $script )
{
    [string] $singleString = $script.ToString()
    [string[]] $lines = $singleString.Split( "`n" )
    UnindentLines $lines
} # end function RenderScript


function script:HighlightTypeName( $obj )
{
    if( $null -eq $obj )
    {
        return
    }

    [Type] $type = $obj.GetType();
    $fullTypeName = $type.FullName
    [int] $idx = $fullTypeName.LastIndexOf( '.' );
    [string] $ns = ''
    if( $idx -gt 0 )
    {
        $ns = $fullTypeName.Substring( 0, $idx + 1 )
    }
    (New-ColorString -Content $ns).AppendPushFgBg( [ConsoleColor]::White, [ConsoleColor]::DarkMagenta ).Append( $type.Name ).AppendPop()
} # end function HighlightTypeName


function script:Get-TypeName( $sym )
{
    try
    {
        if( $null -eq $sym.get_Type() )
        {
            New-ColorString -Foreground Yellow -Content "<type unavailable>"
        }
        else
        {
            if( $sym.Type -is [MS.Dbg.DbgNamedTypeInfo] )
            {
                $sym.Type.get_ColorName();
            }
            else
            {
                Format-DbgTypeName $sym.Type.get_Name()
            }
        }
    } finally { }
} # end function Get-TypeName

# TODO: maybe replace with Format-AltSingleLine that takes -Label, -Width, etc.
function script:Pad( [MS.Dbg.ColorString] $cs, [int] $minLength )
{
    if( $cs.Length -lt $minLength )
    {
        if( $cs.IsReadOnly )
        {
            $cs = (New-ColorString).Append( $cs )
        }
        $cs = $cs.Append( (New-Object 'System.String' -Arg @( ' ', ($minLength - $cs.Length) )) )
    }
    return $cs
}

function script:PadLeft( [MS.Dbg.ColorString] $cs, [int] $minLength )
{
    if( $cs.Length -lt $minLength )
    {
        $cs2 = New-ColorString -Content (New-Object 'System.String' -Arg @( ' ', ($minLength - $cs.Length) ))
        $cs2 = $cs2.Append( $cs )
        $cs = $cs2
    }
    return $cs
}

# TODO: This is not ColorString-aware...
function script:Truncate( [string] $s, [int] $maxLength, [bool] $pad = $false )
{
    try
    {
        if( $maxLength -le 3 )
        {
            throw "Out of range: `$maxLength must be more than 3."
        }

        [int] $diff = $maxLength - $s.Length
        if( $diff -ge 0 )
        {
            if( $pad -and ($diff -gt 0) )
            {
                $s = $s + (New-Object 'System.String' -Arg @( ([char] ' '), $diff ))
            }
            return $s
        }

        return $s.Substring( 0, $maxLength - 3 ) + "..."
    } finally {}
} # end Truncate()

function script:Test-SymValueAvailable( $symVal, [string] $typeName )
{
    $sym = $symVal.DbgGetSymbol()
    if( $sym.get_IsValueUnavailable() )
    {
        if( !$typeName )
        {
            if( $null -ne $sym.get_Type() )
            {
                $typeName = $sym.Type.get_Name()
            }
        }

        if( $typeName )
        {
            return (Format-DbgTypeName $TypeName).Append( ': ' ).Append( $ValueNotAvailable )
        }
        else
        {
            return $ValueNotAvailable
        }
    }
    return $null
} #end Test-SymValueAvailable()


function script:Summarize-SymValue( $symVal )
{
    $notAvailMsg = Test-SymValueAvailable $symVal
    if( $notAvailMsg )
    {
        return $notAvailMsg
    }
    return (Format-AltSingleLine -InputObject $symVal)
} # end Summarize-SymValue

