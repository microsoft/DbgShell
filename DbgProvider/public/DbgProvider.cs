using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    // We don't add ProviderCapabilities.ExpandWildcards here because that means we have
    // to do the wildcard expansion... but PowerShell should be able to do it itself.
    [CmdletProvider( DbgProvider.ProviderId, ProviderCapabilities.ShouldProcess )]
    public partial class DbgProvider : NavigationCmdletProvider, IContentCmdletProvider
    {
        public const string ProviderId = "Debugger";

        // For some reason, PowerShell feels it must create multiple instances of the
        // provider... annoying. For fun, I'll keep track of how many instances get
        // created.
        private static int sm_id;

        // Another consequence of PS not using a single provider instance is that I can't
        // store state in this object. I can hang drive-specific state off of drives
        // (which can be accessed via ProviderInfo.Drives). For runspace/session-specific
        // state, I'll have to have a static dictionary of settings objects, indexed by
        // runspace instance Id. Windows 8 Bugs 929420.

        public DbgProvider()
        {
            sm_id++;
            //Console.WriteLine( "Constructing instance {0}.", sm_id );
        } // end constructor


        // TODO: rationalize this and the LogManager stuff.
        [Conditional( "DEBUG" )]
        private void _WriteDebugFmt( string msgFmt, params object[] inserts )
        {
   // The following code is extremely slow... don't turn it on if you want any
   // performance.
   //       string msg = Util.Sprintf( msgFmt, inserts );
   //       // The problem with this is that it doesn't work everywhere--for instance, you
   //       // can't call it from the constructor, and you won't see output from it during
   //       // tab completion.
   //       //WriteDebug( msg );
   //       //Console.WriteLine( msg );
   //       TraceSource.TraceInformation( "DbgProvider._WriteDebugFmt: {0}", msg );

            // This is no better.
            //LogManager.Trace( msgFmt, inserts );
        } // end WriteDebug


        public new void WriteError( ErrorRecord er )
        {
            WriteError( er, false );
        } // end WriteError()


        internal void WriteError( ErrorRecord er, bool alreadyLogged )
        {
            if( !alreadyLogged )
                LogError( er );

            base.WriteError( er );
        } // end WriteError()


        public new void WriteVerbose( string verboseText )
        {
            WriteVerbose( verboseText, false );
        }

        internal void WriteVerbose( string verboseText, bool alreadyLogged )
        {
            if( !alreadyLogged )
                LogManager.Trace( "WriteVerbose: {0}", verboseText );

            base.WriteVerbose( verboseText );
        }

        internal void WriteVerbose( MulticulturalString verboseText )
        {
            LogManager.Trace( "WriteVerbose: {0}", verboseText );

            WriteVerbose( verboseText, true );
        }

        public new void WriteWarning( string warningText )
        {
            WriteWarning( warningText, false );
        }

        internal void WriteWarning( string warningText, bool alreadyLogged )
        {
            if( !alreadyLogged )
                LogManager.Trace( "WriteWarning: {0}", warningText );

            base.WriteWarning( warningText );
        }

        internal void WriteWarning( MulticulturalString warningText )
        {
            LogManager.Trace( "WriteWarning: {0}", warningText );

            WriteWarning( warningText, true );
        }

        public new void WriteDebug( string debugText )
        {
            WriteDebug( debugText, false );
        }

        internal void WriteDebug( string debugText, bool alreadyLogged )
        {
            if( !alreadyLogged )
                LogManager.Trace( "WriteDebug: {0}", debugText );

            base.WriteDebug( debugText );
        }

        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        protected void WriteError( Exception exception, string errorId, ErrorCategory errorCategory, object targetObject )
        {
            WriteError( new ErrorRecord( exception, errorId, errorCategory, targetObject ) );
        }

        protected void WriteError( DbgProviderException tpe )
        {
            WriteError( tpe.ErrorRecord );
        }

        protected new void ThrowTerminatingError( ErrorRecord er )
        {
            LogManager.Trace( "ThrowTerminatingError1: {0}", er.ErrorDetails == null ? "(no details)" : er.ErrorDetails.Message );
            LogManager.Trace( "ThrowTerminatingError2: {0}", er.CategoryInfo == null ? "(no category info)" : er.CategoryInfo.ToString() );
            LogManager.Trace( "ThrowTerminatingError3: {0}", er.FullyQualifiedErrorId );
            LogManager.Trace( "ThrowTerminatingError4: {0}", er.Exception );

            base.ThrowTerminatingError( er );
        }


        // Justification: The PowerShell ErrorRecord constructor parameter is called
        // "targetObject", and this parameter maps directly to that parameter.
        [System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames" )]
        protected void ThrowTerminatingError( Exception ex, string errorId, ErrorCategory ec, object targetObject )
        {
            ThrowTerminatingError( new ErrorRecord( ex, errorId, ec, targetObject ) );
        }

        protected void ThrowTerminatingError( DbgProviderException tpe )
        {
            ThrowTerminatingError( tpe.ErrorRecord );
        }


        public static void AddListener( TraceListener listener )
        {
            RunspaceSettings rs = RunspaceSettings.Singleton;
            rs.TraceSource.Listeners.Add( listener );
        }


        public static PathBugginessStyle GetPathBugginessStyle()
        {
            RunspaceSettings rs = RunspaceSettings.Singleton;
            return rs.PathBugginessStyle;
        } // end GetPathBugginessStyle()

        public static void SetPathBugginessStyle( PathBugginessStyle pbs )
        {
            RunspaceSettings rs = RunspaceSettings.Singleton;
            rs.PathBugginessStyle = pbs;
        } // end SetPathBugginessStyle()


        public static void PushNamespace()
        {
            RunspaceSettings rs = RunspaceSettings.Singleton;
            rs.PushNamespace();
        } // end PushNamespace()

        public static void PopNamespace()
        {
            RunspaceSettings rs = RunspaceSettings.Singleton;
            rs.PopNamespace();
        } // end PopNamespace()


        protected override ProviderInfo Start( ProviderInfo providerInfo )
        {
            var rs = new RunspaceSettings( Runspace.DefaultRunspace.InstanceId,
                                           providerInfo,
                                           SessionState );
            return base.Start( rs );
        }

        protected override void Stop()
        {
            RunspaceSettings.Remove( RsSettings.RunspaceId );
            base.Stop();
        }

        private RunspaceSettings m_rsSettings;
        private RunspaceSettings RsSettings
        {
            get
            {
                if( null == m_rsSettings )
                {
                 // m_rsSettings = RunspaceSettings.GetSettingsForProvider(
                 //                     this,
                 //                     this.ProviderInfo,
                 //                     (rid_, pi_, ss_ ) => new RunspaceSettings( rid_, pi_, ss_ ) );
                    m_rsSettings = RunspaceSettings.Singleton;
                }
                return m_rsSettings;
            }
        }

        private TraceSource TraceSource { get { return RsSettings.TraceSource; } }

        private DbgDriveInfoBase< T > _TryFindDriveByRoot< T >( string root ) where T : NamespaceItem
        {
            foreach( var drive in this.ProviderInfo.Drives )
            {
                if( 0 == Util.Strcmp_OI( root, drive.Root ) )
                {
                    return (DbgDriveInfoBase< T >) drive;
                }
            }
            return null;
        } // end _TryFindDriveByRoot()


        private bool _TryParsePathRoot( string path, out string minusRoot, out NsRoot nsRoot )
        {
            bool retVal = false;
            DbgRootDriveInfo drive;
            minusRoot = null;
            nsRoot = null;

            // SPECIAL CASE for the root of the namespace:
            if( !path.StartsWith( "\\", StringComparison.Ordinal ) &&
                !path.StartsWith( ".", StringComparison.Ordinal ) )
            {
                drive = (DbgRootDriveInfo) _TryFindDriveByRoot< NsRoot >( String.Empty );
                if( null != drive )
                {
                    retVal = true;
                    minusRoot = path;
                    nsRoot = drive.NsItem;
                }
                _WriteDebugFmt( "_TryParsePathRoot SPECIAL( '{0}' ) = {1}", path, retVal );
                return retVal;
            }

            string car, cdr;
            Util.SplitPath( path, out car, out cdr );

            drive = (DbgRootDriveInfo) _TryFindDriveByRoot< NsRoot >( car );
            if( null != drive )
            {
                retVal = true;
                minusRoot = cdr;
                nsRoot = drive.NsItem;
            }
            _WriteDebugFmt( "_TryParsePathRoot( '{0}' ) = {1}", path, retVal );
            return retVal;
        } // end _TryParsePathRoot()


        protected override bool IsValidPath( string path )
        {
            if( null == path )
                throw new ArgumentNullException( "path" );

            bool retVal = false;
            NsRoot unused;
            string minusRoot;
            if( _TryParsePathRoot( path, out minusRoot, out unused ) )
                retVal = true;

            // Q: Are we supposed to see if the path exists, or if it's just syntactically
            //    valid, or what?
            // A: Just that it's syntactically valid.

            _WriteDebugFmt( "IsValidPath( '{0}' ) = {1}", path ?? "<null>", retVal );
            return retVal;
        } // end IsValidPath()


        // This just gets the leaf item of the path.
        protected override string GetChildName( string path )
        {
            if( String.IsNullOrEmpty( path ) )
                throw new ArgumentException( "You must supply a path.", "path" );

            // This call (to base.GetChildName) is ENORMOUSLY expensive, to the point of affecting
            // performance. I'm replacing it with something simpler...
            //string retVal = base.GetChildName( path );
            string retVal = null;
            string parent, leaf;
            Util.SplitPathLeaf( path, out parent, out leaf );
            retVal = leaf;

            _WriteDebugFmt( "GetChildName( '{0}' ) = '{1}'", path, retVal );
            return retVal;
        } // end GetChildName()


        protected override Collection<PSDriveInfo> InitializeDefaultDrives()
        {
            return RsSettings.InitializeDefaultDrives();
        } // end InitializeDefaultDrives()


        // For working around INT9f732f8e.
        private static string _TryUnescapePath( string path )
        {
            if( String.IsNullOrEmpty( path ) )
                return path;

            System.Text.StringBuilder sb = null;
            bool lastCharWasBacktick = false;
            for( int i = 0; i < path.Length; i++ )
            {
                char c = path[ i ];

                if( '`' == c )
                {
                    if( null == sb )
                    {
                        sb = new System.Text.StringBuilder( path.Length );
                        sb.Append( path, 0, i );
                    }
                    if( lastCharWasBacktick )
                    {
                        // We skipped the last backtick.
                        sb.Append( c );
                        lastCharWasBacktick = false;
                    }
                    else
                    {
                        // Skip it, but remember that we saw it.
                        lastCharWasBacktick = true;
                    }
                }
                else if( null != sb )
                {
                    lastCharWasBacktick = false;
                    sb.Append( c );
                }
            }
            if( null != sb )
                return sb.ToString();
            else
                return path;
        } // end _TryUnescapePath()


        protected override bool ItemExists( string path )
        {
            bool retVal = false;
            NamespaceItem unused;
            retVal = _TryGetItem( path, out unused );
            _WriteDebugFmt( "ItemExists( '{0}' ) = {1}", path ?? "<null>", retVal );
            return retVal;
        } // end ItemExists()


        private static bool _PathIsQuoted( string path )
        {
            return (path.Length >= 2) &&
                   (((path[ 0 ] == '\'')) && (path[ path.Length - 1 ] == '\'') ||
                    ((path[ 0 ] == '"')) && (path[ path.Length - 1 ] == '"'));
        }


        private bool _TryGetItem( string path, out NamespaceItem nsItem )
        {
            nsItem = null;
            if( null == path )
                throw new ArgumentNullException( "path" );

            bool found = __TryGetItemWorker( path, out nsItem, false );
            if( !found && _PathIsQuoted( path ) )
            {
                // This is a workaround for INTe316a361. I might be able to target the
                // workaround a little more tightly (only in dynamic parameter query
                // functions, like RemoveItemDynamicParameters), but I don't know if there
                // are other situations that PS will do this in, so I'll put it here to
                // try and catch them.
                LogManager.Trace( "Path was quoted. I'll try again without the quotes. Path: {0}", path );

                // Unquote:
                path = path.Substring( 1, path.Length - 2 );
                found = __TryGetItemWorker( path, out nsItem, true );
            }
            return found;
        } // end _TryGetItem()


        private bool __TryGetItemWorker( string path,
                                         out NamespaceItem nsItem,
                                         bool assertAboutRelativePaths )
        {
            nsItem = null;

            if( path.StartsWith( @".\" ) )
            {
                // This is known to happen when PS gives us a quoted path. See comment
                // about INTe316a361.
                if( !assertAboutRelativePaths )
                    Util.Fail( "We shouldn't ever get relative paths in here." );

                LogManager.Trace( "Fixing up relative path. Old path: {0}", path );
                path = Path.Combine( PSDriveInfo.CurrentLocation, path.Substring( 2 ) );
                LogManager.Trace( "                         New path: {0}", path );
            }

            // Workaround for INT9f732f8e.
            path = _TryUnescapePath( path );

            NsRoot nsRoot;
            if( _TryParsePathRoot( path, out path, out nsRoot ) )
            {
                return nsRoot.TryWalkTreeRelative( path, out nsItem );
            }
            return false;
        } // end __TryGetItemWorker()


        private bool _TryGetItemAs< T >( string path, out T nsT ) where T : NamespaceItem
        {
            NamespaceItem nsItem;
            if( !_TryGetItem( path, out nsItem ) )
            {
                Util.Fail( "I don't think PS will call this on a non-existent path." );
                throw new ArgumentException( Util.Sprintf( "Path does not exist: {0}", path ) );
            }

            nsT = nsItem as T;
            return null != nsT;
        } // end _TryGetItemAs< T >()

        protected override string NormalizeRelativePath( string path, string basePath )
        {
            string retVal = base.NormalizeRelativePath( path, basePath );
            // Let's attempt to fix casing.
            NamespaceItem nsItem;
            if( _TryGetItem( retVal, out nsItem ) )
            {
                retVal = nsItem.ComputePath( RsSettings.PathBugginessStyle == PathBugginessStyle.RegistryProvider );
            }
            _WriteDebugFmt( "NormalizeRelativePath( '{0}', '{1}' ) = '{2}'",
                        path ?? "<null>",
                        basePath ?? "<null>",
                        retVal ?? "<null>" );

            return retVal;
        } // end NormalizeRelativePath()


        protected override string MakePath( string parent, string child )
        {
            string retVal;

            if( (PathBugginessStyle.CertificateProvider == RsSettings.PathBugginessStyle) &&
                (0 == parent.Length) &&
                (child.Length > 0) && ('\\' != child[ 0 ]) )
            {
                retVal = parent + @"\" + child;
                _WriteDebugFmt( "SPECIAL MakePath case: {0}", retVal );
            }
            else
            {
                retVal = base.MakePath( parent, child );
            }

            _WriteDebugFmt( "MakePath( '{0}', '{1}' ) = '{2}'",
                        parent ?? "<null>",
                        child ?? "<null>",
                        retVal ?? "<null>" );
            return retVal;
        }

        protected override string GetParentPath( string path, string root )
        {
            string retVal = base.GetParentPath( path, root );

            _WriteDebugFmt( "GetParentPath( '{0}', '{1}' ) = '{2}'",
                        path ?? "<null>",
                        root ?? "<null>",
                        retVal ?? "<null>" );
            return retVal;
        }

        protected override bool IsItemContainer( string path )
        {
            if( null == path )
                throw new ArgumentNullException( "path" );

            bool retVal = false;

            NamespaceItem nsItem;
            if( _TryGetItem( path, out nsItem ) )
            {
                retVal = nsItem.IsContainer;
            }

            _WriteDebugFmt( "IsItemContainer( '{0}' ) = {1}", path ?? "<null>", retVal );
            return retVal;
        }

        protected override string[] ExpandPath( string path )
        {
            string[] retVal = base.ExpandPath( path );
            _WriteDebugFmt( "ExpandPath( '{0}' ) = '{1}'", path ?? "<null>", String.Join( ", ", retVal ) );
            Util.Fail( "I don't think this will be called unless I put the ExpandWildcards capability on." );
            return retVal;
        } // end ExpandPath()


        protected override bool HasChildItems( string path )
        {
            // Base implementation throws NotImplementedException. But you need it for
            // wildcard/tab expansion.
            bool retVal = false;
            NamespaceItem nsItem;
            if( _TryGetItem( path, out nsItem ) )
            {
                retVal = nsItem.HasChildren;
            }
            else
            {
                Util.Fail( "I don't think PS will call this on a non-existent item." );
                throw new ArgumentException( Util.Sprintf( "No such item: {0}", path ) );
            }
            _WriteDebugFmt( "HasChildItems( '{0}' ) = {1}", path, retVal );
            return retVal;
        } // end HasChildItems()


        // I think PS calls this during globbing to give the provider a chance to fool
        // around with the path/filter... but I shouldn't need to do anything.
        protected override bool ConvertPath( string path,
                                             string filter,
                                             ref string updatedPath,
                                             ref string updatedFilter )
        {
            string originalUpdatedPath = updatedPath;
            string originalUpdatedFilter = updatedFilter;
            bool retVal = base.ConvertPath( path, filter, ref updatedPath, ref updatedFilter );
            _WriteDebugFmt( "ConvertPath( '{0}', '{1}', '{2}', '{3}' ) = {4} ('{5}', '{6}')",
                            path,
                            filter,
                            originalUpdatedPath,
                            originalUpdatedFilter,
                            retVal,
                            updatedPath,
                            updatedFilter );
            return retVal;
        }


        protected override void GetChildItems( string path, bool recurse )
        {
            _WriteDebugFmt( "GetChildItems( '{0}', {1} )", path ?? "<null>", recurse );

            NamespaceItem nsContainer;
            if( !_TryGetItem( path, out nsContainer ) )
            {
                throw new DirectoryNotFoundException( String.Format( "The path '{0}' does not exist.", path ) );
            }

            Util.Assert( nsContainer.IsContainer );

         // if( nsContainer is Blah )
         // {
         //     var dynamicParams = (MyDynamicParamsType) DynamicParameters;
         //     do stuff based on dynamicParams...
         // }
         // else
         // {
                // TODO: handle 'recurse'
                foreach( NamespaceItem item in nsContainer.EnumerateChildItems() )
                {
                    WriteItemObject( DbgProviderItem.CreateDbgItem( item ), Path.Combine( path, item.Name ), item.IsContainer );
                }
         // }
        } // end GetChildItems()


        protected override object GetChildItemsDynamicParameters( string path, bool recurse )
        {
            _WriteDebugFmt( "GetChildItemsDynamicParameters( '{0}', {1} )", path ?? "<null>", recurse );

            NamespaceItem nsContainer;
            if( !_TryGetItem( path, out nsContainer ) )
            {
                // Just ignore it. If we were to throw here, it would cause things like
                // "dir b*", where there is nothing that starts with a 'b', to fail.
                return null;
            }

            // This is where you would put logic based on nsContainer...
            return null;
        } // end GetChildItemsDynamicParameters()


        protected override void GetChildNames( string path, ReturnContainers returnContainers )
        {
            _WriteDebugFmt( "GetChildNames( '{0}', {1} )", path, returnContainers );
            // Base implementation throws NotImplementedException. But you need it for
            // wildcard/tab expansion.
            // TODO: I have no idea what to do with returnContainers.
            NamespaceItem nsItem;
            if( _TryGetItem( path, out nsItem ) )
            {
                if( nsItem.IsContainer )
                {
                    foreach( NamespaceItem nsChild in nsItem.EnumerateChildItems() )
                    {
                        // YOUDNEVERKNOWTHIS: You must write just the item name, as a string; not the
                        // actual object, else you get nothing in tab completion / globbing.
                        WriteItemObject( nsChild.Name,
                                         nsChild.ComputePath( RsSettings.PathBugginessStyle == PathBugginessStyle.RegistryProvider ),
                                         nsChild.IsContainer );
                    }
                }
            }
        } // end GetChildNames()


        protected override void GetItem( string path )
        {
            _WriteDebugFmt( "GetItem( '{0}' )", path );
            NamespaceItem nsItem;
            if( _TryGetItem( path, out nsItem ) )
            {
                WriteItemObject( DbgProviderItem.CreateDbgItem( nsItem ), path, nsItem.IsContainer );
            }
        } // end GetItem()


        protected override void StopProcessing()
        {
            _WriteDebugFmt( "StopProcessing()" );
            base.StopProcessing();
        }


        protected override PSDriveInfo NewDrive( PSDriveInfo drive )
        {
            if( null == drive )
                throw new ArgumentNullException( "drive" );

            if( null == drive.Root )
                throw new ArgumentException( "The drive cannot have a null Root." );

            // Special case: For the root drive, it hasn't been created/added to our
            // drives collection yet--chicken and egg problem.
            if( (0 == drive.Root.Length) && (0 == this.ProviderInfo.Drives.Count) )
            {
                return drive;
            }

            NamespaceItem nsItem;
            if( !_TryGetItem( drive.Root, out nsItem ) )
            {
                throw new ArgumentException( Util.Sprintf( "The path '{0}' does not exist.", drive.Root ) );
            }

            if( !nsItem.IsContainer )
            {
                throw new ArgumentException( Util.Sprintf( "The path '{0}' is not a container.", drive.Root ) );
            }

            if( drive.Name.StartsWith( drive.Root, StringComparison.OrdinalIgnoreCase ) )
            {
                throw new ArgumentException( "Windows 8 Bugs 922001: A drive with this name won't work; sorry." );
            }

            return drive;
        } // end NewDrive()


        protected override PSDriveInfo RemoveDrive( PSDriveInfo drive )
        {
            if( null == drive )
                throw new ArgumentNullException( "drive" );

            if( drive == RsSettings.NsRoot.RootDrive )
            {
                TraceSource.TraceInformation( "Removing the root drive is not allowed." );
                // Actually, the drive could get removed anyway (drives can be forcibly
                // removed by passing true for "force" to SessionState.Drive.Remove). This
                // is important for our push-/pop-namespace commands.
                return null;
            }

            if( ShouldProcess( drive.Name ) )
                return drive;
            else
                return null;
        } // end RemoveDrive()


        // For when you are going to create an item.
        private void _FindParentOfProposedItem( string path, out NamespaceItem nsParent, out string leafName )
        {
            string minusRoot, parentPath, tmpLeafName;
            NsRoot nsRoot;
            if( !_TryParsePathRoot( path, out minusRoot, out nsRoot ) )
                throw new System.IO.DriveNotFoundException( Util.Sprintf( "Could not find path \"{0}\".", path ) );

            Util.SplitPathLeaf( minusRoot, out parentPath, out tmpLeafName );

            if( !nsRoot.TryWalkTreeRelative( parentPath, out nsParent ) )
                throw new DirectoryNotFoundException( Util.Sprintf( "Could not find container \"{0}\".", parentPath ) );

            leafName = tmpLeafName;
        } // end _FindParentOfProposedItem()


        protected override void NewItem( string path, string itemTypeName, object newItemValue )
        {
            _WriteDebugFmt( "NewItem( '{0}', '{1}', {2} )", path, itemTypeName, newItemValue );

            // PowerShell should have already verified that the path does not already
            // exist.

            NamespaceItem nsParent;
            string leafName;

            // TODO: could this throw? (could PS call us with a path that would cause us
            // to throw here? I think not, but should test it)
            _FindParentOfProposedItem( path, out nsParent, out leafName );

            if( !ShouldProcess( leafName ) ) // TODO: need better message
                return;

            NamespaceItem newItem = null;

            try
            {
                if( 0 == Util.Strcmp_OI( "directory", itemTypeName ) )
                {
                    newItem = NsContainer.CreateStronglyTyped( leafName, nsParent, newItemValue );
                }
                else
                {
                    newItem = NsLeaf.CreateNsLeaf( leafName, nsParent, newItemValue );
                }
            }
            catch( ItemExistsException iee )
            {
                WriteError( iee,
                            "ItemAlreadyExists",
                            ErrorCategory.ResourceExists,
                            nsParent.ComputePath( false ) + "\\" + leafName );
            }

            if( null != newItem )
            {
                WriteItemObject( DbgProviderItem.CreateDbgItem( newItem ), path, newItem.IsContainer );
            }
        } // end NewItem()


        private void _RemoveTarget( NsContainer< DbgTarget > nsTarget, bool detach )
        {
            nsTarget.Unlink();
            var target = nsTarget.RepresentedObject;
            var debugger = target.Debugger;
            using( new DbgEngContextSaver( debugger, target.Context ) )
            {
                Commands.DisconnectDbgProcessCommand._DoDisconnect( debugger,
                                                                    leaveSuspended: false,
                                                                    kill: !detach );
                Util.Await( debugger.WaitForEventAsync() );
            }

            debugger.UpdateNamespace();
        } // end _RemoveTarget()


        // TODO: I left the "path" parameter here in case I want to use it to set the
        // debugger's context.
        internal static DbgEngDebugger GetDebugger( string providerPath )
        {
            // We used to simulate lack of context by returning null from here.
            // Now we will throw instead when we try to set context.
            return DbgEngDebugger._GlobalDebugger;
        } // end GetDebugger()


        // TODO: or should it be public?
        /// <summary>
        ///    Returns a list of objects that are represented by the specified path.
        /// </summary>
        internal static List< DebuggerObject > GetDebuggerObjectsForPath( string providerPath )
        {
            // TODO: factoring with GetDebuggerForTargetByPath()?
            RunspaceSettings rs = RunspaceSettings.Singleton;

            List< NamespaceItem > nsPathItems = null;
            if( !rs.NsRoot.TryWalkTreeRelative( providerPath, ref nsPathItems ) )
            {
                return null; // TODO: or should I throw? probably throw...
            }

            List< DebuggerObject > dbgObjList = new List< DebuggerObject >( nsPathItems.Count );
            foreach( NamespaceItem pathItem in nsPathItems )
            {
                NsContainer nsc = pathItem as NsContainer;
                if( null == nsc )
                    continue;

                var dbgObj = nsc.RepresentedObject as DebuggerObject;
                if( null != dbgObj ) // some things might just be intermediate containers
                    dbgObjList.Add( dbgObj );
            }

            return dbgObjList;
        } // end GetDebuggerObjectsForPath()


        internal static NsContainer GetNsTargetByCurrentDebuggerContext( DbgEngDebugger debugger )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            var curCtx = debugger.GetCurrentDbgEngContext();
            return _GetTopLevelContainerForContext( curCtx );
        } // end GetNsTargetByCurrentDebuggerContext()


    //  //internal static NsContainer< DbgTarget > GetNsTargetForDebuggerContext( DbgEngContext ctx )
    //  internal static NsContainer GetNsTargetForDebuggerContext( DbgEngContext ctx )
    //  {
    //      RunspaceSettings rs = RunspaceSettings.Singleton;

    //      if( null == ctx )
    //          throw new ArgumentNullException( "ctx" );

    //      //NsContainer< DbgTarget > nsTarget = null;
    //      NsContainer nsTarget = null;
    //      foreach( var candidate in rs.NsRoot.EnumerateChildItems() )
    //      {
    //          //nsTarget = candidate as NsContainer< DbgTarget >;
    //          nsTarget = candidate as NsContainer;
    //          if( null == nsTarget )
    //              continue;

    //          DbgTarget t = nsTarget.RepresentedObject as DbgTarget;
    //          if( null == t )
    //              continue;

    //          if( (t.DbgEngSystemId != ctx.SystemIndex) )
    //              continue;

    //          if( ctx.IsKernelContext.HasValue && ctx.IsKernelContext.Value )
    //          {
    //              if( t.IsKernel )
    //                  return nsTarget;
    //              else
    //                  continue;
    //          }

    //          var proc = nsTarget.RepresentedObject as DbgUModeProcess;
    //          Util.Assert( null != proc );
    //          Util.Assert( ctx.ProcessIndexOrAddress == ((ulong) (uint) ctx.ProcessIndexOrAddress) );

    //          if( proc.DbgEngProcessId == (uint) ctx.ProcessIndexOrAddress )
    //          {
    //              return nsTarget;
    //          }
    //      }

    //      return null;
    //  } // end GetNsTargetByCurrentDebuggerContext()


        internal static void RefreshTargetByDebugger( DbgEngDebugger debugger )
        {
            NsContainer nsTarget = GetNsTargetByCurrentDebuggerContext( debugger );
            if( null == nsTarget )
            {
                // This can happen when the dbgeng context claims to have a "system", but
                // no processes in that system. (A context of [0,-1,-1,-1].)
                //
                // Update: actually, I updated DbgEngDebugger.NoTarget to account for that
                // scenario, so I think we shouldn't come this way in that case anymore.
                // But I feel okay about leaving this code here the way it is.
                LogManager.Trace( "RefreshTargetByDebugger: no target for current context." );
                return;
            }

            nsTarget.Refresh();
        } // end RefreshTargetByDebugger()


     // internal static string GetPathForCurrentContext( DbgEngDebugger debugger )
     // {
     //     NsContainer nsTarget = GetNsTargetByCurrentDebuggerContext( debugger );
     //     if( null == nsTarget )
     //         return null;

     //     if( ((DbgKModeTarget) nsTarget.RepresentedObject).IsKernel )
     //     {
     //         // Kernel-mode

     //         return nsTarget.ComputePath( false );


     //         // TODO
     //     }
     //     else
     //     {
     //         // User-mode

     //         //
     //         // Get the thread:
     //         //
     //         NamespaceItem nsThreadsContainer;
     //         if( !nsTarget.TryFindChild( "Threads", out nsThreadsContainer ) )
     //         {
     //             Util.Fail( "Can't find threads container!" );
     //             return null;
     //         }

     //         // We used to enumerate every single thread just to get the one we want here.
     //         // Instead, we'll create an [ephemeral] item directly, for just this thread.
     //         NsContainer< DbgUModeThreadInfo > nsThread = null;
     //         DbgUModeThreadInfo curThread = debugger.GetCurrentThread();

     //         nsThread = (NsContainer< DbgUModeThreadInfo >)
     //             NamespaceItem.CreateItemForObject( curThread,
     //                                                nsThreadsContainer,
     //                                                true ); // ephemeral

     //         if( null == nsThread )
     //             return nsTarget.ComputePath( false );

     //         //
     //         // Are we in a particular frame?
     //         //
     //         // TODO: it would be pretty bad to fail right here... what then?
     //         DbgStackFrameInfo frame = debugger.GetCurrentScopeFrame();

     //         if( 0 == frame.FrameNumber )
     //             return nsThread.ComputePath( false );

     //         NsContainer nsStack;
     //         if( !nsThread.TryFindChildAs( "Stack", out nsStack ) )
     //         {
     //             // TODO: it would be pretty bad to fail right here...
     //             throw new Exception( "couldn't find stack dir!" );
     //         }

     //         // Similar to the thread, we used to enumerate frames just to get the one we
     //         // want here. Instead, we'll create an [ephemeral] item directly, for just
     //         // this frame.

     //         NsContainer< DbgStackFrameInfo > nsFrame;
     //         nsFrame = (NsContainer< DbgStackFrameInfo >)
     //             NamespaceItem.CreateItemForObject( frame,
     //                                                nsStack,
     //                                                true ); // ephemeral

     //         return nsFrame.ComputePath( false );
     //     }
     // } // end GetPathForCurrentContext()


        internal static string GetPathForCurrentContext( DbgEngDebugger debugger )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            var ctx = debugger.GetCurrentDbgEngContext();

            NsContainer nsTarget = _GetTopLevelContainerForContext( ctx );

            if( null == nsTarget )
                return null;

            if( ctx.IsKernelContext.HasValue && ctx.IsKernelContext.Value )
            {
                // Kernel-mode

                if( ctx.ProcessIndexOrAddress == DebuggerObject.DEBUG_ANY_ID )
                {
                    return nsTarget.ComputePath( false );
                }

                //
                // Get the process:
                //
                NamespaceItem nsProcsContainer;
                if( !nsTarget.TryFindChild( "Processes", out nsProcsContainer ) )
                {
                    Util.Fail( "Can't find processes container!" );
                    return null;
                }

                dynamic kmProc = debugger.GetKmProcByAddr( ctx.ProcessIndexOrAddress );

                // We'll create an [ephemeral] item directly, for just this process.
                NsContainer< DbgKmProcessInfo > nsProc = null;

                ulong procAddr = debugger.GetImplicitProcessDataOffset();

                DbgKmProcessInfo curProc = new DbgKmProcessInfo( debugger,
                                                                 (DbgKModeTarget) nsTarget.RepresentedObject,
                                                                 procAddr );

                nsProc = (NsContainer< DbgKmProcessInfo >)
                    NamespaceItem.CreateItemForObject( curProc,
                                                       nsProcsContainer,
                                                       true ); // ephemeral

                return nsProc.ComputePath( false );
            }
            else
            {
                // User-mode

                //
                // Get the thread:
                //
                NamespaceItem nsThreadsContainer;
                if( !nsTarget.TryFindChild( "Threads", out nsThreadsContainer ) )
                {
                    Util.Fail( "Can't find threads container!" );
                    return null;
                }

                // We used to enumerate every single thread just to get the one we want here.
                // Instead, we'll create an [ephemeral] item directly, for just this thread.
                NsContainer< DbgUModeThreadInfo > nsThread = null;
                DbgUModeThreadInfo curThread = debugger.GetCurrentThread();

                nsThread = (NsContainer< DbgUModeThreadInfo >)
                    NamespaceItem.CreateItemForObject( curThread,
                                                       nsThreadsContainer,
                                                       true ); // ephemeral

                if( null == nsThread )
                    return nsTarget.ComputePath( false );

                //
                // Are we in a particular frame?
                //
                // TODO: it would be pretty bad to fail right here... what then?
                DbgStackFrameInfo frame = debugger.GetCurrentScopeFrame();

                if( 0 == frame.FrameNumber )
                    return nsThread.ComputePath( false );

                NsContainer nsStack;
                if( !nsThread.TryFindChildAs( "Stack", out nsStack ) )
                {
                    // TODO: it would be pretty bad to fail right here...
                    throw new Exception( "couldn't find stack dir!" );
                }

                // Similar to the thread, we used to enumerate frames just to get the one we
                // want here. Instead, we'll create an [ephemeral] item directly, for just
                // this frame.

                NsContainer< DbgStackFrameInfo > nsFrame;
                nsFrame = (NsContainer< DbgStackFrameInfo >)
                    NamespaceItem.CreateItemForObject( frame,
                                                       nsStack,
                                                       true ); // ephemeral

                return nsFrame.ComputePath( false );
            } // end else( user-mode )
        } // end GetPathForCurrentContext()




        //
        // Here's the deal:
        //
        // User-mode processes, of which there can be multiple per dbgeng "system", appear
        // as top-level namespace items. Kernel-mode targets also appear as top-level
        // namespace items. I don't think that anyone really does multi-system debugging,
        // and if so, probably not mixed kernel and user-mode, but even so, I'd like the
        // namespace code to be robust. What this means is that a system id alone is not
        // sufficient to identify the top-level namespace item; you need to know the
        // system id and whether or not it's a kernel target, and if it's not kernel, then
        // you also need to know the process id.
        //
        //    Dbg:\ (NS root)
        //      |
        //      \--- User-mode target A (sysId 0, pId 0)   \
        //      |                                           |
        //      \--- User-mode target B (sysId 0, pId 1)    |- same sysId
        //      |                                           |
        //      \--- User-mode target C (sysId 0, pId 2)   /
        //      |
        //      \--- Kernel-mode target X (sysId 1)
        //      |
        //      \--- Kernel-mode target Y (sysId 2)
        //


        private static NsContainer _GetTopLevelContainerForContext( DbgEngContext ctx )
        {
            RunspaceSettings rs = RunspaceSettings.Singleton;

            NsContainer nsResult = null;
            foreach( var candidate in rs.NsRoot.EnumerateChildItems() )
            {
                nsResult = candidate as NsContainer;
                if( null == nsResult )
                    continue;

                DbgTarget t = nsResult.RepresentedObject as DbgTarget;
                if( null == t )
                    continue;

                if( (t.DbgEngSystemId != ctx.SystemIndex) )
                    continue;

                if( ctx.IsKernelContext.HasValue && ctx.IsKernelContext.Value )
                {
                    if( t.IsKernel )
                        return nsResult;
                    else
                        continue;
                }

                var proc = nsResult.RepresentedObject as DbgUModeProcess;
                Util.Assert( null != proc );
                Util.Assert( ctx.ProcessIndexOrAddress == ((ulong) (uint) ctx.ProcessIndexOrAddress) );

                if( proc.DbgEngProcessId == (uint) ctx.ProcessIndexOrAddress )
                {
                    return nsResult;
                }
            }

            return null;
        }


        internal static string GetPathForDebuggerContext( DbgEngContext ctx )
        {
            NsContainer nsTarget = _GetTopLevelContainerForContext( ctx );
            if( null == nsTarget )
                return null;

            var t = nsTarget.RepresentedObject as DbgTarget;
            Util.Assert( null != t );

            if( t.IsKernel )
            {
                //
                // Kernel-mode
                //

                if( ctx.ProcessIndexOrAddress == DebuggerObject.DEBUG_ANY_ID )
                {
                    return nsTarget.ComputePath( false );
                }

                dynamic kmProc = t.Debugger.GetKmProcByAddr( ctx.ProcessIndexOrAddress );
            } // end if( kernel-mode )


            //
            // User-mode
            //

            if( ctx.ThreadIndexOrAddress == DebuggerObject.DEBUG_ANY_ID )
                return nsTarget.ComputePath( false );

            NamespaceItem nsThreadsContainer;
            if( !nsTarget.TryFindChild( "Threads", out nsThreadsContainer ) )
            {
                Util.Fail( "Can't find threads container!" );
                return null;
            }

            NsContainer< DbgUModeThreadInfo > nsThread = null;
            foreach( var candidate in nsThreadsContainer.EnumerateChildItems() )
            {
                nsThread = candidate as NsContainer< DbgUModeThreadInfo >;
                if( null == candidate )
                    continue;

                if( nsThread.RepresentedObject.DebuggerId == ctx.ThreadIndexOrAddress )
                    break;
                else
                    nsThread = null;
            }

            LogManager.Trace( "Warning: can't compute path for context with non-existent thread: {0}",
                              ctx );

            if( null == nsThread )
                return null;

            if( (DebuggerObject.DEBUG_ANY_ID == ctx.FrameIndex) ||
                (0 == ctx.FrameIndex) )
            {
                return nsThread.ComputePath( false );
            }

            NsContainer nsStack;
            if( !nsThread.TryFindChildAs( "Stack", out nsStack ) )
            {
                // TODO: it would be pretty bad to fail right here...
                throw new Exception( "couldn't find stack dir!" );
            }

            NsContainer< DbgStackFrameInfo > nsFrame;
            foreach( var nsChild in nsStack.EnumerateChildItems() )
            {
                nsFrame = nsChild as NsContainer< DbgStackFrameInfo >;
                if( null == nsFrame )
                    continue;

                if( nsFrame.RepresentedObject.FrameNumber == ctx.FrameIndex )
                    return nsFrame.ComputePath( false );
            }

            LogManager.Trace( "Warning: couldn't find frame for {0}", ctx );
            return null;
        } // end GetPathForDebuggerContext()


        public static DbgRegisterSetBase GetRegisterSetForPath( string providerPath )
        {
            return GetRegisterSetForPath( providerPath, null );
        }

        public static DbgRegisterSetBase GetRegisterSetForPath( string providerPath,
                                                                DbgRegisterSetBase baselineRegisters )
        {
            List< DebuggerObject > dbgObjList = GetDebuggerObjectsForPath( providerPath );

            for( int i = dbgObjList.Count - 1; i >= 0; i-- )
            {
                DebuggerObject dbgObj = dbgObjList[ i ];
                DbgRegisterSetBase rsb = dbgObj as DbgRegisterSetBase;
                if( null != rsb )
                    return rsb.WithBaseline( baselineRegisters );

                DbgStackFrameInfo sfi = dbgObj as DbgStackFrameInfo;
                if( null != sfi )
                    return sfi.RegisterSet.WithBaseline( baselineRegisters );

                DbgUModeThreadInfo ti = dbgObj as DbgUModeThreadInfo;
                if( null != ti )
                    return ti.Stack.GetTopFrame().RegisterSet.WithBaseline( baselineRegisters );
            } // foreach( debugger object in path list )

            return null;
        } // end GetRegisterSetForPath()


        public static DbgStackInfo GetStackInfoForPath( string providerPath )
        {
            return GetStackInfoForPath( providerPath, 0 ); // "0" means "all the frames"
        }

        public static DbgStackInfo GetStackInfoForPath( string providerPath,
                                                        int maxFrames ) // pass 0 for "all the frames"
        {
            List< DebuggerObject > dbgObjList = GetDebuggerObjectsForPath( providerPath );

            for( int i = dbgObjList.Count - 1; i >= 0; i-- )
            {
                DebuggerObject dbgObj = dbgObjList[ i ];
                DbgStackFrameInfo sfi = dbgObj as DbgStackFrameInfo;
                if( null != sfi )
                    return new DbgStackInfo( sfi, maxFrames );

                DbgStackInfo si = dbgObj as DbgStackInfo;
                if( null != si )
                    return si.WithMaxFrameCount( maxFrames );

                DbgUModeThreadInfo ti = dbgObj as DbgUModeThreadInfo;
                if( null != ti )
                    return ti.GetStackWithMaxFrames( maxFrames );
            } // foreach( debugger object in path list )

            return null;
        } // end GetStackInfoForPath()


      //protected override object NewItemDynamicParameters( string path, string itemTypeName, object newItemValue )
      //{
      //    _WriteDebugFmt( "NewItemDynamicParameters( '{0}', '{1}', {2} )", path, itemTypeName, newItemValue );

      //    // PowerShell should have already verified that the path does not already
      //    // exist.

      //    return null;
      //} // end NewItemDynamicParameters()


        protected override void RemoveItem( string path, bool recurse )
        {
            _WriteDebugFmt( "RemoveItem( '{0}', {1} )", path, recurse );

            NamespaceItem nsItem;
            if( _TryGetItem( path, out nsItem ) )
            {
                if( nsItem is NsRoot )
                    throw new InvalidOperationException( "You cannot remove the namespace root." );

                if( (nsItem is NsContainer) && !(nsItem is NsContainer< DbgTarget >) )
                    throw new InvalidOperationException( "You cannot remove this item." );

                if( !ShouldProcess( nsItem.Name ) )
                    return;

                // TODO: dynamic parameters for removing a live target (detach, quit)

                if( null != DynamicParameters )
                {
                    RemoveTargetDynamicParams rtdp = DynamicParameters as RemoveTargetDynamicParams;
                    if( null != rtdp )
                    {
                        _RemoveTarget( nsItem as NsContainer< DbgTarget >, rtdp.Detach );
                    }
                }
                else
                {
                    nsItem.Unlink();
                }
            }
            else
            {
                Util.Fail( "I bet PS checks if the item exists before calling me." );
                throw new ArgumentException( Util.Sprintf( "No such item: {0}", path ) );
            }
        } // end RemoveItem()


        private class RemoveTargetDynamicParams
        {
            [Parameter]
            public SwitchParameter Detach { get; set; }
        } // end class RemoveTargetDynamicParams


        protected override object RemoveItemDynamicParameters( string path, bool recurse )
        {
            NamespaceItem nsItem;
            if( _TryGetItem( path, out nsItem ) )
            {
                if( nsItem is NsContainer< DbgTarget > )
                {
                    return new RemoveTargetDynamicParams();
                }
            }
            return null;
        } // end RemoveItemDynamicParameters()


        protected override void CopyItem( string path, string copyPath, bool recurse )
        {
            _WriteDebugFmt( "CopyItem( '{0}', '{1}', {2} )", path, copyPath, recurse );
            NamespaceItem nsItem;
            if( !_TryGetItem( path, out nsItem ) )
            {
                Util.Fail( "I don't think PS will call us if the path doesn't exist." );
                throw new ArgumentException( Util.Sprintf( "Path doesn't exist: {0}", path ) );
            }

            if( !ShouldProcess( copyPath ) )
                return;

            NamespaceItem nsDest;
            NamespaceItem nsCopiedItem = null;
            if( _TryGetItem( copyPath, out nsDest ) )
            {
                if( nsDest.IsContainer )
                {
                    nsCopiedItem = _TryCopyItemDefinite( nsItem, nsDest, null, recurse );
                }
                else
                {
                    // The FileSystem provider checks to see if the destination is a leaf
                    // item, and if the source item is a container, doesn't let you copy
                    // over it, even if you pass -Force. But if both source and dest are
                    // leaf items, it happily overwites the dest without -Force. That
                    // seems odd to me... the behavior I will implement is: if the user
                    // passes Force, I'll obliterate dest; else I won't.
                    if( !Force )
                    {
                        var iee = new ItemExistsException( nsDest.Name, nsDest.ComputePath( false ) );
                        try { throw iee; } catch( ItemExistsException ) { } // give it a stack
                        WriteError( iee,
                                    "Destination item already exists; use -Force to overwrite it.",
                                    ErrorCategory.ResourceExists,
                                    nsDest.ComputePath( false ) );
                    }
                    else
                    {
                        NamespaceItem nsDestParent = nsDest.Parent.Item;
                        // TODO: This changes the order of stuff in the tree (and
                        // therefore the order stuff shows up when doing "dir"). Consider
                        // somehow doing an in-place replace thing instead.
                        nsDest.Unlink();
                        nsCopiedItem = _TryCopyItemDefinite( nsItem, nsDestParent, nsDest.Name, recurse );
                    }
                }
            }
            else
            {
                NamespaceItem nsDestParent;
                string destLeafName;
                try
                {
                    _FindParentOfProposedItem( copyPath, out nsDestParent, out destLeafName );
                    nsCopiedItem = _TryCopyItemDefinite( nsItem, nsDestParent, destLeafName, recurse );
                }
                catch( IOException ioe )
                {
                    WriteError( ioe, "DestinationNotFound", ErrorCategory.ObjectNotFound, copyPath );
                }
            }

            if( null != nsCopiedItem )
            {
                WriteItemObject( DbgProviderItem.CreateDbgItem( nsCopiedItem ),
                                 nsCopiedItem.ComputePath( false ),
                                 nsItem.IsContainer );
            }
        } // end CopyItem()


        private NamespaceItem _TryCopyItemDefinite( NamespaceItem nsItem,
                                                    NamespaceItem nsDestParent,
                                                    string destLeafName,
                                                    bool recurse )
        {
            NamespaceItem nsCopiedItem = null;
            Exception e = null;
            try
            {
                nsCopiedItem = nsItem.CopyTo( nsDestParent,
                                              destLeafName,
                                              recurse,
                                              (ex, path) => { WriteError( ex,
                                                                          "CopyChildItemFailed",
                                                                          ErrorCategory.NotSpecified,
                                                                          path );
                                                            },
                                              () => Stopping );
            }
            catch( ArgumentException ae ) { e = ae; }
            catch( InvalidOperationException ioe ) { e = ioe; }
            catch( IOException ioe ) { e = ioe; }
            catch( NotSupportedException nse ) { e = nse; }

            if( null != e )
            {
                // TODO: I don't want the NamespaceItem stuff to have any PS stuff. But it
                // would be nice to have better errors coming out of .CopyTo, so that we
                // could have a better ErrorCategory, etc. here.
                WriteError( e,
                            "CopyItemFailed",
                            ErrorCategory.NotSpecified,
                            nsDestParent.ComputePath( false ) + "\\" + destLeafName );
            }
            return nsCopiedItem;
        } // end _TryCopyItemDefinite()


        protected override void MoveItem( string path, string destination )
        {
            _WriteDebugFmt( "MoveItem( '{0}', '{1}' )", path, destination );
            NamespaceItem nsItem;
            if( !_TryGetItem( path, out nsItem ) )
            {
                Util.Fail( "I don't think PS will call us if the path doesn't exist." );
                throw new ArgumentException( Util.Sprintf( "Path doesn't exist: {0}", path ) );
            }

            if( !ShouldProcess( path ) )
                return;

            NamespaceItem nsDest;
            NamespaceItem nsMovedItem = null;
            try
            {
                if( _TryGetItem( destination, out nsDest ) )
                {
                    if( nsDest.IsContainer )
                    {
                        nsMovedItem = nsItem.MoveTo( nsDest, null );
                    }
                    else
                    {
                        // The FileSystem provider checks to see if the destination is a
                        // leaf item, and if the source item is a container, doesn't let
                        // you copy over it, even if you pass -Force. But if both source
                        // and dest are leaf items, it happily overwites the dest without
                        // -Force. That seems odd to me... the behavior I will implement
                        // is: if the user passes Force, I'll obliterate dest; else I
                        // won't.
                        if( !Force )
                        {
                            var iee = new ItemExistsException( nsDest.Name, nsDest.ComputePath( false ) );
                            try { throw iee; }
                            catch( ItemExistsException ) { } // give it a stack
                            WriteError( iee,
                                        "Destination item already exists; use -Force to overwrite it.",
                                        ErrorCategory.ResourceExists,
                                        nsDest.ComputePath( false ) );
                        }
                        else
                        {
                            NamespaceItem nsDestParent = nsDest.Parent.Item;
                            // TODO: This changes the order of stuff in the tree (and
                            // therefore the order stuff shows up when doing "dir").
                            // Consider somehow doing an in-place replace thing instead.
                            nsDest.Unlink();
                            nsMovedItem = nsItem.MoveTo( nsDestParent, nsDest.Name );
                        }
                    }
                }
                else
                {
                    NamespaceItem nsDestParent;
                    string destLeafName;
                    try
                    {
                        _FindParentOfProposedItem( destination, out nsDestParent, out destLeafName );
                        nsMovedItem = nsItem.MoveTo( nsDestParent, destLeafName );
                    }
                    catch( IOException ioe )
                    {
                        WriteError( ioe, "DestinationNotFound", ErrorCategory.ObjectNotFound, destination );
                    }
                }
            }
            catch( IOException ioe ) // TODO: better define possible exceptions
            {
                WriteError( ioe, "MoveFailed", ErrorCategory.WriteError, destination ); // TODO: or should the targetObject be 'path'?
            }
            catch( InvalidOperationException ioe )
            {
                WriteError( ioe, "InvalidMove", ErrorCategory.InvalidOperation, destination ); // TODO: or should the targetObject be 'path'?
            }

            if( null != nsMovedItem )
            {
                WriteItemObject( DbgProviderItem.CreateDbgItem( nsMovedItem ),
                                 nsMovedItem.ComputePath( false ),
                                 nsItem.IsContainer );
            }
        } // end MoveItem()


        protected override void RenameItem( string path, string newName )
        {
            _WriteDebugFmt( "RenameItem( '{0}', '{1}' )", path, newName );
            NamespaceItem nsItem;
            if( !_TryGetItem( path, out nsItem ) )
            {
                Util.Fail( "I don't think PS will call us if the path doesn't exist." );
                throw new ArgumentException( Util.Sprintf( "Path doesn't exist: {0}", path ) );
            }

            if( !ShouldProcess( path ) )
                return;

            try
            {
                nsItem.Rename( newName );
                WriteItemObject( DbgProviderItem.CreateDbgItem( nsItem ), nsItem.ComputePath( false ), nsItem.IsContainer );
            }
            catch( ItemExistsException iee )
            {
                WriteError( iee, "RenameTargetAlreadyExists", ErrorCategory.ResourceExists, path );
            }
            catch( InvalidItemNameException iine )
            {
                WriteError( iine, "InvalidItemName", ErrorCategory.InvalidData, path );
            }
            catch( IOException ioe )
            {
                WriteError( ioe, "RenameFailed", ErrorCategory.NotSpecified, path );
            }
        } // end RenameItem()



        //
        // HostSupportsColor stuff:
        //
        // This is all kept directly in DbgProvider (instead of in the RunspaceSettings)
        // for a couple reasons:
        //
        // 1. There would be no point in having it be a per-runspace setting, as the host
        //    is global.
        // 2. It would be a problem to store it in the RunspaceSettings object, because it
        //    needs to be configured very early in the setup of the process--/before/ the
        //    runspace is fully initialized.
        //
        private static bool sm_hostSupportsColor;
        public static bool HostSupportsColor
        {
            get { return sm_hostSupportsColor; }
            set { sm_hostSupportsColor = value; }
        }

        /// <summary>
        ///    Hacky workaround for the fact that the Out-Default proxy will always get
        ///    the last word when trying to set $HostSupportsColor.
        /// </summary>
        /// <remarks>
        ///    The Out-Default proxy dynamically toggles HostSupportsColor, and then tries
        ///    to restore the original value. The problem is that every once in a while,
        ///    if you CTRL-C at just the right time, Out-Default does not get a chance to
        ///    restore the old value (there's no way to implement a "Dispose" method for a
        ///    script function), and you get stuck, because now Out-Default thinks the
        ///    "original" value is something else, and it always sets it back to that.
        ///
        ///    This method queues a work item to a separate thread, which does a "sleep"
        ///    and then sets the property directly, hopefully after the Out-Default proxy
        ///    has ended.
        /// </remarks>
        public static void ResetHostSupportsColor( bool val )
        {
            System.Threading.ThreadPool.QueueUserWorkItem( ( x ) =>
                {
                    System.Threading.Thread.Sleep( 500 );
                    sm_hostSupportsColor = val;
                } );
        } // end ResetHostSupportsColor()


        private class HostSupportsColorPSVariable : PSVariable
        {
            public const string c_HostSupportsColor = "HostSupportsColor";

            internal HostSupportsColorPSVariable() : base( c_HostSupportsColor )
            {
            }

            public override object Value
            {
                get { return DbgProvider.HostSupportsColor; }
                set { DbgProvider.HostSupportsColor = (bool) value; }
            }

            public override ScopedItemOptions Options
            {
                get { return ScopedItemOptions.AllScope; }
                set
                {
                    throw new PSInvalidOperationException( "You cannot modify options for this value." );
                }
            }
        } // end class HostSupportsColorPSVariable


        private static HostSupportsColorPSVariable sm_hostSupportsColorPSVar = new HostSupportsColorPSVariable();

        /// <summary>
        ///    Allows the host to get the PSVariable that should be used to back the
        ///    $HostSupportsColor variable.
        /// </summary>
        // TODO: Might be nice to put this in a different class or something, as it is not
        // something that "normal" clients of the DbgProvider class should ever use.
        public static PSVariable GetHostSupportsColorPSVar()
        {
            return sm_hostSupportsColorPSVar;
        }


        internal class ScriptAndArgs
        {
            public readonly string Script;
            public readonly object[] Args;
            public ScriptAndArgs( string script, params object[] args )
            {
                Script = script;
                Args = args;
            }
        } // end class ScriptAndArgs


        internal static Queue< ScriptAndArgs > StuffToExecuteBeforeNextPrompt = new Queue< ScriptAndArgs >();

        /// <summary>
        ///    Queues some script code for the host to execute before the next prompt.
        ///    Note that this only works with the DbgShell ColorConsoleHost.
        /// </summary>
        /// <remarks>
        ///    Deep in some code path, someone might want to do something like
        ///    "Write-Warning 'Could not load DAC for CLR version blah.'", but a) they
        ///    don't have access to a cmdlet or an IPipelineCallback, and b) even if they
        ///    did, it might not be a good time to write a warning (perhaps we're in the
        ///    middle of table formatting or something).
        ///
        ///    So this method allows them to queue a little snippet of script to be
        ///    executed before the next prompt.
        /// </remarks>
        public static void RequestExecuteBeforeNextPrompt( string psScript, params object[] args )
        {
            if( String.IsNullOrEmpty( psScript ) )
            {
                Util.Fail( "why?" );
                return;
            }

            StuffToExecuteBeforeNextPrompt.Enqueue( new ScriptAndArgs( psScript, args ) );
        } // end RequestExecuteBeforeNextPrompt()


        public static string FormatUInt64( ulong val, bool useTick )
        {
            uint hi = (uint) ((val & 0xffffffff00000000) >> 32);
            uint lo = (uint) ((val & 0x00000000ffffffff));
            if( (0 != hi) && useTick )
            {
                return Util.Sprintf( "{0:x8}`{1:x8}", hi, lo );
            }
            else
            {
                return Util.Sprintf( "{0:x}", val );
            }
        } // end FormatUInt64()


        public static string FormatByteSize( int size )
        {
            return FormatByteSize( (long) size );
        }

        public static string FormatByteSize( uint size )
        {
            return FormatByteSize( (long) size );
        }

        public static string FormatByteSize( ulong size )
        {
            return FormatByteSize( unchecked( (long) size ) );
        }

        public static string FormatByteSize( long size )
        {
            return Util.FormatByteSize( size );
        }


        private static ColorString sm_nullStr = new ColorString( ConsoleColor.DarkMagenta, "(null)" ).MakeReadOnly();

        public static ColorString FormatAddress( ulong addr, bool is32bit, bool useTick )
        {
            return FormatAddress( addr, is32bit, useTick, false );
        }

        public static ColorString FormatAddress( ulong addr,
                                                 bool is32bit,
                                                 bool useTick,
                                                 bool numericNull )
        {
            return FormatAddress( addr, is32bit, useTick, numericNull, ConsoleColor.Cyan );
        }

        public static ColorString FormatAddress( ulong addr,
                                                 bool is32bit,
                                                 bool useTick,
                                                 bool numericNull,
                                                 ConsoleColor defaultColor )
        {
            if( (0 == addr) && !numericNull )
                return sm_nullStr;

            ColorString cs = new ColorString();
            if( HostSupportsColor )
                cs.AppendPushFg( defaultColor );

            string str = null;

            if( is32bit )
            {
                ulong hi = addr & 0xffffffff00000000;
                if( (hi != 0) && (hi != 0xffffffff00000000) )
                {
                    Util.Fail( "Caller said it was a 32-bit address, but the high bits look valid." );
                }
                addr = addr & 0x00000000ffffffff; // because ReadPointersVirtual sign extends.
                str = Util.Sprintf( "{0:x8}", addr );
            }
            else
            {
                if( useTick )
                {
                    uint hi = (uint) ((addr & 0xffffffff00000000) >> 32);
                    uint lo = (uint) ((addr & 0x00000000ffffffff));
                    str = Util.Sprintf( "{0:x8}`{1:x8}", hi, lo );
                }
                else
                {
                    str = Util.Sprintf( "{0:x16}", addr );
                }
            }

            cs.Append( str );

            if( HostSupportsColor )
                cs.AppendPop();

            return cs;
        } // end FormatAddress


        public static ColorString ColorizeRegisterName( string r )
        {
            if( String.IsNullOrEmpty( r ) )
                return r;

            if( r[ 0 ] != '@' )
                r = "@" + r;

            if( HostSupportsColor )
                return new ColorString( ConsoleColor.Cyan, r );
            else
                return r;
        }


        public static ColorString ColorizeTypeName( string s )
        {
            if( String.IsNullOrEmpty( s ) || !HostSupportsColor )
                return s;

            return new ColorString( ConsoleColor.DarkYellow, s );
        }


        public static ColorString ColorizeModuleName( string m )
        {
            if( String.IsNullOrEmpty( m ) || !HostSupportsColor )
                return m;

            return new ColorString( ConsoleColor.Black, ConsoleColor.DarkYellow, m );
        }


        public static ColorString ColorizeFunctionName( string s )
        {
            if( String.IsNullOrEmpty( s ) || !HostSupportsColor )
                return s;

            return new ColorString( ConsoleColor.DarkYellow, s );
        }

        public static ColorString ColorizeInstruction( string s )
        {
            if( String.IsNullOrEmpty( s ) || !HostSupportsColor )
                return s;

            if( (0 == Util.Strcmp_OI( "call", s )) ||
                (0 == Util.Strcmp_OI( "ret", s )) )
            {
                return new ColorString( ConsoleColor.Magenta, s );
            }
            else if( 0 == Util.Strcmp_OI( "int", s ) )
            {
                return new ColorString( ConsoleColor.Yellow, s );
            }
            else if( 0 == Util.Strcmp_OI( "???", s ) )
            {
                return new ColorString( ConsoleColor.Red, s );
            }
            else
            {
                return s;
            }
        } // end ColorizeInstruction()


        private static readonly char[] sm_bangSeparator = new char[] { '!' };
        private static readonly char[] sm_plusSeparator = new char[] { '+' };

        public static void ParseSymbolName( string moduleQualifiedSymbolName,
                                            out string module,
                                            out string bareSym,
                                            out ulong offset )
        {
            if( String.IsNullOrEmpty( moduleQualifiedSymbolName ) )
                throw new ArgumentException( "You must supply a symbol name.", "moduleQualifiedSymbolName" );

            module = null;
            bareSym = null;
            offset = 0;

            string[] tokens = moduleQualifiedSymbolName.Split( sm_bangSeparator );
            if( tokens.Length < 2 )
            {
                module = "*";
                bareSym = tokens[ 0 ];
            }
            else
            {
                module = tokens[ 0 ];
                bareSym = tokens[ 1 ];
                // If there were more bangs, we ignore them.
            }

            int plusIndex = bareSym.LastIndexOf( '+' );
            if( (plusIndex > 0) && (plusIndex < (bareSym.Length - 1)) )
            {
                string strOffset = bareSym.Substring( plusIndex + 1 );

                if( TryParseHexOrDecimalNumber( strOffset, out offset ) )
                {
                    bareSym = bareSym.Substring( 0, plusIndex );
                }
                // else it must not be an offset, I guess. (Sometimes plusses show up in
                // managed type names, for example)
            }
        } // end ParseSymbolName()


        public static ColorString ColorizeSymbol( string symbol )
        {
            if( String.IsNullOrEmpty( symbol ) || !HostSupportsColor )
                return symbol;

            ColorString cs;
            string module;
            string[] tokens = symbol.Split( sm_bangSeparator );
            if( 2 != tokens.Length )
            {
                // This could happen, for instance, if you don't have symbols for
                // something, and you just have the module name and an offset (like
                // "vfbasics+0x58ac"), or maybe you have /just/ an offset.

                tokens = symbol.Split( sm_plusSeparator );
                if( tokens.Length > 1 )
                {
                    cs = new ColorString();
                    module = tokens[ 0 ];
                    cs.Append( ColorizeModuleName( module ) );
                    for( int i = 1; i < tokens.Length; i++ )
                    {
                        cs.Append( "+" );
                        cs.Append( tokens[ i ] );
                    }
                    return cs;
                }
                else
                {
                    return ColorizeFunctionName( symbol );
                }
            }

            module = tokens[ 0 ];
            string postModule = tokens[ 1 ];
            cs = ColorizeModuleName( module );
            cs.AppendPushPopFg( ConsoleColor.Gray, "!" );

            string sym = postModule;

            // There might be an offset component. To find it, we'll look for the last '+'
            // and then check that what follows is numeric.
            ulong offset = 0;
            int plusIndex = postModule.LastIndexOf( '+' );
            if( (plusIndex > 0) && (plusIndex < (postModule.Length - 1)) )
            {
                string strOffset = postModule.Substring( plusIndex + 1 );

                if( TryParseHexOrDecimalNumber( strOffset, out offset ) )
                {
                    sym = postModule.Substring( 0, plusIndex );
                }
                else
                {
                    // else it must not be an offset, I guess. (Sometimes plusses show up
                    // in managed type names, for example)
                }
            }

            cs.Append( ColorizeTypeName( sym ) );

            if( 0 != offset )
            {
                cs.Append( postModule.Substring( plusIndex ) );
            }

            return cs;
        } // end ColorizeSymbol()


        /// <summary>
        ///    Parses a string into a UInt64, taking into account that it may begin with
        ///    "0n" or "0x", or have a backtick in it, or had a backtick that was
        ///    interpreted as an escape. If no "0x" or "0n" prefix, hex is assumed.
        /// </summary>
        public static bool TryParseHexOrDecimalNumber( string s, out ulong address )
        {
            address = 0;
            if( String.IsNullOrEmpty( s ) )
                return false;

            if( s.Length > 22 ) // The length of UInt64.MaxValue, as a decimal string, plus 2 for "0n".
                return false;

            int startIdx = 0;
            bool isDecimal = false;
            int maxDigits = 16;
            if( s.StartsWith( "0n", StringComparison.OrdinalIgnoreCase ) )
            {
                isDecimal = true;
                startIdx = 2;
                maxDigits = 20;
            }
            else if( s.StartsWith( "0x", StringComparison.OrdinalIgnoreCase ) )
            {
                startIdx = 2;
            }

            ulong result = 0;
            int numDigitsProcessed = 0;
            for( int i = startIdx; i < s.Length; i++ )
            {
                // The debugger uses backticks in 64-bit addresses. Unfortunately,
                // backticks are also the PowerShell escape character. If such an escape
                // character was applied to a hex digit, let's "undo" it.
                //
                // Here's some script that lets you see which escapes are valid:
                //

                //    Write-Host "Decimal digits:" -Fore Cyan
                //
                //    0..9 | %{
                //        $script = [System.Management.Automation.ScriptBlock]::Create( " ""``$($_)"" " )
                //        $s = & $script
                //        "$s ($([int] ($s[0])))"
                //    }
                //
                //    Write-Host "Hex digits:" -Fore Cyan
                //    @( 'a', 'b', 'c', 'd', 'e', 'f' ) | %{
                //        $script = [System.Management.Automation.ScriptBlock]::Create( " ""``$($_)"" " )
                //        $s = & $script
                //        "$s ($([int] ($s[0])))"
                //    }
                //
                // Output:
                //
                //    Decimal digits:
                //      (0)
                //    1 (49)
                //    2 (50)
                //    3 (51)
                //    4 (52)
                //    5 (53)
                //    6 (54)
                //    7 (55)
                //    8 (56)
                //    9 (57)
                //    Hex digits:
                //     (7)
                //     (8)
                //    c (99)
                //    d (100)
                //    e (101)
                //    ♀ (12)
                //
                char c = s[ i ];
                if( c == '`' )
                {
                    if( isDecimal )
                        return false; // No such thing as a backtick in a decimal number...

                    continue;
                }

                if( isDecimal )
                    result = result * 10;
                else
                    result = result << 4; // * 16

                switch( c )
                {
                    case (char) 0:
                        // It was a '0' that got escaped.
                        break;

                    case (char) 7:
                        if( isDecimal )
                            return false;

                        result += 0xa;
                        break;

                    case (char) 8:
                        if( isDecimal )
                            return false;

                        result += 0xb;
                        break;

                    case (char) 12:
                        if( isDecimal )
                            return false;

                        result += 0xf;
                        break;

                    default:
                        byte b;
                        if( !Util.TryCharToByte( c, out b, !isDecimal ) )
                            return false;

                        result += b;
                        break;
                } // end switch( c )

                numDigitsProcessed++;
                if( numDigitsProcessed > maxDigits )
                    return false;
            } // end foreach( source char )

            address = result;
            return true;
        } // end TryParseHexOrDecimalNumber()


        public static ulong ParseHexOrDecimalNumber( string strNumber )
        {
            if( String.IsNullOrEmpty( strNumber ) )
                throw new ArgumentException( "You must supply an address.", "addressString" );

            ulong result;
            if( !TryParseHexOrDecimalNumber( strNumber, out result ) )
            {
                throw new ArgumentException( Util.Sprintf( "Could not convert '{0}' to a number.",
                                                           strNumber ),
                                             "strNumber" );
            }

            return result;
        } // end ParseHexOrDecimalNumber()


        public static void SetAutoRepeatCommand( string autoRepeatCommand )
        {
            RunspaceSettings rs = RunspaceSettings.Singleton;
            rs.SetAutoRepeatCommand( autoRepeatCommand );
        } // end SetAutoRepeatCommand()


        internal static void LogError( ErrorRecord er )
        {
            LogManager.Trace( "WriteError1: {0}", er.ErrorDetails == null ? "(no details)" : er.ErrorDetails.Message );
            LogManager.Trace( "WriteError2: {0}", er.CategoryInfo == null ? "(no category info)" : er.CategoryInfo.ToString() );
            LogManager.Trace( "WriteError3: {0}", er.FullyQualifiedErrorId );
            LogManager.Trace( "WriteError4: {0}", er.Exception );

            // There are a couple of special cases where very interesting exception data
            // won't get logged by the default Exception.ToString() call.
            //
            // 1. If the exception came across a PS remoting channel, PS squirrels away
            //    the essential troubleshooting data. Let's dig it out and put it in the
            //    log (else you have to be at an interactive PS prompt to get it).
            // 2. If we transported an external ErrorRecord via a DbgProviderException,
            //    it would be nice to know the stack trace it took through our code.
            int depth = 0;
            Exception e = er.Exception;
            while( null != e )
            {
                if( e is RemoteException )
                {
                    LogManager.Trace( "WriteError5: Found a RemoteException at depth {0}. SerializedRemoteException: {1}",
                                      depth,
                                      ((RemoteException) e).SerializedRemoteException.ToString() );
                }

                if( (null != e.Data) && (e.Data.Contains( DbgProviderException.DpeKey )) )
                {
                    LogManager.Trace( "WriteError6: Found a transport DPE at depth {0}: {1}",
                                      depth,
                                      (DbgProviderException) e.Data[ DbgProviderException.DpeKey ] );
                }

                depth++;
                e = e.InnerException;
            }
        } // end LogError()


        internal static IDisposable LeaseShell( out PowerShell shell )
        {
            RunspaceSettings rs = RunspaceSettings.Singleton;
            return rs.LeaseShell( out shell );
        } // end LeaseShell()


        /// <summary>
        ///    Returns the new current path.
        /// </summary>
        public static string SetLocationByDebuggerContext()
        {
            RunspaceSettings rs = RunspaceSettings.Singleton;
            return rs.SetLocationByDebuggerContext( DbgEngDebugger._GlobalDebugger );
        } // end SetLocationByDebuggerContext()


        public static void ForceRebuildNamespace()
        {
            RunspaceSettings rs = RunspaceSettings.Singleton;
            rs.ForceRebuildNamespace( DbgEngDebugger._GlobalDebugger );
            rs.SetLocationByDebuggerContext(  DbgEngDebugger._GlobalDebugger );

            dynamic host = rs.SessionState.PSVariable.GetValue( "Host", null );
            if( null != host )
            {
                int width = host.UI.RawUI.BufferSize.Width;
                DbgEngDebugger._GlobalDebugger.AdjustAddressColumnWidths( width );
            }
            else
            {
                Util.Fail( "Hey... couldn't get the host width?" );
            }
        } // end ForceRebuildNamespace()


        internal static bool IsTargetNameInUse( string targetName )
        {
            RunspaceSettings rs = RunspaceSettings.Singleton;
            return rs.IsTargetNameInUse( targetName );
        } // end IsTargetNameInUse()


        /// <summary>
        ///    Captures context (variables and functions) defined since the last marker
        ///    set by SetContextStartMarker().
        /// </summary>
        internal static PsContext CapturePsContext()
        {
            return CapturePsContext( true );
        }

        /// <summary>
        ///    Captures PowerShell context (variables and functions).
        /// </summary>
        internal static PsContext CapturePsContext( bool fromLastMarker )
        {
            PowerShell shell;
            using( LeaseShell( out shell ) )
            {
                shell.AddScript( Util.Sprintf( ". \"filesystem::{0}\\GetPsContextFunc.ps1\" ; Get-PsContext",
                                               PsScriptRoot ),
                                               true );

                Collection< PSObject > results = shell.Invoke();

                // I guess we don't care if errors were encountered, as long as it produced output?

                // I used to have asserts here about shell.HadErrors being false, but they
                // fired once when I Ctrl +C'ed in the middle of running some tests.

                if( 0 == results.Count )
                {
                    LogManager.Trace( "WARNING: could not capture context. This can happen if you Ctrl+C at just the right time." );
                    if( 0 != shell.Streams.Error.Count )
                    {
                        // Not sure what this will look like... but if I ever hit this
                        // again it might be interesting to see.
                        LogManager.Trace( "First error record: {0}", shell.Streams.Error[ 0 ] );
                    }
                }
                else if( 1 == results.Count )
                {
                    Util.Assert( results[ 0 ].BaseObject is PsContext );
                    var ctx = (PsContext) results[ 0 ].BaseObject;
                    PsContext finalCtx;
                    if( fromLastMarker )
                        finalCtx = ctx - GetCurrentContextStartMarker();
                    else
                        finalCtx = ctx;

                    return finalCtx;
                }
                else
                {
                    Util.Fail( "Get-PsContext returned too much." );
                }
                return null;
            } // end using( shell )
        } // end CapturePsContext()


        private static Stack< PsContext > sm_contextStartMarkers = new Stack< PsContext >();

        /// <summary>
        ///    Saves the current session state ("context": functions and variables) on a
        ///    stack. This session state is filtered out of captured context. You can
        ///    think of it as setting a marker on the "scope stack", and variables and
        ///    functions defined earlier on the stack will not be captured later when
        ///    someone captures context using CapturePsContext(). TODO: CapturePsContext()
        ///    is internal... I'm inclined to keep it that way, but this is awkward to
        ///    document.
        /// </summary>
        public static IDisposable SetContextStartMarker()
        {
            var ctx = CapturePsContext( false );
            sm_contextStartMarkers.Push( ctx );
            return new Disposable( () =>
                {
                    var popped = sm_contextStartMarkers.Pop();
                    if( !Object.ReferenceEquals( popped, ctx ) )
                    {
                        // I don't know if anybody will actually use the stack, but just
                        // in case they do, let's make sure they use it right.
                        Util.Fail( "Unbalanced context marker stuff." );
                        throw new Exception( "Unbalanced context marker stuff." );
                    }
                } );
        } // end SetContextStartMarker()

        internal static PsContext GetCurrentContextStartMarker()
        {
            if( 0 == sm_contextStartMarkers.Count )
            {
                sm_contextStartMarkers.Push( new PsContext() );
            }
            return sm_contextStartMarkers.Peek();
        } // end GetCurrentContextStartMarker()


        private static string __psScriptRoot;
        public static string PsScriptRoot
        {
            get
            {
                if( null == __psScriptRoot )
                {
                    __psScriptRoot = Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location );
                }
                return __psScriptRoot;
            }
        }



        static string[] sm_months = { null, // because months are 1-based
                                      "Jan",
                                      "Feb",
                                      "Mar",
                                      "Apr",
                                      "May",
                                      "Jun",
                                      "Jul",
                                      "Aug",
                                      "Sep",
                                      "Oct",
                                      "Nov",
                                      "Dec" };

        // Example:
        //
        //   Fri Oct 17 22:44:30.251 2014 (UTC - 7:00)
        //
        private static Regex sm_debuggerTimeStringRegex = new Regex( @"(?<dayName>.{3}) (?<monthName>.{3})\s+(?<day>\d+) (?<time>.*) (?<year>\d{4}) \(UTC (?<utcSign>[-+]) (?<utcHours>.+):(?<utcMin>.+)\)",
                                                                     RegexOptions.Compiled );

        private static bool _TryParseInt( Group g, out int i )
        {
            return Int32.TryParse( g.Value,
                                   NumberStyles.None,
                                   CultureInfo.InvariantCulture,
                                   out i );
        }


        public static bool TryParseDebuggerTimestamp( string timestampString, out DateTimeOffset timestamp )
        {
            timestamp = DateTimeOffset.MinValue;
            var m = sm_debuggerTimeStringRegex.Match( timestampString );

            if( !m.Success )
                return false;

            int month = Array.IndexOf( sm_months, m.Groups[ "monthName" ].Value );
            if( month < 0 )
                return false;

            int day;
            if( !_TryParseInt( m.Groups[ "day" ], out day ) )
                return false;

            TimeSpan ts;
            if( !TimeSpan.TryParse( m.Groups[ "time" ].Value, CultureInfo.InvariantCulture, out ts ) )
                return false;

            int year;
            if( !_TryParseInt( m.Groups[ "year" ], out year ) )
                return false;

            int utcHours;
            if( !_TryParseInt( m.Groups[ "utcHours" ], out utcHours ) )
                return false;

            int utcMin;
            if( !_TryParseInt( m.Groups[ "utcMin" ], out utcMin ) )
                return false;

            TimeSpan utcOffset = TimeSpan.FromMinutes( (60 * utcHours) + utcMin );

            DateTime d = new DateTime( year,
                                       month,
                                       day,
                                       ts.Hours,
                                       ts.Minutes,
                                       ts.Seconds,
                                       ts.Milliseconds );

            string utcSign = m.Groups[ "utcSign" ].Value;
            if( "-" == utcSign )
            {
                utcOffset = -utcOffset;
            }
            else if( "+" != utcSign )
            {
                return false; // bogus sign?!
            }

            timestamp = new DateTimeOffset( d, utcOffset );
            return true;
        } // end TryParseDebuggerTimestamp()


        public static DateTimeOffset ParseDebuggerTimestamp( string timestampString )
        {
            DateTimeOffset dto;
            if( !TryParseDebuggerTimestamp( timestampString, out dto ) )
            {
                throw new DbgProviderException( Util.Sprintf( "Could not convert to timestamp: \"{0}\".",
                                                              timestampString ),
                                                "InvalidTimestampFormat",
                                                ErrorCategory.InvalidData,
                                                timestampString );
            }
            return dto;
        }


        public static ColorString FormatTimestamp( DateTimeOffset timestamp,
                                                   bool includeUtcOffset )
        {
            // Plain text:
            //return timestamp.ToString( "ddd MMM d HH:mm:ss.fff yyyy (UTCK)" );

            var dateColor = ConsoleColor.Magenta;
            var timeBright = ConsoleColor.White;
            var parenColor = ConsoleColor.Gray;
            var insignif = ConsoleColor.DarkGray;

            var cs = new ColorString()
                .AppendPushPopFg( dateColor, timestamp.ToString( "ddd MMM d" ) )
                .Append( " " )
                .AppendPushFg( timeBright )
                .Append( timestamp.ToString( "HH" ) )
                .AppendPushPopFg( insignif, ":" )
                .Append( timestamp.ToString( "mm" ) )
                .AppendPushPopFg( insignif, ":" )
                .Append( timestamp.ToString( "ss" ) )
                .AppendPushPopFg( insignif, timestamp.ToString( ".fff" ) )
                .AppendPop()
                .Append( " " )
                .AppendPushPopFg( dateColor, timestamp.ToString( "yyyy" ) );

            if( includeUtcOffset )
            {
                // Sheesh; I wish they'd just let us use "K" on its own.
                cs.Append( " " )
                  .AppendPushPopFg( parenColor, "(" )
                  .AppendPushPopFg( insignif, "UTC" )
                  .AppendPushFg( timeBright );

                if( timestamp.Offset > TimeSpan.Zero )
                    cs.Append( "+" );
                else
                    cs.Append( "-" );

                cs.Append( timestamp.Offset.ToString( @"h\:mm" ) )
                  .AppendPushPopFg( parenColor, ")" )
                  .AppendPop();
            } // end if( includeUtcOffset )

            return cs;
        } // end ToDebuggerStyleTimestampString()


        // Workaround for INTe02c78c0
        public static bool IsISupportColor( object obj )
        {
            return obj is ISupportColor;
        }


        //
        // Guest mode stuff
        //
        // Guest mode is when we are hosted by a debugger extension, and need to cooperate
        // with the parent debugger.
        //
        #region GuestModeStuff

        private static ManualResetEvent sm_guestModeEvent;
        private static Thread sm_guestModeThread;
        private static bool sm_guestModeShareConsole;

        /// <summary>
        ///    Called by the host to let us know that we are in "guest mode" (being hosted
        ///    by a debugger extension (DbgShellExt).
        /// </summary>
        internal static void SetGuestMode( bool shareConsole )
        {
            sm_guestModeEvent = new ManualResetEvent( false );
            sm_guestModeThread = Thread.CurrentThread;
            sm_guestModeShareConsole = shareConsole;
        }

        /// <summary>
        ///    Used to synchronize things in guest mode.
        /// </summary>
        internal static ManualResetEvent GuestModeEvent { get { return sm_guestModeEvent; } }

        /// <summary>
        ///    In guest mode, the "guest thread" is the thread upon which DbgShell (the
        ///    "guest") is entered. Instead of using that thread for the "Main" method, we
        ///    need to use it to interact with the debugger.
        /// </summary>
        internal static Thread GuestModeGuestThread { get { return sm_guestModeThread; } }

        /// <summary>
        ///    In guest mode, indicates whether DbgShell is sharing the console with a
        ///    console-based debugger (ntsd/cdb/kd), or if it has the console all to
        ///    itself (when used with windbg).
        /// </summary>
        internal static bool GuestModeShareConsole { get { return sm_guestModeShareConsole; } }

        /// <summary>
        ///    This is what needs to be run on the guest thread (which ends up being
        ///    pumping on the DbgEngThread).
        /// </summary>
        internal static Action GuestModeGuestThreadActivity;

        /// <summary>
        ///    In guest mode, when the user types "q", we don't actually shut everything
        ///    down. Instead, we block DbgShell in a special cmdlet and let the guest
        ///    thread return back to the debugger, thus keeping DbgShell "dormant". The
        ///    next time the user runs "!dbgshell" from the debugger, we just rejigger
        ///    the guest thread and then unblock DbgShell (that way you can keep state
        ///    such as variables around in between !dbgshell calls).
        ///
        ///    When the debugger extension gets unloaded is when it is time to actually
        ///    exit. When that happens, this bool gets set before the last time we get
        ///    entered. It is what lets "exit" know that we should actually exit (instead
        ///    of just go dormant).
        /// </summary>
        internal static bool GuestModeTimeToExitAllTheWay;

        /// <summary>
        ///    True if the DbgShell is being hosted by a debugger extension (DbgShellExt)
        ///    and therefore must make special efforts to cooperate with the controlling
        ///    debugger.
        /// </summary>
        public static bool IsInGuestMode { get { return null != GuestModeEvent; } }


        /// <summary>
        ///    This is kind of an awkward way to deal with !dbgshell commands that are run
        ///    when DbgShell.exe is the host (i.e. we are not in guest mode). If the
        ///    !dbgshell command resumes execution, then it needs to run an "exit"
        ///    PowerShell statement to terminate the current script. If we're in guest
        ///    mode, then the top-level input loop already recognizes that "exit" should
        ///    cause us to passivate (instead of fully exit); but if we're NOT in guest
        ///    mode, then we need some way to recognize that we should not exit "all the
        ///    way"--and this bool provides that flag.
        /// </summary>
        public static bool DontExitAllTheWay;


        private static int sm_entryDepth;


        /// <summary>
        ///    Tracks how many times we have recursively "entered" DbgShell. If
        ///    DbgShell.exe is the host, this should always be at least 1; if windbg is
        ///    the host and we are run from !dbgshell, it will be 0 while DbgShell is
        ///    "passive" (windbg is in control).
        /// </summary>
        public static int EntryDepth
        {
            get { return sm_entryDepth; }
            internal set { sm_entryDepth = value; }
        }


        private static bool sm_isInBreakpointCommand;

        /// <summary>
        ///    Indicates if we are running as part of a breakpoint command. This case must
        ///    be handled specially to avoid deadlock if we attempt to resume the target
        ///    from within the breakpoint command.
        /// </summary>
        public static bool IsInBreakpointCommand
        {
            get { return sm_isInBreakpointCommand;  }
            internal set { sm_isInBreakpointCommand = value; }
        }


        internal static IActionQueue CurrentActionQueue { get; set; }

        /// <summary>
        ///    Called when we get put on hold ("!dbgshell" returns, but we want DbgShell
        ///    to stay around in the background).
        /// </summary>
        internal static void GuestModePassivate()
        {
            DbgEngDebugger._GlobalDebugger.Passivate();
        }

        /// <summary>
        ///    Called when we get re-entered ("!dbgshell" is run a second time).
        /// </summary>
        internal static void GuestModeResume()
        {
            DbgEngDebugger._GlobalDebugger.ResetDbgEngThread();
            // Allow the main thread (which is blocked in "SetShouldExit", running
            // Wait-ForBangDbgShellCommand) to continue.
            GuestModeEvent.Set();
        }

        /// <summary>
        ///    Called when it's time to clean up all the way (before appdomain gets
        ///    unloaded).
        /// </summary>
        internal static void GuestModeFinalCleanup()
        {
            DbgEngDebugger._GlobalDebugger.ReleaseEverything();
        } // end GuestModeCleanup()

        #endregion GuestModeStuff


        public static object Thing; // For debugging.

        #region ClrMdTypeAdapterShims
        //
        // The following methods are used by the type adapters (Types.ps1xml) for the
        // ClrMd library.
        //

        public static IList< ClrStackFrame > GetClrStackTraceOnDbgEngThread( PSObject clrThread )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                {
                    return ((dynamic) clrThread).StackTrace;
                } );
        } // end GetClrStackTraceOnDbgEngThread()

        public static IList< BlockingObject > GetBlockingObjectsOnDbgEngThread( PSObject clrThread )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                {
                    return ((dynamic) clrThread).BlockingObjects;
                } );
        } // end GetBlockingObjectsOnDbgEngThread()

        public static IList< ClrThread > GetClrThreadsOnDbgEngThread( PSObject clrRuntime )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
                {
                    return ((dynamic) clrRuntime).Threads;
                } );
        } // end GetClrThreadsOnDbgEngThread()

        #endregion // ClrMdTypeAdapterShims


        private static char[] sm_badChars = _InitBadChars();

        private static char[] _InitBadChars()
        {
            char[] badChars = Path.GetInvalidFileNameChars();
            Array.Resize( ref badChars, badChars.Length + 1 );
            badChars[ badChars.Length - 1 ] = '`';
            return badChars;
        }

        /// <summary>
        ///    Replaces any occurrences of Path.GetInvalidFileNameChars with underscores.
        /// </summary>
        public static string SanitizeNamespaceName( string s )
        {
            int idx = s.IndexOfAny( sm_badChars );
            if( idx < 0 )
                return s;

            char[] chars = s.ToCharArray();
            while( idx >= 0 )
            {
                chars[ idx ] = '_';
                idx = s.IndexOfAny( sm_badChars, ++idx );
            }
            return new string( chars );
        } // end SanitizeNamespaceName()


        public static string EncodeString( string input )
        {
            return EncodeString( input, Encoding.UTF8 );
        }


        public static string EncodeString( string input, Encoding encoding )
        {
            if( null == input )
                return null;

            byte[] raw;
            byte[] preamble = encoding.GetPreamble();
            if( (null == preamble) || (0 == preamble.Length) )
            {
                raw = encoding.GetBytes( input );
            }
            else
            {
                int len = encoding.GetByteCount( input );
                raw = new byte[ preamble.Length + len ];
                Array.Copy( preamble, raw, preamble.Length );
                encoding.GetBytes( input, 0, input.Length, raw, preamble.Length );
            }
            return Convert.ToBase64String( raw );
        } // end Encode()


        public static string DecodeString( string input )
        {
            if( String.IsNullOrEmpty( input ) )
                return input;

            byte[] raw = Convert.FromBase64String( input );
            string encodingName = null;
            int numPreambleBytes = 0;

            if( (raw.Length >= 4) &&
                ((raw[ 0 ] == 0xff) && (raw[ 1 ] == 0xfe) && (raw[ 2 ] == 0) && (raw[ 3 ] == 0)) )
            {
                encodingName = "utf-32";
                numPreambleBytes = 4;
            }
            else if( (raw.Length >= 4) &&
                     ((raw[ 0 ] == 0) && (raw[ 1 ] == 0) && (raw[ 2 ] == 0xfe) && (raw[ 3 ] == 0xff)) )
            {
                encodingName = "utf-32BE";
                numPreambleBytes = 4;
            }
            else if( (raw.Length >= 3) &&
                     ((raw[ 0 ] == 0xef) && (raw[ 1 ] == 0xbb) && (raw[ 2 ] == 0xbf)) )
            {
                encodingName = "utf-8";
                numPreambleBytes = 3;
            }
            else if( (raw.Length >= 2) &&
                     ((raw[ 0 ] == 0xff) && (raw[ 1 ] == 0xfe)) )
            {
                encodingName = "utf-16";
                numPreambleBytes = 2;
            }
            else if( (raw.Length >= 2) &&
                     ((raw[ 0 ] == 0xfe) && (raw[ 1 ] == 0xff)) )
            {
                encodingName = "utf-16BE";
                numPreambleBytes = 2;
            }
            else
            {
                // Assume utf-16. I wish we could assume utf-8, but PowerShell's
                // -EncodedCommand stuff uses UTF-16.
                encodingName = "utf-16";
                numPreambleBytes = 0;
            }

            Encoding encoding = Encoding.GetEncoding( encodingName,
                                                      EncoderFallback.ExceptionFallback,
                                                      DecoderFallback.ExceptionFallback );

            return encoding.GetString( raw, numPreambleBytes, raw.Length - numPreambleBytes );
        } // end DecodeString()


        /// <summary>
        ///    Truncates a 64-bit unsigned integer to a 32-bit unsigned integer. If the
        ///    result is not numerically equivalent, it will throw (i.e. if the top 32
        ///    bits are not zero). The exception is if the top 32 bits are all set
        ///    (0xffffffff), and the top bit of the bottom 32 bits is set (i.e. if it
        ///    looks like a negative number, we'll un-sign-extend it).
        /// </summary>
        public static uint ConvertToUInt32( ulong val )
        {
            return _TruncateTo32( val, true );
        }


        /// <summary>
        ///    Truncates a 64-bit unsigned integer to a 32-bit unsigned integer,
        ///    regardless of whether the top 32 bits are zero or not.
        /// </summary>
        public static uint TruncateTo32( ulong val )
        {
            return _TruncateTo32( val, false );
        }


        private static uint _TruncateTo32( ulong val, bool throwIfNotEqual )
        {
            uint truncated = (uint) val;

            if( throwIfNotEqual && (((ulong) truncated) != val) )
            {
                // Maybe it looks like a sign-extended negative number, and we can
                // un-sign-extend it. (sign de-extend it? sign contract it?)
                uint top32 = (uint) (val >> 32);
                if( (top32 != UInt32.MaxValue) || (0 == (truncated & 0x80000000)) )
                {
                    throw new DbgProviderException( Util.Sprintf( "Conversion failed: high bits were set ({0}).",
                                                                  Util.FormatQWord( val ) ),
                                                    "ConvertToUInt32Failed",
                                                    ErrorCategory.InvalidArgument,
                                                    val );
                }
            }

            return truncated;
        } // end _TruncateTo32()


        /// <summary>
        ///    Truncates a 64-bit unsigned integer to an 8-bit unsigned integer. If the
        ///    result is not numerically equivalent, it will throw (i.e. if the top 56
        ///    bits are not zero). The exception is if the top 56 bits are all set
        ///    (0xffffffffff), and the top bit of the bottom 32 bits is set (i.e. if it
        ///    looks like a negative number, we'll un-sign-extend it).
        /// </summary>
        public static byte ConvertToUInt8( ulong val )
        {
            return _TruncateTo8( val, true );
        }


        /// <summary>
        ///    Truncates a 64-bit unsigned integer to an 8-bit unsigned integer,
        ///    regardless of whether the top 56 bits are zero or not.
        /// </summary>
        public static byte TruncateTo8( ulong val )
        {
            return _TruncateTo8( val, false );
        }


        private static byte _TruncateTo8( ulong val, bool throwIfNotEqual )
        {
            byte truncated = (byte) val;

            if( throwIfNotEqual && (((ulong) truncated) != val) )
            {
                // Maybe it looks like a sign-extended negative number, and we can
                // un-sign-extend it. (sign de-extend it? sign contract it?)
                ulong top56 = (val >> 8);
                if( (top56 != 0x00ffffffffffffff) || (0 == (truncated & 0x80)) )
                {
                    throw new DbgProviderException( Util.Sprintf( "Conversion failed: high bits were set ({0}).",
                                                                  Util.FormatQWord( val ) ),
                                                    "ConvertToUInt32Failed",
                                                    ErrorCategory.InvalidArgument,
                                                    val );
                }
            }

            return truncated;
        } // end _TruncateTo8()


        /// <summary>
        ///    Enumerates common locations where windows debuggers might be installed.
        /// </summary>
        private static IEnumerable< string > _EnumerateCommonDebuggersDirs()
        {
            string sysDrive = Path.GetPathRoot( Environment.SystemDirectory ); // like @"C:\"
            if( Environment.Is64BitOperatingSystem )
            {
                if( Environment.Is64BitProcess )
                {
                    yield return @"C:\Debuggers";
                    if( sysDrive[ 0 ] != 'C' )
                        yield return sysDrive + "Debuggers";

                    string progFiles = Environment.GetFolderPath( Environment.SpecialFolder.ProgramFiles );

                    yield return Path.Combine( progFiles, @"Debugging Tools for Windows" );
                    yield return Path.Combine( progFiles, @"Debugging Tools for Windows (x64)" );
                }
                else
                {
                    yield return @"C:\Debuggers32";
                    yield return @"C:\Debuggers\wow64";

                    if( sysDrive[ 0 ] != 'C' )
                    {
                        yield return sysDrive + @"Debuggers32";
                        yield return sysDrive + @"Debuggers\wow64";
                    }

                    string progFiles = Environment.GetFolderPath( Environment.SpecialFolder.ProgramFilesX86 );

                    yield return Path.Combine( progFiles, @"Debugging Tools for Windows" );
                    yield return Path.Combine( progFiles, @"Debugging Tools for Windows (x86)" );
                }
            }
            else
            {
                //
                // 32-bit system.
                //

                yield return @"C:\Debuggers";
                if( sysDrive[ 0 ] != 'C' )
                    yield return sysDrive + "Debuggers";

                string progFiles = Environment.GetFolderPath( Environment.SpecialFolder.ProgramFiles );

                yield return Path.Combine( progFiles, @"Debugging Tools for Windows" );
                yield return Path.Combine( progFiles, @"Debugging Tools for Windows (x86)" );
            }
        } // end _EnumerateCommonDebuggersDirs()


        /// <summary>
        ///    Looks for the existence of common debuggers directories and returns the
        ///    first one it finds, or null.
        /// </summary>
        internal static string _TryFindDebuggersDir()
        {
            foreach( string candidate in _EnumerateCommonDebuggersDirs() )
            {
                if( Directory.Exists( candidate ) )
                    return candidate;
            }

            //
            // Maybe it's someplace else, but on the %path%.
            //
            // A problem with this is that we aren't checking if it's the right bitness.
            //

            foreach( string candidate in Environment.GetEnvironmentVariable( "PATH" ).Split( ';' ) )
            {
                // Only relatively recent debugger installs (circa 2015) will have
                // DbgCore.dll and DbgModel.dll

                if( Directory.Exists( candidate ) &&
                    File.Exists( Path.Combine( candidate, "windbg.exe" ) ) &&
                    File.Exists( Path.Combine( candidate, "kd.exe" ) ) &&
                    File.Exists( Path.Combine( candidate, "DbgCore.dll" ) ) &&
                    File.Exists( Path.Combine( candidate, "DbgModel.dll" ) ) )
                {
                    return candidate;
                }
            }

            return null;
        }
    } // end class DbgProvider
}

