using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    // Note that UDTs /can/ live in registers*, so just having an address won't suffice.
    //
    // *: Consider a UDT with a single, pointer-sized member--it could easily get stored
    // in a register.
    [DebuggerDisplay( "MemberSym: {Name}" )]
    public class DbgMemberSymbol : DbgSymbol
    {
        public readonly DbgDataMemberTypeInfo MemberInfo;
        private ulong m_addr;


        private static string _GetName( DbgDataMemberTypeInfo memberInfo )
        {
            if( null == memberInfo )
                throw new ArgumentNullException( "memberInfo" );

            return memberInfo.Name;
        }

        private static DbgTarget _GetTargetFromParent( DbgSymbol parent )
        {
            if( null == parent )
                throw new ArgumentNullException( "parent" );

            return parent.Target;
        }


        internal DbgMemberSymbol( DbgEngDebugger debugger,
                                  DbgSymbol parent,
                                  DbgDataMemberTypeInfo memberInfo )
            : base( debugger, _GetName( memberInfo ), _GetTargetFromParent( parent ) )
        {
            Parent = parent;
            MemberInfo = memberInfo;

            if( Parent.IsValueInRegister )
                m_addr = 0;
            else
                m_addr = Parent.Address + memberInfo.Offset;
        } // end constructor


        //
        // DbgSymbol stuff
        //

        public override ulong Address
        {
            get { return m_addr; }
        }

        public override bool IsValueInRegister
        {
            get { return Parent.IsValueInRegister; }
        }

        public override DbgRegisterInfoBase Register
        {
            get { return Parent.Register; }
        }

        public override DbgModuleInfo Module
        {
            get { return Parent.Module; }
        }

        public override DbgNamedTypeInfo Type
        {
            get { return MemberInfo.DataType; }
        }
    } // end class DbgMemberSymbol
}

