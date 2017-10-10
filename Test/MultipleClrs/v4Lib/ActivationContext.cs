using System;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace v4Lib
{
    internal class ActivationContext : IDisposable
    {
        private IntPtr m_hActCtx;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr( -1L );

        private void _CreateCtx( NativeActivationContext actctx )
        {
            m_hActCtx = NativeMethods.CreateActCtx( actctx );
            if( (IntPtr.Zero == m_hActCtx) || (INVALID_HANDLE_VALUE == m_hActCtx) )
                throw new Win32Exception();
        } // end _CreateCtx()

        public ActivationContext( string source, int manifestResourceId )
        {
            var actctx = new NativeActivationContext();
            actctx.Source = source;
            actctx.ResourceName = new IntPtr( manifestResourceId );
            actctx.Flags = ActivationContextFlags.RESOURCE_NAME_VALID;
            _CreateCtx( actctx );
        } // end constructor


        public IDisposable Activate()
        {
            return new ActivationRecord( m_hActCtx );
        } // end Activate()


        public void Dispose()
        {
            if( IntPtr.Zero != m_hActCtx )
            {
                NativeMethods.ReleaseActCtx( m_hActCtx );
                m_hActCtx = IntPtr.Zero;
            }
        } // end Dispose()


        private class ActivationRecord : IDisposable
        {
            private UIntPtr m_cookie;

            public unsafe ActivationRecord( IntPtr hActCtx )
            {
                fixed( UIntPtr* cookie = &m_cookie )
                {
                    bool itWorked = NativeMethods.ActivateActCtx( hActCtx, cookie );
                    if( !itWorked )
                        throw new Win32Exception();
                }
            } // end constructor()

            public void Dispose()
            {
                bool itWorked = NativeMethods.DeactivateActCtx( 0, m_cookie );
                if( !itWorked )
                    throw new Win32Exception();
            } // end Dispose()
        } // end class ActivationRecord
    } // end ActivationContext
}

