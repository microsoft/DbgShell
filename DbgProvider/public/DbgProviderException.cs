//-----------------------------------------------------------------------
// <copyright file="DbgProviderException.cs" company="Microsoft">
//    Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Globalization;
using System.Management.Automation;
using System.Runtime.Serialization;
using System.Text;

namespace MS.Dbg
{
    // Justification: We do have the required constructors for proper serialization
    // support. We have ommitted constructors that would allow the ErrorRecord property to
    // be either null or worthlessly vague.
    [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors" )]
    [Serializable]
    public class DbgProviderException : Exception, IContainsErrorRecord, IMulticultural< string >
    {
        internal const string DpeKey = "DbgProviderException";
        internal const string SourcePluginKey = "SourcePluginKey";

        public ErrorRecord ErrorRecord { get; protected set; }

        internal MulticulturalString McsMessage { get; private set; }


        public DbgProviderException( ErrorRecord er ) : this( Resources.ErrMsgSeeErrorRecord, er )
        {
        }

        private void _PreserveThisInExceptionData( ErrorRecord er )
        {
            // When using an existing ErrorRecord (which contains some other exception
            // besides this one), we'll want to preserve this exception somewhere, too,
            // because we may want to see the path it took through the stack.
            if( er.Exception.Data.Contains( DpeKey ) )
            {
                Util.Fail( "Should not happen." ); // Shouldn't be re-wrapping an ErrorRecord like this.
                LogManager.Trace( "Replacing DSE: {0}", er.Exception.Data[ DpeKey ] );
            }
            er.Exception.Data[ DpeKey ] = this;
        } // end _PreserveThisInExceptionData()


        public DbgProviderException( string message, ErrorRecord er )
            : base( message )
        {
            if( null == er )
                throw new ArgumentNullException( "er" );

            ErrorRecord = er;
            _PreserveThisInExceptionData( er );
        }

        internal DbgProviderException( MulticulturalString mcsMessage, ErrorRecord er )
            : this( mcsMessage.ToString(), er )
        {
            McsMessage = mcsMessage;
        }

        public DbgProviderException( string message, Exception inner, ErrorRecord er )
            : base( message, inner )
        {
            if( null == er )
                throw new ArgumentNullException( "er" );

            ErrorRecord = er;
            _PreserveThisInExceptionData( er );
        }

        internal DbgProviderException( MulticulturalString mcsMessage, Exception inner, ErrorRecord er )
            : this( mcsMessage.ToString(), inner, er )
        {
            McsMessage = mcsMessage;
        }

        protected DbgProviderException( SerializationInfo info, StreamingContext context )
            : base( info, context )
        {
            if( null == info )
                throw new ArgumentNullException( "info" );

            ErrorRecord = (ErrorRecord) info.GetValue( "ErrorRecord", typeof( ErrorRecord ) );
            McsMessage = (MulticulturalString) info.GetValue( "McsMessage", typeof( MulticulturalString ) );
        }

        public DbgProviderException( string message, string errorId, ErrorCategory ec )
            : base( message )
        {
            ErrorRecord = new ErrorRecord( this, errorId, ec, null );
        }

        internal DbgProviderException( MulticulturalString mcsMessage, string errorId, ErrorCategory ec )
            : this( mcsMessage.ToString(), errorId, ec )
        {
            McsMessage = mcsMessage;
        }

        public DbgProviderException( string message, string errorId, ErrorCategory ec, Exception inner )
            : this( message, errorId, ec, inner, null )
        {
        }

        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter corresponds directly to that one.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        public DbgProviderException( string message, string errorId, ErrorCategory ec, Exception inner, object targetObject )
            : base( message, inner )
        {
            ErrorRecord = new ErrorRecord( this, errorId, ec, targetObject );
        }

        internal DbgProviderException( MulticulturalString mcsMessage, string errorId, ErrorCategory ec, Exception inner )
            : this( mcsMessage, errorId, ec, inner, null )
        {
        }

        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter corresponds directly to that one.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        internal DbgProviderException( MulticulturalString mcsMessage, string errorId, ErrorCategory ec, Exception inner, object targetObject )
            : this( mcsMessage.ToString(), errorId, ec, inner, targetObject )
        {
            McsMessage = mcsMessage;
        }


        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter corresponds directly to that one.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        public DbgProviderException( string message, string errorId, ErrorCategory ec, object targetObject )
            : base( message )
        {
            ErrorRecord = new ErrorRecord( this, errorId, ec, targetObject );
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        internal DbgProviderException( MulticulturalString mcsMessage, string errorId, ErrorCategory ec, object targetObject )
            : this( mcsMessage.ToString(), errorId, ec, targetObject )
        {
            McsMessage = mcsMessage;
        }


        internal DbgProviderException SetHResult( int hr )
        {
            HResult = hr;
            return this;
        }

        public override void GetObjectData( SerializationInfo info, StreamingContext context )
        {
            base.GetObjectData( info, context );

            if( null == info )
                throw new ArgumentNullException( "info" );

            info.AddValue( "ErrorRecord", ErrorRecord );
            info.AddValue( "McsMessage", McsMessage );
        }

        public string ToCulture( CultureInfo ci )
        {
            StringBuilder sb = new StringBuilder( Util.GetGenericTypeName( this ), 400 );

            string message;
            if( null != McsMessage )
            {
                message = McsMessage.ToString( ci );
            }
            else
            {
                // It's possible that /this/ object doesn't have an McsMessage, but some
                // inner exception does.
                message = Message;
            }

            if( !String.IsNullOrEmpty( message ) )
            {
                sb.Append( ": " );
                sb.Append( message );
            }

            if( null != InnerException )
            {
                sb.Append( " ---> " );
                IMulticultural< string > mcInner = InnerException as IMulticultural< string >;
                if( null != mcInner )
                {
                    sb.Append( mcInner.ToCulture( ci ) );
                }
                else
                {
                    sb.Append( InnerException.ToString() );
                }
                sb.Append( Environment.NewLine );
                sb.Append( "   --- " );
                sb.Append( Resources.ExMsgEndOfInnerExceptionStack.ToCulture( ci ) );
                sb.Append( " ---" );
            }
            string stackTrace = StackTrace;
            if( !String.IsNullOrEmpty( stackTrace ) )
            {
                sb.Append( Environment.NewLine );
                sb.Append( stackTrace );
            }
            return sb.ToString();
        }
    } // end class DbgProviderException
}
