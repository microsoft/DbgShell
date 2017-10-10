using System;
using System.Management.Automation;

namespace MS.Dbg
{
    /// <summary>
    ///    The base class for classes that represent a "drive", which represents a portal
    ///    into some spot in the virtual namespace.
    /// </summary>
    abstract class DbgDriveInfoBase< T > : PSDriveInfo where T : NamespaceItem
    {
        public T NsItem { get; private set; }


        protected virtual void Init( T nsItem )
        {
            if( null == nsItem )
                throw new ArgumentNullException( "nsItem" );

            if( !nsItem.IsContainer )
                throw new ArgumentException( "Can't create a drive pointing to a non-container.", "nsItem" );

            NsItem = nsItem;
            // I think in the general case the namespace item does not need to know about
            // drives attached to it.
        }


        protected DbgDriveInfoBase( string name, // doesn't have to match item name; drives are subst-like shortcuts
                                    T nsItem,
                                    ProviderInfo providerInfo,
                                    string description )
            : base( name,
                    providerInfo,
                    nsItem == null ? "will throw from Init" : nsItem.ComputePath( false ), // root: where in the namespace the drive is attached
                    description,
                    null )
        {
            Init( nsItem );
        }


        protected DbgDriveInfoBase( string name, T nsItem, PSDriveInfo driveInfo )
            : base( driveInfo )
        {
            Init( nsItem );

            if( 0 != Util.Strcmp_OI( nsItem.ComputePath( false ), driveInfo.Root ) )
            {
                throw new InvalidOperationException( "The specified root for the drive does not match the actual location of the container." );
            }
        }
    } // end class DbgDriveInfoBase


    /// <summary>
    ///    A "drive" that represents a portal into some other spot in the namespace.
    /// </summary>
    class DbgDriveInfo : DbgDriveInfoBase< NamespaceItem >
    {
        /// <summary>
        ///    Creates a new DbgDriveInfo object that represents a drive attached to some
        ///    location in the virtual namespace.
        /// </summary>
        /// <param name="name">
        ///    The name of the drive. This does not have to match the name of the
        ///    <paramref name="nsItem"/>; drives are subst-like shortcuts into the virtual
        ///    namespace, and the name of the drive is the label by which you access it.
        /// </param>
        /// <param name="nsItem">
        ///    The item representing the place in the virtual namespace where the drive
        ///    will be attached. It must be a container, else this will throw.
        /// </param>
        public DbgDriveInfo( string name,
                             NamespaceItem nsItem,
                             ProviderInfo providerInfo,
                             string description )
            : base( name,
                    nsItem,
                    providerInfo,
                    description )
        {
        }

        public DbgDriveInfo( string name, NamespaceItem nsItem, PSDriveInfo driveInfo )
            : base( name, nsItem, driveInfo )
        {
        }
    } // end class DbgDriveInfo



    /// <summary>
    ///    A "drive" that represents the root of the DbgProvider namespace.
    /// </summary>
    /// <remarks>
    ///    The root of such a drive (the place where it "attaches" to or "looks in on" the
    ///    virtual namespace) will always be "" (String.Empty). And the name of the drive
    ///    will be taken from the Name field of the NsRoot object.
    /// </remarks>
    internal class DbgRootDriveInfo : DbgDriveInfoBase< NsRoot >
    {
        protected override void Init( NsRoot nsRoot )
        {
            base.Init( nsRoot );
            nsRoot.SetRootDrive( this );
        }

        /// <summary>
        ///    Creates a new DbgRootDriveInfo object.
        /// </summary>
        /// <param name="nsRoot">
        ///    The NsRoot object representing the root of the virtual namespace where this
        ///    drive will be attached.
        /// </param>
        /// <remarks>
        /// <para>
        ///    The name of the drive will be taken from <paramref name="nsRoot"/>.Name.
        /// </para>
        /// <para>
        ///    The root of such a drive (the place where it "attaches" to or "looks in on"
        ///    the virtual namespace) will always be "" (String.Empty).
        /// </para>
        /// </remarks>
        public DbgRootDriveInfo( NsRoot nsRoot,
                                 ProviderInfo providerInfo,
                                 string description )
            : base( nsRoot == null ? "will throw from Init" : nsRoot.Name,
                    nsRoot,
                    providerInfo,
                    description )
        {
        }

        public DbgRootDriveInfo( string name, NsRoot nsRoot, PSDriveInfo driveInfo )
            : base( name, nsRoot, driveInfo )
        {
        }
    } // end class DbgRootDriveInfo
}

