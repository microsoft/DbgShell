using System;
using System.Management.Automation;

namespace MS.Dbg
{
    internal interface IActionQueue
    {
        void PostCommand( Action< CommandInvocationIntrinsics > action );
    } // end interface IActionQueue
}
