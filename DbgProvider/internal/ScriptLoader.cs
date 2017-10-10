using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Provider;
using MS.Dbg.Commands;

namespace MS.Dbg
{

    /// <summary>
    ///    Encapsulates logic to keep track of a set of self-registering script files, and
    ///    how to re-run them.
    /// </summary>
    internal class ScriptLoader
    {
        // The set is used to prevent duplicates in m_sourceFiles.
        private HashSet< string > m_sourceFilesSet = new HashSet< string >( StringComparer.OrdinalIgnoreCase );
        private LinkedList< string > m_sourceFiles = new LinkedList< string >();


        /// <summary>
        ///    Adds a file to the set of files to be run when Reload is called. This
        ///    method is idempotent.
        /// </summary>
        /// <remarks>
        ///    This depends on the caller using the same string to represent the same file
        ///    (modulo case) (so, for instance, always using full path names would work).
        /// </remarks>
        public bool AddSourceFile( string sourceScript )
        {
            if( !String.IsNullOrEmpty( sourceScript ) &&
                m_sourceFilesSet.Add( sourceScript ) )
            {
                m_sourceFiles.AddLast( sourceScript );
                return true;
            }
            return false;
        } // end AddSourceFile()


        /// <summary>
        ///    Re-runs the list of registered source files, after clearing the internal
        ///    list of registered source files.
        /// </summary>
        /// <remarks>
        ///    It is assumed that when a script runs, it calls some command that causes
        ///    AddSourceFile to be called, thereby re-registering itself. If a script
        ///    produces an error, however, it is kept in the list of source files, so that
        ///    a user can fix the error and call this again without re-registering the
        ///    script.
        ///
        ///    If a script in appendScripts is already in the list, no change is made (the
        ///    script does not get added again to the end of the list, or moved). If a
        ///    script in prependScripts is already in the list, it gets moved from its
        ///    original position to the front.
        /// </remarks>
        public void Reload( CommandInvocationIntrinsics invokeCommand,
                            IList< string > appendScripts,
                            IList< string > prependScripts,
                            string dataDisplayName, // for verbose messages
                            IPipelineCallback pipe )
        {
            if( null == pipe )
                throw new ArgumentNullException( "pipe" );

            pipe.WriteVerbose( "Dumping old {0}...", dataDisplayName );
            m_DumpContent();

            if( null != appendScripts )
            {
                foreach( var newScript in appendScripts )
                {
                    // Unlike the prepend case, here we don't change the position of the
                    // script in the list if it's already in it.
                    if( (null != newScript) && m_sourceFilesSet.Add( newScript ) )
                    {
                        pipe.WriteVerbose( "Appending script: {0}", newScript );
                        m_sourceFiles.AddLast( newScript );
                    }
                }
            }

            if( null != prependScripts )
            {
                foreach( var newScript in prependScripts )
                {
                    if( null != newScript )
                    {
                        if( m_sourceFilesSet.Remove( newScript ) )
                        {
                            pipe.WriteVerbose( "Prepending (moving) script: {0}", newScript );
                            m_sourceFiles.Remove( newScript );
                        }
                        else
                        {
                            pipe.WriteVerbose( "Prepending script: {0}", newScript );
                        }

                        m_sourceFiles.AddFirst( newScript );
                    }
                }
            }

            // It's possible that there's conditional logic in the scripts that causes
            // different scripts to run, so we'd better clear out the existing source
            // files list (after making a copy).
            var oldSourceFiles = m_sourceFiles.ToArray();
            m_sourceFiles.Clear();
            m_sourceFilesSet.Clear();
            foreach( string sourceScript in oldSourceFiles )
            {
                try
                {
                    // If there are errors parsing the file, then it won't be run, and if
                    // it doesn't get run, something won't call AddSourceFile, and if it
                    // doesn't call AddSourceFile, we won't save the script file to run
                    // again next time someone calls Reload. So we'll save the script name
                    // in the "files to run" /now/, instead of later.
                    pipe.WriteVerbose( "Adding {0} from: {1}", dataDisplayName, sourceScript );
                    if( AddSourceFile( sourceScript ) )
                    {
                        ScriptBlockAst ast = InvokeScriptCommand.LoadScript( sourceScript );
                        // TODO: should I do anything with any output?
                        InvokeScriptCommand.InvokeScript( invokeCommand, ast.GetScriptBlock(), true );
                    }
                    else
                    {
                        // I don't think it should be possible to get here.
                        Util.Fail( "How did we get here?" );
                    }
                }
                catch( DbgProviderException dpe )
                {
                    pipe.WriteError( dpe.ErrorRecord );
                }
                catch( RuntimeException re )
                {
                    pipe.WriteError( re.ErrorRecord );
                }
            }
        } // end Reload()


        public void SetFileList( IEnumerable< string > files )
        {
            m_sourceFiles.Clear();
            m_sourceFilesSet.Clear();
            foreach( string file in files )
            {
                if( m_sourceFilesSet.Add( file ) )
                    m_sourceFiles.AddLast( file );
            }
        } // end SetFileList();


        private Action m_DumpContent;

        public ScriptLoader( Action dumpContent )
        {
            if( null == dumpContent )
                throw new ArgumentNullException( "dumpContent" );

            m_DumpContent = dumpContent;
        } // end constructor
    } // end class ScriptLoader
}

