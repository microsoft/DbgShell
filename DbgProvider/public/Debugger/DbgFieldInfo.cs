using System;
using System.Diagnostics;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents a field in a type.
    /// </summary>
    [DebuggerDisplay( "Field: +{Offset} {Name}" )]
    public class DbgFieldInfo : DebuggerObject
    {
        /// <summary>
        ///    The name of the field.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        ///    The offset of the field within the owning type.
        /// </summary>
        public uint Offset { get; private set; }

        /// <summary>
        ///    The type of the field.
        /// </summary>
        public DbgNamedTypeInfo Type { get; private set; }

        /// <summary>
        ///    The type that the field belongs to.
        /// </summary>
        public DbgTypeInfo OwningType { get; private set; }


        public bool IsPointer { get { return Is32BitPointer | Is64BitPointer; } }
        public bool Is32BitPointer { get; private set; }
        public bool Is64BitPointer { get; private set; }

        public bool IsArray { get; private set; }
        //public bool IsStruct { get; private set; }
        public bool IsConstant { get; private set; }
        public bool IsStatic { get; private set; }

        public uint Size { get; private set; }

        public DbgFieldInfo( DbgEngDebugger debugger,
                             DbgTypeInfo owningType,
                             string fieldName,
                             uint fieldOffset,
                             bool is32BitPointer,
                             bool is64BitPointer,
                             bool isArray,
                             //bool isStruct,
                             bool isConstant,
                             bool isStatic,
                             uint size,
                             DbgNamedTypeInfo fieldType )
            : base( debugger )
        {
            if( null == owningType )
                throw new ArgumentNullException( "owningType" );

            if( String.IsNullOrEmpty( fieldName ) )
                throw new ArgumentException( "You must supply a field name.", "fieldName" );

            if( null == fieldType )
                throw new ArgumentNullException( "fieldType" );

            OwningType = owningType;
            Type = fieldType;
            Offset = fieldOffset;
            Name = fieldName;
            Is32BitPointer = is32BitPointer;
            Is64BitPointer = is64BitPointer;
            IsArray = isArray;
            //IsStruct = isStruct;
            IsConstant = isConstant;
            IsStatic = isStatic;
            Size = size;
        } // end constructor

        private static string _ValidateFieldInfoAndGetName( Microsoft.Diagnostics.Runtime.Interop.FieldInfo fieldInfo )
        {
            if( null == fieldInfo )
                throw new ArgumentNullException( "fieldInfo" );

            return fieldInfo.Name;
        } // end _ValidateFieldInfoAndGetName()

        internal DbgFieldInfo( DbgEngDebugger debugger,
                               DbgTypeInfo owningType,
                               Microsoft.Diagnostics.Runtime.Interop.FieldInfo fieldInfo,
                               ulong modBase,
                               DbgUModeProcess process ) // TODO BUGBUG? There is no module base field in 'f'... is it not possible for a field to be of a type from some other module?
            : this( debugger,
                    owningType,
                    _ValidateFieldInfoAndGetName( fieldInfo ),
                    fieldInfo.FieldOffset,
                    (fieldInfo.PointerFlag != Microsoft.Diagnostics.Runtime.Interop.PointerFlag.None) &&
                        (fieldInfo.PointerFlag != Microsoft.Diagnostics.Runtime.Interop.PointerFlag.Pointer64),
                    fieldInfo.PointerFlag.HasFlag( Microsoft.Diagnostics.Runtime.Interop.PointerFlag.Pointer64 ),
                    fieldInfo.IsArray,
                    //fieldInfo.IsStruct,
                    fieldInfo.IsConstant,
                    fieldInfo.IsStatic,
                    fieldInfo.Size,
                    DbgTypeInfo.GetNamedTypeInfo( debugger,
                                                  modBase,
                                                  fieldInfo.TypeId,
                                                  process ) )
        {
            // nothing
        } // end constructor
    } // end class DbgFieldInfo
}
