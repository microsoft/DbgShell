using System;
using System.Management.Automation;

namespace MS.Dbg
{
    internal class PSLazyPropertyInfo : PSPropertyInfo
    {
        private string m_valTypeName;
        private Func< object > m_getter;
        private bool m_valueRetrieved;
        private object m_value;

        public PSLazyPropertyInfo( string propName, Func< object > getter, string valTypeName )
        {
            if( null == getter )
                throw new ArgumentNullException( "getter" );

            if( String.IsNullOrEmpty( propName ) )
                throw new ArgumentException( "You must supply a property name.", "propName" );

            // All of our properties added through code should have value type names, but
            // sometimes we see properties without them--for instance, properties added
            // via Add-Member (which we use in some value converters).
            if( String.IsNullOrEmpty( valTypeName ) )
                valTypeName = "System.Object";

            m_getter = getter;
            m_valTypeName = valTypeName;
            SetMemberName( propName );
        } // end constructor


        public override bool IsGettable
        {
            get { return true; }
        }

        public override bool IsSettable
        {
            // TODO: Allow people to alter memory.
            get { return false; }
        }

        public override PSMemberInfo Copy()
        {
            return new PSLazyPropertyInfo( Name, m_getter, m_valTypeName );
        }

        public override PSMemberTypes MemberType
        {
            get { return PSMemberTypes.CodeProperty; }
        }

        public override string TypeNameOfValue
        {
            get { return m_valTypeName; }
        }

        public override object Value
        {
            get
            {
                if( !m_valueRetrieved )
                {
                    m_value = m_getter();
                    m_valueRetrieved = true;
                }
                return m_value;
            }
            set
            {
                throw new NotImplementedException();
            }
        } // end property Value


        /// <summary>
        ///    This is used in situations where we need to replace the value, such as
        ///    "manual" symbol value conversion.
        /// </summary>
        internal void DiscardCachedValue()
        {
            m_value = null;
            m_valueRetrieved = false;
        }
    } // end class PSLazyPropertyInfo
}

