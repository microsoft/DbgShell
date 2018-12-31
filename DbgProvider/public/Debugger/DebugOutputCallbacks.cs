using System;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interop;
using DbgEngWrapper;

namespace MS.Dbg
{
    public partial class DbgEngDebugger : DebuggerObject
    {
        // TODO: Implement IDebugOutputCallbacks2 instead.
        private sealed class DebugOutputCallbacks : IDebugOutputCallbacksImp
        {
            private string m_prefix;
            public string Prefix
            {
                get { return m_prefix; }
                set
                {
                    if( null == value )
                        value = String.Empty;

                    m_prefix = value;
                }
            } // end property Prefix

            private Action< string > m_ConsumeLine;
            public Action< string > ConsumeLine
            {
                get { return m_ConsumeLine; }
                set
                {
                    if( null == value )
                        throw new ArgumentNullException( "value" );

                    m_ConsumeLine = value;
                }
            } // end property ConsumeLine


            private CircularBuffer<string> m_recentDbgEngOutput = new CircularBuffer< string >( 4096 );

            public CircularBuffer<string> RecentDbgEngOutput
            {
                get { return m_recentDbgEngOutput; }
            }

            // DbgEng sends output in chunks smaller than a whole line, so need to buffer.
            private StringBuilder m_sb = new StringBuilder();

            public DebugOutputCallbacks( string prefix, Action< string > consumeLine )
            {
                if( null == prefix )
                    throw new ArgumentNullException( "prefix" );

                if( null == consumeLine )
                    throw new ArgumentNullException( "consumeLine" );

                m_prefix = prefix;
                m_ConsumeLine = consumeLine;
            } // end constructor

            // TODO: maybe I want this always turned on?? I had a case where an Ioctl returned S_OK,
            // but spewed some error text to the output; it woulda been nice to have seen that before
            // I debugged through it all.
            public int Output( DEBUG_OUTPUT Mask, string Text )
            {
                try
                {
                    Util.Assert( null != Text );
                    if( String.IsNullOrEmpty( Text ) )
                        return 0;

                    // TODO: powershell-ize, using Mask
                    for( int i = 0; i < Text.Length; i++ )
                    {
                        if( 0 == m_sb.Length )
                            m_sb.Append( m_prefix );

                        if( '\r' == Text[ i ] )
                            continue;

                        if( '\n' == Text[ i ] )
                        {
                            Util.Assert( m_sb.Length >= m_prefix.Length );
                            var line = m_sb.ToString();
                            m_sb.Clear();
                            m_recentDbgEngOutput.Add( line );
                            m_ConsumeLine( line );
                        }
                        else
                        {
                            m_sb.Append( Text[ i ] );
                        }
                    } // end foreach( char )
                }
                catch( Exception e )
                {
                    Util.FailFast( "Unexpected exception in output callback", e );
                }
                return 0;
            } // end Output()

            public void Flush()
            {
                if( 0 != m_sb.Length )
                {
                    m_ConsumeLine( m_sb.ToString() );
                    m_sb.Clear();
                }
            }
        } // end class DebugOutputCallbacks
    } // end class DbgEngDebugger
}

