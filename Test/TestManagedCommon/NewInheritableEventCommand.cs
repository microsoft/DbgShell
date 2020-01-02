using System;
using System.ComponentModel;
using System.Management.Automation;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace MS.Dbg.Commands
{
    /// <summary>
    ///    Creates a new, anonymous manual reset event, and handle inheritance set.
    /// </summary>
    /// <remarks>
    ///    This is useful for coordinating with child process (such as our test apps).
    ///
    ///    Alternatively, I could provide a Set-HandleInformation cmdlet, and do the rest
    ///    in script... but I'd end up wrapping all that anyway.
    /// </remarks>
    [Cmdlet( VerbsCommon.New, "InheritableEvent" )]
    public class NewInheritableEventCommand : DbgBaseCommand
    {
        protected override bool TrySetDebuggerContext { get { return false; } }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            bool createdNew;

            var eventWaitHandle = new EventWaitHandle( false,
                                                       EventResetMode.ManualReset,
                                                       null,
                                                       out createdNew );

            bool itWorked = NativeMethods.SetHandleInformation( eventWaitHandle.SafeWaitHandle,
                                                                HandleFlag.Inherit,
                                                                HandleFlag.Inherit );
            if( !itWorked )
                throw new Win32Exception(); // uses last win32 error

            SafeWriteObject( eventWaitHandle );
        } // end ProcessRecord()
    } // end class NewInheritableEventCommand
}

