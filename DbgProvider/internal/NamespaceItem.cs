using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace MS.Dbg
{
    /// <summary>
    ///    Indicates that an item with the same name already exists in the virtual namespace.
    /// </summary>
    internal class ItemExistsException : System.IO.IOException
    {
        public string ItemName { get; private set; }
        public string FullPath { get; private set; }

        private static string _CalcFullPath( NamespaceItem parent, string name )
        {
            if( null == parent )
                throw new ArgumentNullException( "parent" );

            if( String.IsNullOrEmpty( name ) )
                throw new ArgumentException( "You must supply a name.", "name" );

            return parent.ComputePath( false ) + "\\" + name;
        }

        public ItemExistsException( string name, NamespaceItem parent )
            : this( name, _CalcFullPath( parent, name ) )
        {
        }

        public ItemExistsException( string name, string path )
            : base( Util.Sprintf( "There is already an item named '{0}'.", name ) )
        {
            if( String.IsNullOrEmpty( name ) )
                throw new ArgumentException( "You must supply a name.", "name" );

            if( String.IsNullOrEmpty( path ) )
                throw new ArgumentException( "You must supply a path.", "path" );

            ItemName = name;
            FullPath = path;
        } // end constructor
    } // end class ItemExistsException


    internal class InvalidItemNameException : System.IO.IOException
    {
        public string InvalidName { get; private set; }

        private static string _GetMessage( string invalidName )
        {
            if( null == invalidName )
                throw new ArgumentNullException( "invalidName" );

            return Util.Sprintf( "The name \"{0}\" contains characters that cannot be used in an item name.",
                                 invalidName );
        }

        public InvalidItemNameException( string invalidName )
            : base( _GetMessage( invalidName ) )
        {
            InvalidName = invalidName;
        }
    } // end class InvalidItemNameException


    /// <summary>
    ///    Represents items in a virtual namespace.
    /// </summary>
    [DebuggerDisplay( "NamespaceItem: {Name}" )]
    internal abstract class NamespaceItem : TreeNode< NamespaceItem >
    {
        /// <summary>
        ///    The name of the item.
        /// </summary>
        /// <remarks>
        ///    For all namespace items except the root, the Name of the item is what
        ///    you would see in a path string for that item. For the root item, the
        ///    path is always "" (string.Empty). (and the path string "\" is equiv.)
        /// </remarks>
        public string Name { get; private set; }


        protected NamespaceItem( string name ) : this( name, null ) { }

        protected NamespaceItem( string name, NamespaceItem parent ) : this( name, parent, false ) { }


        private static void _CheckValidName( string name )
        {
            if( null == name )
                throw new ArgumentNullException( "name" );

            if( (0 == name.Length) ||
                (-1 != name.IndexOfAny( System.IO.Path.GetInvalidFileNameChars() )) )
            {
                throw new InvalidItemNameException( name );
            }
        } // end _CheckValidName()

        /// <summary>
        ///    Constrains NamespaceItem trees to not have duplicate-named items in the same container,
        ///    verifies the name doesn't contain invalid name characters, etc.
        /// </summary>
        /// <returns>
        ///    The parent object.
        /// </returns>
        private static NamespaceItem _PreLinkValidation( string name, NamespaceItem parent, bool ephemeral )
        {
            if( String.IsNullOrEmpty( name ) )
                throw new ArgumentException( "You must supply a name.", "name" );

            _CheckValidName( name );

            // Ephemeral items are likely created during EnumerateChildren, we don't want to go
            // into infinite recursion here.
            if( (null != parent) && !ephemeral )
            {
                // TODO: pretty inefficient when populating a large directory. Look into using a set instead.
                foreach( var child in parent.EnumerateChildItems() )
                {
                    if( 0 == Util.Strcmp_OI( child.Name, name ) )
                    {
                        throw new ItemExistsException( name, parent );
                    }
                }
            }

            return parent;
        } // end _PreLinkValidation()

        protected NamespaceItem( string name, NamespaceItem parent, bool ephemeral )
            : base( null, _PreLinkValidation( name, parent, ephemeral ), ephemeral )
        {
            // We need to perform validation in _PreLinkValidation, before calling the
            // base constructor, because otherwise, we'll already be linked into the
            // tree.

            // We can't pass "this" to the base constructor in the initializer, so
            // we'll have to manually set Item here.
            Item = this;
            Name = name;
        }


        public bool TryWalkTreeRelative( string path, out NamespaceItem dest )
        {
            dest = null;
            List< NamespaceItem > nsPathItems = null;
            if( !TryWalkTreeRelative( path, ref nsPathItems ) )
            {
                return false;
            }
            else
            {
                Util.Assert( (null != nsPathItems) && (nsPathItems.Count >= 1) );
                dest = nsPathItems[ nsPathItems.Count - 1 ];
                return true;
            }
        } // end TryWalkTreeRelative()


        public bool TryWalkTreeRelative( string path, ref List< NamespaceItem > nsPathItems )
        {
            if( null == path )
                throw new ArgumentNullException( "path" );

            if( null == nsPathItems )
                nsPathItems = new List< NamespaceItem >();

            if( path.StartsWith( "\\" ) )
            {
                // Not actually invariant, since we don't control the caller, but a well-formed
                // caller should never pass this.
                // Update: We can indeed get into this situation if the user types something
                // like "cd Debugger::\\".
                // The Certificate provider can handle things like that ("cd Certificate::\\\\\" works),
                // But the Registry provider doesn't. I don't think we need to tolerate that kind of thing.
                // If we did, we could uncomment this assert to find the stack where it occurs, as a starting
                // point to figure out where we would need to make a change.
                //Util.Fail( "I don't think this should happen." );
                //
                // Also, we can get here when the current directory does not belong to the
                // FileSystem provider, and we're trying to run things from a network
                // share. (Even if we properly prefixed the path with "filesystem::", PS
                // apparently still has some bugs where it will try to call into the
                // current provider to resolve the path. TODO: file bugs.
                //
                // You can set a breakpoint here and then run off a network share to see
                // all the places that happens.

                // Interestingly, the user will /usually/ not see this exception--PS will
                // end up handling and throwing its own "cannot find path '\\\' because it
                // does not exist" exception.
                throw new ArgumentException( Util.Sprintf( "A relative path should not start with a whack. ({0})", path ), "path" );
            }

            nsPathItems.Add( this );

            if( 0 == path.Length )
            {
                return true;
            }

            string car, cdr;
            Util.SplitPath( path, out car, out cdr );
            NamespaceItem child;
            if( !TryFindChild( car, out child ) )
            {
                //Console.WriteLine( "[{0}] No such child '{1}'.", Name, car );
                // Not nulling out pathStack allows the caller to see how far
                // we got before the trail went cold.
                //pathStack = null;
                return false; // no such child
            }
            return child.TryWalkTreeRelative( cdr, ref nsPathItems );
        } // end TryWalkTreeRelative()


        public virtual bool TryFindChild( string childName, out NamespaceItem child )
        {
            child = null;
            foreach( var candidate in EnumerateChildItems() )
            {
                if( 0 == Util.Strcmp_OI( childName, candidate.Name ) )
                {
                    child = candidate;
                    return true;
                }
            }
            return false;
        } // end FindChild()


        /// <summary>
        ///    Computes the path string to this object by following Parent nodes up to the root.
        /// </summary>
        public string ComputePath( bool registryProviderStyle )
        {
            NamespaceItem item = this;
            Stack< string > pathStack = new Stack< string >();
            // We go while item.Parent is not null, rather than while item is not null,
            // because the root item will have a name, but we need to use String.Empty
            // for the root instead. (The root of the virtual namespace is always String.Empty.)
            while( null != item.Parent )
            {
                pathStack.Push( item.Name );
                item = item.Parent.Item;
            }
            // The non-registry provider style makes a lot more sense to me. But it causes
            // weirdness with globbing at the root of the namespace--for instance, if you
            // are at the root, and you type "dir .\Pro[tab]", you'll end up seeing "dir
            // .\\Process" (note the extra whack).  Removing the root whack by passing
            // 'true' for registryProviderStyle is just part of the change to enable
            // behavior more like the Registry provider (wherein tab completion for files
            // at the root does not work when CWD is a provider-qualified path that is not
            // at the root).
            if( !registryProviderStyle )
                pathStack.Push( String.Empty ); // for the root of the virtual namespace

            return String.Join( @"\", pathStack );
        } // end ComputePath()


        public abstract bool IsContainer { get; }

        public bool HasChildren
        {
            get
            {
                if( !IsContainer )
                    return false;

                using( var iter = EnumerateChildItems().GetEnumerator() )
                {
                    return iter.MoveNext();
                }
            }
        }


        /// <summary>
        ///    Returns true if this NamespaceItem is a parent of the specified item.
        /// </summary>
        public bool Contains( NamespaceItem nsItem )
        {
            if( null == nsItem )
                throw new ArgumentNullException( "nsItem" );

            string myPath = ComputePath( false );
            string itsPath = nsItem.ComputePath( false );

            return itsPath.StartsWith( myPath, StringComparison.OrdinalIgnoreCase );
        } // end Contains()


        /// <summary>
        ///    Copies the current item to the specified destination, with the specified name.
        /// </summary>
        /// <param name="destContainer">
        ///    The destination to copy to.
        /// </param>
        /// <param name="destName">
        ///    The name of the newly-copied item, or null to use the current name.
        /// </param>
        /// <returns>
        ///    The copied item.
        /// </returns>
        public NamespaceItem CopyTo( NamespaceItem destContainer,
                                     string destName,
                                     bool recurse,
                                     Action< Exception, string > onCopyChildError,
                                     Func< bool > stopping )
        {
            if( null == destContainer )
                throw new ArgumentNullException( "destContainer" );

            if( !destContainer.IsContainer )
                throw new InvalidOperationException( Util.Sprintf( "The destination ({0}) is not a container.",
                                                                   destContainer.ComputePath( false ) ) );

            if( null == destName )
                destName = this.Name;

            if( (destContainer == Parent) && (0 == Util.Strcmp_OI( this.Name, destName )) )
                throw new InvalidOperationException( Util.Sprintf( "You cannot copy an item onto itself ({0}).",
                                                                   ComputePath( false ) ) );

            if( Contains( destContainer ) )
                throw new InvalidOperationException( "Cannot copy an item into itself." );

            _CheckValidName( destName );

            NamespaceItem unused;
            if( destContainer.TryFindChild( destName, out unused ) )
                throw new ItemExistsException( destName, destContainer );

            return CopyToWorker( destContainer,
                                 destName,
                                 recurse,
                                 onCopyChildError,
                                 stopping );
        } // end CopyTo()


        // Should return the copied item.
        protected virtual NamespaceItem CopyToWorker( NamespaceItem destContainer,
                                                      string destName,
                                                      bool recurse,
                                                      Action< Exception, string > onCopyChildError,
                                                      Func< bool > stopping )
        {
            throw new NotSupportedException( Util.Sprintf( "This object type ({0}) does not support copying.",
                                                           Util.GetGenericTypeName( this ) ) );
        } // end CopyToWorker()

        /// <summary>
        ///    Moves the current item to the specified destination, with the specified name.
        /// </summary>
        /// <param name="destContainer">
        ///    The destination to move to.
        /// </param>
        /// <param name="destName">
        ///    The name of the newly-moved item, or null to use the current name.
        /// </param>
        /// <returns>
        ///    The copied item.
        /// </returns>
        public virtual NamespaceItem MoveTo( NamespaceItem destContainer, string destName )
        {
            if( null == destContainer )
                throw new ArgumentNullException( "destContainer" );

            if( !destContainer.IsContainer )
                throw new InvalidOperationException( Util.Sprintf( "The destination ({0}) is not a container.",
                                                                   destContainer.ComputePath( false ) ) );

            if( null == destName )
                destName = this.Name;

            // Or should we just no-op it?
            if( (destContainer == Parent) && (0 == Util.Strcmp_OI( this.Name, destName )) )
                throw new InvalidOperationException( Util.Sprintf( "You cannot move an item onto itself ({0}).",
                                                                   ComputePath( false ) ) );

            if( Contains( destContainer ) )
                throw new InvalidOperationException( "Cannot move an item into itself." );

            _CheckValidName( destName );

            NamespaceItem unused;
            if( destContainer.TryFindChild( destName, out unused ) )
                throw new ItemExistsException( destName, destContainer );

            Unlink();

            Name = destName;
            destContainer.AddChild( this );

            return this;
        } // end MoveTo()

        public virtual void Rename( string newName )
        {
            if( null == newName )
                throw new ArgumentNullException( "newName" );

            _CheckValidName( newName );

            if( null != Parent )
            {
                NamespaceItem unused;
                if( Parent.Item.TryFindChild( newName, out unused ) )
                    throw new ItemExistsException( newName, Parent.Item );
            }

            Name = newName;
        } // end Rename()

        public bool TryFindChildAs< TChild >( string childName, out TChild nsChild ) where TChild : NamespaceItem
        {
            nsChild = null;
            NamespaceItem nsItem;
            if( !TryFindChild( childName, out nsItem ) )
                return false;

            nsChild = nsItem as TChild;
            return nsChild != null;
        } // end TryFindChildAs()

        internal bool FromDataBinding;


        /// <summary>
        ///    Gets the [NsLeafItem] attribute for a given MemberInfo, if it exists (else
        ///    null).
        /// </summary>
        protected static NsLeafItemAttribute _TryGetLeafAttr( MemberInfo mi )
        {
            return (NsLeafItemAttribute) mi.GetCustomAttributes( typeof( NsLeafItemAttribute ),
                                                                 inherit: true
                                                               ).FirstOrDefault();
        }

        /// <summary>
        ///    Gets the [NsContainer] attribute for a given MemberInfo, if it exists (else
        ///    null).
        /// </summary>
        protected static NsContainerAttribute _TryGetContainerAttr( MemberInfo mi )
        {
            return (NsContainerAttribute) mi.GetCustomAttributes( typeof( NsContainerAttribute ),
                                                                  inherit: true
                                                                ).FirstOrDefault();
        }


        /// <summary>
        ///    Creates a Namespace item for an object. The object's class must be
        ///    decorated with an [NsLeafItem] or [NsContainer] attribute.
        /// </summary>
        public static NamespaceItem CreateItemForObject( object obj,
                                                         NamespaceItem parent,
                                                         bool ephemeral )
        {
            Util.Assert( null != obj );
            Type t = obj.GetType();

            //
            // Is it a leaf item?
            //

            var itemAttr = _TryGetLeafAttr( t );
            if( null != itemAttr )
            {
                string name = itemAttr.GetNameForObject( obj );

                if( String.IsNullOrEmpty( name ) )
                {
                    throw new InvalidOperationException( Util.Sprintf( "Cannot directly create a NamespaceItem for {0} because the name can't be calculated. Either set the NameProperty on the attribute or add an overload here takes the name.",
                                                                       Util.GetGenericTypeName( t ) ) );
                }

                Func< List< object >, int, ColorString > summarizeFunc = null;
                if( !String.IsNullOrEmpty( itemAttr.SummarizeFunc ) )
                {
                    var methodInfo = t.GetMethod( itemAttr.SummarizeFunc,
                                                  BindingFlags.Public |
                                                      BindingFlags.NonPublic |
                                                      BindingFlags.Static );
                    Util.Assert( null != methodInfo );
                    summarizeFunc = ( content, maxWidth ) => Util.UnwrapTargetInvocationException( () =>
                                (ColorString) (methodInfo.Invoke( null, new object[] { content, maxWidth } )) );
                }

                return NsLeaf.CreateNsLeaf( name,
                                            parent,
                                            obj,
                                            ephemeral,
                                            false, // fromDataBinding
                                            summarizeFunc );
            }

            //
            // Nope... it must be a container item.
            //
            var containerAttr = _TryGetContainerAttr( t );

            if( null != containerAttr )
            {
                string name = containerAttr.GetNameForObject( obj );
                if( String.IsNullOrEmpty( name ) )
                {
                    throw new InvalidOperationException( Util.Sprintf( "Cannot directly create a NamespaceItem container for {0} because the name can't be calculated. Either set the NameProperty on the attribute or add an overload here takes the name.",
                                                                       Util.GetGenericTypeName( t ) ) );
                }

                var c = NsContainer.CreateStronglyTyped( name, parent, obj, ephemeral );
                c.ChildrenMember = containerAttr.ChildrenMember;

                return c;
            }

            throw new InvalidOperationException( Util.Sprintf( "Cannot directly create a NamespaceItem for a {0} because it does not have [NsLeafItem] or [nsContainer] attribute",
                                                               Util.GetGenericTypeName( obj ) ) );
        } // end CreateItemForObject()
    } // end class NamespaceItem


    /// <summary>
    ///    Base class for namespace databinding attributes.
    /// </summary>
    /// <remarks>
    ///    Items can be added to the PowerShell namespace by using attributes. See
    ///    DbgUModeProcess for an example.
    /// </remarks>
    public abstract class NsItemAttribute : Attribute
    {
        /// <summary>
        ///    (OPTIONAL) The name of the item. This does not need to be set, as there are
        ///    other ways the name can be determined--such as via the name of the member
        ///    that the attribute is applied to, or the NameProperty property.
        /// </summary>
        public readonly string Name;

        /// <summary>
        ///    (Optional) The name of the property (or field) whose value should be used
        ///    for the name of the item.
        /// </summary>
        public string NameProperty { get; set; }


        protected NsItemAttribute() : this( null )
        {
        }

        protected NsItemAttribute( string name )
        {
            Name = name;
        }


        protected static string _ObjToString( object obj )
        {
            if( null == obj )
                return null;

            string str = obj as string;
            if( null != str )
                return str;

            return obj.ToString();
        } // end _ObjToString()


        internal string GetNameForObject( object obj )
        {
            Util.Assert( null != obj );

            if( !String.IsNullOrEmpty( Name ) )
                return Name;

            if( !String.IsNullOrEmpty( NameProperty ) )
            {
                Type t = obj.GetType();
                var pi = t.GetProperty( NameProperty );
                if( null != pi )
                {
                    string str = _ObjToString( pi.GetValue( obj ) );
                    if( String.IsNullOrEmpty( str ) )
                        LogManager.Trace( "Uh-oh... GetNameForObject: NameProperty gave us nuthin'." );

                    return str;
                }

                var fi = t.GetField( NameProperty );
                if( null != fi )
                {
                    string str = _ObjToString( fi.GetValue( obj ) );
                    if( String.IsNullOrEmpty( str ) )
                        LogManager.Trace( "Uh-oh... GetNameForObject: NameProperty (field) gave us nuthin'." );

                    return str;
                }
            }

            // The name might be coming from elsewhere, such as a field name.
            return null;
        } // end GetNameForObject()
    } // end class NsItemAttribute


    /// <summary>
    ///    This attribute is used for simple databinding to make it a little easier to
    ///    populate the PowerShell namespace. Apply it to something that should be a
    ///    container in the PowerShell debugger namespace.
    /// </summary>
    [AttributeUsage( AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method,
                     AllowMultiple = false )]
    public class NsContainerAttribute : NsItemAttribute
    {
        /// <summary>
        ///    (Optional) The name of the member whose value(s) should be the children of
        ///    the container. (This effectively lets you omit a "level" from the object
        ///    heirarchy in the namespace heirarchy--for example, the "Stack" directory
        ///    does not need to have a "Frames" subdirectory that contains the frames; it
        ///    can just contain the frames directly, using this property.)
        /// </summary>
        public string ChildrenMember { get; set; }


        public NsContainerAttribute() : this( null )
        {
        }

        public NsContainerAttribute( string name )
            : base( name )
        {
        }
    } // end class NsContainerAttribute


    /// <summary>
    ///    This attribute is used for simple databinding to make it a little easier to
    ///    populate the PowerShell namespace. Apply it to something that should be a
    ///    leaf item (non-container) in the PowerShell debugger namespace.
    /// </summary>
    [AttributeUsage( AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field,
                     AllowMultiple = false )]
    public class NsLeafItemAttribute : NsItemAttribute
    {
        /// <summary>
        ///    Indicates the name of a public or private static method that takes a
        ///    List&lt; object &gt; (content) and int (maxWidth), and returns a
        ///    ColorString.
        /// </summary>
        public string SummarizeFunc { get; set; }

        public NsLeafItemAttribute() : this( null )
        {
        }

        public NsLeafItemAttribute( string name )
            : base( name )
        {
        }
    } // end class NsLeafItemAttribute


    [DebuggerDisplay( "NsContainer: {Name}" )]
    internal class NsContainer : NamespaceItem
    {
        public override bool IsContainer { get { return true; } }

        // Used to propogate a ChildrenMember attribute value from an attribute on an
        // [NsContainer] attribute that is applied to a member (as opposed to one applied
        // to a class).
        internal string ChildrenMember;

        protected object m_representedObject;

        public object RepresentedObject { get { return m_representedObject; } }

        public NsContainer( string name, NamespaceItem parent, object representedObject )
            : this( name, parent, representedObject, false )
        {
        }


        protected NsContainer( string name )
            : base( name )
        {
        }

        public NsContainer( string name,
                            NamespaceItem parent,
                            object representedObject,
                            bool ephemeral )
            : base( name, parent, ephemeral )
        {
            if( null == parent )
                throw new ArgumentNullException( "parent" );

            // Sometimes a container is JUST a container (like the "Process" dir, or
            // "Threads" dir, which represent a collection of processes or threads), so we
            // don't do a null check for this.
            m_representedObject = representedObject;
        }

        protected override NamespaceItem CopyToWorker( NamespaceItem destContainer,
                                                       string destName,
                                                       bool recurse,
                                                       Action< Exception, string > onCopyChildError,
                                                       Func< bool > stopping )
        {
            NsContainer nsCopy = NsContainer.CreateStronglyTyped( destName, destContainer, m_representedObject );
            return nsCopy;
        } // end CopyToWorker()


        private static object _GetValueForMemberInfo( object obj, MemberInfo mi )
        {
            return Util.UnwrapTargetInvocationException( () =>
                {
                    Util.Assert( null != obj );
                    object val = null;
                    PropertyInfo pi = mi as PropertyInfo;
                    if( null != pi )
                    {
                        val = pi.GetValue( obj );
                    }
                    else
                    {
                        System.Reflection.FieldInfo fi = mi as System.Reflection.FieldInfo;
                        if( null != fi )
                        {
                            val = fi.GetValue( obj );
                        }
                        else
                        {
                            MethodInfo methodInfo = mi as MethodInfo;
                            if( null != methodInfo )
                            {
                                //return _CreateContainerItemForMethod( containerAttr, methodInfo );
                                // TODO: screen for or otherwise produce a nicer error when the
                                // method is not parameter-less.
                                val = methodInfo.Invoke( obj, null );
                            }
                            else
                            {
                                // TODO: Is this error sufficient? For instance it can happen for
                                // bad use of the ChildrenMember property.
                                throw new ArgumentException( Util.Sprintf( "The [NsContainer/NsLeaf] attribute is only supported on field, property, or method members (actual MemberInfo type: {0}).",
                                                                           Util.GetGenericTypeName( mi ) ) );
                            }
                        }
                    }
                    return val;
                } );
        } // end _GetValueForMemberInfo()


        private NsLeaf _CreateLeafItemForMember( NsLeafItemAttribute itemAttr, MemberInfo mi )
        {
            Util.Assert( null != m_representedObject );

            string name = itemAttr.GetNameForObject( m_representedObject ); // TODO: seems wrong... why are we passing the parent object?
            // but we wouldn't want to get the value for the mi, because hey, it's supposed to be lazy...

            if( String.IsNullOrEmpty( name ) )
                name = mi.Name;
            else
                Debugger.Break();

            return new NsLazyLeaf( name, this, true, () =>
                {
                    object val = _GetValueForMemberInfo( m_representedObject, mi );

                    Func< List< object >, int, ColorString > summarizeFunc = null;
                    if( !String.IsNullOrEmpty( itemAttr.SummarizeFunc ) )
                    {
                        // In this case, SummarizeFunc must be referring to a method on the type
                        // that contains the member.
                        var methodInfo = mi.ReflectedType.GetMethod( itemAttr.SummarizeFunc,
                                                                     BindingFlags.Public |
                                                                         BindingFlags.NonPublic |
                                                                         BindingFlags.Static |
                                                                         BindingFlags.FlattenHierarchy );
                        Util.Assert( null != methodInfo );
                        // TODO: We don't handle the case where there is also an [NsLeafItem]
                        // attribute on the val's type.
                        summarizeFunc = ( content, maxWidth ) => Util.UnwrapTargetInvocationException( () =>
                                    (ColorString) (methodInfo.Invoke( null, new object[] { content, maxWidth } )) );
                    }

                    // Note that we pass "null" for the parent: the outer NsLazyLeaf is
                    // what hooks it into the namespace; the actual leaf (when created)
                    // just holds the data.
                    return NsLeaf.CreateNsLeaf( name, null, val, true, summarizeFunc );
                } );
        } // end _CreateLeafItemForMember()


        private NsContainer _CreateContainerItemForMember( NsContainerAttribute containerAttr, MemberInfo mi )
        {
            Util.Assert( null != m_representedObject );
            object val = _GetValueForMemberInfo( m_representedObject, mi );

            // If a member has a [NsContainer] attribute:
            // If value is null, do nothing.
            // If value type also has an [NsContainer] attribute
            // {
            //     Name comes from:
            //         1) member attr.Name
            //         2) member attr.NameProperty
            //         3) type attr.Name
            //         4) type attr.NameProperty
            //         5) member name
            // }
            // else If value is IEnumerable
            // {
            //     Name comes from:
            //         1) member attr.Name
            //         2) member name
            // }

            if( null == val )
                return null;

            string name = containerAttr.GetNameForObject( val );
            NsContainerAttribute containerAttr2 = null;
            if( String.IsNullOrEmpty( name ) )
            {
                // There might also be a [NsContainer] attribute on the member type.
                var valType = val.GetType();
                containerAttr2 = (NsContainerAttribute) valType.GetCustomAttributes( typeof( NsContainerAttribute ), true ).FirstOrDefault();
                if( null != containerAttr2 )
                {
                    name = containerAttr2.GetNameForObject( val );
                }
            }
            if( String.IsNullOrEmpty( name ) )
            {
                name = mi.Name;
            }
            var c = NsContainer.CreateStronglyTyped( name, this, val );
            c.FromDataBinding = true;
            c.ChildrenMember = containerAttr.ChildrenMember;
            if( String.IsNullOrEmpty( c.ChildrenMember ) && (null != containerAttr2) )
                c.ChildrenMember = containerAttr2.ChildrenMember;

            return c;
        } // end _CreateContainerItemForMember()


        private void _CreateChildrenFromObject( object obj )
        {
            Util.Assert( null != obj );
            Type t = obj.GetType();
            foreach( var mi in t.GetMembers() )
            {
                var itemAttr = _TryGetLeafAttr( mi );
                if( null != itemAttr )
                {
                    _CreateLeafItemForMember( itemAttr, mi );
                }

                var containerAttr = _TryGetContainerAttr( mi );
                if( null != containerAttr )
                {
                    _CreateContainerItemForMember( containerAttr, mi );
                }
            } // end foreach( member )

            if( t.GetInterfaces().Any( ( x ) => x.IsGenericType && (x.GetGenericTypeDefinition() == typeof( IEnumerable<> )) ) )
            {
                // I /was/ going to check for a NsItemAttribute, but I think we can skip it.
                //Type tItem = t.GetGenericArguments()[ 0 ];
                //foreach( object obj in ((dynamic) obj).GetEnumerator() )
                foreach( object childObj in ((dynamic) obj) )
                {
                    if( null != childObj )
                        CreateChild( childObj, true );
                }
            }
        } // end _CreateChildrenFromObject()


        private bool m_populatedChildren;

        // We want to lazily create the children, so we'll implement the necessary methods as
        // if the children were ephemeral, but we can persist them ourselves here.
        public override IEnumerable< TreeNode< NamespaceItem > > EnumerateChildren()
        {
            if( !m_populatedChildren )
            {
                // We need to immediately set m_populatedChildren to true to prevent
                // infinite recursion. But if we hit an exception, we'll unset it so that
                // we can keep trying (good for debugging, anyway).
                m_populatedChildren = true;
                bool noException = false;

                try
                {
                    if( null != m_representedObject ) // for namespace root?
                    {
                        if( String.IsNullOrEmpty( ChildrenMember ) )
                        {
                            _CreateChildrenFromObject( m_representedObject );
                        } // end else( no ChildrenMember )
                        else
                        {
                            Type t = m_representedObject.GetType();
                            // TODO: Hmm... hopefully there aren't multiple overloads?
                            var mi = t.GetMember( ChildrenMember ).FirstOrDefault();
                            if( null == mi )
                            {
                                throw new ArgumentException( Util.Sprintf( "No such ChildrenMember member: {0}",
                                                                           ChildrenMember ) );
                            }
                            var val = _GetValueForMemberInfo( m_representedObject, mi );
                            _CreateChildrenFromObject( val );
                        } // end if( !ChildrenMember )
                    } // end if( m_representedObject )

                    if( null != m_nonDataBoundItemsToPreserve )
                    {
                        foreach( var item in m_nonDataBoundItemsToPreserve )
                        {
                            this.AddChildItem( item );
                        }
                        m_nonDataBoundItemsToPreserve.Clear();
                    }
                    noException = true;
                }
                finally
                {
                    if( !noException )
                    {
                        // We might have made it partway through. To prevent future
                        // attempts at enumeration from blowing up due to "child already
                        // exists" errors, let's clean the slate.
                        _RemoveDataBoundChildren(); // will set m_populatedChildren to false.
                    }
                }
            } // end if( !m_populatedChildren )

            return base.EnumerateChildren();
        } // end EnumerateChildren()

        public NamespaceItem CreateChild( object representsChild )
        {
            return CreateChild( representsChild, false );
        }

        internal NamespaceItem CreateChild( object representsChild, bool fromDataBinding )
        {
            if( null == representsChild )
                throw new ArgumentNullException( "representsChild" );

            NamespaceItem newNsItem = null;
            Type t = representsChild.GetType();
            var containerAttr = _TryGetContainerAttr( t );
            if( null != containerAttr )
            {
                string containerName = containerAttr.GetNameForObject( representsChild );
                if( String.IsNullOrEmpty( containerName ) )
                    containerName = representsChild.ToString();
                newNsItem = NsContainer.CreateStronglyTyped( containerName, this, representsChild ); // constructor hooks it into the namespace.
                newNsItem.FromDataBinding = fromDataBinding;
                return newNsItem;
            }

            var itemAttr = _TryGetLeafAttr( t );
            if( null != itemAttr )
            {
                string itemName = itemAttr.GetNameForObject( representsChild );
                if( String.IsNullOrEmpty( itemName ) )
                    itemName = representsChild.ToString();

                Func< List< object >, int, ColorString > summarizeFunc = null;
                if( !String.IsNullOrEmpty( itemAttr.SummarizeFunc ) )
                {
                    var methodInfo = t.GetMethod( itemAttr.SummarizeFunc,
                                                  BindingFlags.Public |
                                                      BindingFlags.NonPublic |
                                                      BindingFlags.Static );

                    Util.Assert( null != methodInfo );
                    summarizeFunc = ( content, maxWidth ) => Util.UnwrapTargetInvocationException( () =>
                         (ColorString) (methodInfo.Invoke( null, new object[] { content, maxWidth } )) );
                }
                newNsItem = NsLeaf.CreateNsLeaf( itemName,
                                                 this,
                                                 representsChild,
                                                 fromDataBinding,
                                                 summarizeFunc );
                return newNsItem;
            }

            var e = new Exception( Util.Sprintf( "Could not create NsItem for child: {0}", representsChild.ToString() ) );
            // This doesn't work if the object isn't ISerializable.
            //e.Data[ "ObjectInQuestion" ] = representsChild;
            DbgProvider.Thing = representsChild;
            throw e;
        } // end CreateChild()


        List< NamespaceItem > m_nonDataBoundItemsToPreserve;

        // Maybe it should be named LazyRefresh? Because it doesn't actually refresh
        // immediately; it just discards children that came from data binding.
        public void Refresh()
        {
            if( !m_populatedChildren )
                return;

            if( null == m_nonDataBoundItemsToPreserve )
                m_nonDataBoundItemsToPreserve = new List< NamespaceItem >();

            // Because if we've populated children, these should have been moved into our
            // children collection.
            Util.Assert( 0 == m_nonDataBoundItemsToPreserve.Count );

            if( null == m_representedObject ) // for namespace root?
            {
                foreach( var item in base.EnumerateChildItems() )
                {
                    var container = item as NsContainer;
                    if( null != container )
                        container.Refresh();
                }
            } // end if( !m_representedObject )
            else
            {
                List< NamespaceItem > toRemove = new List< NamespaceItem >();
                foreach( var item in base.EnumerateChildItems() )
                {
                    if( item.FromDataBinding )
                        toRemove.Add( item );
                    else
                        m_nonDataBoundItemsToPreserve.Add( item );
                }

                foreach( var item in toRemove )
                {
                    item.Unlink();
                }
            }
            m_populatedChildren = false;
        } // end Refresh()


        /// <summary>
        ///    Removes data-bound children, preserves non-data-bound children.
        /// </summary>
        // TODO: Should Refresh call this?
        private void _RemoveDataBoundChildren()
        {
            if( null == m_nonDataBoundItemsToPreserve )
                m_nonDataBoundItemsToPreserve = new List< NamespaceItem >();

            List< NamespaceItem > toRemove = new List< NamespaceItem >();
            foreach( var item in base.EnumerateChildItems() )
            {
                if( item.FromDataBinding )
                    toRemove.Add( item );
                else
                    m_nonDataBoundItemsToPreserve.Add( item );
            }

            foreach( var item in toRemove )
            {
                item.Unlink();
            }

            m_populatedChildren = false;
        } // end _RemoveDataBoundChildren()


        private static Type _CreateStrongNsContainerTypeFor( object representedObject, out Type genericParam )
        {
            Type t;
            if( null == representedObject )
                t = typeof( object );
            else
                t = representedObject.GetType();

            genericParam = t;
            return typeof( NsContainer<> ).MakeGenericType( t );
        } // end _CreateStrongNsContainerTypeFor()

        public static NsContainer CreateStronglyTyped( string name,
                                                       NamespaceItem parent,
                                                       object representedObject )
        {
            return CreateStronglyTyped( name, parent, representedObject, false );
        }

        public static NsContainer CreateStronglyTyped( string name,
                                                       NamespaceItem parent,
                                                       object representedObject,
                                                       bool ephemeral )
        {
            Type genericParam;
            Type strongType = _CreateStrongNsContainerTypeFor( representedObject,
                                                               out genericParam );

            var ci = strongType.GetConstructor( new Type[] { typeof( string ),
                                                             typeof( NamespaceItem ),
                                                             genericParam,
                                                             typeof( bool ) } );

            return Util.UnwrapTargetInvocationException( () =>
                (NsContainer) ci.Invoke( new object[] { name,
                                                        parent,
                                                        representedObject,
                                                        ephemeral } ) );
        }
    } // end class NsContainer


    /// <summary>
    ///    A strongly-type convenience class for containers. (Convenient because it gets
    ///    rid of a lot of type checking and casting.)
    /// </summary>
    internal class NsContainer< T > : NsContainer where T : class
    {
        public new T RepresentedObject
        {
            get
            {
                return (T) base.RepresentedObject;
            }
        }

        public NsContainer( string name, NamespaceItem parent, T representedObject )
            : this( name, parent, representedObject, false )
        {
        }


        public NsContainer( string name,
                            NamespaceItem parent,
                            T representedObject,
                            bool ephemeral )
            : base( name, parent, representedObject, ephemeral )
        {
        }
    } // end class NsContainer< T >


    /// <summary>
    ///    A NamespaceItem used to designate the root of a virtual namespace.
    /// </summary>
    internal class NsRoot : NsContainer
    {
        public DbgRootDriveInfo RootDrive { get; private set; }
        public override bool IsContainer { get { return true; } }

        private static string _CheckName( string name )
        {
            if( String.IsNullOrEmpty( name ) )
                throw new ArgumentException( "You must supply a name.", "name" );

            return name;
        }

        public NsRoot( string name ) : base( _CheckName( name ) )
        {
            new NsProcessDir( this ); // it's constructor will link it to this
        }

        internal void SetRootDrive( DbgRootDriveInfo rootDrive )
        {
            if( null == rootDrive )
                throw new ArgumentNullException( "rootDrive" );

            if( null != RootDrive )
                throw new InvalidOperationException( "The root drive has already been set." );

            RootDrive = rootDrive;
        }
    } // end class NsRoot


    internal abstract class NsLeaf : NamespaceItem
    {
        public override bool IsContainer { get { return false; } }

        public int MaxSize { get; protected set; }

        public virtual bool IsReadOnly { get; protected set; }

        public abstract int Size { get; }


        public abstract IList Read( int offset, long readCount );


        public abstract IList Write( ref int offset, IList content );


        public abstract void Truncate( int desiredSize );


        internal Func< List< object >, int, ColorString > SummarizeFunc;

        public abstract ColorString Summarize( int maxWidth );


        protected NsLeaf( string name, NamespaceItem parent )
            : this( name, parent, false )
        {
        }

        protected NsLeaf( string name, NamespaceItem parent, bool ephemeral )
            : base( name, parent, ephemeral )
        {
            // You're now allowed to have a leaf item with no parent: for NsLazyLeaf.
         // if( null == parent )
         //     throw new ArgumentNullException( "parent" );

            MaxSize = Int32.MaxValue;
        }


        public static NsLeaf CreateNsLeaf( string name, NamespaceItem parent, object value )
        {
            return CreateNsLeaf( name, parent, value, false, null );
        }

        public static NsLeaf CreateNsLeaf( string name,
                                           NamespaceItem parent,
                                           object value,
                                           Func< List< object >, int, ColorString > summarizeFunc )
        {
            return CreateNsLeaf( name, parent, value, false, summarizeFunc );
        }


        internal static NsLeaf CreateNsLeaf( string name,
                                             NamespaceItem parent,
                                             object value,
                                             bool fromDataBinding,
                                             Func< List< object >, int, ColorString > summarizeFunc )
        {
            return CreateNsLeaf( name,
                                 parent,
                                 value,
                                 false, // ephemeral
                                 fromDataBinding,
                                 summarizeFunc );
        }

        internal static NsLeaf CreateNsLeaf( string name,
                                             NamespaceItem parent,
                                             object value,
                                             bool ephemeral,
                                             bool fromDataBinding,
                                             Func< List< object >, int, ColorString > summarizeFunc )
        {
            IEnumerable enumerable = System.Management.Automation.LanguagePrimitives.GetEnumerable( value );
            NsLeaf leaf = null;
            if( null == enumerable )
                leaf = new NsSimpleLeaf( name, parent, value, ephemeral );
            else
                leaf = new NsListLeaf( name, parent, ephemeral, enumerable );

            leaf.FromDataBinding = fromDataBinding;
            leaf.SummarizeFunc = summarizeFunc;
            return leaf;
        } // end CreateNsLeaf()
    } // end class NsLeaf


    internal class NsListLeaf : NsLeaf
    {
        private List< object > m_content;

        public override int Size { get { return m_content.Count; } }


        public override IList Read( int offset, long readCount )
        {
            if( readCount <= 0 )
            {
                return new List< object >( m_content );
            }
            int iReadCount = (int) readCount; // TODO: TOTEST: overflow

            if( offset == m_content.Count )
                return null; // we've hit the end

            // User can request arbitrary readCount, so be sure to bound it for them.
            iReadCount = Math.Min( iReadCount, m_content.Count - offset );
            object[] read = new object[ iReadCount ];
            //// Drat; IList doesn't have this overload of CopyTo.
            m_content.CopyTo( offset, read, 0, iReadCount );

         // int destIdx = 0;
         // foreach( var item in m_content.Skip( offset ).Take( iReadCount ) )
         // {
         //     read[ destIdx ] = item;
         // }
            return read;
        } // end Read()


        public override IList Write( ref int offset, IList content )
        {
            if( IsReadOnly )
                throw new InvalidOperationException( "This item is read-only." );

            int idx = offset;
            foreach( object obj in content )
            {
                if( offset >= MaxSize )
                {
                    // TODO: better error?
                    throw new ArgumentException( Util.Sprintf( "Cannot exceed the maximum size of this item ({0}).",
                                                               MaxSize ) );
                }

                if( offset < m_content.Count )
                    m_content[ offset ] = obj;
                else
                    m_content.Add( obj );

                offset++;
            }
            return content;
        } // end Write()


        public override void Truncate( int desiredSize )
        {
            if( desiredSize > Size )
                throw new ArgumentOutOfRangeException();

            if( desiredSize == Size )
                return;

            m_content.RemoveRange( desiredSize, Size - desiredSize );
        } // end Truncate()


        public NsListLeaf( string name, NamespaceItem parent )
            : this( name, parent, false )
        {
        }

        public NsListLeaf( string name, NamespaceItem parent, bool ephemeral )
            : base( name, parent, ephemeral )
        {
            if( null == parent )
                throw new ArgumentNullException( "parent" );

            m_content = new List< object >();
            MaxSize = Int32.MaxValue;
        }

        public NsListLeaf( string name, NamespaceItem parent, IEnumerable content )
            : this( name, parent, false, content )
        {
        }

        public NsListLeaf( string name, NamespaceItem parent, bool ephemeral, IEnumerable content )
            : base( name, parent, ephemeral )
        {
            m_content = new List< object >( content.Cast< object >() );
            MaxSize = Int32.MaxValue;
        }

        protected override NamespaceItem CopyToWorker( NamespaceItem destContainer,
                                                       string destName,
                                                       bool recurse,
                                                       Action< Exception, string > onCopyChildError,
                                                       Func< bool > stopping )
        {
            NsListLeaf nsCopy = new NsListLeaf( destName, destContainer );
            foreach( object obj in m_content )
            {
                nsCopy.m_content.Add( obj );
            }
            return nsCopy;
        } // end CopyToWorker()

        public override ColorString Summarize( int maxWidth )
        {
            if( null != SummarizeFunc )
            {
                return SummarizeFunc( m_content, maxWidth );
            }
            else
            {
                ColorString cs = new ColorString( m_content.Count.ToString() );
                cs.Append( " items: " );
                cs.Append( Formatting.Commands.FormatAltSingleLineCommand.FormatSingleLineDirect( m_content ) );
                return CaStringUtil.Truncate( cs, maxWidth, true );
            }
        } // end Summarize
    } // end class NsListLeaf


    internal class NsSimpleLeaf : NsLeaf
    {
        protected object m_value;

     // protected override object GetValue()
     // {
     //     return m_value;
     // }


        public NsSimpleLeaf( string name, NamespaceItem parent, object value )
            : this( name, parent, value, false )
        {
        }

        public NsSimpleLeaf( string name, NamespaceItem parent, object value, bool ephemeral )
            : this( name, parent, ephemeral, value, 1 )
        {
        }

        public NsSimpleLeaf( string name,
                             NamespaceItem parent,
                             bool ephemeral,
                             object value,
                             int contentMaxSize )
            : this( name, parent, ephemeral, value, contentMaxSize, null )
        {
        }

        public NsSimpleLeaf( string name,
                             NamespaceItem parent,
                             bool ephemeral,
                             object value,
                             Func< List< object >, int, ColorString > summarizeFunc )
            : this( name, parent, ephemeral, value, 1, summarizeFunc )
        {
        }

        public NsSimpleLeaf( string name,
                             NamespaceItem parent,
                             bool ephemeral,
                             object value,
                             int contentMaxSize,
                             Func< List< object >, int, ColorString > summarizeFunc )
            : base( name, parent, ephemeral )
        {
            IsReadOnly = true; // for now?
            MaxSize = contentMaxSize;
            m_value = value;
            SummarizeFunc = summarizeFunc;
        } // end constructor


        public override ColorString Summarize( int maxWidth )
        {
            if( null != SummarizeFunc )
            {
                return SummarizeFunc( new List< object >( 1 ) { m_value }, maxWidth );
            }
            else
            {
                ColorString cs = Formatting.Commands.FormatAltSingleLineCommand.FormatSingleLineDirect( m_value );
                return CaStringUtil.Truncate( cs, maxWidth, true );
            }
        } // end Summarize()


        public override int Size { get { return 1; } }

        public override IList Read( int offset, long readCount )
        {
            if( 0 != offset )
                return null; // signal EOF

            return new object[] { m_value };
        } // end Read()


        public override IList Write( ref int offset, IList content )
        {
            if( IsReadOnly )
                throw new InvalidOperationException( "This item is read-only." );

            throw new NotImplementedException();
        } // end Write()


        public override void Truncate( int desiredSize )
        {
            throw new InvalidOperationException( "This item does not support truncation." );
        } // end Truncate()


        protected override NamespaceItem CopyToWorker( NamespaceItem destContainer,
                                                       string destName,
                                                       bool recurse,
                                                       Action<Exception, string> onCopyChildError,
                                                       Func<bool> stopping )
        {
            // TODO: This should be easily supportable...
            throw new NotSupportedException( Util.Sprintf( "This object type ({0}) does not support copying.",
                                                           Util.GetGenericTypeName( this ) ) );
        } // end CopyToWorker()


        // TODO: This should be easily supportable...
        protected virtual bool SupportsRename { get { return false; } }

        public override void Rename( string newName )
        {
            if( SupportsRename )
            {
                base.Rename( newName );
            }
            else
            {
                throw new InvalidOperationException( Util.Sprintf( "This object type ({0}) does not support renaming.",
                                                                   Util.GetGenericTypeName( this ) ) );
            }
        } // end Rename()

        public override NamespaceItem MoveTo( NamespaceItem destContainer, string destName )
        {
            // TODO: This should be easily supportable...
            throw new InvalidOperationException( Util.Sprintf( "This object type ({0}) does not support moving.",
                                                               Util.GetGenericTypeName( this ) ) );
        } // end MoveTo()
    } // end class NsSimpleLeaf


    // This class is used to hook a leaf into a namespace (so it has a name), without
    // actually having to create the leaf right away. It's basically a facade.
    internal class NsLazyLeaf : NsLeaf
    {
        private NsLeaf _lazyActualLeaf;
        private Func< NsLeaf > _produceLeaf;

        private NsLeaf _ActualLeaf
        {
            get
            {
                if( null == _lazyActualLeaf )
                {
                    _lazyActualLeaf = _produceLeaf();
                    Util.Assert( null != _lazyActualLeaf );
                    if( null == _lazyActualLeaf )
                        throw new Exception( "_produceLeaf returned null." );
                }
                return _lazyActualLeaf;
            }
        }


        public override bool IsReadOnly
        {
            get
            {
                return _ActualLeaf.IsReadOnly;
            }
            protected set
            {
                Debug.Fail( "This should not have any callers." );
            }
        }


        public override int Size { get { return _ActualLeaf.Size; } }


        public override IList Read( int offset, long readCount )
        {
            return _ActualLeaf.Read( offset, readCount );
        }


        public override IList Write( ref int offset, IList content )
        {
            return _ActualLeaf.Write( ref offset, content );
        }


        public override void Truncate( int desiredSize )
        {
            _ActualLeaf.Truncate( desiredSize );
        }


        public override ColorString Summarize( int maxWidth )
        {
            return _ActualLeaf.Summarize( maxWidth );
        }


        public NsLazyLeaf( string name,
                           NamespaceItem parent,
                           bool fromDataBinding,
                           Func< NsLeaf > produceActualLeaf )
            : base( name, parent )
        {
            FromDataBinding = fromDataBinding;
            _produceLeaf = produceActualLeaf;
        }
    } // end class NsLazyLeaf


    /// <summary>
    ///    A NamespaceItem container which represents a virtual container for
    ///    processes running on the system.
    /// </summary>
    internal class NsProcessDir : NsContainer
    {
        public NsProcessDir( NamespaceItem parent ) : base( "Process", parent, null )
        {
        } // end constructor

        private static string _GetProcessName( Process process )
        {
            if( null == process )
                throw new ArgumentNullException( "process" );

            try
            {
                return Util.Sprintf( "{0} ({1})", process.ProcessName, process.Id );
            }
            catch( InvalidOperationException ioe )
            {
                // Not an invariant...
                Util.Fail( Util.Sprintf( "Could not get process name: {0}", Util.GetExceptionMessages( ioe ) ) );
                return "<error>";
            }
        } // end _GetProcessName()

        private static ColorString _SummarizeProc( List< object > content, int maxWidth )
        {
            // Nothing for now.
            return ColorString.Empty;
        }

        private static Func< List< object >, int, ColorString > sm_summarizeProcFunc = _SummarizeProc;

        public override IEnumerable< TreeNode< NamespaceItem > > EnumerateChildren()
        {
            using( ExceptionGuard disposer = new ExceptionGuard() )
            {
                Process[] procs = disposer.ProtectAll( Process.GetProcesses() );
                foreach( Process proc in procs )
                {
                    yield return new NsSimpleLeaf( _GetProcessName( proc ),
                                                   this,
                                                   true,
                                                   proc,
                                                   sm_summarizeProcFunc );
                }
            }

            foreach( var target in base.EnumerateChildren() )
            {
                yield return target;
            }
        } // end EnumerateChildren()


        public override bool TryFindChild( string childName, out NamespaceItem child )
        {
            using( ExceptionGuard disposer = new ExceptionGuard() )
            {
                Process[] procs = disposer.ProtectAll( Process.GetProcesses() );
                foreach( Process proc in procs )
                {
                    try
                    {
                        if( 0 == Util.Strcmp_OI( childName, proc.ProcessName ) )
                        {
                            child = new NsSimpleLeaf( _GetProcessName( proc ), this, proc, true );
                            return true;
                        }
                    }
                    catch( InvalidOperationException )
                    {
                    }
                }
            } // end using( disposer )

            return base.TryFindChild( childName, out child );
        } // end TryFindChild() override

        // Because the process items are "ephemeral" and not hooked into the namespace tree, we need
        // to override RemoveChild, because the base class implementation will not be able to
        // find the process child.
        public override TreeNode< NamespaceItem > RemoveChild( TreeNode< NamespaceItem > cutMe )
        {
            Console.WriteLine( "RemoveChild: Killing process '{0}'...", cutMe.Item.Name );
            // I'm not REALLY going to kill anything, ha ha.
            return cutMe;
        }

        protected override NamespaceItem CopyToWorker( NamespaceItem destContainer,
                                                       string destName,
                                                       bool recurse,
                                                       Action<Exception, string> onCopyChildError,
                                                       Func<bool> stopping )
        {
            throw new NotSupportedException( Util.Sprintf( "This object type ({0}) does not support copying.",
                                                           Util.GetGenericTypeName( this ) ) );
        }
    } // end class NsProcessDir
}
