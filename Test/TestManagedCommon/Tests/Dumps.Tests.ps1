
Describe "Dumps" {

    pushd

    It "can write dumps" {

        New-TestApp -TestApp TestNativeConsoleApp -Attach -TargetName testApp -HiddenTargetWindow

        $dumpDir = "$($env:temp)\DbgShellTestDumps"

        function CleanDumpDir()
        {
            if( !(Test-Path $dumpDir) )
            {
                $null = mkdir $dumpDir
            }
            else
            {
                del "$($dumpDir)\*"
            }
        }


        try
        {
            1 | Should Be $Debugger.Targets.Count
            $Debugger.IsLive | Should Be $true

            CleanDumpDir

            $dumpPath = "$($dumpDir)\test.dmp"

            Write-DbgDumpFile -DumpFile $dumpPath -Comment "This is the dump comment" -Verbose
            .kill

            0 | Should Be $Debugger.Targets.Count

            $Debugger.RecentNotifications.Clear()
            Mount-DbgDumpFile $dumpPath
            $Debugger.RecentNotifications

            1 | Should Be $Debugger.Targets.Count
            $Debugger.IsLive | Should Be $false

            .kill
        }
        finally
        {
            if( $Debugger.Targets.Count -ne 0 )
            {
                .kill
            }
            CleanDumpDir
        }
    }

    popd
}

