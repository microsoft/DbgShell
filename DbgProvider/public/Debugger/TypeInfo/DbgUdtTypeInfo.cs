using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [DebuggerDisplay( "UDT: Id {TypeId}: {m_name}" )]
    public partial class DbgUdtTypeInfo : DbgNamedTypeInfo
    {
        public readonly UdtKind UdtKind;
        // TODO: What does it mean when you have a UdtClass type of size 0? With no
        // children?

        private readonly uint m_classParentId;
        private readonly uint m_vtableShapeId;
        private readonly uint m_numChildren;

        private DbgUdtTypeInfo m_classParent;
        public DbgUdtTypeInfo ClassParent // TODO: <-- isn't this just "owning type"?
        {
            get
            {
                if( null == m_classParent )
                {
                    if( 0 != m_classParentId )
                    {
                        _EnsureValid();
                        m_classParent = DbgUdtTypeInfo.GetUdtTypeInfo( Debugger,
                                                                       Module,
                                                                       m_classParentId );
                    }
                }
                return m_classParent;
            }
        } // end property ClassParent

        // TODO: Seems this could be either a BaseType or a VTableShape... seems odd.
        private DbgTypeInfo m_vtableShape;
        public DbgTypeInfo VTableShape
        {
            get
            {
                if( null == m_vtableShape )
                {
                    if( 0 != m_vtableShapeId )
                    {
                        _EnsureValid();
                        m_vtableShape = DbgTypeInfo.GetTypeInfo( Debugger,
                                                                 Module,
                                                                 m_vtableShapeId );
                        // I'd like to track this down.
                        // TODO: so... what should I do about btNoType "vtable shapes"?
                        Util.Assert( (m_vtableShape is DbgVTableShapeTypeInfo) ||
                                     ((m_vtableShape is DbgBaseTypeInfo) && (((DbgBaseTypeInfo) m_vtableShape).BaseType == BasicType.btNoType)) );
                    }
                }
                return m_vtableShape;
            }
        } // end property VTableShape

        private DbgVTableTypeInfo m_vtable;
        public DbgVTableTypeInfo VTable
        {
            get
            {
                if( (null == m_vtable) && !_AlreadyPopulated )
                {
                    _PopulateMembers();
                }
                return m_vtable;
            }
        } // end property VTableShape


        public class VTableSearchResult
        {
            public readonly DbgVTableTypeInfo VTable;
            public int TotalOffset { get; internal set; }

            private List< DbgBaseClassTypeInfo > m_path;
            private ReadOnlyCollection< DbgBaseClassTypeInfo > m_roPath;

            /// <summary>
            ///    The list of base classes we had to search through.
            /// </summary>
            public IReadOnlyList< DbgBaseClassTypeInfo > Path
            {
                get
                {
                    _InitPathCollections();
                    return m_roPath;
                }
            }

            private void _InitPathCollections()
            {
                if( null == m_path )
                {
                    m_path = new List< DbgBaseClassTypeInfo >();
                    m_roPath = new ReadOnlyCollection< DbgBaseClassTypeInfo >( m_path );
                }
            }

            internal void AddToPath( DbgBaseClassTypeInfo ti )
            {
                if( null == ti )
                    throw new ArgumentNullException( "ti" );

                _InitPathCollections();
                m_path.Add( ti );
            }

            public VTableSearchResult( DbgVTableTypeInfo vtable, int totalOffset )
            {
                if( null == vtable )
                    throw new ArgumentNullException( "vtable" );

                VTable = vtable;
                TotalOffset = totalOffset;
            }
        } // end class VTableSearchResult()


        public IEnumerable< VTableSearchResult > FindVTables()
        {
            if( !_AlreadyPopulated )
            {
                _PopulateMembers();
            }

            if( null != m_vtable )
            {
                yield return new VTableSearchResult( m_vtable, (int) m_vtable.Offset );
            }

            foreach( var bc in BaseClasses )
            {
                foreach( VTableSearchResult vsr in bc.FindVTables() )
                {
                    vsr.TotalOffset += (int) bc.Offset;
                    vsr.AddToPath( bc );
                    yield return vsr;
                }
            }
        } // end FindVTables()


        private static List< HashSet< string > > sm_interfaceNameEquivalences =
            new List<HashSet<string>>()
            {
                new HashSet< string >( StringComparer.OrdinalIgnoreCase )
                {
                    "IUnknown",
                    "IPrivateUnknown", // example: ieframe!g_pIntelliFormsFirst.m_punkDoc2 (type mshtml!CDoc)
                    "__abi_IUnknown"   // WinRT thing
                },
                new HashSet< string >( StringComparer.OrdinalIgnoreCase )
                {
                    // These are all WinRT-related things.
                    "Platform::Object",
                    "IInspectable",
                    "__abi_IInspectable"
                },
            };

        // Unfortunately type information in PDBs expose us to the messy world of the
        // linker, where types have to match up across modules by name. (In other words,
        // every module has its own type information for IUnknown; or, IUnknown has a
        // different typeId in every module.) Sometimes we need to be able to make the
        // link between type information across modules (i.e.
        // TestNativeConsoleApp!IXmlReader == XmlLite!IXmlReader) in order to jump the gap
        // between declared types and implementing types. We do this in the same spirit as
        // "Duck Typing": if it looks like a duck, sounds like a duck...
        private bool _DuckTypeEquals( DbgUdtTypeInfo other )
        {
            if( Size != other.Size )
                return false;

            if( 0 == Util.Strcmp_OI( Name, other.Name ) )
            {
               // TODO: Should we add any other points of comparison? Members? Perhaps
               // we should add some more to help detect bugs (in the target, where,
               // for instance, mismatched binaries could lead to mismatched types).
                return true;
            }

            foreach( var equivSet in sm_interfaceNameEquivalences )
            {
                if( equivSet.Contains( Name ) &&
                    equivSet.Contains( other.Name ) )
                {
                    LogManager.Trace( "_DuckTypeEquals: Special case: {0} is equivalent to {1}.",
                                      Name,
                                      other.Name );
                    return true;
                }
            }

            // TODO: Try stripping __abi_ off?

            return false;
        } // end _DuckTypeEquals()


        public List< int > FindPossibleOffsetsFromDerivedClass( DbgUdtTypeInfo baseType )
        {
            var offsets = new List<int>();
            _FindPossibleOffsetsFromDerivedClass( baseType, 0, offsets );
            return offsets;
        }

        private void _FindPossibleOffsetsFromDerivedClass( DbgUdtTypeInfo baseType,
                                                           int curOffset,
                                                           List< int > offsets )
        {
            // Breadth-first search.
            foreach( var bc in BaseClasses )
            {
                if( bc._DuckTypeEquals( baseType ) )
                {
                    int offset = curOffset - (int) bc.Offset; // relative to derived class!
                    offsets.Add( offset );
                    // I think I could actually break out of this loop here, but I'm too
                    // chicken.
                }
            }

            foreach( var bc in BaseClasses )
            {
                int tmpOffset = curOffset;
                tmpOffset -= (int) bc.Offset; // relative to derived class!
                bc._FindPossibleOffsetsFromDerivedClass( baseType,
                                                         tmpOffset,
                                                         offsets );
            }
        } // end _FindPossibleOffsetsFromDerivedClass()


        public List< DbgBaseClassTypeInfo > GetAllBaseClasses()
        {
            // BaseClasses represents immediate base classes (so there will only be
            // more than one in the case of multiple inheritance). We use the q to do a
            // breadth-first traversal. TODO: SQM for use of multiple inheritance?
            var list = new List< DbgBaseClassTypeInfo >();
            VisitAllBaseClasses( ( bc ) => list.Add( bc ) );
            return list;
        } // end GetAllBaseClasses()


        public void VisitAllBaseClasses( Action< DbgBaseClassTypeInfo > action )
        {
            // BaseClasses represents immediate base classes (so there will only be
            // more than one in the case of multiple inheritance). We use the q to do a
            // breadth-first traversal. TODO: SQM for use of multiple inheritance?
            var q = new Queue< DbgBaseClassTypeInfo >();
            foreach( var bc in BaseClasses )
            {
                action( bc ); // because I don't start by enqueuing the root (because it's not a DbgBaseClassTypeInfo)
                q.Enqueue( bc );
            }

            while( q.Count > 0 )
            {
                var cur = q.Dequeue();
                foreach( var bc in cur.BaseClasses )
                {
                    action( bc );
                    q.Enqueue( bc );
                }
            } // end while( items in q )
        } // end VisitAllBaseClasses()


        private IReadOnlyList< DbgTypeInfo > m_children;
        public IReadOnlyList< DbgTypeInfo > Children
        {
            get
            {
                if( null == m_children )
                {
                    _PopulateMembers();
                }
                return m_children;
            }
        } // end property Children


        private NameIndexableList< DbgDataMemberTypeInfo > m_members;
        public NameIndexableList< DbgDataMemberTypeInfo > Members
        {
            get
            {
                if( null == m_members )
                {
                    _PopulateMembers();
                }
                return m_members;
            }
        } // end property Members


        private NameIndexableList< DbgDataStaticMemberTypeInfo > m_staticMembers;
        public NameIndexableList< DbgDataStaticMemberTypeInfo > StaticMembers
        {
            get
            {
                if( null == m_staticMembers )
                {
                    _PopulateMembers();
                }
                return m_staticMembers;
            }
        } // end property StaticMembers


        NameIndexableList< DbgBaseClassTypeInfo > m_baseClasses;
        public NameIndexableList< DbgBaseClassTypeInfo > BaseClasses
        {
            get
            {
                if( null == m_baseClasses )
                {
                    _PopulateMembers();
                }
                return m_baseClasses;
            }
        } // end property BaseClasses


        NameIndexableList< DbgVirtualBaseClassTypeInfo > m_virtBaseClasses;
        public NameIndexableList< DbgVirtualBaseClassTypeInfo > VirtualBaseClasses
        {
            get
            {
                if( null == m_virtBaseClasses )
                {
                    _PopulateMembers();
                }
                return m_virtBaseClasses;
            }
        } // end property VirtualBaseClasses


        NameIndexableList< DbgUdtTypeInfo > m_nestedTypes;
        public NameIndexableList< DbgUdtTypeInfo > NestedTypes
        {
            get
            {
                if( null == m_nestedTypes )
                {
                    _PopulateMembers();
                }
                return m_nestedTypes;
            }
        } // end property NestedTypes


        NameIndexableList< DbgEnumTypeInfo > m_nestedEnums;
        public NameIndexableList< DbgEnumTypeInfo > NestedEnums
        {
            get
            {
                if( null == m_nestedEnums )
                {
                    _PopulateMembers();
                }
                return m_nestedEnums;
            }
        } // end property NestedEnums


        NameIndexableList< DbgTypedefTypeInfo > m_typedefs;
        public NameIndexableList< DbgTypedefTypeInfo > Typedefs
        {
            get
            {
                if( null == m_typedefs )
                {
                    _PopulateMembers();
                }
                return m_typedefs;
            }
        } // end property Typedefs


        NameIndexableList< DbgFunctionTypeInfo > m_functions;
        public NameIndexableList< DbgFunctionTypeInfo > Functions
        {
            get
            {
                if( null == m_functions )
                {
                    _PopulateMembers();
                }
                return m_functions;
            }
        } // end property Functions


        public bool IsAbstract
        {
            get
            {
                foreach( var f in Functions )
                {
                    if( f.IsAbstract )
                        return true;
                }
                return false;
            }
        } // end property IsAbstract


        // TODO: constants


        private InstanceLayout m_layout;

        public InstanceLayout Layout
        {
            get
            {
                if( null == m_layout )
                {
                    m_layout = new InstanceLayout( this, false );
                }
                return m_layout;
            }
        }


        private bool _AlreadyPopulated
        {
            get { return null != m_children; } // It might be empty, but not null if we've called _PopulateMembers.
        }


        private void _PopulateMembers()
        {
            Util.Assert( null == m_members ); // shouldn't need to do this more than once

            var members     = new NameIndexableList< DbgDataMemberTypeInfo >();
            var statics     = new NameIndexableList< DbgDataStaticMemberTypeInfo >();
            var bases       = new NameIndexableList< DbgBaseClassTypeInfo >();
            var virtBases   = new NameIndexableList< DbgVirtualBaseClassTypeInfo >();
            var nestedTypes = new NameIndexableList< DbgUdtTypeInfo >();
            var nestedEnums = new NameIndexableList< DbgEnumTypeInfo >();
            var typedefs    = new NameIndexableList< DbgTypedefTypeInfo >();
            var functions   = new NameIndexableList< DbgFunctionTypeInfo >();

            var childrenIds = GetChildrenIds( m_numChildren ); // _EnsureValid() called here
            var children    = new List< DbgTypeInfo >( childrenIds.Length );
            foreach( var childId in childrenIds )
            {
                var child = DbgTypeInfo.GetTypeInfo( Debugger, Module, childId );
                children.Add( child );

                if( child is DbgDataMemberTypeInfo )
                {
                    members.Add( (DbgDataMemberTypeInfo) child );
                }
                else if( child is DbgDataStaticMemberTypeInfo )
                {
                    statics.Add( (DbgDataStaticMemberTypeInfo) child );
                }
                else if( child is DbgBaseClassTypeInfo )
                {
                    DbgBaseClassTypeInfo bcti = (DbgBaseClassTypeInfo) child;
                    bases.Add( bcti );
                    bcti.AggregateMembers( members, statics );
                }
                else if( child is DbgVirtualBaseClassTypeInfo )
                {
                    virtBases.Add( (DbgVirtualBaseClassTypeInfo) child );
                }
                else if( child is DbgUdtTypeInfo )
                {
                    nestedTypes.Add( (DbgUdtTypeInfo) child );
                }
                else if( child is DbgEnumTypeInfo )
                {
                    nestedEnums.Add( (DbgEnumTypeInfo) child );
                }
                else if( child is DbgTypedefTypeInfo )
                {
                    typedefs.Add( (DbgTypedefTypeInfo) child );
                }
                else if( child is DbgFunctionTypeInfo )
                {
                    functions.Add( (DbgFunctionTypeInfo) child );
                }
                else if( child is DbgVTableTypeInfo )
                {
                    m_vtable = (DbgVTableTypeInfo) child;
                }
                else
                {
                    string key = Util.Sprintf( "{0}+{1}", Util.GetGenericTypeName( child ), child.SymTag );
                    if( sm_unhandledTypes.Add( key ) )
                        LogManager.Trace( "Need to handle children of type {0} ({1}, parent {2}, id {3}).", Util.GetGenericTypeName( child ), child.SymTag, TypeId, child.TypeId );
                }
            }

            m_children = children.AsReadOnly();

            members.Freeze();
            statics.Freeze();
            bases.Freeze();
            virtBases.Freeze();
            nestedTypes.Freeze();
            nestedEnums.Freeze();
            typedefs.Freeze();
            functions.Freeze();

            m_members = members;
            m_staticMembers = statics;
            m_baseClasses = bases;
            m_virtBaseClasses = virtBases;
            m_nestedTypes = nestedTypes;
            m_nestedEnums = nestedEnums;
            m_typedefs = typedefs;
            m_functions = functions;
        } // end _PopulateMembers()

        private static HashSet< string > sm_unhandledTypes = new HashSet<string>( StringComparer.OrdinalIgnoreCase );


        public static DbgUdtTypeInfo GetUdtTypeInfo( DbgEngDebugger debugger,
                                                     DbgModuleInfo module,
                                                     uint typeId )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            if( null == module )
                throw new ArgumentNullException( "module" );

            RawUdtInfo rui = DbgHelp.GetUdtInfo( debugger.DebuggerInterface, module.BaseAddress, typeId );
            return new DbgUdtTypeInfo( debugger,
                                       module,
                                       typeId,
                                       rui.UdtKind,
                                       rui.SymName,
                                       rui.Size,
                                       rui.ChildrenCount,
                                       rui.ClassParentId,
                                       rui.VirtualTableShapeId );
        } // end GetUdtTypeInfo()


        public DbgUdtTypeInfo( DbgEngDebugger debugger,
                               ulong moduleBase,
                               uint typeId,
                               UdtKind kind,
                               string name,
                               ulong size,
                               uint numChildren,
                               uint classParentId,   // could be 0
                               uint vtableShapeId,   // could be 0
                               DbgTarget target )
            : base( debugger, moduleBase, typeId, SymTag.UDT, name, size, target )
        {
            UdtKind = kind;
            m_classParentId = classParentId;
            m_vtableShapeId = vtableShapeId;
            m_numChildren = numChildren;
        } // end constructor

        public DbgUdtTypeInfo( DbgEngDebugger debugger,
                               DbgModuleInfo module,
                               uint typeId,
                               UdtKind kind,
                               string name,
                               ulong size,
                               uint numChildren,
                               uint classParentId,   // could be 0
                               uint vtableShapeId )  // could be 0
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    kind,
                    name,
                    size,
                    numChildren,
                    classParentId,
                    vtableShapeId,
                    module.Target )
        {
            __mod = module;
        } // end constructor

        #region IEquatable stuff

        //
        // We'll consider two UDTs to be equal if they have the same size, modBase, name,
        // base classes (and virtual bases classes), and members.
        //
        // We have to be careful not to include /all/ children (like statics), because we
        // could get stuck in infinite recursion (such as if a class had a static member
        // of its own type).
        //

        public override bool Equals( DbgTypeInfo other )
        {
            var uti = other as DbgUdtTypeInfo;
            if( null == uti )
                return false;

            if( (Size != uti.Size) ||
                (m_modBase != uti.m_modBase) ||
                (Members.Count != uti.Members.Count) ||
                (BaseClasses.Count != uti.BaseClasses.Count) ||
                (VirtualBaseClasses.Count != uti.VirtualBaseClasses.Count) ||
                (0 != String.CompareOrdinal( Name, uti.Name )) )
            {
                return false;
            }

            for( int i = 0; i < Members.Count; i++ )
            {
                if( Members[ i ] != uti.Members[ i ] )
                {
                    return false;
                }
            }

            for( int i = 0; i < BaseClasses.Count; i++ )
            {
                if( BaseClasses[ i ] != uti.BaseClasses[ i ] )
                {
                    return false;
                }
            }

            for( int i = 0; i < VirtualBaseClasses.Count; i++ )
            {
                if( VirtualBaseClasses[ i ] != uti.VirtualBaseClasses[ i ] )
                {
                    return false;
                }
            }

            return true;
        } // end Equals( DbgTypeInfo )

        public override int GetHashCode()
        {
            int hash = ((int) Size) << 16;
            hash += (int) Members.Count + BaseClasses.Count + VirtualBaseClasses.Count;
            hash ^= Name.GetHashCode();
            hash ^= m_modBase.GetHashCode();

            foreach( var member in Members )
            {
                hash ^= member.GetHashCode();
            }

            foreach( var bc in BaseClasses )
            {
                hash ^= bc.GetHashCode();
            }

            foreach( var vbc in VirtualBaseClasses )
            {
                hash ^= vbc.GetHashCode();
            }

            return hash;
        } // end GetHashCode()

        #endregion IEquatable stuff


        /// <summary>
        ///    Finds the offset for a given member/member path from the start of the
        ///    specified type. (memberPath can contain dots, drilling into members)
        /// </summary>
        public uint FindMemberOffset( string memberPath )
        {
            uint offset = 0;
            string[] memberNames = memberPath.Split( '.' );
            DbgUdtTypeInfo curType = this;

            for( int idx = 0; idx < memberNames.Length; idx++ )
            {
                string memberName = memberNames[ idx ];

                if( !curType.Members.HasItemNamed( memberName ) )
                {
                    throw new ArgumentException( Util.Sprintf( "'{0}' does not have a member called '{1}'.",
                                                               FullyQualifiedName,
                                                               memberName ),
                                                 "memberPath" );
                }

                var member = curType.Members[ memberName ];

                offset += member.Offset;

                bool notLastTime = idx < (memberNames.Length - 1);
                if( notLastTime )
                {
                    if( member.DataType is DbgPointerTypeInfo )
                    {
                        throw new ArgumentException( Util.Sprintf( "There can't be a pointer in the memberPath ('{0}').",
                                                                   memberName ),
                                                     "memberPath" );
                    }

                    curType = member.DataType as DbgUdtTypeInfo;
                    if( null == curType )
                    {
                        throw new Exception( Util.Sprintf( "Unexpected DataType: '{0}' is a {1}, not a UDT.",
                                                           memberName,
                                                           member.DataType.GetType().FullName ) );
                    }
                } // end if( not the last one )
            } // end for( each memberName )

            return offset;
        } // end FindMemberOffset
    } // end class DbgUdtTypeInfo
}

