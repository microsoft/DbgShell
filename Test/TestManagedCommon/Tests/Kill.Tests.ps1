
Describe "Kill" {

    pushd

    It "can kill a non-exited process" {

        try
        {
            New-TestApp -TestApp TestNativeConsoleApp -Attach -HiddenTargetWindow
            .kill
            0 | Should Be ($debugger.Targets.Count)
            'Dbg:\' | Should Be ((Get-Location).Path)
        }
        catch
        {
            # This shouldn't be a problem if everything succeeded. If something blew up...
            # hopefully this will clean things up for subsequent tests.
            $debugger.EndSession( [Microsoft.Diagnostics.Runtime.Interop.DEBUG_END]::PASSIVE )
            throw
        }
    }

    It "can kill a process that has already exited" {

        try
        {
            New-TestApp -TestApp TestNativeConsoleApp -Attach -HiddenTargetWindow
            g
            .kill
            0 | Should Be ($debugger.Targets.Count)
            'Dbg:\' | Should Be ((Get-Location).Path)
        }
        catch
        {
            # This shouldn't be a problem if everything succeeded. If something blew up...
            # hopefully this will clean things up for subsequent tests.
            $debugger.EndSession( [Microsoft.Diagnostics.Runtime.Interop.DEBUG_END]::PASSIVE )
            throw
        }
    }

    It "can .abandon, re-attach, and .detach" {

        try
        {
            New-TestApp -TestApp TestNativeConsoleApp -Attach -HiddenTargetWindow
            $osPid = $debugger.GetProcessSystemId()
            # Let's switch to thread 0 and step a bit, just for fun.
            ~0 s
            p
            p
            p
            $savedIp = $ip
            .abandon
            $process = Get-Process -Id $osPid

            0 | Should Be ($debugger.Targets.Count)
            'Dbg:\' | Should Be ((Get-Location).Path)

            # TODO: I'm seeing lots of problems getting "access denied" when re-attaching. A bit of
            # delay seems to help the problem, but not always (does the delay need to be longer?).
            Start-Sleep -Milliseconds 4000
            #Write-Host "   Trying to reconnect to pid $($osPid)" -Back Blue -Fore White
            Connect-Process -Id $osPid -Reattach

            $savedIp | Should Be $ip
            1 | Should Be ($debugger.Targets.Count)
            'Dbg:\' | Should Not Be ((Get-Location).Path)

            #Write-Host '   Detaching...' -Back Blue -Fore White
            .detach
            #Write-Host '   Waiting for exit...' -Back Blue -Fore White
            $itExited = $process.WaitForExit( 5000 )

            $itExited | Should Be $true
        }
        catch
        {
            # This shouldn't be a problem if everything succeeded. If something blew up...
            # hopefully this will clean things up for subsequent tests.
            $debugger.EndSession( [Microsoft.Diagnostics.Runtime.Interop.DEBUG_END]::PASSIVE )
            throw
        }
    }

    popd
}

