
Register-DbgValueConverterInfo {

    Set-StrictMode -Version Latest

    New-DbgValueConverterInfo -TypeName '!utl::basic_string<?*>' -Converter {
        try
        {
            # $_ is the symbol
            $stockValue = $_.GetStockValue()

            # A pointer to the start of the string is stored in _MyBegin; let's just return that.
            # TODO: Have not tested with single-byte strings; will it be in the form of a string?
            return $stockValue._MyBegin
        }
        finally { }
    }

    New-DbgValueConverterInfo -TypeName '!utl::unique_ptr<?*>' -Converter {
        try
        {
            # $_ is the symbol
            $stockValue = $_.GetStockValue()

            return $stockValue._MyDat._MyRealVal
        }
        finally { }
    }
}
