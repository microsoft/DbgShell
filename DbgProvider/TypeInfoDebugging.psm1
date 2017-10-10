Set-StrictMode -Version Latest

#
# This is just hacky stuff intended for helping debug type stuff; you shouldn't use it.
#

function Get-UdtTypes
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [string] $ModuleName )

    try
    {
        Get-DbgTypeInfo ($ModuleName + '!*') | %{ if( $_.SymTag -eq 'UDT' ) { $_ } }
    } finally { }
}

function Get-UdtClassTypes
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [string] $ModuleName )

    try
    {
        Get-UdtTypes $ModuleName | % { if( (Test-SymGetTypeInfo -ModBase $_.Module.BaseAddress -TypeId $_.TypeId -TypeInfo TI_GET_UDTKIND) -eq 'UdtClass' ) { $_ } }
    } finally { }
}

function Get-UdtStructTypes
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [string] $ModuleName )

    try
    {
        Get-UdtTypes $ModuleName | % { if( (Test-SymGetTypeInfo -ModBase $_.Module.BaseAddress -TypeId $_.TypeId -TypeInfo TI_GET_UDTKIND) -eq 'UdtStruct' ) { $_ } }
    } finally { }
}

function Get-UdtUnionTypes
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [string] $ModuleName )

    try
    {
        Get-UdtTypes $ModuleName | % { if( (Test-SymGetTypeInfo -ModBase $_.Module.BaseAddress -TypeId $_.TypeId -TypeInfo TI_GET_UDTKIND) -eq 'UdtUnion' ) { $_ } }
    } finally { }
}

<# Question: Why do base types sometimes show up a lot:

    $baseTypeTypes = @{}
    Get-BaseTypes -ModuleName TestNativeConsoleApp | %{
        if( !$baseTypeTypes.ContainsKey( $_.TypeId ) ) {
            $baseTypeTypes[ $_.TypeId ] = 1
        } else {
            $baseTypeTypes[ $_.TypeId ]++ }
    }
    $baseTypeTypes
#>
function Get-BaseTypes
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [string] $ModuleName )

    try
    {
        Get-DbgTypeInfo ($ModuleName + '!*') | %{ if( $_.SymTag -eq 'BaseType' ) { $_ } }
    } finally { }
}


function Get-Typedefs
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [string] $ModuleName )

    try
    {
        Get-DbgTypeInfo ($ModuleName + '!*') -NoFollowTypedefs | %{ if( $_.SymTag -eq 'Typedef' ) { $_ } }
    } finally { }
}


<#
.SYNOPSIS
    Helps you find members of a particular type. BUGGY; MISSES LOTS OF THINGS.
#>
function Find-MemberWithType
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0, ValueFromPipeline = $true )]
           [MS.Dbg.DbgSymbol[]] $Symbol,

           [Parameter( Mandatory = $true, Position = 1 )]
           [string] $TypeName,

           [Parameter( Mandatory = $false )]
           #[int] $MaxDepth = [Int32]::MaxValue
           # TODO: I'm limiting depth here not because searching is too slow... but because /canceling/ is too slow. I need to figure out if I can do something about that. Maybe it's only when running under a debugger, but still.
           [int] $MaxDepth = 10

           # TODO: Need to be able to show DbgShell-style path string
           # (i.e. "dfsrs!g_NetworkGlobals.ioPortManager" versus
           #       "dfsrs!g_NetworkGlobals->ioPortManager")
         )

    begin
    {
        $bangIdx = $TypeName.IndexOf( '!' )
        if( $bangIdx -ge 0 )
        {
            # If the '!' is the last character... well, I don't care.
            $TypeName = $TypeName.Substring( $bangIdx + 1 )
        }

        function DePoint( $type )
        {
            while( $type.SymTag -eq 'PointerType' )
            {
                $type = $type.PointeeType
            }
            # TODO: What about arrays?
            return $type
        }

        # TODO: wildcard support
        # If we have wildcard support... what then do we do about pointers?
        function TypeMatches( $type )
        {
            # should already be de-pointed
          # while( $type.SymTag -eq 'PointerType' )
          # {
          #     $type = $type.PointeeType
          # }

            # TODO
          # while( $type.SymTag -eq 'ArrayType' )
          # {
          # }

            #Write-Verbose "   Does it match? $($type.Name) -eq $($TypeName)"
            return $type.Name -eq $TypeName
        } # end TypeMatches

        function SearchSymbol( $sym,
                               [int] $curDepth,
                               [ref] [int] $numHits,
                               $noHitsCache )
        {
            try
            {

                #[Console]::WriteLine( ((New-Object 'System.String' -Arg @( ([char] ' '), $curDepth )) + 'Entering depth {0}'), $curDepth )
                #[Console]::WriteLine( ((New-Object 'System.String' -Arg @( ([char] ' '), $curDepth )) + 'Symbol {0}'), $sym.PathString )
                if( $curDepth -gt $MaxDepth )
                {
                    return
                }

                [int] $origNumHits = $numHits.Value
                [int] $possibleLevelsBelow = $MaxDepth - $curDepth

                $type = DePoint $sym.Type

                [int] $prevSearchedLevels = 0
                if( $noHitsCache.TryGetValue( $type, ([ref] $prevSearchedLevels) ) )
                {
                    if( $prevSearchedLevels -ge $possibleLevelsBelow )
                    {
                        # No point in searching again; we've already searched this type.
                        Write-Verbose "<<<< Already searched $($type.Name) at least $prevSearchedLevels levels."
                        return
                    }
                }

                if( (TypeMatches $type) )
                {
                    $numHits.Value = $numHits.Value + 1
                    $sym.PathString
                }

                Write-Verbose "Searching $($sym.PathString)..."
                foreach( $prop in $sym.Value.PSObject.Properties )
                {
                    #[Console]::WriteLine( "Stopping? {0}", $PSCmdlet.get_Stopping() )
                    if( $PSCmdlet.get_Stopping() )
                    {
                        return
                    }

                    if( $prop.GetType().FullName -ne 'MS.Dbg.PSSymFieldInfo' )
                    {
                        continue
                    }

                    # I could get a lot smarter about this. For instance, cache types where there are no hits underneath.
                    SearchSymbol $prop.Symbol ($curDepth + 1) $numHits $noHitsCache
                } # end foreach( property )

                $enumerable = GetEnumerable $sym.Value
                if( $enumerable -and ($enumerable.Count -gt 0) )
                {
                    $testObj = $sym.Value | Select-Object -First 1
                    if( $testObj.PSObject.Methods.Name -contains 'DbgGetSymbol' )
                    {
                        $sym = $testObj.DbgGetSymbol()
                        SearchSymbol $sym ($curDepth + 1) $numHits $noHitsCache
                    }
                }

                if( $numHits.Value -eq $origNumHits )
                {
                    # No hits found.
                    $noHitsCache[ $type ] = $possibleLevelsBelow
                }
            }
            finally
            {
                #[Console]::WriteLine( ((New-Object 'System.String' -Arg @( ([char] ' '), $curDepth )) + 'Leaving depth {0}'), $curDepth )
                try
                {
                    #[Console]::WriteLine( ((New-Object 'System.String' -Arg @( ([char] ' '), $curDepth )) + 'Stopping? {0}'), $PSCmdlet.get_Stopping() )
                }
                catch
                {
                    #[Console]::WriteLine( 'Uh-oh...' )
                }
            }
        } # end SearchSymbol
    } # end 'begin' block

    end { }
    process
    {
        try
        {
            foreach( $sym in $Symbol )
            {
                if( $PSCmdlet.get_Stopping() )
                {
                    return
                }

                $noHitsCache = New-Object 'System.Collections.Generic.Dictionary[[MS.Dbg.DbgTypeInfo],[System.Int32]]'
                [int] $numHits = 0;
                SearchSymbol $sym 0 ([ref] $numHits) $noHitsCache

                Write-Verbose "Found $($numHits) hits."
            } # end foreach( $sym )
        }
        finally { }
    }
} # end Find-MemberWithType


<#
.SYNOPSIS
    Helps you find members of a particular type. BUGGY; MISSES LOTS OF THINGS.
#>
function Find-MemberWithType2
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0, ValueFromPipeline = $true )]
           [ValidateNotNull()]
           [MS.Dbg.DbgTypeInfo] $UDT,

           [Parameter( Mandatory = $true, Position = 1 )]
           [ValidateNotNullOrEmpty()]
           [string] $TypeName,

           [Parameter( Mandatory = $false )]
           #[int] $MaxDepth = [Int32]::MaxValue
           # TODO: I'm limiting depth here not because searching is too slow... but because /canceling/ is too slow. I need to figure out if I can do something about that. Maybe it's only when running under a debugger, but still.
           [int] $MaxDepth = 10

           # TODO: Need to be able to show DbgShell-style path string
           # (i.e. "dfsrs!g_NetworkGlobals.ioPortManager" versus
           #       "dfsrs!g_NetworkGlobals->ioPortManager")
         )

    begin
    {
        $bangIdx = $TypeName.IndexOf( '!' )
        if( $bangIdx -ge 0 )
        {
            # If the '!' is the last character... well, I don't care.
            $TypeName = $TypeName.Substring( $bangIdx + 1 )
        }

        function DePoint( $type )
        {
            if( $type.SymTag -eq 'PointerType' )
            {
                $type = DePoint $type.PointeeType
            }
            elseif( $type.SymTag -eq 'ArrayType' )
            {
                $type = DePoint $type.ArrayElementType
            }
            return $type
        }

        # TODO: wildcard support
        function TypeMatches( $type )
        {
            #Write-Verbose "   Does it match? $($type.Name) -eq $($TypeName)"
            return $type.Name -eq $TypeName
        } # end TypeMatches

        function PathStackToString( $path )
        {
            [string]::Join( ', ', $path )
        } # end PathStackToString()


        function SearchType( $type,
                             [string] $memberName,
                             [int] $curDepth,
                             [ref] [int] $numHits,
                             $noHitsCache,
                             $path )
        {
            try
            {

                #[Console]::WriteLine( ((New-Object 'System.String' -Arg @( ([char] ' '), $curDepth )) + 'Entering depth {0}'), $curDepth )
                #[Console]::WriteLine( ((New-Object 'System.String' -Arg @( ([char] ' '), $curDepth )) + 'Symbol {0}'), $sym.PathString )
                if( $curDepth -gt $MaxDepth )
                {
                    return
                }

                [int] $origNumHits = $numHits.Value
                [int] $possibleLevelsBelow = $MaxDepth - $curDepth

                $type = DePoint $type

                [int] $prevSearchedLevels = 0
                if( $noHitsCache.TryGetValue( $type, ([ref] $prevSearchedLevels) ) )
                {
                    if( $prevSearchedLevels -ge $possibleLevelsBelow )
                    {
                        # No point in searching again; we've already searched this type.
                        Write-Verbose "<<<< Already searched $($type.Name) at least $prevSearchedLevels levels."
                        return
                    }
                }

                $path.Add( $memberName )

                if( (TypeMatches $type) )
                {
                    $numHits.Value = $numHits.Value + 1
                    PathStackToString $path
                }

                if( $type -is [MS.Dbg.DbgUdtTypeInfo] )
                {
                    Write-Verbose "Searching ($(PathStackToString $path))..."
                    foreach( $mem in $type.Members )
                    {
                        #[Console]::WriteLine( "Stopping? {0}", $PSCmdlet.get_Stopping() )
                        if( $PSCmdlet.get_Stopping() )
                        {
                            return
                        }

                        SearchType $mem.DataType $mem.Name ($curDepth + 1) $numHits $noHitsCache $path
                    } # end foreach( member )

                    foreach( $mem in $type.StaticMembers )
                    {
                        #[Console]::WriteLine( "Stopping? {0}", $PSCmdlet.get_Stopping() )
                        if( $PSCmdlet.get_Stopping() )
                        {
                            return
                        }

                        SearchType $mem.DataType $mem.Name ($curDepth + 1) $numHits $noHitsCache $path
                    } # end foreach( static member )
                }

                if( $numHits.Value -eq $origNumHits )
                {
                    # No hits found.
                    $noHitsCache[ $type ] = $possibleLevelsBelow
                }
                $path.RemoveAt( ($path.Count - 1) )
            }
            finally
            {
                #[Console]::WriteLine( ((New-Object 'System.String' -Arg @( ([char] ' '), $curDepth )) + 'Leaving depth {0}'), $curDepth )
                try
                {
                    #[Console]::WriteLine( ((New-Object 'System.String' -Arg @( ([char] ' '), $curDepth )) + 'Stopping? {0}'), $PSCmdlet.get_Stopping() )
                }
                catch
                {
                    #[Console]::WriteLine( 'Uh-oh...' )
                }
            }
        } # end SearchType
    } # end 'begin' block

    end { }
    process
    {
        try
        {
            $noHitsCache = New-Object 'System.Collections.Generic.Dictionary[[MS.Dbg.DbgTypeInfo],[System.Int32]]'
            $path = New-Object 'System.Collections.Generic.List[System.String]'
            [int] $numHits = 0;
            SearchType $UDT $UDT.Name 0 ([ref] $numHits) $noHitsCache $path

            Write-Verbose "Found $($numHits) hits."
        }
        finally { }
    }
} # end Find-MemberWithType




function Find-DerivedType
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0, ValueFromPipeline = $true )]
           [MS.Dbg.DbgUdtTypeInfo] $BaseType
         )

    begin
    {
        if( $BaseType -is [MS.Dbg.DbgBaseClassTypeInfoBase] )
        {
            # We know what you meant.
            $BaseType = Get-DbgTypeInfo -TypeId $BaseType.BaseClassTypeId -Module $BaseType.Module
        }
    } # end 'begin' block

    end { }
    process
    {
        try
        {
            # Or should it be Get-UdtClassTypes??
            foreach( $udt in (Get-UdtTypes -ModuleName $BaseType.Module.Name) )
            {
                if( $PSCmdlet.get_Stopping() )
                {
                    return
                }

                # N.B. This only finds immediately derived types!
                # (TODO: maybe add an option to follow derived types? But it will be expensive.)
                if( ($udt.BaseClasses) -and
                    ($udt.BaseClasses.Count -gt 0) -and
                    ($udt.BaseClasses.BaseClassTypeId -contains $BaseType.typeid) )
                {
                    $udt
                }
            } # end foreach( $udt )
        }
        finally { }
    }
} # end Find-MemberWithType

