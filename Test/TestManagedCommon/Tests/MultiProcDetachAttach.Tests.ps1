
Describe "MultiProcDetachAttach" {

    pushd
    New-TestApp -TestApp TestNativeConsoleApp -Attach -TargetName testApp1 -Arg 'Sleep 10000000' -HiddenTargetWindow

    $p1 = $debugger.GetCurrentTarget()

    It "can't have duplicate target names" {

        $($p1.TargetFriendlyName) | Should Be 'testApp1'

        [bool] $itThrew = $false
        try
        {
            New-TestApp -TestApp TestManagedConsoleApp -TargetName testApp1 -Arg 'Sleep 10000000' -HiddenTargetWindow
        }
        catch
        {
            $itThrew = $true
            # Expected.
        }

        $itThrew | Should Be $true

    }


    It "can attach to a second process, and detach/re-attach" {

        Write-Host "This test disabled until someone has time to get it working" -Fore Yellow

        if( $false ) {

        New-TestApp -TestApp TestManagedConsoleApp -Attach -TargetName testApp2 -Arg 'Sleep 10000000' -HiddenTargetWindow

        $p2 = $debugger.GetCurrentTarget()
        $($p2.TargetFriendlyName) | Should Be 'testApp2'


        $dirs = dir Dbg:\ | %{ $_.Name }
        $dirs.Contains( 'testApp1' ) | Should Be $true
        $dirs.Contains( 'testApp2' ) | Should Be $true


        cd Dbg:\testApp1
        $((Get-Content Modules -TotalCount 1).Name) | Should Be 'TestNativeConsoleApp'
        $(Get-Content DbgEngSystemId) | Should Be ([System.UInt32] 0)
        $(Get-Content DbgEngProcessId) | Should Be ([System.UInt32] 0)

        cd Dbg:\testApp2
        $((Get-Content Modules -TotalCount 1).Name) | Should Be 'TestManagedConsoleApp'
        $(Get-Content DbgEngSystemId) | Should Be ([System.UInt32] 0)
        $(Get-Content DbgEngProcessId) | Should Be ([System.UInt32] 1)

        $osPid1 = $p1.OsProcessId
        cd Dbg:\testApp1
        .detach

        $dirs = dir Dbg:\ | %{ $_.Name }
        $dirs.Contains( 'testApp1' ) | Should Be $false
        $dirs.Contains( 'testApp2' ) | Should Be $true

        Connect-Process -Id $osPid1

        $dirs = dir Dbg:\ | %{ $_.Name }
        $dirs.Contains( 'testApp1' ) | Should Be $true
        $dirs.Contains( 'testApp2' ) | Should Be $true

        $osPid2 = $p2.OsProcessId
        $osPid1 -ne $osPid2 | Should Be $true

        cd Dbg:\testApp2
        .detach

        $dirs = dir Dbg:\ | %{ $_.Name }
        $dirs.Contains( 'testApp1' ) | Should Be $true
        $dirs.Contains( 'testApp2' ) | Should Be $false

        Connect-Process -Id $osPid2

        $dirs = dir Dbg:\ | %{ $_.Name }
        $dirs.Contains( 'testApp1' ) | Should Be $true
        $dirs.Contains( 'testApp2' ) | Should Be $true

        } # (end disabled portion)
    }

    # TODO: targeted kill
    .kill
    # We might not have gotten the second process off the ground. Let's not muddy up that
    # error with a botched .kill.
    if( $debugger.Targets.Count -gt 0 ) {
        .kill
    }
    popd
}

