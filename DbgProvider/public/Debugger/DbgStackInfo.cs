using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;
using DbgEngWrapper;

namespace MS.Dbg
{
    public class DbgStackInfo : DebuggerObject, ISupportColor
    {
        private DbgUModeThreadInfo m_thread;
        private DbgStackFrameInfo m_contextFrame;

        /// <summary>
        ///    0 means "all the frames".
        /// </summary>
        public readonly int MaxFrameCount;


        public DbgEngContext Context
        {
            get
            {
                if( null != m_contextFrame )
                    return m_contextFrame.Context;
                else
                    return m_thread.Context;
            }
        } // end property Context


        private static DbgUModeThreadInfo _ValidateThread( DbgUModeThreadInfo thread )
        {
            if( null == thread )
                throw new ArgumentNullException( "thread" );

            return thread;
        } // end _ValidateThread()


        internal DbgStackInfo( DbgUModeThreadInfo thread )
            : this( thread, 0 )
        {
        }

        internal DbgStackInfo( DbgUModeThreadInfo thread, int maxFrames )
            : base( _ValidateThread( thread ).Debugger )
        {
            m_thread = thread;
            MaxFrameCount = maxFrames;
        } // end constructor

        internal DbgStackInfo( DbgStackFrameInfo contextFrame, int maxFrameCount )
            : this( contextFrame.Thread ) // if ever made public we'd want to validate contextFrame not null
        {
            m_contextFrame = contextFrame;
            MaxFrameCount = maxFrameCount;
        } // end constructor


        internal DbgStackInfo WithMaxFrameCount( int maxFrames )
        {
            if( maxFrames == MaxFrameCount )
                return this;

            if( null != m_contextFrame )
                return new DbgStackInfo( m_contextFrame, maxFrames );

            return new DbgStackInfo( m_thread, maxFrames );

        }


        private List< DbgStackFrameInfo > m_frames;

        public IList< DbgStackFrameInfo > Frames
        {
            get
            {
                if( null == m_frames )
                    EnumerateStackFrames();

                return m_frames.AsReadOnly();
            }
        } // end property Frames


        public IEnumerable< DbgStackFrameInfo > EnumerateStackFrames()
        {
            if( null != m_frames )
            {
                return m_frames;
            }

            m_frames = new List< DbgStackFrameInfo >();

            bool noException = false;
            try
            {
                return Debugger.ExecuteOnDbgEngThread( () =>
                    {
                        using( new DbgEngContextSaver( Debugger, Context ) )
                        {
                            WDebugControl dc = (WDebugControl) Debugger.DebuggerInterface;

                            /* TODO: I guess frameOffset doesn't do what I think it does... or maybe I need to file a bug.
                            //int stride = 64;
                            int stride = 3;
                            ulong frameOffset = 0;
                            int hr = 0;
                            DEBUG_STACK_FRAME_EX[] frames = new DEBUG_STACK_FRAME_EX[ stride ];
                            while( true )
                            {
                                uint framesFilled = 0;
                                hr = dc.GetStackTraceEx( frameOffset, 0, 0, frames, frames.Length, out framesFilled );

                                CheckHr( hr );

                                if( 0 == framesFilled )
                                    break;

                                for( int i = 0; i < framesFilled; i++ )
                                {
                                    var newFrame = new StackFrameInfo( Debugger, frames[ i ] );
                                    m_frames.Add( newFrame );
                                    //yield return newFrame; hmm, can't mix an iterator with the straight 'return' above
                                }

                                frameOffset += 3;
                            } // while( keep requesting more frames )
                            */

                            DEBUG_STACK_FRAME_EX[] frames;
                            int hr = dc.GetStackTraceEx( 0, // frameOffset
                                                         0, // stackOffset
                                                         0, // instructionOffset
                                                         MaxFrameCount,
                                                         out frames );

                            CheckHr( hr );

                            int[] managedFrameIndices;

                            try
                            {
                                managedFrameIndices = new int[ Thread.ManagedThreads.Count ];
                            }
                            catch( InvalidOperationException ioe )
                            {
                                //
                                // Likely "Mismatched architecture between this process
                                // and the dac." No managed code information for you, sir.
                                //

                                DbgProvider.RequestExecuteBeforeNextPrompt(
                                        Util.Sprintf( "Write-Warning 'Could not get managed thread/frame information: {0}'",
                                                      System.Management.Automation.Language.CodeGeneration.EscapeSingleQuotedStringContent( Util.GetExceptionMessages( ioe ) ) ) );
                                managedFrameIndices = new int[ 0 ];

                                // (and remember not to keep trying to get managed info)
                                Debugger.ClrMdDisabled = true;
                            }

                            foreach( var nativeFrame in frames )
                            {
                                ClrStackFrame managedFrame = null;
                                for( int i = 0; i < managedFrameIndices.Length; i++ )
                                {
                                    var mThread = Thread.ManagedThreads[ i ];
                                    int mFrameIdx = managedFrameIndices[ i ];
                                    // It's possible that a thread is marked as a managed
                                    // thread, but has no stack frames. (I've seen this,
                                    // for instance, at the final breakpoint of a managed
                                    // app.)
                                    if( 0 == mThread.StackTrace.Count )
                                        continue;

                                    if( mFrameIdx >= mThread.StackTrace.Count )
                                    {
                                        // We've exhausted the managed frames for this
                                        // particular managed thread.
                                        continue;
                                    }

                                    var mFrame = mThread.StackTrace[ mFrameIdx ];
                                    while( (0 == mFrame.InstructionPointer) &&
                                           (mFrameIdx < (mThread.StackTrace.Count - 1)) ) // still at least one more frame below?
                                    {
                                        // It's some sort of "helper" or GC frame or
                                        // something, which we need to skip.
                                        mFrameIdx++;
                                        managedFrameIndices[ i ] = mFrameIdx;
                                        mFrame = mThread.StackTrace[ mFrameIdx ];
                                    }

                                    if( nativeFrame.InstructionOffset == mFrame.InstructionPointer )
                                    {
                                        managedFrame = mFrame;
                                        managedFrameIndices[ i ] += 1;
                                        break;
                                    }
                                }

                                var newFrame = new DbgStackFrameInfo( Debugger,
                                                                      m_thread,
                                                                      nativeFrame,
                                                                      managedFrame );
                                m_frames.Add( newFrame );
                            }

                            noException = true;
                            return m_frames;
                        } // end using( context saver )
                    } );
            }
            finally
            {
                if( !noException )
                    m_frames = null; // so we can try again (handy for debugging)
            }
        } // end EnumerateStackFrames()


        private DbgStackFrameInfo m_topFrame;

        public DbgStackFrameInfo GetTopFrame()
        {
            if( null != m_frames )
            {
                return m_frames[ 0 ];
            }

            if( null == m_topFrame )
            {
                m_topFrame = Debugger.ExecuteOnDbgEngThread( () =>
                {
                    using( new DbgEngContextSaver( Debugger, Context ) )
                    {
                        WDebugControl dc = (WDebugControl) Debugger.DebuggerInterface;

                        DEBUG_STACK_FRAME_EX[] frames;
                        int hr = dc.GetStackTraceEx( 0, 0, 0, 1, out frames );

                        CheckHr( hr );
                        Util.Assert( 1 == frames.Length );

                        return new DbgStackFrameInfo( Debugger, Thread, frames[ 0 ] );
                    } // end using( context saver )
                } );
            }
            return m_topFrame;
        } // end GetTopFrame()


        public DbgUModeThreadInfo Thread { get { return m_thread; } }


        public int CompareFrames( DbgStackInfo other )
        {
            if( other == null )
            {
                throw new ArgumentNullException( nameof( other ) );
            }

            int countDiff = Frames.Count - other.Frames.Count;


            if( countDiff < 0 )
                return -1;
            else if( countDiff > 0 )
                return 1;

            for( int i = 0; i < Frames.Count; i++ )
            {
                long offsetDiff = ((long) Frames[ i ].InstructionPointer) -
                                  ((long) other.Frames[ i ].InstructionPointer);

                if( offsetDiff == 0 )
                    continue;
                else if( offsetDiff < 0 )
                    return -1;
                else
                    return 1;
            }

            return 0;
        } // end CompareFrames()


        private int m_framesHash;

        public int FramesHash
        {
            get
            {
                if( 0 == m_framesHash )
                {
                    // TODO: is this any good?

                    int hash = Frames.Count * 8427;

                    int i = 0;
                    foreach( var frame in Frames )
                    {
                        int hi = (int) (frame.InstructionPointer >> 32);
                        int lo = (int) (frame.InstructionPointer);

                        if( hi != 0 )
                            hash = hash ^ hi;

                        hash = (hash ^ lo) + i++;
                    }

                    m_framesHash = hash;
                }
                return m_framesHash;
            }
        } // end property FramesHash


        public class FramesComparer : IComparer< DbgStackInfo >,
                                      IEqualityComparer< DbgStackInfo >,
                                      IEqualityComparer
        {
            private FramesComparer() { }

            public static FramesComparer Instance => new FramesComparer();


            public int Compare( DbgStackInfo x, DbgStackInfo y )
            {
                if( x == null )
                {
                    if( y == null )
                        return 0;
                    else
                        return -1;
                }
                else if( y == null )
                {
                    return 1;
                }

                return x.CompareFrames( y );
            }

            public bool Equals( DbgStackInfo x, DbgStackInfo y )
            {
                return 0 == Compare( x, y );
            }

            public int GetHashCode( DbgStackInfo obj )
            {
                if( null == obj )
                    return 0;

                return obj.FramesHash;
            }

            bool IEqualityComparer.Equals( object x, object y )
            {
                if( (x == null) || (y == null) )
                    return Object.Equals( x, y );

                return Equals( x as DbgStackInfo, y as DbgStackInfo );
            }

            int IEqualityComparer.GetHashCode( object obj )
            {
                DbgStackInfo dsi = obj as DbgStackInfo;

                if( null == dsi )
                    return 0;

                return GetHashCode( dsi );
            }
        } // end class FramesComparer


        // TODO: Support different options
        public override string ToString()
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                foreach( DbgStackFrameInfo frame in EnumerateStackFrames() )
                {
                    sb.AppendLine( frame.ToString() );
                }

                return sb.ToString();
            }
            catch( Exception e )
            {
                // If PS is directly calling ToString() and it blows up, PS will swallow
                // it. This is a giant pain to track down (i.e. you run "k" and get no
                // output--no errors, nothing in $error, no clue). Hopefully this assert
                // will help make debugging these problems a little easier.
                Util.Fail( Util.Sprintf( "Exception in ToString(): if this was called by PS, it will swallow it: {0}",
                                         Util.GetExceptionMessages( e ) ) );
                throw;
            }
        } // end ToString();


        public ColorString ToColorString()
        {
            ColorString cs = new ColorString();
            foreach( DbgStackFrameInfo frame in EnumerateStackFrames() )
            {
                cs.AppendLine( frame.ToColorString() );
            }
            return cs;
        } // end ToColorString()
    } // end class DbgStackInfo
}
