using System;
using System.Linq;
using System.Management.Automation;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interop;
using DbgEngWrapper;

namespace MS.Dbg
{
    internal interface ICanSetContext
    {
        void SetContext();
    }

    public abstract class DebuggerObject
    {
        internal const uint DEBUG_ANY_ID = unchecked( (uint) 0xffffffff );
        internal const uint S_FALSE = 0x00000001;
        internal const int S_OK = 0;
        internal const int E_INVALIDARG = unchecked( (int) 0x80070057 );
        internal const int E_NOINTERFACE = unchecked( (int) 0x80004002 );
        internal const int E_NOTIMPL = unchecked( (int) 0x80004001 );
        internal const int E_UNEXPECTED = unchecked( (int) 0x8000ffff );
        internal const int E_ACCESSDENIED = unchecked( (int) 0x80070005 );
        internal const int HR_ERROR_READ_FAULT = unchecked( (int) 0x8007001e );
        internal const int HR_ERROR_PARTIAL_COPY = unchecked( (int) 0x8007012b );
        internal const int ERROR_NOACCESS = unchecked( (int) 0x800703e6 );
        internal const int HR_BAD_BP_OFFSET = unchecked( (int) 0x80070016 ); // HRESULT_FROM_WIN32( ERROR_BAD_COMMAND )
        internal const int HR_ERROR_FILE_NOT_FOUND = unchecked( (int) 0x80070002 );
        internal const int E_FAIL = unchecked( (int) 0x80040005 );

        // This is HRESULT_FOR_WIN32 for ERROR_NESTING_NOT_ALLOWED. HR_ILLEGAL_NESTING is
        // the symbolic name used in dbgeng sources.
        internal const int HR_ILLEGAL_NESTING = unchecked( (int) 0x800700d7 );

        // These 0xd.. codes are the result of HRESULT_FROM_NT( 0xc... ).
        internal const int HR_STATUS_PORT_ALREADY_SET = unchecked( (int) 0xd0000048 );
        internal const int HR_STATUS_PORT_NOT_SET     = unchecked( (int) 0xd0000353 );
        internal const int HR_STATUS_NOT_SUPPORTED    = unchecked( (int) 0xd00000bb );
        internal const int HR_STATUS_NO_PAGEFILE      = unchecked( (int) 0xd0000147 );
        internal const int HR_STATUS_NO_MORE_ENTRIES  = unchecked( (int) 0x9000001a );

        internal const ulong InvalidAddress = unchecked( (ulong) 0xffffffffffffffff ); // like DEBUG_INVALID_OFFSET

        public DbgEngDebugger Debugger { get; private set; }


        protected DebuggerObject( DbgEngDebugger debugger )
        {
            if( null == debugger )
            {
                if( this is DbgEngDebugger )
                    Debugger = (DbgEngDebugger) this;
                else
                    throw new ArgumentNullException( "debugger" );
            }
            else
            {
                Debugger = debugger;
            }
        } // end constructor


        // TODO: nice-ify, log, etc.
        protected void CheckHr( int hresult )
        {
            if( hresult < 0 )
            {
                if( hresult == HR_ILLEGAL_NESTING )
                    throw new DbgEngIllegalNestingException();
                else
                    throw new DbgEngException( hresult );
            }
            if( hresult > 0 )
            {
                Util.Fail( Util.Sprintf( "I'm not handling success HRESULTS besides S_OK, like this one: 0x{0:x}.", hresult ) );
            }
        } // end CheckHr()

        // TODO: nice-ify, log, etc.
        protected static void StaticCheckHr( int hresult )
        {
            if( hresult < 0 )
            {
                //System.Diagnostics.Debugger.Break();
                throw new DbgEngException( hresult );
            }
            if( hresult > 0 )
            {
                Util.Fail( Util.Sprintf( "I'm not handling success HRESULTS besides S_OK, like this one: 0x{0:x}.", hresult ) );
            }
        } // end StaticCheckHr()


        public string GetNameByInlineContext( ulong address, uint inlineContext, out ulong displacement )
        {
            string name = null;
            ulong tmpDisplacement = 0xffffffffffffffff;
            Debugger.ExecuteOnDbgEngThread( () =>
                {
                    WDebugSymbols ds = (WDebugSymbols) Debugger.DebuggerInterface;

                    CheckHr( ds.GetNameByInlineContextWide( address, inlineContext, out name, out tmpDisplacement ) );
                } );
            displacement = tmpDisplacement;
            return name;
        } // end GetNameByInlineContext()


        public string TryGetNameByInlineContext( ulong address, uint inlineContext, out ulong displacement )
        {
            string localName = null;
            ulong localDisplacement = 0xffffffffffffffff;
            Debugger.ExecuteOnDbgEngThread( () =>
                {
                    WDebugSymbols ds = (WDebugSymbols) Debugger.DebuggerInterface;

                    string tmpName = null;
                    ulong tmpDisplacement = 0;
                    int hr = ds.GetNameByInlineContextWide( address, inlineContext, out tmpName, out tmpDisplacement );
                    if( S_OK == hr )
                    {
                        localName = tmpName;
                        localDisplacement = tmpDisplacement;
                    }
                } );
            displacement = localDisplacement;
            return localName;
        } // end GetNameByInlineContext()


        /// <summary>
        ///    Gets the symbol name for the specified address. Throws a
        ///    DbgProviderException if it fails. Do not use for getting stack frame stuff;
        ///    this won't get inline frame info. Also consider using FindSymbolForAddress
        ///    if you need an actual symbol.
        /// </summary>
        public string GetNameByOffset( ulong offset, out ulong displacement )
        {
            string name = null;
            if( !TryGetNameByOffset( offset, out name, out displacement ) )
            {
                throw new DbgProviderException( Util.Sprintf( "Could not GetNameByOffset for address {0}.",
                                                              Util.FormatHalfOrFullQWord( offset ) ),
                                                              "GetNameByOffsetFailed",
                                                              ErrorCategory.ObjectNotFound );
            }

            return name;
        } // end GetNameByOffset()


        /// <summary>
        ///    Gets the symbol name for the specified address. Do not use for getting
        ///    stack frame stuff; this won't get inline frame info. Also consider using
        ///    FindSymbolForAddress
        /// </summary>
        public bool TryGetNameByOffset( ulong offset, out string name, out ulong displacement )
        {
            name = null;
            bool itWorked = false;
            string tmpName = null;
            ulong tmpDisplacement = 0xffffffffffffffff;
            Debugger.ExecuteOnDbgEngThread( () =>
                {
                    WDebugSymbols ds = (WDebugSymbols) Debugger.DebuggerInterface;

                    int hr = ds.GetNameByOffsetWide( offset, out tmpName, out tmpDisplacement );
                    if( 0 == hr )
                        itWorked = true;
                } );

            displacement = tmpDisplacement;
            if( itWorked )
                name = tmpName;

            return itWorked;
        } // end TryGetNameByOffset()


        // TODO: Why is this method here? Belongs in DbgEngDebugger, methinks.
        /// <summary>
        ///    Gets the symbol name for the specified address. Do not use for getting
        ///    stack frame stuff; this won't get inline frame info.
        /// </summary>
        public string GetNearNameByOffset( ulong offset, int delta, out long displacement )
        {
            string tmpName = null;
            ulong tmpDisplacement = 0xffffffffffffffff;
            Debugger.ExecuteOnDbgEngThread( () =>
                {
                    WDebugSymbols ds = (WDebugSymbols) Debugger.DebuggerInterface;

                    int hr = ds.GetNearNameByOffsetWide( offset, delta, out tmpName, out tmpDisplacement );
                    if( E_NOINTERFACE != hr )
                        CheckHr( hr );
                } );

            displacement = (long) tmpDisplacement;
            // N.B. The displacement is an unsigned value. So if we asked for a negative
            // delta, we are to understand that the displacement is also negative. Best
            // to just encode that for our caller by using a signed value instead.
            //
            // 0 also will result in a negative displacement, because if we are asking for
            // a delta of 0 and the displacement is not zero, the symbol we've got is
            // 'displacement' bytes /after/ the address we asked about (the next symbol).
            //
            // (displacement of 0 means that GetNearNameByOffsetWide will behave the same
            // as GetNameByOffset, which means "get me the symbol that is at this address,
            // or this first one that comes after it")
            if( delta <= 0 )
                displacement = -displacement;

            return tmpName;
        } // end GetNearNameByOffset()


        public DbgSymbol GetSymbolByAddress( ulong address, out ulong displacement )
        {
            // TODO: Should I handle exceptions and return null?
            ulong tmpDisplacement = 0xffffffffffffffff;
            var retval = Debugger.ExecuteOnDbgEngThread( () =>
                {
                    SymbolInfo si = DbgHelp.SymFromAddr( Debugger.DebuggerInterface, address, out tmpDisplacement );
                    // TODO: This doesn't seem like a good idea to use whatever the "current" context
                    // is here... but what can I do? Perhaps I could just get rid of this method.
                    return new DbgPublicSymbol( Debugger, si, Debugger.GetCurrentTarget() );
                } );
            displacement = tmpDisplacement;
            return retval;
        } // end GetSymbolByAddress()


        public ulong GetOffsetByName( string symbolName )
        {
            return Debugger.ExecuteOnDbgEngThread( () =>
                {
                    ulong offset;
                    WDebugSymbols ds = (WDebugSymbols) Debugger.DebuggerInterface;
                    int hr = ds.GetOffsetByNameWide( symbolName, out offset );
                    if( S_FALSE == hr )
                    {
                        // The symbolName was ambiguous.
                        var matches = Debugger.FindSymbol_Enum( symbolName ).ToArray();
                        Util.Assert( (matches != null) && (matches.Length > 1) );
                        throw new DbgProviderException( Util.Sprintf( "Ambiguous symbol error at: '{0}'.",
                                                                      symbolName ),
                                                        "AmbiguousSymbol",
                                                        System.Management.Automation.ErrorCategory.InvalidArgument,
                                                        matches );
                    }
                    else
                    {
                        CheckHr( hr );
                    }
                    return offset;
                } );
        } // end GetOffsetByName()
    } // end class DebuggerObject
}
