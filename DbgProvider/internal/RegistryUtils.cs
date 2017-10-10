//-----------------------------------------------------------------------
// <copyright file="RegistryUtils.cs" company="Microsoft">
//      (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;
using System.Security;
using Microsoft.Win32;

// PreSharp says: "disable warnings about unknown message numbers and pragmas using at the top of your source file:"
#pragma warning disable 1634, 1691

namespace MS.Dbg
{

    //
    // N.B.
    //
    // Do not call into the LogManager from this class--RegistryUtils are used during
    // initialization of the LogManager.
    //

    internal static class RegistryUtils
    {
        /// <summary>
        ///    Creates and returns a new RegistryKey object representing the same registry
        ///    key as rk.
        /// </summary>
        /// <remarks>
        /// <note>
        ///    IMPORTANT: The caller is responsible for disposing of the returned
        ///    RegistryKey.
        /// </note>
        /// </remarks>
        /// <param name="rk">
        ///    The existing RegistryKey that represents the desired registry key to
        ///    represent with a new RegistryKey object.
        /// </param>
        /// <param name="writable">
        ///    Specifies whether the new RegistryKey object will be writable or not.
        /// </param>
        /// <returns>
        ///    A new RegistryKey object with representing the same registry key as
        ///    <paramref name="rk"/>.
        /// </returns>
        public static RegistryKey OpenKeyNew( RegistryKey rk, bool writable )
        {
            if( null == rk )
                throw new ArgumentNullException( "rk" );

// You can't have a registry key with a null Name. (If you could, it would be a bug, but in
// the RegistryKey class, not here.) Mail sent to pshbug on 12/08/2006.
#pragma warning suppress 56506
            int firstBackSlash = rk.Name.IndexOf( '\\' );
            string baseKeyName = rk.Name.Substring( 0, firstBackSlash );
            string subKeyName = rk.Name.Substring( firstBackSlash+1 );
            return _GetBaseKey( baseKeyName ).OpenSubKey( subKeyName, writable );
        } // end OpenKeyNew()


        private static RegistryKey _GetBaseKey( string baseKeyName )
        {
            switch( baseKeyName.ToUpperInvariant() )
            {
                case "HKEY_LOCAL_MACHINE":
                    return Registry.LocalMachine;
                case "HKEY_CURRENT_USER":
                    return Registry.CurrentUser;
                case "HKEY_USERS":
                    return Registry.Users;
                case "HKEY_CURRENT_CONFIG":
                    return Registry.CurrentConfig;
                case "HKEY_CLASSES_ROOT":
                    return Registry.ClassesRoot;
                case "HKEY_PERFORMANCE_DATA":
                    return Registry.PerformanceData;
                default:
                    throw new ArgumentException( String.Format( System.Globalization.CultureInfo.InvariantCulture, "baseKeyName (\"{0}\") is not a registry base key name.", baseKeyName ) );
            }
        } // end _GetBaseKey



        public static RegistryKey GetDbgProviderHklmKey()
        {
            return GetDbgProviderHklmKey( false );
        }

        public static RegistryKey GetDbgProviderHklmKey( bool writable )
        {
            return _GetDbgProviderRegKey( RegistryHive.LocalMachine, writable );
        }

        public static RegistryKey GetDbgProviderHkcuKey()
        {
            return GetDbgProviderHkcuKey( false );
        }

        public static RegistryKey GetDbgProviderHkcuKey( bool writable )
        {
            return _GetDbgProviderRegKey( RegistryHive.CurrentUser, writable );
        }

        private static string sm_regpath = @"SOFTWARE\DbgProvider";

        private static RegistryKey _GetDbgProviderRegKey( RegistryHive hive, bool writable )
        {
            using( RegistryKey hkxx = RegistryKey.OpenBaseKey( hive, RegistryView.Registry64 ) )
            {
                return OpenOrCreateKey( hkxx, sm_regpath, writable );
            }
        }


        // This throws all the exceptions that the OpenSubKey/CreateSubKey methods can
        // throw, plus DbgProviderException if the key cannot be created and no other
        // exception is thrown.
        public static RegistryKey OpenOrCreateKey( RegistryKey baseKey, string path, bool writable )
        {
            RegistryKey resultKey = baseKey.OpenSubKey( path, writable );
            if( resultKey == null )
            {
                using( baseKey.CreateSubKey( path ) ) { }
                resultKey = baseKey.OpenSubKey( path, writable );
                if( null == resultKey )
                {
                    // baseKey.CreateSubKey a few lines up must have failed. Pity that it
                    // doesn't throw an error based on the problem.
                    throw new DbgProviderException( Util.McSprintf( Resources.ErrMsgCannotCreateSubkeyFmt,
                                                                    baseKey.Name,
                                                                    path ),
                                                     "FailedToCreateKey",
                                                     ErrorCategory.NotInstalled );
                }
            }

            return resultKey;
        } // end OpenOrCreateKey()


        private static object sm_syncRoot = new object();
        // TODO: do I really need/want the cache?
        private static Dictionary< string, object > sm_cache = new Dictionary< string, object >();


        private static string _KeyForValName( RegistryHive hive, string valueName )
        {
            return hive.ToString() + ":" + valueName;
        } // end _KeyForValName()


        // Rather than throwing for access problems, this will just return null. (for
        // instance if the key does not exist and could not be created). (note that it
        // will still throw for "weird" errors, like IOException, because those should
        // generally not be handled)
        private static bool _TryGetRegValue< T >( RegistryHive hive,
                                                  string valueName,
                                                  out T t )
        {
            t = default( T );
            string key = _KeyForValName( hive, valueName );
            lock( sm_syncRoot )
            {
                object obj;
                if( !sm_cache.TryGetValue( key, out obj ) )
                {
                    Exception e = null;
                    try
                    {
                        using( RegistryKey rk = _GetDbgProviderRegKey( hive, false ) )
                        {
                            obj = rk.GetValue( valueName );
                            if( null == obj )
                            {
                                return false;
                            }

                            if( typeof( T ) != obj.GetType() )
                            {
                                throw new DbgProviderException( Util.McSprintf( Resources.ExMsgInvalidRegistryDataFmt,
                                                                                valueName ),
                                                                "InvalidRegistryData",
                                                                ErrorCategory.InvalidData );
                            }
                            sm_cache[ key ] = obj;
                            t = (T) obj;
                            return true;
                        }
                    }
                    catch( UnauthorizedAccessException uae ) { e = uae; }
                    catch( SecurityException se ) { e = se; }
                    catch( DbgProviderException dpe ) { e = dpe; }

                    Util.Assert( null != e );
                    return false;
                }
                else
                {
                    t = (T) obj;
                    return true;
                }
            }
        } // end _TryGetRegValue()


        // Rather than throwing for access problems, this will just use the supplied
        // default value (for instance if the key does not exist and could not be
        // created). (note that it will still throw for "weird" errors, like IOException)
        private static T _GetRegValue< T >( RegistryHive hive,
                                            string valueName,
                                            T defaultValue,
                                            Func< T, T > normalize )
        {
            T val;
            if( !_TryGetRegValue( hive, valueName, out val ) )
            {
                string key = _KeyForValName( hive, valueName );
                sm_cache[ key ] = val = defaultValue;
            }
            return normalize( val );
        }

        public static T GetHklmRegValue< T >( string valueName, T defaultValue )
        {
            return GetHklmRegValue< T >( valueName, defaultValue, (t) => t );
        }

        public static T GetHklmRegValue< T >( string valueName, T defaultValue, Func< T, T > normalize )
        {
            return _GetRegValue< T >( RegistryHive.LocalMachine, valueName, defaultValue, normalize );
        }

        public static T GetHkcuRegValue< T >( string valueName, T defaultValue )
        {
            return GetHkcuRegValue< T >( valueName, defaultValue, (t) => t );
        }

        public static T GetHkcuRegValue< T >( string valueName, T defaultValue, Func< T, T > normalize )
        {
            return _GetRegValue( RegistryHive.CurrentUser, valueName, defaultValue, normalize );
        }


        private static void _SetRegValue< T >( RegistryHive hive, string valueName, T val )
        {
            string key = _KeyForValName( hive, valueName );
            using( RegistryKey rk = _GetDbgProviderRegKey( hive, true ) )
            {
                lock( sm_syncRoot )
                {
                    rk.SetValue( valueName, val );
                    if( sm_cache.ContainsKey( key ) )
                    {
                        sm_cache[ key ] = val;
                    }
                    sm_cache.Remove( valueName );
                }
            }
        } // end _SetRegValue()

        public static void SetHkcuRegValue< T >( string valueName, T val )
        {
            _SetRegValue( RegistryHive.CurrentUser, valueName, val );
        } // end SetHkcuRegValue()


        //
        // The following overloads will look in multiple hives until it finds the value.
        //

        // HKCU takes priority (user preference).
        private static readonly RegistryHive[] sm_defaultHiveOrder = new RegistryHive[] { RegistryHive.CurrentUser,
                                                                                          RegistryHive.LocalMachine };

        public static T GetRegValue< T >( string valueName, T defaultValue )
        {
            return GetRegValue< T >( valueName, defaultValue, (t) => t );
        }

        public static T GetRegValue< T >( string valueName, T defaultValue, Func< T, T > normalize )
        {
            return GetRegValue< T >( sm_defaultHiveOrder, valueName, defaultValue, normalize );
        }

        public static T GetRegValue< T >( RegistryHive[] hivesInPriorityOrder, string valueName, T defaultValue, Func< T, T > normalize )
        {
            T val;
            string key = valueName;
            object obj;
            if( sm_cache.TryGetValue( key, out obj ) )
            {
                return normalize( (T) obj );
            }

            foreach( RegistryHive hive in hivesInPriorityOrder )
            {
                if( _TryGetRegValue( hive, valueName, out val ) )
                {
                    sm_cache[ key ] = val;
                    return normalize( val );
                }
            }
            sm_cache[ key ] = defaultValue;
            return normalize( defaultValue );
        } // end GetRegValue()
    } // end class RegistryUtils

}
