using System;

namespace MS.Dbg
{
    internal sealed class Disposable : IDisposable
    {
        private Action m_onDispose;
        public Disposable( Action onDispose )
        {
            if( null == onDispose )
                throw new ArgumentNullException( "onDispose" );

            m_onDispose = onDispose;
        }

        public static explicit operator Disposable( Action onDispose )
        {
            return new Disposable( onDispose );
        }

        public void Dispose()
        {
            if( null != m_onDispose )
            {
                m_onDispose();
                m_onDispose = null;
            }
        } // end Dispose()
    } // end class Disposable
}

