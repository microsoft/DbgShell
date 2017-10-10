using System;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents an item in the DbgProvider PowerShell provider namespace.
    /// </summary>
    // Internal: Analogous to NamespaceItem, but I want to keep my public and and internal
    // stuff separate.
    public abstract class DbgProviderItem
    {
        internal NamespaceItem NsItem { get; private set; }

        /// <summary>
        ///    The name of the item.
        /// </summary>
        public string Name { get { return NsItem.Name; } }


        /// <summary>
        ///    The full path to the item in the PowerShell provider namespace.
        /// </summary>
        public string FullName { get { return NsItem.ComputePath( false ); } }


        public DbgContainer Parent
        {
            get
            {
                if( null == NsItem.Parent )
                    return null;

                return new DbgContainer( NsItem.Parent.Item );
            }
        } // end property Parent

    //  /// <summary>
    //  ///    Indicates if the item can hold child items or not.
    //  /// </summary>
    //  public bool IsContainer { get { return NsItem.IsContainer; } }

        internal DbgProviderItem( NamespaceItem nsItem )
        {
            if( null == nsItem )
                throw new ArgumentNullException( "nsItem" );

            NsItem = nsItem;
        } // end constructor

        // TODO: a protected constructor that takes a name/path only, and finds the
        // NamespaceItem from that.


        internal static DbgProviderItem CreateDbgItem( NamespaceItem nsItem )
        {
            if( nsItem is NsContainer< DbgUModeProcess > )
                return new DbgUModeTarget( (NsContainer< DbgUModeProcess >) nsItem );
            else if( nsItem is NsContainer< DbgUModeThreadInfo > )
                return new DbgThreadContainer( (NsContainer< DbgUModeThreadInfo >) nsItem );
            else if( nsItem is NsContainer< DbgStackInfo > )
                return new DbgStackContainer( (NsContainer< DbgStackInfo >) nsItem );
            else if( nsItem is NsContainer< DbgStackFrameInfo > )
                return new DbgStackFrameContainer( (NsContainer< DbgStackFrameInfo >) nsItem );
            else if( nsItem.IsContainer )
                return new DbgContainer( nsItem );
            else
                return new DbgLeafItem( nsItem );
        } // end CreateDbgItem


        public virtual ColorString Summarize( int maxWidth )
        {
            NsLeaf nsLeaf = NsItem as NsLeaf;
            if( null != nsLeaf )
                return nsLeaf.Summarize( maxWidth );

            return ColorString.Empty;
        }
    } // end class DbgProviderItem


    /// <summary>
    ///    Represents a container in the DbgProvider PowerShell provider namespace.
    /// </summary>
    public class DbgContainer : DbgProviderItem
    {
        // TODO: EnumerateContainers
        // TODO: EnumerateItems
        // TODO: EnumerateChildren

        internal DbgContainer( NamespaceItem nsItem )
            : base( nsItem )
        {
            if( !nsItem.IsContainer )
            {
                Util.Fail( "A leaf should not be represented by a container." );
                throw new ArgumentException( "The specified item is a leaf, not a container.", "nsItem" );
            }
        } // end constructor

    } // end class DbgContainer


    /// <summary>
    ///    Represents a leaf element in the DbgProvider PowerShell provider namespace.
    /// </summary>
    public class DbgLeafItem : DbgProviderItem
    {
        internal DbgLeafItem( NamespaceItem nsItem )
            : base( nsItem )
        {
            if( nsItem.IsContainer )
            {
                Util.Fail( "A container should not be represented by a leaf." );
                throw new ArgumentException( "The specified item is a container, not a leaf.", "nsItem" );
            }
        } // end constructor
    } // end class DbgLeafItem


    public class DbgUModeTarget : DbgContainer
    {
        public DbgUModeProcess Target { get { return ((NsContainer< DbgUModeProcess >) NsItem).RepresentedObject; } }

        internal DbgUModeTarget( NsContainer< DbgUModeProcess > nsTarget ) : base( nsTarget )
        {
        }
    } // end class DbgUModeTarget


    public class DbgThreadContainer : DbgContainer
    {
        public DbgUModeThreadInfo Thread { get; private set; }

        internal DbgThreadContainer( NsContainer< DbgUModeThreadInfo > nsThread ) : base( nsThread )
        {
            Thread = nsThread.RepresentedObject;
        } // end constructor
    } // end class DbgThreadContainer


    public class DbgStackContainer : DbgContainer
    {
        public DbgStackInfo Stack { get; private set; }

        internal DbgStackContainer( NsContainer< DbgStackInfo > nsStack ) : base( nsStack )
        {
            Stack = nsStack.RepresentedObject;
        } // end constructor
    } // end class DbgStackContainer


    public class DbgStackFrameContainer : DbgContainer
    {
        public DbgStackFrameInfo Frame { get; private set; }

        internal DbgStackFrameContainer( NsContainer< DbgStackFrameInfo > nsFrame ) : base( nsFrame )
        {
            Frame = nsFrame.RepresentedObject;
        } // end constructor
    } // end class DbgStackFrameContainer
}

