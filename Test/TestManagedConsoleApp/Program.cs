using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace TestManagedConsoleApp
{
    class Program
    {
        [DllImport( "kernel32.dll" )]
        private static extern void DebugBreak();


        public struct FooStruct
        {
            public int MyInt;
            public string MyString;
            public bool MyBool;
        }

        public class ClassThatContainsFoo
        {
            public int IntInClass;
            public FooStruct MyFoo;
            public string ClassString;
        }

        private static volatile ClassThatContainsFoo sm_ctcf;
        private static volatile object sm_boxedStruct;

        static int Main( string[] args )
        {
            Thread.CurrentThread.Name = "Main Thread";
            Console.WriteLine( "Hello. This is the test managed console app." );
            int rc = 0;

            sm_ctcf = new ClassThatContainsFoo() { IntInClass = 0x99 };
            sm_ctcf.ClassString = "This is the string for the class.";
            sm_ctcf.MyFoo.MyString = "blah";

            sm_boxedStruct = new FooStruct() { MyInt = 0x42, MyString = "Hello", MyBool = true };


            if( (null != args) &&
                (0 != args.Length) &&
                (0 == StringComparer.OrdinalIgnoreCase.Compare( args[ 0 ], "earlyBreak" )) )
            {
                Console.WriteLine( "Breaking in to debugger..." );
                DebugBreak();
                args = args.Skip( 1 ).ToArray();
            }

            if( (null == args) || (0 == args.Length) )
            {
                Console.WriteLine();
                Console.WriteLine( "What do you want to do?" );
                // TODO: print out choices, loop, handle "quit"
            }
            else
            {
                Func< string[], int > routine;
                while( args.Length > 0 )
                {
                    string instruction = args[ 0 ];
                    args = args.Skip( 1 ).ToArray();
                    int i;
                    for( i = 0; i < args.Length; i++ )
                    {
                        if( args[ i ] == ";" )
                            break;
                    }

                    string[] routineArgs;
                    if( i >= args.Length )
                    {
                        routineArgs = args;
                        args = new string[ 0 ];
                    }
                    else
                    {
                        routineArgs = args.Take( i ).ToArray();
                        args = args.Skip( i + 1 ).ToArray();
                    }

                    if( sm_routines.TryGetValue( instruction, out routine ) )
                    {
                        Console.WriteLine( "Running routine: {0}", instruction );
                        rc = routine( routineArgs );
                        Console.WriteLine( "Routine '{0}' returned: {1}", instruction, rc );
                    }
                    else
                    {
                        Console.WriteLine( "Error: did not understand instruction: {0}", instruction );
                    }
                } // end while( more instructions )
            }

            if( (null != sm_ctcf) && (null != sm_boxedStruct) )
                Console.WriteLine( "Don't optimize those dudes out." );

            Console.WriteLine( "Exiting with rc: {0}", rc );
            return rc;
        } // end Main()


        private static Dictionary< string, Func< string[], int > > sm_routines = new Dictionary< string, Func< string[], int > >( StringComparer.OrdinalIgnoreCase )
        {
            { "nothing", (x) => { return 0; } },

            { "debugBreak", (x) =>
                {
                    Console.WriteLine( "Breaking in to debugger..." );
                    DebugBreak();
                    return 0;
                }
            },

            { "waitEvent", (args) =>
                {
                    IntPtr eventAddr;
                    if( (null == args) || (0 == args.Length) )
                        throw new Exception( "What event should I wait for?" );

                    if( 4 == Marshal.SizeOf( typeof( IntPtr ) ) )
                    {
                        int tmp;
                        if( !Int32.TryParse( args[ 0 ], out tmp ) )
                        {
                            throw new Exception( "Bad Int32 format." );
                        }
                        eventAddr = new IntPtr( tmp );
                    }
                    else
                    {
                        long tmp;
                        if( !Int64.TryParse( args[ 0 ], out tmp ) )
                        {
                            throw new Exception( "Bad Int64 format." );
                        }
                        eventAddr = new IntPtr( tmp );
                    }

                    SafeWaitHandle wh = new SafeWaitHandle( eventAddr, false );
                    // I can't find a better way to do this; I don't know why they don't have
                    // a constructor that takes a SafeWaitHandle.
                    var mre = new ManualResetEvent( false );
                    mre.SafeWaitHandle.Dispose();
                    mre.SafeWaitHandle = wh;
                    Console.WriteLine( "Waiting for event {0}...", eventAddr );
                    mre.WaitOne();
                    Console.WriteLine( "Wait completed." );
                    return 0;
                }
            }, // end waitEvent

            { "setEvent", (args) =>
                {
                    IntPtr eventAddr;
                    if( (null == args) || (0 == args.Length) )
                        throw new Exception( "What event should I set?" );

                    if( 4 == Marshal.SizeOf( typeof( IntPtr ) ) )
                    {
                        int tmp;
                        if( !Int32.TryParse( args[ 0 ], out tmp ) )
                        {
                            throw new Exception( "Bad Int32 format." );
                        }
                        eventAddr = new IntPtr( tmp );
                    }
                    else
                    {
                        long tmp;
                        if( !Int64.TryParse( args[ 0 ], out tmp ) )
                        {
                            throw new Exception( "Bad Int64 format." );
                        }
                        eventAddr = new IntPtr( tmp );
                    }

                    SafeWaitHandle wh = new SafeWaitHandle( eventAddr, false );
                    // I can't find a better way to do this; I don't know why they don't have
                    // a constructor that takes a SafeWaitHandle.
                    var mre = new ManualResetEvent( false );
                    mre.SafeWaitHandle.Dispose();
                    mre.SafeWaitHandle = wh;
                    Console.WriteLine( "Setting event {0}.", eventAddr );
                    mre.Set();
                    return 0;
                }
            }, // end setEvent

            { "sleep", (args) =>
                {
                    int millis = 0;
                    if( (null != args) && (0 != args.Length) )
                        Int32.TryParse( args[ 0 ], out millis );

                    if( 0 == millis )
                        millis = 2000;

                    Thread.Sleep( millis );
                    return 0;
                }
            }, // end sleep

            { "throw", (args) =>
                {
                    throw new Exception( "kaboom! Test exception." );
                }
            }, // end throw

            { "throw2", (args) =>
                {
                    try
                    {
                        throw new Exception( "kaboom! Test exception." );
                    }
                    catch( Exception e )
                    {
                        Console.WriteLine( "Caught: {0}", e.Message );
                        while( true )
                        {
                            Thread.Sleep( 500 );
                        }
                    }
                }
            }, // end throw

            { "createThread", (args) =>
                {
                    Thread t = new Thread( () => { Console.WriteLine( "This is from another thread." ); } );
                    t.Start();
                    t.Join();
                    return 0;
                }
            }, // end createThread

            { "createManyThreads", (args) =>
                {
                    int numThreads = 0;
                    if( (null != args) && (args.Length > 0) )
                    {
                        Int32.TryParse( args[ 0 ], out numThreads );
                    }

                    if( 0 == numThreads )
                        numThreads = 20;

                    Thread[] threads = new Thread[ numThreads ];
                    for( int i = 0; i < numThreads; i++ )
                    {
                        threads[ i ] = new Thread( () => { Console.WriteLine( "This is from another thread." ); Thread.Sleep( 200 );  } );
                        threads[ i ].Start();
                    }

                    for( int i = 0; i < numThreads; i++ )
                    {
                        threads[ i ].Join();
                    }

                    return 0;
                }
            }, // end createManyThreads

            // Same as above but does not wait for the threads.
            { "createManyThreadsAsync", (args) =>
                {
                    int numThreads = 0;
                    if( (null != args) && (args.Length > 0) )
                    {
                        Int32.TryParse( args[ 0 ], out numThreads );
                    }

                    if( 0 == numThreads )
                        numThreads = 20;

                    Thread[] threads = new Thread[ numThreads ];
                    for( int i = 0; i < numThreads; i++ )
                    {
                        threads[ i ] = new Thread( () => { Console.WriteLine( "This is from another thread." ); Thread.Sleep( 2000 );  } );
                        threads[ i ].Start();
                    }

                    return 0;
                }
            }, // end createManyThreads


            { "createChildProcess", (args) =>
                {
                    StringBuilder sb = new StringBuilder();
                    if( null != args )
                    {
                        for( int i = 0; i < args.Length; i++ )
                        {
                            sb.Append( ' ' );
                            if( 0 == String.Compare( "`;", args[ i ], StringComparison.Ordinal ) )
                                sb.Append( ';' );
                            else
                                sb.Append( args[ i ] );
                        }
                    }

                    using( Process p = new Process() )
                    {
                        p.StartInfo = new ProcessStartInfo( Assembly.GetExecutingAssembly().Location, sb.ToString() );
                        p.StartInfo.UseShellExecute = false;
                        p.Start();
                        p.WaitForExit();
                        return p.ExitCode;
                    }
                }
            }, // end createChildProcess

        };
    } // end class Program
}
