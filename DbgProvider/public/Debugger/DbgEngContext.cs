using System;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents a DbgEng.dll context.
    /// </summary>
    /// <remarks>
    ///    DbgEng.dll has a concept of "context": what is the current "system", the
    ///    current process within that system, the current thread within that process, and
    ///    the current frame within that thread. When you run a debugger command, it
    ///    operates against the current context (so you don't have to specify which
    ///    system, process, thread, and frame for every command). This class represents
    ///    that context as a single object to help simplify things (so you don't have to
    ///    keep track of the four possible values separately). It is represented textually
    ///    by a 4-tuple: [s, p, t, f] ([system, process, thread, frame]).
    ///
    ///    Some parts of a context may be unspecified, represented by the value
    ///    DEBUG_ANY_ID (-1). A context with one or more unspecified components could mean
    ///    different things depending on the context (haha). For instance, if I wanted to
    ///    specify the context of a particular frame relative to the current system,
    ///    process, and thread, I could use the context [-1,-1,-1, 7]. In this context,
    ///    the unspecified values mean "relative to the current context".
    ///
    ///    An unspecified part of a DbgEngContext might also mean that there /is/ no such
    ///    context. For instance, if I ask the debugger to give me the current context,
    ///    and it gives me [0,2,-1,-1], that means that the current context is system 0,
    ///    process 2, but that it has no current thread or frame context).
    ///
    ///    Note that it is valid for a context to represent a "system", but not any
    ///    particular process, thread, or frame ([0,-1,-1,-1]); and it is valid for a
    ///    context to represent "no context" ([-1,-1,-1,-1]).
    /// </remarks>
    public class DbgEngContext : IEquatable< DbgEngContext >, IComparable< DbgEngContext >
    {
        public const uint DEBUG_ANY_ID = unchecked( (uint) 0xffffffff );

        /// <summary>
        ///    Indicates which DbgEng.dll "system id" the context represents.
        /// </summary>
        /// <remarks>
        /// <para>
        ///    DbgEng.dll can be connected to multiple targets: dumps, live processes and
        ///    kernel targets. It groups these targets into "systems": each dump lives in
        ///    a separate "system", each kernel target lives in a separate "system", but
        ///    there can be multiple live processes in a single "system".
        /// </para>
        /// <para>
        ///    A value of 0xffffffff (DEBUG_ANY_ID) means the index is unspecified,
        ///    indicating the current system index, or /no/ system index (such as if the
        ///    debugger is not connected to any system).
        /// </para>
        /// </remarks>
        public readonly uint SystemIndex;

        /// <summary>
        ///    Indicates which DbgEng.dll "process id" the context represents.
        /// </summary>
        /// <remarks>
        /// <para>
        ///    Within a single "system", DbgEng.dll can be connected to multiple live
        ///    processes.  N.B. The dbgeng.dll process id is not the same as the OS
        ///    process id.
        /// </para>
        /// <para>
        ///    A value of 0xffffffff (DEBUG_ANY_ID) means the index is unspecified,
        ///    indicating the current process index, or /no/ process index (such as if the
        ///    debugger has no current process).
        /// </para>
        /// </remarks>
        public readonly ulong ProcessIndexOrAddress;

        /// <summary>
        ///    Indicates which DbgEng.dll "thread id" the context represents.
        /// </summary>
        /// <remarks>
        /// <para>
        ///    N.B. The dbgeng.dll thread id is not the same as the OS thread id.
        /// </para>
        /// <para>
        ///    A value of 0xffffffff (DEBUG_ANY_ID) means the index is unspecified,
        ///    indicating the current thread index, or /no/ thread index (such as if the
        ///    debugger has no current thread).
        /// </para>
        /// </remarks>
        public readonly ulong ThreadIndexOrAddress;

        /// <summary>
        ///    Indicates a particular frame in a thread's stack.
        /// </summary>
        /// <remarks>
        /// <para>
        ///    A value of 0xffffffff (DEBUG_ANY_ID) means the index is unspecified,
        ///    indicating the current frame index, or /no/ frame index.
        /// </para>
        /// </remarks>
        public readonly uint FrameIndex;


        public readonly bool? IsKernelContext;


        /// <summary>
        ///    Returns ProcessIndexOrAddress, ensuring that it is a ProcessIndex (and not
        ///    an address). (i.e. it must be specified, and it must be user-mode)
        /// </summary>
        public uint RequireUModeProcessIndex()
        {
            if( ProcessIndexOrAddress == DEBUG_ANY_ID )
            {
                throw new DbgProviderException( "Process index is required.",
                                                "ProcessIndexRequired",
                                                System.Management.Automation.ErrorCategory.InvalidData,
                                                this );
            }

            if( (IsKernelContext.HasValue && IsKernelContext.Value) ||
                ((ProcessIndexOrAddress & 0xffffffff00000000) != 0) )
            {
                throw new DbgProviderException( "Process index (user-mode) is required.",
                                                "ProcessIndexRequired_notUserMode",
                                                System.Management.Automation.ErrorCategory.InvalidData,
                                                this );
            }

            return (uint) ProcessIndexOrAddress;
        } // end RequireUModeProcessIndex()


        /// <summary>
        ///    Indicates the current system, process, thread, and frame; alternatively,
        ///    represents NO system, process, thread, or frame.
        /// </summary>
        public static readonly DbgEngContext CurrentOrNone = new DbgEngContext( DEBUG_ANY_ID,
                                                                                DEBUG_ANY_ID,
                                                                                DEBUG_ANY_ID,
                                                                                DEBUG_ANY_ID,
                                                                                null );


        /// <summary>
        ///    Creates a DbgEngContext that represents the current process, thread, and
        ///    frame in the specified system (unless the systemIndex is DEBUG_ANY_ID).
        /// </summary>
        public DbgEngContext( uint systemIndex )
            : this( systemIndex, DEBUG_ANY_ID, DEBUG_ANY_ID, DEBUG_ANY_ID, null )
        {
        }


        public DbgEngContext( uint systemIndex, bool isKernel )
            : this( systemIndex, DEBUG_ANY_ID, DEBUG_ANY_ID, DEBUG_ANY_ID, isKernel )
        {
        }


        /// <summary>
        ///    Creates a DbgEngContext that represents the current thread and frame in the
        ///    specified system and process (unless any of the specified indexes are
        ///    DEBUG_ANY_ID).
        /// </summary>
        public DbgEngContext( uint systemIndex,
                              uint processIndex )
            : this( systemIndex, processIndex, DEBUG_ANY_ID, DEBUG_ANY_ID )
        {
        }


        private static bool? _LooksLikeAddress( ulong idOrAddr )
        {
            if( idOrAddr == DEBUG_ANY_ID )
                return null;

            return 0 != (0xffffffff00000000 & idOrAddr);
        }

        private static bool? _EitherLooksLikeAddress( ulong idOrAddr1, ulong idOrAddr2 )
        {
            bool? b = _LooksLikeAddress( idOrAddr1 );
            if( null == b )
                b = _LooksLikeAddress( idOrAddr2 );

            return b;
        }

        public DbgEngContext( uint systemIndex,
                              ulong processIndexOrAddress )
            : this( systemIndex,
                    processIndexOrAddress,
                    DEBUG_ANY_ID,
                    DEBUG_ANY_ID,
                    _LooksLikeAddress( processIndexOrAddress ) )
        {
        }


        /// <summary>
        ///    Creates a DbgEngContext that represents the current frame in the specified
        ///    system, process, and thread (unless any of the specified indexes are
        ///    DEBUG_ANY_ID).
        /// </summary>
        public DbgEngContext( uint systemIndex,
                              uint processIndex,
                              uint threadIndex )
            : this( systemIndex, processIndex, threadIndex, DEBUG_ANY_ID )
        {
        }

        /// <summary>
        ///    Creates a fully-specified DbgEngContext that represents a particular
        ///    system, process, thread, and frame (unless any of the indexes are
        ///    DEBUG_ANY_ID).
        /// </summary>
        public DbgEngContext( uint systemIndex,
                              uint processIndex,
                              uint threadIndex,
                              uint frameIndex )
            : this( systemIndex,
                    processIndex,
                    threadIndex,
                    frameIndex,
                    false )
        {
        } // end constructor


        private DbgEngContext( uint systemIndex,
                               uint processIndex,
                               uint threadIndex,
                               uint frameIndex,
                               bool isKernelContext )
        {
            SystemIndex = systemIndex;
            ProcessIndexOrAddress = processIndex;
            ThreadIndexOrAddress = threadIndex;
            FrameIndex = frameIndex;
            IsKernelContext = isKernelContext;
        } // end constructor


        public DbgEngContext( uint systemIndex,
                              ulong processAddress,
                              ulong threadAddress,
                              uint frameIndex )
            : this( systemIndex,
                    processAddress,
                    threadAddress,
                    frameIndex,
                    _EitherLooksLikeAddress( processAddress, threadAddress ) )
        {
        }

        internal DbgEngContext( uint systemIndex,
                                ulong processIndexOrAddress,
                                ulong threadIndexOrAddress,
                                uint frameIndex,
                                bool? isKernelContext )
        {
            SystemIndex = systemIndex;
            ProcessIndexOrAddress = processIndexOrAddress;
            ThreadIndexOrAddress = threadIndexOrAddress;
            FrameIndex = frameIndex;
            IsKernelContext = isKernelContext;
        } // end constructor


        /// <summary>
        ///    Returns true if none of the indexes are unspecified (DEBUG_ANY_ID).
        /// </summary>
        public bool IsFullySpecified
        {
            get
            {
                bool isFullySpecified = (SystemIndex != DEBUG_ANY_ID) &&
                                        (ProcessIndexOrAddress != DEBUG_ANY_ID) &&
                                        (ThreadIndexOrAddress != DEBUG_ANY_ID) &&
                                        (FrameIndex != DEBUG_ANY_ID);

                if( isFullySpecified )
                {
                    Util.Assert( IsKernelContext != null );
                }

                return isFullySpecified;
            }
        } // end property IsFullySpecified

        /// <summary>
        ///    If any indexes that are specified (!= DEBUG_ANY_ID) come after an unspecified index
        ///    (DEBUG_ANY_ID), this returns true.
        /// </summary>
        /// <remarks>
        ///    Note that a non-fully-specified context (for example: {0, 0, DEBUG_ANY_ID, DEBUG_ANY_ID}) does
        ///    not count as relative: "relative" here means relative to the current
        ///    context, not relative to an absolute context (a context of {0, 0, DEBUG_ANY_ID, DEBUG_ANY_ID}
        ///    would be relative to the absolute context of system 0, process 0; whereas a
        ///    context of {0, DEBUG_ANY_ID, 0, 0} would be relative to whatever the current process
        ///    is in system 0).
        /// </remarks>
        public bool IsRelative
        {
            get
            {
                bool foundUnspecifiedIndex = false;
                if( SystemIndex == DEBUG_ANY_ID )
                    foundUnspecifiedIndex = true;

                if( ProcessIndexOrAddress == DEBUG_ANY_ID )
                    foundUnspecifiedIndex = true;
                else if( foundUnspecifiedIndex )
                    return true;

                if( ThreadIndexOrAddress == DEBUG_ANY_ID )
                    foundUnspecifiedIndex = true;
                else if( foundUnspecifiedIndex )
                    return true;

                if( (FrameIndex != DEBUG_ANY_ID) && foundUnspecifiedIndex )
                    return true;

                return false;
            }
        } // end property IsRelative


        #region IEquatable< DbgEngContext > Members

        public bool Equals( DbgEngContext other )
        {
            if( null == other )
                return false;

            return (SystemIndex == other.SystemIndex) &&
                   (ProcessIndexOrAddress == other.ProcessIndexOrAddress) &&
                   (ThreadIndexOrAddress == other.ThreadIndexOrAddress) &&
                   (FrameIndex == other.FrameIndex) &&
                   (IsKernelContext == other.IsKernelContext);
        } // end Equals( other )

        #endregion IEquatable< DbgEngContext >

        public override bool Equals( object obj )
        {
            return Equals( obj as DbgEngContext );
        }

        public override int GetHashCode()
        {
            // We'll spread the indexes out across the 32 bits of the result:
            //
            // User mode or not specified:
            //    SystemIndex:  5 bits
            //    ProcessIndex: 5 bits
            //    ThreadIndex:  11 bits
            //    FrameIndex:   11 bits
            //
            // Kernel mode:
            //    SystemIndex:  4 bits
            //    ProcessIndex: 11 bits  (taken from proc addr >> 8)
            //    ThreadIndex:  11 bits  (taken from thread addr >> 8)
            //    FrameIndex:   6 bits

            int hash;

            if( IsKernelContext.HasValue && IsKernelContext.Value )
            {
                hash = ((int) SystemIndex & 0x00f) << 28
                     + (((int) ProcessIndexOrAddress >> 8) & 0x6ff) << 17
                     + (((int) ThreadIndexOrAddress >> 8) & 0x6ff) << 6
                     + ((int) FrameIndex & 0x03f);
            }
            else
            {
                hash = ((int) SystemIndex & 0x01f) << 27
                     + ((int) ProcessIndexOrAddress & 0x01f) << 22
                     + ((int) ThreadIndexOrAddress & 0x6ff) << 11
                     + ((int) FrameIndex & 0x6ff);
            }

            return hash;
        } // end GetHashCode()

        public static bool operator ==( DbgEngContext dec1, DbgEngContext dec2 )
        {
            if( null == (object) dec1 )
                return (null == (object) dec2);

            return dec1.Equals( dec2 );
        }

        public static bool operator !=( DbgEngContext dec1, DbgEngContext dec2 )
        {
            return !(dec1 == dec2);
        }

        #region IComparable< DbgEngContext > Members

        public int CompareTo( DbgEngContext other )
        {
            int result = 0;

            result = SystemIndex.CompareTo( other.SystemIndex );
            if( 0 != result )
                return result;

            result = IsKernelContext.HasValue.CompareTo( other.IsKernelContext.HasValue );
            if( 0 != result )
                return result;

            if( IsKernelContext.HasValue )
            {
                result = IsKernelContext.Value.CompareTo( other.IsKernelContext.Value );
                if( 0 != result )
                    return result;
            }

            result = ProcessIndexOrAddress.CompareTo( other.ProcessIndexOrAddress );
            if( 0 != result )
                return result;

            result = ThreadIndexOrAddress.CompareTo( other.ThreadIndexOrAddress );
            if( 0 != result )
                return result;

            result = FrameIndex.CompareTo( other.FrameIndex );

            return result;
        } // end CompareTo( other )

        #endregion IComparable< DbgEngContext >

        bool TryCombine( DbgEngContext other, out DbgEngContext combined )
        {
            combined = null;
            uint sId, fId;
            ulong pId, tId;
            bool? iskc;

            if( SystemIndex == DEBUG_ANY_ID )
                sId = other.SystemIndex;
            else if( other.SystemIndex == DEBUG_ANY_ID )
                sId = SystemIndex;
            else if( SystemIndex == other.SystemIndex )
                sId = SystemIndex;
            else
                return false; // they conflict

            if( IsKernelContext == null )
                iskc = other.IsKernelContext;
            else if( other.IsKernelContext == null )
                iskc = IsKernelContext;
            else if( IsKernelContext.Value == other.IsKernelContext.Value )
                iskc = IsKernelContext;
            else
                return false; // they conflict

            if( ProcessIndexOrAddress == DEBUG_ANY_ID )
                pId = other.ProcessIndexOrAddress;
            else if( other.ProcessIndexOrAddress == DEBUG_ANY_ID )
                pId = ProcessIndexOrAddress;
            else if( ProcessIndexOrAddress == other.ProcessIndexOrAddress )
                pId = ProcessIndexOrAddress;
            else
                return false; // they conflict

            if( ThreadIndexOrAddress == DEBUG_ANY_ID )
                tId = other.ThreadIndexOrAddress;
            else if( other.ThreadIndexOrAddress == DEBUG_ANY_ID )
                tId = ThreadIndexOrAddress;
            else if( ThreadIndexOrAddress == other.ThreadIndexOrAddress )
                tId = ThreadIndexOrAddress;
            else
                return false; // they conflict

            if( FrameIndex == DEBUG_ANY_ID )
                fId = other.FrameIndex;
            else if( other.FrameIndex == DEBUG_ANY_ID )
                fId = FrameIndex;
            else if( FrameIndex == other.FrameIndex )
                fId = FrameIndex;
            else
                return false; // they conflict

            if( (sId == SystemIndex) &&
                (pId == ProcessIndexOrAddress) &&
                (tId == ThreadIndexOrAddress) &&
                (fId == FrameIndex) &&
                (iskc == IsKernelContext) )
            {
                // They were the same.
                combined = this;
            }
            else
            {
                // They were complementary
                combined = new DbgEngContext( sId, pId, tId, fId, iskc );
            }

            return true;
        } // end TryCombine()


        public bool IsStrictSuperSetOf( DbgEngContext other )
        {
            bool foundDifference = false;

            if( SystemIndex != other.SystemIndex )
            {
                if( DEBUG_ANY_ID == SystemIndex )
                    return false; // 'other' has a component we don't have
                else if( DEBUG_ANY_ID != other.SystemIndex )
                    return false; // they conflict

                foundDifference = true;
            }

            if( IsKernelContext != other.IsKernelContext )
            {
                if( null == IsKernelContext )
                    return false; // 'other' has a component we don't have
                else if( null != other.IsKernelContext )
                    return false; // they conflict

                foundDifference = true;
            }

            if( ProcessIndexOrAddress != other.ProcessIndexOrAddress )
            {
                if( DEBUG_ANY_ID == ProcessIndexOrAddress )
                    return false; // 'other' has a component we don't have
                else if( DEBUG_ANY_ID != other.ProcessIndexOrAddress )
                    return false; // they conflict

                foundDifference = true;
            }

            if( ThreadIndexOrAddress != other.ThreadIndexOrAddress )
            {
                if( DEBUG_ANY_ID == ThreadIndexOrAddress )
                    return false; // 'other' has a component we don't have
                else if( DEBUG_ANY_ID != other.ThreadIndexOrAddress )
                    return false; // they conflict

                foundDifference = true;
            }

            if( FrameIndex != other.FrameIndex )
            {
                if( DEBUG_ANY_ID == FrameIndex )
                    return false; // 'other' has a component we don't have
                else if( DEBUG_ANY_ID != other.FrameIndex )
                    return false; // they conflict

                foundDifference = true;
            }

            return foundDifference;
        } // end IsStrictSuperSetOf()


        public override string ToString()
        {
            var sb = new System.Text.StringBuilder( 50 );
            sb.Append( "DbgEngContext: [" );
            if( DEBUG_ANY_ID == SystemIndex )
                sb.Append( "-1," );
            else
                sb.Append( SystemIndex.ToString( "x" ).PadLeft( 2 ) ).Append( ',' );

            if( DEBUG_ANY_ID == ProcessIndexOrAddress )
                sb.Append( "-1," );
            else
                sb.Append( ProcessIndexOrAddress.ToString( "x" ).PadLeft( 2 ) ).Append( ',' );

            if( DEBUG_ANY_ID == ThreadIndexOrAddress )
                sb.Append( " -1," );
            else
                sb.Append( ThreadIndexOrAddress.ToString( "x" ).PadLeft( 3 ) ).Append( ',' );

            if( DEBUG_ANY_ID == FrameIndex )
                sb.Append( " -1" );
            else
                sb.Append( FrameIndex.ToString( "x" ).PadLeft( 3 ) );

            if( IsKernelContext.HasValue )
            {
                if( IsKernelContext.Value )
                    sb.Append( " (km)]" );
                else
                    sb.Append( " (um)]" );
            }
            else
            {
                sb.Append( " (--)]" );
            }

            return sb.ToString();
        } // end ToString()


        /// <summary>
        ///    Returns a [potentially new] context representing just the system and
        ///    process portions of the context. If the context does not represent a
        ///    process, this will throw an InvalidOperationException. Use
        ///    TryAsProcessContext for a non-throwing alternative.
        ///
        ///    Consider whether this is actually the right method, or if you actually want
        ///    AsTargetContext.
        /// </summary>
        public DbgEngContext AsProcessContext()
        {
            DbgEngContext ctx;
            if( !TryAsProcessContext( out ctx ) )
                throw new InvalidOperationException( "The context is not specific enough to represent a process." );

            return ctx;
        } // end AsProcessContext()


        /// <summary>
        ///    Attempts to convert this to a context representing just the system and
        ///    process portions of the context. If the context does not represent a
        ///    process, this will return false.
        ///
        ///    Consider whether this is actually the right method, or if you actually want
        ///    TryAsTargetContext.
        /// </summary>
        public bool TryAsProcessContext( out DbgEngContext asProcessContext )
        {
            asProcessContext = null;

            if( (DEBUG_ANY_ID == SystemIndex) ||
                (DEBUG_ANY_ID == ProcessIndexOrAddress) )
            {
                return false;
            }

            if( (DEBUG_ANY_ID == ThreadIndexOrAddress) &&
                (DEBUG_ANY_ID == FrameIndex) )
            {
                asProcessContext = this;
                return true;
            }

            asProcessContext = new DbgEngContext( SystemIndex, ProcessIndexOrAddress );
            return true;
        } // end TryAsProcessContext()


        /// <summary>
        ///    Returns a [potentially new] context representing just the system
        ///    portion of the context.
        /// </summary>
        public DbgEngContext AsSystemContext()
        {
            DbgEngContext ctx;
            if( !TryAsSystemContext( out ctx ) )
                throw new InvalidOperationException( "The context is not specific enough to represent a system." );

            return ctx;
        } // end AsSystemContext()


        /// <summary>
        ///    Attempts to convert this to a context representing just the system
        ///    portion of the context. If the context does not represent a
        ///    system, this will return false.
        /// </summary>
        public bool TryAsSystemContext( out DbgEngContext asSystemContext )
        {
            asSystemContext = null;

            if( DEBUG_ANY_ID == SystemIndex )
            {
                return false;
            }

            if( (DEBUG_ANY_ID == ProcessIndexOrAddress) &&
                (DEBUG_ANY_ID == ThreadIndexOrAddress) &&
                (DEBUG_ANY_ID == FrameIndex) )
            {
                asSystemContext = this;
                return true;
            }

            asSystemContext = new DbgEngContext( SystemIndex, DEBUG_ANY_ID, DEBUG_ANY_ID, DEBUG_ANY_ID, IsKernelContext );
            return true;
        } // end TryAsSystemContext()


        /// <summary>
        ///    Returns a [potentially new] context representing either just the system and
        ///    process portions of the context (for a user-mode context), or just the
        ///    system portion (for kernel mode). If the context does not represent a
        ///    target, this will throw an InvalidOperationException. Use
        ///    TryAsTargetContext for a non-throwing alternative.
        /// </summary>
        public DbgEngContext AsTargetContext()
        {
            DbgEngContext ctx;
            if( !TryAsTargetContext( out ctx ) )
                throw new InvalidOperationException( "The context is not specific enough to represent a target." );

            return ctx;
        } // end AsTargetContext()


        /// <summary>
        ///    Attempts to convert this to a context representing either just the system
        ///    and process portions of the context (for a user-mode context), or just the
        ///    system portion (for kernel mode). If the context does not represent a
        ///    target, this will return false.
        /// </summary>
        public bool TryAsTargetContext( out DbgEngContext asTargetContext )
        {
            asTargetContext = null;

            if( IsKernelContext.HasValue && IsKernelContext.Value )
            {
                return TryAsSystemContext( out asTargetContext );
            }
            else
            {
                return TryAsProcessContext( out asTargetContext );
            }
        } // end TryAsTargetContext()


        public DbgEngContext WithoutFrameIndex()
        {
            if( DEBUG_ANY_ID == FrameIndex )
            {
                return this;
            }

            return new DbgEngContext( SystemIndex,
                                      ProcessIndexOrAddress,
                                      ThreadIndexOrAddress,
                                      DEBUG_ANY_ID,
                                      IsKernelContext );
        } // end WithoutFrameIndex()


        public DbgEngContext WithoutThreadOrFrameIndex()
        {
            if( (DEBUG_ANY_ID == FrameIndex) &&
                (DEBUG_ANY_ID == ThreadIndexOrAddress) )
            {
                return this;
            }

            return new DbgEngContext( SystemIndex,
                                      ProcessIndexOrAddress,
                                      DEBUG_ANY_ID,
                                      DEBUG_ANY_ID,
                                      IsKernelContext );
        } // end WithoutThreadOrFrameIndex()
    } // end class DbgEngContext
}
