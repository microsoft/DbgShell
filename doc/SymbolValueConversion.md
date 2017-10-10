# Custom Symbol Value Conversion
DbgShell lets you get symbol values as nice objects. This is extremely powerful for
scripting!

For most objects, the stock object given to you by DbgShell is fine, but some objects are
very painful to deal with in "raw" form&mdash;notably STL containers. So DbgShell has a
generic "custom symbol value conversion" facility, which lets you register some little
snippet of script to take a stock symbol value object and turn it into something more
useful. You can add properties or methods, or create an entirely new object.

DbgShell comes with a few of these little conversion snippets, including some for dealing
with STL containers: 

 STL symbol value type      |        | Gets tranformed into a .NET object of this type    
 ---------------------      | ------ | -----------------------------------------------    
`std::string, std::wstring` | &rarr; | `System.String`                                    
`std::vector<*>`            | &rarr; | `System.Collections.ObjectModel.ReadOnlyCollection`
`std::map<*>`               | &rarr; | `System.Collections.ObjectModel.ReadOnlyDictionary`
`std::set<*>`               | &rarr; | `System.Collections.ObjectModel.ReadOnlyCollection`

## Examples
To give you an idea how these work, here’s the script for dealing with STL vectors:

```powershell
Register-DbgValueScriptConverter -TypeName '!std::vector<?*>' -Converter {
    try
    {
        # $_ is the symbol
        $stockValue = $_.GetStockValue()

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
                $elem = Get-DbgSymbolValue -Address ($stockValue._Myfirst + $i) -Type $elemType # N.B. Pointer arithmetic
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

        Write-Collection -Collection $val
    }
    finally
    {
    }
} # end vector converter
```

That's relatively simple and gives you the general idea. The one for maps is a little more
involved, but still not too bad:

```powershell
Register-DbgValueScriptConverter -TypeName '!std::map<?*>' -Converter {
    try
    {
        # $_ is the symbol
        $stockValue = $_.GetStockValue()

        # This "root" node does not have a value, and is used as a sentinel (sub-tree pointers
        # point to the root to mean "no sub-tree").
        $dummyHead = $stockValue._Myhead
        <#assert#> if( $dummyHead._Isnil -ne 1 ) { throw "Expected dummy root to be 'nil'" }

        if( 0 -eq $stockValue._Mysize )
        {
            # Since the map is empty, we don't know what the key type will be...  but that's
            # okay, since it's empty.
            #
            # (We don't know what the key type will be, because even if we pulled the type
            # from the template parameters, a) that's the type in the target, and b) we don't
            # know what type key values would be after going through value conversion.)
            $d = New-Object 'System.Collections.Generic.Dictionary[PSObject,PSObject]' -ArgumentList @( 0 )
            $d = New-Object 'System.Collections.ObjectModel.ReadOnlyDictionary[PSObject,PSObject]' -ArgumentList @( $d )
            Write-Collection -Collection $d
            return
        }

        $rootNode = $dummyHead._Parent
        $sampleKey = $rootNode._Myval.first
        # Of course, there's no facility to specify a comparer. I suppose if someone has a
        # certain case where they want a special comparer, they could write their own
        # converter, more narrowly specified for just their specific type.
        [string] $keyTypeName = $sampleKey.GetType().FullName
        if( ($sampleKey -is [MS.Dbg.DbgNullValue]) -or
            ($sampleKey -is [MS.Dbg.DbgPointerValue]) )
        {
            # Because we need to be able to handle both DbgPointerValue objects and
            # DbgNullValue objects as keys. Having a separate DbgNullValue type makes most
            # things simpler, but it makes this particular scenario a little more
            # complicated.
            $keyTypeName = 'MS.Dbg.DbgPointerValueBase'
        }
        $d = New-Object "System.Collections.Generic.Dictionary[$($keyTypeName),PSObject]" -ArgumentList @( $stockValue._Mysize )

        # We'll do a recursive traversal of the tree, using this function:
        function ProcessSubtree( $rootNode )
        {
            <#assert#> if( $rootNode._Isnil -ne 0 ) { throw "ProcessSubtree called with invalid node." }
            # We don't preserve the order, but we'll go ahead and process them
            # in the proper order anyway: left subtreee, current node, right
            # subtree.
            if( $dummyHead -ne $rootNode._Left )
            {
                ProcessSubtree $rootNode._Left
            }
            $d.Add( $rootNode._Myval.first, $rootNode._Myval.second )
            if( $dummyHead -ne $rootNode._Right )
            {
                ProcessSubtree $rootNode._Right
            }
        } # end ProcessSubtree

        ProcessSubtree $rootNode

        $d = New-Object "System.Collections.ObjectModel.ReadOnlyDictionary[$($keyTypeName),PSObject]" -ArgumentList @( $d )
        Write-Collection -Collection $d
    }
    finally { }
} # end map converter
```

## More information
More information on various techniques for writing symbol value converters is available
from within DbgShell&mdash;just run "`Get-Help
about_HowTo_Write_a_Symbol_Value_Converter`". You can also see a bunch of existing
converter scripts by running `dir Bin:\Debugger\*.converters.*`.

