//-----------------------------------------------------------------------
// <copyright file="DbgMemoryAccessException.cs" company="Microsoft">
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
    public class DbgMemoryAccessException : DbgEngException
    {
        public ulong Address { get; set; }

        protected DbgMemoryAccessException( SerializationInfo info, StreamingContext context )
            : base( info, context )
        {
            Address = (ulong) info.GetValue( "Address", typeof( ulong ) );
        }

        public override void GetObjectData( SerializationInfo info, StreamingContext context )
        {
            base.GetObjectData( info, context );

            if( null == info )
                throw new ArgumentNullException( "info" );

            info.AddValue( "Address", Address );
        }

        public DbgMemoryAccessException( ulong address, bool is32bit )
            : this( address,
                    Util.Sprintf( "Could not access memory: {0}",
                                  DbgProvider.FormatAddress( address, is32bit, true ) ) )
        {
        }

        public DbgMemoryAccessException( ulong address, string message )
            : this( address,
                    message,
                    "MemoryAccessFailure",
                    ErrorCategory.ReadError )
        {
        }

        public DbgMemoryAccessException( ulong address,
                                         string message,
                                         string errorId,
                                         ErrorCategory errorCategory )
            : base( DebuggerObject.HR_ERROR_READ_FAULT,
                    message,
                    errorId,
                    errorCategory,
                    null,
                    address )
        {
            Address = address;
        } // end constructor

    } // end class DbgMemoryAccessException
}
