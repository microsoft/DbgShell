using System;
using System.Diagnostics;

namespace MS.Dbg
{
    [DebuggerDisplay( "SimpleSym: {Name}" )]
    public class DbgSimpleSymbol : DbgSymbol
    {
        private ulong m_address;
        private DbgRegisterInfoBase m_register;
        private DbgNamedTypeInfo m_type;
        private DbgModuleInfo m_mod;

        public override ulong Address { get { return m_address; } }

        public override bool IsValueInRegister { get { return null != m_register; } }

        public override DbgRegisterInfoBase Register { get { return m_register; } }

        public override DbgNamedTypeInfo Type { get { return m_type; } }


        public override DbgModuleInfo Module
        {
            get
            {
                if( null == m_mod )
                {
                    if( null != Parent )
                        m_mod = Parent.Module;
                    else
                    {
                        // TODO: is this valid? What about with managed code?
                        m_mod = Type.Module;
                     // try
                     // {
                     //     m_mod = new DbgModuleInfo( Debugger, m_address );
                     // }
                     // catch( DbgEngException ) { } // Ignore it; only public symbols are in a module
                    }
                }
                return m_mod;
            }
        } // end property Module


        public uint Size
        {
            get { return (uint) Type.Size; } // TODO: what about types > 2GB?
        }


        private static DbgTarget _GetTargetFromType( DbgNamedTypeInfo type )
        {
            if( null == type )
                throw new ArgumentNullException( "type" );

            return type.Target;
        }

        private DbgSimpleSymbol( DbgEngDebugger debugger,
                                 string name,
                                 DbgNamedTypeInfo type )
            : base( debugger, name, _GetTargetFromType( type ) )
        {
            m_type = type;
        } // end constructor


        internal DbgSimpleSymbol( DbgEngDebugger debugger,
                                  string name,
                                  DbgNamedTypeInfo type,
                                  ulong address )
            : this( debugger, name, type )
        {
            m_address = address;
        } // end constructor


        internal DbgSimpleSymbol( DbgEngDebugger debugger,
                                  string name,
                                  DbgNamedTypeInfo type,
                                  DbgRegisterInfoBase register )
            : this( debugger, name, type )
        {
            if( null == register )
                throw new ArgumentNullException( "register" );

            m_register = register;
        } // end constructor


        internal DbgSimpleSymbol( DbgEngDebugger debugger,
                                  string name,
                                  DbgNamedTypeInfo type,
                                  ulong address,
                                  DbgSymbol parent )
            : this( debugger, name, type, address )
        {
            Parent = parent;
        }

        internal DbgSimpleSymbol( DbgEngDebugger debugger,
                                  string name,
                                  DbgNamedTypeInfo type,
                                  DbgRegisterInfoBase register,
                                  DbgSymbol parent )
            : this( debugger, name, type, register )
        {
            Parent = parent;
        } // end constructor


    } // end class DbgSimpleSymbol
}

