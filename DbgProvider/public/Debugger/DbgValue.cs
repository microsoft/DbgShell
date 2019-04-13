using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Runtime.Interop;
using DbgEngWrapper;

namespace MS.Dbg
{
    // We used to derive from PSObject, which was quite convenient. Unfortunately,
    // INTd12d3285 got in the way (wherein such objects, piped to Get-Member, cause a
    // hang).
    //
    // We implement ICloneable in order to support PSObject.Copy well. This allows people
    // to do things like <symbol>.GetStockValue().PSObject.Copy and then modify their copy
    // without messing up the symbol object's stock value.
    public abstract class DbgValue : ISupportColor, ICloneable
    {
        /// <summary>
        ///    The Symbol is the symbol that this value represents. The OperativeSymbol is
        ///    the actual symbol used to create the value (after taking Derived Type
        ///    Detection and Symbol Value Conversion into account), which might be
        ///    different--for instance, it might be a different type (perhaps the type of
        ///    Symbol is LPVOID, but the type of OperativeSymbol is LPREALTYPE).
        /// </summary>
        protected DbgSymbol Symbol
        {
            get
            {
                Util.Assert( m_symbolHistory.Count > 0 );
                return m_symbolHistory[ m_symbolHistory.Count - 1 ].OriginalSymbol;
            }
        }


        /// <summary>
        ///    The OperativeSymbol corresponds to the actual address and type used to
        ///    create the value, which could be different than Symbol, if Derived Type
        ///    Detection (DTD) or Symbol Value Conversion (SVC) came into play.
        ///
        ///    Note that DTD only comes into play for UDTs (DbgUdtValues).
        /// </summary>
        protected DbgSymbol OperativeSymbol
        {
            get
            {
                Util.Assert( m_symbolHistory.Count > 0 );
                return m_symbolHistory[ 0 ].OriginalSymbol;
            }
        }


        /// <summary>
        ///    Maintains the chain of symbols used to arrive at the current
        ///    OperativeSymbol (the one used to actually create this DbgValue).
        ///
        ///    For DbgValues where there was no Derived Type Detection (DTD) or Symbol
        ///    Value Conversion (SVC) applied, it will only have a single record in it.
        /// </summary>
        /// <remarks>
        ///    A note on reading symbol history: you read it backward. In other words, the
        ///    symbol at the very end of the list is the first one--the original symbol.
        ///    If there is more than one record, then the type of the record will indicate
        ///    what type of transformation (DTD or SVC) was done to it in order to arrive
        ///    at the next symbol in the chain.
        ///
        ///    So, for example, here is an annotated listing of a symbol history:
        ///
        ///         Type    SymbolName           SymbolType
        ///         ----    ----------           ----------
        ///        (final)  (*Subject)       (7) Fabrikam::Implementation::ConcreteClient
        ///      (6) DTD    (*Subject)       (5) Fabrikam::Object
        ///      (4) SVC    (*ptr_)          (3) Fabrikam::IExportSomeInterface$X__ExportAdapter
        ///      (2) DTD    (*ptr_)          (1) ISomeInterface
        ///
        ///    1) The very first symbol that we laid hands on was an ISomeInterface.
        ///    2) We applied Derived Type Detection (DTD) to it, to arrive at:
        ///    3) The ExportAdapter symbol.
        ///    4) We applied Symbol Value Conversion (SVC) to the ExportAdapter, to get:
        ///    5) An Fabrikam::Object symbol.
        ///    6) We applied DTD to the Fabrikam::Object symbol to get:
        ///    7) The final value (represented by the DbgValue that we got this symbol
        ///       history chain from), of type ConcreteClient.
        ///
        /// </remarks>
        private List< SymbolHistoryRecord > m_symbolHistory;


        // I'd like to make this return a read-only collection (a real, concrete one; not
        // just changing the return type to one that List<T> already implements, since PS
        // sees all) so that PS callers can't mess with it. But /we/ have places (in this
        // file) where we need to dynamically call DbgGetSymbolHistory and modify the
        // list.
        public List< SymbolHistoryRecord > DbgGetSymbolHistory()
        {
            return m_symbolHistory;
        }


        internal PSObject WrappingPSObject { get; private set; }


        private readonly ulong m_execStatusCookie;

        /// <summary>
        ///     The value of the debugger's ExecStatusCookie at the time the DbgValue
        ///     object was created.
        /// </summary>
        public ulong DbgGetExecStatusCookie() { return m_execStatusCookie; }


        protected DbgValue( DbgSymbol symbol )
            : this( new List< SymbolHistoryRecord >() { new SymbolHistoryRecord( symbol ) } )
        {
        }

        protected DbgValue( IList< SymbolHistoryRecord > symbolHistory )
        {
            if( null == symbolHistory )
                throw new ArgumentNullException( "symbolHistory" );

            if( 0 == symbolHistory.Count )
                throw new ArgumentException( "must have at least one symbol transform record", "symbolHistory" );

            // Note that we copy the list!
            m_symbolHistory = new List< SymbolHistoryRecord >( symbolHistory );

            m_execStatusCookie = Symbol.Debugger.ExecStatusCookie;

            WrappingPSObject = new PSObject( this );

            //
            // We don't add type names to DbgValueError or DbgUnavailableValue objects so
            // that custom formatters won't try to pick them up and access a bunch of
            // properties that they won't have: instead, we'll just see the error for what
            // it is.
            //
            if( !(this is DbgValueError) &&
                !(this is DbgUnavailableValue) )
            {
                _AddTypeNames( OperativeSymbol, WrappingPSObject );
            }
        } // end constructor


        public DbgSymbol DbgGetSymbol()
        {
            return Symbol;
        } // end DbgGetSymbol()


        /// <summary>
        ///    Returns the symbol used to create the value, as opposed to the symbol that
        ///    is declared. For instance, if the debugger was able to detect that an
        ///    object declared as a base type is actually an instance of a derived type,
        ///    returns a symbol representing the actual object that was detected; else
        ///    returns the original symbol.
        /// </summary>
        public DbgSymbol DbgGetOperativeSymbol()
        {
            // I was tempted to make the Symbol property virtual and overload it in this
            // class to return the symbol for the derived type, so that DbgGetSymbol()
            // would alway return the "real" symbol for the value that you called it on
            // (and I would add some sort of DbgGetOriginalSymbol() method to get the
            // original symbol, for the declared type).
            //
            // However, that would lead to some weird non-transitive behavior: say you
            // have a $symbol (that you got from "dv" or something). You could do "$val =
            // $symbol.Value" to get the value for the symbol. But if Derived Type
            // Detection found a derived type (and generated a new symbol for it), then
            // calling "$val.DbgGetSymbol()" would yield something different than $symbol.

            return OperativeSymbol;
        } // end DbgGetOperativeSymbol()


        // These never get skipped, because they are pretty basic and pretty bullet-proof.
        private static bool _TrySafeConversions( DbgSymbol symbol, out object val, out bool isBitfield )
        {
            val = null;
            isBitfield = false;

            Util.Assert( !symbol.IsValueUnavailable );

            if( symbol.Type is DbgBaseTypeInfo )
            {
                return ((DbgBaseTypeInfo) symbol.Type).TryConvertToPrimitive( symbol, out val, out isBitfield );
            }

            if( symbol.Type.Name == "_GUID" )
            {
                val = symbol.ReadAs_Guid();
                return true;
            }
            return false;
        } // end _TrySafeConversions()


        /// <summary>
        ///    Tries to find a registered Symbol Value Converter for the specifid symbol
        ///    and apply it.
        /// </summary>
        private static bool _TryUserSVC( DbgSymbol symbol,
                                         out object val,
                                         out string converterApplied )
        {
            val = null;
            converterApplied = null;
            var cdi = DbgValueConversionManager.ChooseConverterForSymbol( symbol );
            if( null != cdi )
            {
                // The first thing the converter will probably do is get the stock value
                // (and then use that to build a better value).
                //
                // Let's check that the stock value is well-formed (i.e. not a
                // DbgValueError, which you might get if you are looking at bad data), and
                // if it's not, we won't run the converter.
                //
                // A converter /conceivably/ could want to try its hand at conversion
                // anyway, even if the stock value is broken. If anybody ever asks for
                // that feature, we could do it by sticking an attribute on the converter
                // script block, and then we'd check it here.
                var stockVal = symbol.GetStockValue();
                if( stockVal.BaseObject is DbgValueError )
                {
                    LogManager.Trace( "Skipping conversion for symbol {0}, because the stock value is broken: {1}",
                                      symbol.Name,
                                      stockVal );
                    return false;
                }

                sm_userConversionDepth++;

                try
                {
                    val = cdi.Converter.Convert( symbol );
                    if( null != val )
                    {
                        converterApplied = cdi.TypeName.FullName;
                        LogManager.Trace( "Applied converter for symbol {0}: {1}",
                                          symbol.Name,
                                          converterApplied );
                    }
                }
                finally
                {
                    sm_userConversionDepth--;
                    if( 0 == sm_userConversionDepth )
                        sm_addrBreadcrumbs.Clear();
                }
            }
            else
            {
                //
                // Special case converter for things that look like smart pointers
                //

                if( symbol.Type.TemplateNode.IsTemplate )
                {
                    // Does the template name contain "pointer" or "ptr"?
                    //
                    // Looking for an English string, so ignoring culture here.
                    //
                    // Actually... sometimes the "ptr" is in a base class/template name
                    // (like somebody deriving from smartptr<T>). For instance, the
                    // Cn::FieldRef<T> derives from Cn::SmartPtrBase<T>. Do we really want
                    // to dig through all the base classes looking for "pointer"/"ptr", or
                    // should we just say if it is a template with a single pointer
                    // member, call it a smart pointer?

                //  string typeNameLower = symbol.Type.TemplateNode.TemplateName.ToLowerInvariant();
                //  if( typeNameLower.Contains( "pointer" ) || typeNameLower.Contains( "ptr" ) )
                    {
                        if( symbol.IsUdt )
                        {
                            DbgUdtTypeInfo udtti = (DbgUdtTypeInfo) symbol.Type;

                            //
                            // Does it have just one member, which is a pointer?
                            //
                            if( (udtti.Members.Count == 1) && (udtti.Members[ 0 ].DataType.SymTag == SymTag.PointerType) )
                            {
                                // There should only be one...
                                var pointerMember = symbol.Children.Where( ( x ) => x is DbgMemberSymbol ).First();

                                DbgPointerTypeInfo pti = (DbgPointerTypeInfo) pointerMember.Type;

                                // Last question: does the pointee type name appear in the
                                // name of the template?
                                //
                                // Note that we strip spaces here to deal with the fact
                                // that sometimes there are spaces in pointer types
                                // ("foo *"), and we also strip asterisks, because often
                                // the template just uses the type name.
                                string templateNameModified = symbol.Type.TemplateNode.FullName.Replace( " ", String.Empty ).Replace( "*", String.Empty );
                                string pointeeNameModified = pti.PointeeType.Name.Replace( " ", String.Empty ).Replace( "*", String.Empty );

                                if( templateNameModified.Contains( pointeeNameModified ) )
                                {
                                    val = pointerMember.Value;
                                    LogManager.Trace( "Smart pointer auto conversion for type: {0}",
                                                      symbol.Type.Name );
                                }
                            }
                        }
                    }
                }
            } // end smart pointer special case
            return val != null;
        } // end _TryUserSVC()


        private static readonly Dictionary< string, Func< DbgSymbol, object > > sm_knownTypeConverters
            = new Dictionary< string, Func< DbgSymbol, object > >()
            {
                { "wchar_t*",           (dsi) => dsi.ReadAs_pwszString() },
                { "WCHAR*",             (dsi) => dsi.ReadAs_pwszString() },
                { "char*",              (dsi) => dsi.ReadAs_pszString() },
            };

        private static bool _TrySymbolValueConversion( DbgSymbol symbol,
                                                       out object val,
                                                       out string converterApplied )
        {
            if( _TryUserSVC( symbol, out val, out converterApplied ) )
                return true;

            DbgArrayTypeInfo dati = symbol.Type as DbgArrayTypeInfo;
            if( null != dati )
            {
                // Most array types we'll just wrap in a DbgArrayValue (which has an indexer,
                // can be enumerated, etc.). But for arrays that represent something higher-level
                // (like strings), we'll try and interpret it that way.
                var elemType = dati.ArrayElementType;
                var elemBaseType = elemType as DbgBaseTypeInfo;
                if( null != elemBaseType )
                {
                    switch( elemBaseType.BaseType )
                    {
                        case BasicType.btChar:
                            if( 1 == elemBaseType.Size )
                            {
                                if( (dati.Count == 0) || (dati.Count == 1) )
                                {
                                    // Hope that it's part of a variable-sized structure,
                                    // with a NULL terminator!
                                    val = symbol.ReadAs_szString();
                                }
                                else
                                {
                                    // We /could/ read this as a "counted" string, but I
                                    // think there are lots of cases where a fixed-size
                                    // character array is used to hold a [usually smaller]
                                    // NULL-terminated string. So we'll use the
                                    // ReadAs_szString method instead.
                                    //val = symbol.ReadAs_Utf8String( dati.Count );
                                    val = symbol.ReadAs_szString( dati.Count );
                                }
                                return true;
                            }
                            else if( 2 == elemBaseType.Size )
                            {
                                if( (dati.Count == 0) || (dati.Count == 1) )
                                {
                                    // Hope that it's part of a variable-sized structure,
                                    // with a NULL terminator!
                                    val = symbol.ReadAs_wszString();
                                }
                                else
                                {
                                    // We /could/ read this as a "counted" string, but I
                                    // think there are lots of cases where a fixed-size
                                    // character array is used to hold a [usually smaller]
                                    // NULL-terminated string. So we'll use the
                                    // ReadAs_wszString method instead.
                                    //val = symbol.ReadAs_UnicodeString( dati.Count * 2 );
                                    val = symbol.ReadAs_wszString( dati.Count );
                                }
                                return true;
                            }
                            break;

                        // TODO: Is this a stretch? What happens if it's not really
                        // character data; how will the user get the proper data?
                     // case BasicType.btUInt:
                     // case BasicType.btULong:
                     //     if( 2 == elemBaseType.Size )
                     //     {
                     //         if( (dati.Count == 0) || (dati.Count == 1) )
                     //             val = symbol.ReadAs_wszString();
                     //         else
                     //             val = symbol.ReadAs_wszString( dati.Count );
                     //         return true;
                     //     }
                     //     break;
                        case BasicType.btWChar:
                            if( 2 == elemBaseType.Size )
                            {
                                if( (dati.Count == 0) || (dati.Count == 1) )
                                {
                                    // Hope that it's part of a variable-sized structure,
                                    // with a NULL terminator!
                                    val = symbol.ReadAs_wszString();
                                }
                                else
                                {
                                    // We /could/ read this as a "counted" string, but I
                                    // think there are lots of cases where a fixed-size
                                    // character array is used to hold a [usually smaller]
                                    // NULL-terminated string. So we'll use the
                                    // ReadAs_wszString method instead.
                                    //val = symbol.ReadAs_UnicodeString( dati.Count * 2 );
                                    val = symbol.ReadAs_wszString( dati.Count );
                                }
                                return true;
                            }
                            break;
                    } // end switch( base type )
                }
            } // end if( it's an array )

            Func< DbgSymbol, object > knownTypeConverter;
            if( !sm_knownTypeConverters.TryGetValue( symbol.Type.Name, out knownTypeConverter ) )
                return false;

            val = knownTypeConverter( symbol );
            Util.Assert( null != val );
            return true;
        } // end _TrySymbolValueConversion()


        internal static PSObject CreateDbgValue( DbgSymbol symbol )
        {
            return CreateDbgValue( symbol, false );
        }

        private static PSObject _CreatePointerValue( DbgSymbol sym )
        {
            if( 0 == Util.Strcmp_OI( "Void*", sym.Type.Name ) )
                return new DbgVoidStarValue( sym ).WrappingPSObject;
            else if( SymTag.FunctionType == sym.Type.SymTag )
                return new DbgFunctionPointerValue( sym ).WrappingPSObject;
            else if( 0 == sym.ReadAs_pointer() )
                return new DbgNullValue( sym ).WrappingPSObject;
            else if( SymTag.Null == sym.Type.SymTag ) // for DbgGeneratedTypeInfo
                return new DbgVoidStarValue( sym ).WrappingPSObject;
            else
                return new DbgPointerValue( sym ).WrappingPSObject;
        } // end _CreatePointerValue()


        [ThreadStatic]
        private static int sm_skipConversionCount;

        internal static PSObject CreateDbgValue( DbgSymbol symbol, bool skipConversion )
        {
            return CreateDbgValue( symbol, skipConversion, false );
        }

        internal static PSObject CreateDbgValue( DbgSymbol symbol,
                                                 bool skipConversion,
                                                 bool skipDerivedTypeDetection )
        {
            int unused;
            PSObject pso = _CreateDbgValueWorker( symbol,
                                                  skipConversion,
                                                  skipDerivedTypeDetection,
                                                  false, // followPointers
                                                  out unused );

            try
            {
                if( !symbol.IsValueUnavailable && symbol.IsPointer )
                {
                    // Convenience: If it's a pointer, add all the properties of the
                    // referenced value, so that you don't have to manually dereference.
                    DbgPointerValue dpv = pso.BaseObject as DbgPointerValue;
                    if( null != dpv )
                    {
                        PSObject pointee = dpv.DbgFollowPointers( skipConversion, skipDerivedTypeDetection );
                        foreach( var prop in pointee.Properties )
                        {
                            if( prop.GetType().FullName.StartsWith( "MS.Dbg" ) )
                            {
                                pso.Properties.Add( prop );
                            }
                            else
                            {
                                // TODO: Wait... maybe I need to try PSPropertyInfo.Copy?

                                // I don't understand why System.Management.Automation.PSPropertyInfo can't
                                // be added to the Properties collection directly... but this seems
                                // to work as a workaround. TODO: ask if it's a bug?
                                // This is the error we get if we try to add it directly:
                                //
                                //    System.Management.Automation.MethodInvocationException: Exception calling "get_Value" with "0" argument(s): "A PSProperty or PSMethod object cannot be added to this collection." ---> System.Management.Automation.ExtendedTypeSystemException: A PSProperty or PSMethod object cannot be added to this collection.
                                //       at System.Management.Automation.PSMemberInfoIntegratingCollection`1.Add(T member, Boolean preValidated)
                                //       at System.Management.Automation.PSMemberInfoIntegratingCollection`1.Add(T member)
                                //       at MS.Dbg.DbgValue.CreateDbgValue(DbgSymbol symbol, Boolean skipConversion) in c:\Users\danthom\Documents\Visual Studio 2012\Projects\DbgShell\DbgProvider\public\Debugger\DbgValue.cs:line 240
                                //       at MS.Dbg.DbgValue.CreateDbgValue(DbgSymbol symbol) in c:\Users\danthom\Documents\Visual Studio 2012\Projects\DbgShell\DbgProvider\public\Debugger\DbgValue.cs:line 158
                                //       at MS.Dbg.DbgSymbol._GetValue() in c:\Users\danthom\Documents\Visual Studio 2012\Projects\DbgShell\DbgProvider\public\Debugger\DbgSymbol.cs:line 226
                                //       at MS.Dbg.DbgSymbol.get_Value() in c:\Users\danthom\Documents\Visual Studio 2012\Projects\DbgShell\DbgProvider\public\Debugger\DbgSymbol.cs:line 217
                                //       at CallSite.Target(Closure , CallSite , Object )
                                //       --- End of inner exception stack trace ---
                                //       at System.Management.Automation.ExceptionHandlingOps.ConvertToMethodInvocationException(Exception exception, Type typeToThrow, String methodName, Int32 numArgs, MemberInfo memberInfo)
                                //       at CallSite.Target(Closure , CallSite , Object )
                                //       at System.Management.Automation.Interpreter.DynamicInstruction`2.Run(InterpretedFrame frame)
                                //       at System.Management.Automation.Interpreter.EnterTryCatchFinallyInstruction.Run(InterpretedFrame frame)
                                pso.Properties.Add( new MS.Dbg.PSLazyPropertyInfo( prop.Name,
                                                                                   () => { return prop.Value; },
                                                                                   prop.TypeNameOfValue ) );
                            }
                        } // end foreach( prop )
                    } // end if( it's a pointer value )
                } // end if( symbol.IsPointer )
            }
            catch( DbgEngException dee )
            {
                LogManager.Trace( "Ignoring error from following pointer: {0}", Util.GetExceptionMessages( dee ) );
            }
            return pso;
        } // end CreateDbgValue( symbol )


        // This stuff is to help us prevent an infinite loop during DbgValue creation.
        //
        // During user value conversion, we will be running "arbitrary", user-supplied
        // code/script (script converter stuff). The reason that code exists is to deal
        // with complex structures, so it's not unlikely that the code could blow up in
        // some way, especially when dealing with bad data. One way that it could go wrong
        // is to get stuck in an infinite loop (for example, if a std::forward_list
        // converter got pointed at an address that contained itself, it would get stuck
        // in a loop following _Next pointers (blah._Next._Next._Next._Next._Next etc.).
        //
        // To deal with this, when user conversion is running, we'll start keeping track
        // of all the addresses that we see go by. It can be legitimate to see the same
        // address go by more than once (for instance, retrieving the stock value, then
        // interpreting it as something else, etc.), so we'll keep a count for each
        // address, and only if the count goes over some threshold, we'll call foul and
        // halt the process by throwing.
        private static Dictionary< ulong, int > sm_addrBreadcrumbs = new Dictionary< ulong, int >();
        private static int sm_userConversionDepth;
        private const int c_MaxAddrCount = 20;

        private static void _CheckForRunawayUserConversion( DbgSymbol symbol )
        {
            // We should only need to do this during user value conversion, which runs
            // arbitrary code which could get stuck in a loop.
            if( 0 == sm_userConversionDepth )
                return;

            if( symbol.IsValueUnavailable || (symbol.Address == 0) ) // might it be in a register or something?
                return;

            int count;
            if( !sm_addrBreadcrumbs.TryGetValue( symbol.Address, out count ) )
                count = 1;
            else
                count = count + 1;

            sm_addrBreadcrumbs[ symbol.Address ] = count;

            if( count > c_MaxAddrCount )
            {
                throw new DbgProviderException( Util.Sprintf( "DbgValue creation: potentially infinite loop detected. We've seen this address too many times before ({0}), so we will terminate DbgValue conversion to prevent hanging (Address {1}, for symbol {2}).",
                                                              c_MaxAddrCount,
                                                              DbgProvider.FormatUInt64( symbol.Address, true ),
                                                              symbol.PathString ),
                                                "DbgValueCreation_infiniteLoopThwarted",
                                                ErrorCategory.OperationStopped,
                                                symbol );
            }
        } // end _CheckForRunawayUserConversion()


        /// <summary>
        ///    If you have a variable of type "BaseClass", but at runtime you assign an
        ///    object of type "DerivedClass", you'd probably be interested in seeing all
        ///    the members of DerivedClass when you looked at the variable in the
        ///    debugger.
        ///
        ///    Well, it's not possible to do that for all cases, but we can try, and
        ///    hopefully be able to help out at least some of the time.
        ///
        ///    Q: Which direction does "offset" go?
        ///    A: Base* + offset = Derived*
        /// </summary>
        private static bool _TryDetectDerivedType( DbgSymbol symbol,
                                                   out DbgUdtTypeInfo derivedType,
                                                   out int offset )
        {
            offset = 0;
            derivedType = null;
            var declaredType = (DbgUdtTypeInfo) symbol.Type;
            DbgUdtTypeInfo.VTableSearchResult vsr = null;
            int vtableOffset;

            vsr = declaredType.FindVTables().FirstOrDefault();

            if( null != vsr )
            {
                vtableOffset = vsr.TotalOffset;
            }
            else
            {
                // Special case:
                //
                // Sometimes you see something like an IUnknown looking like this:
                //
                //      iertutil!IUnknown (size 0x4)
                //         +0x000 lpVtbl           : IUnknownVtbl*
                //
                // Instead of a proper VTable, it has a "manual" vtable pointer.
                //
                // This can happen when people define the "CINTERFACE" macro, in order to
                // consume the C-style interface definitions generated by midl.exe
                //
                // (for an example, try taking a look at an iexplore.exe process, symbol
                // iertutil!g_punkProcessExplorer)
                if( _LooksLikeManualVTable( declaredType ) )
                    vtableOffset = 0;
                else
                    return false;
            }

            // There could be more than one vtable, but I think we should only need one.

            // The way we try to detect a derived type is to look at the vtable. The
            // vtable will have a symbolic name, and if it is a derived class, it will
            // have the name of the derived class in it.
            //
            // I would prefer to not parse a string to get a type name and then do a
            // lookup by type name, but I have found no better way. One alternative I
            // investigated was to look at the actual slots in the vtable. We can get
            // strong type information for them (DbgFunctionTypeInfo), which will include
            // the owning type (the derived class, if they belong to a derived class).
            //
            // Unfortunately, that approach doesn't always work, because on CHK builds,
            // each slot in the vtable points to an unconditional "jmp" instruction, which
            // doesn't belong to the target function (but it jumps to the target
            // function). This lone "jmp" has an ugly symbolic name and no type
            // information.
            //
            // I could disassemble that jmp instruction and parse the target
            // address out of it in order to find the "real" function, but I think parsing
            // the type name out of the vtable symbol is not as icky as that.

            Util.Assert( !symbol.IsValueInRegister );
            Util.Assert( 0 != symbol.Address );
            var dbgr = symbol.Debugger;

            ulong vtableAddr = (ulong) (((long) symbol.Address) + vtableOffset);
            ulong firstSlotPtr;
            string implementingTypeName = dbgr._TryGetImplementingTypeNameFromVTableAddr( vtableAddr,
                                                                                          out firstSlotPtr );

            if( null == implementingTypeName )
                return false;

            if( 0 == Util.Strcmp_OI( declaredType.Module.Name + "!" + declaredType.Name, implementingTypeName ) )
            {
                // It's the same type... let's not bother going through the type lookup.
                return false;
            }

            derivedType = dbgr._TryGetUdtForTypeNameExact( implementingTypeName );
            if( null == derivedType )
            {
                LogManager.Trace( "Unable to get type info for derived type: {0}",
                                  implementingTypeName );
                return false;
            }

            // _GetUdtForTypeName might have needed to "massage" the implementingTypeName
            // in order to find the type (such as by stripping "__ptr64" junk). So let's
            // re-check to see if we are actually talking about the same type.
            //
            // Note that if we cross a module boundary, ("foo!bar" versus "zip!bar"), we
            // /don't/ want to bail early--it might be just a stub/interface type in one
            // DLL, but we want to get to the full, actual type--so we compare the module-
            // qualified names here.

            if( 0 == Util.Strcmp_OI( declaredType.FullyQualifiedName,
                                     derivedType.FullyQualifiedName ) )
            {
                // It's the same type... let's not bother going through the type lookup.
                return false;
            }

            LogManager.Trace( "_TryDetectDerivedType: declaredType: {0}, derivedType: {1}",
                              declaredType.Name,
                              derivedType.Name );

            // We now know the declared type T1 (m_udtType) and the actual implmenting
            // type T2 (derivedType). We need to find the spatial relationship (in the
            // address space) between the two so that we can properly interpret the
            // memory. To do this, We search T2's BaseClasses until we find T1, keeping
            // track of offsets along the way. Note that in some cases, there could be
            // more than one possible relationship.
            //
            //                         T1 (declared/base)
            //                          \
            //                           \
            //                            \
            //                             T2 (impl/derived) (at offset X or something)
            //
            // (Also note that this may fail... one reason being that we could just be
            // looking at garbage that happened to point to some object, which is actually
            // not related at all.)
            //

            return PluginManager.TryFindPossibleOffsetFromDerivedClass( dbgr,
                                                                        vtableAddr,
                                                                        firstSlotPtr,
                                                                        derivedType,
                                                                        declaredType,
                                                                        out offset );
        } // end _TryDetectDerivedType()


        // An interface type that uses a "manual VTable" is a struct with a single
        // member--a pointer to an array of functions, named "lpISomethingVtbl". A common
        // way to get this is by using the "C style" interface definitions generated by
        // midl.exe (which produces both C++ and C definitions, in different #ifdef
        // blocks).
        private static bool _LooksLikeManualVTable( DbgUdtTypeInfo ti )
        {
            if( !ti.IsPointerSized() )
                return false;

            if( 1 != ti.Members.Count )
                return false;

            DbgDataMemberTypeInfo dmti = ti.Members[ 0 ] as DbgDataMemberTypeInfo;
            if( null == dmti )
                return false;

            DbgPointerTypeInfo pti = dmti.DataType as DbgPointerTypeInfo;
            if( null == pti )
                return false;

            if( !(pti.PointeeType is DbgUdtTypeInfo) )
                return false;

            if( dmti.Name.EndsWith( "Vtbl", StringComparison.OrdinalIgnoreCase ) )
                return true;
            // I could also check that the pointee type points to a bunch of functions.
            // But this is probably sufficient.

            return false;
        } // end _LooksLikeManualVTable()


        protected static PSObject _CreateDbgValueWorker( DbgSymbol symbol,
                                                         bool skipConversion,
                                                         bool skipDerivedTypeDetection,
                                                         bool followPointers,
                                                         out int numDerefs )
        {
            DbgSymbol valSymbol = symbol;
            numDerefs = 0;

            bool incrementedSkipVersion;
            if( skipConversion )
            {
                sm_skipConversionCount++;
                incrementedSkipVersion = true;
            }
            else
                incrementedSkipVersion = false;

            skipConversion = sm_skipConversionCount > 0;

            // Note that this is outside the try/catch, so that it if it throws, it will
            // bubble up and stop a user conversion script.
            _CheckForRunawayUserConversion( symbol );
            try
            {
                object val;

                DtdRecord dtdRecord = null;

                PSObject finalVal = null;

                // *sigh* I feel like this could be rewritten more elegantly if I had time
                // to detangle it.
                do
                {
                    if( valSymbol.IsValueUnavailable )
                    {
                        finalVal = new DbgUnavailableValue( valSymbol ).WrappingPSObject;
                        break;
                    }

                    // We need to try conversions before creating a DbgPointerValue
                    // (because some conversions may handle pointer values, like WCHAR*),
                    // but NOT if the pointer is NULL, so we handle NULL even before that.
                    if( valSymbol.IsPointer && (0 == valSymbol.ReadAs_pointer()) )
                    {
                        finalVal = _CreatePointerValue( valSymbol );
                        break;
                    }

                    //
                    // We shouldn't be able to make it around the loop after DTD has kicked in...
                    // TODO: unless SVC returns a pointer, and we decide to follow it?
                    //
                    Util.Assert( null == dtdRecord );

                    if( !skipDerivedTypeDetection && valSymbol.IsUdt )
                    {
                        var udtType = (DbgUdtTypeInfo) valSymbol.Type;
                        DbgUdtTypeInfo derivedType;
                        int offsetFromDerived = 0;

                        if( _TryDetectDerivedType( valSymbol, out derivedType, out offsetFromDerived ) )
                        {
                            var originalSym = valSymbol;
                            valSymbol = new DbgSimpleSymbol( valSymbol.Debugger,
                                                             valSymbol.Name,
                                                             derivedType,
                                                             (ulong) (((long) valSymbol.Address) + offsetFromDerived) );

                            dtdRecord = new DtdRecord( originalSym );
                        } // end if( we detected that the object was of a derived type )
                    } // end if( should attempt DTD )


                    bool isBitfield;
                    if( _TrySafeConversions( valSymbol, out val, out isBitfield ) )
                    {
                        // DTD should not ever apply to things that "safe conversions"
                        // apply to, so we don't need to worry about that here.
                        Util.Assert( null == dtdRecord );
                        finalVal = _CreateWrapperPso( valSymbol, val, isBitfield );
                        break;
                    }

                    string converterApplied = null;
                    if( !skipConversion && _TrySymbolValueConversion( valSymbol,
                                                                      out val,
                                                                      out converterApplied ) )
                    {
                        var pso = _CreateWrapperPso( valSymbol,
                                                     val,
                                                     isBitfield );

                        // The conversion could have returned anything (besides null)...

                        var svcRecord = new SvcRecord( converterApplied, valSymbol );

                        DbgValue dv = val as DbgValue;
                        if( null == dv )
                        {
                            dv = pso.BaseObject as DbgValue;
                        }

                        List< SymbolHistoryRecord > symbolHistory;

                        if( null != dv )
                        {
                            symbolHistory = dv.m_symbolHistory;
                        }
                        else
                        {
                            // *MUST* it be a PSO here? We can't get, say, a raw [boxed] int?

                            symbolHistory = ((dynamic) pso).DbgGetSymbolHistory();
                        }

                        LogManager.Trace( "Adding SVC record to transform history on a DbgValue." );
                        symbolHistory.Add( svcRecord );

                        if( null != dtdRecord )
                        {
                            LogManager.Trace( "Also adding dtdRecord to converted DbgValue." );
                            symbolHistory.Add( dtdRecord );
                        }

                        //
                        // Any other Derived Type Detection or Symbol Value Conversion
                        // should have been handled recursively... so we should be able to
                        // return here.
                        //
                        // TODO: what if conversion returned a pointer?? should we keep following it?
                        //

                        finalVal = pso;
                        break;
                    }

                    if( valSymbol.IsPointer )
                    {
                        // We can't follow some pointers (void*, function pointers).
                        if( followPointers && valSymbol.CanFollow )
                        {
                            Util.Assert( 1 == valSymbol.Children.Count );
                            valSymbol = valSymbol.Children[ 0 ]; // There should be one child: the pointee
                        }
                        else
                        {
                            // We should not have a dtdRecord here...
                            // TODO: unless SVC returned a pointer and we decided to keep chasing it?
                            Util.Assert( null == dtdRecord );
                            finalVal = _CreatePointerValue( valSymbol );
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }

                    numDerefs++;
                } while( followPointers );

                if( null == finalVal )
                    finalVal = DbgValue._CreateDbgValueTerminal( valSymbol, dtdRecord );

                // If we were following pointers, let's cache the value so that we don't
                // have to re-compute it again later.
                if( numDerefs > 0 )
                {
                    valSymbol.SetCachedValue( finalVal,
                                              skipConversion,
                                              skipDerivedTypeDetection );
                }

                return finalVal;
            }
            catch( DbgProviderException dpe )
            {
                return DbgValue._CreateDbgValueError( valSymbol, dpe );
            }
            finally
            {
                if( incrementedSkipVersion )
                    sm_skipConversionCount--;
            }
        } // end _CreateDbgValueWorker()


        // "Terminal": because there can be no following of pointers.
        private static PSObject _CreateDbgValueTerminal( DbgSymbol symbol,
                                                         SymbolHistoryRecord transformRecord )
        {
            Util.Assert( !symbol.IsValueUnavailable );

            DbgValue val = null;

            if( symbol.IsEnum )
            {
                var pso = _CreatePsoForEnum( symbol );
                if( null != transformRecord )
                {
                    Util.Fail( "how would DTD end up resulting in an enum??" ); // TODO: SVC, but we would have returned from a different code path and never ended up here in that case... this code is so horrible
                    ((dynamic) pso).DbgGetSymbolHistory().Add( transformRecord );
                }
                return pso;
            }
            else if( symbol.IsArray )
                val = new DbgArrayValue( symbol );
            else if( (symbol.Type is DbgNullTypeInfo) ||
                     ((symbol.Type is DbgBaseTypeInfo) && (((DbgBaseTypeInfo) symbol.Type).BaseType == BasicType.btNoType)) )
                val = new DbgNoValue( symbol );
            else
                val = new DbgUdtValue( symbol );

            if( null != transformRecord )
            {
                val.m_symbolHistory.Add( transformRecord );
            }

            return val.WrappingPSObject;
        } // end _CreateDbgValueTerminal()


        internal static PSObject CreateWrapperPso( DbgSymbol symbol, object wrapped )
        {
            if( null == wrapped )
                throw new ArgumentNullException( "wrapped" );

            return _CreateWrapperPso( symbol, wrapped, false );
        }


        private static PSObject _CreateWrapperPso( DbgSymbol symbol,
                                                   object wrapped,
                                                   bool isBitfield )
        {
            if( symbol.IsPointer )
            {
                // This assert may be overzealous. What if we had a pointer symbol, and it
                // was null, and in the process of "manual" conversion, we created
                // something besides a DbgNullValue? Perhaps not the best idea, but should
                // it be wrong? If so, we should throw, not assert. If not, we should
                // probably not assert.
                Util.Assert( 0 != symbol.ReadAs_pointer() || (wrapped is DbgNullValue) );
            }

            if( null == wrapped )
            {
                // You can't actually pass 'null' to the PSObject constructor; it will
                // throw.  So instead we have a special object type to represent a null
                // value.

                // Because we pre-screen for null pointers before doing conversions, and
                // verify that converters don't return null (if they do, it means that
                // conversion failed), we should never get here.
                Util.Fail( "Shouldn't call _CreateWrapperPso for a null." );
                return new DbgNullValue( symbol ).WrappingPSObject;
            }

            PSObject pso = wrapped as PSObject;
            if( null != pso )
            {
                if( null != pso.Methods[ "DbgGetSymbol" ] )
                {
                    // We'll make a copy to prevent accidental aliasing.
                    //
                    // What is the problem? Consider the converter for unique_ptr, which
                    // does this:
                    //
                    //    $stockValue = $_.GetStockValue()
                    //
                    //    # unique_ptr is just a resource management wrapper around a pointer. We can make
                    //    # things easier to see in the debugger by just hiding the unique_ptr layer (which
                    //    # has nothing in it anyway; just the pointer).
                    //    return $stockValue._Myptr
                    //
                    // The problem is that now any modifications or alterations to the
                    // converted value will be modifying that _Myptr value, but that
                    // _Myptr value is the one that belongs to the unique_ptr's "stock"
                    // value. If we didn't make a copy, that would make it hard to get to
                    // the stock value for _Myptr, because we would have messed up its
                    // stock value.
                    //
                    // We could just require converter authors to prevent aliasing by
                    // making copies themselves, but it's VERY easy to forget/overlook, so
                    // we'll go ahead and do it for them.
                    //
                    // (I'm not sure that that specific example is valid anymore, given
                    // how broadly I am changing how the DbgGetSymbol stuff works, but it
                    // still seems like a good idea.)

                    return pso.Copy();
                } // end( has DbgGetSymbol )
            } // end if( pso )
            else
            {
                pso = new PSObject( wrapped );
            }

            _AddDbgGetSymbolMethods( symbol, pso );

            _AddTypeNames( symbol, pso );

            if( isBitfield )
                pso.TypeNames.Insert( 0, "!<bitfield>" );

            if( symbol.IsPointer )
            {
                // Sometimes people want to deal with things as pointers, even if
                // there is a conversion.

                // For example, on win7 x86, _UNICODE_STRING.Buffer shows up as
                // "wchar_t*", so we automagically turn it into a string.  But on win8
                // x64, it shows up as an "unsigned short*". So the formatting
                // definition just uses the pointer value to handle all cases.
                Func< object > getPointer = () => { return symbol.ReadAs_pointer(); };
                Func< object > isNull = () => { return 0 == (ulong) getPointer(); };

                pso.Methods.Add( new PSDbgMethodInfo( "DbgGetPointer",
                                                      "System.UInt64",
                                                      getPointer ) );

                pso.Methods.Add( new PSDbgMethodInfo( "DbgIsNull",
                                                      "System.Boolean",
                                                      isNull ) );
            }
            return pso;
        } // end _CreateWrapperPso()


        private static PSObject _CreateDbgValueError( DbgSymbol symbol, Exception error )
        {
            return new DbgValueError( symbol, error ).WrappingPSObject;
        } // end _CreateDbgValueError()


        private static void _AddDbgGetSymbolMethods( DbgSymbol symbol,
                                                     PSObject pso )
        {

            if( null != pso.Methods[ "DbgGetSymbol" ] )
            {
                //Util.Fail( "so... were we hiding the existing one, then?" );
                LogManager.Trace( "oops; need to not hide existing methods..." );
                return;
            }

            var symbolHistory = new List< SymbolHistoryRecord >();

            symbolHistory.Add( new SymbolHistoryRecord( symbol ) );

            Func< object > del = () => { return symbolHistory; };
            var pmi = new PSDbgMethodInfo( "DbgGetSymbolHistory",
                                           "System.Collections.Generic.List`1", // what should this be??
                                           del );
            pso.Methods.Add( pmi );


            // TODO: I would really like to be able to put an OutputTypeAttribute on this
            // or something so that tab completion would work (like
            // "$blah.m_foo._bar.DbgGetSymbol().Ty[tab]").
            //
            // Using PSCodeMethod with the MethodInfo of this delegate does not work
            // because PSCodeMethod wants the MethodInfo to be public, static, and take a
            // single parameter of type PSOBject. If I create a helper class with a method
            // that has such a signature, that doesn't work either, because the PSObject
            // that it passes might be a string or an int or something else that isn't a
            // DbgValue (from when we wrap a "raw" or primitive type in a PSO), and there
            // is no way to get the symbol from that.

            del = () => { return symbolHistory[ symbolHistory.Count - 1 ].OriginalSymbol; };
            pmi = new PSDbgMethodInfo( "DbgGetSymbol",
                                       "MS.Dbg.DbgSymbol",
                                       del );
            pso.Methods.Add( pmi );


            // The case for DbgGetOperativeSymbol is thus:
            //
            // Say you have a converter for some sort of "wrapper" type, like unique_ptr.
            // Since the unique_ptr structure is completely uninteresting when debugging
            // (its benefit comes from its methods, for RAII), the point of the converter
            // is to just eliminate that boring stuff and return the _Myptr member in its
            // place.
            //
            // That's all well and good, but what if you ask for the symbol represented by
            // the [converted] value? (In other words, you have the converted value in
            // $val, so you want to call "$val.DbgGetSymbol().) You'll get back the symbol
            // for the unique_ptr structure. Which is the truth--we don't want to
            // completely hide stuff. But what if you want to see the type object of the
            // _Myptr field? You could do one of the following:
            //
            //    $val.DbgGetSymbol().Children[ 0 ].Type
            //    $val.DbgGetSymbol().GetStockValue()._Myptr.DbgGetSymbol().Type
            //
            // The first is unattractive, because it requires you know that the field you
            // are interested in will be the first child. For a unique_ptr, maybe you know
            // that, but you won't for everything. And it's not clear what is being
            // accessed.
            //
            // The second is unattractive because, wow, look how long that line is. And
            // you won't get any tab completion after the first pair of parens. So
            // instead, we'd like to be able to do this:
            //
            //    $val.DbgGetOperativeSymbol()
            //

            del = () => { return symbolHistory[ 0 ].OriginalSymbol; };
            pmi = new PSDbgMethodInfo( "DbgGetOperativeSymbol",
                                       "MS.Dbg.DbgSymbol",
                                       del );
            pso.Methods.Add( pmi );


            ulong cookie = symbol.Debugger.ExecStatusCookie;
            del = () => { return cookie; };
            pmi = new PSDbgMethodInfo( "DbgGetExecStatusCookie",
                                       "System.UInt64",
                                       del );
            pso.Methods.Add( pmi );
        } // end _AddDbgGetSymbolMethods()


        // This allows formatting cmdlets to specially format values of certain types.
        private static void _AddTypeNames( DbgSymbol symbol, PSObject pso )
        {
            if( null == symbol.Type )
                return;

            // TODO: Terribly inefficient. Anything we can do about it?
            var typeNames = symbol.GetTypeNames();
            for( int i = 0; i < typeNames.Count; i++ )
            {
                pso.TypeNames.Insert( i, typeNames[ i ] );
            }
        } // end _AddTypeNames()


        private static PSObject _CreatePsoForEnum( DbgSymbol symbol )
        {
            EnumValueHelper evh = new EnumValueHelper( symbol );
            PSObject pso = new PSObject( evh.Value );

            _AddDbgGetSymbolMethods( symbol, pso );
            Util.Assert( !symbol.IsPointer );
            _AddTypeNames( symbol, pso );

            Func< object > del = () => { return evh.ToString(); };
            pso.Methods.Add( new PSDbgMethodInfo( "ToString",
                                                  "System.String",
                                                  del ) );

            del = () => { return evh.ToColorStringSimple(); };
            pso.Methods.Add( new PSDbgMethodInfo( "ToColorStringSimple",
                                                  "MS.Dbg.ColorString",
                                                  del ) );
            return pso;
        } // end _CreatePsoForEnum


        // It's important to have this, to prevent PS from expanding so much stuff.
        public override string ToString()
        {
            return ToColorString().ToString( DbgProvider.HostSupportsColor );
        }

        public abstract ColorString ToColorString();


        protected abstract object DoCloning();

        object ICloneable.Clone()
        {
            return DoCloning();
        }
    } // end class DbgValue


    public class DbgUdtValue : DbgValue
    {
        internal DbgUdtValue( DbgSymbol symbol )
            : base( symbol )
        {
            m_udtType = symbol.Type as DbgUdtTypeInfo;
            if( null == m_udtType )
                throw new ArgumentException( "The specified symbol does not represent a UDT.",
                                             "symbol" );

            // TODO: lazily?
            _InitFields();
        } // end constructor


        private DbgUdtTypeInfo m_udtType;

        // There can be multiple fields with the same name (but different offset), thanks
        // to what I call "type inlining", so we need to keep track of names we have
        // already used.
        private HashSet< string > m_memberNames;


        /// <summary>
        ///    Used for "manual symbol value conversion"; where a parent type wants to
        ///    replace the value of one of its members (for instance replacing a member
        ///    whose declared type is LPVOID with a LPREALTYPE).
        /// </summary>
        public void DbgReplaceMemberValue( string memberName, object newValue )
        {
            var prop = WrappingPSObject.Properties[ memberName ];
            if( (null == prop) || !(prop is PSSymFieldInfo) )
            {
                throw new ArgumentException( Util.Sprintf( "Invalid member: {0}", memberName ) );
            }
            var pssfi = prop as PSSymFieldInfo;

            // We want to avoid aliasing of the Symbol--we'll make a shallow copy of it.
            pssfi.Symbol = pssfi.Symbol.Copy();

            pssfi.Symbol.SetCachedValue( CreateWrapperPso( pssfi.Symbol, newValue ),
                                         skipConversion: false,
                                         skipDerivedTypeDetection: false );
        }


        private string _TryGetTypeName( DbgNamedTypeInfo ti )
        {
            try
            {
                return ti.Name;
            }
            catch( DbgEngException dee )
            {
                // We should always be able to get the type name. But just in case we
                // can't... we don't want to /completely/ derail symbol expansion.
                Util.Fail( Util.Sprintf( "Couldn't get type name for type ID {0}: {1}.", ti.TypeId, Util.FormatErrorCode( dee.HResult ) ) );
                return Util.Sprintf( "<err: {0}>", Util.GetExceptionMessages( dee ) );
            }
        }


        private bool _IsVtableRelated( DbgSymbol symbol )
        {
            if( symbol.Name.StartsWith( "__vtcast_", StringComparison.OrdinalIgnoreCase ) )
                return true;

            if( symbol.Name.StartsWith( "__vfptr", StringComparison.OrdinalIgnoreCase ) )
                return true;

            return false;
        } // end _IsVtableRelated()

        private int _IndexOfFirstNonVtableRelatedChild( DbgSymbol symbol )
        {
            int i;
            for( i = 0; i < symbol.Children.Count; i++ )
            {
                if( symbol.Children[ i ].Name.StartsWith( "__vtcast_", StringComparison.OrdinalIgnoreCase ) )
                    continue;

                if( symbol.Children[ i ].Name.StartsWith( "__vfptr", StringComparison.OrdinalIgnoreCase ) )
                    continue;

                break;
            }

            if( i >= symbol.Children.Count )
                return -1;
            else
                return i;
        } // end _IndexOfFirstNonVtableRelatedChild()


        private void _InitFields()
        {
            m_memberNames = new HashSet< string >();

            if( OperativeSymbol.IsValueUnavailable )
            {
                Util.Fail( "Shouldn't be able to construct a DbgUdtValue for an unavailable value." );
                return;
            }

            DbgSymbol sym = OperativeSymbol;

            foreach( var staticMember in m_udtType.StaticMembers )
            {
                string key = staticMember.Name;
                // There can be multiple fields with the same name (but different offset),
                // thanks to what I call "type inlining".
                int i = 0;
                while( m_memberNames.Contains( key ) )
                {
                    i++;
                    // I wish there was a way to do this without effectively
                    // renaming the field, but things like spaces and parentheses
                    // really mess things up, so oh well.
                    //key = Util.Sprintf( "{0}({1})", soughtField.Name, i );
                    key = Util.Sprintf( "{0}_{1}", staticMember.Name, i );
                }
                m_memberNames.Add( key );

                WrappingPSObject.Properties.Add( new PSSymFieldInfo( key,
                                                                     staticMember.GetSymbol(),
                                                                     staticMember ) );
            }

            foreach( var member in m_udtType.Members )
            {
                // TODO TODO: factor out this duplicated code
                string key = member.Name;
                // There can be multiple fields with the same name (but different offset),
                // thanks to what I call "type inlining".
                int i = 0;
                while( m_memberNames.Contains( key ) )
                {
                    i++;
                    // I wish there was a way to do this without effectively
                    // renaming the field, but things like spaces and parentheses
                    // really mess things up, so oh well.
                    //key = Util.Sprintf( "{0}({1})", soughtField.Name, i );
                    key = Util.Sprintf( "{0}_{1}", member.Name, i );
                }
                m_memberNames.Add( key );
                // TODO: get rid of useless symbol.children? or somehow reconcile with the
                // fact that we are creating new DbgSimpleSymbol objects here...
                // maybe an indexer, from datamemberinfobase to symbol?
                WrappingPSObject.Properties.Add( new PSSymFieldInfo( key,
                                                                     new DbgMemberSymbol( Symbol.Debugger,
                                                                                          sym, // because m_udtType may correspond to m_detectedDerivedSymbol
                                                                                          member ),
                                                                     member ) );
            }
        } // end _InitFields()


        public override ColorString ToColorString()
        {
            // If we're in here, we know we are not representing a primitive
            // type (else we would just be an object of that type, or rather, PS
            // would have called ToString on the instance of that type).

            // ConversionOverhaul TODO: seems like we should be using OperativeSymbol in
            // here... but then the DbgUdtValue custom formatting view (in debugger.psfmt)
            // will need to take that into account... that needs to be reworked to
            // properly deal with transform history, anyway.

            Util.Assert( !OperativeSymbol.IsValueUnavailable );

            ColorString cs = new ColorString();

            Util.Assert( !OperativeSymbol.IsPointer );
            Util.Assert( OperativeSymbol.IsUdt );

            if( OperativeSymbol.IsPointer )
            {
                // This code must be left over from some copy/paste or something...
                if( this != null )
                    throw new Exception( "Uh... this code path should not be valid; this should be a UDT." );

                if( OperativeSymbol.IsValueInRegister )
                {
                    cs.Append( "(" );
                    cs.Append( DbgProvider.ColorizeRegisterName( OperativeSymbol.Register.Name ) );
                    cs.Append( ") " );
                    cs.Append( DbgProvider.FormatAddress( OperativeSymbol.Register.ValueAsPointer,
                                                          OperativeSymbol.Debugger.TargetIs32Bit,
                                                          true ) );
                }
                else
                {
                    ulong val;
                    if( OperativeSymbol.Debugger.TryReadMemAs_pointer( OperativeSymbol.Address, out val ) )
                    {
                        cs.Append( DbgProvider.FormatAddress( val, OperativeSymbol.Debugger.TargetIs32Bit, true ) );
                    }
                    else
                    {
                        cs.AppendPushFg( ConsoleColor.Yellow ).Append( "(memory access error at " );
                        cs.Append( DbgProvider.FormatAddress( OperativeSymbol.Address, OperativeSymbol.Debugger.TargetIs32Bit, true ) );
                        cs.Append( ")" ).AppendPop();
                    }
                }
                cs.Append( " " );
            }
            else if( OperativeSymbol.IsUdt )
            {
                // We'll just print the type name, so nothing to do here.
                // TODO: Add a way for users to customize how certain types are displayed
                // TODO: Display it 'inline', since it is 'inline' to its parent?
            }
            else
            {
             // DbgRealSymbol drs = Symbol as DbgRealSymbol;
             // if( null != drs )
             //     cs.Append( Util.Sprintf( "...tbd ({0})... ", drs._DSE.Tag ) );
             // else
                    cs.Append( "...tbd (manual symbol)... " );
            }

            string typeName = OperativeSymbol.Type.Name;
            if( OperativeSymbol.IsPointer )
            {
                // We deref'ed it, so take off the '*'.
                Util.Assert( '*' == typeName[ typeName.Length - 1 ] && typeName.Length > 1 );
                typeName = typeName.Substring( 0, typeName.Length - 1 );
            }
            cs.Append( new ColorString( ConsoleColor.DarkYellow, typeName ) );

            if( OperativeSymbol.IsPointer )
            {
                // If it's a pointer, we displayed the value of the pointer, along with the type.
                // If the thing /pointed TO/ is also a pointer, let's display that, too.
                var sym = OperativeSymbol;
                ulong deref = sym.ReadAs_pointer();

                try
                {
                    if( (null != sym.Children) &&
                        (0 != sym.Children.Count) &&
                        //(0 == Util.Strcmp_OI( sym.Children[ 0 ].Name, "*" )) &&
                        sym.Children[ 0 ].IsPointer )
                    {
                        do
                        {
                            sym = sym.Children[ 0 ];
                            if( sym.IsValueUnavailable )
                                break;

                            Util.Assert( sym.IsPointer );
                            cs.Append( " -> " );
                            deref = sym.ReadAs_pointer();
                            cs.Append( DbgProvider.FormatAddress( deref, OperativeSymbol.Debugger.TargetIs32Bit, true ) );
                        }
                        while( sym.IsPointer &&
                               (0 != deref) &&
                               (null != sym.Children) &&
                               (sym.Children.Count > 0) &&
                               //(0 == Util.Strcmp_OI( sym.Children[ 0 ].Name, "*" )) &&
                               sym.Children[ 0 ].IsPointer );
                    }

                    if( (0 != deref) && sym.IsPointer && (null != sym.Type) )
                    {
                        // Probably something like a "void**" that we can't follow symbolically.
                        string t = sym.Type.Name;
                        while( (deref != 0) &&
                               (t.Length > 1) &&
                               ('*' == t[ t.Length - 1 ]) &&
                               ('*' == t[ t.Length - 2 ]) )
                        {
                            cs.Append( " -> " );
                            ulong tmpDeref;
                            if( !sym.Debugger.TryReadMemAs_pointer( deref, out tmpDeref ) )
                            {
                                // We don't need to say the address we failed to access,
                                // because we already did.
                                cs.AppendPushPopFg( ConsoleColor.Red, "<mem access error>" );
                                break;
                            }
                            deref = tmpDeref;
                            cs.Append( DbgProvider.FormatAddress( deref, OperativeSymbol.Debugger.TargetIs32Bit, true ) );

                            t = t.Substring( 0, t.Length - 1 );
                        } // end while( type name ends with '**' )
                    } // end if( following a void* (sans symbols) )
                }
                catch( DbgMemoryAccessException )
                {
                    cs.AppendPushPopFg( ConsoleColor.Red, "<mem access error>" );
                }
            } // end if( IsPointer )
            return cs;
        } // end ToColorString()


        // For cloning.
        private DbgUdtValue( DbgUdtValue other )
            : base( other.DbgGetSymbolHistory() )
        {
            //this.m_operativeSymbol = other.m_operativeSymbol;
            this.m_udtType = other.m_udtType;

            this.WrappingPSObject.TypeNames.Clear();
            foreach( var tn in other.WrappingPSObject.TypeNames )
            {
                this.WrappingPSObject.TypeNames.Add( tn );
            }
            _InitFields();
        }

        protected override object DoCloning()
        {
            return new DbgUdtValue( this );
        } // end DoCloning()
    } // end class DbgUdtValue


    internal class PSSymFieldInfo : PSLazyPropertyInfo
    {
        public PSSymFieldInfo( string propName, DbgSymbol symbol, DbgDataMemberTypeInfoBase fieldInfo )
            : base( propName, _ValidateSymAndGenerateGetter( propName, symbol ), _TryGetTypeName( symbol.Type ) )
        {
            Symbol = symbol;
            FieldInfo = fieldInfo;
        } // end constructor

        private static Func<object> _ValidateSymAndGenerateGetter( string propName, DbgSymbol symbol )
        {
            if( null == symbol )
                throw new ArgumentNullException( "symbol" );

            return () =>
            {
                // PowerShell will silently swallow exceptions that come from properties,
                // except a lucky few deemed "serious", and MethodExceptions. Their intent
                // was to not mess up Formatting+Output (F+O), but they did it at the
                // language level instead of suppressing the error in F+O. (nothing shows
                // up in $error, either) (I don't know why they let MethodExceptions
                // through... maybe F+O can handle those? dunno)
                //
                // For regular .NET properties on .NET objects, the workaround is to use
                // "method syntax" instead of "property syntax": $blah.get_Foo() instead
                // of $blah.Foo. However, for a custom property like this, there is no
                // get_Foo() method. Rather than adding one (and requiring people to use
                // it if they want to see exceptions), we'll wrap the getter and propagate
                // exceptions thrown therefrom in MethodExceptions.
                try
                {
                    return symbol.Value;
                }
                catch( Exception e )
                {
                    // TODO: we should have a custom exception (derived from
                    // MethodException). Our alternate formatting engine may want to
                    // specially recognize it. Also, we can store the target PSObject and
                    // the symbol on it.
                    throw new MethodException( Util.Sprintf( "Accessing {0} threw: {1}",
                                                             propName,
                                                             Util.GetExceptionMessages( e ) ),
                                               e );
                }
            };
        } // end _ValidateSymAndGenerateGetter()

        private static string _TryGetTypeName( DbgNamedTypeInfo ti )
        {
            try
            {
                return ti.Name;
            }
            catch( DbgEngException dee )
            {
                // We should always be able to get the type name. But just in case we
                // can't... we don't want to /completely/ derail symbol expansion.
                Util.Fail( Util.Sprintf( "Couldn't get type name for type ID {0}: {1}.", ti.TypeId, Util.FormatErrorCode( dee.HResult ) ) );
                return Util.Sprintf( "<err: {0}>", Util.GetExceptionMessages( dee ) );
            }
        }

        public override PSMemberInfo Copy()
        {
            // N.B. Symbol is aliased here...
            return new PSSymFieldInfo( Name, Symbol, FieldInfo );
        }

        public DbgSymbol Symbol { get; internal set; }

        public DbgDataMemberTypeInfoBase FieldInfo { get; private set; }

        // TODO: This works (overriding ToString lets me control what is shown
        // for the 'Definition' when members are displayed by Get-Member); use it.
      //public override string ToString()
      //{
      //    return "fooby doo";
      //}
    } // end class PSSymFieldInfo


    internal class PSDbgMethodInfo : PSMethodInfo
    {
        // TODO: Right now we don't support overloads.
        private Delegate m_delegate;
        private string m_valTypeName;

        public PSDbgMethodInfo( string methodName, string valTypeName, Delegate methodDelegate )
        {
            if( String.IsNullOrEmpty( methodName ) )
                throw new ArgumentException( "You must supply a method name.", "methodName" );

            if( String.IsNullOrEmpty( valTypeName ) )
                throw new ArgumentException( "You must supply a value type name.", "valTypeName" );

            if( null == methodDelegate )
                throw new ArgumentNullException( "methodDelegate" );

            m_delegate = methodDelegate;
            m_valTypeName = valTypeName;
            SetMemberName( methodName );
        } // end constructor


        public override object Invoke( params object[] arguments )
        {
            return m_delegate.DynamicInvoke( arguments );
        }

        public override Collection< string > OverloadDefinitions
        {
            get { return new Collection< string >() { Util.Sprintf( "{0} {1}()", TypeNameOfValue, Name ) }; }
        }

        public override PSMemberInfo Copy()
        {
            return new PSDbgMethodInfo( Name, m_valTypeName, m_delegate );
        }

        public override PSMemberTypes MemberType
        {
            get { return PSMemberTypes.CodeMethod; }
        }

        public override string TypeNameOfValue
        {
            get { return m_valTypeName; }
        }
    } // end class PSDbgMethodInfo


    public abstract class DbgPointerValueBase : DbgValue,
                                                IEquatable< DbgPointerValueBase >,
                                                IComparable< DbgPointerValueBase >,
                                                IComparable
    {
        internal DbgPointerValueBase( DbgSymbol symbol )
            : base( symbol )
        {
            Util.Assert( symbol.IsPointer );
            Util.Assert( !symbol.IsValueUnavailable );
        } // end constructor

        protected DbgPointerValueBase( List< SymbolHistoryRecord > symbolHistory )
            : base( symbolHistory )
        {
            Util.Assert( OperativeSymbol.IsPointer );
            Util.Assert( !OperativeSymbol.IsValueUnavailable );
        } // end constructor


        public static implicit operator ulong( DbgPointerValueBase dnv )
        {
            if( object.ReferenceEquals( dnv, null ) )
                return 0;

            return dnv.DbgGetPointer();
        }

        // TODO: need to convert from int, long as well, for PS cmdline
        // TODO: base classes also need their own converters, because PS only likes to compare like to like?
        public static explicit operator DbgPointerValueBase( ulong addr )
        {
            var dbg = DbgEngDebugger._GlobalDebugger;

            var mod = dbg.GetNtdllModuleEffective();

            //var ptrType = DbgPointerTypeInfo.GetPointerTypeInfo( dbg, mod, /* DNTYPE_VOID */ 0x80000000 );
            //var ptrType = DbgPointerTypeInfo.GetNamedTypeInfo( dbg, mod, /* DNTYPE_VOID */ 0x80000000, SymTag.BaseType );
            var ptrType = DbgPointerTypeInfo.GetFakeVoidStarType( dbg, mod );

            DEBUG_VALUE dv = new DEBUG_VALUE();
            dv.Type = DEBUG_VALUE_TYPE.INT64;
            dv.I64 = addr;
            var reg = new DbgPseudoRegisterInfo( dbg, "fakeyfakereg", dv, 0, 0, UInt32.MaxValue );

            var sym = new DbgSimpleSymbol( dbg,
                                           "explicit_op_DbgPointerValueBase_" + addr.ToString( "x" ),
                                           ptrType,
                                           reg );

            //return new DbgVoidStarValue( sym );
            return new DbgPointerValue( sym );
        }


        // This was supposed to make null checks in PowerShell really easy, by letting you
        // just type "if( $sym.Value.m_pBlah ) ..." (like in C). Unfortunately, PS does
        // not consider implicit conversion to bool. TODO: ask them to.
        public static implicit operator bool( DbgPointerValueBase dnv )
        {
            if( object.ReferenceEquals( dnv, null ) )
                return false;

            return 0 != dnv.DbgGetPointer();
        }

        // N.B. DbgPointerValues do not have any properties of their own, so that
        // we can add properties of the pointed-to value for convenience without
        // collisions or confusion over if the properties apply to the pointer or
        // the pointed-to value.

        // Is this too confusing? (to have Pointer and Pointee, which differ by only
        // the last letter?)
        public ulong DbgGetPointer() { return OperativeSymbol.ReadAs_pointer(); }

        public bool DbgIsNull() { return 0UL == DbgGetPointer(); }


        public PSObject DbgReinterpretPointeeType( DbgNamedTypeInfo newPointeeType )
        {
            DbgPointerTypeInfo pti = newPointeeType.GetPointerType();
            var newSym = OperativeSymbol.ChangeType( pti );
            return newSym.Value;
        }


        internal protected static bool _DisplaysOwnPointerVal( DbgSymbol sym )
        {
            return sym.IsPointer &&
                   (SymTag.FunctionType == sym.Type.SymTag);
        }

        public override ColorString ToColorString()
        {
            Util.Assert( !OperativeSymbol.IsValueUnavailable );
            return DbgProvider.FormatAddress( DbgGetPointer(), OperativeSymbol.Debugger.TargetIs32Bit, true );
        }

        public static bool operator==( DbgPointerValueBase p1, ulong p2 )
        {
            return ((ulong) p1) == p2;
        }

        public static bool operator!=( DbgPointerValueBase p1, ulong p2 )
        {
            return ((ulong) p1) != p2;
        }

        public static bool operator<( DbgPointerValueBase p1, ulong p2 )
        {
            return ((ulong) p1) < p2;
        }

        public static bool operator<=( DbgPointerValueBase p1, ulong p2 )
        {
            return ((ulong) p1) <= p2;
        }

        public static bool operator>( DbgPointerValueBase p1, ulong p2 )
        {
            return ((ulong) p1) > p2;
        }

        public static bool operator>=( DbgPointerValueBase p1, ulong p2 )
        {
            return ((ulong) p1) >= p2;
        }

        public bool Equals( DbgPointerValueBase other )
        {
            if( Object.ReferenceEquals( other, null ) )
                return base.Equals( other );

            return this == (ulong) other;
        }

        public override bool Equals( object obj )
        {
            if( !Object.ReferenceEquals( obj, null ) && (obj is DbgPointerValueBase) )
            {
                return Equals( (DbgPointerValueBase) obj );
            }
            if( obj is int )
            {
                return this == (ulong) (int) obj;
            }
            if( obj is ulong )
            {
                return this == (ulong) obj;
            }
            if( obj is long )
            {
                return this == (ulong) (long) obj;
            }
            return base.Equals( obj );
        }

        public override int GetHashCode()
        {
            return ((ulong) this).GetHashCode();
        }

        public int CompareTo( DbgPointerValueBase other )
        {
            if( null == other )
                return Int32.MaxValue;

            return DbgGetPointer().CompareTo( other.DbgGetPointer() );
        }

        public int CompareTo( ulong other )
        {
            return DbgGetPointer().CompareTo( other );
        }

        int IComparable.CompareTo( object obj )
        {
            if( null == obj )
                return Int32.MaxValue;

            if( obj is ulong asUlong )
            {
                return CompareTo( asUlong );
            }

            return CompareTo( (DbgPointerValueBase) obj );
        }
    } // end class DbgPointerValueBase


    public class DbgPointerValue : DbgPointerValueBase
    {
        public object DbgGetPointee()
        {
            // TODO: or should we let the .Value access throw?
            if( DbgIsNull() )
                throw new InvalidOperationException( "Cannot dereference a null pointer." );

            Util.Assert( 0 != OperativeSymbol.Children.Count );
            Util.Assert( 1 == OperativeSymbol.Children.Count );
            return OperativeSymbol.Children[ 0 ].Value;
        }

        public PSObject DbgFollowPointers()
        {
            return DbgFollowPointers( false );
        }

        public PSObject DbgFollowPointers( bool skipConversion )
        {
            return DbgFollowPointers( skipConversion, false );
        }

        public PSObject DbgFollowPointers( bool skipConversion, bool skipDerivedTypeDetection )
        {
            int unused;
            return DbgFollowPointers( skipConversion, skipDerivedTypeDetection, out unused );
        }

        // Key: skipConversion, skipDerivedTypeDetection
        // Value: ultimatePointee, numDerefs
        // TODO: wait... why did I think the number of derefs would be useful?
        private DbgValueCache< Tuple< PSObject, int > > m_ultimatePointeeCache;

        public PSObject DbgFollowPointers( bool skipConversion,
                                           bool skipDerivedTypeDetection,
                                           out int numDerefs )
        {
            if( null == m_ultimatePointeeCache )
            {
                m_ultimatePointeeCache = new DbgValueCache< Tuple< PSObject, int > >();
            }

            ValueOptions valOpts = DbgValueCache<int>.ComputeValueOptions( skipConversion,
                                                                           skipDerivedTypeDetection );

            ref Tuple< PSObject, int > cachedVal = ref m_ultimatePointeeCache[ valOpts ];

            if( null == cachedVal )
            {
                var obj = _DbgFollowPointers( skipConversion,
                                              skipDerivedTypeDetection,
                                              out numDerefs );

                cachedVal = Tuple.Create( obj, numDerefs );
            }

            numDerefs = cachedVal.Item2;
            return cachedVal.Item1;
        }


        /// <summary>
        ///    Clears the m_ultimatePointeeCache; handy for debugging.
        /// </summary>
        public void _DbgDumpPointeeCache()
        {
            m_ultimatePointeeCache.Clear();
        }


        private PSObject _DbgFollowPointers( bool skipConversion,
                                             bool skipDerivedTypeDetection,
                                             out int numDerefs )
        {
            return _CreateDbgValueWorker( OperativeSymbol,
                                          skipConversion,
                                          skipDerivedTypeDetection,
                                          true, // followPointers
                                          out numDerefs );
        } // end _DbgFollowPointers()


        private Dictionary< int, PSObject > m_arrayIndexCache;

        public PSObject this[ int idx ]
        {
            get
            {
                // Note that you can use negative indexes with pointers.

                if( null == m_arrayIndexCache )
                    m_arrayIndexCache = new Dictionary< int, PSObject >();

                PSObject val;
                if( !m_arrayIndexCache.TryGetValue( idx, out val ) )
                {
                    var sbElemName = new StringBuilder( OperativeSymbol.Name )
                                            .Append( "[" )
                                            .Append( idx.ToString() )
                                            .Append( "]" );

                    DbgNamedTypeInfo pointeeType = ((DbgPointerTypeInfo) OperativeSymbol.Type).PointeeType;
                    Util.Assert( 0 != pointeeType.Size );

                    ulong elemAddr = OperativeSymbol.ReadAs_pointer() + (uint) (idx * (int) pointeeType.Size);
                    var elemSym = new DbgSimpleSymbol( OperativeSymbol.Debugger,
                                                       sbElemName.ToString(),
                                                       pointeeType,
                                                       elemAddr );
                    val = DbgValue.CreateDbgValue( elemSym );
                    m_arrayIndexCache.Add( idx, val );
                }
                return val;
            }

            set
            {
                throw new NotImplementedException();
            }
        } // end indexer


        /// <summary>
        ///    Performs pointer addition (takes size of the pointed-to type into account).
        /// </summary>
        public static ulong operator+( DbgPointerValue basePointer, int i )
        {
            if( null == basePointer )
                throw new ArgumentNullException( "basePointer" );

            // This method returns a raw address (a ulong), which can be used, for
            // instance, as the address to pass to Get-DbgSymbolValue.
            //
            // We /could/ return a new DbgPointerValue, but to do so, we'd have to
            // introduce a new, "synthetic" symbol type--one without a true address
            // (because the symbol for the new pointer would not represent an actual
            // pointer value present anywhere in the target's memory). And that would
            // probably be way overkill, considering a) we already have an indexer for
            // pointers, and you have Get-DbgSymbolValue to turn addresses into values.
            //
            // For future reference, consider the following diagram (the last line
            // illustrates why we'd need a special new DbgSymbol type, which would
            // overload ReadAs_pointer()):
            //
            //    Address  Value
            //    -------  -----
            //    0x0000   ?? ?? ?? ?? ?? ?? ?? ??  ?? ?? ?? ?? ?? ?? ?? ??
            //    0x0010   00 00 00 00 18 00 00 00  00 00 00 00 00 00 00 00
            //    0x0020   04 03 02 01 04 03 02 01  00 00 00 00 00 00 00 00
            //    0x0030   08 07 06 05 08 07 06 05  00 00 00 00 00 00 00 00
            //    0x0040   04 03 02 01 04 03 02 01  00 00 00 00 00 00 00 00
            //    0x0050   04 03 02 01 04 03 02 01  00 00 00 00 00 00 00 00
            //    0x0060   ?? ?? ?? ?? ?? ?? ?? ??  ?? ?? ?? ?? ?? ?? ?? ??
            //
            //
            //    Symbol       Address               Type     Value
            //    ------       -------               ----     -----
            //    pThing        0x10                 Thing*    0x18  (DbgPointerValue)
            //    Thing         0x18                 Thing     00 00 00 00 00 00 00 00 04 03 02 01 04 03 02 01 (DbgUdtValue)
            //
            //    pThing[1]     0x18+sizeof(Thing)   Thing     00 00 00 00 00 00 00 00 08 07 06 05 08 07 06 05 (DbgUdtValue)
            //
            //    (pThing + 1)  ????                 Thing*    0x18+sizeof(Thing) (DbgPointerValue)
            //
            // If we really wanted to do this, a possible way to do it would be to say
            // that the pointer's value is in a [fake] register. Or pretend it's const
            // data.

            DbgNamedTypeInfo pointeeType = ((DbgPointerTypeInfo) basePointer.OperativeSymbol.Type).PointeeType;
            Util.Assert( 0 != pointeeType.Size );
            if( 0 == pointeeType.Size )
            {
                throw new ArgumentException( Util.Sprintf( "Can't do pointer arithmetic: pointee size unknown ({0}).",
                                                           pointeeType.Name ),
                                             "basePointer" );
            }

            int offset = i * (int) pointeeType.Size;
            return basePointer.OperativeSymbol.ReadAs_pointer() + (uint) offset;
        } // end operator+()


        // This is just to get a better error message when you try to add two pointers.
        public static ulong operator+( DbgPointerValue basePointer, DbgPointerValue other )
        {
            throw new InvalidOperationException( "Cannot add two pointers." ); // same error message that the C compiler will give you.
        }


        /// <summary>
        ///    Performs pointer subtraction (takes size of the pointed-to type into account).
        /// </summary>
        public static ulong operator-( DbgPointerValue basePointer, int i )
        {
            return basePointer + (-i);
        } // end operator-()


        /// <summary>
        ///    Performs pointer subtraction (takes size of the pointed-to type into account).
        /// </summary>
        public static int operator-( DbgPointerValue basePointer, DbgPointerValue offsetPointer )
        {
            if( null == basePointer )
                throw new ArgumentNullException( "basePointer" );

            if( null == offsetPointer )
                throw new ArgumentNullException( "offsetPointer" );

            if( basePointer.OperativeSymbol.Type != offsetPointer.OperativeSymbol.Type )
            {
                throw new InvalidOperationException( Util.Sprintf( "Can't do pointer arithmetic with mismatched pointer types {0} and {1}.",
                                                                   basePointer.OperativeSymbol.Type,
                                                                   offsetPointer.OperativeSymbol.Type ) );
            }

            DbgNamedTypeInfo pointeeType = ((DbgPointerTypeInfo) basePointer.OperativeSymbol.Type).PointeeType;
            Util.Assert( 0 != pointeeType.Size );
            if( 0 == pointeeType.Size )
            {
                throw new ArgumentException( Util.Sprintf( "Can't do pointer arithmetic: pointee size unknown ({0}).",
                                                           pointeeType.Name ),
                                             "basePointer" );
            }

            ulong u1 = (ulong) basePointer;
            ulong u2 = (ulong) offsetPointer;
            long diff = ((long) u1) - ((long) u2);
            diff = diff / (long) pointeeType.Size;

            if( (diff > Int32.MaxValue) || (diff < Int32.MinValue) )
                throw new NotSupportedException( "That's a pretty big gap between those pointers." );

            return (int) diff;
        } // end operator-()


        internal DbgPointerValue( DbgSymbol symbol )
            : base( symbol )
        {
            Util.Assert( symbol.IsPointer );
            Util.Assert( !symbol.IsValueUnavailable );
        } // end constructor

        public override ColorString ToColorString()
        {
            Util.Assert( !OperativeSymbol.IsValueUnavailable );
            ColorString cs;
            var childSym = OperativeSymbol.Children[ 0 ];
            if( _DisplaysOwnPointerVal( childSym ) )
            {
                cs = new ColorString();
            }
            else
            {
                cs = new ColorString( DbgProvider.FormatAddress( DbgGetPointer(),
                                                                 OperativeSymbol.Debugger.TargetIs32Bit,
                                                                 true ) ).Append( " " );
            }

            if( !DbgIsNull() )
            {
                if( childSym.IsPointer && childSym.CanFollow && !_DisplaysOwnPointerVal( childSym ))
                {
                    cs.AppendPushPopFg( ConsoleColor.DarkBlue, "-> " );
                }
                cs.Append( Formatting.Commands.FormatAltSingleLineCommand.FormatSingleLineDirect( DbgGetPointee() ) );
            }

            // We do not follow the pointer here; that will happen in the formatted view.
            return cs;
        } // end ToColorString()


        // For cloning.
        private DbgPointerValue( DbgPointerValue other )
            : base( other.DbgGetSymbolHistory() )
        {
            this.m_ultimatePointeeCache = other.m_ultimatePointeeCache;
        }

        protected override object DoCloning()
        {
            return new DbgPointerValue( this );
        }
    } // end class DbgPointerValue


    // N.B. Having a separate type for null pointers makes some things a lot simpler (for
    // people writing scripts or working at the command line), but it makes some things
    // more complicated (for instance, take a look at the special case in the STL map
    // converter script to handle when there are pointers for keys). But I think the
    // simplifications outweigh the complications. (If we got rid of this type, there
    // would need to be a lot more null checks all over, both here in the code, as well
    // as in script stuff.)
    public class DbgNullValue : DbgPointerValueBase
    {
        internal DbgNullValue( DbgSymbol symbol )
            : base( symbol )
        {
            Util.Assert( 0 == symbol.ReadAs_pointer() );
        } // end constructor

        internal DbgNullValue( List< SymbolHistoryRecord > symbolHistory )
            : base( symbolHistory )
        {
            Util.Assert( 0 == OperativeSymbol.ReadAs_pointer() );
        } // end constructor

        protected override object DoCloning()
        {
            return new DbgNullValue( DbgGetSymbolHistory() );
        }
    } // end class DbgNullValue


    public class DbgVoidStarValue : DbgPointerValueBase
    {
        internal DbgVoidStarValue( DbgSymbol symbol )
            : base( symbol )
        {
        } // end constructor

        protected DbgVoidStarValue( List< SymbolHistoryRecord > symbolHistory )
            : base( symbolHistory )
        {
        } // end constructor

        public override ColorString ToColorString()
        {
            Util.Assert( !OperativeSymbol.IsValueUnavailable );
            return new ColorString( DbgProvider.FormatAddress( OperativeSymbol.ReadAs_pointer(),
                                              OperativeSymbol.Debugger.TargetIs32Bit,
                                              false ) )
                                    .Append( " " )
                                    .Append( DbgProvider.ColorizeTypeName( "Void*" ) );
        }

        protected override object DoCloning()
        {
            return new DbgVoidStarValue( DbgGetSymbolHistory() );
        }
    } // end class DbgVoidStarValue


    public class DbgFunctionPointerValue : DbgPointerValueBase
    {
        internal DbgFunctionPointerValue( DbgSymbol symbol )
            : base( symbol )
        {
            if( null == (symbol.Type as DbgFunctionTypeTypeInfo) )
            {
                throw new ArgumentException( Util.Sprintf( "A DbgFunctionPointerValue can't represent a {0}.",
                                                           Util.GetGenericTypeName( symbol.Type ) ),
                                             "symbol" );
            }
        } // end constructor

        internal DbgFunctionPointerValue( List< SymbolHistoryRecord > symbolHistory )
            : base( symbolHistory )
        {
            if( null == (OperativeSymbol.Type as DbgFunctionTypeTypeInfo) )
            {
                throw new ArgumentException( Util.Sprintf( "A DbgFunctionPointerValue can't represent a {0}.",
                                                           Util.GetGenericTypeName( OperativeSymbol.Type ) ),
                                             "symbol" );
            }
        } // end constructor

        public override ColorString ToColorString()
        {
            Util.Assert( !OperativeSymbol.IsValueUnavailable );
            var funcTypeInfo = (DbgFunctionTypeTypeInfo) OperativeSymbol.Type;
            Util.Assert( !OperativeSymbol.IsValueInRegister );
            ulong funcAddr = OperativeSymbol.Address;
            var cs = DbgProvider.FormatAddress( funcAddr,
                                                OperativeSymbol.Debugger.TargetIs32Bit,
                                                false )
                                    .Append( " " )
                                    .Append( funcTypeInfo.ColorName );
            // If the function pointer points to something we have a symbol for, let's
            // show it:
            if( 0 != funcAddr )
            {
                cs.Append( ": " );
                ulong displacement;
                string symName;

                if( !OperativeSymbol.Debugger.TryGetNameByOffset( funcAddr, out symName, out displacement ) )
                    cs.AppendPushPopFg( ConsoleColor.DarkGray, "<no name>" );
                else
                    cs.Append( DbgProvider.ColorizeSymbol( symName ) );

                if( 0 != displacement )
                {
                    // The function pointer points to a symbol... plus a displacement?
                    // Either it's an inline function (which is fine), or something is
                    // wrong or missing. Either way, we'll highlight this by making the
                    // offset a little more noticeable.
                    cs.AppendPushFg( ConsoleColor.Yellow )
                      .Append( "+0x" )
                      .Append( displacement.ToString( "x" ) )
                      .AppendPop();
                } // end if( displacement )
            } // end if( funcAddr )

            return cs;
        } // end ToColorString()

        protected override object DoCloning()
        {
            return new DbgFunctionPointerValue( DbgGetSymbolHistory() );
        }
    } // end class DbgFunctionPointerValue


    public class DbgUnavailableValue : DbgValue
    {
        internal DbgUnavailableValue( DbgSymbol symbol )
            : base( symbol )
        {
        } // end constructor

        internal DbgUnavailableValue( List< SymbolHistoryRecord > symbolHistory )
            : base( symbolHistory )
        {
        } // end constructor

        private static readonly ColorString sm_notAvailStr
            = new ColorString( ConsoleColor.Yellow, "<value not available>" ).MakeReadOnly();

        public override ColorString ToColorString()
        {
            return sm_notAvailStr;
        }

        protected override object DoCloning()
        {
            return new DbgUnavailableValue( DbgGetSymbolHistory() );
        }
    } // end class DbgUnavailableValue


    public class DbgValueError : DbgValue
    {
        private Exception m_error;

        internal DbgValueError( DbgSymbol symbol, Exception error )
            : base( symbol )
        {
            m_error = error;
        } // end constructor

        internal DbgValueError( List< SymbolHistoryRecord > symbolHistory, Exception error )
            : base( symbolHistory )
        {
            m_error = error;
        } // end constructor

        public Exception DbgGetError()
        {
            return m_error;
        }

        private const string sm_errFmt = "<error: {0}>";

        public override ColorString ToColorString()
        {
            return new ColorString( ConsoleColor.Red,
                                    Util.Sprintf( sm_errFmt,
                                                  Util.GetExceptionMessages( m_error ) ) ).MakeReadOnly();
        }

        protected override object DoCloning()
        {
            return new DbgValueError( DbgGetSymbolHistory(), m_error );
        }
    } // end class DbgValueError


    public class DbgNoValue : DbgValue
    {
        // This constructor is public so that converters can do things like handle a
        // VARIANT with VT_EMPTY.
        public DbgNoValue( DbgSymbol symbol )
            : base( symbol )
        {
            // Sometimes other special values also represent "nothing", like a VARIANT
            // with VT_EMPTY.
          //Util.Assert( (symbol.Type is DbgNullTypeInfo) ||
          //             ((symbol.Type is DbgBaseTypeInfo) && (((DbgBaseTypeInfo) symbol.Type).BaseType == BasicType.btNoType)));
        } // end constructor

        public DbgNoValue( List< SymbolHistoryRecord > symbolHistory )
            : base( symbolHistory )
        {
            // Sometimes other special values also represent "nothing", like a VARIANT
            // with VT_EMPTY.
          //Util.Assert( (symbol.Type is DbgNullTypeInfo) ||
          //             ((symbol.Type is DbgBaseTypeInfo) && (((DbgBaseTypeInfo) symbol.Type).BaseType == BasicType.btNoType)));
        } // end constructor

        public override ColorString ToColorString()
        {
            return new ColorString( ConsoleColor.Yellow, "<no value>" );
        }

        protected override object DoCloning()
        {
            return new DbgNoValue( DbgGetSymbolHistory() );
        }
    } // end class DbgNoValue


    internal class EnumValueHelper : ISupportColor
    {
        public object Value { get; private set; }
        public DbgSymbol Symbol { get; private set; }

        public readonly bool IsBitfield;

        internal EnumValueHelper( DbgSymbol symbol )
        {
            Symbol = symbol;
            Value = _GetEnumValue( symbol,
                                   true, // throw on failure
                                   out IsBitfield );
        } // end constructor


        private static object _GetEnumValue( DbgSymbol symbol,
                                             bool throwOnFailure,
                                             out bool isBitfield )
        {
            isBitfield = false;

            if( null == symbol )
                throw new ArgumentNullException( "symbol" );

            // Native enums are almost always backed by "int" storage.
            // But sometimes not! (ex. WEBPLATFORM_VERSION)
            if( (4 != symbol.Type.Size) &&
                (8 != symbol.Type.Size) &&
                (2 != symbol.Type.Size) &&
                (1 != symbol.Type.Size) )
            {
                throw new Exception( Util.Sprintf( "Unexpected enum size: {0}",
                                                   symbol.Type.Size ) );
            }

            ulong raw;
            if( !symbol.Debugger.TryReadMemAs_integer( symbol.Address,
                                                       (uint) symbol.Type.Size,
                                                       false, // signExtend
                                                       out raw ) )
            {
                // One way we can get here is if we've followed a garbage pointer.
                if( !throwOnFailure )
                    return 0;

                throw new DbgMemoryAccessException( symbol.Address,
                                                    symbol.Debugger.TargetIs32Bit );
            }

            uint bitfieldPos = 0;
            uint bitfieldLen = 0;

            var memberSym = symbol as DbgMemberSymbol;
            if( null != memberSym )
            {
                isBitfield = memberSym.MemberInfo.IsBitfield;
                if( isBitfield )
                {
                    bitfieldPos = memberSym.MemberInfo.BitfieldPosition;
                    bitfieldLen = memberSym.MemberInfo.BitfieldLength;
                }
            }

            object result = null;

            if( 4 == symbol.Type.Size )
                result = (uint) raw;
            else if( 8 == symbol.Type.Size )
                result = raw;
            else if( 2 == symbol.Type.Size )
                result = (ushort) raw;
            else if( 1 == symbol.Type.Size )
                result = (byte) raw;
            else
                throw new Exception( Util.Sprintf( "Unexpected enum size: {0}",
                                                   symbol.Type.Size ) );

            if( bitfieldLen > 0 )
            {
                result = BitHelper.ExtractBitfield( (dynamic) result, bitfieldPos, bitfieldLen );
            }

            return result;
        } // end _GetEnumValue()


        private IReadOnlyList<Enumerand> _Enumerands
        {
            get
            {
                return ((DbgEnumTypeInfo) Symbol.Type).Enumerands;
            }
        } // end property _Enumerands


        public override string ToString()
        {
            return ToColorString().ToString( DbgProvider.HostSupportsColor );
        }

        private ulong _ValueAsULong
        {
            get
            {
                ulong ulongVal;
                if( !(Value is uint) )
                {
                    if( !(Value is ulong) )
                    {
                        if( !(Value is ushort) )
                        {
                            Util.Assert( Value is byte );
                            ulongVal = (ulong) (byte) Value;
                        }
                        else
                        {
                            ulongVal = (ulong) (ushort) Value;
                        }
                    }
                    else
                    {
                        ulongVal = (ulong) Value;
                    }
                }
                else
                {
                    ulongVal = (ulong) (uint) Value;
                }
                return ulongVal;
            }
        }


        public ColorString ToColorString()
        {
            Util.Assert( !Symbol.IsValueUnavailable );

            ulong ulongVal = _ValueAsULong;

            ColorString cs;

            string valStr;

            if( IsBitfield &&
                ((DbgMemberSymbol) Symbol).MemberInfo.BitfieldLength < 8 )
            {
                uint len = ((DbgMemberSymbol) Symbol).MemberInfo.BitfieldLength;

                valStr = Convert.ToString( (long) ulongVal, 2 ); // base 2
                int numZeroesNeeded = ((int) len) - valStr.Length;

                if( numZeroesNeeded > 0 )
                    valStr = new String( '0', numZeroesNeeded ) + valStr;

                valStr = "0y" + valStr;
            }
            else
            {
                if( (ulongVal >= 0) && (ulongVal < 10) )
                    valStr = Util.Sprintf( "{0}", ulongVal );
                else
                    valStr = Util.Sprintf( "0x{0:x}", ulongVal );
            }

            cs = new ColorString( valStr );

            var csEnumName = ToColorStringSimple();

            if( csEnumName.Length > 0 )
                cs.Append( " (" ).Append( csEnumName ).Append( ") " );
            else
                cs.Append( " " );

            cs = cs.Append( DbgProvider.ColorizeTypeName( Symbol.Type.Name ) ).MakeReadOnly();

            return cs;
        } // end ToColorString()


        public ColorString ToColorStringSimple()
        {
            Util.Assert( !Symbol.IsValueUnavailable );

            ulong ulongVal = _ValueAsULong;

            // First check if it's just a plain enumerand:
            foreach( Enumerand e in _Enumerands )
            {
                if( e.Value == ulongVal )
                {
                    return new ColorString( ConsoleColor.Cyan, e.Name ).MakeReadOnly();
                }
            }

            // Hm. Maybe it's flags.
            var cs = new ColorString();
            bool first = true;
            dynamic dVal = (dynamic) Value;
            int unknownFlags = 0;
            foreach( var bit in BitHelper.EnumBitsThatAreSet( dVal ) ) // cast to dynamic for dynamic dispatch
            {
                bool foundEnumerandForBit = false;
                foreach( Enumerand e in _Enumerands )
                {
                    if( e.Value == bit )
                    {
                        foundEnumerandForBit = true;
                        if( !first )
                            cs.Append( " | " );
                        else
                            first = false;

                        cs.AppendPushPopFg( ConsoleColor.Cyan, e.Name );
                        break;
                    }
                } // end foreach( enumerand )
                if( !foundEnumerandForBit )
                    unknownFlags |= bit;
            } // end foreach( bit that is set )

            if( !first )
            {
                if( 0 != unknownFlags )
                    cs.AppendPushPopFg( ConsoleColor.Yellow, Util.Sprintf( " + unknown 0x{0:x}", unknownFlags ) );
            }
            else
            {
                // It's pretty common to have 0 as an implicit "nothing"; let's not
                // complain about not finding an enumerand for that.
                if( ulongVal != 0 )
                {
                    cs.AppendPushPopFg( ConsoleColor.Yellow, "couldn't find matching enumerands" );
                }
            }

            return cs.MakeReadOnly();
        } // end ToColorStringSimple()

     // public ColorString ToColorString( string format )
     // {
     //     if( String.IsNullOrEmpty( format ) || (0 == Util.Strcmp_OI( "g", format )) )
     //         return ToColorString();

     //     if( 0 == Util.Strcmp_OI( "s", format ) )
     //         return ToColorStringSimple();

     //     throw new ArgumentException( Util.Sprintf( "Unknown format: {0}.", format ),
     //                                  "format" );
     // } // end ToColorString( format )
    } // end class EnumValueHelper


    public class DbgArrayValue : DbgValue, IReadOnlyList< PSObject >
    {
        private DbgArrayTypeInfo m_arrayType;

        internal DbgArrayValue( DbgSymbol symbol )
            : base( symbol )
        {
            Util.Assert( !symbol.IsValueUnavailable );
            if( !symbol.IsArray )
                throw new ArgumentException( "The symbol should be an array type.",
                                             "symbol" );

            m_arrayType = (DbgArrayTypeInfo) symbol.Type;
        } // end constructor

        internal DbgArrayValue( List< SymbolHistoryRecord > symbolHistory )
            : base( symbolHistory )
        {
            Util.Assert( !OperativeSymbol.IsValueUnavailable );
            if( !OperativeSymbol.IsArray )
                throw new ArgumentException( "The symbol should be an array type.",
                                             "symbol" );

            m_arrayType = (DbgArrayTypeInfo) OperativeSymbol.Type;
        } // end constructor

        public override ColorString ToColorString()
        {
            ColorString cs = new ColorString( m_arrayType.ColorName ).Append( " {" );

            // TODO: Get $FormatEnumerationLimit
            int numSummaryElementsToDisplay = 4; // TODO: it would be REALLY nice if somehow we knew how much line length we had left, and could just truncate/stop building at the appropriate spot.
            int idx = 0;
            while( (idx < m_arrayType.Count) && (idx < numSummaryElementsToDisplay) )
            {
                cs.Append( Formatting.Commands.FormatAltSingleLineCommand.FormatSingleLineDirect( this[ idx ] ) );
                if( idx < (m_arrayType.Count - 1) )
                    cs.Append( ", " );

                idx++;
            }
            if( idx < (((int) m_arrayType.Count) - 1) ) // Cast to prevent underflow
                cs.Append( "..." );

            cs.Append( "}" );

            return cs;
        } // end ToColorString()


        private Dictionary< int, PSObject > m_arrayIndexCache;

        public PSObject this[ int idx ]
        {
            get
            {
                if( (idx >= m_arrayType.Count) || (idx < 0) )
                {
                    throw new ArgumentOutOfRangeException( "idx",
                                                           idx,
                                                           Util.Sprintf( "Index {0} is out of range. Valid indices are [0 - {1}].",
                                                                                                                idx,
                                                                                                                m_arrayType.Count - 1 ) );
                }

                if( null == m_arrayIndexCache )
                    m_arrayIndexCache = new Dictionary<int, PSObject>( Math.Min( 256, (int) m_arrayType.Count ) );

                PSObject val;
                if( !m_arrayIndexCache.TryGetValue( idx, out val ) )
                {
                    StringBuilder sbElemName;
                    sbElemName = new StringBuilder( OperativeSymbol.Name );
                    sbElemName.Append( "[" ).Append( idx.ToString() ).Append( "]" );

                    ulong elemAddr = OperativeSymbol.Address + ((uint) idx * (uint) m_arrayType.ArrayElementType.Size);
                    var elemSym = new DbgSimpleSymbol( OperativeSymbol.Debugger,
                                                       sbElemName.ToString(),
                                                       m_arrayType.ArrayElementType,
                                                       elemAddr );
                    val = DbgValue.CreateDbgValue( elemSym );
                    m_arrayIndexCache.Add( idx, val );
                }
                return val;
            }

            set
            {
                throw new NotImplementedException();
            }
        } // end indexer

        #region IReadOnlyCollection< PSObject > Members

        public int Count
        {
            get { return (int) m_arrayType.Count; }
        }

        #endregion

        #region IEnumerable<object> Members

        public IEnumerator< PSObject > GetEnumerator()
        {
            for( int i = 0; i < Count; i++ )
            {
                yield return this[ i ];
            }
        }

        #endregion

        #region IEnumerable Members

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion


        protected override object DoCloning()
        {
            return new DbgArrayValue( DbgGetSymbolHistory() );
        }
    } // end class DbgArrayValue
}

