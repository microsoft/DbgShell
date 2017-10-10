//-----------------------------------------------------------------------
// <copyright file="Util.cs" company="Microsoft">
//    Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Serialization;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
//using System.Xml;
using Microsoft.Win32.SafeHandles;

using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace MS.Dbg
{
    internal static class Util
    {
        // OI: OrdinalIgnoreCase
        public static int Strcmp_OI( string s1, string s2 )
        {
            return StringComparer.OrdinalIgnoreCase.Compare( s1, s2 );
        }

        // II: InvariantCultureIgnoreCase
        public static int Strcmp_II( string s1, string s2 )
        {
            return StringComparer.InvariantCultureIgnoreCase.Compare( s1, s2 );
        }

        // Uses the InvariantCulture.
        public static string Sprintf( string fmt, params object[] inserts )
        {
            return MulticulturalString.Format( CultureInfo.InvariantCulture, fmt, inserts ).ToCulture( CultureInfo.InvariantCulture );
        }

        // Uses the InvariantCulture.
        public static string Sprintf( MulticulturalString mfmt, params object[] inserts )
        {
            return MulticulturalString.Format( CultureInfo.InvariantCulture, mfmt, inserts ).ToCulture( CultureInfo.InvariantCulture );
        }

        // Uses the _c_urrent [UI] _c_ulture.
        public static string CcSprintf( string fmt, params object[] inserts )
        {
            return String.Format( CultureInfo.CurrentCulture, fmt, MulticulturalString.InsertsToCulture( inserts, CultureInfo.CurrentUICulture ) );
        }

        // Uses the _c_urrent [UI] _c_ulture.
        public static string CcSprintf( MulticulturalString mfmt, params object[] inserts )
        {
            return MulticulturalString.Format( CultureInfo.CurrentCulture, mfmt, inserts ).ToCulture( CultureInfo.CurrentUICulture );
        }

        /*
        // Uses the _c_urrent [UI] _c_ulture.
        public static string CcSprintf( IFormatProvider fmtProvider, string fmt, params object[] inserts )
        {
            return String.Format( fmtProvider,
                                  fmt,
                                  MulticulturalString.InsertsToCulture( inserts, CultureInfo.CurrentUICulture ) );
        }

        // Uses the _c_urrent [UI] _c_ulture.
        public static string CcSprintf( IFormatProvider fmtProvider, MulticulturalString mfmt, params object[] inserts )
        {
            return MulticulturalString.Format( fmtProvider, mfmt, inserts ).ToCulture( CultureInfo.CurrentUICulture );
        }
        */

        // Composes a _M_ulti_c_ultural string.
        public static MulticulturalString McSprintf( MulticulturalString mfmt, params object[] inserts )
        {
            // FUTURE: Alter MulticulturalString so that you can defer both the language
            // and the format provider.
            return MulticulturalString.Format( CultureInfo.CurrentCulture, mfmt, inserts );
        }

        // Composes a _M_ulti_c_ultural string.
        public static MulticulturalString McSprintf( string fmt, params object[] inserts )
        {
            return MulticulturalString.Format( CultureInfo.CurrentCulture, fmt, inserts );
        }

        public static string PSCommandToString( PSCommand psCommand )
        {
            return String.Join< Command >( " | ", psCommand.Commands );
        }

        public static string Truncate( string s )
        {
            return Truncate( s, 32 );
        }

        public static string Truncate( string s, int maxLen )
        {
            if( maxLen <= 3 )
                throw new ArgumentOutOfRangeException( "maxLen" );

            if( s.Length <= maxLen )
                return s;

            return s.Substring( 0, maxLen - 3 ) + "...";
        }


        public static string FormatBytes( string s, int numBytes )
        {
            StringBuilder sb = new StringBuilder( 8 * numBytes );
            int idx = 0;
            int bytesDisplayed = 0;
            while( idx < s.Length )
            {
                char c = s[ idx ];
                idx++;

                short word = (short) c;
                byte lo = unchecked( (byte)  (word & 0x00ff));
                byte hi = unchecked( (byte) ((word & 0xff00) >> 8));

                if( 0 != idx )
                    sb.Append( " " );

                sb.Append( lo.ToString( "x2", CultureInfo.InvariantCulture ) );

                bytesDisplayed++;
                if( bytesDisplayed >= numBytes )
                    break;

                sb.Append( " " );
                sb.Append( hi.ToString( "x2", CultureInfo.InvariantCulture ) );

                bytesDisplayed++;
                if( bytesDisplayed >= numBytes )
                    break;
            }
            return sb.ToString();
        } // end FormatBytes()


        public static string FormatBytes( byte[] bytes )
        {
            StringBuilder sb = new StringBuilder( bytes.Length * 3 );
            bool prependSpace = false;
            foreach( byte b in bytes )
            {
                if( prependSpace )
                    sb.Append( ' ' );
                else
                    prependSpace = true;

                sb.AppendFormat( "{0:x2}", b );
            }
            return sb.ToString();
        } // end FormatBytes()


    //  private static PsTypesDcs sm_dcs = new PsTypesDcs();

    //  private static DataContractSerializer _CreateDcs( Type t )
    //  {
    //      return new DataContractSerializer( t,
    //                                         null,           // knownTypes
    //                                         0x1000000,      // maxItemsInObjectGraph
    //                                         true,           // ignoreExtensionDataObject
    //                                         true,           // preserveObjectReferences
    //                                         sm_dcs );       // dataContractSurrogate
    //  } // end _CreateDcs()


    //  public static string SerializeObject( object data, Type type )
    //  {
    //      DataContractSerializer dataContractSerializer = _CreateDcs( type );

    //      using( StringWriter stringWriter = new StringWriter( CultureInfo.InvariantCulture ) )
    //      {
    //          using( XmlWriter xmlWriter = new XmlTextWriter(stringWriter ) )
    //          {
    //              dataContractSerializer.WriteObject( xmlWriter, data );
    //              return stringWriter.GetStringBuilder().ToString();
    //          }
    //      }
    //  }

    //  /// <summary>
    //  ///    This method serializes an object to a string containing XML using
    //  ///    DataContractSerializer.
    //  /// </summary>
    //  public static string SerializeObject< T >( T data )
    //  {
    //      return SerializeObject( data, typeof( T ) );
    //  }

    //  /// <summary>
    //  ///    Reconstructs an object from an XML string using DataContractSerializer.
    //  /// </summary>
    //  public static T DeserializeObject< T >( String xmlValue )
    //  {
    //      if( String.IsNullOrEmpty( xmlValue ) )
    //      {
    //          return default( T );
    //      }

    //      T ret = default( T );

    //      DataContractSerializer dataContractSerializer = _CreateDcs( typeof( T ) );

    //      using( TextReader stringReader = new StringReader( xmlValue ) )
    //      {
    //          using( XmlReader xmlReader = new XmlTextReader( stringReader ) )
    //          {
    //              try
    //              {
    //                  ret = (T) dataContractSerializer.ReadObject( xmlReader );
    //              }
    //              catch( SerializationException se )
    //              {
    //                  LogManager.Trace( "Serialization error: {0}", Util.GetExceptionMessages( se ) );
    //                  LogManager.Trace( "Serialization failed on this text (truncated to 512 chars): {0}", Truncate( xmlValue, 515 ) );
    //                  // If there are embedded nulls, then the data after won't make it
    //                  // to the .etl log. Let's log some hex bytes too.
    //                  LogManager.Trace( "(first 16 [hex] bytes): {0}", FormatBytes( xmlValue, 16 ) );
    //                  throw;
    //              }
    //          } // end using()
    //      }// end using()

    //      return ret;
    //  }

        private static byte[] _DoCompression( byte[] input, CompressionMode mode )
        {
            using( MemoryStream inputStream = new MemoryStream( input ) )
            using( MemoryStream outputStream = new MemoryStream( input.Length * (mode == CompressionMode.Compress ? 1 : 5 ) ) )
            {
                if( CompressionMode.Compress == mode )
                {
                    // Compress
                    using( GZipStream gzStream = new GZipStream( outputStream, mode ) )
                    {
                        inputStream.CopyTo( gzStream );
                    }
                }
                else
                {
                    // Decompress
                    using( GZipStream gzStream = new GZipStream( inputStream, mode ) )
                    {
                        gzStream.CopyTo( outputStream );
                    }
                }
                return outputStream.ToArray();
            }
        } // end _DoCompression()


        public static byte[] Compress( byte[] input )
        {
            return _DoCompression( input, CompressionMode.Compress );
        } // end Compress()

        public static byte[] Decompress( byte[] input )
        {
            return _DoCompression( input, CompressionMode.Decompress );
        } // end Decompress()

        public static byte[] CompressString( string toCompress )
        {
            if( string.IsNullOrEmpty( toCompress ) )
                throw new ArgumentNullException( "toCompress" );

            byte[] bytes = new byte[ toCompress.Length * sizeof( char ) ];
            System.Buffer.BlockCopy( toCompress.ToCharArray(), 0, bytes, 0, bytes.Length );

            byte[] compressed = Compress( bytes );

#if DEBUG
            LogManager.Trace( "Size before compression: {0}; size after compression: {1}", bytes.Length, compressed.Length );
            LogManager.Trace( "Compression rate: {0:0.00} (compressed bytes) / (original bytes) ", (double) compressed.Length / (bytes.Length) );
#endif
            return compressed;
        } // end CompressString

        public static string DecompressToString( byte[] bytes )
        {
            if( bytes == null )
                throw new ArgumentNullException( "bytes" );

            byte[] deCompressedBytes = Decompress( bytes );

            char[] chars = new char[ deCompressedBytes.Length / sizeof( char ) ];
            System.Buffer.BlockCopy( deCompressedBytes, 0, chars, 0, deCompressedBytes.Length );

            return new string(chars);
        } // end DecompressToString


        /// <summary>
        ///    Returns a string that contains e.Message, plus e.InnerException.Message,
        ///    plus e.InnerException.InnerException.Message, etc.
        /// </summary>
        public static MulticulturalString GetExceptionMessages( Exception e )
        {
            return new MulticulturalString( (ci) =>
                {
                    StringBuilder sb = new StringBuilder( 256 );

                    // Need to use a local so that we don't affect the captured local
                    // (else we won't be able to call this closure twice).
                    Util.Assert( null != e );
                    Exception tmpException = e;
                    while( null != tmpException )
                    {
                        if( (tmpException is DbgProviderException) &&
                            (null != ((DbgProviderException) tmpException).McsMessage) )
                        {
                            // StringBuilder.AppendFormat will pass ci to the MCS's
                            // IFormattable ToString overload.
                            sb.AppendFormat( ci, "({0}) {1}", GetGenericTypeName( tmpException ), ((DbgProviderException) tmpException).McsMessage );
                        }
                        else
                        {
                            sb.AppendFormat( ci, "({0}) {1}", GetGenericTypeName( tmpException ), tmpException.Message );
                        }

                        tmpException = tmpException.InnerException;
                        if( null != tmpException )
                        {
                            sb.Append( " ==> " );
                        }
                    }
                    return sb.ToString();
                } );
        } // end GetExceptionMessages()


        public static string GetGenericTypeName<T>( T t ) where T : class
        {
            return GetGenericTypeName<T>( t, false );
        }


        public static string GetGenericTypeName<T>( T t, bool includeNamespace ) where T : class
        {
            if( null == t )
                return String.Empty;

            return GetGenericTypeName( t.GetType(), includeNamespace );
        } // end GetGenericTypeName<T>()


        public static string GetGenericTypeName( Type t )
        {
            return GetGenericTypeName( t, false );
        }


        public static string GetGenericTypeName( Type t, bool includeNamespace )
        {
            if( null == t )
                throw new ArgumentNullException( "t" );

            string name = includeNamespace ? t.FullName : t.Name;

            if( !t.IsGenericType )
            {
                return name;
            }

            StringBuilder sb = new StringBuilder( name.Length * 2 );
            int tickIndex = name.IndexOf( '`' );
            if( tickIndex < 0 )
            {
                // FUTURE: Actually, this is possible for nested types if the parent type
                // is the one with the generic parameter. It would be nice if we could
                // handle that and include the parent type.
                //Util.Fail( "I don't think this should be possible." );
                Util.Assert( t.IsNested );
                return name;
            }

            sb.Append( name.Substring( 0, tickIndex ) );
            sb.Append( '<' );
            Type[] genericArgs = t.GetGenericArguments();
            for( int i = 0; i < genericArgs.Length; i++ )
            {
                sb.Append( GetGenericTypeName( genericArgs[ i ], includeNamespace ) );
                if( i != genericArgs.Length - 1 )
                    sb.Append( ',' );
            }
            sb.Append( '>' );

            return sb.ToString();
        } // end GetGenericTypeName()


        public static string FormatErrorCode( int i )
        {
            return FormatErrorCode( unchecked( (uint) i ) );
        }

        public static string FormatErrorCode( uint ui )
        {
            if( ui > 0x7fffffff )
            {
                return "0x" + ui.ToString( "x", System.Globalization.CultureInfo.InvariantCulture );
            }
            else
            {
                return ui.ToString( System.Globalization.CultureInfo.InvariantCulture );
            }
        }

        /// <summary>
        ///    Workaround for the fact that Task.FromCancellation is not public.
        /// </summary>
        /// <remarks>
        ///    There is no direct (and public) way to create a task that is in the
        ///    canceled state. This workaround relies on the implementation of some other
        ///    Task method to call Task.FromCancellation for us.
        ///    (see INT337c517d)
        /// </remarks>
        public static Task TaskFromCancellation( CancellationToken cancelToken )
        {
            return Task.Delay( -1, cancelToken );
        }

        /// <summary>
        ///    Workaround for the fact that Task.FromCancellation is not public.
        /// </summary>
        /// <remarks>
        ///    There is no direct (and public) way to create a task that is in the
        ///    canceled state. This workaround relies on the implementation of some other
        ///    Task method to call Task.FromCancellation for us.
        ///    (see INT337c517d)
        /// </remarks>
        public static Task< TResult > TaskFromCancellation< TResult >( CancellationToken cancelToken )
        {
            return Task.Run< TResult >( () => { return default( TResult ); }, cancelToken );
        }

        // Using t.Wait() will throw an AggregateException, even if there are no child
        // tasks. But using "await" will propagate the original exception, at least for
        // tasks that do not have child tasks. (not sure about tasks that DO have child
        // tasks, but we are not using child tasks at all)
        public static void Await( Task t )
        {
            TaskAwaiter awaiter = t.GetAwaiter();
            object lockObject = new object();
            lock( lockObject )
            {
                if( !awaiter.IsCompleted )
                {
                    awaiter.OnCompleted( () => { lock( lockObject ) { Monitor.PulseAll( lockObject ); } } );
                    Monitor.Wait( lockObject );
                }
                awaiter.GetResult();
            }
        }

        public static TResult Await< TResult >( Task< TResult > t )
        {
            TaskAwaiter< TResult > awaiter = t.GetAwaiter();
            object lockObject = new object();
            lock( lockObject )
            {
                if( !awaiter.IsCompleted )
                {
                    awaiter.OnCompleted( () => { lock( lockObject ) { Monitor.PulseAll( lockObject ); } } );
                    Monitor.Wait( lockObject );
                }
                return awaiter.GetResult();
            }
        }


        /// <summary>
        ///    Awaits a task, but if the task faults, throws the task's
        ///    AggregateException, instead of the first Exception inside the tasks's
        ///    AggregateException (which is what would happen if you used plain "await").
        /// </summary>
        public static async Task AwaitThrowAggregate( Task t )
        {
            if( null == t )
                throw new ArgumentNullException( "t" );

            try
            {
                await t;
            }
            catch( Exception )
            {
                if( t.IsFaulted )
                {
                    ExceptionDispatchInfo.Capture( t.Exception ).Throw();
                }
                else
                {
                    throw; // something must be wrong with the task object.
                }
            }
        } // end AwaitThrowAggregate()


        /// <summary>
        ///    Like AggregateException.Flatten, but if there is only one inner exception,
        ///    it strips the containing AggregateException entirely.
        /// </summary>
        public static Exception SuperFlatten( Exception e )
        {
            if( null == e )
            {
                Util.Fail( "SuperFlatten( null )" );
                throw new ArgumentNullException( "e" );
            }

            AggregateException ae = e as AggregateException;
            if( null == ae )
                return e;

            ae.Flatten();
            if( 1 == ae.InnerExceptions.Count )
                return ae.InnerExceptions[ 0 ];

            return ae;
        } // end SuperFlatten()


        // This is to work around INTc3a7c600, wherein SemaphoreSlim.WaitAsync
        // might successfully acquire despite indicating cancellation.
        //
        // The downside to this workaround (besides the hassle and overhead) is that it is
        // not possible to associate a particular CancellationToken with cancellation.
        // (INT423ade0e)
        public static Task WaitAsync( SemaphoreSlim sem, CancellationToken cancelToken )
        {
            Task t = sem.WaitAsync();
            if( TaskStatus.RanToCompletion == t.Status )
                return t;

            var tcs = new TaskCompletionSource< bool >();
            CancellationTokenRegistration ctReg = cancelToken.Register(
                    () =>
                    {
                        if( tcs.TrySetCanceled() )
                        {
                            t.ContinueWith( (x) => sem.Release(),
                                            CancellationToken.None,
                                            TaskContinuationOptions.ExecuteSynchronously,
                                            TaskScheduler.Default );
                        }
                    } );

            t.ContinueWith(
                    (x) =>
                    {
                        ctReg.Dispose();
                        tcs.TrySetResult( true );
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default );

            return tcs.Task;
        } // end WaitAsync()


        /// <summary>
        ///    Similar to Parallel.ForEach, but async.
        /// </summary>
        /// <remarks>
        ///    This uses a non-greedy, semaphore-based approach to parallelize the body
        ///    tasks: a semaphore with a maxCount of <paramref name="concurrencyLevel"/>
        ///    is created, which is used to gate kickoff of <paramref name="body"/> tasks.
        ///    The body tasks are created in a simple 'for' loop, so tasks will only
        ///    execute in parallel as much as they are truly asynchronous--if the body
        ///    tasks are actually completely synchronous, then they will all be executed
        ///    in serial, on the calling thread. If they are asynchronous, then you will
        ///    be able to have up to 'concurrencyLevel' tasks outstanding at a time.
        /// </remarks>
        public static async Task< ConcurrentQueue< Exception > > ParallelForEachWorkerAsync< T >(
                IEnumerable< T > source,
                int concurrencyLevel,
                CancellationToken cancelToken,
                int errorLimit, // max # of errors to tolerate before stopping; -1 for "just keep going"
                Func< T, Task > body )
        {
            var exceptions = new ConcurrentQueue< Exception >();
            using( var sem = new SemaphoreSlim( concurrencyLevel ) )
            {
                try
                {
                    foreach( T item in source )
                    {
                        // INTc3a7c600: this might throw an OperationCanceledException,
                        // despite having successfully acquired a slot in the semaphore.
                        //await sem.WaitAsync( cancelToken );
                        await WaitAsync( sem, cancelToken );
                        if( (errorLimit >= 0) && (exceptions.Count > errorLimit) )
                        {
                            sem.Release();
                            break;
                        }

                        // Assigning the task to a local will suppress warning CS4014. We
                        // don't await this task because not awaiting is what gives us the
                        // parallelism that we want.
                        Task unused = body( item ).ContinueWith( (t) =>
                        {
                            if( t.IsFaulted )
                                exceptions.Enqueue( SuperFlatten( t.Exception ) );

                            sem.Release();
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default );
                    } // end foreach( item in source )
                }
                catch( OperationCanceledException )
                {
                    // This can only be from actual cancellation.
                 // Util.Assert( oce.CancellationToken == cancelToken ); <-- with the new WaitAsync workaround, there is no way to associate the cancelToken with the task. :/ INT423ade0e
                    // We are handling this exception for two reasons: 1) we want the
                    // failures to take precedence over cancellation (which matches the
                    // behavior of the partition-based approach), and 2) we need to wait
                    // for the cancellation to finish (by waiting for running tasks to
                    // drain).
                }

                // Wait for everything to be finished by waiting for every slot in the
                // semaphore to become available.
                for( int i = 0; i < concurrencyLevel; i++ )
                {
                    // We don't use the cancelToken here because even if we've canceled, we want
                    // to wait until the cancellation is done.
                    await sem.WaitAsync();
                }
            } // end using( sem )
            return exceptions;
        } // end ParallelForEachWorkerAsync< T >()


        public static Task ParallelForEachAsync< T >( IEnumerable< T > source,
                                                      int concurrencyLevel,
                                                      Func< T, Task > body )
        {
            return ParallelForEachAsync( source, concurrencyLevel, CancellationToken.None, body );
        }

        public static Task ParallelForEachAsync< T >( IEnumerable< T > source,
                                                      int concurrencyLevel,
                                                      CancellationToken cancelToken,
                                                      Func< T, Task > body )
        {
            return ParallelForEachAsync( source, concurrencyLevel, cancelToken, 0, body );
        }

        public static Task ParallelForEachAsync< T >( IEnumerable< T > source,
                                                      int concurrencyLevel,
                                                      CancellationToken cancelToken,
                                                      int errorLimit,
                                                      Func< T, Task > body )
        {
            var tcs = new TaskCompletionSource< bool >();
            Task< ConcurrentQueue< Exception > > worker = ParallelForEachWorkerAsync( source,
                                                                                      concurrencyLevel,
                                                                                      cancelToken,
                                                                                      errorLimit,
                                                                                      body );
            worker.ContinueWith( (t) =>
                {
                    // The worker task itself should not fail; instead it returns a queue
                    // of exceptions from failed body tasks.
                    Util.Assert( !t.IsFaulted );
                    ConcurrentQueue< Exception > exceptions = t.Result;
                    if( !exceptions.IsEmpty )
                        tcs.SetException( exceptions );
                    else if( t.IsCanceled )
                        tcs.SetCanceled();
                    else
                        tcs.SetResult( true );
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default );

            return tcs.Task;
        } // end ParallelForEachAsync< T >()

        //
        // Below is a completely different implementation of ParallelForEachAsync, which
        // uses a partitioner instead of a semaphore. The primary differences are:
        //
        //    1. The semaphore-based approach has nicer (shorter) callstacks in
        //       exceptions. This is because the partition-based approach has an extra
        //       'catch' that the semaphore-based approach doesn't.
        //    2. The partition-based approach is "greedier": it will spin up
        //       concurrencyLevel partition tasks immediately, and maintain them, whereas
        //       the semaphore-based approach only spins up additional tasks during async
        //       lulls in the body tasks (IOW if the body tasks execute completely
        //       synchronously, then the semaphore-based approach will process them
        //       synchronously, and NOT in parallel).
        //
        // Which approach is Better? I don't know. It depends? Both approaches are tricky
        // to implement, and both of these implementations are thoroughly tested and
        // debugged, so I want to include them both. In the event of an emergency, or if
        // you suspect a bug in one, one can be swapped out for the other, and it could be
        // fun to compare performance characteristics.
        //

    //  public static Task ParallelForEachAsync< T >( IEnumerable< T > source,
    //                                                int concurrencyLevel,
    //                                                Func< T, Task > body )
    //  {
    //      return ParallelForEachAsync( source,
    //                                   concurrencyLevel,
    //                                   CancellationToken.None,
    //                                   body );
    //  }

    //  public static Task ParallelForEachAsync< T >( IEnumerable< T > source,
    //                                                int concurrencyLevel,
    //                                                CancellationToken cancelToken,
    //                                                Func< T, Task > body )
    //  {
    //      return ParallelForEachAsync( source,
    //                                   concurrencyLevel,
    //                                   CancellationToken.None,
    //                                   0,
    //                                   body );
    //  }

    //  public static Task ParallelForEachAsync< T >( IEnumerable< T > source,
    //                                                int concurrencyLevel,
    //                                                CancellationToken cancelToken,
    //                                                int errorLimit,
    //                                                Func< T, Task > body )
    //  {
    //      // Q: What does NoBuffering mean?
    //      //
    //      // A: I did a test, where I did a ParallelForEachAsync on a list of 100 ints
    //      // (0 .. 99), where the "body" task just did an "await Task.Delay( 2000 )",
    //      // and a concurrencyLevel of 10.
    //      //
    //      // For the first 9 blocks of 10 integers, everything went as expected: I'd see
    //      // a block of 10 ints begin processing, then a delay of a couple of seconds
    //      // before the next "batch". But for the last 10 elements, instead of each
    //      // partition grabbing a single element from among the last 10 and processing
    //      // them in parallel, the elements in that last block of 10 got split up across
    //      // just a few partitions, resulting in several smaller batches. This also
    //      // meant additional 2-second delays. Our parallelism had dropped off at the
    //      // end!
    //      //
    //      // What was going on?
    //      //
    //      // It turns out the default partitioning scheme can pull multiple items out of
    //      // the source enumerator at a time, to be more efficient (reduce the cost of
    //      // locking since pulling stuff out of the enumerator is not threadsafe and
    //      // must be locked). By using the "NoBuffering" option, we turn off that
    //      // behavior. For async tasks that are relatively long-lived (for example,
    //      // going over the network), whose costs dwarfs the cost of locking,
    //      // NoBuffering will likely offer better performance (by not dropping the level
    //      // of parallelism at the end) for sufficiently "short" source lists.
    //      //
    //      // But if you have very, very cheap tasks, you might want to use
    //      // EnumerablePartitionerOptions.None instead.
    //      //
    //      // From stoub:
    //      //
    //      //   Pulling data out of an enumerator is not a thread-safe operation; even if
    //      //   the enumerator internally synchronized in calls to MoveNext and Current,
    //      //   it still takes a consumer two calls to extract data, and those could be
    //      //   interleaved from multiple threads accessing the enumerator. As such, the
    //      //   partitioner needs to synchronize across all of the partitions it creates,
    //      //   ensuring that MoveNext and Current on the underlying enumerator are
    //      //   called atomically.
    //      //
    //      //   If the consumer of this data is doing a lot of work per element being
    //      //   processed, then the cost of that synchronization is likely negligible.
    //      //   But as the amount of work per element decreases, the relative overhead of
    //      //   that lock increases, to the point where it can negate the benefits of
    //      //   parallelism.
    //      //
    //      //   As such, the default partitioner uses a heuristic to try to amortize the
    //      //   cost of the lock across multiple elements: acquire the lock, take
    //      //   multiple elements from the enumerator, release the lock, hand back those
    //      //   elements in the partition for the consumer to process.
    //      //
    //      //   Now, the partitioner has no idea how many elements are in the enumerator,
    //      //   as it's just a forward iterator without a length, so it'd be bad for it
    //      //   to assume a batch size of N and always hand out N, e.g. if N were 10 and
    //      //   there were only 8 elements in the enumerable, all 8 elements would be
    //      //   given to a single partition.  So, the default partitioner uses a ramp-up
    //      //   scheme. Each partition first does one element at a time. After a few
    //      //   successful acquisitions of one at a time, it then goes to two. And then
    //      //   after a few of those, to four at a time. It maxes out at a batch size of
    //      //   16 IIRC; after that it doesn't increase further. This approach generally
    //      //   provides good behavior. But for cases where it doesn't, you can disable
    //      //   that ramp-up scheme and enforce a "batch size" of 1, aka NoBuffering,
    //      //   such that the partitioner extracts just one element from the enumerator
    //      //   at a time. You can also of course implement your own Partitioner if you
    //      //   like.
    //      //
    //      return ParallelForEachAsync( source,
    //                                   concurrencyLevel,
    //                                   cancelToken,
    //                                   errorLimit,
    //                                   EnumerablePartitionerOptions.NoBuffering,
    //                                   body );
    //  }

    //  public static Task ParallelForEachAsync< T >( IEnumerable< T > source,
    //                                                int concurrencyLevel,
    //                                                CancellationToken cancelToken,
    //                                                int errorLimit,
    //                                                EnumerablePartitionerOptions partitionOptions,
    //                                                Func< T, Task > body )
    //  {
    //      var tcs = new TaskCompletionSource< bool >();
    //      var exceptions = new ConcurrentQueue< Exception >();
    //
    //      // We're going to create 'concurrencyLevel' partitions, each with an async
    //      // Task that will run in parallel. Each partition task has a 'while' loop that
    //      // pulls items out of 'source' and processes them.
    //      Task[] partitionTasks = new Task[ concurrencyLevel ];
    //      int partitionIdx = 0;
    //      foreach( var partition in Partitioner.Create( source, partitionOptions ).GetPartitions( concurrencyLevel ) )
    //      {
    //          partitionTasks[ partitionIdx++ ] = Task.Run( async delegate
    //              {
    //                  using( partition )
    //                  while( partition.MoveNext() )
    //                  {
    //                      cancelToken.ThrowIfCancellationRequested();
    //                      if( (errorLimit >= 0) && (exceptions.Count > errorLimit) )
    //                          break;

    //                      Task bodyTask = null;
    //                      try
    //                      {
    //                          bodyTask = body( partition.Current );
    //                          await bodyTask;
    //                      }
    //                      catch( Exception e )
    //                      {
    //                          if( bodyTask.IsFaulted )
    //                          {
    //                              // We use "await" for convenient asynchrony, but if it failed and
    //                              // there was actually more than one exception, 'await' will only
    //                              // re-throw the first. We'll use the Task.Exception field to make
    //                              // sure we get all the exceptions related to the task.
    //                              exceptions.Enqueue( SuperFlatten( bodyTask.Exception ) );
    //                          }
    //                          else
    //                          {
    //                              Util.Assert( bodyTask.IsCanceled );
    //                              break;
    //                          }
    //                      }
    //                  } // end while( partition.MoveNext() )
    //              },
    //              cancelToken );
    //      }

    //      Task rollupTask = Task.WhenAll( partitionTasks );

    //      rollupTask.ContinueWith( (t) =>
    //          {
    //              if( !exceptions.IsEmpty )
    //                  tcs.SetException( exceptions );
    //              else if( t.IsCanceled )
    //                  tcs.SetCanceled();
    //              else
    //                  tcs.SetResult( true );
    //          },
    //          CancellationToken.None,
    //          TaskContinuationOptions.ExecuteSynchronously,
    //          TaskScheduler.Default );

    //      return tcs.Task;
    //  } // end ParallelForEachAsync< T >()


        public static ErrorRecord FixErrorRecord( ErrorRecord er, Exception e )
        {
            if( !(er.Exception is ParentContainsErrorRecordException) )
            {
                // The error record already contains a "useful" exception.
                return er;
            }

            // The ParentContainsErrorRecordException is completely useless--the PS
            // runtime sometimes uses it to prevent a circular reference between an
            // Exception that implements IContainsErrorRecord and the ErrorRecord object.
            // So we need to create a new ErrorRecord with the useful exception.
            return new ErrorRecord( er, e );
        } // end FixErrorRecord()


        public static void WriteErrorsForAggregateException( IPipelineCallback callback, AggregateException ae, string errorId )
        {
            if( null == ae )
                return;

            foreach( Exception e in ae.Flatten().InnerExceptions )
            {
                if( e is IContainsErrorRecord )
                {
                    callback.WriteError( FixErrorRecord( ((IContainsErrorRecord) e).ErrorRecord, e ) );
                }
                else
                {
                    callback.WriteError( e, errorId, ErrorCategory.NotSpecified, null );
                }
            }
        } // end WriteErrorsForAggregateException()


        private sealed class FakeImpersonationHandle : IDisposable
        {
            public void Dispose() { }
        }


        public static IDisposable LogonAndImpersonateUser( PSCredential credential )
        {
            using( SafeUserTokenHandle userToken = LogonUser( credential ) )
            {
                return ImpersonateUser( userToken );
            }
        }


        internal static SafeUserTokenHandle LogonUser( PSCredential credential )
        {
            if( credential == null )
                return null;

            NetworkCredential nc = credential.GetNetworkCredential();
            IntPtr password = IntPtr.Zero;
            SafeUserTokenHandle phToken = null;
            try
            {
                password = Marshal.SecureStringToGlobalAllocUnicode( nc.SecurePassword );

                // Using interactive logon will fail if we are using the VCO
                // credential--non-user accounts cannot log on interactively. Because of
                // the VCO credential scenario, really our only choices are NETWORK,
                // NETWORK_CLEARTEXT, and SERVICE.
                int logonType = NativeMethods.LOGON32_LOGON_NETWORK_CLEARTEXT;
                int logonProvider = NativeMethods.LOGON32_PROVIDER_DEFAULT;
                LogManager.Trace( "Using logon type {0}, provider {1}.", logonType, logonProvider );
                if( !NativeMethods.LogonUser( nc.UserName,
                                              nc.Domain,
                                              password,
                                              logonType,
                                              logonProvider,
                                              out phToken ) )
                {
                    int err = Marshal.GetLastWin32Error();
                    LogManager.Trace( "LogonUser failed: {0}", FormatErrorCode( err ) );
                    throw new Win32Exception( err );
                }

                return phToken;
            }
            finally
            {
                if( IntPtr.Zero != password )
                    Marshal.ZeroFreeGlobalAllocUnicode( password );
            }
        }


        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods", MessageId="System.Runtime.InteropServices.SafeHandle.DangerousGetHandle" )]
        internal static IDisposable ImpersonateUser( SafeUserTokenHandle userToken )
        {
            if( null == userToken)
                return new FakeImpersonationHandle();

            bool addedRef = false;
            try
            {
                WindowsImpersonationContext impCtx;

                // we need to call userToken.DangerousAddRef because after userToken.DangerousGetHandle
                // returns, but before WindowsIdentity.Impersonate uses the underlying IntPtr handle,
                // there's no reference to userToken anymore, so GC might claim it.
                userToken.DangerousAddRef( ref addedRef );
                impCtx = WindowsIdentity.Impersonate( userToken.DangerousGetHandle() );
                return impCtx;
            }
            finally
            {
                if( addedRef )
                    userToken.DangerousRelease();
            }
        } // end ImpersonateUser()


        // TODO: do we only need to have it, or does it need to be enabled? I haven't
        // tried a case where it is present but disabled because you have to write code to
        // disable it at runtime.
        public static bool HaveImpersonatePrivilege()
        {
            SafeUserTokenHandle token = null;
            try
            {
                if( !NativeMethods.OpenProcessToken( NativeMethods.GetCurrentProcess(), TokenAccessLevels.Query, out token ) )
                {
                    throw new Win32Exception(); // uses last win32 error automatically
                }
                IList< TokenPrivilege > privs = GetTokenPrivileges( token );
                return null != privs.FirstOrDefault( (p) => p.Privilege == Privilege.Impersonate );
            }
            catch( Win32Exception w32e )
            {
                LogManager.Trace( "Could not get token privileges: {0}", Util.GetExceptionMessages( w32e ) );
                return false;
            }
            finally
            {
                if( null != token )
                    token.Dispose();
            }
        } // end HaveImpersonatePrivilege()


        internal static IList< TokenPrivilege > GetTokenPrivileges( SafeUserTokenHandle token )
        {
            int err = 0;
            int cbValid = 512;
            int cbBuf;
            byte[] buf;
            do
            {
                cbBuf = cbValid;
                buf = new byte[ cbBuf ];
                bool itWorked = NativeMethods.GetTokenInformation( token,
                                                                   TokenInformationClass.Privileges,
                                                                   buf,
                                                                   cbBuf,
                                                                   out cbValid );
                if( itWorked )
                {
                    err = 0;
                }
                else
                {
                    err = Marshal.GetLastWin32Error();
                }
            } while( (NativeMethods.ERROR_INSUFFICIENT_BUFFER == err) ||
                     (NativeMethods.ERROR_MORE_DATA == err) );

            if( 0 != err )
                throw new Win32Exception( err );

            try
            {
                using( MemoryStream ms = new MemoryStream( buf, 0, cbValid ) )
                using( BinaryReader reader = new BinaryReader( ms ) )
                {
                    int privilegeCount = reader.ReadInt32();
                    TokenPrivilege[] privileges = new TokenPrivilege[ privilegeCount ];
                    for( int i = 0; i < privilegeCount; i++ )
                    {
                        uint lo = reader.ReadUInt32();
                        uint hi = reader.ReadUInt32();
                        uint attrs = reader.ReadUInt32();

                        privileges[ i ] = new TokenPrivilege( new LUID_AND_ATTRIBUTES()
                                                              {
                                                                  Luid = new LUID() { LowPart = lo, HighPart = hi },
                                                                  Attributes = attrs
                                                              } );
                    }

                    return privileges;
                }
            }
            catch( IOException ioe )
            {
                Util.Fail( "This should not happen." );
                throw new InvalidDataException( Util.Sprintf( "GetTokenInformation returned unexpected data: {0}",
                                                              Util.GetExceptionMessages( ioe ) ),
                                                ioe );
            }
        } // end GetTokenPrivileges()


        public static ErrorCategory GetErrorCategoryForException( Exception e )
        {
            if( e is UnauthorizedAccessException )
            {
                return ErrorCategory.SecurityError;
            }
            else if( e is FileNotFoundException )
            {
                return ErrorCategory.ObjectNotFound;
            }
            else if( e is NotSupportedException )
            {
                return ErrorCategory.NotImplemented;
            }
            else if( e is IOException )
            {
                return ErrorCategory.WriteError;
            }

            return ErrorCategory.NotSpecified;
        }

        private static readonly int[] sm_accessDeniedErrors = unchecked( new int[]
                {
                    5,                  // ERROR_ACCESS_DENIED
                    (int) 0x80070005,   // HRESULT_FROM_WIN32( ERROR_ACCESS_DENIED )
                    (int) 0x8001011B,   // RPC_E_ACCESS_DENIED
                    (int) 0x803D0005,   // WS_E_ENDPOINT_ACCESS_DENIED
                } );

        public static bool IsAccessDeniedError( int errorCode )
        {
            return sm_accessDeniedErrors.Contains( errorCode );
        }


        public static string HashtableToPsExpression( Hashtable h )
        {
            if( null == h)
                return "$null";

            StringBuilder sb = new StringBuilder();
            sb.Append( "@{" );
            bool first = true;
            foreach( object obj in h )
            {
                if( first )
                {
                    first = false;
                }
                else
                {
                    sb.Append( ";" );
                }

                DictionaryEntry de = (DictionaryEntry) obj;
                sb.Append( " '" );
                sb.Append( de.Key.ToString() );
                sb.Append( "' = " );
                if( null == de.Value )
                {
                    sb.Append( "$null" );
                }
                else
                {
                    sb.Append( "'" );
                    sb.Append( de.Value.ToString() );
                    sb.Append( "'" );
                }
            }

            sb.Append( " }" );
            return sb.ToString();
        } // end HashtableToPsExpression()

        public static string ArrayToPsExpression( Array a )
        {
            StringBuilder sb = new StringBuilder();
            sb.Append( "@(" );
            bool first = true;
            foreach( object obj in a )
            {
                if( first )
                {
                    first = false;
                }
                else
                {
                    sb.Append( "," );
                }

                if( null == obj )
                {
                    sb.Append( "$null" );
                }
                else
                {
                    sb.Append( " '" );
                    sb.Append( obj.ToString() );
                    sb.Append( "'" );
                }
            }

            sb.Append( " )" );
            return sb.ToString();
        } // end ArrayToPsExpression()

        //
        // GetTickCount64 stuff
        //

        private delegate ulong GetTickCount64Delegate();

        private static GetTickCount64Delegate sm_GetTickCount64;


        private static void _AcquireGetTickCount64Delegate()
        {
            IntPtr hKernel32 = NativeMethods.LoadLibrary( "kernel32.dll" );
            if( IntPtr.Zero == hKernel32 )
                throw new Win32Exception(); // last win32 error will automatically be used.

            IntPtr ptrGetTickCount64 = NativeMethods.GetProcAddress( hKernel32, "GetTickCount64" );
            if( IntPtr.Zero != ptrGetTickCount64 )
            {
                sm_GetTickCount64 = (GetTickCount64Delegate) Marshal.GetDelegateForFunctionPointer( ptrGetTickCount64, typeof( GetTickCount64Delegate ) );
            }
            else
            {
                // We must be on XP.
                NativeMethods.FreeLibrary( hKernel32 );
                sm_GetTickCount64 = _EmulateGetTickCount64;
            }
        } // end _AcquireGetTickCount64Delegate()


        private static uint sm_tickCountHi;
        private static uint sm_lastTickCountLo;

        // N.B. Must be called more than once every 49.7 days, else you get the wrong
        // answer. But in no case will time appear to move backwards.
        private static ulong _EmulateGetTickCount64()
        {
            uint curTickCount = NativeMethods.GetTickCount();
            if( curTickCount < sm_lastTickCountLo )
            {
                sm_tickCountHi++;
            }
            sm_lastTickCountLo = curTickCount;

            return (((ulong) sm_tickCountHi) << 32) + ((ulong) curTickCount);
        } // end _EmulateGetTickCount64()


        /// <summary>
        ///    Retrieves the number of milliseconds that have elapsed since the system was
        ///    started.
        /// </summary>
        /// <remarks>
        /// <para>
        ///    This value is a monotonically increasing value, unlike DateTime.Now.
        ///    DateTime.Now can appear to move backwards due to system time changes, which
        ///    can cause bugs in programs that expect to be able to calculate elapsed time
        ///    by subtracting DateTimes obtained from DateTime.Now.
        /// </para>
        /// <para>
        ///    IMPORTANT: On Vista+, this simply calls the native GetTickCount64. On XP,
        ///    it emulates a 64-bit tick counter. The emulation assumes that the calling
        ///    program will call it <em>more than once every 49 days.</em> If not, the
        ///    results will be unpredictable.
        /// </para>
        /// </remarks>
        /// <returns>
        ///    A 64-bit value that contains the number of milliseconds that have elapsed
        ///    since the system was started.
        /// </returns>
        public static ulong GetTickCount64()
        {
            // This is not threadsafe. It does not matter.
            if( null == sm_GetTickCount64 )
            {
                _AcquireGetTickCount64Delegate();
            }

            return sm_GetTickCount64();
        } // end GetTickCount64()


        public static FILETIME FileTimeFromLong( long filetime )
        {
            return new FILETIME()
            {
                dwLowDateTime  = (int) (filetime & 0x00000000ffffffffL),
                dwHighDateTime = (int) (filetime >> 32)
            };
        } // end FileTimeFromLong()


        public static byte[] MarshalStringNullTerminate( string s )
        {
            byte[] buf = new byte[ (s.Length + 1) * 2 ]; // account for the terminating null
            int cb = Encoding.Unicode.GetBytes( s, 0, s.Length, buf, 0 );
            Util.Assert( buf.Length == cb + 2 );
            return buf;
        }

        public static string UnmarshalString( byte[] buffer )
        {
            return UnmarshalString( buffer, buffer.Length );
        }

        public static string UnmarshalString( byte[] buffer, int dataLength )
        {
            // Ignore trailing nulls.
            while( (dataLength > 2) &&
                   (0 == (buffer[ dataLength - 2 ] | buffer[ dataLength - 1 ])) )
            {
                dataLength -= 2;
            }

            try
            {
                return Encoding.Unicode.GetString( buffer, 0, dataLength );
            }
            catch( ArgumentException ae )
            {
                throw new InvalidDataException( Util.Sprintf( "Invalid string data: {0}",
                                                              Util.GetExceptionMessages( ae ) ),
                                                ae );
            }
        } // end UnmarshalString()


        // Returns the number of bytes from idx up to the next null, NOT counting the
        // null.
        private static int _BytesUntilNextNull( byte[] buf, int startIdx )
        {
            try
            {
                int idx = startIdx;
                while( 0 != (buf[ idx ] | buf[ idx + 1 ]) )
                {
                    idx += 2;
                }

                return idx - startIdx;
            }
            catch( IndexOutOfRangeException ioore )
            {
                throw new InvalidDataException( "Malformed MultiSz data; couldn't find null.", ioore );
            }
        } // end _BytesUntilNextNull()


        public static string[] UnmarshalMultiSz( byte[] buffer )
        {
            return UnmarshalMultiSz( buffer, buffer.Length );
        }

        public static string[] UnmarshalMultiSz( byte[] buffer, int dataLength )
        {
            List< string > strings = new List< string >();
            try
            {
                int idx = 0;
                while( true )
                {
                    int len = _BytesUntilNextNull( buffer, idx );
                    if( (0 == len) && ((idx + 2) == dataLength) )
                        break;

                    strings.Add( Encoding.Unicode.GetString( buffer, idx, len ) );
                    idx += len + 2; // 2 for null
                }

                if( (idx + 2) != dataLength )
                {
                    throw new InvalidDataException( Util.Sprintf( "Invalid MultiSz data: dataLength was {0}, but we read {1}.",
                                                                  dataLength,
                                                                  (idx + 2) ) );
                }

                return strings.ToArray();
            }
            catch( ArgumentException ae )
            {
                throw new InvalidDataException( Util.Sprintf( "Invalid string data: {0}",
                                                              Util.GetExceptionMessages( ae ) ),
                                                ae );
            }
        } // end UnmarshalMultiSz()


        public static FileStream CreateFile( string fileName,
                                             FileMode creationDisposition,
                                             FileAccess desiredAccess )
        {
            return CreateFile( fileName, creationDisposition, desiredAccess, FileShare.Read );
        }

        // Unlike the FileStream constructors, this allows you to open handles to
        // alternate NTFS streams. (ex.: @"C:\temp\tmp.txt:altStream")
        public static FileStream CreateFile( string fileName,
                                             FileMode creationDisposition,
                                             FileAccess desiredAccess,
                                             FileShare shareMode )
        {
            const uint GENERIC_READ    = 0x80000000;
            const uint GENERIC_WRITE   = 0x40000000;

            uint dwAccess = 0;
            switch( desiredAccess )
            {
                case FileAccess.Read:
                    dwAccess = GENERIC_READ;
                    break;
                case FileAccess.Write:
                    dwAccess = GENERIC_WRITE;
                    break;
                case FileAccess.ReadWrite:
                    dwAccess = GENERIC_READ | GENERIC_WRITE;
                    break;
                default:
                    throw new ArgumentException( "I don't recognize the desiredAccess value.", "desiredAccess" );
            }

            bool seekToEnd;
            if( FileMode.Append == creationDisposition )
            {
                // FileMode.Append doesn't actually correspond to a win32 API flag; it's a
                // "made up" flag in .NET.
                seekToEnd = true;
                creationDisposition = FileMode.OpenOrCreate;
            }
            else
            {
                seekToEnd = false;
            }

            SafeFileHandle hFile = NativeMethods.CreateFile( fileName,
                                                             dwAccess,
                                                             shareMode,
                                                             IntPtr.Zero,
                                                             creationDisposition,
                                                             0,
                                                             IntPtr.Zero );
            int lastErr = Marshal.GetLastWin32Error();
            if( hFile.IsInvalid )
            {
                throw Win32ErrorToIOException( lastErr, Util.McSprintf( Resources.ErrMsgCreateFileFailedFmt, fileName ) );
            }

            FileStream fs = new FileStream( hFile, desiredAccess );
            // If lastErr is 0, then the file was just now created; no need to seek.
            if( seekToEnd && (NativeMethods.ERROR_ALREADY_EXISTS == lastErr) )
            {
                fs.Seek( 0, SeekOrigin.End );
            }
            return fs;
        } // end CreateFile()


        // This method should only be used for IO errors. However:
        //
        // N.B. This /can/ return something that is not an IOException: for instance,
        // ArgumentException, OperationCanceledException, etc.
        private static Exception Win32ErrorToIOException( int errorCode, MulticulturalString errorMsg )
        {
            Exception e = TryConvertWin32ErrorToIOException( errorCode, errorMsg );
            if( null == e )
            {
                Util.Fail( Util.Sprintf( "Unexpected win32 IO error code: {0}", errorCode ) );
                return new IOException( errorMsg, new Win32Exception( errorCode ) );
            }
            else
            {
                return e;
            }
        } // end Win32ErrorToIOException()


        // This method will return an exception for certain known error codes. (N.B. It
        // will not necessarily return an IOException: for instance, ArgumentException,
        // OperationCanceledException, etc.) If the error code is not known, it will
        // return null.
        public static Exception TryConvertWin32ErrorToIOException( int errorCode, MulticulturalString errorMsg )
        {
            Win32Exception w32e = new Win32Exception( errorCode );
            return TryConvertWin32ExceptionToIOException( w32e, errorMsg );
        } // end TryConvertWin32ErrorToIOException()


        public static Exception TryConvertWin32ExceptionToIOException( Win32Exception w32e, MulticulturalString errorMsg )
        {
            int errorCode = w32e.NativeErrorCode;
            if( errorCode < 0 )
            {
                const int HRESULT_WITH_FACILITY_WIN32 = unchecked( (int) 0x80070000 );
                if( HRESULT_WITH_FACILITY_WIN32 == (errorCode & 0xffff0000) )
                {
                    errorCode = errorCode & 0x0000ffff;
                }
            }

            if( 0 == errorCode )
            {
                throw new ArgumentException( "The error code does not indicate an error.", "w32e" );
            }

            // Log the en-us and localized message.
            LogManager.Trace( "Win32 IO Error {0}: {1} ({2})", errorCode, errorMsg, errorMsg.ToString() );

            const int ERROR_FILE_NOT_FOUND = 2;
            const int ERROR_PATH_NOT_FOUND = 3;
            const int ERROR_ACCESS_DENIED = 5;
            const int ERROR_INVALID_DRIVE = 15;
            const int ERROR_SHARING_VIOLATION = 32;
            const int ERROR_LOCK_VIOLATION = 33;
            const int ERROR_HANDLE_EOF = 38;
            const int ERROR_HANDLE_DISK_FULL = 39;
            const int ERROR_FILE_EXISTS = 80;
            const int ERROR_CANNOT_MAKE = 82;
            const int ERROR_INVALID_PARAMETER = 87;
            const int ERROR_OPEN_FAILED = 110;
            const int ERROR_DISK_FULL = 112;
            const int ERROR_CALL_NOT_IMPLEMENTED = 120;
            const int ERROR_INVALID_NAME = 123;
            const int ERROR_NEGATIVE_SEEK = 131;
            const int ERROR_DIR_NOT_EMPTY = 145;
            const int ERROR_PATH_BUSY = 148;
            const int ERROR_BAD_ARGUMENTS = 160;
            const int ERROR_BAD_PATHNAME = 161;
            const int ERROR_ALREADY_EXISTS = 183;
            const int ERROR_FILENAME_EXCED_RANGE = 206;
            const int ERROR_OPERATION_ABORTED = 995;

            switch( errorCode )
            {
                case NativeMethods.ERROR_INVALID_DATA:
                    return new InvalidDataException( errorMsg, w32e );
                case ERROR_FILE_EXISTS:
                case ERROR_SHARING_VIOLATION:
                case ERROR_LOCK_VIOLATION:
                case ERROR_HANDLE_DISK_FULL:
                case ERROR_CANNOT_MAKE:
                case ERROR_OPEN_FAILED:
                case ERROR_DISK_FULL:
                case ERROR_INVALID_NAME:
                case ERROR_NEGATIVE_SEEK:
                case ERROR_DIR_NOT_EMPTY:
                case ERROR_PATH_BUSY:
                case ERROR_BAD_PATHNAME:
                case ERROR_ALREADY_EXISTS:
                    return new IOException( errorMsg, w32e );
                case ERROR_FILE_NOT_FOUND:
                    return new FileNotFoundException( errorMsg, w32e );
                case ERROR_PATH_NOT_FOUND:
                    return new DirectoryNotFoundException( errorMsg, w32e );
                case ERROR_ACCESS_DENIED:
                    return new UnauthorizedAccessException( errorMsg, w32e );
                case ERROR_INVALID_DRIVE:
                    return new System.IO.DriveNotFoundException( errorMsg, w32e );
                case ERROR_HANDLE_EOF:
                    return new EndOfStreamException( errorMsg, w32e );
                case ERROR_INVALID_PARAMETER:
                case ERROR_BAD_ARGUMENTS:
                    return new ArgumentException( errorMsg, w32e );
                case ERROR_CALL_NOT_IMPLEMENTED:
                    return new NotImplementedException( errorMsg, w32e );
                case ERROR_FILENAME_EXCED_RANGE:
                    return new PathTooLongException( errorMsg, w32e );
                case ERROR_OPERATION_ABORTED:
                    return new OperationCanceledException( errorMsg, w32e );
                default:
                    return null;
            }
        } // end TryConvertWin32ExceptionToIOException()


        public static bool TryIO( Action ioOp, out Exception e )
        {
            e = null;
            try
            {
                ioOp();
            }
            catch( IOException ioe ) { e = ioe; }
            catch( UnauthorizedAccessException uae ) { e = uae; }
            catch( SecurityException se ) { e = se; }

            return null == e;
        } // end TryIO()


        public static Exception TryIO( Action ioOp )
        {
            Exception e = null;
            try
            {
                ioOp();
            }
            catch( IOException ioe ) { e = ioe; }
            catch( UnauthorizedAccessException uae ) { e = uae; }
            catch( SecurityException se ) { e = se; }

            return e;
        } // end TryIO()


        public static Exception TryWin32IO( Action ioOp )
        {
            Exception e = null;
            try
            {
                ioOp();
            }
            catch( IOException ioe ) { e = ioe; }
            catch( UnauthorizedAccessException uae ) { e = uae; }
            catch( SecurityException se ) { e = se; }
            catch( Win32Exception w32e ) { e = w32e; }
            catch( NotSupportedException nse ) { e = nse; }

            return e;
        } // end TryWin32IO()


        public static void CopyFile( string srcFile, string destFile, bool allowOverwrite, bool preserveTimes )
        {
            File.Copy( srcFile, destFile, allowOverwrite );
            if( preserveTimes )
            {
                FileInfo srcFi = new FileInfo( srcFile );
                FileInfo destFi = new FileInfo( destFile );
                // Seems inefficient to set these in three separate calls. Oh well.
                destFi.CreationTime = srcFi.CreationTime;
                destFi.LastWriteTime = srcFi.LastWriteTime;
                destFi.LastAccessTime = srcFi.LastAccessTime;
            }
        } // end CopyFile()


        /// <summary>
        /// Removes the "Zone.Identifier" alternate stream, and returns true if at least
        /// one mark was removed.
        ///
        /// If you pass in a directory, it tries to remove all marks in the subtree.
        ///
        /// Ignores all errors.
        /// </summary>
        public static bool RemoveMarkOfTheInternet( string path )
        {
            return _RemoveMarkOfTheInternetWorker( path, true );
        }

        private static bool _RemoveMarkOfTheInternetWorker( string path, bool mightBeDirectory )
        {
            if( mightBeDirectory && Directory.Exists( path ) )
            {
                // Apply to all files in subtree
                bool removedMark = false;
                foreach( var file in Directory.EnumerateFiles( path, "*", SearchOption.AllDirectories ) )
                {
                    removedMark |= _RemoveMarkOfTheInternetWorker( file, false );
                }
                return removedMark;
            }
            else
            {
                const uint STANDARD_RIGHTS_READ = 0x00020000;
                const uint DELETE = 0x00010000;
                const int FILE_FLAG_DELETE_ON_CLOSE = 0x04000000;

                SafeFileHandle hFile = NativeMethods.CreateFile( path + ":Zone.Identifier",
                                                                 STANDARD_RIGHTS_READ | DELETE,
                                                                 FileShare.ReadWrite | FileShare.Delete,
                                                                 IntPtr.Zero,
                                                                 FileMode.Open,
                                                                 FILE_FLAG_DELETE_ON_CLOSE,
                                                                 IntPtr.Zero );
                //int lastErr = Marshal.GetLastWin32Error();
                if( hFile.IsInvalid )
                {
                    //Console.WriteLine( "Error: {0}", lastErr );
                    return false;
                }
                hFile.Close();
                return true;
            }
        } // end _RemoveMarkOfTheInternetWorker()


        public static string FormatByteSize( long numBytes )
        {
            StringBuilder sb = new StringBuilder( 32 );
            IntPtr ptr = NativeMethods.StrFormatByteSizeW( numBytes, sb, sb.Capacity );
            Util.Assert( ptr != IntPtr.Zero, "StrFormatByteSizeW failed." );
            return sb.ToString();
        } // end FormatByteSize()


        public static string FormatQWord( IntPtr ip )
        {
            return FormatQWord( (long) ip );
        } // end FormatQWord( IntPtr )

        public static string FormatQWord( long l )
        {
            return FormatQWord( (ulong) l );
        } // end FormatQWord( long )

        public static string FormatQWord( ulong ul )
        {
            return Sprintf( "0x{0:x8}`{1:x8}",
                            (int) ((ul & 0xffffffff00000000) >> 32),
                            (int) (ul & 0x00000000ffffffff) );
        } // end FormatQWord( ulong )


        public static string FormatHalfOrFullQWord( ulong ul )
        {
            int hi = (int) ((ul & 0xffffffff00000000) >> 32);
            int lo = (int) (ul & 0x00000000ffffffff);

            if( 0 == hi )
            {
                if( 0 == lo )
                    return "0";
                else
                    return Sprintf( "0x{0:x8}", lo );
            }
            else
            {
                return Sprintf( "0x{0:x8}`{1:x8}", hi, lo );
            }
        } // end FormatHalfOrFullQWord( ulong )


        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes" )]
        public static void FailFast( string msg, Exception e )
        {
            // Invoking the unhandled exception event is unfortunately not a good idea
            // when running under PowerShell.exe: they hook that event, and their handler
            // will attempt to submit their own, custom WER report, and then call
            // Environment.Exit to perform a "clean" shutdown. This bypasses all the
            // normal OS WER handling (including things like attaching a JIT debugger),
            // and in the instance I tried, ended up blocking on one of their own
            // finalizers which hung waiting for an external PowerShell process.
       //   Type t = typeof( AppDomain );
       //   FieldInfo fi = t.GetField( "_unhandledException", BindingFlags.Instance | BindingFlags.NonPublic );
       //   if( null != fi )
       //   {
       //       UnhandledExceptionEventHandler handler = (UnhandledExceptionEventHandler) fi.GetValue( AppDomain.CurrentDomain );
       //       if( null != handler )
       //       {
       //           try
       //           {
       //               // This should actually pass the AppDomain in effect at the bottom
       //               // of the stack, not the current domain, but the code to get that
       //               // is very complicated. Additionally, if that appdomain is not the
       //               // default AppDomain, then we should check for and execute
       //               // handlers in the default AppDomain, too, which is also
       //               // complicated. Using the current AppDomain will be "good enough"
       //               // for current handlers. FUTURE: maybe a more complete simulation
       //               // in vNext.
       //               handler( AppDomain.CurrentDomain, new UnhandledExceptionEventArgs( e, true ) );
       //           }
       //           catch( Exception e2 )
       //           {
       //               Util.Fail( Sprintf( "Somebody's unhandled exception hook failed: {0}", e2 ) );
       //           }
       //       }
       //   }
       //   else
       //   {
       //       Util.Fail( "Could not find AppDomain unhandled exception handler field." );
       //   }

            try
            {
                LogManager.Trace( "Unhandled exception triggering FailFast: {0}: {1}", msg, e );
                LogManager.Flush();
            }
#if DEBUG
            catch( Exception e2 )
            {
                Console.WriteLine( "Failed to trace unhandled exception: {0}", e2 );
            }
            Console.WriteLine( "Current trace file: {0}", LogManager.CurrentLogFile == null ? "<null>" : LogManager.CurrentLogFile );
#else
            catch( Exception ) { }
#endif

            if( String.IsNullOrEmpty( Environment.GetEnvironmentVariable( "_DBGSHELL_DEMO_MODE" ) ) )
            {
                // VS does not make failfasts easy to dig through; let's stop here if we're under
                // the debugger.
                System.Diagnostics.Debugger.Break();
                Environment.FailFast( msg, e );
            }
            else
            {
                Console.WriteLine( "DEMO MODE: /Not/ failing fast for: {0}", e );
            }
        } // end FailFast()


        public static void Assert( bool allegedInvariant,
                                   [CallerLineNumber] int line = 0,
                                   [CallerMemberName] string caller = "",
                                   [CallerFilePath] string file = "" )
        {
            if( allegedInvariant )
                return;

            _AssertFailWorker( null, caller, file, line );
        }

        public static void Assert( bool allegedInvariant,
                                   string msg,
                                   [CallerLineNumber] int line = 0,
                                   [CallerMemberName] string caller = "",
                                   [CallerFilePath] string file = "" )
        {
            if( allegedInvariant )
                return;

            _AssertFailWorker( msg, caller, file, line );
        }

        public static void Fail( string msg,
                                 [CallerLineNumber] int line = 0,
                                 [CallerMemberName] string caller = "",
                                 [CallerFilePath] string file = "" )
        {
            _AssertFailWorker( msg, caller, file, line );
        }

        private static void _AssertFailWorker( string msg, string caller, string file, int line )
        {
            if( String.IsNullOrEmpty( msg ) )
                msg = "<no message supplied>";

            msg = Util.Sprintf( "{0} ({1}, {2}:{3})", msg, caller, file, line );
            // We want to both 1) capture the failed assert in the log, even in FRE
            // builds, and 2) flush the log in case someone captures the logs while the
            // assert popup is still up.
            LogManager.Trace( "FAILED ASSERT: \"{0}\", at:", msg );
            LogManager.Trace( new StackTrace( 2, true ).ToString() ); // don't show this stack frame and the public assert stack frame
            LogManager.Flush();
            Debug.Fail( msg );
        } // end _AssertFailWorker()


        /// <summary>
        ///    Dumbly splits on the first '\'.
        /// </summary>
        /// <remarks>
        ///    If there is no '\', then car will have the value of path, and cdr will be
        ///    empty. If path is empty, car and cdr will both be empty.
        /// </remarks>
        public static void SplitPath( string path, out string car, out string cdr )
        {
            if( null == path )
                throw new ArgumentNullException( "path" );

            int idx = path.IndexOf( '\\' );
            if( idx < 0 )
            {
                car = path;
                cdr = String.Empty;
            }
            else
            {
                car = path.Substring( 0, idx );
                if( idx == (path.Length - 1) )
                    cdr = String.Empty;
                else
                    cdr = path.Substring( idx + 1 );
            }
        } // end SplitPath()


        /// <summary>
        ///    Similar to SplitPath, but splits on the last '\' instead of the first one.
        /// </summary>
        public static void SplitPathLeaf( string path, out string parent, out string leaf )
        {
            if( null == path )
                throw new ArgumentNullException( "path" );

            int idx = path.LastIndexOf( '\\' );
            if( idx < 0 )
            {
                parent = String.Empty;
                leaf = path;
            }
            else
            {
                parent = path.Substring( 0, idx );
                if( idx == (path.Length - 1) )
                    leaf = String.Empty;
                else
                    leaf = path.Substring( idx + 1 );
            }
        } // end SplitPathLeaf()


        public static string PadWhitespaceRight( string s, int width )
        {
            if( s.Length < width )
            {
                s = s + new String( ' ', width - s.Length );
            }
            return s;
        } // end PadWhitespaceRight


        public static void LogError( string prefix, ErrorRecord er )
        {
            LogManager.Trace( "{0} WriteError1: {1}", prefix, er.ErrorDetails == null ? "(no details)" : er.ErrorDetails.Message );
            LogManager.Trace( "{0} WriteError2: {1}", prefix, er.CategoryInfo == null ? "(no category info)" : er.CategoryInfo.ToString() );
            LogManager.Trace( "{0} WriteError3: {1}", prefix, er.FullyQualifiedErrorId );
            LogManager.Trace( "{0} WriteError4: {1}", prefix, er.Exception );

            // There are a couple of special cases where very interesting exception data
            // won't get logged by the default Exception.ToString() call.
            //
            // 1. If the exception came across a PS remoting channel, PS squirrels away
            //    the essential troubleshooting data. Let's dig it out and put it in the
            //    log (else you have to be at an interactive PS prompt to get it).
            // 2. If we transported an external ErrorRecord via a DbgProviderException,
            //    it would be nice to know the stack trace it took through our code.
            int depth = 0;
            Exception e = er.Exception;
            while( null != e )
            {
                if( e is RemoteException )
                {
                    LogManager.Trace( "{0} WriteError5: Found a RemoteException at depth {1}. SerializedRemoteException: {2}",
                                      prefix,
                                      depth,
                                      ((RemoteException) e).SerializedRemoteException.ToString() );
                }

                if( (null != e.Data) && (e.Data.Contains( DbgProviderException.DpeKey )) )
                {
                    LogManager.Trace( "{0} WriteError6: Found a transport TPE at depth {1}: {2}",
                                      prefix,
                                      depth,
                                      (DbgProviderException) e.Data[ DbgProviderException.DpeKey ] );
                }

                depth++;
                e = e.InnerException;
            }
        } // end LogError()


        private class MassageTypeNameTestCase
        {
            public readonly string Input;
            public readonly bool RemoveNamespaces;
            public readonly string ExpectedOutput; // If null, then exception expected
            public readonly bool ExpectIsGeneric;
            public MassageTypeNameTestCase( string input,
                                            string expectedOutput,
                                            bool expectIsGeneric )
                : this( input, false, expectedOutput, expectIsGeneric )
            {
            }

            public MassageTypeNameTestCase( string input,
                                            bool removeNamespaces,
                                            string expectedOutput,
                                            bool expectIsGeneric )
            {
                Input = input;
                RemoveNamespaces = removeNamespaces;
                ExpectedOutput = expectedOutput;
                ExpectIsGeneric = expectIsGeneric;
            }
        } // end class MassageTypeNameTestCase()


        //
        // This is where you can put code to test.
        //
        public static bool TestMassageTypeName()
        {
            MassageTypeNameTestCase[] testCases = {
     /*  0 */   new MassageTypeNameTestCase( "System.Collections.Generic.Dictionary`2[[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[System.Int32, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]",
                                             "System.Collections.Generic.Dictionary<System.String,System.Int32>",
                                             true ),
     /*  1 */   new MassageTypeNameTestCase( "System.String",
                                             "System.String",
                                             false ),
     /*  2 */   new MassageTypeNameTestCase( "System.Collections.Generic.Dictionary`2[[System.Collections.Generic.KeyValuePair`2[[System.Int32, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[System.Tuple`2[[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[System.Collections.Generic.List`1[[System.Char, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]], mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]], mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]], mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[System.Tuple`2[[System.Int32, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]], mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]",
                                             "System.Collections.Generic.Dictionary<System.Collections.Generic.KeyValuePair<System.Int32,System.Tuple<System.String,System.Collections.Generic.List<System.Char>>>,System.Tuple<System.Int32,System.String>>",
                                             true ),
     /*  3 */   new MassageTypeNameTestCase( "System.String`xxxx",
                                             "System.String`xxxx", // (this now looks like a native type name with a backtick in it)
                                             false ),
     /*  4 */   new MassageTypeNameTestCase( "Blarg`2[[asdf, blah],[jkl, blah]]",
                                             "Blarg<asdf,jkl>",
                                             true ),
     /*  5 */   new MassageTypeNameTestCase( "Blarg`2[[asdf, blah],[jkl, blah], [qwer, blah]]",
                                             null,
                                             true ),
     /*  6 */   new MassageTypeNameTestCase( "clr!Indexer<unsigned short,SString::Iterator>", // <-- a native template type! (no '`')
                                             "clr!Indexer<unsigned short,SString::Iterator>",
                                             false ), // it's not a generic; it's a native template
     /*  7 */   new MassageTypeNameTestCase( "System.Collections.Generic.Dictionary`2[[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[System.Int32, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]",
                                             true,
                                             "Dictionary<String,Int32>",
                                             true ),
                                             // This was a real type name from CLR.dll.
     /*  8 */   new MassageTypeNameTestCase( "VolatilePtr<`anonymous namespace'::AptcaKillBitList,A0x8f085d03::AptcaKillBitList *>",
                                             "VolatilePtr<`anonymous namespace'::AptcaKillBitList,A0x8f085d03::AptcaKillBitList *>",
                                             false ),
            };


            int numFailed = 0;
            for( int i = 0; i < testCases.Length; i++ )
            {
                var testCase = testCases[ i ];
                string output = null;
                bool isGeneric = false;
                Exception e = null;
                try
                {
                    output = MassageManagedTypeName( testCase.Input,
                                                     testCase.RemoveNamespaces,
                                                     out isGeneric );
                }
                catch( Exception e_ )
                {
                    e = e_;
                }

                if( 0 != StringComparer.OrdinalIgnoreCase.Compare( output, testCase.ExpectedOutput ) )
                {
                    Console.WriteLine( "ERROR : Test case {0} failed.", i );
                    if( null == testCase.ExpectedOutput )
                        Console.WriteLine( "   Expected output: <exception>" );
                    else
                        Console.WriteLine( "   Expected output: {0}", testCase.ExpectedOutput );

                    if( null == e )
                        Console.WriteLine( "     Actual output: {0}", output );
                    else
                        Console.WriteLine( "     Actual output: <exception: {0}>", GetExceptionMessages( e ) );

                    numFailed++;
                }

                if( isGeneric != testCase.ExpectIsGeneric )
                {
                    Console.WriteLine( "ERROR : Test case {0} failed (isGeneric).", i );
                    numFailed++;
                }
            } // end foreach( test case )

            if( 0 == numFailed )
                Console.WriteLine( "All tests passed." );
            else
                Console.WriteLine( "{0} tests failed (out of {1}).", numFailed, testCases.Length );

            return 0 == numFailed;
        } // end TestMassageTypeName()


        /// <summary>
        ///    Converts the name of a generic type to something more like you would see in
        //     source code.
        /// </summary>
        /// <remarks>
        ///    This function /should/ have no effect on native template type names (and
        ///    isGeneric should be returned false). However the current method of
        ///    discrimination between managed generics and native templates is the
        ///    presence of a backtick character, which might be too loose. We'll see.
        ///
        ///    Update: Native types can have backticks in them. It might be necessary to
        ///    use the source of a type name to know if it is managed or not, rather than
        ///    guessing based on the format of the name.
        /// </remarks>
        public static string MassageManagedTypeName( string inputTypeName,
                                                     out bool isGeneric )
        {
            return MassageManagedTypeName( inputTypeName, false, out isGeneric );
        }

        public static string MassageManagedTypeName( string inputTypeName,
                                                     bool removeNamespaces,
                                                     out bool isGeneric )
        {
            int idx = 0;
            var sb = new StringBuilder( inputTypeName.Length + 1 );
            isGeneric = false;
            MassageManagedTypeName( inputTypeName,
                                    removeNamespaces,
                                    ref idx,
                                    sb,
                                    ref isGeneric );
            return sb.ToString();
        }


        private static void MassageManagedTypeName( string input,
                                                    bool removeNamespaces,
                                                    ref int idx,
                                                    StringBuilder output,
                                                    ref bool isGeneric )
        {
            int outputStartPos = output.Length;
            while( idx < input.Length )
            {
                if( (',' == input[ idx ]) && isGeneric ) // note the 'isGeneric' check so we don't trigger this on native tempate type names
                {
                    // We must be in a generic parameter. We can ignore everything up to the
                    // ']'.
                    while( ']' != input[ idx ] )
                    {
                        idx++;
                        if( idx >= input.Length )
                            throw new Exception( "Unexpected end of input." );
                    }
                    return;
                }
                else if( ('.' == input[ idx ]) && removeNamespaces )
                {
                    idx++;
                    output.Length = outputStartPos;
                }
                else if( '`' != input[ idx ] )
                {
                    output.Append( input[ idx ] );
                    idx++;
                }
                else
                {
                    // input[ idx ] is a backtick. ('`')
                    // This /usually/ means it's a managed, generic type name. But
                    // sometimes not. For example:
                    //
                    //    VolatilePtr<`anonymous namespace'::AptcaKillBitList,A0x8f085d03::AptcaKillBitList *>
                    //
                    // Let's peek ahead to see if the next char is a digit and use that
                    // to tell the difference.

                    idx++;
                    char c = input[ idx ];

                    if( !Char.IsDigit( c ) )
                    {
                        // Oops; not generic. Nothing to see here; move along.
                        output.Append( '`' ); // the backtick we skipped.
                        output.Append( input[ idx ] );
                        idx++;
                    }
                    else
                    {
                        isGeneric = true;
                        int numGenericParams = 0;
                        while( Char.IsDigit( c ) )
                        {
                            numGenericParams *= 10; // TODO: It's in base 10, right?
                            numGenericParams += CharToByte( c ); // base 10
                            idx++;
                            c = input[ idx ];
                        }

                        if( '[' != input[ idx ] )
                            throw new Exception( String.Format( "Expected '[' at position {0}.", idx ) );

                        idx++;

                        output.Append( '<' );

                        for( int i = 0; i < numGenericParams; i++ )
                        {
                            if( 0 != i )
                            {
                                if( ',' != input[ idx ] )
                                    throw new Exception( String.Format( "Expected ',' at position {0}.", idx ) );

                                idx++;
                                output.Append( ',' );
                            }

                            if( '[' != input[ idx ] )
                                throw new Exception( String.Format( "Expected '[' at position {0}.", idx ) );

                            idx++;

                            MassageManagedTypeName( input, removeNamespaces, ref idx, output, ref isGeneric );

                            if( ']' != input[ idx ] )
                                throw new Exception( String.Format( "Expected ']' at position {0}.", idx ) );

                            idx++;
                        } // end foreach( generic type param )

                        if( ']' != input[ idx ] )
                            throw new Exception( String.Format( "Expected ']' at position {0}.", idx ) );

                        idx++;
                        output.Append( '>' );
                    } // end( it's a generic type )
                } // end( found backtick; maybe a generic type )
            } // end while( idx < length )
        } // end MassageManagedTypeName()


        /// <summary>
        ///    Converts a string like "63f2c800f4" into a byte[]. (assumes string is hex)
        /// </summary>
        public static byte[] ConvertToBytes( string s )
        {
            if( 0x01 == (s.Length & 0x01) )
                throw new Exception( "Unexpected odd byte string length." );

            byte[] bytes = new byte[ s.Length / 2 ];

            for( int i = 0; i < bytes.Length; i++ )
            {
                byte b = CharToByte( s[ i * 2 ], true );
                b = (byte) (b << 4);
                b += CharToByte( s[ (i * 2) + 1 ], true );
                bytes[ i ] = b;
            }

            return bytes;
        } // end ConvertToBytes()


        public static byte CharToByte( char c )
        {
            return CharToByte( c, false );
        }

        public static byte CharToByte( char c, bool allowHex )
        {
            byte b;
            if( !TryCharToByte( c, out b, allowHex ) )
            {
                if( allowHex )
                    throw new ArgumentOutOfRangeException( "c", c, "Not a hex number character." );
                else
                    throw new ArgumentOutOfRangeException( "c", c, "Not a decimal number character." );
            }

            return b;
        } // end CharToByte()


        public static bool TryCharToByte( char c, out byte b, bool allowHex )
        {
            b = 0;
            byte tmp = unchecked( (byte) c );

            tmp -= 48; // '0'
            if( tmp < 0 )
                return false;

            if( tmp < 10 ) // 0 - 9
            {
                b = tmp;
                return true;
            }

            if( !allowHex )
                return false;

            tmp -= 17; // ... to 'A'
            if( tmp < 0 )
                return false;

            if( tmp < 6 ) // A - F
            {
                b = (byte) (tmp + 10);
                return true;
            }

            tmp -= 32; // ... to 'a'
            if( tmp < 0 )
                return false;

            if( tmp < 6 ) // a - f
            {
                b = (byte) (tmp + 10);
                return true;
            }

            return false;
        } // end TryCharToByte()


        /// <summary>
        ///    Attempts to retrieve a custom (dynamic) ToString method for a given PSObject.
        /// </summary>
        /// <remarks>
        ///    The base object's normal, .NET ToString() method won't count--only
        ///    'CodeMethod' and 'ScriptMethod' methods.
        /// </remarks>
        public static PSMethodInfo TryGetCustomToStringMethod( PSObject pso )
        {
            var toStringMethods = pso.Methods[ "ToString" ];
            if( (null != toStringMethods) &&
                ((toStringMethods.MemberType == PSMemberTypes.CodeMethod) ||   // These conditions ensure that we don't
                 (toStringMethods.MemberType == PSMemberTypes.ScriptMethod)) ) // count the base object's ToString method.
            {
                // It has a custom ToString.
                return toStringMethods;
            }
            else
            {
                return null;
            }
        } // end TryGetCustomToStringMethod

        /// <summary>
        ///    Convert a UNIX timestamp (time_t, seconds since Jan 1 1970) to a DateTime.
        /// </summary>
        public static DateTime DateTimeFrom_time_t( uint time_t )
        {
            // From http://blogs.msdn.com/b/brada/archive/2003/07/30/50205.aspx.
            long win32FileTime = 10000000 * (long) time_t + 116444736000000000;
            return DateTime.FromFileTimeUtc( win32FileTime );
        }


        public static ulong ConvertToUlongWithoutSignExtension( sbyte b )
        {
            return (ulong) (byte) b;
        }

        public static ulong ConvertToUlongWithoutSignExtension( short s )
        {
            return (ulong) (ushort) s;
        }

        public static ulong ConvertToUlongWithoutSignExtension( int i )
        {
            return (ulong) (uint) i;
        }

        public static ulong ConvertToUlongWithoutSignExtension( long l )
        {
            return (ulong) l;
        }

        // These overloads for the unsigned types aren't so necessary, but they make it
        // convenient to be able to pass an integral of unknown type. (e.g.
        // "Util.ConvertToUlongWithoutSignExtension( (dynamic) obj );")

        public static ulong ConvertToUlongWithoutSignExtension( byte b )
        {
            return (ulong) b;
        }

        public static ulong ConvertToUlongWithoutSignExtension( ushort us )
        {
            return (ulong) us;
        }

        public static ulong ConvertToUlongWithoutSignExtension( uint ui )
        {
            return (ulong) ui;
        }

        public static ulong ConvertToUlongWithoutSignExtension( ulong ul )
        {
            return ul;
        }


        /// <summary>
        ///    Calls the specified function, and if it throws a TargetInvocationException,
        ///    it handles that and re-throws the inner exception (preserving the
        ///    callstack). This lets you call things via reflection, but get the same
        ///    exceptions you would get if you weren't using reflection.
        /// </summary>
        public static TRet UnwrapTargetInvocationException< TRet >( Func< TRet > f )
        {
            try
            {
                return f();
            }
            catch( TargetInvocationException tie )
            {
                Assert( null != tie.InnerException );
                ExceptionDispatchInfo.Capture( tie.InnerException ).Throw();
                return default( TRet ); // <-- unreachable code
            }
        } // end UnwrapTargetInvocationException()
    } // end class Util
}

