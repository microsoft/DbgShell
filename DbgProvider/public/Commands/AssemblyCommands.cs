using System;
using System.Collections.Concurrent;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace MS.Dbg.Commands
{

    // Is it overkill to have these? After all, people can say "$Debugger.AssemblyOptions"...

    [Cmdlet( VerbsCommon.Get, "DbgAssemblyOptions" )]
    [OutputType( typeof( DbgAssemblyOptions ) )]
    public class GetDbgAssemblyOptionsCommand : DbgBaseCommand
    {
        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            WriteObject( Debugger.AssemblyOptions );
        } // end ProcessRecord()
    } // end class GetDbgAssemblyOptionsCommand


    [Cmdlet( VerbsCommon.Set, "DbgAssemblyOptions" )]
    public class SetDbgAssemblyOptionsCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true, Position = 0 )]
        public DbgAssemblyOptions Options { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            Debugger.AssemblyOptions = Options;
        } // end ProcessRecord()
    } // end class SetDbgAssemblyOptionsCommand


    [Cmdlet( VerbsCommunications.Read, "DbgDisassembly", DefaultParameterSetName = c_InstructionCountParamSet )]
    [OutputType( typeof( DbgDisassembly ) )]
    public class ReadDbgDisassemblyCommand : DbgBaseCommand
    {
        private const string c_InstructionCountParamSet = "InstructionCountParamSet";
        private const string c_WholeFunctionParamSet    = "WholeFunctionParamSet";
        private const string c_EndAddressParamSet       = "EndAddressParamSet";

        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true )]
        [AddressTransformation( FailGracefully = true )] // FailGracefully to allow ValueFromPipelineByPropertyName to work
        public ulong Address { get; set; }

        [Parameter( Mandatory = false,
                    Position = 1,
                    ParameterSetName = c_InstructionCountParamSet)]
        [Alias( "Count" )]
        public int InstructionCount { get; set; }

        [Parameter( Mandatory = false,
                    Position = 1,
                    ParameterSetName = c_WholeFunctionParamSet)]
        public SwitchParameter WholeFunction { get; set; }

        [Parameter( Mandatory = true,
                    Position = 1,
                    ParameterSetName = c_EndAddressParamSet )]
        [AddressTransformation]
        public ulong EndAddress { get; set; }

        // TODO: more options for uf?.

        // Normally you can't have [non-const] static members in PowerShell stuff, thanks
        // to runspaces, but dbgeng (and therefore dbgshell) doesn't support more than
        // one instance per process.
        public static ulong NextAddrToDisassemble { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            ulong addr = Address;
            if( 0 == addr )
                addr = NextAddrToDisassemble;

            if( 0 == addr )
            {
                // I've seen this case when looking at an iDNA dump, but didn't have time
                // to dig in.
                Util.Fail( "Why wasn't NextAddrToDisassemble set?" );
                addr = Debugger.GetCurrentScopeFrame().InstructionPointer;
            }

            try
            {
                if( WholeFunction )
                {
                    _UnassembleWholeFunction( addr );
                }
                else if( 0 != EndAddress )
                {
                    _UnassembleInstructions( addr, ( a ) =>
                        {
                            return a <= EndAddress;
                        } );
                }
                else
                {
                    if( 0 == InstructionCount )
                    {
                        InstructionCount = 8; // default
                    }
                    else if( InstructionCount < 0 )
                    {
                        // N.B. Windbg's "u <addr> L-<count>" does not work the same as
                        // ours: it just subtracts <count> bytes off of the start address
                        // rather than counting back <count> instructions. The way we do
                        // it is equivalent to windbg's "ub" (which /does/ count back
                        // <count> instructions).
                        addr = Debugger.GetNearInstruction( addr, InstructionCount );
                        InstructionCount = -InstructionCount;
                    }

                    int i = 0;
                    _UnassembleInstructions( addr, ( a ) =>
                        {
                            bool keepGoing = i < InstructionCount;
                            i++;
                            return keepGoing;
                        } );
                }
            }
            catch( DbgProviderException dpe )
            {
                WriteError( dpe );
            }
        } // end ProcessRecord()


        private void _UnassembleInstructions( ulong addr, Func< ulong, bool > keepGoing )
        {
            ColorString csBlockId;

            ulong disp;
            string addrName;
            if( !Debugger.TryGetNameByOffset( addr, out addrName, out disp ) )
            {
                // Ignore. We'll just use the address as the block id. If we really can't
                // get the memory there, we'll fail with a good error message later.
                csBlockId = new ColorString().Append( DbgProvider.FormatAddress( addr,
                                                                                 Debugger.TargetIs32Bit,
                                                                                 true ) );
            }
            csBlockId = DbgProvider.ColorizeSymbol( addrName );
            if( 0 != disp )
            {
                csBlockId.Append( Util.Sprintf( "+{0:x}", disp ) );
            }

            csBlockId.Append( ":" ).MakeReadOnly();

            bool hasCodeBytes = !Debugger.AssemblyOptions.HasFlag( DbgAssemblyOptions.NoCodeBytes );

            while( keepGoing( addr ) )
            {
                ulong tmpAddr = addr;
                string disasm = Debugger.Disassemble( tmpAddr, out addr ).Trim();
                WriteObject( _ParseDisassembly( tmpAddr,
                                                disasm,
                                                csBlockId,
                                                hasCodeBytes ) );
            }
            DbgProvider.SetAutoRepeatCommand( Util.Sprintf( "{0} -Address 0x{1} -InstructionCount {2}",
                                                            MyInvocation.InvocationName,
                                                            addr.ToString( "x" ),
                                                            InstructionCount ) );
            NextAddrToDisassemble = addr;
        } // end _UnassembleInstructions()


        private void _UnassembleWholeFunction( ulong addr )
        {
            using( BlockingCollection< string > output = new BlockingCollection<string>() )
            {
                var ufTask = Debugger.InvokeDbgEngCommandAsync( Util.Sprintf( "uf {0:x}", addr ),
                                                                String.Empty, // outputPrefix
                                                                ( x ) => output.Add( x.Trim() ) );
                ufTask.ContinueWith( ( x ) => output.CompleteAdding() );

                bool hasCodeBytes = !Debugger.AssemblyOptions.HasFlag( DbgAssemblyOptions.NoCodeBytes );
                using( var pipe = GetPipelineCallback() )
                {
                    var parser = new WholeFunctionDisasmParser( pipe, hasCodeBytes );
                    foreach( string line in output.GetConsumingEnumerable() ) // TODO: should this be cancellable?
                    {
                        parser.AddLine( line );
                    }
                }

                Util.Await( ufTask ); // in case it threw.

                NextAddrToDisassemble = addr;
            } // end using( output )
        } // end _UnassembleWholeFunction()


        private static Regex sm_asmRegex = new Regex( @"(?<addr>[0-9a-fA-F]{8}(`?[0-9a-fA-F]{8})?)" +
                                                      @"(?<space1>\s+)" +
                                                      @"(?<codebytes>[0-9a-fA-F]+)" +
                                                      @"(?<space2>\s+)" +
                                                      @"(?<instr>\S+)" +
                                                      @"(?<space3>\s+)?" +
                                                      @"(?<args>.+)?",
                                                      RegexOptions.Compiled );

        private static Regex sm_badAsmRegex = new Regex( @"(?<addr>[0-9a-fA-F]{8}(`?[0-9a-fA-F]{8})?)" +
                                                         @"(?<space1>\s+)" +
                                                         @"(?<dunno1>\?+)" +
                                                         @"(?<space2>\s+)" +
                                                         @"(?<dunno2>\?+)",
                                                         RegexOptions.Compiled );

        // For when the 'NoCodeBytes' option is on.
        private static Regex sm_asmRegex_noCodeBytes = new Regex( @"(?<addr>[0-9a-fA-F]{8}(`?[0-9a-fA-F]{8})?)" +
                                                                  @"(?<space1>\s+)" +
                                                                  // no code bytes
                                                                  @"(?<instr>\S+)" +
                                                                  @"(?<space3>\s+)?" +
                                                                  @"(?<args>.+)?",
                                                                  RegexOptions.Compiled );

        // For when the 'NoCodeBytes' option is on.
        private static Regex sm_badAsmRegex_noCodeBytes = new Regex( @"(?<addr>[0-9a-fA-F]{8}(`?[0-9a-fA-F]{8})?)" +
                                                                     // no code bytes
                                                                     @"(?<space2>\s+)" +
                                                                     @"(?<dunno2>\?+)",
                                                                     RegexOptions.Compiled );

        // Throws a DbgProviderException if the disassembly represents a bad memory access.
        internal static DbgDisassembly _ParseDisassembly( ulong address,
                                                          string s,
                                                          ColorString blockId,
                                                          bool hasCodeBytes )
        {
            // Example inputs:
            //
            //    0113162e 55              push    ebp
            //    0113162f 8bec            mov     ebp,esp
            //    01131631 51              push    ecx
            //    01131632 894dfc          mov     dword ptr [ebp-4],ecx
            //    01131635 8b45fc          mov     eax,dword ptr [ebp-4]
            //    01131638 c70068c81301    mov     dword ptr [eax],offset TestNativeConsoleApp!VirtualBase1::`vftable' (0113c868)
            //    0113163e 8b4508          mov     eax,dword ptr [ebp+8]
            //    01131641 83e001          and     eax,1
            //    01131644 740a            je      TestNativeConsoleApp!VirtualBase1::`scalar deleting destructor'+0x22 (01131650)
            //    01131646 ff75fc          push    dword ptr [ebp-4]
            //    01131649 ff1578c01301    call    dword ptr [TestNativeConsoleApp!_imp_??3YAXPAXZ (0113c078)]
            //    0113164f 59              pop     ecx
            //    01131650 8b45fc          mov     eax,dword ptr [ebp-4]
            //    01131653 c9              leave
            //    01131654 c20400          ret     4
            //
            // Here's what it looks like if the address is bad:
            //
            //    00007ff6`ece87d60 ??              ???
            //

            ColorString cs = new ColorString();
            byte[] codeBytes = null;
            string instruction = null;
            string arguments = null;

            Regex goodRegex;
            Regex badRegex;
            if( hasCodeBytes )
            {
                goodRegex = sm_asmRegex;
                badRegex = sm_badAsmRegex;
            }
            else
            {
                goodRegex = sm_asmRegex_noCodeBytes;
                badRegex = sm_badAsmRegex_noCodeBytes;
            }

            int matchCount = 0;
            foreach( Match match in goodRegex.Matches( s ) )
            {
                if( 0 == address )
                {
                    // Then we need to parse it out. (this is for -WholeFunction.
                    if( !DbgProvider.TryParseHexOrDecimalNumber( match.Groups[ "addr" ].Value, out address ) )
                        throw new Exception( Util.Sprintf( "Couldn't convert to address: {0}", match.Groups[ "addr" ].Value ) );
                }
#if DEBUG
                else
                {
                    ulong parsedAddress;
                    if( !DbgProvider.TryParseHexOrDecimalNumber( match.Groups[ "addr" ].Value, out parsedAddress ) )
                        throw new Exception( Util.Sprintf( "Couldn't convert to address: {0}", match.Groups[ "addr" ].Value ) );

                    // Nope: these are routinely different on ARM/THUMB2, where the low
                    // bit of the program counter is used as some sort of flag.
                    //Util.Assert( address == parsedAddress );
                }
#endif

                if( hasCodeBytes )
                    codeBytes = Util.ConvertToBytes( match.Groups[ "codebytes" ].Value );

                instruction = match.Groups[ "instr" ].Value;
                arguments = match.Groups[ "args" ].Value;

                matchCount++;
                cs.AppendPushPopFg( ConsoleColor.DarkCyan, match.Groups[ "addr" ].Value )
                  .Append( match.Groups[ "space1" ].Value );

                if( hasCodeBytes )
                {
                    cs.AppendPushPopFg( ConsoleColor.DarkGray, match.Groups[ "codebytes" ].Value )
                      .Append( match.Groups[ "space2" ].Value );
                }

                var instr = match.Groups[ "instr" ].Value;
                cs.Append( DbgProvider.ColorizeInstruction( instr ) );

                cs.Append( match.Groups[ "space3" ].Value );
                cs.Append( match.Groups[ "args" ].Value );
            }

            if( 0 == matchCount )
            {
                var match = badRegex.Match( s );
                if( (null != match) && match.Success )
                {
                    string addrString = match.Groups[ "addr" ].Value;
                    if( 0 == address )
                    {
                        if( !DbgProvider.TryParseHexOrDecimalNumber( addrString, out address ) )
                        {
                            Util.Fail( Util.Sprintf( "Couldn't convert to address: {0}", addrString ) );
                        }
                    }

                    throw new DbgMemoryAccessException( address,
                                                        Util.Sprintf( "No code found at {0}.",
                                                                      addrString ) );
                }
                else
                {
                    throw new Exception( Util.Sprintf( "TODO: Need to handle disassembly format: {0}", s ) );
                }
            }
            else
            {
                Util.Assert( 1 == matchCount );
            }

            return new DbgDisassembly( address,
                                       codeBytes,
                                       instruction,
                                       arguments,
                                       blockId,
                                       cs.MakeReadOnly() );
        } // end _ParseDisassembly()


        private class WholeFunctionDisasmParser
        {
            private enum WfdState
            {
                ExpectingBlockId,
                ExpectingInstructionOrBlankLine,
                // Well this is unfortunate. Sometimes after "Flow analysis was incomplete
                // ...", we don't get a blank line.
                DontKnow
            } // end enum WfdState

            private IPipelineCallback m_pipeline;
            private bool m_hasCodeBytes;
            private WfdState m_state;
            private ColorString m_currentBlockId;

            // TODO: Is anybody crazy enough to programmatically consume the disassembly
            // objects? If so, I could stick a flag on them saying if we ran into flow
            // analysis errors.
            //private bool m_flowAnalysisWarning;


            public WholeFunctionDisasmParser( IPipelineCallback pipeline, bool hasCodeBytes )
            {
                if( null == pipeline )
                    throw new ArgumentNullException( "pipeline" );

                m_pipeline = pipeline;
                m_hasCodeBytes = hasCodeBytes;
                m_state = WfdState.DontKnow;
            } // end constructor


            public void AddLine( string s )
            {
                switch( m_state )
                {
                    case WfdState.ExpectingBlockId:
                        if( (s.Length > 0) && (s[ s.Length - 1 ] != ':') )
                        {
                            // It's not a block ID... this can happen when "Flow
                            // analysis was incomplete".
                            m_currentBlockId = new ColorString( ConsoleColor.DarkGray, "<unknown>" ).MakeReadOnly();
                            m_pipeline.WriteObject( _ParseDisassembly( 0, // will get parsed out of s
                                                                       s,
                                                                       m_currentBlockId,
                                                                       m_hasCodeBytes ) );
                        }
                        else
                        {
                            m_currentBlockId = DbgProvider.ColorizeSymbol( s ).MakeReadOnly();
                        }
                        m_state = WfdState.ExpectingInstructionOrBlankLine;
                        break;
                    case WfdState.ExpectingInstructionOrBlankLine:
                        if( String.IsNullOrEmpty( s ) )
                        {
                            m_state = WfdState.ExpectingBlockId;
                            m_currentBlockId = null;
                        }
                        else
                        {
                            m_pipeline.WriteObject( _ParseDisassembly( 0, // will get parsed out of s
                                                                       s,
                                                                       m_currentBlockId,
                                                                       m_hasCodeBytes ) );
                        }
                        break;

                        // This is a horrible state. But sometimes we see it, like
                        // sometimes after a flow analysis error.
                    case WfdState.DontKnow:
                        if( s.StartsWith( "Flow analysis", StringComparison.OrdinalIgnoreCase ) )
                        {
                            //m_flowAnalysisWarning = true;
                            m_pipeline.WriteWarning( s );
                            m_state = WfdState.DontKnow;
                            break;
                        }
                        else if( s.StartsWith( "*** WARNING: ", StringComparison.OrdinalIgnoreCase ) )
                        {
                            m_pipeline.WriteWarning( s.Substring( 13 ) ); // trim off what will become redundant "WARNING" label.
                            m_state = WfdState.DontKnow;
                            break;
                        }
                        else if( s.StartsWith( "*** ERROR: ", StringComparison.OrdinalIgnoreCase ) )
                        {
                            s = s.Substring( 11 ); // trim off what will become redundant "ERROR" label.
                            var dpe = new DbgProviderException( s,
                                                                "ErrorDuringUnassemble",
                                                                ErrorCategory.ReadError );
                            try { throw dpe; } catch( Exception ) { } // give it a stack.
                            m_pipeline.WriteError( dpe );
                            m_state = WfdState.DontKnow;
                            break;
                        }
                        else if( s.StartsWith( "No code found, aborting", StringComparison.OrdinalIgnoreCase ) )
                        {
                            var dpe = new DbgProviderException( s,
                                                                "NoCodeFound",
                                                                ErrorCategory.ReadError );
                            try { throw dpe; } catch( Exception ) { } // give it a stack.
                            m_pipeline.WriteError( dpe );
                            m_state = WfdState.DontKnow; // actually, we should be done now.
                            break;
                        }
                        else if( (s.Length > 0) && (s[ s.Length - 1 ] == ':') )
                        {
                            // Looks like a block ID...
                            m_state = WfdState.ExpectingBlockId;
                            AddLine( s );
                        }
                        else if( String.IsNullOrEmpty( s ) )
                        {
                            m_state = WfdState.ExpectingBlockId;
                            m_currentBlockId = null;
                        }
                        else
                        {
                            m_state = WfdState.ExpectingInstructionOrBlankLine;
                            AddLine( s );
                        }
                        break;
                    default:
                        Util.Fail( "Invalid parse state." );
                        throw new Exception( "Invalid parse state." );
                }
            } // end AddLine()
        } // end class WholeFunctionDisasmParser
    } // end class ReadDbgDisassemblyCommand
}
