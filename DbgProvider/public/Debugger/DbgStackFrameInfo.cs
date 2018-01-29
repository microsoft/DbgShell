using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;
using DbgEngWrapper;

namespace MS.Dbg
{
    [NsContainer( NameProperty = "ShortCleanName" )]
    public class DbgStackFrameInfo : DebuggerObject, ICanSetContext, ISupportColor
    {
        public DEBUG_STACK_FRAME_EX NativeFrameEx { get; private set; }

        private bool m_alreadySearchedManagedFrames;
        private ClrStackFrame m_managedFrame;
        public ClrStackFrame ManagedFrame
        {
            get
            {
                if( !m_alreadySearchedManagedFrames )
                {
                    m_managedFrame = _TryFindManagedFrame();
                    m_alreadySearchedManagedFrames = true;
                }
                return m_managedFrame;
            }
        }


        public DbgUModeThreadInfo Thread { get; private set; }

        private DbgEngContext __context;
        public DbgEngContext Context
        {
            get
            {
                if( null == __context )
                {
                    __context = new DbgEngContext( Thread.Context.SystemIndex,
                                                   Thread.Context.ProcessIndexOrAddress,
                                                   Thread.Context.ThreadIndexOrAddress,
                                                   FrameNumber );
                }
                return __context;
            }
        } // end property Context


        public string Path
        {
            get
            {
                return DbgProvider.GetPathForDebuggerContext( Context );
            }
        }


        internal DbgStackFrameInfo( DbgEngDebugger debugger,
                                    DbgUModeThreadInfo thread,
                                    DEBUG_STACK_FRAME_EX nativeFrame )
            : this( debugger, thread, nativeFrame, null, false )
        {
        }

        internal DbgStackFrameInfo( DbgEngDebugger debugger,
                                    DbgUModeThreadInfo thread,
                                    DEBUG_STACK_FRAME_EX nativeFrame,
                                    ClrStackFrame managedFrame )
            : this( debugger, thread, nativeFrame, managedFrame, true )
        {
        }

        internal DbgStackFrameInfo( DbgEngDebugger debugger,
                                    DbgUModeThreadInfo thread,
                                    DEBUG_STACK_FRAME_EX nativeFrame,
                                    ClrStackFrame managedFrame,
                                    bool alreadySearchManagedFrames )
            : base( debugger )
        {
            if( null == thread )
                throw new ArgumentNullException( "thread" );

            NativeFrameEx = nativeFrame;
            Thread = thread;
            m_managedFrame = managedFrame;
            m_alreadySearchedManagedFrames = alreadySearchManagedFrames;
        } // end constructor()


        public uint FrameNumber { get { return NativeFrameEx.FrameNumber; } }

        [NsLeafItem]
        public ulong InstructionPointer { get { return NativeFrameEx.InstructionOffset; } }

        [NsLeafItem]
        public ulong ReturnAddress { get { return NativeFrameEx.ReturnOffset; } }

        [NsLeafItem]
        public ulong StackPointer { get { return NativeFrameEx.StackOffset; } }


        [NsLeafItem]
        public bool IsFrameInline
        {
            get
            {
                return DbgEngDebugger.IsFrameInline( NativeFrameEx.InlineFrameContext );
            }
        }


        private ulong m_displacement;


        [NsLeafItem]
        public string SymbolName
        {
            get
            {
                if( null != Function )
                {
                    return Function.ModuleQualifiedName;
                }

                return DbgProvider.FormatAddress( InstructionPointer,
                                                  Debugger.TargetIs32Bit,
                                                  useTick: true );
            }
        } // end property SymbolName


        DbgModuleInfo __module;
        [NsLeafItem]
        public DbgModuleInfo Module
        {
            get
            {
                if( null == __module )
                {
                    using( new DbgEngContextSaver( Debugger, Context ) )
                    {
                        // TODO: BUGBUG: This doesn't work for managed modules.
                        try
                        {
                            if( 0 == InstructionPointer )
                            {
                                LogManager.Trace( "DbgStackFrameInfo.Module: can't get module for an InstructionPointer of 0." );
                            }
                            else
                            {
                                __module = Debugger.GetModuleByAddress( InstructionPointer );
                            }
                        }
                        catch( DbgProviderException dpe )
                        {
                            LogManager.Trace( "IP not in any module (probably managed or otherwise JITted): 0x{0:x} ({1})",
                                               InstructionPointer,
                                               Util.GetExceptionMessages( dpe ) );

                            // For now let's lean on dbgeng's managed support.
                            try
                            {
                                ulong dontCare;
                                string fullname = GetNameByInlineContext( InstructionPointer,
                                                                          NativeFrameEx.InlineFrameContext,
                                                                          out dontCare );
                                string modName, funcName;
                                DbgProvider.ParseSymbolName( fullname,
                                                             out modName,
                                                             out funcName,
                                                             out dontCare );
                                __module = Debugger.GetModuleByName( modName ).FirstOrDefault();
                            }
                            catch( DbgProviderException dpe2 )
                            {
                                LogManager.Trace( "Still couldn't get module: {0}", Util.GetExceptionMessages( dpe2 ) );
                            } // end workaround for lack of managed support
                        } // end catch( dpe )
                    } // end using( ctxSaver )
                } // end if( null == __module )
                return __module;
            }
        }


        private DbgFunction m_function;

        /// <summary>
        ///    Gets the function that the frame represents, or null if the function is not
        ///    known (such as when you don't have proper symbols).
        /// </summary>
        [NsLeafItem]
        public DbgFunction Function
        {
            get
            {
                if( null == m_function )
                {
                    // Don't go searching for a managed frame first, but if we already
                    // have it, let's use it.
                    if( null != m_managedFrame )
                    {
                        // ClrStackFrames can have null "methods" for marker frames, but
                        // we should have screened those out; this should be a real frame.
                        Util.Assert( null != ManagedFrame.Method );
                        m_function = new DbgManagedFunction( Debugger,
                                                             Context,
                                                             ManagedFrame.Method );
                        m_displacement = NativeFrameEx.InstructionOffset - m_function.Address;
                    }
                    else
                    {
                        // Let's just always try native first, since it is likely.
                        bool itWorked = DbgNativeFunction.TryCreateFunction( Debugger,
                                                                             Context,
                                                                             NativeFrameEx,
                                                                             out m_function,
                                                                             out m_displacement );

                        if( itWorked )
                        {
                            // Don't bother looking for a managed frame if someone
                            // accesses the ManagedFrame property.
                            m_alreadySearchedManagedFrames = true;
                        }
                        else
                        {
                            // Accessing via this property will cause us to go search for
                            // a managed frame if we haven't already tried that.
                            if( null != ManagedFrame )
                            {
                                // ClrStackFrames can have null "methods" for marker
                                // frames, but we should have screened those out; this
                                // should be a real frame.
                                Util.Assert( null != ManagedFrame.Method );
                                m_function = new DbgManagedFunction( Debugger,
                                                                     Context,
                                                                     ManagedFrame.Method );
                                m_displacement = NativeFrameEx.InstructionOffset - m_function.Address;
                            }
                            else
                            {
                                // There are probably legitimate cases like javascript stacks that
                                // I don't know how to handle yet, in which case using a
                                // DbgUnknownFunction is appropriate. This assert is just to see
                                // if I've got any places where I /should/ be able to find the
                                // function but I've just bungled something.
                            //  Util.Assert( itWorked );

                                if( !itWorked )
                                {
                                    m_function = DbgUnknownFunction.Create( Debugger, Context, NativeFrameEx );
                                    m_displacement = NativeFrameEx.InstructionOffset - m_function.Address;
                                }
                            } // end else( not a managed frame for sure )
                        } // end else( not a native frame )
                    } // end else( don't have managed frame in-hand already )
                } // end if( !m_function )
                return m_function;
            } // end get
        } // end property Function


        private ClrStackFrame _TryFindManagedFrame()
        {
            foreach( var mThread in Thread.ManagedThreads )
            {
                foreach( var mFrame in mThread.StackTrace )
                {
                    if( 0 == mFrame.InstructionPointer )
                        continue; // some sort of "helper" or GC frame or something

                    if( mFrame.InstructionPointer == NativeFrameEx.InstructionOffset )
                    {
                        return mFrame;
                    }

                    // TODO: Do the managed stack frame pointers match up with the native frame pointers???
                    if( mFrame.StackPointer > NativeFrameEx.StackOffset )
                    {
                        break; // didn't find it in this thread; don't need to keep looking on this thread
                    }
                } // end foreach( mFrame in mThread )
            } // end foreach( mThread )

            return null;
        }

        [NsLeafItem]
        public ulong Displacement
        {
            get
            {
                return m_displacement;
            }
        } // end property Displacement


        public override string ToString()
        {
            if( null == Function )
            {
                // Let's see if dbgeng can do any better.
                try
                {
                    Util.Fail( "We don't have a Function." );
                    ulong displacement;
                    string fullname = GetNameByInlineContext( InstructionPointer,
                                                              NativeFrameEx.InlineFrameContext,
                                                              out displacement );
                    string modName, funcName;
                    ulong dontCare;
                    DbgProvider.ParseSymbolName( fullname,
                                                 out modName,
                                                 out funcName,
                                                 out dontCare );

                    if( displacement == 0 )
                        return fullname;
                    else
                        return Util.Sprintf( "{0}+0x{1:x}", fullname, displacement );
                }
                catch( DbgProviderException dpe )
                {
                    LogManager.Trace( "Still couldn't get symbolic name: {0}", Util.GetExceptionMessages( dpe ) );
                }
            } // end if( !Function )

            try
            {
                if( Displacement == 0 )
                    return SymbolName;
                else
                    return Util.Sprintf( "{0}+0x{1:x}", SymbolName, Displacement );
            }
            catch( DbgProviderException dpe )
            {
                LogManager.Trace( "Could not get symbol name: {0}", Util.GetExceptionMessages( dpe ) );
            }

            return DbgProvider.FormatAddress( InstructionPointer,
                                              Debugger.TargetIs32Bit,
                                              useTick: true );
        } // end ToString()


        public ColorString ToColorString()
        {
            ColorString cs;

            if( 0 == InstructionPointer )
            {
                cs = new ColorString( ConsoleColor.Red, "0" )
                    .Append( "   " )
                    .AppendPushFg( ConsoleColor.Yellow )
                    .Append( "WARNING: Frame IP not in any known module. Following frames may be wrong." )
                    .AppendPop();
            }
            else
            {
                cs = DbgProvider.ColorizeSymbol( ToString() );
                if( (null != Module) && (Module.SymbolType == DEBUG_SYMTYPE.EXPORT) )
                {
                    // If we only have export symbols, let's give a visual clue:
                    cs = new ColorString().AppendPushBg( ConsoleColor.DarkRed ).Append( cs ).AppendPop();
                }
            }
            return cs;
        } // end ToColorString()


        private static string _StripParams( string s )
        {
            int idx = s.IndexOf( '(' );
            if( idx > 0 )
            {
                s = s.Substring( 0, idx );
            }
            return s;
        } // end _StripParams()


        private string m_shortCleanName;

        /// <summary>
        ///    Suitable for use as a container name in PowerShell, modulo name collisions.
        /// </summary>
        public string ShortCleanName
        {
            get
            {
                if( null == m_shortCleanName )
                {
                    m_shortCleanName = _StripParams( SymbolName );
                    m_shortCleanName = DbgProvider.SanitizeNamespaceName( m_shortCleanName );
                    if( m_displacement != 0 )
                        m_shortCleanName = Util.Sprintf( "{0}+0x{1:x}", m_shortCleanName, Displacement );

                    m_shortCleanName = Util.Sprintf( "{0:x} {1}",
                                                     FrameNumber,
                                                     m_shortCleanName );
                }
                return m_shortCleanName;
            }
        } // end property ShortCleanName


        private DbgRegisterSetBase m_regSet;

        [NsLeafItem]
        public DbgRegisterSetBase RegisterSet
        {
            get
            {
                if( null == m_regSet )
                {
                    m_regSet = DbgRegisterSetBase.GetRegisterSet( Debugger, this );
                }
                return m_regSet;
            }
        } // end property RegisterSet


        private DbgSymbolGroup m_dsg;

        [NsLeafItem]
        public IList< DbgLocalSymbol > Locals
        {
            get
            {
                if( null == m_dsg )
                {
                    m_dsg = new DbgSymbolGroup( Debugger, DEBUG_SCOPE_GROUP.ALL, this, Context );
                }
                return m_dsg.Items;
            }
        } // end property Locals

        public DbgStackFrameInfo Caller
        {
            get
            {
                uint callerIdx = FrameNumber + 1;
                if( callerIdx >= Thread.Stack.Frames.Count )
                    return null;
                else
                    return Thread.Stack.Frames[ (int) callerIdx ];
            }
        }

        public DbgStackFrameInfo Callee
        {
            get
            {
                if( 0 == FrameNumber )
                    return null;

                uint calleeIdx = FrameNumber - 1;
                return Thread.Stack.Frames[ (int) calleeIdx ];
            }
        }

        // This is awkward. But I don't want the interface or the method to be public.
        internal void SetContext()
        {
            ((ICanSetContext) this).SetContext();
        }

        void ICanSetContext.SetContext()
        {
            Debugger.SetCurrentDbgEngContext( Context );
        }
    } // end class DbgStackFrameInfo
}
