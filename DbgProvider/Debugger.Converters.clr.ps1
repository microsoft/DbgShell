
Register-DbgValueConverterInfo {

    Set-StrictMode -Version Latest

    # The Volatile<?> type is just a wrapper for a single thing, so we'll simplify things by
    # just converting it into the thing it wraps.
    New-DbgValueConverterInfo -TypeName '!Volatile<?>' -Converter {
        try
        {
            # $_ is the symbol
            $sym = $_
            $stockValue = $sym.GetStockValue()
            return $stockValue.m_val
        }
        finally { }
    } # end Volatile<?> converter

}

