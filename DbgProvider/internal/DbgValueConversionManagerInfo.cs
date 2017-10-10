using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace MS.Dbg
{

    public partial class DbgValueConversionManager
    {
        //
        // This class is used to store information for the value conversion manager.
        //
        // (which is essentially ALL of its information)
        //
        private class DbgValueConversionManagerInfo
        {
            private const string c_NoModule = "<nomodule>";

            private ScriptLoader m_scriptLoader;

            // m_moduleMap: maps module names to:
            //   Dictionary< string, TypeNameMatchList >: map template name to TypeNameMatchList
            //      TypeNameMatchList: templates with no multi-match wildcards
            //                            ordered list of pair< number single-match wildcards, list of templates in order registered >
            //                         templates with multi-match wildcards
            //                            ordered list of pair< number single-match wildcards, list of templates in order registered >

            // Maps:            Module          Templatename    Match list
            private Dictionary< string, Dictionary< string, TypeNameMatchList< DbgValueConverterInfo > > > m_moduleMap
                = new Dictionary< string, Dictionary< string, TypeNameMatchList< DbgValueConverterInfo > > >( StringComparer.OrdinalIgnoreCase );

            internal void RegisterConverter( DbgValueConverterInfo converterInfo )
            {
                if( null == converterInfo )
                    throw new ArgumentNullException( "converterInfo" );

                m_scriptLoader.AddSourceFile( converterInfo.SourceScript );

                string mod;
                if( String.IsNullOrEmpty( converterInfo.ScopingModule ) )
                    mod = c_NoModule;
                else
                    mod = converterInfo.ScopingModule;

                var templateMap = GetOrCreateValue( m_moduleMap,
                                                    mod,
                                                    () => new Dictionary< string, TypeNameMatchList< DbgValueConverterInfo > >( StringComparer.OrdinalIgnoreCase ) );

                var matchList = GetOrCreateValue( templateMap, converterInfo.TypeName.TemplateName );

                if( !matchList.AddOrReplace( converterInfo ) )
                {
                    LogManager.Trace( "Replaced existing converter for: {0}", converterInfo.TypeName );
                }
            } // end RegisterConverter()

            // TODO: Make these extension methods for use elsewhere?
            private static TValue GetOrCreateValue< TKey, TValue >( Dictionary< TKey, TValue > dict,
                                                                    TKey key ) where TValue : new()
            {
                return GetOrCreateValue( dict, key, () => new TValue() );
            }

            private static TValue GetOrCreateValue< TKey, TValue >( Dictionary< TKey, TValue > dict,
                                                                    TKey key,
                                                                    Func< TValue > createValue )
            {
                TValue result;
                if( !dict.TryGetValue( key, out result ) )
                {
                    result = createValue();
                    dict.Add( key, result );
                }
                return result;
            } // end GetOrCreateValue()


            private static TValue TryGetNonNullValue< TKey, TValue >( Dictionary< TKey, TValue > dict,
                                                                      TKey key ) where TValue : class
            {
                TValue result;
                dict.TryGetValue( key, out result );
                return result;
            } // end GetOrCreateValue()


            /// <summary>
            ///    Picks a converter for a given DbgSymbol.
            /// </summary>
            internal DbgValueConverterInfo ChooseConverterForSymbol( DbgSymbol symbol )
            {
                if( null == symbol )
                    throw new ArgumentNullException( "symbol" );

                var converter = _ChooseConverter( symbol.Type.Module.Name, symbol );
                if( null != converter )
                    return converter;

                return _ChooseConverter( c_NoModule, symbol );
            } // end ChooseConverterForSymbol()


            private DbgValueConverterInfo _ChooseConverter( string modName, DbgSymbol symbol )
            {
                return _EnumerateAllConvertersForSymbol( modName, symbol ).FirstOrDefault();
            } // end _ChooseConverter()


            internal IEnumerable< DbgValueConverterInfo > EnumerateAllConvertersForSymbol( DbgSymbol symbol )
            {
                if( symbol.Module.BaseAddress != 0 )
                {
                    Util.Assert( !String.IsNullOrEmpty( symbol.Module.Name ) );
                    foreach( var converter in _EnumerateAllConvertersForSymbol( symbol.Module.Name, symbol ) )
                    {
                        yield return converter;
                    }
                }

                foreach( var converter in _EnumerateAllConvertersForSymbol( c_NoModule, symbol ) )
                {
                    yield return converter;
                }
            } // end EnumerateAllConvertersForSymbol()


            private IEnumerable< DbgValueConverterInfo > _EnumerateAllConvertersForSymbol( string modName,
                                                                                           DbgSymbol symbol )
            {
                if( null == symbol )
                    throw new ArgumentNullException( "symbol" );

                var templateMap = TryGetNonNullValue( m_moduleMap, modName );
                if( null == templateMap )
                    yield break;

                foreach( var typeNameTemplate in symbol.GetTemplateNodes() )
                {
                    var matchList = TryGetNonNullValue( templateMap, typeNameTemplate.TemplateName );
                    if( null != matchList )
                    {
                        var converter = matchList.TryFindMatchingItem( typeNameTemplate );
                        if( null != converter )
                            yield return converter;
                    }
                }
            } // end _EnumerateAllConvertersForSymbol()


            internal IEnumerable< DbgValueConverterInfo > EnumerateEntries()
            {
                foreach( var modEntries in m_moduleMap.Values )
                {
                    foreach( var tml in modEntries.Values )
                    {
                        foreach( var converter in tml )
                        {
                            yield return converter;
                        }
                    }
                }
            } // end GetEntries()


            private IEnumerable< Dictionary< string, TypeNameMatchList< DbgValueConverterInfo > > >
            _GetMatchingModuleDictionaries( string modFilter )
            {
                if( null == modFilter )
                    modFilter = "*";

                if( WildcardPattern.ContainsWildcardCharacters( modFilter ) )
                {
                    WildcardPattern wp = new WildcardPattern( modFilter,
                                                              WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant );
                    foreach( var mod in m_moduleMap.Keys )
                    {
                        if( !wp.IsMatch( mod ) )
                            continue;

                        yield return m_moduleMap[ mod ];
                    }
                    yield break;
                } // end if( modFilter wildcard used )

                if( 0 == modFilter.Length )
                {
                    yield return m_moduleMap[ c_NoModule ];
                    yield break;
                }

                Dictionary< string, TypeNameMatchList< DbgValueConverterInfo > > modEntry;
                if( m_moduleMap.TryGetValue( modFilter, out modEntry ) )
                {
                    yield return modEntry;
                }
            } // end _GetMatchingModuleDictionaries()


            internal IEnumerable< DbgValueConverterInfo > GetMatchingEntries( string modFilter,
                                                                              string templateFilter,
                                                                              bool exactMatchOnly )
            {
                if( null == modFilter )
                    modFilter = "*";

                bool modFilterHasWildcards = WildcardPattern.ContainsWildcardCharacters( modFilter );

                int dictsFound = 0;
                foreach( var modDict in _GetMatchingModuleDictionaries( modFilter ) )
                {
                    dictsFound++;

                    foreach( var converter in _GetMatchingEntriesForModule( modDict,
                                                                            templateFilter,
                                                                            exactMatchOnly,
                                                                            !modFilterHasWildcards ) )
                    {
                        yield return converter;
                    }
                }

                if( !modFilterHasWildcards && (0 == dictsFound) )
                {
                    // TODO: Better error? Or maybe we shouldn't throw if templateFilter has wildcards...
                    throw new DbgProviderException( Util.Sprintf( "No DbgValueConverterInfo objects registered for module {0}.",
                                                                  modFilter ),
                                                    "NoConvertersForModule",
                                                    ErrorCategory.ObjectNotFound,
                                                    modFilter );
                }
            } // end GetMatchingEntries()


            private IEnumerable< DbgValueConverterInfo >
            _GetMatchingEntriesForModule( Dictionary< string, TypeNameMatchList< DbgValueConverterInfo > > entryDict,
                                          string templateFilter,
                                          bool exactMatchOnly,
                                          bool throwIfNoResult )
            {
                if( String.IsNullOrEmpty( templateFilter ) )
                {
                    if( exactMatchOnly )
                        throw new ArgumentException( "You asked for exact matches only, but gave no templateFilter." );

                    foreach( var tml in entryDict.Values )
                    {
                        foreach( var converter in tml )
                            yield return converter;
                    }
                }
                else
                {
                    TypeNameMatchList< DbgValueConverterInfo > tml;
                    DbgTemplateNode crackedName = DbgTemplate.CrackTemplate( templateFilter );
                    if( !entryDict.TryGetValue( crackedName.TemplateName, out tml ) )
                    {
                        if( throwIfNoResult )
                        {
                            throw new DbgProviderException( Util.Sprintf( "Could not find converter info matching '{0}'.",
                                                                          templateFilter ),
                                                            "NoConverterForTemplateFilter",
                                                            ErrorCategory.ObjectNotFound,
                                                            templateFilter );
                        }
                    }
                    else
                    {
                        if( exactMatchOnly )
                        {
                            int count = 0;
                            foreach( var converter in tml.FindMatchingItemsExact( crackedName ) )
                            {
                                count++;
                                yield return converter;
                            }
                            Util.Assert( count <= 1 ); // shouldn't be able to have more than one exact match
                        }
                        else
                        {
                            foreach( var converter in tml.FindMatchingItems( crackedName ) )
                                yield return converter;
                        }
                    }
                }
            } // end _GetMatchingEntriesForModule()


            private void _Dump()
            {
                // TODO: BUGBUG: We should probably not drop converters that have no
                // source script. (because we can't reload them!) What converters might
                // not have a source script? Something entered at the command line, or
                // a converter written in C#.
                m_moduleMap.Clear();
            } // end _Dump()


            public void Reload( CommandInvocationIntrinsics invokeCommand,
                                IList< string > appendScripts,
                                IList< string > prependScripts,
                                IPipelineCallback pipe )
            {
                m_scriptLoader.Reload( invokeCommand, appendScripts, prependScripts, "converter info", pipe );
            } // end Reload()


            public void RemoveEntries( string modFilter,
                                       string templateFilter,
                                       bool exactMatchOnly )
            {
                // TODO: Does modFilter interact correctly with exactMatchOnly=true?
                var removeUs = GetMatchingEntries( modFilter,
                                                   templateFilter,
                                                   exactMatchOnly ).ToArray();
                foreach( var removeMe in removeUs )
                {
                    Dictionary< string, TypeNameMatchList< DbgValueConverterInfo > > dict;
                    if( String.IsNullOrEmpty( removeMe.ScopingModule ) )
                        dict = m_moduleMap[ c_NoModule ];
                    else
                        dict = m_moduleMap[ removeMe.ScopingModule ];

                    var tnml = dict[ templateFilter ];
                    tnml.Remove( removeMe );
                }

                // Alternate approach.
             // foreach( var modDict in _GetMatchingModuleDictionaries( modFilter ) )
             // {
             //     modDict[ templateFilter ].Remove
             // }
            } // end RemoveEntries()


            /// <summary>
            ///    Removes any script file entries that have no associated converters.
            /// </summary>
            public void ScrubFileList()
            {
                m_scriptLoader.SetFileList( EnumerateEntries().Select( ( x ) => x.SourceScript ) );
            } // end ScrubFileList()


            internal DbgValueConversionManagerInfo()
            {
                m_scriptLoader = new ScriptLoader( _Dump );
            } // end constructor
        } // end class DbgValueConversionManagerInfo
    } // end partial class DbgValueConversionManager
}

