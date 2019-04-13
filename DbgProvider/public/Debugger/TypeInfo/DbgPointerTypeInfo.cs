using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [DebuggerDisplay( "PointerType: Id {TypeId} -> {m_pointeeTypeId}" )]
    public class DbgPointerTypeInfo : DbgNamedTypeInfo
    {
        private readonly uint m_pointeeTypeId;
        private DbgNamedTypeInfo m_pointeeType;

        protected override string GetName()
        {
            if( !IsReference )
                return PointeeType.Name + "*";
            else
                return PointeeType.Name + "&";
        }


        // TODO: What possible things can a PointerType be pointing to? (can I narrow the
        // scope of the return type, or hint the static factory method what type of thing
        // it should be creating and therefore what sort of data to request)
        public DbgNamedTypeInfo PointeeType
        {
            get
            {
                if( null == m_pointeeType )
                {
                    _EnsureValid();
                    m_pointeeType = DbgNamedTypeInfo.GetNamedTypeInfo( Debugger,
                                                                       Module,
                                                                       m_pointeeTypeId );
                }
                return m_pointeeType;
            }
        }

        public readonly bool IsReference;


        public static DbgPointerTypeInfo GetPointerTypeInfo( DbgEngDebugger debugger,
                                                             DbgModuleInfo module,
                                                             uint typeId )
        {
            uint pointeeTypeId;
            ulong size;
            bool isReference;
            DbgHelp.GetPointerTypeInfo( debugger.DebuggerInterface,
                                        module.BaseAddress,
                                        typeId,
                                        out pointeeTypeId,
                                        out size,
                                        out isReference );
            return new DbgPointerTypeInfo( debugger, module, typeId, pointeeTypeId, size, isReference );
        } // end GetPointerTypeInfo()


        public DbgPointerTypeInfo( DbgEngDebugger debugger,
                                   ulong moduleBase,
                                   uint typeId,
                                   uint pointeeTypeId,
                                   ulong size,
                                   bool isReference,
                                   DbgTarget target )
            : base( debugger, moduleBase, typeId, SymTag.PointerType, size, target )
        {
            m_pointeeTypeId = pointeeTypeId;
            IsReference = isReference;
        } // end constructor

        public DbgPointerTypeInfo( DbgEngDebugger debugger,
                                   DbgModuleInfo module,
                                   uint typeId,
                                   uint pointeeTypeId,
                                   ulong size,
                                   bool isReference )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    pointeeTypeId,
                    size,
                    isReference,
                    module.Target )
        {
            __mod = module;
        } // end constructor


        public static DbgPointerTypeInfo GetFakeVoidStarType( DbgEngDebugger debugger,
                                                              DbgModuleInfo module )
        {
            return new DbgPointerTypeInfo( debugger, module, 0, 0, debugger.PointerSize, false );
        }

        #region IEquatable stuff

        public override bool Equals( DbgTypeInfo other )
        {
            var pti = other as DbgPointerTypeInfo;
            if( null == pti )
                return false;

            return (IsReference == pti.IsReference) &&
                   (PointeeType.Equals( pti.PointeeType ));
        } // end Equals( DbgTypeInfo )

        public override int GetHashCode()
        {
            int hash = PointeeType.GetHashCode();
            if( IsReference )
                hash |= unchecked( (int) 0x80000000 );
            else
                hash &= unchecked( (int) 0x7fffffff );

            return hash;
        } // end GetHashCode()

        #endregion IEquatable stuff
    } // end class DbgPointerTypeInfo
}

