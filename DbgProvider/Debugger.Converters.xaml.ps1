
Register-DbgValueConverterInfo {

    Set-StrictMode -Version Latest


    New-DbgValueConverterInfo -TypeName '!CXString' -Converter {
        try
        {
            # $_ is the symbol
            $sym = $_
            $stockValue = $sym.GetStockValue()

            # TODO: I don't actually know where CXString is defined, so I don't
            # know what a bunch of this stuff is about...

            # TODO: What is that low bit for?
            $ptr = $stockValue.pString.DbgGetPointer() -band (-bnot ([UInt64] 1))
            $str = $Debugger.ReadMemAs_UnicodeString( $ptr, ($stockValue.cString * 2) )

            # TODO: What do the flags mean? For now I'll just stick 'em on.
            $str = Add-Member -InputObject $str -MemberType NoteProperty -Name 'flags' -Value $stockValue.flags -Force -PassThru

            return $str
        }
        finally { }
    } # end CXString converter


    New-DbgValueConverterInfo -TypeName 'Windows_UI_Xaml!CDependencyObject' -Converter {
        try
        {
            # $_ is the symbol
            $sym = $_
            # N.B. We make a copy of the stock value, because it would be really bad if we
            # modified the stock value!
            # $_ is the symbol
            $val = $_.GetStockValue().PSObject.Copy()

            [string] $name = spoi $val.m_pstrName

            # Not all dependency objects have a class name (for instance, HWCompTreeNode),
            # but I'll stick a ClassName property on them all for convenience.
            #
            # (The call to DbgGetOperativeSymbol() is so that we get the detected derived type,
            # not the declared type (which is CDependencyObject, which does not have an
            # m_pClassName member)).
            [string] $className = ''
            if( $val.DbgGetOperativeSymbol().Type.Members.HasItemNamed( 'm_pClassName' ) )
            {
                $className = spoi $val.m_pClassName
            }

            Add-Member -InputObj $val -Name 'Name'      -MemberType NoteProperty -Value $name
            Add-Member -InputObj $val -Name 'ClassName' -MemberType NoteProperty -Value $className

            Add-Member -InputObj $val -Name 'Children'  -MemberType ScriptProperty -Value {
                if( $val.m_pChildren -eq 0 )
                {
                    # Children pointer is null.
                    return
                }

                if( $val.m_pChildren.m_ppBlock -ne 0 )
                {
                    Write-Warning "I'm not sure what to do with non-null m_ppBlock."
                }

                for( [int] $i = 0; $i -lt $val.m_pChildren.m_nObject; $i++ )
                {
                    poi $val.m_pChildren.m_pFlattened[ $i ]
                }
            }.GetNewClosure() # end 'Children' synthetic property

            return $val
        }
        finally { }
    } # end Windows_UI_Xaml!CDependencyObject converter
}

