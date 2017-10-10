using System;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [DebuggerDisplay( "VTable: Id {TypeId}" )]
    public class DbgVTableTypeInfo : DbgTypeInfo, ISupportColor
    {
        public readonly uint Offset;

        private readonly uint m_numChildren;
        private readonly uint m_owningTypeId;
        private readonly uint m_pointerTypeId;

        // Could be a plain DbgUdtTypeInfo (likely), or a DbgBaseClassTypeInfo or
        // a DbgVirtualBaseClassTypeInfo.
        private DbgUdtTypeInfo m_owningType;
        public DbgUdtTypeInfo OwningType
        {
            get
            {
                if( (null == m_owningType) && (0 != m_owningTypeId) )
                {
                    _EnsureValid();

                    SymTag symTag = DbgHelp.GetSymTag( Debugger.DebuggerInterface,
                                                       Module.BaseAddress,
                                                       m_owningTypeId );
                    switch( symTag )
                    {
                        case SymTag.UDT:
                            m_owningType = DbgUdtTypeInfo.GetUdtTypeInfo( Debugger,
                                                                          Module,
                                                                          m_owningTypeId );
                            break;
                        case SymTag.BaseClass:
                            m_owningType = DbgBaseClassTypeInfo.GetBaseClassTypeInfo( Debugger,
                                                                                      Module,
                                                                                      m_owningTypeId );
                            break;
                        default:
                            throw new DbgProviderException( Util.Sprintf( "Unexpected: VTable type {0} has an owning type ({1}) which is not a UDT or BaseClass (it's a {2}).",
                                                                          TypeId,
                                                                          m_owningTypeId,
                                                                          symTag ),
                                                            "Unexpected_NotUdtOrBaseClass",
                                                            System.Management.Automation.ErrorCategory.MetadataError,
                                                            this );
                    } // end switch( symTag )
                } // end if( it's null )
                return m_owningType;
            }
        } // end property OwningType


        // If I ever see a VTable with children, I'll expose them.
    //  private IReadOnlyList< DbgTypeInfo > m_children;
    //  public IReadOnlyList< DbgTypeInfo > Children
    //  {
    //      get
    //      {
    //          if( null == m_children )
    //          {
    //              var childrenIds = GetChildrenIds( m_numChildren );
    //              var children = new List<DbgTypeInfo>( childrenIds.Length );
    //              foreach( var childId in childrenIds )
    //              {
    //                  children.Add( DbgTypeInfo.GetTypeInfo( Debugger, Module, childId ) );
    //              }
    //              m_children = children.AsReadOnly();
    //          }
    //          return m_children;
    //      }
    //  } // end property Children


        private DbgVTableShapeTypeInfo m_vtableShape;
        public DbgVTableShapeTypeInfo VTableShape
        {
            get
            {
                if( null == m_vtableShape )
                {
                    _EnsureValid();

                    // Oh crud. One reason this won't work is because currently
                    // DbgPointerTypeInfo assumes that the pointee type will always be a
                    // DbgNamedTypeInfo... but it turns out that's not the case--here the
                    // pointee type is a VTableShape (DbgVTableShapeTypeInfo), which has
                    // no name.
                    //
                    // TODO: DbgPointerTypeInfo probably has to be amended to not assume
                    // that the pointee has a name. That might have unfortunate ripples.
                    //
                    //var dpti = DbgPointerTypeInfo.GetPointerTypeInfo( Debugger, Module, m_pointerTypeId );
                    //m_vtableShape = DbgVTableShapeTypeInfo.GetVTableShapeTypeInfo( Debugger, Module, dpti.PointeeType.TypeId );
                    uint pointeeTypeId;
                    ulong dontCareSize;
                    bool dontCareIsRef;
                    DbgHelp.GetPointerTypeInfo( Debugger.DebuggerInterface,
                                                Module.BaseAddress,
                                                m_pointerTypeId,
                                                out pointeeTypeId,
                                                out dontCareSize,
                                                out dontCareIsRef );
                    m_vtableShape = DbgVTableShapeTypeInfo.GetVTableShapeTypeInfo( Debugger, Module, pointeeTypeId );
                }
                return m_vtableShape;
            }
        } // end property VTableShape


        public override string ToString()
        {
            return ToColorString().ToString( false );
        }

        private ColorString m_cs;

        public ColorString ToColorString()
        {
            if( null == m_cs )
            {
                m_cs = new ColorString( "VTable for " )
                    .Append( OwningType.ColorName )
                    .Append( Util.Sprintf( " (rel. offset {0}, {1} slots)",
                                           Offset,
                                           VTableShape.NumSlots ) )
                    .MakeReadOnly();
            }
            return m_cs;
        }


        public static DbgVTableTypeInfo GetVTableTypeInfo( DbgEngDebugger debugger,
                                                           DbgModuleInfo module,
                                                           uint typeId )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            if( null == module )
                throw new ArgumentNullException( "module" );

            RawVTableInfo rvi = DbgHelp.GetVTableInfo( debugger.DebuggerInterface, module.BaseAddress, typeId );
            return new DbgVTableTypeInfo( debugger,
                                          module,
                                          typeId,
                                          rvi.OwningClassTypeId,
                                          rvi.PointerTypeId,
                                          rvi.ChildrenCount,
                                          rvi.Offset );
        } // end GetVTableTypeInfo()
                                               

        public DbgVTableTypeInfo( DbgEngDebugger debugger,
                                  ulong moduleBase,
                                  uint typeId,
                                  uint owningTypeId,
                                  uint pointerTypeId,
                                  uint numChildren,
                                  uint offset,
                                  DbgTarget target )
            : base( debugger, moduleBase, typeId, SymTag.VTable, target )
        {
            m_owningTypeId = owningTypeId;
            m_pointerTypeId = pointerTypeId;
            m_numChildren = numChildren;
            Offset = offset;
        } // end constructor


        public DbgVTableTypeInfo( DbgEngDebugger debugger,
                                  DbgModuleInfo module,
                                  uint typeId,
                                  uint owningTypeId,
                                  uint pointerTypeId,
                                  uint numChildren,
                                  uint offset )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    owningTypeId,
                    pointerTypeId,
                    numChildren,
                    offset,
                    module.Target )
        {
            __mod = module;
        } // end constructor
    } // end class DbgVTableTypeInfo
}

