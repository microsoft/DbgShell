using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents a node in a tree structure.
    /// </summary>
    /// <typeparam name="T">
    ///    The type of item stored in each node of the tree.
    /// </typeparam>
    /// <remarks>
    ///    Each node may have an arbitrary number of children.
    /// </remarks>
    internal class TreeNode< T >
    {
        public T Item { get; protected set; }

        private TreeNode< T > m_parent;
        private List< TreeNode< T > > m_children = new List< TreeNode< T > >();


        /// <summary>
        ///    Constructs a new TreeNode&lt; T &gt; object.
        /// </summary>
        /// <param name="item">
        ///    The item stored in the new TreeNode.
        /// </param>
        public TreeNode( T item ) : this( item, null ) { }

        /// <summary>
        ///    Constructs a new TreeNode&lt; T &gt; object.
        /// </summary>
        /// <param name="item">
        ///    The item stored in the new TreeNode.
        /// </param>
        /// <param name="parent">
        ///    The parent of the new TreeNode.
        /// </param>
        public TreeNode( T item, TreeNode< T > parent ) : this( item, parent, false ) { }

        /// <summary>
        ///    Constructs a new TreeNode&lt; T &gt; object.
        /// </summary>
        /// <param name="item">
        ///    The item stored in the new TreeNode.
        /// </param>
        /// <param name="parent">
        ///    The parent of the new TreeNode.
        /// </param>
        /// <param name="ephemeral">
        ///    True if the node should be "ephemeral", and not added to the parent node's
        ///    list of children. Use this when representing items that come from an
        ///    external data source.
        /// </param>
        public TreeNode( T item, TreeNode< T > parent, bool ephemeral )
        {
            Item = item;
            m_parent = parent;

            if( (null != m_parent) && !ephemeral )
                m_parent.m_children.Add( this );
        } // end constructor


        /// <summary>
        ///    Constructs a new TreeNode for the specified item and adds it as a child of
        ///    the current node.
        /// </summary>
        /// <returns>
        ///    The newly-constructed TreeNode.
        /// </returns>
        public virtual TreeNode< T > AddChildItem( T item )
        {
            if( item is TreeNode<T> )
                throw new InvalidOperationException( "Unless you are building some kind of meta-tree, this is not likely to give you the results you want. Use AddChild instead." );

            TreeNode< T > newNode = new TreeNode< T >( item, this );
            return newNode;
        } // end AddChild()


        /// <summary>
        ///    Adds an existing TreeNode as a child of the current TreeNode.
        /// </summary>
        /// <returns>
        ///    The TreeNode that was passed in.
        /// </returns>
        public virtual TreeNode< T > AddChild( TreeNode< T > graft )
        {
            if( null == graft )
                throw new ArgumentNullException( "graft" );

            m_children.Add( graft );
            graft.m_parent = this;
            return graft;
        } // end AddChild( graft )


        public virtual IEnumerable< TreeNode< T > > EnumerateChildren()
        {
            foreach( var node in m_children )
            {
                yield return node;
            }
        } // end EnumerateChildren()


        public IEnumerable< T > EnumerateChildItems()
        {
            foreach( var node in EnumerateChildren() )
            {
                yield return node.Item;
            }
        } // end EnumerateChildren()


        public virtual TreeNode< T > RemoveChild( TreeNode< T > cutMe )
        {
            if( this != cutMe.m_parent )
                throw new ArgumentException( "The specified TreeNode is not a child of the current node.", "cutMe" );

            if( !m_children.Remove( cutMe ) )
            {
                Debug.Fail( "The parent check should not allow us to get here." );
                throw new ArgumentException( "The specified TreeNode was not found.", "cutMe" );
            }

            cutMe.m_parent = null;
            return cutMe;
        } // end RemoveChild()


        /// <summary>
        ///    A handy shortcut for x.Parent.RemoveChild( x ).
        /// </summary>
        public void Unlink()
        {
            if( null == m_parent )
                throw new InvalidOperationException( "Cannot unlink the root of the tree. (This can also happen if you try to unlink an item that was previously unlinked, which will make it look like a root of a tree because its parent is null.)" );

            m_parent.RemoveChild( this );
        } // end Unlink()


        public int SubtreeCount { get { return 1 + m_children.Count; } }

        public TreeNode< T > Parent { get { return m_parent; } }

        public bool IsLeaf { get { return 0 == m_children.Count; } }

        public bool IsRoot { get { return null == m_parent; } }

    } // end class TreeNode< T >
}

