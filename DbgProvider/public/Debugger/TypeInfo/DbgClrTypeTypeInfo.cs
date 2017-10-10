using System;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime;

namespace MS.Dbg
{
    [DebuggerDisplay( "ClrType: {Name}" )]
    public class DbgClrTypeTypeInfo : DbgNamedTypeInfo
    {
        private readonly ClrType m_clrType;

        public ClrType ClrType
        {
            get { return m_clrType; }
        } // end property ClrType


        protected override ColorString GetColorName()
        {
            throw new NotImplementedException();
        } // end GetColorName()


        private static ulong _CheckClrTypeParamAndGetImageBase( ClrType clrType )
        {
            if( null == clrType )
                throw new ArgumentNullException( "clrType" );

            return clrType.Module.ImageBase;
        }

        public DbgClrTypeTypeInfo( DbgEngDebugger debugger,
                                   ClrType clrType,
                                   DbgUModeProcess process )
            : base( debugger,
                    _CheckClrTypeParamAndGetImageBase( clrType ),
                    (uint) DbgNativeType.DNTYPE_CLR_TYPE,
                    SymTag.ManagedType,
                    clrType.Name,
                    (ulong) clrType.BaseSize,
                    process )
        {
            m_clrType = clrType;
        } // end constructor
    } // end class DbgClrTypeTypeInfo
}

