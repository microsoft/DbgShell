using System;
using System.IO;
using System.Reflection;
using System.Management.Automation;

namespace MS.Dbg
{
    /// <summary>
    ///    Helps you save the context created by a ScriptBlock.
    /// </summary>
    internal class PsContextHelper : IDisposable
    {
        private ScriptBlock m_scriptBlock;
        private IDisposable m_ctxMarker;
        private PsContext m_withContext;
        private PSVariable m_savedContext;

        public ScriptBlock AdjustedScriptBlock { get { return m_scriptBlock; } }

        public PsContext WithContext { get { return m_withContext; } }

        public PsContext SavedContext
        {
            get
            {
                if( null == m_savedContext )
                    return null;

                return m_savedContext.Value as PsContext;
            }
        }

        private const string c_SavedContextVarName = "__tmp_savedContext";

        public PsContextHelper( ScriptBlock scriptBlock,
                                PsContext withContext,
                                bool saveContext )
        {
            if( null == scriptBlock )
                throw new ArgumentNullException( "scriptBlock" );

            m_withContext = withContext;

            if( !saveContext )
            {
                m_scriptBlock = scriptBlock;
                return;
            }

            if( null == m_withContext )
                m_withContext = new PsContext();

            m_savedContext = new PSVariable( c_SavedContextVarName, null );
            m_withContext.Vars[ m_savedContext.Name ] = m_savedContext;

            m_ctxMarker = DbgProvider.SetContextStartMarker();

            // Hacking the scriptBlock and smuggling the context out like this is
            // pretty ugly. I don't know a better way, though--I'm open to
            // suggestions.
            m_scriptBlock = ScriptBlock.Create( Util.Sprintf( @"{0}
. ""{1}\\GetPsContextFunc.ps1""
${2} = Get-PsContext",
                                                              scriptBlock.ToString(),
                                                              DbgProvider.PsScriptRoot,
                                                              c_SavedContextVarName ) );
        } // end constructor


        public void Dispose()
        {
            if( null != m_ctxMarker )
            {
                m_ctxMarker.Dispose();
                m_ctxMarker = null;

                if( null != m_withContext )
                    m_withContext.Vars.Remove( c_SavedContextVarName );

                if( null != m_savedContext )
                {
                    var savedCtx = m_savedContext.Value as PsContext;
                    savedCtx.Vars.Remove( c_SavedContextVarName );
                    // I don't think it makes to save these:
                    savedCtx.Vars.Remove( "_" );
                    savedCtx.Vars.Remove( "PSItem" );
                }
            }
        }
    } // end class PsContextHelper
}

