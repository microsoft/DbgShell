
Register-DbgValueConverterInfo {

    Set-StrictMode -Version Latest

    New-DbgValueConverterInfo -TypeName @( '!tlx::auto_xxx<?*>', 'tlx::auto_array_ptr<?*>', 'tlx::auto_ptr<?*>' ) -Converter {
        try
        {
            # $_ is the symbol
            $stockValue = $_.GetStockValue()

            # Just another smart pointer.
            return $stockValue._MyResource
        }
        finally { }
    }

}
