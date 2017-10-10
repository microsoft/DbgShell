using System;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [DebuggerDisplay( "Array: Id {TypeId}: {Name}" )]
    public class DbgArrayTypeInfo : DbgNamedTypeInfo
    {
        private readonly uint m_arrayElemTypeId;
        private DbgNamedTypeInfo m_arrayElemType;

        public DbgNamedTypeInfo ArrayElementType
        {
            get
            {
                if( null == m_arrayElemType )
                {
                    _EnsureValid();
                    m_arrayElemType = DbgTypeInfo.GetNamedTypeInfo( Debugger, Module, m_arrayElemTypeId );
                }
                return m_arrayElemType;
            }
        } // end property ArrayType


        /// <summary>
        ///    The number of elements in the array.
        /// </summary>
        public readonly uint Count;


        protected override string GetName()
        {
            return GetColorName().ToString( false );
        } // end GetName()


        private static void _AppendDimension( ColorString cs, uint dim )
        {
            cs.AppendPushPopFg( ConsoleColor.Yellow, "[" )
              .AppendPushPopFg( ConsoleColor.DarkYellow, dim.ToString() )
              .AppendPushPopFg( ConsoleColor.Yellow, "]" );
        }

        protected override ColorString GetColorName()
        {
            ColorString csDimensions = new ColorString();
            DbgNamedTypeInfo curDti = this;
            while( curDti is DbgArrayTypeInfo )
            {
                var ati = (DbgArrayTypeInfo) curDti;
                _AppendDimension( csDimensions, ati.Count );
                curDti = ati.ArrayElementType;
            }
            return new ColorString( curDti.ColorName ).Append( csDimensions ).MakeReadOnly();
        } // end GetColorName()


        public static DbgArrayTypeInfo GetArrayTypeInfo( DbgEngDebugger debugger,
                                                         DbgModuleInfo module,
                                                         uint typeId )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            uint arrayTypeId;
            uint count;
            ulong size;
            DbgHelp.GetArrayTypeInfo( debugger.DebuggerInterface,
                                      module.BaseAddress,
                                      typeId,
                                      out arrayTypeId,
                                      out count,
                                      out size );
            return new DbgArrayTypeInfo( debugger, module, typeId, arrayTypeId, count, size );
        } // end GetArrayTypeInfo()


        public DbgArrayTypeInfo( DbgEngDebugger debugger,
                                 ulong moduleBase,
                                 uint typeId,
                                 uint arrayTypeId,
                                 uint count,
                                 ulong size,
                                 DbgTarget target )
            : base( debugger, moduleBase, typeId, SymTag.ArrayType, null, size, target )
        {
            m_arrayElemTypeId = arrayTypeId;
            Count = count;
            Util.Assert( Size >= (ulong) (ArrayElementType.Size * Count) );
            if( Size != (ulong) (ArrayElementType.Size * Count) )
            {
                // This seems rare, but I've seen it happen. For instance, the
                // shell32!ObjectMap symbol on 32-bit win7 had a Size of 0x24 and an
                // element count of 1, but the element size was 0x1c. I thought it might
                // be some sort of padding (perhaps for alignment), but it seemed like a
                // strange amount of padding (why pad to 0x24 instead of 0x20??).
                LogManager.Trace( "Warning: strange array type size. The size of the type is 0x{0:x}, but the (ArrayElementType.Size * Count) is (0x{1:x} * {2}) = 0x{3:x}.",
                                  Size,
                                  ArrayElementType.Size,
                                  Count,
                                  (ulong) (ArrayElementType.Size * Count) );
            }
        } // end constructor

        public DbgArrayTypeInfo( DbgEngDebugger debugger,
                                 DbgModuleInfo module,
                                 uint typeId,
                                 uint arrayTypeId,
                                 uint count,
                                 ulong size )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    arrayTypeId,
                    count,
                    size,
                    module.Target )
        {
            __mod = module;
        } // end constructor

        #region IEquatable stuff

        public override bool Equals( DbgTypeInfo other )
        {
            var ati = other as DbgArrayTypeInfo;
            if( null == ati )
                return false;

            return (Count == ati.Count) &&
                   (ArrayElementType.Equals( ati.ArrayElementType ));
        } // end Equals( DbgTypeInfo )

        public override int GetHashCode()
        {
            int hash = ArrayElementType.GetHashCode();
            hash ^= (int) Count;
            return hash;
        } // end GetHashCode()

        #endregion IEquatable stuff
    } // end class DbgArrayTypeInfo
}

