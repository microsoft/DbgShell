using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Runtime.ExceptionServices;
using static MS.Dbg.Commands.AtmeHelper;

namespace MS.Dbg.Commands
{
    static class AtmeHelper
    {
        /// <summary>
        ///    Creates an ArgumentTransformationMetadataException which indicates a
        ///    "recoverable" error (so that subsequent binding attempts can proceed).
        /// </summary>
        /// <remarks>
        ///    Docs currently say that for recoverable errors, you should throw an ATME.
        ///    This is not quite correct--if the ATME does not have an InnerException
        ///    which is a PSInvalidCastException, then the error is /not/ treated as
        ///    recoverable by the PS runtime.
        ///
        ///    If an error is "recoverable", that means that PS can continue to do
        ///    parameter binding. For example if you have a parameter that can be bound to
        ///    pipeline input AND can be bound to pipeline input by property name, then
        ///    when you pipe an object in, the first transformation may fail, and you want
        ///    PS to be able to continue binding by checking the object for a property
        ///    with the appropriate name.
        ///
        ///    An alternative way to fail in a recoverable way is to return the original
        ///    inputData object.
        ///
        ///    C.f. https://github.com/PowerShell/PowerShell/issues/7600
        /// </remarks>
        public static Exception CreateRecoverableAtme( string msgFmt,
                                                       params object[] msgInserts )
        {
            return CreateRecoverableAtme( null, msgFmt, msgInserts );
        }

        public static Exception CreateRecoverableAtme( Exception innerInnerException,
                                                       string msgFmt,
                                                       params object[] msgInserts )
        {
            string msg  = Util.Sprintf( msgFmt, msgInserts );

            return new ArgumentTransformationMetadataException( msg,
                                                                new PSInvalidCastException( msg,
                                                                                            innerInnerException ) );
        }
    }


    /// <summary>
    ///    Converts things into ulongs:
    ///
    ///    1) The sort of hex strings that you see in debuggers. In particular, it handles
    ///       strings like "0123abcd`def05679"--including if PowerShell has interpreted
    ///       the backtick as an escape, or if PowerShell has interpreted it as a base-10
    ///       number (an address with no hex digits in it, like "77954108").
    ///    2) Sybmolic names (via GetOffsetByName).
    /// </summary>
    [AttributeUsage( AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false )]
    public class AddressTransformationAttribute : ArgumentTransformationAttribute
    {
        //public string DebuggingTag { get; set; }

        public bool SkipGlobalSymbolTest { get; set; }

        // This is a hack to let all the memory dumping functions (dd, dp, dps, dc, dq,
        // etc.) deal with /either/ an address, or just operate against an existing
        // DbgMemory object. (It's very convenient to be able to be able to do something
        // like "$mem | dps", when $mem originally came from something like "dd".)
        //
        // (I can't find any way to get PS to properly disambiguate between two param sets
        // with all the behavior I want.)
        public bool DbgMemoryPassThru { get; set; }


        public bool AllowList { get; set; }


        public override object Transform( EngineIntrinsics engineIntrinsics, object inputData )
        {
            return Transform( engineIntrinsics,
                              null,
                              SkipGlobalSymbolTest,
                              true, // throwOnFailure
                              DbgMemoryPassThru,
                              AllowList,
                              inputData );
        }

        // You must pass either the engineIntrinsics, or the path. (You don't need both.)
        // This is a hacky-wacky workaround for the fact that there's no way for user code
        // to get an EngineIntrinsics object, but I want user code to be able to call this
        // method directly, and I don't want everyone to /always/ have to get the path.
        public static object Transform( EngineIntrinsics engineIntrinsics,
                                        string dbgProviderPath,
                                        bool skipGlobalSymbolTest,
                                        bool throwOnFailure,
                                        object inputData )
        {
            return Transform( engineIntrinsics,
                              dbgProviderPath,
                              skipGlobalSymbolTest,
                              throwOnFailure,
                              false, // dbgMemoryPassthru
                              false, // allowList
                              inputData );
        }

        // You must pass either the engineIntrinsics, or the path. (You don't need both.)
        // This is a hacky-wacky workaround for the fact that there's no way for user code
        // to get an EngineIntrinsics object, but I want user code to be able to call this
        // method directly, and I don't want everyone to /always/ have to get the path.
        public static object Transform( EngineIntrinsics engineIntrinsics,
                                        string dbgProviderPath,
                                        bool skipGlobalSymbolTest,
                                        bool throwOnFailure,
                                        bool dbgMemoryPassthru,
                                        object inputData )
        {
            return Transform( engineIntrinsics,
                              dbgProviderPath,
                              skipGlobalSymbolTest,
                              throwOnFailure,
                              dbgMemoryPassthru,
                              false, // allowList
                              inputData );
        }

        // You must pass either the engineIntrinsics, or the path. (You don't need both.)
        // This is a hacky-wacky workaround for the fact that there's no way for user code
        // to get an EngineIntrinsics object, but I want user code to be able to call this
        // method directly, and I don't want everyone to /always/ have to get the path.
        public static object Transform( EngineIntrinsics engineIntrinsics,
                                        string dbgProviderPath,
                                        bool skipGlobalSymbolTest,
                                        bool throwOnFailure,
                                        bool dbgMemoryPassthru,
                                        bool allowList,
                                        object inputData )
        {
            //Console.WriteLine( "vvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvv" );
            //Console.WriteLine( "{0} 1: inputData type: {1}", DebuggingTag, inputData.GetType().FullName );
            //Console.WriteLine( "{0} 2: dynamic type: {1}", DebuggingTag, ((dynamic) inputData).GetType().FullName );
            //Console.WriteLine( "{0} 3: ToString(): {1}", DebuggingTag, inputData.ToString() );
            //Console.WriteLine( "{0} 4: dynamic ToString(): {1}", DebuggingTag, ((dynamic) inputData).ToString() );

            var pso = inputData as PSObject;

            if( allowList )
            {
                var objList = inputData as IList;

                if( (null != pso) && (pso.BaseObject is IList) )
                {
                    objList = (IList) pso.BaseObject;
                }

                if( null != objList )
                {
                    ulong[] addrs = new ulong[ objList.Count ];
                    try
                    {
                        for( int i = 0; i < objList.Count; i++ )
                        {
                            addrs[ i ] = (ulong) Transform( engineIntrinsics,
                                                            dbgProviderPath,
                                                            skipGlobalSymbolTest,
                                                            true, // throwOnFailure,
                                                            dbgMemoryPassthru,
                                                            false, // we don't allow nested arrays
                                                            objList[ i ] );
                        } // end for( each obj )
                        return addrs;
                    }
                    catch( Exception e_temp )
                    {
                        if( throwOnFailure ||
                            (!(e_temp is DbgProviderException) && !(e_temp is MetadataException)) )
                        {
                            throw;
                        }
                    }
                } // end if( it's an array )
            } // end if( allowList )

            if( null != pso )
            {
                // Addresses are always expressed in hexadecimal.
                //
                // Thus you can type a leading "0x", but it is redundant and not
                // necessary.
                //
                // If the address contains a backtick, or for some reason is expressed in
                // decimal with a leading "0n", or has hex-only digits ([a-f]) but no
                // leading "0x", then PowerShell will parse it as a string. Then we can
                // handle parsing it ourselves (in this method).
                //
                // However... if the user /did/ use a leading "0x" and there is no
                // backtick, OR if the address contains no hex-only digits and there is no
                // backtick, OR if the address ended with "eN" (where N is a digit)...
                // then PowerShell will have parsed it as a number (giving us either an
                // UInt32, or a UInt64, or a double, depending on how big).
                //
                // In that case, we need to figure out whether or not the user typed an
                // "0x", because if they did not, that means that PowerShell parsed it
                // incorrectly (as a base-10 number, and possibly using scientific
                // notation, instead of a base-16 number).
                //
                // Fortunately, if we have the PSObject for that typed number, we can get
                // the originally typed string, which will let us know if there was an
                // "0x" or not.
                //
                // Update: "if we have the PSObject for that typed number, we can get the
                // originally typed string": unfortunately, that is not always true, such
                // as when the address is piped in. TODO: can we change PowerShell to
                // allow us to get the string as originally typed in more cases?

                //Console.WriteLine( "{0} 5: BaseObject type: {1}", DebuggingTag, pso.BaseObject.GetType().FullName );
                if( (pso.BaseObject is int) ||
                    (pso.BaseObject is long) ||
                    (pso.BaseObject is double) ||
                    (pso.BaseObject is float) )
                {
                    // The standard way to get the originally typed string is to use
                    // LanguagePrimitives.ConvertTo< string >. However, it seems that it
                    // wants to /always/ give us a string back, even if it doesn't have
                    // the originally typed string. So if we use that method, we don't
                    // know if the string we get back actually is what was originally
                    // typed or not.
                    //var asTyped = LanguagePrimitives.ConvertTo< string >( pso );

                    // This /will/ get what the user actually typed (if it was typed),
                    // but relies on reflection to get at PS internals. :(
                    var asTyped = _GetAsTyped_usingIckyPrivateReflection( pso );
                    //Console.WriteLine( "As typed: {0}", asTyped );

                    if( null != asTyped )
                    {
                        if( asTyped.StartsWith( "0x", StringComparison.OrdinalIgnoreCase ) )
                        {
                            // Yes, they typed an "0x", so PS correctly parsed as hex.
                            //
                            // The cast to (int) first is to un-box. Then to (uint) to
                            // prevent sign extension.
                            if( pso.BaseObject is int )
                                return (ulong) (uint) (int) pso.BaseObject;

                            if( pso.BaseObject is long )
                                return unchecked( (ulong) (long) pso.BaseObject );

                            // Should not reach here.
                            Util.Fail( "How could the typed string start with 0x but get parsed as something besides an int or long?" );
                        }

                        inputData = asTyped; // we'll re-parse it below as base-16
                    }
                    else
                    {
                        // If we get here, then it /was/ typed, but piped in:
                        //
                        //    01234000 | ConvertTo-Number
                        //  0x01234000 | ConvertTo-Number
                        //
                        // So PS parsed it... but if we ended up with an integer type, we
                        // don't know if it parsed as decimal or hex, so we can't be sure
                        // how to undo that parsing. :(  For now we'll have to just assume
                        // that the user knows that they need to use 0x when piping in.
                        //
                        // That sounds bad, because actually a user probably will /not/
                        // know that, but the alternative is worse; a user who directly
                        // specifies hex ("0x01230000 | something") should never get the
                        // wrong result.
                        //
                        // TODO: see if we can get PS to preserve the as-typed value for
                        // things that are piped in.
                        inputData = pso.BaseObject;
                    }
                }
                else
                {
                    inputData = pso.BaseObject;
                }
            }
            //Console.WriteLine( "^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^" );

            if( dbgMemoryPassthru && (inputData is DbgMemory) )
                return inputData;

            // Some commands do not require an address.
            if( null == inputData )
                return (ulong) 0;

            if( inputData is ulong )
                return inputData;

            // We used to assume that this was probably a base-16 number without any
            // letters in it, so PS interpreted it as a base-10 number, so then we would
            // undo that. And hope it was right. But unfortunately, that messed up the
            // scenario where you assign a number to a variable ("$blah = 123"), so I'm
            // going the other way now--we'll assume it's already correct. Unfortunately,
            // that means that on 32-bit, you'll need to always use "0x" to be safe. :(
         // if( (inputData is int) || (inputData is long) )
         // {
         //     inputData = inputData.ToString();
         // }

            if( inputData is int )
                return (ulong) (uint) (int) inputData; // 1st cast unboxes; 2nd prevents sign extension

            if( inputData is long )
                return (ulong) (long) inputData; // need two casts in order to unbox first.

            if( inputData is uint )
            {
                // This can happen, for instance, when using the register variables for a
                // 32-bit process ("u $eip").
                return (ulong) (uint) inputData; // need two casts in order to unbox first.
            }

            if( inputData is byte )
            {
                // This can happen because we [ab]use AddressTransformationAttribute to
                // convert lots of numeric data, not just addresses. (For instance, the
                // "eb" command.)
                return (ulong) (byte) inputData; // need two casts in order to unbox first.
            }

            if( inputData is double )
            {
                // This can happen when doing arithmetic. For instance, this will yield
                // Double:
                //
                //    [UInt32] $ui1 = 0x03
                //    [UInt32] $ui2 = 0x01
                //    ($ui2 - $ui1).GetType()
                //
                // To determine if it's really something that can be represented as a
                // ulong, we'll round-trip it through a ulong back to a double, and then
                // see if the bits representation matches the original double.

                double dOrig = (double) inputData;
                Int64 origBits = BitConverter.DoubleToInt64Bits( dOrig );
                unchecked
                {
                    ulong asUlong = (ulong) (long) dOrig;
                    double d2 = Convert.ToDouble( (long) asUlong );
                    Int64 d2Bits = BitConverter.DoubleToInt64Bits( d2 );
                    if( d2Bits == origBits )
                    {
                        // We round-tripped back to double: it doesn't have any fractional
                        // part.
                        return asUlong;
                    }
                }
            } // end if( inputData is double )

            Exception e = null;
            string str = inputData as string;
            if( null != str )
            {
                // Some commands do not require an address.
                if( 0 == str.Length )
                    return (ulong) 0;

                if( (1 == str.Length) && (str[ 0 ] == '.') )
                {
                    dbgProviderPath = _GetDbgProviderPath( dbgProviderPath, engineIntrinsics );
                    var regSet = DbgProvider.GetRegisterSetForPath( dbgProviderPath );
                    return regSet.Pseudo[ "$ip" ].Value;
                }

                ulong address;
                if( DbgProvider.TryParseHexOrDecimalNumber( str, out address ) )
                    return address;

                // Mabye it's a symbolic name?
                if( !skipGlobalSymbolTest )
                {
                    dbgProviderPath = _GetDbgProviderPath( dbgProviderPath, engineIntrinsics );
                    var debugger = DbgProvider.GetDebugger( dbgProviderPath );
                    try
                    {
                        address = debugger.GetOffsetByName( str );
                        return address;
                    }
                    catch( DbgProviderException dpe )
                    {
                        e = dpe;
                    }
                }
            }

            // Check for implicit conversion to ulong. (For instance, types that derive
            // from DbgPointerValueBase have this.)
            ulong addr;
            if( _TryImplicitConversionTo( inputData, out addr ) )
                return addr;

            if( !throwOnFailure )
                return null;

            if( null != e )
            {
                ExceptionDispatchInfo.Capture( e ).Throw();
            }

            // https://github.com/PowerShell/PowerShell/issues/7600
            //
            // For parameter binding to be able to continue (for example, to try binding
            // by property name), this exception needs to wrap a PSInvalidCastException.
            throw CreateRecoverableAtme( "Could not convert '{0}' to an address.", inputData );
        } // end Transform()


        private static FieldInfo s_tokenTextField =
            typeof( PSObject ).GetField( "TokenText",
                                         BindingFlags.Instance | BindingFlags.NonPublic );

        private static string _GetAsTyped_usingIckyPrivateReflection( PSObject pso )
        {
            return (string) s_tokenTextField.GetValue( pso );
        } // end _GetAsTyped_usingIckyPrivateReflection()


        private static string _GetDbgProviderPath( string suppliedDbgProviderPath, EngineIntrinsics engineIntrinsics )
        {
            if( String.IsNullOrEmpty( suppliedDbgProviderPath ) )
            {
                if( null == engineIntrinsics )
                    throw new ArgumentException( "I need either engineIntrinsics or dbgProviderPath." );

                suppliedDbgProviderPath = engineIntrinsics.SessionState.Path.CurrentProviderLocation( DbgProvider.ProviderId ).ProviderPath;
            }
            return suppliedDbgProviderPath;
        } // end _GetDbgProviderPath()


        private static bool _TryImplicitConversionTo< T >( object inputData, out T t )
        {
            t = default( T );

            if( null == inputData )
                return false;

            return _TryImplicitConversionTo_worker( inputData, inputData.GetType(), ref t );
        } // end _TryImplicitConversionTo()

        private static bool _TryImplicitConversionTo_worker< T >( object inputData,
                                                                  Type inputType,
                                                                  ref T t )
        {
            if( null == inputType )
                return false; // we've exhausted base classes

            Type returnType = typeof( T );
            foreach( var implicitOp in inputType.GetMethods( BindingFlags.Public | BindingFlags.Static )
                                                .Where( (x) => 0 == Util.Strcmp_OI( x.Name, "op_Implicit" ) ) )
            {
                if( returnType.IsAssignableFrom( implicitOp.ReturnType ) )
                {
                    t = (T) implicitOp.Invoke( null, new object[] { inputData } );
                    return true;
                }
            }

            // We have to manually check base classes.
            return _TryImplicitConversionTo_worker( inputData, inputType.BaseType, ref t );
        } // end _TryImplicitConversionTo_worker()
    } // end class AddressTransformationAttribute


    /// <summary>
    ///    Converts range strings (like "L4", "L-4", "L?67108868") into Int32s.
    ///
    ///    Assumes the number is decimal, unless you use an 0x.
    ///
    ///    Also does a range check to make sure it's not too big (256 MB).
    /// </summary>
    [AttributeUsage( AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false )]
    public class RangeTransformationAttribute : ArgumentTransformationAttribute
    {
        // Like for "dd", the ElementSize should be set to 4 (4 bytes in a DWORD).
        public int ElementSize { get; set; }

        public RangeTransformationAttribute()
        {
            ElementSize = 1; // default if none specified
        }

        public override object Transform( EngineIntrinsics engineIntrinsics, object inputData )
        {
            if( 0 == ElementSize )
            {
                // Pointer size.
                dynamic debugger = engineIntrinsics.SessionState.PSVariable.GetValue( "Debugger", null );
                if( null != debugger )
                    ElementSize = debugger.TargetIs32Bit ? 4 : 8;
                else
                {
                    Util.Fail( "Could not get debugger?" );
                    ElementSize = 8;
                }
            } // end if( 0 == ElementSize )

            object addrObj = AddressTransformationAttribute.Transform( engineIntrinsics,
                                                                       null,
                                                                       true,  // skipGlobalSymbolTest
                                                                       false, // throwOnFailure
                                                                       inputData );

            if( null != addrObj )
            {
                return _CreatePso( (ulong) addrObj );
            }

            //
            // It must be in "range" syntax. ("L[?][0n|0x]\d+", ex. "L100")
            //

            var pso = inputData as PSObject;
            if( null != pso )
            {
                inputData = pso.BaseObject;
            }

            // Some commands do not require a range.
            if( null == inputData )
                return _CreatePso( (ulong) 0 );

            string str = inputData as string;
            if( null != str )
            {
                // TODO: Shoule we implement "n" command and "default radix" concept? Or
                // will that be too everlastingly confusing, given that we can't change
                // the "radix" concept for the rest of PowerShell?

                // Some commands do not require a range.
                if( 0 == str.Length )
                    return _CreatePso( (ulong) 0 );

                bool sizeOverride = false;
                bool negative = false;
                bool hasLengthPrefix = false;

                if( ('L' == str[ 0 ]) || ('l' == str[ 0 ]) )
                {
                    hasLengthPrefix = true;
                    int numIdx = 1;
                    if( str.Length > 1 )
                    {
                        if( '?' == str[ 1 ] )
                        {
                            sizeOverride = true;
                            numIdx++;
                        }
                        else if( '-' == str[ 1 ] )
                        {
                            negative = true;
                            numIdx++;
                        }
                    }
                    str = str.Substring( numIdx );
                } // end if( StartsWith( 'L' ) )

                NumberStyles ns = NumberStyles.AllowHexSpecifier;
                if( str.StartsWith( "0x", StringComparison.OrdinalIgnoreCase ) )
                {
                    str = str.Substring( 2 );
                }
                else if( str.StartsWith( "0n", StringComparison.OrdinalIgnoreCase ) )
                {
                    ns = NumberStyles.None;
                    str = str.Substring( 2 );
                }
                ulong rangeOrAddr;
                if( UInt64.TryParse( str, ns, CultureInfo.InvariantCulture, out rangeOrAddr ) )
                {
                    // 256 MB seems rather large... maybe I'll default to 256k without a size override.
                    const ulong maxRange = 1024 * 256;

                    // We only do a max size check if we know we are checking a range;
                    // otherwise it could be an end address.
                    if( hasLengthPrefix &&
                        ((rangeOrAddr > maxRange) || ((rangeOrAddr * (ulong) ElementSize) > maxRange)) &&
                        !sizeOverride )
                    {
                        throw new ArgumentTransformationMetadataException(
                            Util.Sprintf( "Requested size ({0} * {1}) is too large; probably a typo. If you really want a size that large, use the \"L?<size> syntax.",
                                          rangeOrAddr,
                                          ElementSize ) );
                    }
                    return _CreatePso( rangeOrAddr, hasLengthPrefix, negative );
                }
            } // end if( it's a string )

            throw CreateRecoverableAtme( "Could not convert '{0}' to a range or an address.",
                                         inputData );
        } // end Transform()


        private static PSObject _CreatePso( ulong rangeOrAddress )
        {
            return _CreatePso( rangeOrAddress, false, false );
        }

        private static PSObject _CreatePso( ulong rangeOrAddress, bool hasLengthPrefix )
        {
            return _CreatePso( rangeOrAddress, hasLengthPrefix, false );
        }

        private static PSObject _CreatePso( ulong rangeOrAddress,
                                            bool hasLengthPrefix,
                                            bool isNegative )
        {
            PSObject pso = new PSObject( rangeOrAddress );
            pso.Properties.Add( new SimplePSPropertyInfo( "HasLengthPrefix", hasLengthPrefix ) );
            pso.Properties.Add( new SimplePSPropertyInfo( "IsNegative", isNegative ) );
            return pso;
        }
    } // end class RangeTransformationAttribute


    /// <summary>
    ///     Transforms things (well, strings) into DbgModuleInfo objects.
    /// </summary>
    [AttributeUsage( AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false )]
    public class ModuleTransformationAttribute : ArgumentTransformationAttribute
    {
        public override object Transform( EngineIntrinsics engineIntrinsics, object inputData )
        {
            object originalInputData = inputData;

            var pso = inputData as PSObject;
            if( null != pso )
            {
                inputData = pso.BaseObject;
            }

            if( inputData is DbgModuleInfo )
            {
                return inputData;
            }

            if( null == inputData )
            {
                return null;
            }

            string path = engineIntrinsics.SessionState.Path.CurrentProviderLocation( DbgProvider.ProviderId ).ProviderPath;
            var debugger = DbgProvider.GetDebugger( path );

            Exception e = null;
            string str = inputData as string;
            if( null != str )
            {
                if( 0 == str.Length )
                {
                    return null;
                }

                try
                {
                    var foundMod = debugger.Modules.FirstOrDefault( (m) => 0 == Util.Strcmp_OI( m.Name, str ) );
                    if( null != foundMod )
                        return foundMod;
                }
                catch( DbgProviderException dpe )
                {
                    e = dpe;
                }
            }

            if( null == e )
            {
                // Maybe it was an address...
                try
                {
                    object addrObj = AddressTransformationAttribute.Transform(
                                        engineIntrinsics,
                                        null,
                                        true,  // skipGlobalSymbolTest
                                        false, // throwOnFailure
                                        originalInputData );
                    if( null != addrObj )
                    {
                        return new DbgModuleInfo( debugger,
                                                  (ulong) addrObj,
                                                  debugger.GetCurrentTarget() );
                    }
                }
                catch( DbgEngException dee )
                {
                    e = dee;
                }
            }

            throw CreateRecoverableAtme( e,
                                         "Could not convert '{0}' to a module.",
                                         originalInputData );
        } // end Transform()
    } // end class ModuleTransformationAttribute


    /// <summary>
    ///     Transforms things (well, DbgModuleInfo objects) into strings (specifically
    ///     the ImageName of the module).
    /// </summary>
    [AttributeUsage( AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false )]
    public class ModuleNameTransformationAttribute : ArgumentTransformationAttribute
    {
        public override object Transform( EngineIntrinsics engineIntrinsics, object inputData )
        {
            object originalInputData = inputData;

            var pso = inputData as PSObject;
            if( null != pso )
            {
                inputData = pso.BaseObject;
            }

            if( inputData is string )
            {
                return inputData;
            }

            if( inputData is DbgModuleInfo )
            {
                return ((DbgModuleInfo) inputData).ImageName;
            }

            string path = engineIntrinsics.SessionState.Path.CurrentProviderLocation( DbgProvider.ProviderId ).ProviderPath;
            var debugger = DbgProvider.GetDebugger( path );

            Exception e = null;
            // Maybe it is an address?
            try
            {
                object addrObj = AddressTransformationAttribute.Transform(
                                    engineIntrinsics,
                                    null,
                                    true,  // skipGlobalSymbolTest
                                    false, // throwOnFailure
                                    originalInputData );
                if( null != addrObj )
                {
                    var mod = new DbgModuleInfo( debugger,
                                                 (ulong) addrObj,
                                                 debugger.GetCurrentTarget() );
                    return mod.Name;
                }
            }
            catch( DbgEngException dee )
            {
                e = dee;
            }

            throw CreateRecoverableAtme( e,
                                         "Could not convert '{0}' to a module name.",
                                         originalInputData );
        } // end Transform()
    } // end class ModuleNameTransformationAttribute




    [AttributeUsage( AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false )]
    public class TypeTransformationAttribute : ArgumentTransformationAttribute
    {
        public override object Transform( EngineIntrinsics engineIntrinsics, object inputData )
        {
            object originalInputData = inputData;

            var pso = inputData as PSObject;
            if( null != pso )
            {
                inputData = pso.BaseObject;
            }

            if( inputData is DbgTypeInfo )
            {
                return inputData;
            }

            string path = engineIntrinsics.SessionState.Path.CurrentProviderLocation( DbgProvider.ProviderId ).ProviderPath;
            var debugger = DbgProvider.GetDebugger( path );

            Exception e = null;
            string str = inputData as string;
            if( null != str )
            {
                try
                {
                    // TODO: Would be nice to ask the PowerShell team to plumb cancellation support
                    // through parameter transformation.
                    var types = debugger.GetTypeInfoByName( str,
                                                            System.Threading.CancellationToken.None ).ToList();
                    if( null != types )
                    {
                        if( types.Count > 1 )
                        {
                            engineIntrinsics.InvokeCommand.InvokeScript( Util.Sprintf( "Write-Warning 'Multiple types found for name \"{0}\".'",
                                                                                       str ) );
                            LogManager.Trace( "Warning: TypeTransformationAttribute: found multiple types for name '{0}'.",
                                              str );
                        }
                        else if( 0 != types.Count )
                        {
                            return types[ 0 ];
                        }
                    }
                }
                catch( DbgProviderException dpe )
                {
                    e = dpe;
                }
            } // end if( it's a string )

            throw CreateRecoverableAtme( e,
                                         "Type not found. (Could not convert '{0}' to a type.)",
                                         originalInputData );
        } // end Transform()
    } // end class TypeTransformationAttribute


    [AttributeUsage( AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false )]
    public class EffMachTransformationAttribute : ArgumentTransformationAttribute
    {
        public override object Transform( EngineIntrinsics engineIntrinsics, object inputData )
        {
            object originalInputData = inputData;

            var pso = inputData as PSObject;
            if( null != pso )
            {
                inputData = pso.BaseObject;
            }

            if( inputData is Microsoft.Diagnostics.Runtime.Interop.IMAGE_FILE_MACHINE )
            {
                return inputData;
            }

            string str = inputData as string;
            if( !String.IsNullOrEmpty( str ) )
            {
                if( 0 == Util.Strcmp_OI( str, "x86" ) )
                {
                    return Microsoft.Diagnostics.Runtime.Interop.IMAGE_FILE_MACHINE.I386;
                }
                else if( 0 == Util.Strcmp_OI( str, "x64" ) )
                {
                    return Microsoft.Diagnostics.Runtime.Interop.IMAGE_FILE_MACHINE.AMD64;
                }
                else
                {
                    Microsoft.Diagnostics.Runtime.Interop.IMAGE_FILE_MACHINE effmach;
                    // Hack: Enum.TryParse does not seem to work case-insensitively. But
                    // we know that (for now, anyway) none of the possible enumerands have
                    // any lower-case in them, so we'll just upcase everything here.
                    str = str.ToUpperInvariant();
                    if( Enum.TryParse( str, out effmach ) )
                    {
                        return effmach;
                    }
                }
            }

            if( inputData is int )
            {
                return (Microsoft.Diagnostics.Runtime.Interop.IMAGE_FILE_MACHINE) ((int) inputData);
            }

            throw CreateRecoverableAtme( "Could not convert '{0}' to an effective machine type.",
                                         originalInputData );
        } // end Transform()
    } // end class EffMachTransformationAttribute


    [AttributeUsage( AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false )]
    public class ThreadTransformationAttribute : ArgumentTransformationAttribute
    {
        private static bool _TryConvertToUint( EngineIntrinsics engineIntrinsics, object inputData, out uint val )
        {
            val = 0xffffffff;

            object originalInputData = inputData;

            var pso = inputData as PSObject;
            if( null != pso )
            {
                inputData = pso.BaseObject;
            }

            if( inputData is int )
            {
                val = (uint) (int) inputData;
                return true;
            }
            else if( inputData is uint )
            {
                val = (uint) inputData;
                return true;
            }

            // Maybe it was hex and got parsed as a string?
            try
            {
                object addrObj = AddressTransformationAttribute.Transform(
                                    engineIntrinsics,
                                    null,
                                    true,  // skipGlobalSymbolTest
                                    false, // throwOnFailure
                                    originalInputData );
                if( null != addrObj )
                {
                    ulong asAddr = (ulong) addrObj;
                    val = unchecked( (uint) asAddr );
                    return true;
                }
            }
            catch( DbgEngException )
            {
            }

            return false;
        } // end _TryConvertToUint()


        public override object Transform( EngineIntrinsics engineIntrinsics, object inputData )
        {
            object originalInputData = inputData;

            var pso = inputData as PSObject;
            if( null != pso )
            {
                inputData = pso.BaseObject;
            }

            if( inputData is DbgUModeThreadInfo )
            {
                return inputData;
            }

            if( null == inputData )
            {
                // Some commands CAN take a thread, but don't require it.
                return null;
            }

            var type = inputData.GetType();
            if( type.IsArray )
            {
                Array asArray = (Array) inputData;
                DbgUModeThreadInfo[] threads = new DbgUModeThreadInfo[ asArray.Length ];
                for( int i = 0; i < threads.Length; i++ )
                {
                    threads[ i ] = Transform( engineIntrinsics, asArray.GetValue( i ) ) as DbgUModeThreadInfo;
                    if( null == threads[ i ] )
                    {
                        // Array embedded within an array? Not going to handle that.
                        throw new ArgumentTransformationMetadataException( Util.Sprintf( "Could not convert item at index {0} to a thread.",
                                                                                         i ) );
                    }
                }
                return threads;
            } // end if( array )

            DbgUModeThreadInfo thread = null;
            uint tid;
            if( _TryConvertToUint( engineIntrinsics, originalInputData, out tid ) )
            {
                string path = engineIntrinsics.SessionState.Path.CurrentProviderLocation( DbgProvider.ProviderId ).ProviderPath;
                var debugger = DbgProvider.GetDebugger( path );

                try
                {
                    thread = debugger.GetUModeThreadByDebuggerId( tid );
                    return thread;
                }
                catch( DbgProviderException )
                {
                    // ignore
                }

                // Maybe it's a system tid?
                // I wonder if this "helpfulness" actually confuses things...
                try
                {
                    thread = debugger.GetThreadBySystemTid( tid );
                    return thread;
                }
                catch( DbgProviderException )
                {
                    // ignore
                }

                throw new ArgumentTransformationMetadataException( Util.Sprintf( "No such thread: {0}.",
                                                                                 tid ) );
            }

            throw CreateRecoverableAtme( "Could not convert '{0}' to a thread.",
                                         originalInputData );
        } // end Transform()
    } // end class ThreadTransformationAttribute


    [AttributeUsage( AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false )]
    public class ScriptBlockTransformationAttribute : ArgumentTransformationAttribute
    {
        public override object Transform( EngineIntrinsics engineIntrinsics, object inputData )
        {
            object originalInputData = inputData;

            var pso = inputData as PSObject;
            if( null != pso )
            {
                inputData = pso.BaseObject;
            }

            if( inputData is ScriptBlock )
            {
                return inputData;
            }

            if( inputData is String inputDataStr )
            {
                return ScriptBlock.Create( inputDataStr );
            }

            if( null == inputData )
            {
                return null;
            }

            throw CreateRecoverableAtme( "Could not convert '{0}' to a ScriptBlock.",
                                         originalInputData );
        } // end Transform()
    } // end class ScriptBlockTransformationAttribute
}

