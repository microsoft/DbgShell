
# Gu: "go up"
Describe "Gu" {

    pushd

    It "can gu" {

        try
        {
            # Write-Host 'Testing that "gu" stays on the same thread...' -Back Blue -Fore White
            New-TestApp -TestApp TestNativeConsoleApp -SkipInitialBreakpoint -Arg 'twoThreadGuTest' -HiddenTargetWindow
            $threads = @( Find-DbgThread -WithStackText 'TwoThreadGuTest' )
            $curThread = Get-DbgUModeThreadInfo -Current
            2 | Should Be ($threads.Count)
            0 | Should Be ($curThread.DebuggerId)
            ($threads[1].Stack.Frames.Function.Name.Contains( '_TwoThreadGuTestWorkerInner' )) | Should Be $true
            '_TwoThreadGuTestWorkerInner' | Should Be ($curThread.Stack.Frames[ 0 ].Function.Name)

            gu # Or: Resume-Process -ResumeType StepOut

            $threads = @( Find-DbgThread -WithStackText 'TwoThreadGuTest' )
            $curThread = Get-DbgUModeThreadInfo -Current
            1 | Should Be ($threads.Count)
            0 | Should Be ($curThread.DebuggerId)
            '_TwoThreadGuTestWorker' | Should Be ($curThread.Stack.Frames[ 0 ].Function.Name)
        }
        finally
        {
            .kill
        }
    }

    popd
}

