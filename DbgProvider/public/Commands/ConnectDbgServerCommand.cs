using System;
using System.Diagnostics;
using System.Globalization;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommunications.Connect, "DbgServer" )]
    public class ConnectDbgServerCommand : ExecutionBaseCommand
    {
        private const string c_NamedPipeServerParamSet     = "NamedPipeServerParamSet";
        private const string c_SslPipeServerParamSet       = "SslPipeServerParamSet";
        private const string c_TcpServerParamSet           = "TcpServerParamSet";
        private const string c_ReverseTcpServerParamSet    = "ReverseTcpServerParamSet";
        private const string c_SslTcpServerParamSet        = "SslTcpServerParamSet";
        private const string c_SslReverseTcpServerParamSet = "SslReverseTcpServerParamSet";
        private const string c_ComPortServerParamSet       = "ComPortServerParamSet";


        //
        // Named Pipe parameters
        //
        [Parameter( Mandatory = true, Position = 0, ParameterSetName = c_NamedPipeServerParamSet )]
        [ValidateNotNullOrEmpty]
        public string NamedPipeServer { get; set; }

        [Parameter( Mandatory = true, Position = 1, ParameterSetName = c_NamedPipeServerParamSet )]
        [Parameter( Mandatory = true, Position = 1, ParameterSetName = c_SslPipeServerParamSet )]
        [ValidateNotNullOrEmpty]
        public string PipeName { get; set; }


        //
        // TCP parameters
        //
        [Parameter( Mandatory = true, Position = 0, ParameterSetName = c_TcpServerParamSet )]
        [ValidateNotNullOrEmpty]
        public string TcpServer { get; set; }

        [Parameter( Mandatory = true, Position = 1, ParameterSetName = c_TcpServerParamSet )]
        [Parameter( Mandatory = true, Position = 1, ParameterSetName = c_ReverseTcpServerParamSet )]
        [Parameter( Mandatory = true, Position = 1, ParameterSetName = c_SslTcpServerParamSet )]
        [Parameter( Mandatory = true, Position = 1, ParameterSetName = c_SslReverseTcpServerParamSet )]
        [Parameter( Mandatory = true, Position = 1, ParameterSetName = c_ComPortServerParamSet )]
        public int Port { get; set; }

        [Parameter( ParameterSetName = c_TcpServerParamSet )]
        [Parameter( ParameterSetName = c_ReverseTcpServerParamSet )]
        public SwitchParameter IpVersion6 { get; set; }


        //
        // Reverse TCP (clicon) parameters
        //
        [Parameter( Mandatory = true, Position = 0, ParameterSetName = c_ReverseTcpServerParamSet )]
        [ValidateNotNullOrEmpty]
        public string ReverseTcpServer { get; set; }


        //
        // COM parameters
        //
        [Parameter( Mandatory = true, Position = 0, ParameterSetName = c_ComPortServerParamSet )]
        [ValidateNotNullOrEmpty]
        public string ComPortServer { get; set; }

        [Parameter( Mandatory = true, Position = 1, ParameterSetName = c_ComPortServerParamSet )]
        public int BaudRate { get; set; }

        [Parameter( Mandatory = true, Position = 2, ParameterSetName = c_ComPortServerParamSet )]
        public int Channel { get; set; }


        //
        // Ssl Pipe (spipe) parameters
        //
        [Parameter( Mandatory = true, Position = 0, ParameterSetName = c_SslPipeServerParamSet )]
        [ValidateNotNullOrEmpty]
        public string SslPipeServer { get; set; }

        [Parameter( Mandatory = true, Position = 1, ParameterSetName = c_SslPipeServerParamSet )]
        [Parameter( Mandatory = true, Position = 1, ParameterSetName = c_SslTcpServerParamSet )]
        [Parameter( Mandatory = true, Position = 1, ParameterSetName = c_SslReverseTcpServerParamSet )]
        [ValidateNotNullOrEmpty]
        public string Protocol { get; set; }

        [Parameter( ParameterSetName = c_SslPipeServerParamSet )]
        [Parameter( ParameterSetName = c_SslTcpServerParamSet )]
        [Parameter( ParameterSetName = c_SslReverseTcpServerParamSet )]
        [ValidateNotNullOrEmpty]
        public string CertUser { get; set; }

        //
        // Ssl TCP (ssl) parameters
        //
        [Parameter( Mandatory = true, Position = 0, ParameterSetName = c_SslTcpServerParamSet )]
        [ValidateNotNullOrEmpty]
        public string SslTcpServer { get; set; }


        //
        // Ssl Reverse TCP (ssl, clicon) parameters
        //
        [Parameter( Mandatory = true, Position = 0, ParameterSetName = c_SslReverseTcpServerParamSet )]
        [ValidateNotNullOrEmpty]
        public string SslReverseTcpServer { get; set; }


        //
        // Common parameters
        //
        [Parameter( Mandatory = false )]
        [ValidateNotNull]
        public SecureString Password { get; set; }

        // The name we should use in the root of the Dbg:\ drive to represent this target.
        [Parameter( Mandatory = false, Position = 1  )]
        [ValidateNotNullOrEmpty]
        public string TargetName { get; set; }



        private string _GenerateTargetName()
        {
            var sb = new StringBuilder( 224 );
            if( 0 == Util.Strcmp_OI( c_NamedPipeServerParamSet          , this.ParameterSetName ) )
            {
                sb.Append( NamedPipeServer )
                  .Append( "_" )
                  .Append( PipeName );
            }
            else if( 0 == Util.Strcmp_OI( c_TcpServerParamSet           , this.ParameterSetName ) )
            {
                sb.Append( TcpServer )
                  .Append( "_" )
                  .Append( Port.ToString( CultureInfo.InvariantCulture ) );
            }
            else if( 0 == Util.Strcmp_OI( c_ReverseTcpServerParamSet    , this.ParameterSetName ) )
            {
                sb.Append( "r_" ) // is this useful?
                  .Append( ReverseTcpServer )
                  .Append( "_" )
                  .Append( Port.ToString( CultureInfo.InvariantCulture ) );
            }
            else if( 0 == Util.Strcmp_OI( c_ComPortServerParamSet       , this.ParameterSetName ) )
            {
                sb.Append( "com" )
                  .Append( Port.ToString( CultureInfo.InvariantCulture ) )
                  .Append( "_channel_" )
                  .Append( Channel.ToString( CultureInfo.InvariantCulture ) );
            }
            else if( 0 == Util.Strcmp_OI( c_SslPipeServerParamSet       , this.ParameterSetName ) )
            {
                sb.Append( "s_" ) // is this useful?
                  .Append( SslPipeServer )
                  .Append( "_" )
                  .Append( PipeName );
            }
            else if( 0 == Util.Strcmp_OI( c_SslTcpServerParamSet        , this.ParameterSetName ) )
            {
                sb.Append( "s_" ) // is this useful?
                  .Append( SslTcpServer )
                  .Append( "_" )
                  .Append( Port.ToString( CultureInfo.InvariantCulture ) );

                if( !String.IsNullOrEmpty( CertUser ) )
                    sb.Append( ",certuser=" ).Append( CertUser );
            }
            else if( 0 == Util.Strcmp_OI( c_SslReverseTcpServerParamSet , this.ParameterSetName ) )
            {
                sb.Append( "rs_" ) // is this useful?
                  .Append( SslReverseTcpServer )
                  .Append( "_" )
                  .Append( Port.ToString( CultureInfo.InvariantCulture ) );
            }

            return sb.ToString();
        } // end _GenerateTargetName()


        private static string _DecryptSecureString( SecureString ss )
        {
            if( null == ss )
                return null;

            if( ss.Length == 0 )
                return String.Empty;

            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToBSTR( ss );
                return Marshal.PtrToStringBSTR( ptr );
            }
            finally
            {
                if( ptr != IntPtr.Zero )
                    Marshal.ZeroFreeBSTR( ptr );
            }
        } // end _DecryptSecureString()


        private string _BuildRemoteParams()
        {
            var sb = new StringBuilder( 224 );
            if( 0 == Util.Strcmp_OI( c_NamedPipeServerParamSet          , this.ParameterSetName ) )
            {
                sb.Append( "npipe:server=" )
                  .Append( NamedPipeServer )
                  .Append( ",pipe=" )
                  .Append( PipeName );
            }
            else if( 0 == Util.Strcmp_OI( c_TcpServerParamSet           , this.ParameterSetName ) )
            {
                sb.Append( "tcp:server=" )
                  .Append( TcpServer )
                  .Append( ",port=" )
                  .Append( Port.ToString( CultureInfo.InvariantCulture ) );

                if( IpVersion6 )
                    sb.Append( ",ipversion=6" );
            }
            else if( 0 == Util.Strcmp_OI( c_ReverseTcpServerParamSet    , this.ParameterSetName ) )
            {
                sb.Append( "tcp:clicon=" )
                  .Append( ReverseTcpServer )
                  .Append( ",port=" )
                  .Append( Port.ToString( CultureInfo.InvariantCulture ) );

                if( IpVersion6 )
                    sb.Append( ",ipversion=6" );
            }
            else if( 0 == Util.Strcmp_OI( c_ComPortServerParamSet       , this.ParameterSetName ) )
            {
                sb.Append( "com:port=" )
                  .Append( Port.ToString( CultureInfo.InvariantCulture ) )
                  .Append( ",baud=" )
                  .Append( BaudRate.ToString( CultureInfo.InvariantCulture ) )
                  .Append( Channel.ToString( CultureInfo.InvariantCulture ) );
            }
            else if( 0 == Util.Strcmp_OI( c_SslPipeServerParamSet       , this.ParameterSetName ) )
            {
                sb.Append( "spipe:proto=" )
                  .Append( Protocol )
                  .Append( ",server=" )
                  .Append( SslPipeServer )
                  .Append( ",pipe=" )
                  .Append( PipeName );

                if( !String.IsNullOrEmpty( CertUser ) )
                    sb.Append( ",certuser=" ).Append( CertUser );
            }
            else if( 0 == Util.Strcmp_OI( c_SslTcpServerParamSet        , this.ParameterSetName ) )
            {
                sb.Append( "ssl:proto=" )
                  .Append( Protocol )
                  .Append( ",server=" )
                  .Append( SslTcpServer )
                  .Append( ",port=" )
                  .Append( Port.ToString( CultureInfo.InvariantCulture ) );

                if( !String.IsNullOrEmpty( CertUser ) )
                    sb.Append( ",certuser=" ).Append( CertUser );
            }
            else if( 0 == Util.Strcmp_OI( c_SslReverseTcpServerParamSet , this.ParameterSetName ) )
            {
                sb.Append( "ssl:proto=" )
                  .Append( Protocol )
                  .Append( ",clicon=" )
                  .Append( SslReverseTcpServer )
                  .Append( ",port=" )
                  .Append( Port.ToString( CultureInfo.InvariantCulture ) );

                if( !String.IsNullOrEmpty( CertUser ) )
                    sb.Append( ",certuser=" ).Append( CertUser );
            }

            if( null != Password )
            {
                sb.Append( ",password=" ).Append( _DecryptSecureString( Password ) );
            }

            return sb.ToString();
        } // end _BuildRemoteParams()


        protected override void ProcessRecord()
        {
            if( String.IsNullOrEmpty( TargetName ) )
            {
                TargetName = _GenerateTargetName();
            }

            string remoteParams = _BuildRemoteParams();

            // TODO:
            if( null == Password )
                LogManager.Trace( "Connecting to remote with parameters: {0}", remoteParams );
            else
                LogManager.Trace( "Connecting to remote with parameters: <TODO need to take password out>" );

            using( var psPipe = GetPipelineCallback() )
            {
                Debugger = DbgEngDebugger.ConnectToServer( remoteParams, psPipe );

                // TODO: This is a temp workaround to pre-fetch filter info. The
                // problem is that when connected to a remote debugger, we're going
                // over RPC, and when the event callbacks are being dispatched, RPC
                // doesn't like us to try to call back in to the dbgeng API--and one
                // place where we normally do that is to decide if we need to output
                // an event or not. So we'll pre-fetch here.
                Debugger.GetSpecificEventFilters();

                // This will call Debugger.WaitForEvent(), if necessary
                base.ProcessRecord();
            } // end using( psPipe )
        } // end ProcessRecord()
    } // end class ConnectDbgServerCommand
}

