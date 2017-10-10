using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    public partial class DbgEngDebugger : DebuggerObject
    {
        // This is the object given to the ClrMd library (Microsoft.Diagnostics.Runtime)
        // DataTarget object, to allow it to read stuff out of the process.
        private class DbgShellDebugClientDataReader : IDataReader
        {
            private DbgEngDebugger m_umd;
            private DbgTarget m_target;
            private bool m_closed;

            private void _CheckClosed()
            {
                // Our implementation doesn't actually close anything, so just an assert
                // is fine.
                Util.Assert( !m_closed );
            }


            public DbgShellDebugClientDataReader( DbgEngDebugger umd, DbgTarget proc )
            {
                m_umd = umd;
                m_target = proc;
            }

            public void Close()
            {
                m_closed = true;
            }

            public void Flush()
            {
                _CheckClosed();
                Util.Fail( "Just want to see when this gets called." );
            }

            public Architecture GetArchitecture()
            {
                _CheckClosed();
                using( new DbgEngContextSaver( m_umd, m_target.Context ) )
                {
                    IMAGE_FILE_MACHINE machineType = m_umd.GetEffectiveProcessorType();
                    switch (machineType)
                    {
                        case IMAGE_FILE_MACHINE.I386:
                            return Architecture.X86;

                        case IMAGE_FILE_MACHINE.AMD64:
                            return Architecture.Amd64;

                        case IMAGE_FILE_MACHINE.ARM:
                        case IMAGE_FILE_MACHINE.THUMB:
                        case IMAGE_FILE_MACHINE.THUMB2:
                            return Architecture.Arm;

                        default:
                            return Architecture.Unknown;
                    }
                } // end using( ctx )
            } // end GetArchitecture()

            public uint GetPointerSize()
            {
                _CheckClosed();
                if( m_target.Is32Bit )
                    return 4;
                else
                    return 8;
            }

            public IList<ModuleInfo> EnumerateModules()
            {
                _CheckClosed();
                return m_target.Modules.Select( ( x ) => x.ToClrMdModuleInfo( this ) ).ToList();
            }

            public void GetVersionInfo( ulong baseAddress, out VersionInfo version )
            {
                _CheckClosed();
                var mod = m_target.Modules.Where( ( x ) => x.BaseAddress == baseAddress ).FirstOrDefault();
                if( null == mod )
                {
                    Util.Fail( "Bogus base address?" );
                    version = new VersionInfo();
                    return;
                }

                if( null == mod.VersionInfo.FixedFileInfo )
                {
                    // Could happen, for instance, in kernel mode when memory is paged out.
                    LogManager.Trace( "Missing FixedFileInfo for module at {0}.", Util.FormatQWord( baseAddress ) );
                    version = new VersionInfo();
                    return;
                }

                version = mod.VersionInfo.FixedFileInfo.ToClrMdVersionInfo();
            } // end GetVersionInfo()

            public unsafe bool ReadMemory( ulong address, byte[] buffer, int bytesRequested, out int bytesRead )
            {
                _CheckClosed();
                if( null == buffer )
                    throw new ArgumentNullException( "buffer" );

                int tmpBytesRead = 0;
                bool bResult = m_umd.ExecuteOnDbgEngThread( () =>
                    {
                        using( new DbgEngContextSaver( m_umd, m_target.Context ) )
                        {
                            fixed( byte* pBuf = buffer )
                            {
                                uint uiBytesRead;
                                int hr = m_umd.m_debugDataSpaces.ReadVirtualDirect( address,
                                                                                    (uint) bytesRequested,
                                                                                    pBuf,
                                                                                    out uiBytesRead );
                                tmpBytesRead = (int) uiBytesRead;
                                return hr == 0;
                            }
                        } // end using( ctx )
                    } );
                bytesRead = tmpBytesRead;
                return bResult;
            } // end ReadMemory()

            public unsafe bool ReadMemory( ulong address, IntPtr buffer, int bytesRequested, out int bytesRead )
            {
                _CheckClosed();
                if( ((UInt64) buffer.ToInt64()) < 4096 )
                    throw new ArgumentException( "The buffer pointer is bad." );

                int tmpBytesRead = 0;
                bool bResult = m_umd.ExecuteOnDbgEngThread( () =>
                    {
                        using( new DbgEngContextSaver( m_umd, m_target.Context ) )
                        {
                            uint uiBytesRead;
                            int hr = m_umd.m_debugDataSpaces.ReadVirtualDirect( address,
                                                                                (uint) bytesRequested,
                                                                                (byte*) buffer,
                                                                                out uiBytesRead );
                            tmpBytesRead = (int) uiBytesRead;
                            return hr == 0;
                        } // end using( ctx )
                    } );
                bytesRead = tmpBytesRead;
                return bResult;
            }

            public bool CanReadAsync
            {
                // TODO?
                get { _CheckClosed();  return false; }
            }

            public bool IsMinidump
            {
                get
                {
                    _CheckClosed();
                    return m_umd.ExecuteOnDbgEngThread( () =>
                        {
                            using( new DbgEngContextSaver( m_umd, m_target.Context ) )
                            {
                                DEBUG_CLASS cls;
                                DEBUG_CLASS_QUALIFIER qual;
                                m_umd.m_debugControl.GetDebuggeeType( out cls, out qual );

                                if( qual == DEBUG_CLASS_QUALIFIER.USER_WINDOWS_SMALL_DUMP )
                                {
                                    DEBUG_FORMAT flags;
                                    m_umd.m_debugControl.GetDumpFormatFlags( out flags );
                                    // TODO: ClrMd's DbgEngDataReader caches this bit of info.
                                    // Should I?
                                    return (flags & DEBUG_FORMAT.USER_SMALL_FULL_MEMORY) == 0;
                                }
                                return false;
                            }
                        } );
                }
            } // end IsMinidump

            public ulong GetThreadTeb( uint thread )
            {
                _CheckClosed();
                using( new DbgEngContextSaver( m_umd, m_target.Context ) )
                {
                    var t = m_umd.GetThreadBySystemTid( thread );
                    return t.TebAddress;
                }
            } // end GetThreadTeb()

            public IEnumerable< uint > EnumerateAllThreads()
            {
                _CheckClosed();
                throw new Exception( "tbd" );
                //return m_target.EnumerateThreads().Select( ( x ) => x.Tid );
            }

            public bool VirtualQuery( ulong addr, out VirtualQueryData vq )
            {
                _CheckClosed();
                using( new DbgEngContextSaver( m_umd, m_target.Context ) )
                {
                    vq = new VirtualQueryData();

                    try
                    {
                        MEMORY_BASIC_INFORMATION64 mem = m_umd.QueryVirtual( addr );
                        vq.BaseAddress = mem.BaseAddress;
                        vq.Size = mem.RegionSize;
                        return true;
                    }
                    catch( DbgProviderException dpe )
                    {
                        LogManager.Trace( "DbgShellDebugClientDataReader.VirtualQuery: ignoring \"{0}\".",
                                          Util.GetExceptionMessages( dpe ) );
                        return false;
                    }
                }
            } // end VirtualQuery()

            public unsafe bool GetThreadContext( uint threadID, uint contextFlags, uint contextSize, IntPtr context )
            {
                _CheckClosed();
                return m_umd.ExecuteOnDbgEngThread( () =>
                    {
                        using( new DbgEngContextSaver( m_umd, m_target.Context ) )
                        {
                            var t = m_umd.GetThreadBySystemTid( threadID );
                            using( new DbgEngContextSaver( m_umd, t.Context.WithoutFrameIndex() ) )
                            {
                                // TODO: What are contextFlags? It looks like I usually get passed
                                // CONTEXT_ALL, but I'm not sure what I should do if I got
                                // something different... do I need to set context->ContextFlags?
                                int hr = m_umd.m_debugAdvanced.GetThreadContext( (byte*) context, contextSize );
                                return 0 == hr;
                            }
                        }
                    } );
            }

            public unsafe bool GetThreadContext( uint threadID, uint contextFlags, uint contextSize, byte[] context )
            {
                _CheckClosed();
                fixed( byte* pContext = context )
                {
                    return GetThreadContext( threadID, contextFlags, contextSize, new IntPtr( pContext ) );
                }
            }

            public ulong ReadPointerUnsafe( ulong addr )
            {
                _CheckClosed();
                using( new DbgEngContextSaver( m_umd, m_target.Context ) )
                {
                    return m_umd.ReadMemAs_pointer( addr );
                }
            }

            public uint ReadDwordUnsafe( ulong addr )
            {
                _CheckClosed();
                using( new DbgEngContextSaver( m_umd, m_target.Context ) )
                {
                    return m_umd.ReadMemAs_UInt32( addr );
                }
            }
        } // end class DbgShellDebugClientDataReader
    } // end class DbgEngDebugger
}

