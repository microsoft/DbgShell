using System;
using System.Collections;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Provider;

namespace MS.Dbg
{
    public partial class DbgProvider : NavigationCmdletProvider, IContentCmdletProvider
    {
        public IContentReader GetContentReader( string path )
        {
            NsLeaf nsLeaf;
            if( !_TryGetContentItem( path, out nsLeaf ) )
                return null; // TODO: or wait... should this throw?

            return new NsContentReaderWriter( nsLeaf );
        }

        public IContentWriter GetContentWriter( string path )
        {
            NsLeaf nsLeaf;
            if( !_TryGetContentItem( path, out nsLeaf ) )
                return null; // TODO: or wait... should this throw?

            return new NsContentReaderWriter( nsLeaf );
        }

        public void ClearContent( string path )
        {
            NsLeaf nsLeaf;
            if( !_TryGetContentItem( path, out nsLeaf ) )
                return;

            nsLeaf.Truncate( 0 );
        } // end ClearContent()

        public object GetContentReaderDynamicParameters( string path )
        {
            return null;
        }

        public object GetContentWriterDynamicParameters( string path )
        {
            return null;
        }

        public object ClearContentDynamicParameters( string path )
        {
            return null;
        }

        private bool _TryGetContentItem( string path, out NsLeaf nsLeaf )
        {
            if( !_TryGetItemAs( path, out nsLeaf ) )
            {
                InvalidOperationException ioe = new InvalidOperationException( Util.Sprintf( "The item at '{0}' cannot contain content.",
                                                                                             path ) );
                try { throw ioe; } catch( InvalidOperationException ) { } // give it a stack
                WriteError( ioe, "NotAContentItem", ErrorCategory.InvalidType, path );
                return false;
            }
            return true;
        } // end _TryGetContentItem()

        private class NsContentReaderWriter : IContentReader, IContentWriter
        {
            private NsLeaf m_nsLeaf;
            private int m_pos;

            public NsContentReaderWriter( NsLeaf nsLeaf )
            {
                if( null == nsLeaf )
                    throw new ArgumentNullException( "nsLeaf" );

                m_nsLeaf = nsLeaf;
            } // end constructor

            public void Close()
            {
                Dispose();
            }

            public IList Read( long readCount )
            {
                var read = m_nsLeaf.Read( m_pos, readCount );
                if( null != read )
                    m_pos += read.Count;

                return read;
            } // end Read()

            public void Seek( long offset, SeekOrigin origin )
            {
                int oldPos = m_pos;
                int iOffset = (int) offset; // TOTEST: overflow
                switch( origin )
                {
                    case SeekOrigin.Begin:
                        m_pos = iOffset;
                        break;
                    case SeekOrigin.Current:
                        m_pos += iOffset;
                        break;
                    case SeekOrigin.End:
                        m_pos = m_nsLeaf.Size + iOffset;
                        break;
                }
                if( (m_pos < 0) || (m_pos > m_nsLeaf.Size) )
                {
                    m_pos = oldPos;
                    throw new ArgumentOutOfRangeException( "offset" );
                }
            } // end Seek()

            public IList Write( IList content )
            {
                var written = m_nsLeaf.Write( ref m_pos, content );
                if( null != written )
                    m_pos += written.Count;

                return written;
            } // end Write()

            public void Dispose()
            {
                m_nsLeaf = null;
            } // end Dispose()
        } // end class NsContentReaderWriter
    } // end class DbgProvider
}

