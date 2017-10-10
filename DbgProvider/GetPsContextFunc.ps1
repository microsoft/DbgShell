
<#
.SYNOPSIS
    Gets an object that represents a set of functions and variables, which can be used with ScriptBlock.InvokeWithContext.
#>
function Get-PsContext
{
    [CmdletBinding()]
    param()

    begin { }
    process { }
    end
    {
        function private:_GetPsContextWorker
        {
            [CmdletBinding()]
            param( [Parameter( Mandatory = $false, Position = 0 )]
                   [System.Management.Automation.PSModuleInfo] $private:ScopingMod )

            begin { }
            process { }
            end
            {
                $private:sb = {
                    try
                    {
                        $private:ctx = New-Object 'MS.Dbg.PsContext'

                        $private:funcs = Get-ChildItem 'Function:\'
                        foreach( $private:func in $funcs )
                        {
                            # Filter out our own functions:
                            if( ($func.Name -ne '_GetPsContextWorker') -and
                                ($func.Name -ne 'Get-PsContext') )
                            {
                                # Workaround for Windows Blue Bugs NNNNNN (TODO: file it)
                                #
                                # Private functions show up via "dir Function:\" and
                                # Get-Command, even though you can't call them.
                                # Fortunately, Test-Path tells the truth.
                                if( (Test-Path "Function:\$($func.Name)") )
                                {
                                    $ctx.Funcs.Add( $func.Name, $func.ScriptBlock )
                                }
                            }
                        }

                        # Using "Get-Variable -Scope 2" doesn't quite work; it seems to
                        # get stuff /only/ in scope 2, and sometimes we need different
                        # scopes (because nesting levels are different in Debugger.psfmt
                        # versus Debugger.Converters.STL.ps1, for example). We can filter
                        # out our own locals by making them private (like "$private:foo").

                        $private:vars = Get-Variable
                        foreach( $private:var in $vars )
                        {
                            if( $var.Name -ne 'PSCmdlet' )
                            {
                                $ctx.Vars.Add( $var.Name, $var )
                            }
                        }

                        return $ctx
                    }
                    finally { }
                } # end $sb

                try
                {
                    # You'd think we could just let the caller decide wether to call this
                    # function (Get-PsContext) in the scope of $ScopingMod or not, but it
                    # doesn't work out.  We'll control how $ScopingMod is used here to try
                    # to make sure it works how we want. (One problem is that sometimes
                    # even "& $mod $stuff" doesn't execute $stuff in $mod's scope, because
                    # $stuff comes from some other module and even "& $mod" can't "break
                    # out" of that.
                    if( $ScopingMod ) {
                        & $ScopingMod $sb
                    } else {
                        & $sb
                    }
                }
                finally { }
            } # end 'end' block
        } # end _GetPsContextWorker

        try
        {
            #[Console]::WriteLine( "Get-PsContext: current module context is: $($ExecutionContext.SessionState.Module)" )
            $private:curCtx = _GetPsContextWorker
            $private:tmpMod = New-Module { } # module scopes always come under the global scope
            $private:globalCtx = _GetPsContextWorker $tmpMod
            $private:newCtx = $curCtx - $globalCtx

            return $newCtx
        }
        finally { }
    } # end 'end' block
} # end Get-PsContext


