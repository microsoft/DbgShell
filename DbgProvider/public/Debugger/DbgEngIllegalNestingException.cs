//-----------------------------------------------------------------------
// <copyright file="DbgEngIllegalNestingException.cs" company="Microsoft">
//    Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Management.Automation;
using System.Runtime.Serialization;

namespace MS.Dbg
{
    // Justification: We do have the required constructors for proper serialization
    // support. We have ommitted constructors that would allow the ErrorRecord property to
    // be either null or worthlessly vague.
    [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors" )]
    [Serializable]
    // Known place where this can be thrown: trying to run ".kill" from within !dbgshell.
    public class DbgEngIllegalNestingException : DbgEngException
    {
        public DbgEngIllegalNestingException()
            : this( DebuggerObject.HR_ILLEGAL_NESTING,
                    "Certain commands cannot be run from with the !dbgshell extension command.",
                    "IllegalNesting",
                    ErrorCategory.ResourceBusy )
        {
        }


        public DbgEngIllegalNestingException( int hresult,
                                              string message,
                                              string errorId,
                                              ErrorCategory errorCategory )
            : base( hresult,
                    message,
                    errorId,
                    errorCategory )
        {
        } // end constructor

    } // end class DbgEngIllegalNestingException
}
