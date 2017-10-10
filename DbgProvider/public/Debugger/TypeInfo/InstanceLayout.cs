using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MS.Dbg
{
    public partial class DbgUdtTypeInfo : DbgNamedTypeInfo
    {
        public class LayoutItem : IComparable< LayoutItem >
        {
            public readonly int Offset;
            public readonly int Size;
            public readonly DbgTypeInfo Item;

            // This is used to turn the unstable List.Sort into a stable sort.
            private int m_origIdx;

            internal LayoutItem( int offset, DbgTypeInfo item, int origIdx )
            {
                Offset = offset;
                Item = item;
                m_origIdx = origIdx;
                if( IsVTable )
                {
                    //Size = ((DbgVTableTypeInfo) Item)
                    Size = Item.Debugger.TargetIs32Bit ? 4 : 8; // TODO: There should be a better way
                }
                else
                {
                    Size = (int) ((DbgNamedTypeInfo) Item).Size;
                }
            }

            public bool IsVTable
            {
                get
                {
                    return Item is DbgVTableTypeInfo;
                }
            }

            #region IComparable<LayoutItem> Members

            public int CompareTo( LayoutItem other )
            {
                int result = Offset.CompareTo( other.Offset );
                if( 0 == result )
                    result = m_origIdx.CompareTo( other.m_origIdx );

                return result;
            }

            #endregion
        } // end class LayoutItem

        public class VTableLayoutItem : LayoutItem
        {
            public IReadOnlyList< DbgBaseClassTypeInfo > VTablePath { get; private set; }

            internal VTableLayoutItem( VTableSearchResult vsr, int origIdx )
                : base( vsr.TotalOffset, vsr.VTable, origIdx )
            {
                VTablePath = vsr.Path;
            }
        } // end class VTableLayoutItem


        public sealed class InstanceLayout : ISupportColor
        {
            public readonly ReadOnlyCollection< LayoutItem > Items;

            private static List< LayoutItem > _BuildList( DbgUdtTypeInfo udt,
                                                          bool sortByOffset )
            {
                var vtables = udt.FindVTables().ToList();
                var list = new List< LayoutItem >( vtables.Count + udt.Members.Count );

                if( sortByOffset )
                {
                    // This is NOT how windbg does things. In windbg, in the face of
                    // unions, you'll see something like offsets 0, 4, 8, then 0, 4, 8
                    // again, etc.; similar to how the type is declared.
                    //
                    // Here, the caller has exercised the option to sort everything by
                    // offset instead. List.Sort is unstable, but we added a stabilizing
                    // factor in LayoutItem's IComparable implementation. This allows us
                    // to keep things in mostly the same order as would be displayed by
                    // windbg.
                    int idx = 0;
                    foreach( var vtr in vtables )
                    {
                        list.Add( new VTableLayoutItem( vtr, idx ) );
                        idx++;
                    }

                    foreach( var mem in udt.Members )
                    {
                        list.Add( new LayoutItem( (int) mem.Offset, mem, idx ) );
                        idx++;
                    }

                    list.Sort();
                }
                else
                {
                    // (this should result in the same order as windbg displays)
                    //
                    // The members are already in the proper order. We just need to slip
                    // the vtable entries in. I'll slip them in as if the list was sorted
                    // by offset. (but actually... is that what windbg does? I need to
                    // find some exotic vtable examples)
                    int memIdx = 0;
                    foreach( var vt in vtables )
                    {
                        var vtli = new VTableLayoutItem( vt, 0 );
                        while( (memIdx < udt.Members.Count) &&
                               (vt.TotalOffset > udt.Members[ memIdx ].Offset) )
                        {
                            list.Add( new LayoutItem( (int) udt.Members[ memIdx ].Offset,
                                                      udt.Members[ memIdx ],
                                                      memIdx ) );
                            memIdx++;
                        }
                        list.Add( vtli );
                    }

                    // Finish the rest.
                    for( ; memIdx < udt.Members.Count; memIdx++ )
                    {
                        list.Add( new LayoutItem( (int) udt.Members[ memIdx ].Offset,
                                                  udt.Members[ memIdx ],
                                                  memIdx ) );
                    }
                }

                return list;
            } // end _BuildList()

            private readonly DbgUdtTypeInfo m_udt;
            private bool m_isSorted;

            internal InstanceLayout( DbgUdtTypeInfo udt, bool sortByOffset )
            {
                Items = new ReadOnlyCollection< LayoutItem >( _BuildList( udt, sortByOffset ) );
                m_udt = udt;
                m_isSorted = sortByOffset;
            }

            public InstanceLayout SortByOffset()
            {
                if( m_isSorted )
                    return this;
                else
                    return new InstanceLayout( m_udt, true );
            } // end sortByOffset()


            private ColorString m_cs;

            public ColorString ToColorString()
            {
                if( null == m_cs )
                {
                    m_cs = new ColorString();
                    m_cs.Append( DbgProvider.ColorizeModuleName( m_udt.Module.Name ) )
                        .Append( "!" )
                        .Append( m_udt.ColorName )
                        .Append( " (size 0x" )
                        .Append( m_udt.Size.ToString( "x" ) )
                        .Append( ")" );

                    int widest = 16;
                    foreach( var item in Items )
                    {
                        var ddti = item.Item as DbgDataTypeInfo;
                        if( null != ddti )
                        {
                            widest = Math.Max( widest, ddti.Name.Length );
                        }
                    }

                    widest = Math.Min( widest, 40 );

                    foreach( var item in Items )
                    {
                        m_cs.AppendLine()
                            .Append( "   +0x" )
                            .Append( item.Offset.ToString( "x3" ) )
                            .Append( " " );

                        var ddti = item.Item as DbgDataTypeInfo;
                        if( null != ddti )
                        {
                            m_cs.Append( ColorString.MakeFixedWidth( ddti.Name, widest ) )
                                .Append( " : " )
                                .Append( ddti.DataType.ColorName );
                        }
                        else
                        {
                            VTableLayoutItem vli = (VTableLayoutItem) item;
                            var dvti = (DbgVTableTypeInfo) item.Item;
                            m_cs.AppendPushFg( ConsoleColor.DarkGray );

                            if( vli.VTablePath.Count > 1 )
                            {
                                m_cs.Append( vli.VTablePath[ 1 ].ColorName ).Append( ": " );
                            }
                            m_cs.Append( dvti.ToColorString() )
                                .AppendPop();
                        }
                    }

                    m_cs.MakeReadOnly();
                } // end if( !m_cs )
                return m_cs;
            } // end ToColorString()

            public override string ToString()
            {
                return ToColorString().ToString( false );
            }
        } // end class InstanceLayout

    } // end class DbgUdtTypeInfo
}

