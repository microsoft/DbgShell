using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [DebuggerDisplay( "Function: Id {TypeId}: {Name}" )]
    public class DbgFunctionTypeInfo : DbgNamedTypeInfo
    {
        private readonly uint m_funcTypeTypeId;
        private readonly uint m_owningClassTypeId;
        private readonly uint m_numChildren;

        public readonly ulong Address;
        public readonly ulong Length;
        public readonly uint VirtualBaseOffset;
        public readonly uint SymIndex; // TODO: I don't actually know what this is.

        private DbgFunctionTypeTypeInfo m_funcType;
        public DbgFunctionTypeTypeInfo FunctionType
        {
            get
            {
                if( (null == m_funcType) && (0 != m_funcTypeTypeId) )
                {
                    _EnsureValid();
                    m_funcType = DbgFunctionTypeTypeInfo.GetFunctionTypeTypeInfo( Debugger,
                                                                                  Module,
                                                                                  m_funcTypeTypeId );
                }
                return m_funcType;
            }
        } // end property FunctionType


        // Possible children include FuncDebugStart, FuncDebugEnd, and Data (like for the "this" param?)
        private List< DbgTypeInfo > m_children;
        public IReadOnlyList< DbgTypeInfo > Children
        {
            get
            {
                if( null == m_children )
                {
                    var childrenIds = GetChildrenIds( m_numChildren ); // _EnsureValid() called here
                    m_children = new List<DbgTypeInfo>( childrenIds.Length );
                    foreach( var childId in childrenIds )
                    {
                        m_children.Add( DbgTypeInfo.GetTypeInfo( Debugger, Module, childId ) );
                    }
                }
                return m_children;
            }
        } // end property Children


        private DbgUdtTypeInfo m_owningClass;
        public DbgUdtTypeInfo OwningClass
        {
            get
            {
                if( (null == m_owningClass) && (0 != m_owningClassTypeId) )
                {
                    _EnsureValid();
                    m_owningClass = DbgUdtTypeInfo.GetUdtTypeInfo( Debugger,
                                                                   Module,
                                                                   m_owningClassTypeId );
                }
                return m_owningClass;
            }
        } // end property OwningClass


        protected override ColorString GetColorName()
        {
            return DbgProvider.ColorizeFunctionName( Name );
        } // end GetColorName()


        private ColorString m_signature;
        public ColorString Signature
        {
            get
            {
                if( null == m_signature )
                {
                    m_signature = _BuildSignature();
                }
                return m_signature;
            }
        } // end property Signature


        private ColorString _BuildSignature()
        {
            var cs = new ColorString();
            if( !IsConstructor && !IsDestructor )
            {
                cs.Append( FunctionType.ReturnType.ColorName ).Append( " " );
            }

            cs.Append( ColorName ).Append( "(" );

            if( (0 == FunctionType.Arguments.Count) ||
                ((1 == FunctionType.Arguments.Count) && (0 == Util.Strcmp_OI( "void", FunctionType.Arguments[ 0 ].ArgType.Name ))) )
            {
                cs.Append( ")" );
            }
            else
            {
                bool first = true;
                foreach( var fa in FunctionType.Arguments )
                {
                    if( first )
                        first = false;
                    else
                        cs.Append( "," );

                    cs.Append( " " ).Append( fa.ArgType.ColorName );
                } // end foreach( arg )
                cs.Append( " )" );
            } // end else( there are arguments )
            return cs.MakeReadOnly();
        } // end GetSignature()


        public bool IsConstructor
        {
            get
            {
                return (null != OwningClass) &&
                       // TODO: What about namespace stuff?
                       (0 == Util.Strcmp_OI( Name, OwningClass.Name )) &&
                       (0 == Util.Strcmp_OI( "void", FunctionType.ReturnType.Name ));
            }
        } // end property IsConstructor


        public bool IsDestructor
        {
            get
            {
                return (null != OwningClass) &&
                       // TODO: What about namespace stuff?
                       (0 == Util.Strcmp_OI( Name, "~" + OwningClass.Name )) &&
                       (0 == Util.Strcmp_OI( "void", FunctionType.ReturnType.Name ));
            }
        } // end property IsDestructor


        public bool IsAbstract
        {
            get { return 0 == Address; }
        } // end property IsAbstract


        public static DbgFunctionTypeInfo GetFunctionTypeInfo( DbgEngDebugger debugger,
                                                               DbgModuleInfo module,
                                                               uint typeId )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            if( null == module )
                throw new ArgumentNullException( "module" );

            RawFuncInfo rfi = DbgHelp.GetFuncInfo( debugger.DebuggerInterface, module.BaseAddress, typeId );
            return new DbgFunctionTypeInfo( debugger,
                                            module,
                                            typeId,
                                            rfi.FuncName,
                                            rfi.FunctionTypeTypeId,
                                            rfi.OwningClassTypeId,
                                            rfi.ChildrenCount,
                                            rfi.Address,
                                            rfi.Length,
                                            rfi.VirtualBaseOffset,
                                            rfi.SymIndex );
        } // end GetFunctionTypeInfo()
                                               

        public DbgFunctionTypeInfo( DbgEngDebugger debugger,
                                    ulong moduleBase,
                                    uint typeId,
                                    string name,
                                    uint functionTypeTypeId,
                                    uint owningClassTypeId,
                                    uint numChildren,
                                    ulong address,
                                    ulong length,
                                    uint virtualBaseOffset,
                                    uint symIndex,
                                    DbgTarget target )
            : base( debugger, moduleBase, typeId, SymTag.Function, name, 0, target )
        {
            m_funcTypeTypeId = functionTypeTypeId;
            m_owningClassTypeId = owningClassTypeId;
            m_numChildren = numChildren;
            Address = address;
            Length = length;
            VirtualBaseOffset = virtualBaseOffset;
            SymIndex = symIndex;
        } // end constructor

        public DbgFunctionTypeInfo( DbgEngDebugger debugger,
                                    DbgModuleInfo module,
                                    uint typeId,
                                    string name,
                                    uint functionTypeTypeId,
                                    uint owningClassTypeId,
                                    uint numChildren,
                                    ulong address,
                                    ulong length,
                                    uint virtualBaseOffset,
                                    uint symIndex )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    name,
                    functionTypeTypeId,
                    owningClassTypeId,
                    numChildren,
                    address,
                    length,
                    virtualBaseOffset,
                    symIndex,
                    module.Target )
        {
            __mod = module;
        } // end constructor
    } // end class DbgFunctionTypeInfo
}

