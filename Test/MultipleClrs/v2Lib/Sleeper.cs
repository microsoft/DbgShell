using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace v2Lib
{
    [ComVisible( true )]
    [Guid( "14F82F54-9161-43C3-B212-E018988EC509" )]
    public class Sleeper
    {
        public void Sleep(int milliseconds)
        {
            Thread.Sleep(milliseconds);
        }
    }
}
