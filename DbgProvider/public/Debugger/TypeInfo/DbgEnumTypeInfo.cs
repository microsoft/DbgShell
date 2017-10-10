using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [DebuggerDisplay( "Enum: Id {TypeId}: {Name}" )]
    public class DbgEnumTypeInfo : DbgNamedTypeInfo
    {
        private DbgBaseTypeInfo m_baseType;
        private uint m_numEnumerands;

        private IReadOnlyList< Enumerand > m_enumerands;


        public DbgBaseTypeInfo BaseType { get { return m_baseType; } }


        public IReadOnlyList< Enumerand > Enumerands
        {
            get
            {
                if( null == m_enumerands )
                {
                    _EnsureValid();
                    m_enumerands = new List< Enumerand >( DbgHelp.GetEnumerands( Debugger.DebuggerInterface,
                                                                                 Module.BaseAddress,
                                                                                 TypeId,
                                                                                 Size,
                                                                                 m_numEnumerands ) ).AsReadOnly();
                }
                return m_enumerands;
            }
        } // end property Enumerands


        private PSObject m_byName;

        public PSObject ByName
        {
            get
            {
                if( null == m_byName )
                {
                    m_byName = new PSObject();
                    foreach( var e in Enumerands )
                    {
                        m_byName.Properties.Add( new SimplePSPropertyInfo( e.Name, e.Value ) );
                    }
                }
                return m_byName;
            }
        }

        public static DbgEnumTypeInfo GetEnumTypeInfo( DbgEngDebugger debugger,
                                                       DbgModuleInfo module,
                                                       uint typeId )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            RawEnumInfo rei = DbgHelp.GetEnumInfo( debugger.DebuggerInterface,
                                                   module.BaseAddress,
                                                   typeId );

            return new DbgEnumTypeInfo( debugger, module, rei );
        } // end GetEnumTypeInfo()
                                               

        public DbgNamedTypeInfo AsDifferentSize( ulong newSize )
        {
            // Should I check to see if all enumerands will fit within newSize?
            //
            // But maybe that's too zealous... because there could be a "mask"-style
            // enumerand that's defined as "all the top bits" (0xffffff00) or something
            // like that. So I'll leave it alone for now and trust the user to know what
            // they are doing.
            //return new DbgEnumTypeInfo( Debugger, Module, TypeId, newSize, Name, Process );
            return new DbgEnumTypeInfo( this, newSize );
        }


        private DbgEnumTypeInfo( DbgEngDebugger debugger,
                                 ulong moduleBase,
                                 uint typeId,
                                 string name,
                                 DbgBaseTypeInfo baseType,
                                 uint numEnumerands,
                                 DbgTarget target )
            : base( debugger,
                    moduleBase,
                    typeId,
                    SymTag.Enum,
                    name,
                    baseType.Size,
                    target )
        {
            m_baseType = baseType;
            m_numEnumerands = numEnumerands;
        } // end constructor

        private DbgEnumTypeInfo( DbgEngDebugger debugger,
                                 DbgModuleInfo module,
                                 RawEnumInfo rei )
            : this( debugger,
                    GetModBase( module ),
                    rei.TypeId,
                    rei.Name,
                    new DbgBaseTypeInfo( debugger,
                                         module,
                                         rei.BaseTypeTypeId,
                                         rei.BaseType,
                                         rei.Size ),
                    rei.NumEnumerands,
                    module.Target )
        {
            __mod = module;
        } // end constructor

        private DbgEnumTypeInfo( DbgEnumTypeInfo copyMe,
                                 ulong newSize )
            : this( copyMe.Debugger,
                    copyMe.Module.BaseAddress,
                    copyMe.TypeId,
                    copyMe.Name,
                    copyMe.BaseType.AsDifferentSize( newSize ),
                    copyMe.m_numEnumerands,
                    copyMe.Target )
        {
            __mod = copyMe.Module;
        } // end constructor

        #region IEquatable stuff

        public override bool Equals( DbgTypeInfo other )
        {
            var eti = other as DbgEnumTypeInfo;
            if( null == eti )
                return false;

            if( (Size != eti.Size) ||
                (m_modBase != eti.m_modBase) ||
                (0 != String.CompareOrdinal( Name, eti.Name )) ||
                (Enumerands.Count != eti.Enumerands.Count) )
            {
                return false;
            }

            for( int i = 0; i < Enumerands.Count; i++ )
            {
                if( Enumerands[ i ] != eti.Enumerands[ i ] )
                {
                    return false;
                }
            }

            return true;
        } // end Equals( DbgTypeInfo )

        public override int GetHashCode()
        {
            int hash = Name.GetHashCode();
            hash ^= (int) Size;
            hash ^= m_modBase.GetHashCode();
            foreach( var e in Enumerands )
            {
                hash ^= e.GetHashCode();
            }
            return hash;
        } // end GetHashCode()

        #endregion IEquatable stuff
    } // end class DbgEnumTypeInfo
}

