Register-DbgValueConverterInfo {
    Set-StrictMode -Version Latest

    New-DbgValueConverterInfo -TypeName '!FrsStringImpl<unsigned short,char>' -Converter {
        # $_ is the symbol
        $stockValue = $_.GetStockValue()
        $stockValue.m_Body.m_String
    }

    New-DbgValueConverterInfo -TypeName 'dfsrs!Guid' -Converter {
        # $_ is the symbol
        $_.ReadAs_Guid()
    }


    New-DbgValueConverterInfo -TypeName 'dfsrs!scoped_any<?*>' -Converter {
        try
        {
            # $_ is the symbol
            $stockValue = $_.GetStockValue()

            # scoped_any is just a resource management wrapper around a pointer. We can make
            # things easier to see in the debugger by just hiding the scoped_any layer (which
            # has nothing in it anyway, except the pointer).
            return $stockValue.m_t
        }
        finally { }
    } # end scoped_any converter


    New-DbgValueConverterInfo -TypeName 'dfsrs!CREWLock' -Converter {
        # N.B. We make a copy the stock value, because it would be really bad if we modified
        # the stock value!
        # $_ is the symbol
        $val = $_.GetStockValue().PSObject.Copy()

        [bool] $isReadLocked = $false
        [bool] $isWriteLocked = $false
        [bool] $isBeingModified = $false

        if( !$val.cs.m_cs.IsLocked )
        {
            $isBeingModified = $true
        }

        if( $val.holderCount -lt 0 )
        {
            $isWriteLocked = $true
        }
        elseif( $val.holderCount -gt 0 )
        {
            $isReadLocked = $true
        }

        $owningWriterThreadDbgId = 0
        $owningReaderThreadDbgIds = New-Object 'System.Collections.Generic.List[UInt32]'
        $nonThreadOwnerPointers = New-Object 'System.Collections.Generic.List[UInt64]'
        try
        {
            [UInt32] $owningThreadSysTid = 0
            if( $isWriteLocked )
            {
                $owningThreadSysTid = ([UInt32] ([UInt64] $val.lockHolders[0]))
                if( 0 -ne $owningThreadSysTid )
                {
                    $owningWriterThreadDbgId = $Debugger.GetThreadDebuggerIdBySystemTid( $owningThreadSysTid )
                }
            }
            elseif( $isReadLocked )
            {
                $tids = (Get-DbgUModeThreadInfo).Tid
                $val.lockHolders | %{
                    $tidOrPointer = $_
                    $owningThreadSysTid = ([UInt32] ([UInt64] $tidOrPointer))
                    if( 0 -ne $owningThreadSysTid )
                    {
                        if( $tids -contains $tidOrPointer )
                        {
                            $owningReaderThreadDbgIds.Add( $Debugger.GetThreadDebuggerIdBySystemTid( $owningThreadSysTid ) )
                        }
                        else
                        {
                            # The problem is that "lock holder" is an abstract concept.
                            # /Usually/ it's a thread Id, but some things use their "this"
                            # pointer. (like VolumeManager, FnIdRecordInfo)
                            $nonThreadOwnerPointers.Add( $tidOrPointer )
                            # TODO: can this happen for the writer, too?
                        }
                    }
                } # end foreach( lock holder )
            } # end elseif( $isReadLocked )
        }
        catch
        {
            $problem = $_
            # We could be looking at bad data.
            # Is this okay, using a ColorString instead of, say, 0xdeadbeef?
            $owningWriterThreadDbgId = New-ColorString -Foreground 'Red' -Content '<bad data>'
        }

        $splatMe = @{ 'InputObject' = $val; 'MemberType' = 'ScriptProperty' }
        Add-Member @splatMe -Name 'IsReadLocked'             -Value { $isReadLocked }.GetNewClosure()
        Add-Member @splatMe -Name 'IsWriteLocked'            -Value { $isWriteLocked }.GetNewClosure()
        Add-Member @splatMe -Name 'IsBeingModified'          -Value { $val.cs.m_cs.IsLocked }.GetNewClosure()
        Add-Member @splatMe -Name 'OwningWriterThreadDbgId'  -Value { $owningWriterThreadDbgId }.GetNewClosure()
        Add-Member @splatMe -Name 'OwningReaderThreadDbgIds' -Value { $owningReaderThreadDbgIds }.GetNewClosure()
        Add-Member @splatMe -Name 'NonThreadOwnerPointers'   -Value { $nonThreadOwnerPointers }.GetNewClosure()

        InsertTypeNameBefore -InputObject $val `
                             -NewTypeName 'ConverterApplied:dfsrs CREWLock' `
                             -Before 'dfsrs!CREWLock'
        return $val
    } # end CREWLock converter

}

