using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    public class DbgNativeFunction : DbgFunction
    {
        private DbgSymbol m_sym;

        public DbgNativeFunction( DbgEngDebugger debugger,
                                  DbgEngContext context,
                                  DbgSymbol symbol )
            : base( debugger, context, symbol.Address, symbol.Name )
        {
            m_sym = symbol;
        } // end constructor()


        public DbgSymbol Symbol
        {
            get
            {
                return m_sym;
            }
        }



        public override DbgModuleInfo Module { get { return m_sym.Module; } }


        public override ColorString ToColorString()
        {
            var cs = new ColorString().Append( DbgProvider.ColorizeModuleName( m_sym.Module.Name ) )
                                      .Append( "!" )
                                      .Append( DbgProvider.ColorizeTypeName( m_sym.Name ) );
            return cs;
        }


        public static bool TryCreateFunction( DbgEngDebugger debugger,
                                              DbgEngContext context,
                                              DEBUG_STACK_FRAME_EX nativeStackFrame,
                                              out DbgFunction function,
                                              out ulong displacement )
        {
            function = null;
            displacement = 0;
            DbgSymbol sym = null;
            try
            {
                SymbolInfo si = DbgHelp.SymFromInlineContext( debugger.DebuggerInterface,
                                                              nativeStackFrame.InstructionOffset,
                                                              nativeStackFrame.InlineFrameContext,
                                                              out displacement );
                sym = new DbgPublicSymbol( debugger, si, debugger.GetCurrentTarget() );
                function = new DbgNativeFunction( debugger, context, sym );
                return true;
            }
            catch( DbgProviderException dpe )
            {
                // Sometimes the debugger doesn't know. E.g., frame 'e' here (from ntsd):
                //    0:000> kn
                //    # Child-SP          RetAddr           Call Site
                //    00 00000000`0058dff8 00000000`76c02ef8 ntdll!NtRequestWaitReplyPort+0xa
                //    01 00000000`0058e000 00000000`76c352d1 kernel32!GetConsoleMode+0xf8
                //    02 00000000`0058e030 00000000`76c4a60c kernel32!VerifyConsoleIoHandle+0x281
                //    03 00000000`0058e180 000007fe`fae30fe1 kernel32!ReadConsoleW+0xbc
                //    04 00000000`0058e260 000007fe`fae1eb88 Microsoft_PowerShell_ConsoleHost_ni+0x70fe1
                //    05 00000000`0058e390 000007fe`fae2a7e2 Microsoft_PowerShell_ConsoleHost_ni+0x5eb88
                //    06 00000000`0058e410 000007fe`fae29fae Microsoft_PowerShell_ConsoleHost_ni+0x6a7e2
                //    07 00000000`0058e4c0 000007fe`fae32bd1 Microsoft_PowerShell_ConsoleHost_ni+0x69fae
                //    08 00000000`0058e5b0 000007fe`fae235c6 Microsoft_PowerShell_ConsoleHost_ni+0x72bd1
                //    09 00000000`0058e670 000007fe`fae23f27 Microsoft_PowerShell_ConsoleHost_ni+0x635c6
                //    0a 00000000`0058e6d0 000007fe`fade5006 Microsoft_PowerShell_ConsoleHost_ni+0x63f27
                //    0b 00000000`0058e760 000007fe`fade2c1a Microsoft_PowerShell_ConsoleHost_ni+0x25006
                //    0c 00000000`0058e7e0 000007fe`fae33588 Microsoft_PowerShell_ConsoleHost_ni+0x22c1a
                //    0d 00000000`0058e890 000007fe`97f805de Microsoft_PowerShell_ConsoleHost_ni+0x73588
                //    0e 00000000`0058e8f0 000007fe`f777dad3 0x000007fe`97f805de
                //    0f 00000000`0058ea80 000007fe`f777d7ae clr!PreBindAssemblyEx+0x13e07
                //    10 00000000`0058eac0 000007fe`f777d830 clr!PreBindAssemblyEx+0x13ae2
                //    11 00000000`0058eb00 000007fe`f76d0f3b clr!PreBindAssemblyEx+0x13b64
                //    12 00000000`0058ecb0 000007fe`f76a9e5a clr!GetHistoryFileDirectory+0x945b
                //    13 00000000`0058ee80 000007fe`f76a9d54 clr!InitializeFusion+0x8b12
                //    14 00000000`0058f170 000007fe`f76a98ce clr!InitializeFusion+0x8a0c
                //    15 00000000`0058f730 000007fe`f76a9826 clr!InitializeFusion+0x8586
                //    16 00000000`0058f7a0 000007fe`f76aa078 clr!InitializeFusion+0x84de
                //    17 00000000`0058f830 000007fe`f8247b95 clr!CorExeMain+0x14
                //    18 00000000`0058f870 000007fe`f82e5b21 mscoreei!CorExeMain+0x5d
                //    19 00000000`0058f8c0 00000000`76bf652d mscoree!CorExeMain+0x69
                //    1a 00000000`0058f8f0 00000000`772ec521 kernel32!BaseThreadInitThunk+0xd
                //    1b 00000000`0058f920 00000000`00000000 ntdll!RtlUserThreadStart+0x21
                LogManager.Trace( "Could not get symbol for stack frame {0} on thread index {1}. Error: {2}",
                                  nativeStackFrame.FrameNumber,
                                  context.ThreadIndexOrAddress,
                                  Util.GetExceptionMessages( dpe ) );
            }
            return false;
        } // end TryCreateFunction()

    } // end class DbgNativeFunction
}
