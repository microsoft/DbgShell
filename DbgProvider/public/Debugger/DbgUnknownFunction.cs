using System;
using System.Linq;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    public class DbgUnknownFunction : DbgFunction
    {
        public DEBUG_STACK_FRAME_EX NativeFrameEx { get; private set; }


        private DbgUnknownFunction( DbgEngDebugger debugger,
                                    DbgEngContext context,
                                    DEBUG_STACK_FRAME_EX nativeFrame,
                                    ulong address,
                                    string modName,
                                    string funcName )
            : base( debugger, context, address, funcName )
        {
            NativeFrameEx = nativeFrame;
            m_modName = modName;
        } // end constructor()


        public static DbgUnknownFunction Create( DbgEngDebugger debugger,
                                                 DbgEngContext context,
                                                 DEBUG_STACK_FRAME_EX nativeFrame )
        {
            ulong address = nativeFrame.InstructionOffset;

            string modName = null;
            string funcName = null;
            ulong offset = 0;
            string fullname = null;

            // If our dbgeng has managed support (or support for whatever this is), let's
            // try that:

            if( 0 != nativeFrame.InstructionOffset )
            {
                fullname = debugger.TryGetInfoFromFrame( nativeFrame,
                                                         out modName,
                                                         out funcName,
                                                         out offset );

                if( String.IsNullOrEmpty( fullname ) )
                {
                    LogManager.Trace( "DbgUnknownFunction.Create: dbgeng doesn't know, either." );
                }
            }
            else
            {
                LogManager.Trace( "DbgUnknownFunction.Create: we have no chance with an InstructionOffset of zero." );
            }

            if( String.IsNullOrEmpty( fullname ) )
            {
                funcName = DbgProvider.FormatUInt64( address, useTick: true );
            }
            else
            {
                address = address - offset;
            }

            return new DbgUnknownFunction( debugger,
                                           context,
                                           nativeFrame,
                                           address,
                                           modName,
                                           funcName );
        } // end static Create() factory


        private bool m_triedModule;
        private string m_modName;
        private DbgModuleInfo m_module;

        public override DbgModuleInfo Module
        {
            get
            {
                if( !m_triedModule )
                {
                    m_triedModule = true;
                    Util.Assert( m_module == null );

                    if( !String.IsNullOrEmpty( m_modName ) )
                    {
                        try
                        {
                            m_module = Debugger.GetModuleByName( m_modName ).FirstOrDefault();
                        }
                        catch( DbgProviderException dpe2 )
                        {
                            LogManager.Trace( "Still couldn't get module: {0}", Util.GetExceptionMessages( dpe2 ) );
                        } // end workaround for lack of managed support
                    }
                    else
                    {
                        try
                        {
                            if( 0 == NativeFrameEx.InstructionOffset )
                            {
                                LogManager.Trace( "DbgUnknownFunction: Cannot get module for NativeFrameEx.InstructionOffset of 0." );
                            }
                            else
                            {
                                m_module = Debugger.GetModuleByAddress( NativeFrameEx.InstructionOffset );
                            }
                        }
                        catch( DbgProviderException dpe )
                        {
                            LogManager.Trace( "DbgUnknownFunction: Could not determine module for address 0x{0}: {1}",
                                              Util.FormatQWord( NativeFrameEx.InstructionOffset ),
                                              Util.GetExceptionMessages( dpe ) );
                            // Ignore it. (this is not surprising)
                        }
                    }
                }
                return m_module;
            }
        } // end Module


        public override ColorString ToColorString()
        {
            ColorString cs = new ColorString();
            if( null != Module )
            {
                cs.Append( DbgProvider.ColorizeModuleName( Module.Name ) ).Append( "!" );
            }
            cs.Append( DbgProvider.ColorizeTypeName( Name ) );
            return cs;
        }
    } // end class DbgUnknownFunction
}
