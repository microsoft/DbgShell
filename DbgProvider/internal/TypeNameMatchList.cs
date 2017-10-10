using System;
using System.Collections.Generic;

namespace MS.Dbg
{
    public interface IHaveTemplateTypeName
    {
        DbgTemplateNode TypeName { get; }
    }

    /// <summary>
    ///    This class manages prioritization of multiple DbgTemplateNode objects that
    ///    match the same template.
    /// </summary>
    /// <remarks>
    ///    Priority order is:
    ///       templates with no multi-match wildcards
    ///          ordered list of pair&lt; number single-match wildcards, list of templates in descending order of non-wildcard parameter count &gt;
    ///       templates with multi-match wildcards
    ///          ordered list of pair&lt; number single-match wildcards, list of templates in descending order of non-wildcard parameter count &gt;
    /// </remarks>
    internal class TypeNameMatchList< TItem > : IEnumerable< TItem > where TItem : class, IHaveTemplateTypeName
    {
        private string m_matchedTemplateName;
        private const int NoMultiMatchIdx = 0;
        private const int HasMultiMatchIdx = 1;
        private SortedList< int, SortedList< TItem > >[] m_itemLists =
        {
            new SortedList< int, SortedList< TItem > >(),
            new SortedList< int, SortedList< TItem > >()
        };
        private int m_version;


        public TypeNameMatchList()
        {
        } // end constructor

        /// <summary>
        ///    Adds a new item, or replaces an existing item with an exact-match name.
        ///    Returns true if the item was added; false if it replaced an existing item.
        /// </summary>
        public bool AddOrReplace( TItem item )
        {
            if( null == item )
                throw new ArgumentNullException( "item" );

            SortedList< TItem > itemList = _PrepareAdd( item.TypeName, 0 );
            for( int i = 0; i < itemList.Count; i++ )
            {
                var existing = itemList[ i ];
                if( existing.TypeName.MatchesExact( item.TypeName ) )
                {
                    itemList[ i ] = item;
                    return false;
                }
            }
            itemList.Add( item );

            return true;
        } // end AddOrReplace()

        /// <summary>
        ///    Removes an exact-matching item.
        ///    Returns true if the item was removed; false if it was not found.
        /// </summary>
        public bool Remove( TItem item )
        {
            if( null == item )
                throw new ArgumentNullException( "item" );

            SortedList< TItem > itemList = _PrepareRemove( item.TypeName );
            if( null == itemList )
                return false;

            for( int i = 0; i < itemList.Count; i++ )
            {
                var existing = itemList[ i ];
                if( existing.TypeName.MatchesExact( item.TypeName ) )
                {
                    itemList.RemoveAt( i );
                    return true;
                }
            }

            return false;
        } // end Remove()

     // public void AddRange( IEnumerable< TItem > items )
     // {
     //     if( null == items )
     //         throw new ArgumentNullException( "items" );

     //     DbgTemplateNode template = null;
     //     int count = 0;
     //     foreach( var item in items )
     //     {
     //         count++;

     //         if( null == template )
     //             template = item.TypeName;

     //         // Technically they only need to have wildcard type and count match, but
     //         // this should be fine.
     //         if( !item.TypeName.MatchesExact( template ) )
     //             throw new InvalidOperationException( "All items used with AddRange must have exactly-matching templates." );
     //     }

     //     if( 0 == count )
     //     {
     //         Util.Fail( "Why AddRange with nothing?" ); // Not a program invariant; just weird.
     //         return;
     //     }

     //     List< TItem > itemList = _PrepareAdd( template, count );
     //     itemList.AddRange( items );
     // } // end AddRange()


        private SortedList< TItem > _PrepareAdd( DbgTemplateNode template, int sizeHint )
        {
            m_version++;
            if( 0 >= sizeHint )
                sizeHint = 4;

            if( null == m_matchedTemplateName )
            {
                m_matchedTemplateName = template.TemplateName;
                Util.Assert( !String.IsNullOrEmpty( m_matchedTemplateName ) );
            }
            else
            {
                if( 0 != Util.Strcmp_OI( m_matchedTemplateName, template.TemplateName ) )
                {
                    Util.Fail( "should not happen" );
                    throw new ArgumentException( Util.Sprintf( "The supplied item is for template {0}, but this list is for template {1}.",
                                                               template.TemplateName,
                                                               m_matchedTemplateName ),
                                                  "item" );
                }
            }

            SortedList< int, SortedList< TItem > > targetCategoryList;
            if( template.HasMultiTypeWildcard )
                targetCategoryList = m_itemLists[ NoMultiMatchIdx ];
            else
                targetCategoryList = m_itemLists[ HasMultiMatchIdx ];

            SortedList< TItem > itemList;
            if( !targetCategoryList.TryGetValue( template.SingleTypeWildcardCount, out itemList ) )
            {
                itemList = new SortedList< TItem >( sizeHint,
                                                    NonWildcardParamComparer.Instance,
                                                    ExactMatchEqualityComparer.Instance );
                targetCategoryList.Add( template.SingleTypeWildcardCount, itemList );
            }
            return itemList;
        } // end _PrepareAdd()


        private SortedList< TItem > _PrepareRemove( DbgTemplateNode template )
        {
            Util.Assert( !String.IsNullOrEmpty( m_matchedTemplateName ) );
            if( 0 != Util.Strcmp_OI( m_matchedTemplateName, template.TemplateName ) )
            {
                Util.Fail( "should not happen" );
                throw new ArgumentException( Util.Sprintf( "The supplied item is for template {0}, but this list is for template {1}.",
                                                           template.TemplateName,
                                                           m_matchedTemplateName ),
                                              "item" );
            }

            SortedList< int, SortedList< TItem > > targetCategoryList;
            if( template.HasMultiTypeWildcard )
                targetCategoryList = m_itemLists[ NoMultiMatchIdx ];
            else
                targetCategoryList = m_itemLists[ HasMultiMatchIdx ];

            SortedList< TItem > itemList;
            if( !targetCategoryList.TryGetValue( template.SingleTypeWildcardCount, out itemList ) )
            {
                return null;
            }
            return itemList;
        } // end _PrepareRemove()


        private class NonWildcardParamComparer : IComparer< TItem >
        {
            public static readonly NonWildcardParamComparer Instance = new NonWildcardParamComparer();

            private NonWildcardParamComparer() { }

            public int Compare( TItem first, TItem second )
            {
                int f = 0, s = 0;
                DbgTemplate t;

                t = first.TypeName as DbgTemplate;
                if( null != t )
                    f = t.NonWildcardParameterCount;

                t = second.TypeName as DbgTemplate;
                if( null != t )
                    s = t.NonWildcardParameterCount;

                return s.CompareTo( f ); // reverse because we want descending--most non-wildcard parameters first
            }

        } // end class NonWildcardParamComparer


        private class ExactMatchEqualityComparer : IEqualityComparer< TItem >
        {
            public static readonly ExactMatchEqualityComparer Instance = new ExactMatchEqualityComparer();

            private ExactMatchEqualityComparer() { }

            public bool Equals( TItem x, TItem y )
            {
                if( null == x )
                {
                    return null == y;
                }
                else
                {
                    if( null == y )
                        return false;

                    return x.TypeName.MatchesExact( y.TypeName );
                }
            }

            public int GetHashCode( TItem obj )
            {
                if( null == obj )
                    return 0;

                return obj.TypeName.FullName.GetHashCode();
            }
        } // end class ExactMatchEqualityComparer


        /// <summary>
        ///    Attempts to find an item with a matching template.
        /// </summary>
        /// <remarks>
        ///    There may be multiple matches in the list; this just returns the first
        ///    match, where matches are made according to priority order.
        /// </remarks>
        public TItem TryFindMatchingItem( string typeName )
        {
            if( String.IsNullOrEmpty( typeName ) )
                throw new ArgumentException( "You must supply a type name.", "typeName" );

            DbgTemplateNode matchMe = DbgTemplateNode.CrackTemplate( typeName );

            return TryFindMatchingItem( matchMe );
        } // end TryFindMatchingItem()


        /// <summary>
        ///    Attempts to find an item with a matching template.
        /// </summary>
        /// <remarks>
        ///    There may be multiple matches in the list; this just returns the first
        ///    match, where matches are made according to priority order.
        /// </remarks>
        public TItem TryFindMatchingItem( DbgTemplateNode typeTemplate )
        {
            if( null == typeTemplate )
                throw new ArgumentNullException( "typeTemplate" );

            if( 0 != Util.Strcmp_OI( m_matchedTemplateName, typeTemplate.TemplateName ) )
            {
                Util.Fail( "should not happen" );
                throw new ArgumentException( Util.Sprintf( "The supplied item is for template {0}, but this list is for template {1}.",
                                                           typeTemplate.TemplateName,
                                                           m_matchedTemplateName ),
                                              "item" );
            }

            foreach( var categoryList in m_itemLists )
            {
                foreach( var kvp in categoryList )
                {
                    foreach( var item in kvp.Value )
                    {
                        if( item.TypeName.Matches( typeTemplate ) )
                            return item;
                    }
                }
            }

            return null;
        } // end TryFindMatchingItem()


        /// <summary>
        ///    Finds all items with a matching template.
        /// </summary>
        public IEnumerable< TItem > FindMatchingItems( DbgTemplateNode typeTemplate )
        {
            if( null == typeTemplate )
                throw new ArgumentNullException( "typeTemplate" );

            if( 0 != Util.Strcmp_OI( m_matchedTemplateName, typeTemplate.TemplateName ) )
            {
                Util.Fail( "should not happen" );
                throw new ArgumentException( Util.Sprintf( "The supplied item is for template {0}, but this list is for template {1}.",
                                                           typeTemplate.TemplateName,
                                                           m_matchedTemplateName ),
                                              "item" );
            }

            foreach( var categoryList in m_itemLists )
            {
                foreach( var kvp in categoryList )
                {
                    foreach( var item in kvp.Value )
                    {
                        if( item.TypeName.Matches( typeTemplate ) )
                            yield return item;
                    }
                }
            }
        } // end FindMatchingItems()


        /// <summary>
        ///    Enumerates all items that have the exact same template.
        /// </summary>
        /// <remarks>
        ///    Items with templates that match but are not exact matches will not be
        ///    returned.
        /// </remarks>
        public IEnumerable< TItem > FindMatchingItemsExact( string typeName )
        {
            if( String.IsNullOrEmpty( typeName ) )
                throw new ArgumentException( "You must supply a type name.", "typeName" );

            DbgTemplateNode matchMe = DbgTemplateNode.CrackTemplate( typeName );

            return FindMatchingItemsExact( matchMe );
        } // end FindMatchingItemsExact()


        /// <summary>
        ///    Enumerates all items that have the exact same template.
        /// </summary>
        /// <remarks>
        ///    Items with templates that match but are not exact matches will not be
        ///    returned.
        /// </remarks>
        public IEnumerable< TItem > FindMatchingItemsExact( DbgTemplateNode typeTemplate )
        {
            if( null == typeTemplate )
                throw new ArgumentNullException( "typeTemplate" );

            if( 0 != Util.Strcmp_OI( m_matchedTemplateName, typeTemplate.TemplateName ) )
            {
                Util.Fail( "should not happen" );
                throw new ArgumentException( Util.Sprintf( "The supplied item is for template {0}, but this list is for template {1}.",
                                                           typeTemplate.TemplateName,
                                                           m_matchedTemplateName ),
                                              "item" );
            }

            SortedList< int, SortedList< TItem > > targetCategoryList;
            if( typeTemplate.HasMultiTypeWildcard )
                targetCategoryList = m_itemLists[ NoMultiMatchIdx ];
            else
                targetCategoryList = m_itemLists[ HasMultiMatchIdx ];

            SortedList< TItem > itemList;
            if( !targetCategoryList.TryGetValue( typeTemplate.SingleTypeWildcardCount, out itemList ) )
            {
                yield break;
            }

            foreach( var item in itemList )
            {
                // TODO: But couldn't I go faster by using NonWildcardParameterCount?
                if( item.TypeName.MatchesExact( typeTemplate ) )
                    yield return item;
            }
        } // end FindMatchingItemsExact()


     // public void UpdateItemsExact( DbgTemplateNode typeTemplate, Func< TItem, TItem > updater )
     // {
     //     if( null == typeTemplate )
     //         throw new ArgumentNullException( "typeTemplate" );

     //     if( 0 != Util.Strcmp_OI( m_matchedTemplateName, typeTemplate.TemplateName ) )
     //     {
     //         Util.Fail( "should not happen" );
     //         throw new ArgumentException( Util.Sprintf( "The supplied item is for template {0}, but this list is for template {1}.",
     //                                                    typeTemplate.TemplateName,
     //                                                    m_matchedTemplateName ),
     //                                       "item" );
     //     }

     //     SortedList< int, List< TItem > > targetCategoryList;
     //     if( typeTemplate.HasMultiTypeWildcard )
     //         targetCategoryList = m_itemLists[ NoMultiMatchIdx ];
     //     else
     //         targetCategoryList = m_itemLists[ HasMultiMatchIdx ];

     //     List< TItem > itemList;
     //     if( !targetCategoryList.TryGetValue( typeTemplate.SingleTypeWildcardCount, out itemList ) )
     //     {
     //         return;
     //     }

     //     for( int i = 0; i < itemList.Count; i++ )
     //     {
     //         if( itemList[ i ].TypeName.MatchesExact( typeTemplate ) )
     //             itemList[ i ] = updater( itemList[ i ] );
     //     }
     // } // end UpdateItemsExact()

        #region IEnumerable< TItem > Members

        public IEnumerator< TItem > GetEnumerator()
        {
            foreach( var list in m_itemLists )
            {
                foreach( var key in list.Keys )
                {
                    foreach( var item in list[ key ] )
                    {
                        yield return item;
                    }
                }
            }
        } // end GetEnumerator

        #endregion

        #region IEnumerable Members

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion
    } // end class TypeNameMatchList
}

