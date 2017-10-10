using System;
using System.Management.Automation;

namespace MS.Dbg
{
    internal class SimplePSPropertyInfo : PSPropertyInfo
    {
        private object m_val;

        public override bool IsGettable
        {
            get { return true; }
        }

        public override bool IsSettable
        {
            get { return false; }
        }

        public override PSMemberInfo Copy()
        {
            return new SimplePSPropertyInfo( Name, m_val );
        }

        public override PSMemberTypes MemberType
        {
            get { return PSMemberTypes.CodeProperty; }
        }

        public override string TypeNameOfValue
        {
            get
            {
                if( null == m_val )
                    return null;

                return m_val.GetType().ToString();
            }
        }

        public override object Value
        {
            get { return m_val; }
            set { throw new InvalidOperationException( "Property is read-only." ); }
        }

        public SimplePSPropertyInfo( string name, object val )
        {
            m_val = val;
            SetMemberName( name );
        } // end constructor
    } // end class SimplePSPropertyInfo
}


