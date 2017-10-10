
#
# We can put functions in here that are not necessarily debugger-related, like
# type-specific utilities.
#
# This sort of thing might should probably go in a separate module (like
# MyTeamDbgExtensions.psm1), but I'm just going to throw stuff in here for now.
#


<#
.SYNOPSIS
    Finds the offset for a given member/member path from the start of the specified type.

    Example: Find-MemberOffset 'nt!_ETHREAD' 'Tcb.ThreadListEntry'
#>
function Find-MemberOffset
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0 )]
           [ValidateNotNull()]
           [MS.Dbg.Commands.TypeTransformation()]
           [MS.Dbg.DbgUdtTypeInfo] $Type,

           [Parameter( Mandatory = $true, Position = 1, ValueFromPipeline = $true )]
           [ValidateNotNullOrEmpty()]
           [string] $MemberPath
         )

    begin { }
    end { }

    process
    {
        try
        {
            $memberNames = $MemberPath.Split( '.' )
            [int] $offset = 0

            for( [int] $idx = 0; $idx -lt $memberNames.Length; $idx++ )
            {
                $memberName = $memberNames[ $idx ]

                if( !$Type.Members.HasItemNamed( $memberName ) )
                {
                    throw "Type $($Type.FullyQualifiedName) does not have a member called $($memberName)."
                }

                $member = $Type.Members[ $memberName ]

                $offset += $member.Offset

                if( $idx -lt ($memberNames.Length - 1) )
                {
                    # Not done with the loop; need to get next Type.
                    if( $member.DataType -is [MS.Dbg.DbgPointerTypeInfo] )
                    {
                        throw "There can't be a pointer in the member path ($memberName)."
                    }
                    elseif( $member.DataType -isnot [MS.Dbg.DbgUdtTypeInfo] )
                    {
                        throw "Unexpected DataType: '$memberName' member is a $($Type.Members[ $memberName ].DataType.GetType().FullName)."
                    }
                    $Type = $member.DataType
                }
            }
            return $offset
        }
        finally { }
    } # end 'process' block
} # end Find-MemberOffset



<#
.SYNOPSIS
    Enumerates items in a LIST_ENTRY list.

    Example (kernel mode): Expand-LIST_ENTRY (dt 'nt!PsActiveProcessHead') 'nt!_EPROCESS' 'ActiveProcessLinks'

    Note that $ListEntryMemberName can can reference a member of a member (of a member, etc.). In other words, it can go more than a single member deep--for instance, it could be 'Tcb.ThreadListEntry'.
#>
function Expand-LIST_ENTRY
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $true, Position = 0, ValueFromPipeline = $true )]
           [ValidateNotNull()]
           # TODO: Would it be nice to have a [DbgValueTransformation()] attribute, so we
           # could pass a string here, like we can for the type? So you could do:
           #
           #    Expand-LIST_ENTRY 'nt!PsActiveProcessHead' 'nt!_EPROCESS' 'ActiveProcessLinks'
           #
           # I guess it's not so bad to do:
           #
           #    Expand-LIST_ENTRY (dt 'nt!PsActiveProcessHead') 'nt!_EPROCESS' 'ActiveProcessLinks'
           #
           [MS.Dbg.DbgValue] $Head,

           [Parameter( Mandatory = $true, Position = 1 )]
           [ValidateNotNull()]
           [MS.Dbg.Commands.TypeTransformation()]
           [MS.Dbg.DbgUdtTypeInfo] $EntryType,

           [Parameter( Mandatory = $false, Position = 2 )]
           [ValidateNotNullOrEmpty()]
           [string] $ListEntryMemberName
         )

    begin
    {
        try
        {
            function addrOf( $dbgVal )
            {
                # You're allowed to pass a LIST_ENTRY, a LIST_ENTRY*, a LIST_ENTRY**, ...
                if( $dbgVal -is [MS.Dbg.DbgPointerValue] )
                {
                    $dbgVal = $dbgVal.DbgFollowPointers()
                }
                return $dbgVal.DbgGetOperativeSymbol().Address
            }

            if( !$ListEntryMemberName )
            {
                #
                # We'll find it ourself, by looking for the first LIST_ENTRY member.
                #

                $listEntryMembers = @( $EntryType.Members | where { $_.DataType.Name -eq '_LIST_ENTRY' } )
                if( 0 -eq $listEntryMembers.Count )
                {
                    throw "Could not find a _LIST_ENTRY member in $($EntryType.FullyQualifiedName)."
                }
                else
                {
                    $ListEntryMemberName = $listEntryMembers[ 0 ]
                    if( $listEntryMembers.Count -gt 1 )
                    {
                        Write-Warning "Type $($EntryType.FullyQualifiedName) has more than one _LIST_ENTRY member. We'll just guess and use the first one ($ListEntryMemberName)."
                    }
                }
            } # end if( !$ListEntryMemberName )

            [string] $dotFlink = '.Flink'
            if( $ListEntryMemberName.EndsWith( $dotFlink ) )
            {
                Write-Warning "Don't include 'Flink' in the -ListEntryMemberName parameter."
                $ListEntryMemberName = $ListEntryMemberName.Substring( 0, $ListEntryMemberName.Length - $dotFlink.Length )
            }

            $listEntryOffset = Find-MemberOffset $EntryType $ListEntryMemberName
        }
        finally { }
    } # end 'begin' block

    end { }

    process
    {
        try
        {
            $headAddr = addrOf $Head

            $curListEntry = $Head.Flink

            [int] $idx = 0

            # $curListEntry is a pointer, so can compare directly to $headAddr.
            while( $curListEntry -ne $headAddr )
            {
                # If things go badly, this is likely where we find out--$curListEntry will
                # be a DbgValueError, and thus won't have a DbgGetPointer() method. We can
                # make the error a little nicer.
                if( $curListEntry -is [MS.Dbg.DbgValueError] )
                {
                    $er = New-Object 'System.Management.Automation.ErrorRecord' `
                                        -ArgumentList @( $curListEntry.DbgGetError(),
                                                         'ErrorWhileTraversingList', # ErrorId
                                                         'NotSpecified',             # ErrorCategory
                                                         $curListEntry )             # TargetObject
                    $PSCmdlet.ThrowTerminatingError( $er )
                }

                # N.B. $curListEntry is a pointer, but we need to get the raw pointer else
                # pointer arithmetic will mess things up.

                $curItemAddr = $curListEntry.DbgGetPointer() - $listEntryOffset

                #$val = dt $EntryType $curItemAddr
                $val = Get-DbgSymbolValue -Address $curItemAddr `
                                          -Type $EntryType `
                                          -NameForNewSymbol "$($Head.DbgGetOperativeSymbol().Name)_$($idx)" `
                                          -ErrorAction Stop

                Write-Output $val


                # The $ListEntryMemberName might have dots (".") in it (like
                # "Tcb.ThreadListEntry"), so we'll use Invoke-Expression to get what we
                # want. (without iex, it will complain that $val has no such member
                # "Tcb.ThreadListEntry", which is true)

                $curListEntry = Invoke-Expression "`$val.$ListEntryMemberName.Flink"
                $idx++
            }
        }
        finally { }
    } # end 'process' block
} # end function Expand-LIST_ENTRY

