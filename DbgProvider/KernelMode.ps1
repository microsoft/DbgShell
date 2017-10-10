
#
# We can put functions in here that are specific to kernel-mode debugging.
#

<#
.SYNOPSIS
    Throws if you are not attached to a kernel-mode target.
#>
function ThrowIfNotKernelMode
{
    [CmdletBinding()]
    param()

    begin { }
    end { }

    process
    {
        try
        {
            if( !$Debugger.IsKernelMode )
            {
                'This command requires you be attached to a kernel-mode target.'
            }
        }
        finally { }
    } # end 'process' block
} # end function ThrowIfNotKernelMode


<#
.SYNOPSIS
#>
function !process
{
    [CmdletBinding()]
    param( # TODO

           [Parameter( Mandatory = $false )]
           [switch] $Current
         )

    begin { ThrowIfNotKernelMode }
    end { }

    process
    {
        try
        {
            if( $Current )
            {
                dt 'nt!_EPROCESS' $Debugger.GetImplicitProcessDataOffset()
            }
            else
            {
                $head = dt nt!PsActiveProcessHead -ErrorAction Stop

                Expand-LIST_ENTRY -Head $head `
                                  -EntryType 'nt!_EPROCESS' `
                                  -ListEntryMemberName 'ActiveProcessLinks'
            }
        }
        finally { }
    } # end 'process' block
} # end function !process


<#
.SYNOPSIS
#>
function !tmpGetThreads
{
    [CmdletBinding()]
    param( [Parameter( Mandatory = $false )]
           $Process = $null,

           [Parameter( Mandatory = $false )]
           [switch] $IncludeInactive
         )

    begin { ThrowIfNotKernelMode }
    end { }

    process
    {
        try
        {
            if( !$Process )
            {
                $Process = !process -Current
            }

            if( $IncludeInactive )
            {
                Expand-LIST_ENTRY -Head $Process.ThreadListHead `
                                  -EntryType (dt 'nt!_ETHREAD') `
                                  -ListEntryMemberName 'ThreadListEntry'
            }
            else
            {
                Expand-LIST_ENTRY -Head $Process.Pcb.ThreadListHead `
                                  -EntryType (dt 'nt!_ETHREAD') `
                                  -ListEntryMemberName 'Tcb.ThreadListEntry'
            }
        }
        finally { }
    } # end 'process' block
} # end function !process

