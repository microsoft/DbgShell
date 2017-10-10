
Register-DbgValueConverterInfo {

    Set-StrictMode -Version Latest


    New-DbgValueConverterInfo -TypeName @( '!Platform::Guid' ) -Converter {
        try
        {
            # $_ is the symbol
            $sym = $_
            return $sym.ReadAs_Guid()
        }
        finally { }
    } # end Guid converter
}

