
Register-DbgValueConverterInfo {

    Set-StrictMode -Version Latest

    New-DbgValueConverterInfo -TypeName '!std::basic_string<?*>' -Converter {
        try
        {
            # $_ is the symbol
            $stockValue = $_.GetStockValue()

            # Handle Dev14 types:
            if( $stockValue.PSObject.Properties.Match( '_Mypair' ).Count )
            {
                # New in Dev14 (VS 2015)
                $stockValue = $stockValue._Mypair._Myval2
            }

            # What size characters are we dealing with?
            $charSize = $stockValue._Bx._Ptr.DbgGetSymbol().Type.PointeeType.Size
            if( ($charSize -ne 1) -and ($charSize -ne 2) )
            {
                # Unusual character size.
                return $null # this means that we couldn't convert it
            }

            # Small-string optimization: will it fit into the buffer?
            # +1 for terminating null.
            if( (($stockValue._Mysize + 1) * $charSize) -le ($stockValue._Bx._Buf.DbgGetSymbol().Type.Size) ) # Or should I just hardcode 16?
            {
                if( 1 -eq $charSize )
                {
                    return $stockValue._Bx._Buf.DbgGetSymbol().ReadAs_szString()
                }
                else
                {
                    return $stockValue._Bx._Buf.DbgGetSymbol().ReadAs_wszString()
                }
            }
            else
            {
                return $stockValue._Bx._Ptr
            }
        }
        finally { }
    }


    New-DbgValueConverterInfo -TypeName '!std::vector<?*>' -Converter {
        try
        {
            # $_ is the symbol
            $stockValue = $_.GetStockValue()

            # Handle Dev14 types:
            if( $stockValue.PSObject.Properties.Match( '_Mypair' ).Count )
            {
                # New in Dev14 (VS 2015)
                $stockValue = $stockValue._Mypair._Myval2
            }

            $val = $null
            [bool] $unallocatedEmpty = $false
            if( $stockValue._Myfirst.DbgIsNull() )
            {
                $unallocatedEmpty = $true
                $val = New-Object 'System.Collections.Generic.List[PSObject]' -ArgumentList @( 0 )
            }
            else
            {
                $unallocatedEmpty = $false
                $elemType = $stockValue._Myfirst.DbgGetSymbol().Type.PointeeType
                $numElements = $stockValue._Mylast - $stockValue._Myfirst # N.B. Pointer arithmetic (takes element size into account)
                $val = New-Object 'System.Collections.Generic.List[PSObject]' -ArgumentList @( $numElements )
                for( [int] $i = 0; $i -lt $numElements; $i++ )
                {
                    $name = $_.Name + '[' + $i.ToString() + ']'
                    # N.B. Pointer arithmetic:
                    $elem = Get-DbgSymbolValue -Address ($stockValue._Myfirst + $i) `
                                               -Type $elemType `
                                               -NameForNewSymbol $name
                    $null = $val.Add( $elem )
                }
            }

            $val = $val.AsReadOnly()

            if( $unallocatedEmpty )
            {
                Add-Member -InputObject $val -MemberType ScriptMethod -Name 'capacity' -Value { 0 }
                Add-Member -InputObject $val -MemberType ScriptMethod -Name 'size' -Value { 0 }
            }
            else
            {
                $capacity = $stockValue._MyEnd - $stockValue._Myfirst # N.B. Pointer arithmetic
                Add-Member -InputObject $val -MemberType ScriptMethod -Name 'capacity' -Value { $capacity }.GetNewClosure()
                Add-Member -InputObject $val -MemberType ScriptMethod -Name 'size' -Value { $numElements }.GetNewClosure()
            }

            Add-Member -InputObject $val -MemberType ScriptMethod -Name 'ToString' -Value { $stockValue.ToString() }.GetNewClosure() -Force
            Write-Collection -Collection $val
        }
        finally
        {
        }
    } # end vector converter


    # vector<bool> is specialized, and so needs its own specialized converter.
    New-DbgValueConverterInfo -TypeName '!std::vector<bool,?*>' -Converter {
        try
        {
            # $_ is the symbol
            $sym = $_
            $stockValue = $sym.GetStockValue()

            $bitArray = $null
            if( 0 -eq $stockValue._Mysize )
            {
                $bitArray = new-object 'MS.Dbg.FreezableBitArray' -arg @( 0 )
            }
            else
            {
              # When I wrote the old VS12 code, I must not have realized that I could just
              # pass _Myvec directly to the FreezableBitArray constructor (PS is able to
              # coerce the type enough).
              #
              # $rawVector = $sym.Children[ '_Myvec' ].
              #                   Children[ '_MyFirst' ].
              #                   Children[ 0 ]. # follow pointer
              #                   ReadAs_TArray( $stockValue._Myvec.Count, [int] )

                $bitArray = new-object 'MS.Dbg.FreezableBitArray' -arg @( $stockValue._Mysize, $stockValue._Myvec )
            }

            $bitArray.Freeze()

            Write-Collection -Collection $bitArray
        }
        finally { }
    } # end vector<bool> converter


    # We need to protect against bad/unavailable data, without completely giving up on the
    # whole tree.
    function ChildLooksOkay( $rootAddr, [string] $which, $child )
    {
        if( $child.DbgIsNull() )
        {
            Write-Warning "$which child unexpectedly null for node at $(Format-DbgAddress $rootAddr)."
            return $false
        }
        elseif( $child.DbgGetPointee() -is [MS.Dbg.DbgUnavailableValue] )
        {
            Write-Warning "$which child value unavailable for node at $(Format-DbgAddress $rootAddr)."
            return $false
        }
        elseif( $child._Isnil -ne 0 )
        {
            Write-Warning "$which child _Isnil unexpectedly non-zero for node at $(Format-DbgAddress $rootAddr)."
            return $false
        }
        return $true
    } # end ChildLooksOkay()


    function ProcessSubtree( $rootNode, $scriptBlockForRootVal )
    {
        # We'll process them in the proper order: left subtreee, current
        # node, right subtree.
        if( $dummyHead -ne $rootNode._Left )
        {
            # We need to protect against bad/unavailable data.
            if( (ChildLooksOkay $rootNode.DbgGetPointer() 'Left' $rootNode._Left) )
            {
                ProcessSubtree $rootNode._Left $scriptBlockForRootVal
            }
        }
        & $scriptBlockForRootVal $rootNode._Myval
        if( $dummyHead -ne $rootNode._Right )
        {
            # We need to protect against bad/unavailable data.
            if( (ChildLooksOkay $rootNode.DbgGetPointer() 'Right' $rootNode._Right) )
            {
                ProcessSubtree $rootNode._Right $scriptBlockForRootVal
            }
        }
    } # end ProcessSubtree


    New-DbgValueConverterInfo -TypeName @( '!std::map<?*>', '!std::multimap<?*>' ) -Converter {
        # map and multimap are basically the same (for our purposes), except one can have
        # duplicate keys. The PsIndexedDictionary can handle that.
        try
        {
            # $_ is the symbol
            $stockValue = $_.GetStockValue()

            # Handle Dev14 types:
            if( $stockValue.PSObject.Properties.Match( '_Mypair' ).Count )
            {
                # New in Dev14 (VS 2015)
                # I don't know why there are two layers of _Myval2 here...
                $stockValue = $stockValue._Mypair._Myval2._Myval2
            }

            # This "root" node does not have a value, and is used as a sentinel (sub-tree pointers
            # point to the root to mean "no sub-tree").
            $dummyHead = $stockValue._Myhead
            <#assert#> if( $dummyHead._Isnil -ne 1 ) { throw "Expected dummy root to be 'nil'" }

            $d = New-Object "MS.Dbg.PsIndexedDictionary" -ArgumentList @( $stockValue._Mysize )

            # We'll do a recursive traversal of the tree, using this function:
            if( 0 -ne $stockValue._Mysize )
            {
                ProcessSubtree $dummyHead._Parent { $d.Add( $args[ 0 ].first, $args[ 0 ].second ) }
            }

            Add-Member -InputObject $d -MemberType ScriptMethod -Name 'ToString' -Value { $stockValue.ToString() }.GetNewClosure() -Force
            $d.Freeze()
            Write-Collection -Collection $d
        }
        finally { }
    } -CaptureContext # end map converter


    New-DbgValueConverterInfo -TypeName '!std::set<?*>', '!std::multiset<?*>' -Converter {
        # Sets are pretty much just like maps, but no key/value distinction (no
        # _Myval.first, _Myval.second--it's just _Myval).
        #
        # TODO: Factor out shared script once we have got script sharing worked out.
        try
        {
            # $_ is the symbol
            $stockValue = $_.GetStockValue()

            # Handle Dev14 types:
            if( $stockValue.PSObject.Properties.Match( '_Mypair' ).Count )
            {
                # New in Dev14 (VS 2015)
                # I don't know why there are two layers of _Myval2 here...
                $stockValue = $stockValue._Mypair._Myval2._Myval2
            }

            # This "root" node does not have a value, and is used as a sentinel (sub-tree pointers
            # point to the root to mean "no sub-tree").
            $dummyHead = $stockValue._Myhead
            <#assert#> if( $dummyHead._Isnil -ne 1 ) { throw "Expected dummy root to be 'nil'" }

            # Since sets are ordered, and we also don't want to have problems with
            # the default comparer tossing out values as duplicate, we'll just use
            # a simple list to represent the set.
            $list = New-Object "System.Collections.Generic.List[PSObject]" -ArgumentList @( $stockValue._Mysize )

            # We'll do a recursive traversal of the tree, using this function:
            if( 0 -ne $stockValue._Mysize )
            {
                ProcessSubtree $dummyHead._Parent { $list.Add( $args[ 0 ] ) }
            }

            $list = $list.AsReadOnly()
            Add-Member -InputObject $list -MemberType ScriptMethod -Name 'ToString' -Value { $stockValue.ToString() }.GetNewClosure() -Force
            Write-Collection -Collection $list
        }
        finally { }
    } -CaptureContext # end set converter


    New-DbgValueConverterInfo -TypeName '!std::list<?*>' -Converter {
        try
        {
            # $_ is the symbol
            $stockValue = $_.GetStockValue()

            # Handle Dev14 types:
            if( $stockValue.PSObject.Properties.Match( '_Mypair' ).Count )
            {
                # New in Dev14 (VS 2015)
                $stockValue = $stockValue._Mypair._Myval2
            }

            # This "root" node does not have a [valid] value, and is used as a sentinel (the
            # first and last list elements point to it from their _Prev and _Next pointers).
            $dummyHead = $stockValue._Myhead

            # It's a bi-directional linked list. head._Prev points to the end of the list, and
            # head._Next points to the first of the list.

            $list = New-Object "System.Collections.Generic.List[PSObject]" -ArgumentList @( $stockValue._Mysize )

            if( 0 -ne $stockValue._Mysize )
            {
                $curNode = $dummyHead._Next
                do
                {
                    $list.Add( $curNode._Myval )
                    $curNode = $curNode._Next
                } while( $curNode -ne $dummyHead )
            }

            <# assert #> if( $list.Count -ne $stockValue._Mysize ) { throw "sizes don't match!" }

            $list = $list.AsReadOnly()
            Add-Member -InputObject $list -MemberType ScriptMethod -Name 'ToString' -Value { $stockValue.ToString() }.GetNewClosure() -Force
            Write-Collection -Collection $list
        }
        finally { }
    } # end list converter


    New-DbgValueConverterInfo -TypeName '!std::forward_list<?*>' -Converter {
        try
        {
            # $_ is the symbol
            $stockValue = $_.GetStockValue()

            # Handle Dev14 types:
            if( $stockValue.PSObject.Properties.Match( '_Mypair' ).Count )
            {
                # New in Dev14 (VS 2015)
                $stockValue = $stockValue._Mypair._Myval2
            }

            # It's a unidirectional linked list.

            $list = New-Object "System.Collections.Generic.List[PSObject]"

            $curNode = $stockValue._Myhead
            while( !$curNode.DbgIsNull() )
            {
                $list.Add( $curNode._Myval )
                $curNode = $curNode._Next
            }

            $list = $list.AsReadOnly()

            # Preserve the original ToString() so we get the type name 'n stuff:
            Add-Member -InputObject $list `
                       -MemberType ScriptMethod `
                       -Name 'ToString' `
                       -Value { $stockValue.ToString() }.GetNewClosure() `
                       -Force

            # Use Write-Collection to prevent PS from unrolling the collection.
            Write-Collection -Collection $list
        }
        finally { }
    } # end forward_list converter


    New-DbgValueConverterInfo -TypeName @( '!stdext::hash_map<?*>', '!std::unordered_map<?*>') -Converter {
        try
        {
            # $_ is the symbol
            $stockValue = $_.GetStockValue()

            # It has some fancy stuff... but it also has a list for easy iteration. We'll
            # just use that.

            $d = New-Object "MS.Dbg.PsIndexedDictionary" -ArgumentList @( $stockValue._List.Count )

            foreach( $pair in $stockValue._List )
            {
                $d.Add( $pair.first, $pair.second )
            }

            $d.Freeze()
            Add-Member -InputObject $d -MemberType ScriptMethod -Name 'ToString' -Value { $stockValue.ToString() }.GetNewClosure() -Force
            Write-Collection -Collection $d
        }
        finally { }
    } # end hash_map converter


    New-DbgValueConverterInfo -TypeName '!std::unique_ptr<?*>' -Converter {
        try
        {
            # $_ is the symbol
            $stockValue = $_.GetStockValue()

            # Handle Dev14 types:
            if( $stockValue.PSObject.Properties.Match( '_Mypair' ).Count )
            {
                # New in Dev14 (VS 2015)
                return $stockValue._Mypair._Myval2
            }
            else
            {
                # unique_ptr is just a resource management wrapper around a pointer. We can make
                # things easier to see in the debugger by just hiding the unique_ptr layer (which
                # has nothing in it anyway; just the pointer).
                return $stockValue._Myptr
            }
        }
        finally { }
    } # end unique_ptr converter


    New-DbgValueConverterInfo -TypeName '!std::tr2::sys::basic_path<?*>' -Converter {
        try
        {
            # $_ is the symbol
            $stockValue = $_.GetStockValue()

            # There's only one member:
            return $stockValue._Mystr
        }
        finally { }
    } # end basic_path converter
}
