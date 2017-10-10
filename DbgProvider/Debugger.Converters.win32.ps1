Register-DbgValueConverterInfo {
    Set-StrictMode -Version Latest

    New-DbgValueConverterInfo -TypeName '!_RTL_CRITICAL_SECTION' -Converter {
        try
        {
            # N.B. We make a copy of the stock value, because it would be really bad if we
            # modified the stock value!
            # $_ is the symbol
            $val = $_.GetStockValue().PSObject.Copy()

            [bool] $isLocked = $false
            $waiterWoken = $null
            $numWaitingThreads = $null

            # Is it a "new-style" critsec?
            # TODO: When did "new-style" critsecs show up? Do I really need to care about "old-style"?
            [bool] $isNewStyleCritsec = ($null -ne (Get-DbgSymbol 'ntdll!RtlIsCriticalSectionLocked*'))
            if( $isNewStyleCritsec )
            {
                $isLocked    = 0 -eq ($val.LockCount -band 0x01)
                $waiterWoken = 0 -eq ($val.LockCount -band 0x02)
                $numWaitingThreads = (-1 - $val.LockCount) -shr 2
            }
            else
            {
                $isLocked = $val.LockCount -ge 0
            }

            $owningThreadDbgId = 0
            try
            {
                [UInt32] $owningThreadSysTid = ([UInt32] ([UInt64] $val.OwningThread))
                if( 0 -ne $owningThreadSysTid )
                {
                    $owningThreadDbgId = $Debugger.GetThreadDebuggerIdBySystemTid( $owningThreadSysTid )
                }
            }
            catch
            {
                $problem = $_
                # We could be looking at bad data.
                # Is this okay, using a ColorString instead of, say, 0xdeadbeef?
                $owningThreadDbgId = New-ColorString -Foreground 'Red' -Content '<bad data>'
            }

            $commonParams = @{ 'InputObject' = $val; 'MemberType' = 'NoteProperty' }
            Add-Member @commonParams -Name 'IsLocked'          -Value $isLocked
            Add-Member @commonParams -Name 'WaiterWoken'       -Value $waiterWoken
            Add-Member @commonParams -Name 'NumWaitingThreads' -Value $numWaitingThreads
            Add-Member @commonParams -Name 'OwningThreadDbgId' -Value $owningThreadDbgId

            InsertTypeNameBefore -InputObject $val `
                                 -NewTypeName 'ConverterApplied:_RTL_CRITICAL_SECTION' `
                                 -Before '!_RTL_CRITICAL_SECTION'
            return $val
        }
        finally { }
    } # end _RTL_CRITICAL_SECTION converter

}

