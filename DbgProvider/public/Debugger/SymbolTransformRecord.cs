namespace MS.Dbg
{
    /// <summary>
    ///    Represents a symbol (location + type) in a chain of symbol transformations
    ///    derived when creating a DbgValue. Note that in a chain of SymbolHistoryRecords,
    ///    the symbol in each record represents the "previous" symbol.
    /// </summary>
    /// <remarks>
    ///    SymbolHistoryRecords are pretty minimal--a given SymbolHistoryRecord does not
    ///    capture both 'A' and 'B' for a conversion from 'A' to 'B'; it only captures 'A'
    ///    or 'B'.
    ///
    ///    So every DbgValue has at least one SymbolHistoryRecord in its transform
    ///    history, even though it could be said that there was no transformation.
    /// </remarks>
    public class SymbolHistoryRecord
    {
        public readonly DbgSymbol OriginalSymbol;

        internal SymbolHistoryRecord( DbgSymbol originalSymbol )
        {
            Util.Assert( null != originalSymbol );
            OriginalSymbol = originalSymbol;
        }
    } // end class SymbolHistoryRecord


    /// <summary>
    ///    Represents a symbol which Derived Type Detection was applied to.
    /// </summary>
    /// <remarks>
    ///    Note that the symbol contained in the record is not the symbol for the derived
    ///    type; it's the original symbol, before DTD was applied to it.
    /// </remarks>
    public class DtdRecord : SymbolHistoryRecord
    {
        internal DtdRecord( DbgSymbol originalSymbol )
            : base( originalSymbol )
        {
        }
    } // end class DtdRecord


    /// <summary>
    ///    Represents a symbol which Symbol Value Conversion was applied to.
    /// </summary>
    /// <remarks>
    ///    Note that the symbol contained in the record is not the symbol for the
    ///    SVC-computed type; it's the original symbol, before SVC was applied to it.
    /// </remarks>
    public class SvcRecord : SymbolHistoryRecord
    {
        public readonly string ConverterApplied;

        internal SvcRecord( string converterApplied, DbgSymbol originalSymbol )
            : base( originalSymbol )
        {
            ConverterApplied = converterApplied;
        }
    } // end class SvcRecord
}

