using System;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace v4Lib
{
    [ComVisible( true )]
    [Guid( "A3819EE3-8AB4-441C-BBCF-54F15CAA3565" )]
    public class Sleeper
    {
        public void Sleep(int milliseconds)
        {
            Thread.Sleep(milliseconds);
        }

     // public void SleepThroughv2(int milliseconds)
     // {
     //     v2Lib.Sleeper sleeper = new v2Lib.Sleeper();
     //     sleeper.Sleep(milliseconds);
     // }

        public void SleepThroughv2(int milliseconds)
        {
            try
            {
                string v2LibPath = Path.Combine( Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location ),
                                                 "v2Lib.dll" );

             // var objHandle = Activator.CreateComInstanceFrom( v2LibPath, "v2Lib.Sleeper" );
             // Console.WriteLine( "Got objHandle. Null? {0}", null == objHandle );

             // dynamic dObj = (dynamic) objHandle.Unwrap();

                using( var actCtx = new ActivationContext( v2LibPath, 2 ) )
                using( actCtx.Activate() )
                {
                    dynamic dObj;
                    var clsid = Guid.Parse( "14F82F54-9161-43C3-B212-E018988EC509" );
                    var iid = Guid.Parse( "00000000-0000-0000-C000-000000000046" ); // IUnknown
                    int hr = NativeMethods.CallCoCreateInstance( clsid,
                                                                 null,
                                                                 1,
                                                                 iid,
                                                                 out dObj );

                    Console.WriteLine( "CallCoCreateInstance returned: {0}", hr );

                    if( (0 == hr) && (null != dObj) )
                    {
                        Console.WriteLine( "Now sleeping through v2..." );
                        dObj.Sleep( milliseconds );
                    }
                } // end using( actCtx, activation )
            }
            catch( Exception e )
            {
                Console.WriteLine( "Error: {0}", e );
            }
        }
    }
}
