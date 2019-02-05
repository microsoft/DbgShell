using System.Diagnostics;

namespace MS.Dbg
{
    [DebuggerDisplay( "Data Static Member: Id {TypeId}: {Name}" )]
    public class DbgDataStaticMemberTypeInfo : DbgDataMemberTypeInfoBase
    {
        public readonly ulong Address;

        public readonly uint AddressOffset;


        public override bool IsStatic
        {
            get { return true; }
        }

        /// <summary>
        ///    If not null, this symbol represents a constant value (which will not have
        ///    an address).
        /// </summary>
        public readonly object ConstantValue;

        private DbgSymbol m_cachedSymbol;

        /// <summary>
        ///    Gets a (possibly cached) DbgSymbol representing this static member.
        /// </summary>
        public DbgSymbol GetSymbol()
        {
            if( null == m_cachedSymbol )
            {
                // Note that we don't pass a "Parent" symbol, as we want to share this
                // symbol between any instances of the owning type.

                if( ConstantValue != null )
                {
                    Util.Assert( Address == 0 );
                    Util.Assert( AddressOffset == 0 );

                    m_cachedSymbol = new DbgSimpleSymbol( Debugger,
                                                          Name,
                                                          DataType,
                                                          ConstantValue );
                }
                else
                {
                    m_cachedSymbol = new DbgSimpleSymbol( Debugger,
                                                          Name,
                                                          DataType,
                                                          Address );
                }
            }
            else
            {
                m_cachedSymbol.DumpCachedValueIfCookieIsStale();
            }
            return m_cachedSymbol;
        } // end GetSymbol()

        public DbgDataStaticMemberTypeInfo( DbgEngDebugger debugger,
                                            ulong moduleBase,
                                            uint typeId,
                                            RawDataInfo rdi,
                                            DbgTarget target )
            : base( debugger, moduleBase, typeId, rdi, target )
        {
            Address = rdi.Address;
            AddressOffset = rdi.AddressOffset;
            ConstantValue = rdi.Value;
        } // end constructor

        public DbgDataStaticMemberTypeInfo( DbgEngDebugger debugger,
                                            DbgModuleInfo module,
                                            uint typeId,
                                            RawDataInfo rdi )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    rdi,
                    module.Target )
        {
            __mod = module;
        } // end constructor
    } // end class DbgDataStaticMemberTypeInfo
}

