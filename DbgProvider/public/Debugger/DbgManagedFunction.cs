using Microsoft.Diagnostics.Runtime;

namespace MS.Dbg
{
    public class DbgManagedFunction : DbgFunction
    {
        private ClrMethod m_clrMethod;

        public ClrMethod Method { get { return m_clrMethod; } }

        public DbgManagedFunction( DbgEngDebugger debugger,
                                   DbgEngContext context,
                                   ClrMethod method )
            : base( debugger, context, method.NativeCode, method.Name )
        {
            m_clrMethod = method;
        } // end constructor()


        public override DbgModuleInfo Module
        {
            get
            {
                using( new DbgEngContextSaver( Debugger, Context ) )
                {
                    return Debugger.GetModuleByAddress( m_clrMethod.Type.Module.ImageBase );
                }
            }
        }


        public override ColorString ToColorString()
        {
            // TODO: This is the same as the DbgNativeFunction... factor out?
            // Or factor Module "in"? (Because maybe not all functions would have a
            // module?)
            var cs = new ColorString().Append( DbgProvider.ColorizeModuleName( Module.Name ) )
                                      .Append( "!" )
                                      .Append( DbgProvider.ColorizeTypeName( Name ) );
            return cs;
        }


      //public static bool TryCreateFunction( DbgEngDebugger debugger,
      //                                      DbgEngContext context,
      //                                      DEBUG_STACK_FRAME_EX nativeStackFrame,
      //                                      out DbgFunction function,
      //                                      out ulong displacement )
      //{
      //    function = null;
      //    displacement = 0;
      //    ClrMethod method = null;
      //    try
      //    {
      //        // TODO!
      //        function = new DbgManagedFunction( debugger, context, method );
      //        return true;
      //    }
      //    catch( DbgProviderException dpe )
      //    {
      //        LogManager.Trace( "Could not get a managed method for stack frame {0} on thread index {1}. Error: {2}",
      //                          nativeStackFrame.FrameNumber,
      //                          context.ThreadIndex,
      //                          Util.GetExceptionMessages( dpe ) );
      //    }
      //    return false;
      //} // end TryCreateFunction()
    } // end class DbgManagedFunction
}
