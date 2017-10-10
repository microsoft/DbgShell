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
    public class DbgEngException : DbgProviderException
    {
        protected DbgEngException( SerializationInfo info, StreamingContext context )
            : base( info, context )
        {
        }

        public DbgEngException( int hresult )
            : this( hresult,
                    Util.Sprintf( "DbgEng API returned {0}.", Util.FormatErrorCode( hresult ) ) )
        {
        }

        public DbgEngException( int hresult, string message )
            : this( hresult,
                    message,
                    "DbgEngApiError",
                    ErrorCategory.NotSpecified,
                    null,
                    null )
        {
        }

        public DbgEngException( int hresult,
                                string message,
                                string errorId,
                                ErrorCategory errorCategory )
            : this( hresult,
                    message,
                    errorId,
                    errorCategory,
                    null,
                    null )
        {
        }

        public DbgEngException( int hresult,
                                string message,
                                string errorId,
                                ErrorCategory errorCategory,
                                Exception innerException )
            : this( hresult,
                    message,
                    errorId,
                    errorCategory,
                    innerException,
                    null )
        {
        }

        public DbgEngException( int hresult,
                                string message,
                                string errorId,
                                ErrorCategory errorCategory,
                                object targetObject )
            : this( hresult,
                    message,
                    errorId,
                    errorCategory,
                    null,
                    targetObject )
        {
        }

        public DbgEngException( int hresult,
                                string message,
                                string errorId,
                                ErrorCategory errorCategory,
                                Exception innerException,
                                object targetObject )
            : base( message,
                    errorId,
                    errorCategory,
                    innerException,
                    targetObject )
                    
        {
            HResult = hresult;
        } // end constructor
    } // end class DbgEngException
}
