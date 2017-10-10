//-----------------------------------------------------------------------
// <copyright file="DbgEngAlreadyRunningException.cs" company="Microsoft">
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
    public class DbgEngAlreadyRunningException : DbgEngException
    {
        public DbgEngAlreadyRunningException( int hresult )
            : this( hresult,
                    "The target is already running.",
                    "TargetAlreadyRunning",
                    ErrorCategory.ResourceBusy )
        {
        }


        public DbgEngAlreadyRunningException( int hresult, string message )
            : this( hresult,
                    message,
                    "TargetAlreadyRunning",
                    ErrorCategory.ResourceBusy )
        {
        }

        public DbgEngAlreadyRunningException( int hresult,
                                              string message,
                                              string errorId,
                                              ErrorCategory errorCategory )
            : base( hresult,
                    message,
                    errorId,
                    errorCategory )
        {
        } // end constructor

    } // end class DbgEngAlreadyRunningException
}
