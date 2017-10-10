using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents a type that has a name and a size.
    /// </summary>
    [DebuggerDisplay( "Type: {m_name}" )]
    public abstract class DbgNamedTypeInfo : DbgTypeInfo, ISupportColor, IHaveName
    {
        private string m_name;
        public string Name
        {
            get
            {
                if( null == m_name )
                    m_name = GetName();

                return m_name;
            }
        }

        public string FullyQualifiedName
        {
            get
            {
                return Module.Name + "!" + Name;
            }
        }

        private DbgTemplateNode m_templateNode;
        public DbgTemplateNode TemplateNode
        {
            get
            {
                if( null == m_templateNode )
                    m_templateNode = DbgTemplateNode.CrackTemplate( Name );

                return m_templateNode;
            }
        }

        protected virtual string GetName()
        {
            if( !String.IsNullOrEmpty( m_name ) )
                return m_name;

            throw new NotImplementedException( Util.Sprintf( "Type {0} needs to implement GetName().",
                                                             Util.GetGenericTypeName( this ) ) );
        }

        private ColorString m_colorName;
        public ColorString ColorName
        {
            get
            {
                if( null == m_colorName )
                    m_colorName = GetColorName();

                return m_colorName;
            }
        }

        protected virtual ColorString GetColorName()
        {
            return DbgProvider.ColorizeTypeName( Name );
        } // end GetColorName()


        public ColorString ToColorString()
        {
            return ColorName;
        }

        private ulong? m_pSize;
        public ulong Size
        {
            get
            {
                if( null == m_pSize )
                    m_pSize = GetSize();

                return (ulong) m_pSize;
            }
        }

        protected virtual ulong GetSize()
        {
            if( null != m_pSize )
                return (ulong) m_pSize;

            throw new NotImplementedException( Util.Sprintf( "Type {0} needs to implement GetSize().",
                                                             Util.GetGenericTypeName( this ) ) );
        }

        public bool IsPointerSized()
        {
            return Size == GetTargetPointerSize();
        }

        public override string ToString()
        {
            return Name;
        }

        private IReadOnlyList< DbgTemplateNode > m_templateNodes;

        public IReadOnlyList< DbgTemplateNode > GetTemplateNodes()
        {
            if( null == m_templateNodes )
            {
                var templateTypeNames = new List< DbgTemplateNode >();
                DbgArrayTypeInfo ati = this as DbgArrayTypeInfo;
                if( null != ati )
                {
                    templateTypeNames.Add( DbgTemplateNode.CrackTemplate( ati.ArrayElementType.Name + "[]" ) ); // we don't want the dimensions
                    // TODO: And maybe for array types, we'd like to include the array type /with/ dimensions,
                    // so that, for instance, all Foo[8] could be treated specially or something. But maybe that's
                    // stretching it.

                    // TODO: How about co-(or is it contra-)variance? (should we get base types of the element type, like we do for pointers?)
                }
                else if( typeof( DbgUdtTypeInfo ).IsAssignableFrom( GetType() ) )
                {
                    DbgUdtTypeInfo uti = (DbgUdtTypeInfo) this;
                    templateTypeNames.Add( uti.TemplateNode );
                    _AddBaseClassNodesToList( templateTypeNames, uti );
                }
                else if( typeof( DbgPointerTypeInfo ).IsAssignableFrom( GetType() ) )
                {
                    DbgPointerTypeInfo pti = (DbgPointerTypeInfo) this;
                    templateTypeNames.Add( pti.TemplateNode );
                    _AddBaseClassPointerNodesToList( templateTypeNames, pti );
                }
                else
                {
                    templateTypeNames.Add( DbgTemplateNode.CrackTemplate( Name ) );
                }

                m_templateNodes = templateTypeNames.AsReadOnly();
            }
            return m_templateNodes;
        } // end GetTemplateNodes()


        private void _AddBaseClassNodesToList( List< DbgTemplateNode > list, DbgUdtTypeInfo uti )
        {
            uti.VisitAllBaseClasses( (bc) => list.Add( bc.TemplateNode ) );
        } // end _AddBaseClassNodesToList()


        private void _AddBaseClassPointerNodesToList( List< DbgTemplateNode > list, DbgPointerTypeInfo pti )
        {
            DbgNamedTypeInfo dnti = pti;
            int numStars = 0;
            while( dnti is DbgPointerTypeInfo )
            {
                dnti = ((DbgPointerTypeInfo) dnti).PointeeType;
                numStars++;
            }

            if( !typeof( DbgUdtTypeInfo ).IsAssignableFrom( dnti.GetType() ) )
                return; // It doesn't point to a UDT.

            string stars = new String( '*', numStars );
            var q = new Queue< DbgUdtTypeInfo >();
            var uti = (DbgUdtTypeInfo) dnti;

            uti.VisitAllBaseClasses( (bc) => list.Add( DbgTemplateNode.CrackTemplate( bc.TemplateNode.FullName + stars ) ) );
        } // end _AddBaseClassPointerNodesToList()


        private IReadOnlyList< string > m_typeNames;

        /// <summary>
        ///    Gets the list of type names to use for formatting, in order of precedence.
        /// </summary>
        public IReadOnlyList< string > GetTypeNames()
        {
            if( null == m_typeNames )
            {
                var templateNodes = GetTemplateNodes();
                var typeNames = new List<string>( templateNodes.Count * 2 );
                foreach( var ttn in templateNodes )
                {
                    // If we have a module, give a module-scoped name precedence:
                    if( 0 != Module.BaseAddress )
                        typeNames.Add( Module.Name + "!" + ttn.FullName );

                    typeNames.Add( "!" + ttn.FullName );
                }
                m_typeNames = typeNames.AsReadOnly();
            }
            return m_typeNames;
        } // end GetTypeNames()


        /// <summary>
        ///    Creates or gets a pointer type, pointing to the current type.
        /// </summary>
        /// <remarks>
        ///    This creates a synthetic pointer type (or retrieves a previously created
        ///    one) because it doesn't work well to query for a pointer type (the '*' is
        ///    interpreted as a wildcard, and the results do not include the desired
        ///    pointer types).
        /// </remarks>
        public DbgPointerTypeInfo GetPointerType()
        {
            _EnsureValid();
            uint pointerTypeId = DbgHelp.TryGetSynthPointerTypeIdByPointeeTypeId( Debugger.DebuggerInterface,
                                                                                  Module.BaseAddress,
                                                                                  TypeId );
            if( 0 == pointerTypeId )
            {
                pointerTypeId = DbgHelp.AddSyntheticPointerTypeInfo( Debugger.DebuggerInterface,
                                                                     Module.BaseAddress,
                                                                     TypeId,
                                                                     Debugger.PointerSize,
                                                                     0 ); // no predetermined typeId
            }
            var ptrType = new DbgPointerTypeInfo( Debugger,
                                                  Module,
                                                  pointerTypeId,
                                                  TypeId,
                                                  Debugger.PointerSize,
                                                  false );

            return ptrType;
        }


        private DbgNamedTypeInfo( DbgEngDebugger debugger,
                                  ulong moduleBase,
                                  uint typeId,
                                  SymTag symTag,
                                  string name,
                                  ulong? pSize,
                                  DbgTarget target )
            : base( debugger, moduleBase, typeId, symTag, target )
        {
            m_name = name;
            m_pSize = pSize;
        } // end constructor

        protected DbgNamedTypeInfo( DbgEngDebugger debugger,
                                    ulong moduleBase,
                                    uint typeId,
                                    SymTag symTag,
                                    string name,
                                    ulong size,
                                    DbgTarget target )
            : this( debugger, moduleBase, typeId, symTag, name, (ulong?) size, target )
        {
        } // end constructor

        protected DbgNamedTypeInfo( DbgEngDebugger debugger,
                                    ulong moduleBase,
                                    uint typeId,
                                    SymTag symTag,
                                    string name,
                                    DbgTarget target )
            : this( debugger, moduleBase, typeId, symTag, name, null, target )
        {
        } // end constructor

        protected DbgNamedTypeInfo( DbgEngDebugger debugger,
                                    ulong moduleBase,
                                    uint typeId,
                                    SymTag symTag,
                                    ulong size,
                                    DbgTarget target )
            : this( debugger, moduleBase, typeId, symTag, null, size, target )
        {
        } // end constructor

        protected DbgNamedTypeInfo( DbgEngDebugger debugger,
                                    ulong moduleBase,
                                    uint typeId,
                                    SymTag symTag,
                                    DbgTarget target )
            : this( debugger, moduleBase, typeId, symTag, null, null, target )
        {
        } // end constructor

        protected DbgNamedTypeInfo( DbgEngDebugger debugger,
                                    DbgModuleInfo module,
                                    uint typeId,
                                    SymTag symTag,
                                    string name,
                                    ulong size )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    symTag,
                    name,
                    size,
                    module.Target )
        {
            __mod = module;
        } // end constructor

        protected DbgNamedTypeInfo( DbgEngDebugger debugger,
                                    DbgModuleInfo module,
                                    uint typeId,
                                    SymTag symTag,
                                    string name )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    symTag,
                    name,
                    null,
                    module.Target )
        {
            __mod = module;
        } // end constructor

        protected DbgNamedTypeInfo( DbgEngDebugger debugger,
                                    DbgModuleInfo module,
                                    uint typeId,
                                    SymTag symTag,
                                    ulong size )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    symTag,
                    null,
                    size,
                    module.Target )
        {
            __mod = module;
        } // end constructor

        protected DbgNamedTypeInfo( DbgEngDebugger debugger,
                                    DbgModuleInfo module,
                                    uint typeId,
                                    SymTag symTag )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    symTag,
                    null,
                    null,
                    module.Target )
        {
            __mod = module;
        } // end constructor
    } // end class DbgNamedTypeInfo
}

