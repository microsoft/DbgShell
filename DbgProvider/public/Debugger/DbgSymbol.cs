using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    [DebuggerDisplay( "Sym: {Name}" )]
    public abstract class DbgSymbol : DebuggerObject, IEquatable< DbgSymbol >, IHaveName
    {
        public string Name { get; private set; }

        public string ModuleQualifiedName
        {
            get { return Module.Name + "!" + Name; }
        }

        protected bool m_metaDataUnavailable;

        private bool? __memoryUnavailable;

        /// <summary>
        ///    Indicates if the memory represented by the symbol is unavailable. Note:
        ///    check IsMetadataUnavailable first; if metadata is unavailable, this will
        ///    throw, because the address will come from the metadata.
        /// </summary>
        public bool IsMemoryUnavailable
        {
            get
            {
                if( null == __memoryUnavailable )
                {
                    if( m_metaDataUnavailable )
                    {
                        __memoryUnavailable = true;
                    }
                    else if( IsValueInRegister )
                    {
                        __memoryUnavailable = false;
                    }
                    else if( IsConstant )
                    {
                        // Constants don't live in memory.
                        __memoryUnavailable = false;
                    }
                    else
                    {
                        // MEMORY_BASIC_INFORMATION64 memInfo;
                        // int hr = Debugger.TryQueryVirtual( Address, out memInfo );

                        // // TODO: Should I check the type of memory?
                        // //
                        // // Or maybe I should just try and actually read the memory?
                        // //
                        // // Also, I'm not covering the case where the size of the object is
                        // // larger than a page.
                        // //
                        // // +1 for switching to just reading the memory... iDNA targets do
                        // // not currently implement QueryVirtual, but it looks like
                        // // ReadVirtual might work on them...
                        // __memoryUnavailable = (0 != hr) && (hr != E_NOTIMPL);

                        // Not only is QueryVirtual problematic because not all targets
                        // implement it (iDNA), but it might not give us the answer we
                        // need: we might have the memInfo for the address in question,
                        // but not the memory (frequently the case with kernel minidumps).
                        // We are trying to find out if we have the memory in hand, so we
                        // need to try to read it.

                        __memoryUnavailable = !_ProbeMemory();
                    }
                }
                return (bool) __memoryUnavailable;
            }
        }


        /// <summary>
        ///    Returns true if the memory can be read.
        /// </summary>
        private bool _ProbeMemory()
        {
            if( 0 == Address )
                return false;

            uint readSize = (uint) Math.Min( Type.Size, 4096 );

            if( 0 == readSize )
            {
                Util.Assert( !CanFollow ); // things like function pointers
                // maybe for 0-sized things we should skip the memory probe? Or not?
                readSize = 1;
            }

            byte[] memDontCare;
            return Debugger.TryReadMem( Address,
                                        readSize,
                                        true,
                                        out memDontCare );
        } // end _ProbeMemory()


        public virtual bool IsConstant { get { return false; } }


        internal virtual object GetConstantValue()
        {
            if( !IsConstant )
            {
                throw new DbgProviderException( Util.Sprintf( "Symbol {0} is not a constant.",
                                                              Name ),
                                                "SymbolNotConstant",
                                                ErrorCategory.InvalidOperation,
                                                this );
            }

            string msg = Util.Sprintf( "Type {0} has IsConstant override but not GetConstantValue()?",
                                       Util.GetGenericTypeName( this ) );

            Util.Fail( msg );
            throw new Exception( msg );
        }


        public virtual bool IsThreadLocal { get { return false; } }

        public bool IsMetadataUnavailable { get { return m_metaDataUnavailable; } }


        public bool IsValueUnavailable { get { return m_metaDataUnavailable || IsMemoryUnavailable; } }

        // It would seem redundant to have m_context when we have m_target, but there's
        // a good reason: really we just want m_target, but during the "bootstrapping" of
        // attach, we don't always get all the info we want/need. In one of those cases,
        // we just take a DbgEngContext, which we can use later to get m_target, if we
        // need.
        private DbgEngContext m_context;
        private DbgTarget m_target;

        public DbgTarget Target
        {
            get
            {
                if( null == m_target )
                {
                    Util.Assert( null != m_context );
                    m_target = Debugger.GetTargetForContext( m_context );
                }
                return m_target;
            }
        }


        private SymbolIdentity m_identity;

        public SymbolIdentity Identity
        {
            get
            {
                if( null == m_identity )
                {
                    m_identity = new SymbolIdentity( this );
                }
                return m_identity;
            }
        }

        // TODO: This is also called "offset" in a lot of places. I think 'Address' is
        // a lot more user-friendly, but is it worth breaking with tradition?
        // This could throw if the value is unavailable...
        public abstract ulong Address { get; }

        public abstract bool IsValueInRegister { get; }

        /// <summary>
        ///    The register that contains the symbol's value, or null if the value is not
        ///    in a register.
        /// </summary>
        public abstract DbgRegisterInfoBase Register { get; }

        /// <summary>
        ///    Some pointers ("this") are adjusted by a certain amount. If the adjustment
        ///    is non-zero, it is automatically un-done when you call ReadAs_pointer().
        /// </summary>
        public virtual uint PointerAdjustment { get { return 0; } }

        public DbgSymbol Parent { get; protected set; }

        public abstract DbgModuleInfo Module { get; }

        public abstract DbgNamedTypeInfo Type { get; }

        // TODO: shouldn't be public, right?
        public void Expand()
        {
            if( IsExpanded )
                return;

            if( IsValueUnavailable )
            {
                LogManager.Trace( "Can't expand {0}: value is unavailable.", PathString );
                return;
            }

            // We do this to avoid INTe4db089f, wherein ExpandSymbol returns
            // E_FAIL instead of S_FALSE when there are no children.
    /////   if( 0 == Type.Fields.Count )
    /////   {
    /////       LogManager.Trace( "Type {0} has no fields, so nothing to expand.", Type.Name );
    /////       return;
    /////   }

            CoreExpand();
        } // end Expand()


        private bool m_isExpanded;

        protected bool IsExpanded
        {
            get { return m_isExpanded; }
        }

        private NameIndexableList< DbgSymbol > m_children = new NameIndexableList< DbgSymbol >();

        public IReadOnlyList<DbgSymbol> Children
        {
            get
            {
                if( !m_isExpanded )
                {
                    Expand();
                }
                return m_children;
            }
        }

        protected void CoreExpand()
        {
            do
            {
                var pointer = Type as DbgPointerTypeInfo;
                if( null != pointer )
                {
                    string pointeeName;
                    if( Name.StartsWith( "(*", StringComparison.OrdinalIgnoreCase ) )
                        pointeeName = "(*" + Name.Substring( 1 );
                    else
                        pointeeName = "(*" + Name + ")";

                    ulong pointee = ReadAs_pointer();
                    m_children.Add( new DbgSimpleSymbol( Debugger,
                                                         pointeeName,
                                                         pointer.PointeeType,
                                                         pointee,
                                                         this ) );
                    break;
                }

                var udt = Type as DbgUdtTypeInfo;
                if( null != udt )
                {
                    foreach( var member in udt.Members )
                    {
                        m_children.Add( new DbgMemberSymbol( Debugger,
                                                             this,
                                                             member ) );
                    }

                    foreach( var staticMember in udt.StaticMembers )
                    {
                        m_children.Add( new DbgSimpleSymbol( Debugger,
                                                             staticMember.Name,
                                                             staticMember.DataType,
                                                             staticMember.Address,
                                                             this ) );
                    }

                    // TODO: functions, nested types, etc.
                    break;
                }

             // var clr = Type as DbgClrTypeTypeInfo;
             // if( null != clr )
             // {
             //     clr.


             //     break;
             // }

                var bt = Type as DbgBaseTypeInfo;
                if( null != bt )
                {
                    // No children.
                    break;
                }

                
                var dmti = Type as DbgDataMemberTypeInfo;
                if( null != dmti )
                {
                    // No children.
                    break;
                }

                var at = Type as DbgArrayTypeInfo;
                if( null != at )
                {
                    // No children... I think it would be too crazy/expensive
                    // to have a child "symbol" for each element.
                    break;
                }

                var et = Type as DbgEnumTypeInfo;
                if( null != et )
                {
                    // No children.
                    break;
                }

                var ft = Type as DbgFunctionTypeTypeInfo;
                if( null != ft )
                {
                    // No children.
                    break;
                }

                var nt = Type as DbgNullTypeInfo;
                if( null != nt )
                {
                    // No children.
                    break;
                }

                var dgt = Type as DbgGeneratedTypeInfo;
                if( null != dgt )
                {
                    // No children.
                    break;
                }

                Util.Fail( Util.Sprintf( "Need to handle expand for type: {0}",
                                         Util.GetGenericTypeName( Type ) ) );
            } while( false );

            m_children.Freeze();
            m_isExpanded = true;
        } // end CoreExpand()

        public bool IsPointer
        {
            get
            {
                return (SymTag.PointerType == Type.SymTag) ||
                       (SymTag.FunctionType == Type.SymTag); // We consider function pointers (addresses of functions) to be pointers, too.
            }
        }

        public bool IsArray
        {
            get { return SymTag.ArrayType == Type.SymTag; }
        }

        public bool IsEnum
        {
            get { return SymTag.Enum == Type.SymTag; }
        }

        public bool IsUdt
        {
            get { return SymTag.UDT == Type.SymTag; }
        }

        public bool IsClrObject
        {
            get { return Type is DbgClrTypeTypeInfo; }
        }


        public bool AddressMatches( DbgSymbol sym )
        {
            if( IsValueUnavailable || sym.IsValueUnavailable )
                // TODO: or should I just return false?
                throw new InvalidOperationException( "Value(s) is unavailable; cannot check for address match." );

            return Address == sym.Address;
        } // end AddressMatches()


        // This can be cast to 'dynamic' for more convenient C# programming if desired.
        public PSObject Value
        {
            get
            {
                return GetValue( skipConversion: false,
                                 skipDerivedTypeDetection: false );
            }
        } // end property Value


        /// <summary>
        ///    This is handy for debugging converters.
        ///
        ///    WARNING: If any of these were manually set (such as by
        ///    DbgReplaceMemberValue), then you might not have a way to get it re-set.
        /// </summary>
        public void DumpCachedValue()
        {
            m_valueCache.Clear();
            __memoryUnavailable = null;
        }


        private IReadOnlyList< string > m_typeNames;
        private IReadOnlyList< DbgTemplateNode > m_templateNodes;


        private void _DiscoverTypeNameStuff()
        {
            if( null == Type )
            {
                m_templateNodes = new List< DbgTemplateNode >( 0 ).AsReadOnly();
                m_typeNames = new List< string >( 0 ).AsReadOnly();
            }
            else
            {
                m_typeNames = Type.GetTypeNames();
                m_templateNodes = Type.GetTemplateNodes();
            }
        } // end _DiscoverTypeNameStuff()


        public IReadOnlyList< string > GetTypeNames()
        {
            if( null == m_typeNames )
            {
                _DiscoverTypeNameStuff();
            }

            return m_typeNames;
        } // end GetTypeNames()


        public IReadOnlyList< DbgTemplateNode > GetTemplateNodes()
        {
            if( null == m_templateNodes )
            {
                _DiscoverTypeNameStuff();
            }

            return m_templateNodes;
        } // end GetTemplateNodes()


        // This is for symbols where the normal GetValue path converts it into
        // some native .NET object (such as converting a symbol representing a
        // WCHAR* into a System.String object). In some cases, the conversion
        // might not work right, might not expose all the needed info, or have
        // some other problem, and you need a way to get the value /without/
        // the conversion, which is what this method provides.
        public PSObject GetStockValue()
        {
            return GetValue( skipConversion: true, skipDerivedTypeDetection: false );
        }


        private DbgValueCache< PSObject > m_valueCache = new DbgValueCache< PSObject >();

        public PSObject GetValue( bool skipConversion, bool skipDerivedTypeDetection )
        {
            ValueOptions valOpts = DbgValueCache< PSObject >.ComputeValueOptions( skipConversion,
                                                                                  skipDerivedTypeDetection );

            ref PSObject val = ref m_valueCache[ valOpts ];

            if( null == val )
            {
                LogManager.Trace( "Creating value for symbol: {0} ({1})",
                                  this.PathString,
                                  valOpts );

                if( IsConstant )
                    val = DbgValue.CreateWrapperPso( this, GetConstantValue() );
                else
                    val = DbgValue.CreateDbgValue( this, skipConversion, skipDerivedTypeDetection );
            }

            return val;
        }


        /// <summary>
        ///    Useful for "manual" symbol value conversion. Sometimes this is done by the
        ///    converter script for a parent--e.g. the type CFoo has a member "LPVOID
        ///    MyMember", and the converter script for type CFoo wants to cast that
        ///    member's value to a "CRealType*".
        ///
        ///    One drawback to this is that if the cache is dumped, there is no way to
        ///    recreate that value.
        /// </summary>
        internal void SetCachedValue( PSObject val,
                                      bool skipConversion,
                                      bool skipDerivedTypeDetection )
        {
            if( null == val )
                throw new ArgumentNullException( "val" );

            m_valueCache[ skipConversion, skipDerivedTypeDetection ] = val;
        }


        /// <summary>
        ///    Some pointers (function pointers, void*) you can't sensibly dereference.
        /// </summary>
        public bool CanFollow
        {
            get
            {
                return IsPointer &&
                       (0 != Util.Strcmp_OI( "Void*", Type.Name )) &&
                       (SymTag.FunctionType != Type.SymTag) &&
                       (SymTag.Null != Type.SymTag); // for DbgGeneratedTypeInfo
            }
        }


        private List< DbgSymbol > m_path;

        /// <summary>
        ///    The path through a symbol group to the current DbgSymbol.
        ///    (including the current DbgSymbol)
        /// </summary>
        public IList< DbgSymbol > Path
        {
            get
            {
                if( null == m_path )
                {
                    var stack = new Stack< DbgSymbol >();
                    stack.Push( this );
                    DbgSymbol parent = Parent;
                    while( null != parent )
                    {
                        stack.Push( parent );
                        parent = parent.Parent;
                    }
                    m_path = stack.ToList();
                }
                return m_path.AsReadOnly();
            }
        } // end property Path


        private string m_pathString;
        private int m_nonDerefPathElements;

        public string PathString
        {
            get
            {
                if( null == m_pathString )
                {
                    m_pathString = _GetDbgEngPathString( out m_nonDerefPathElements );
                }
                return m_pathString;
            }
        } // end PathString


        public int NonDerefPathElements
        {
            get
            {
                if( null == m_pathString )
                {
                    m_pathString = _GetDbgEngPathString( out m_nonDerefPathElements );
                }
                return m_nonDerefPathElements;
            }
        }

        // This should produce a string suitable for passing to "Invoke-DbgEng '?? ...'".
        // Useful for comparing DbgShell results against normal debugger results.
        // TODO: BUG: This does not work for anything that requires multiple
        // derefs in a row (like if you have to follow a '**' pointer...)
        private string _GetDbgEngPathString( out int nonDerefPathElements )
        {
            nonDerefPathElements = 0;
            StringBuilder sb = new StringBuilder();
            bool first = true;
            if( Path[ 0 ] is DbgPublicSymbol )
                sb.Append( Module.Name ).Append( '!' );

            bool lastWasPointer = false;
            bool skippedPointer = false;
            foreach( var sym in Path )
            {
                if( !lastWasPointer ) // skip pointee (which is named "(*symName)")
                {
                    if( first )
                        first = false;
                    else
                    {
                        if( skippedPointer )
                        {
                            sb.Append( "->" );
                            skippedPointer = false;
                        }
                        else
                            sb.Append( "." );
                    }

                    sb.Append( sym.Name );
                    nonDerefPathElements++;
                }
                else
                {
                    // Multiple derefs in a row? Can't do that with just arrows ("->")...
                    if( skippedPointer )
                    {
                        sb.Insert( 0, "(*" ).Append( ")" );
                    }
                    skippedPointer = true;
                }
                lastWasPointer = sym.IsPointer;
            }
            if( skippedPointer )
            {
                // We can't just tack a trailing "->" on...
                sb.Insert( 0, "(*" ).Append( ")" );
            }
            return sb.ToString();
        } // end _GetDbgEngPathString()



        private static Dictionary< DbgNamedTypeInfo, Dictionary< uint, DbgNamedTypeInfo > >
            sm_syntheticArrayTypeCache = new Dictionary<DbgNamedTypeInfo, Dictionary<uint, DbgNamedTypeInfo>>();


        private static DbgNamedTypeInfo _GetArrayType( DbgNamedTypeInfo pointeeType, uint arrayLength )
        {
            Dictionary< uint, DbgNamedTypeInfo > sizeMap;
            if( !sm_syntheticArrayTypeCache.TryGetValue( pointeeType, out sizeMap ) )
            {
                sizeMap = new Dictionary<uint, DbgNamedTypeInfo>();
                sm_syntheticArrayTypeCache.Add( pointeeType, sizeMap );
            }

            DbgNamedTypeInfo arrayType;
            if( !sizeMap.TryGetValue( arrayLength, out arrayType ) )
            {
                uint newTypeId = DbgHelp.AddSyntheticArrayTypeInfo( pointeeType.Debugger.DebuggerInterface,
                                                                    pointeeType.Module.BaseAddress,
                                                                    pointeeType.TypeId,
                                                                    arrayLength,
                                                                    arrayLength * pointeeType.Size );
                arrayType = DbgTypeInfo.GetNamedTypeInfo( pointeeType.Debugger,
                                                          pointeeType.Module,
                                                          newTypeId );
                sizeMap.Add( arrayLength, arrayType );
            }
            return arrayType;
        } // end _GetArrayType()


        // For when you have a Foo*, but it points to an contiguous block of Foo, not just one.
        //
        // Actually, if you have a Foo*, the DbgValue indexer will be more convenient. But if
        // the type is some primitive type, you won't have that indexer.
        public DbgSymbol AsArray( uint arrayLength )
        {
            DbgNamedTypeInfo arrayType;
            ulong deref;

            if( IsPointer )
            {
                DbgPointerTypeInfo pti = (DbgPointerTypeInfo) Type;
                arrayType = _GetArrayType( pti.PointeeType, arrayLength );
                deref = ReadAs_pointer();
            }
            else
            {
                if( IsValueInRegister )
                    throw new NotSupportedException( "We don't support treating a register as an array. Ask if you need it." );

                deref = Address;

                if( IsArray )
                {
                    //
                    // Sometimes you want to do this when you have a variable-sized structure
                    // that is declared with a single-element array, but it's actually some
                    // variable number of elements.
                    //

                    DbgArrayTypeInfo ati = (DbgArrayTypeInfo) Type;
                    if( ati.Count == arrayLength )
                    {
                        // Already the right size.
                        return this;
                    }

                    arrayType = _GetArrayType( ati.ArrayElementType, arrayLength );
                }
                else
                {
                    //
                    // Sometimes you've dumped a single element at a particular memory
                    // location manually, but it's really the first element of an array.
                    //

                    arrayType = _GetArrayType( Type, arrayLength );
                }
            }

            var newSym = new DbgSimpleSymbol( Debugger,
                                              Name,
                                              arrayType,
                                              deref );
            return newSym;
        } // end AsArray()



        public DbgSymbol ChangeType( DbgNamedTypeInfo newType )
        {
            var asEnumType = newType as DbgEnumTypeInfo;
            if( null != asEnumType )
            {
                // The enum type may be declared without specifying storage, which will
                // result in the default of a size of 4 bytes. However, where it is
                // actually used may be a different size, so we have to account for that.
                // (example: KTHREAD_STATE (4-byte enum) and _KTHREAD.State (1 byte int))
                if( asEnumType.Size != Type.Size )
                {
                    newType = asEnumType.AsDifferentSize( Type.Size );
                }
            }

            DbgSimpleSymbol newSym;
            if( IsValueInRegister )
            {
                newSym = new DbgSimpleSymbol( Debugger, Name, newType, Register, Parent );
            }
            else
            {
                newSym = new DbgSimpleSymbol( Debugger, Name, newType, Address, Parent );
            }

            return newSym;
        }


        /// <summary>
        ///    Performs a semi-shallow copy of the current object. Be careful with this!
        ///    Used to avoid some aliasing when PSSymFieldInfo objects are copied.
        /// </summary>
        internal DbgSymbol Copy()
        {
            var copy = (DbgSymbol) MemberwiseClone();

            copy.m_valueCache.Clear();

            return copy;
        }


        protected DbgSymbol( DbgEngDebugger debugger, string name, DbgTarget target )
            : base( debugger )
        {
            if( String.IsNullOrEmpty( name ) )
                throw new ArgumentException( "You must supply a symbol name.", "name" );

            if( null == target )
                throw new ArgumentNullException( "target" );

            Name = name;
            m_target = target;
        } // end constructor

        /// <summary>
        ///    This constructor is a workaround for the fact that we may not have some
        ///    target information early in the attach process, so we settle for just the
        ///    context, and hope it will work to get the full target object later if we
        ///    need it.
        /// </summary>
        protected DbgSymbol( DbgEngDebugger debugger, string name, DbgEngContext context )
            : base( debugger )
        {
            if( String.IsNullOrEmpty( name ) )
                throw new ArgumentException( "You must supply a symbol name.", "name" );

            if( null == context )
                throw new ArgumentNullException( "context" );

            Name = name;
            m_context = context;
        } // end constructor


        #region Memory access stuff

        private void _CheckValueAvailable()
        {
            if( IsValueUnavailable )
                // TODO: value unavailable exception?
                throw new InvalidOperationException( "The value is unavailable." );
        }

        public sbyte ReadAs_sbyte()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
                return (sbyte) Register.DEBUG_VALUE.I8;
            else
                return Debugger.ReadMemAs_sbyte( Address );
        }

        public char ReadAs_WCHAR()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
                return (char) Register.DEBUG_VALUE.I16;
            else
                return Debugger.ReadMemAs_WCHAR( Address );
        }

        public short ReadAs_short()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
                return (short) Register.DEBUG_VALUE.I16;
            else
                return Debugger.ReadMemAs_short( Address );
        }

        public Int32 ReadAs_Int32()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
                return (Int32) Register.DEBUG_VALUE.I32;
            else
                return Debugger.ReadMemAs_Int32( Address );
        }

        public Int64 ReadAs_Int64()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
                return (Int64) Register.DEBUG_VALUE.I64;
            else
                return Debugger.ReadMemAs_Int64( Address );
        }

        public byte ReadAs_byte()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
                return (byte) Register.DEBUG_VALUE.I8;
            else
                return Debugger.ReadMemAs_byte( Address );
        }

        public ushort ReadAs_ushort()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
                return (ushort) Register.DEBUG_VALUE.I16;
            else
                return Debugger.ReadMemAs_ushort( Address );
        }

        public UInt32 ReadAs_UInt32()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
                return (UInt32) Register.DEBUG_VALUE.I32;
            else
                return Debugger.ReadMemAs_UInt32( Address );
        }

        public UInt64 ReadAs_UInt64()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
                return (UInt64) Register.DEBUG_VALUE.I64;
            else
                return Debugger.ReadMemAs_UInt64( Address );
        }

        public float ReadAs_float()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
                return Register.DEBUG_VALUE.F32;
            else
                return Debugger.ReadMemAs_float( Address );
        }

        public double ReadAs_double()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
                return Register.DEBUG_VALUE.F64;
            else
                return Debugger.ReadMemAs_double( Address );
        }

        public bool ReadAs_CPlusPlusBool()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
                return 0 != Register.DEBUG_VALUE.I8;
            else
                return Debugger.ReadMemAs_CPlusPlusBool( Address );
        }

        public string ReadAs_UnicodeString( uint cb )
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                Util.Fail( "A string value in a register? Seems... unlikely." );
                throw new InvalidOperationException( "There isn't really a string in a register, is there?" );
            }
            else
            {
                return Debugger.ReadMemAs_UnicodeString( Address, cb );
            }
        }

        public string ReadAs_pUnicodeString( uint cb )
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                return Debugger.ReadMemAs_pUnicodeString( Register.ValueAsPointer, cb );
            }
            else
            {
                return Debugger.ReadMemAs_pUnicodeString( Address, cb );
            }
        }

        public string ReadAs_AsciiString( uint cb )
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                Util.Fail( "A string value in a register? Seems... unlikely." );
                throw new InvalidOperationException( "There isn't really a string in a register, is there?" );
            }
            else
            {
                return Debugger.ReadMemAs_AsciiString( Address, cb );
            }
        }

        public string ReadAs_pAsciiString( uint cb )
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                return Debugger.ReadMemAs_pAsciiString( Register.ValueAsPointer, cb );
            }
            else
            {
                return Debugger.ReadMemAs_pAsciiString( Address, cb );
            }
        }

        public string ReadAs_Utf8String( uint cb )
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                Util.Fail( "A string value in a register? Seems... unlikely." );
                throw new InvalidOperationException( "There isn't really a string in a register, is there?" );
            }
            else
            {
                return Debugger.ReadMemAs_Utf8String( Address, cb );
            }
        }

        public string ReadAs_pUtf8String( uint cb )
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                return Debugger.ReadMemAs_pUtf8String( Register.ValueAsPointer, cb );
            }
            else
            {
                return Debugger.ReadMemAs_pUtf8String( Address, cb );
            }
        }


        public string ReadAs_utf8szString()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                Util.Fail( "A string value in a register? Seems... unlikely." );
                throw new InvalidOperationException( "There isn't really a string in a register, is there?" );
            }
            else
            {
                return Debugger.ReadMemAs_utf8szString( Address );
            }
        }

        public string ReadAs_pUtf8szString()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                return Debugger.ReadMemAs_putf8szString( Register.ValueAsPointer );
            }
            else
            {
                return Debugger.ReadMemAs_putf8szString( Address );
            }
        }

        public string ReadAs_utf8szString( uint maxCch )
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                Util.Fail( "A string value in a register? Seems... unlikely." );
                throw new InvalidOperationException( "There isn't really a string in a register, is there?" );
            }
            else
            {
                return Debugger.ReadMemAs_utf8szString( Address, maxCch );
            }
        }

        public string ReadAs_pUtf8szString( uint maxCch )
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                return Debugger.ReadMemAs_putf8szString( Register.ValueAsPointer, maxCch );
            }
            else
            {
                return Debugger.ReadMemAs_putf8szString( Address, maxCch );
            }
        }


        public string ReadAs_wszString()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                Util.Fail( "A string value in a register? Seems... unlikely." );
                throw new InvalidOperationException( "There isn't really a string in a register, is there?" );
            }
            else
            {
                return Debugger.ReadMemAs_wszString( Address );
            }
        }

        public string ReadAs_wszString( uint maxCch )
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                Util.Fail( "A string value in a register? Seems... unlikely." );
                throw new InvalidOperationException( "There isn't really a string in a register, is there?" );
            }
            else
            {
                return Debugger.ReadMemAs_wszString( Address, maxCch );
            }
        }

        public string ReadAs_pwszString()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                return Debugger.ReadMemAs_wszString( Register.ValueAsPointer );
            }
            else
            {
                return Debugger.ReadMemAs_pwszString( Address );
            }
        }

        public string ReadAs_pwszString( uint maxCch )
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                return Debugger.ReadMemAs_wszString( Register.ValueAsPointer, maxCch );
            }
            else
            {
                return Debugger.ReadMemAs_pwszString( Address, maxCch );
            }
        }

        public string ReadAs_szString()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                Util.Fail( "A string value in a register? Seems... unlikely." );
                throw new InvalidOperationException( "There isn't really a string in a register, is there?" );
            }
            else
            {
                return Debugger.ReadMemAs_szString( Address );
            }
        }

        public string ReadAs_szString( uint maxCch )
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                Util.Fail( "A string value in a register? Seems... unlikely." );
                throw new InvalidOperationException( "There isn't really a string in a register, is there?" );
            }
            else
            {
                return Debugger.ReadMemAs_szString( Address, maxCch );
            }
        }

        public string ReadAs_pszString()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                return Debugger.ReadMemAs_szString( Register.ValueAsPointer );
            }
            else
            {
                return Debugger.ReadMemAs_pszString( Address );
            }
        }

        public string ReadAs_pszString( uint maxCch )
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                return Debugger.ReadMemAs_szString( Register.ValueAsPointer, maxCch );
            }
            else
            {
                return Debugger.ReadMemAs_pszString( Address, maxCch );
            }
        }

        /// <summary>
        ///    Reads the value as a pointer. N.B. Automatically undoes the adjustment
        ///    indicated by PointerAdjustment.
        /// </summary>
        public ulong ReadAs_pointer()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
                return Register.ValueAsPointer - PointerAdjustment;
            else
                return Debugger.ReadMemAs_pointer( Address ) - PointerAdjustment;
        }

        public bool TryReadAs_pointer( out ulong addr )
        {
            if( !IsPointer )
                throw new InvalidOperationException( "It's not a pointer." );

            if( IsValueInRegister )
            {
                addr = Register.ValueAsPointer;
                return true;
            }
            else
            {
                return Debugger.TryReadMemAs_pointer( Address, out addr );
            }
        }

        public Guid ReadAs_Guid()
        {
            _CheckValueAvailable();

            if( IsValueInRegister )
            {
                Util.Fail( "How did a GUID fit into a register?!" );
                throw new InvalidOperationException( "There isn't really a GUID in a register, is there?" );
                // TODO: Actually... I'm not sure if there really aren't ANY 16-byte registers on x64 (like maybe SSE or something?)
            }
            else
            {
                return Debugger.ReadMemAs_Guid( Address );
            }
        }

        public T[] ReadAs_TArray< T >( uint count )
        {
            if( IsValueInRegister )
            {
                Util.Fail( "Seems unlikely that there is an array in a register." ); // I guess it's possible, for a small array...
                throw new InvalidOperationException( "There isn't really an array in a register, is there?" );
            }
            else
            {
                return Debugger.ReadMemAs_TArray< T >( Address, count );
            }
        } // end ReadMemAs_TArray< T >()

        // This way looks weird, but it's much more convenient for PowerShell.
        public Array ReadAs_TArray( uint count, Type T )
        {
            if( IsValueInRegister )
            {
                Util.Fail( "Seems unlikely that there is an array in a register." ); // I guess it's possible, for a small array...
                throw new InvalidOperationException( "There isn't really an array in a register, is there?" );
            }
            else
            {
                return Debugger.ReadMemAs_TArray( Address, count, T );
            }
        } // end ReadMemAs_TArray()

        #endregion

        #region IEquatable< DbgSymbol > Stuff

        public bool Equals( DbgSymbol other )
        {
            if( null == other )
                return false;

            return Identity == other.Identity;
        } // end Equals()

        public override bool Equals( object obj )
        {
            return Equals( obj as DbgSymbol );
        }

        public override int GetHashCode()
        {
            return Identity.GetHashCode();
        }

        public static bool operator ==( DbgSymbol s1, DbgSymbol s2 )
        {
            if( null == (object) s1 )
                return (null == (object) s2);

            return s1.Equals( s2 );
        }

        public static bool operator !=( DbgSymbol s1, DbgSymbol s2 )
        {
            return !(s1 == s2);
        }

        #endregion
    } // end class DbgSymbol
}

