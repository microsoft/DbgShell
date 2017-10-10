Register-DbgValueConverterInfo {
    Set-StrictMode -Version Latest


    New-DbgValueConverterInfo -TypeName '!_EXCEPTION_RECORD' -Converter {
        try
        {
            #
            # Some definitions of ExceptionRecord have the ExceptionCode field defined as
            # a /signed/ 4-byte integer. So we're just going to override the .ToString()
            # on the ExceptionCode field so that it always shows hex.
            #
            # An alternative would be to stick a new type name ('System.UInt32') into the
            # ExceptionCode field's TypeNames list. (because uints already have a view
            # definition that prints them as hex)
            #

            # N.B. We make a copy of the stock value, because it would be really bad if we
            # modified the stock value!
            # $_ is the symbol
            $val = $_.GetStockValue().PSObject.Copy()

            $exceptionCode = $val.ExceptionCode
            [string] $str = ''
            if( ($exceptionCode -ge 0) -and ($exceptionCode -lt 10) )
            {
                $str = $exceptionCode.ToString()
            }
            else
            {
                $str = '0x' + $exceptionCode.ToString( 'x8' )
            }

            Add-Member -InputObject $exceptionCode `
                       -MemberType ScriptMethod `
                       -Name 'ToString' `
                       -Value { $str }.GetNewClosure() `
                       -Force

            $val.DbgReplaceMemberValue( 'ExceptionCode', $exceptionCode )

            return $val
        }
        catch
        {
            # We could be looking at bad data.
            return $null
        }
        finally { }
    } # end _EXCEPTION_RECORD converter


    New-DbgValueConverterInfo -TypeName @( '!_UNICODE_STRING', '!_LUNICODE_STRING', '!_LUTF8_STRING', '!_STRING' ) -Converter {
        try
        {
            #
            # We're just going to override the .ToString() method so that it returns the actual string.
            #

            # N.B. We make a copy of the stock value, because it would be really bad if we
            # modified the stock value!
            # $_ is the symbol
            $val = $_.GetStockValue().PSObject.Copy()

            [string] $str = ''
            if( $val.Length -gt 0 )
            {
                # On win7 x86, _UNICODE_STRING.Buffer shows up as "wchar_t*",
                # so we've already automagically turned it into a string. But
                # on win8 x64, it shows up as an "unsigned short*". So we'll
                # just use the pointer value to handle all cases.
                #
                $encoding = [System.Text.Encoding]::Unicode
                if( $val.DbgGetSymbol().Type.Name.IndexOf( 'UTF8' ) -ge 0 )
                {
                    $encoding = [System.Text.Encoding]::Utf8
                }
                elseif( $val.Buffer.DbgGetSymbol().Type.PointeeType.Size -eq 1 )
                {
                    $encoding = [System.Text.Encoding]::ASCII
                }

                try
                {
                    $str = $Debugger.ReadMemAs_String( $val.Buffer.DbgGetPointer(), $val.Length, $encoding )
                }
                catch
                {
                    $cs.AppendPushPopFg( [ConsoleColor]::Red, "<error: $_>" )
                }
            }

            Add-Member -InputObject $val `
                       -MemberType ScriptMethod `
                       -Name 'ToString' `
                       -Value { $str }.GetNewClosure() `
                       -Force

            return $val
        }
        finally { }
    } # end _*STRING converter


    # _HANDLE_TABLE has a FreeLists member declared as a _HANDLE_TABLE_FREE_LIST[1], but
    # the true number of elements in the array is nt!ExpFreeListCount. We'll use this
    # converter to automagically expand that out.
    #
    # TODO: before build 7773 this was different. Do we care about stuff that old?
    New-DbgValueConverterInfo -TypeName 'nt!_HANDLE_TABLE' -Converter {
        try
        {
            # N.B. We make a copy of the stock value, because it would be really bad if we
            # modified the stock value!
            # $_ is the symbol
            $val = $_.GetStockValue().PSObject.Copy()

            $commonParams = @{ 'InputObject' = $val; 'MemberType' = 'NoteProperty' }

            try
            {
                $numFreeLists = dt nt!ExpFreeListCount
                if( $null -ne $numFreeLists ) # not sure if this is possible. Is it in paged mem?
                {
                    # I believe this is what ExpFreeListCount gets initialized to. Hopefully this is valid...
                    $numFreeLists = [Math]::Min( $Debugger.GetNumberProcessors(), 128 )
                }

                $s = $val.FreeLists.DbgGetSymbol()
                $rightSized = $s.AsArray( $numFreeLists ).Value

                $val.DbgReplaceMemberValue( 'FreeLists', $rightSized )
            }
            catch
            {
                # We could be looking at bad data.
                return $null
            }

          # InsertTypeNameBefore -InputObject $val `
          #                      -NewTypeName 'ConverterApplied:_HANDLE_TABLE' `
          #                      -Before 'nt!_HANDLE_TABLE'
            return $val
        }
        finally { }
    } # end _HANDLE_TABLE converter


    New-DbgValueConverterInfo -TypeName 'nt!_EPROCESS' -Converter {
        try
        {
            # N.B. We make a copy of the stock value, because it would be really bad if we
            # modified the stock value!
            # $_ is the symbol
            $val = $_.GetStockValue().PSObject.Copy()

            $commonParams = @{ 'InputObject' = $val; 'MemberType' = 'NoteProperty' }

            # 'Session' is stored as a PVOID. Let's replace it with a strongly-typed pointer.
            #
            # (BTW... apparently this is true since build no. 2280. Do I care about dumps
            # older than that? (How old exactly is that? I think pretty old.)

            # TODO: Should we cache this type info?
            $t = dt 'nt!_MM_SESSION_SPACE'
            if( !$t )
            {
                Write-Error "Could not find type 'nt!_MM_SESSION_SPACE'."
                return $null # signals that we can't do the conversion.
            }

            try
            {
                $session = $val.Session.DbgReinterpretPointeeType( $t )

                $val.DbgReplaceMemberValue( 'Session', $session )
            }
            catch
            {
                # We could be looking at bad data.
                #
                # In that case, should we replace with an error? Or just leave it?
                $problem = $_
                $val.DbgReplaceMemberValue( 'Session', $problem )
            }


            #
            # Let's add a convenient HandleCount property. The object table is in paged
            # memory, though, so it might not be available, so we'll make the value
            # nullable.
            #
            # The FreeLists variable-sized array should have already been right-sized by
            # the _HANDLE_TABLE converter.

            $handleCount = $null

            if( $val.ObjectTable -and ($val.ObjectTable -isnot [MS.Dbg.DbgValueError]) )
            {
                $handleCount = 0
                foreach( $fl in $val.ObjectTable.FreeLists )
                {
                    if( $fl.HandleCount -ne [UInt32]::MaxValue )
                    {
                        $handleCount += $fl.HandleCount
                    }
                }
            }

            Add-Member @commonParams -Name 'HandleCount' -Value $handleCount


            #
            # Let's see if we can get the full (un-truncated) image name.
            #

            $fullName = $null

            # I think it could be in paged memory.
            if( $val.SeAuditProcessCreationInfo -and
                ($val.SeAuditProcessCreationInfo -isnot [MS.Dbg.DbgValueError]) )
            {
                $fullName = $val.SeAuditProcessCreationInfo.ImageFileName.Name.ToString()
            }

            Add-Member @commonParams -Name 'FullImageFileName' -Value $fullName


            #
            # Let's replace the ImageFileName byte[15] with an ImageFileName string.
            #

            $imageFileNameStr = $val.ImageFileName.DbgGetSymbol().ReadAs_szString()
            if( !$imageFileNameStr )
            {
                $imageFileNameStr = 'System'
            }
            elseif( $fullName )
            {
                # Because the "real" ImageFileName is truncated at 15 chars: if we have
                # the full name, let's use that to get an un-truncated version.
                $imageFileNameStr = [System.IO.Path]::GetFileName( $fullName )
            }

            $val.DbgReplaceMemberValue( 'ImageFileName', $imageFileNameStr )


            #
            # Mark the fact that this converter has been applied:
            #

            InsertTypeNameBefore -InputObject $val `
                                 -NewTypeName 'ConverterApplied:_EPROCESS' `
                                 -Before 'nt!_EPROCESS'
            return $val
        }
        finally { }
    } # end _EPROCESS converter


    New-DbgValueConverterInfo -TypeName 'nt!_KTHREAD' -Converter {
        try
        {
            # N.B. We make a copy of the stock value, because it would be really bad if we
            # modified the stock value!
            # $_ is the symbol
            $val = $_.GetStockValue().PSObject.Copy()

            $commonParams = @{ 'InputObject' = $val; 'MemberType' = 'NoteProperty' }

            #
            # 'State' is stored as a byte. Let's change the type to nt!_KTHREAD_STATE.
            #

            # TODO: Should we cache this type info?
            $t = dt 'nt!_KTHREAD_STATE'
            if( !$t )
            {
                Write-Error "Could not find type 'nt!_KTHREAD_STATE'."
                return $null # signals that we can't do the conversion.
            }

            try
            {
                $newSym = $val.State.DbgGetSymbol().ChangeType( $t )

                $val.DbgReplaceMemberValue( 'State', $newSym.Value )
            }
            catch
            {
                # We could be looking at bad data.
                #
                # In that case, should we replace with an error? Or just leave it?
                $problem = $_
                $val.DbgReplaceMemberValue( 'State', $problem )
            }

            #
            # Mark the fact that this converter has been applied:
            #

            InsertTypeNameBefore -InputObject $val `
                                 -NewTypeName 'ConverterApplied:_KTHREAD' `
                                 -Before 'nt!_KTHREAD'
            return $val
        }
        finally { }
    } # end _KTHREAD converter
}

