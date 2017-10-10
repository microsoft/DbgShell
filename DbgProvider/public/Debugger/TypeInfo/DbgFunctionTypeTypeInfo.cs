using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [DebuggerDisplay( "Function: Id {TypeId}: {Name}" )]
    public class DbgFunctionTypeTypeInfo : DbgNamedTypeInfo
    {
        private readonly uint m_returnTypeId;
        private readonly uint m_classParentId;
        public readonly uint ThisAdjust; // Only valid if m_classParentId != 0
        public readonly CallingConvention CallingConvention;
        private readonly IReadOnlyList< RawFuncArgTypeInfo > m_rawArguments;


        private IReadOnlyList< DbgFunctionArgTypeTypeInfo > m_arguments;
        public IReadOnlyList< DbgFunctionArgTypeTypeInfo > Arguments
        {
            get
            {
                if( null == m_arguments )
                {
                    _EnsureValid();
                    var list = new List< DbgFunctionArgTypeTypeInfo >( m_rawArguments.Count );
                    foreach( var rawArg in m_rawArguments )
                    {
                        list.Add( new DbgFunctionArgTypeTypeInfo( Debugger,
                                                                  Module,
                                                                  rawArg.TypeId,
                                                                  rawArg.ArgTypeId ) );
                    }
                    m_arguments = list.AsReadOnly();
                }
                return m_arguments;
            }
        } // end property Arguments


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


        private DbgNamedTypeInfo m_returnType;
        public DbgNamedTypeInfo ReturnType
        {
            get
            {
                if( null == m_returnType )
                {
                    _EnsureValid();
                    m_returnType = DbgTypeInfo.GetNamedTypeInfo( Debugger,
                                                                 Module,
                                                                 m_returnTypeId );
                }
                return m_returnType;
            }
        } // end property ReturnType


        protected override string GetName()
        {
            return GetColorName().ToString( false );
        }

        private ColorString m_colorName;

        // TODO: Is this sufficient? I wonder what a function pointer that returns
        // a function pointer looks like...
        protected override ColorString GetColorName()
        {
            if( null == m_colorName )
            {
                var cs = new ColorString();

                cs.Append( DbgProvider.ColorizeTypeName( ReturnType.Name ) );
                cs.Append( " (*fn)( " );
                if( 0 == Arguments.Count )
                {
                    cs.Append( DbgProvider.ColorizeTypeName( "void" ) );
                    cs.Append( " " );
                }
                else
                {
                    for( int i = 0; i < Arguments.Count; i++ )
                    {
                        cs.Append( DbgProvider.ColorizeTypeName( Arguments[ i ].ArgType.Name ) );
                        if( i < (Arguments.Count - 1) )
                            cs.Append( "," );

                        cs.Append( " " );
                    }
                }
                m_colorName = cs.Append( ")" ).MakeReadOnly();
            }
            return m_colorName;
        } // end GetColorName()


        public static DbgFunctionTypeTypeInfo GetFunctionTypeTypeInfo( DbgEngDebugger debugger,
                                                                       DbgModuleInfo module,
                                                                       uint typeId )
        {
            if( null == debugger )
                throw new ArgumentNullException( "debugger" );

            if( null == module )
                throw new ArgumentNullException( "module" );

            var rfti = DbgHelp.GetFuncTypeInfo( debugger.DebuggerInterface,
                                                module.BaseAddress,
                                                typeId );

            return new DbgFunctionTypeTypeInfo( debugger,
                                                module,
                                                typeId,
                                                rfti.ReturnTypeId,
                                                rfti.ClassParentId,
                                                rfti.ThisAdjust,
                                                rfti.CallingConvention,
                                                rfti.Arguments );
        } // end GetUdtTypeInfo()
                                               

        public DbgFunctionTypeTypeInfo( DbgEngDebugger debugger,
                                        ulong moduleBase,
                                        uint typeId,
                                        uint returnTypeId,
                                        uint classParentId,
                                        uint thisAdjust,
                                        CallingConvention callingConvention,
                                        IReadOnlyList< RawFuncArgTypeInfo > rawArguments,
                                        DbgTarget target )
            : base( debugger, moduleBase, typeId, SymTag.FunctionType, 0, target )
        {
            m_returnTypeId = returnTypeId;
            m_classParentId = classParentId;
            ThisAdjust = thisAdjust;
            CallingConvention = callingConvention;
            m_rawArguments = rawArguments;
        } // end constructor

        public DbgFunctionTypeTypeInfo( DbgEngDebugger debugger,
                                        DbgModuleInfo module,
                                        uint typeId,
                                        uint returnTypeId,
                                        uint classParentId,
                                        uint thisAdjust,
                                        CallingConvention callingConvention,
                                        IReadOnlyList< RawFuncArgTypeInfo > rawArguments )
            : this( debugger,
                    GetModBase( module ),
                    typeId,
                    returnTypeId,
                    classParentId,
                    thisAdjust,
                    callingConvention,
                    rawArguments,
                    module.Target )
        {
            __mod = module;
        } // end constructor
    } // end class DbgFunctionTypeTypeInfo
}

