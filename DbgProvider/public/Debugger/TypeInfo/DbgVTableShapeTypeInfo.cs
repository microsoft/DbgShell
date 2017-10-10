using System;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [DebuggerDisplay( "VTableShape: Id {TypeId} ({NumSlots} slots)" )]
    public class DbgVTableShapeTypeInfo : DbgTypeInfo
    {
        public readonly uint NumSlots;

        // If I ever see a VTableShape with children, then we can uncomment this stuff.
        // (there is an assert in DbgHelp.cs to detect that)
     // private readonly uint m_numChildren;

     // // TODO: Can I narrow the scope of this type down?
     // private IReadOnlyList< DbgTypeInfo > m_children;
     // public IReadOnlyList< DbgTypeInfo > Children
     // {
     //     get
     //     {
     //         // TODO: Segregate into members, functions, statics, nested types, etc.
     //         if( null == m_children )
     //         {
     //             var childrenIds = GetChildrenIds( m_numChildren );
     //             var children = new List<DbgTypeInfo>( childrenIds.Length );
     //             foreach( var childId in childrenIds )
     //             {
     //                 children.Add( DbgTypeInfo.GetTypeInfo( Debugger, Module, childId ) );
     //             }
     //             m_children = children.AsReadOnly();
     //         }
     //         return m_children;
     //     }
     // } // end property Children


        public override string ToString()
        {
            return Util.Sprintf( "{0} slots", NumSlots );
        }

        public static DbgVTableShapeTypeInfo GetVTableShapeTypeInfo( DbgEngDebugger debugger,
                                                                     DbgModuleInfo module,
                                                                     uint typeId )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            if( null == module )
                throw new ArgumentNullException( "module" );

            RawVTableShapeInfo rvsi = DbgHelp.GetVTableShapeInfo( debugger.DebuggerInterface,
                                                                  module.BaseAddress,
                                                                  typeId );
            return new DbgVTableShapeTypeInfo( debugger,
                                               module,
                                               typeId,
                                               rvsi.NumSlots );
        } // end GetVTableTypeInfo()
                                               

        public DbgVTableShapeTypeInfo( DbgEngDebugger debugger,
                                       ulong moduleBase,
                                       uint typeId,
                                       uint numSlots,
                                       DbgTarget target )
            : base( debugger, moduleBase, typeId, SymTag.VTableShape, target )
        {
            NumSlots = numSlots;
        } // end constructor


        public DbgVTableShapeTypeInfo( DbgEngDebugger debugger,
                                       DbgModuleInfo module,
                                       uint typeId,
                                       uint numSlots )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    numSlots,
                    module.Target )
        {
            __mod = module;
        } // end constructor
    } // end class DbgVTableShapeTypeInfo
}

