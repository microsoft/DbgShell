using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Provider;
using MS.Dbg.Commands;

namespace MS.Dbg.Formatting
{

    public static partial class AltFormattingManager
    {
        //
        // This class is used to store per-runspace information for the formatting provider.
        //
        // (which is essentially ALL of its information)
        //
        private class AltFormattingManagerInfo
        {
            // Maps type names to ViewDefinitionInfos.
            private Dictionary< string, List< ViewDefinitionInfo > > m_map = new Dictionary< string, List< ViewDefinitionInfo > >( StringComparer.OrdinalIgnoreCase );

            // Maps generic type names to ViewDefinitionInfos.
            private Dictionary< string, TypeNameMatchList< GenericViewDefinitionInfoList > > m_genericsMap = new Dictionary< string, TypeNameMatchList< GenericViewDefinitionInfoList > >( StringComparer.OrdinalIgnoreCase );

            private ScriptLoader m_scriptLoader;


            public event EventHandler< AltTypeFormatEntry > ViewDefinitionRegistered;


            private void _RaiseViewRegistered( AltTypeFormatEntry typeEntry )
            {
                EventHandler< AltTypeFormatEntry > handler = ViewDefinitionRegistered;
                if( null != handler )
                {
                    handler( null, typeEntry );
                }
            } // end _RaiseViewRegistered()


            internal void RegisterViewDefinition( AltTypeFormatEntry typeEntry )
            {
                if( typeEntry.ViewDefinitions.Count > 0 )
                {
                    // It will be the same for all the views in the same AltTypeFormatEntry,
                    // so we can just use the first one.
                    m_scriptLoader.AddSourceFile( typeEntry.ViewDefinitions[ 0 ].SourceScript );
                }

                if( DbgTemplate.LooksLikeATemplateName( typeEntry.TypeName ) )
                {
                    _RegisterGenericViewDefinition( typeEntry );
                }
                else
                {
                    _RegisterViewDefinition( typeEntry );
                }
            } // end RegisterViewDefinition()

            private void _RegisterViewDefinition( AltTypeFormatEntry typeEntry )
            {
                List< ViewDefinitionInfo > views;
                if( !m_map.TryGetValue( typeEntry.TypeName, out views ) )
                {
                    views = new List< ViewDefinitionInfo >( typeEntry.ViewDefinitions );
                    m_map.Add( typeEntry.TypeName, views );
                    // TODO: check for duplicate view types. Then, TODO: how to warn that
                    // they are ignored. Pass an IPipelineCallback, I guess.
                    _RaiseViewRegistered( typeEntry );
                    return;
                }

                // Rather than just blindly adding to the list, we check to see if
                // we already have views of the same type, and if so, update them.

                var updatedInPlace = new HashSet< ViewDefinitionInfo >();

                for( int i = 0; i < views.Count; i++ )
                {
                    foreach( var view in typeEntry.ViewDefinitions )
                    {
                        if( views[ i ].ViewDefinition.GetType() == view.ViewDefinition.GetType() )
                        {
                            // In-place update.
                            views[ i ] = view;
                            updatedInPlace.Add( view );
                        }
                    }
                }

                if( 0 == updatedInPlace.Count )
                {
                    views.AddRange( typeEntry.ViewDefinitions );
                }
                else
                {
                    foreach( var view in typeEntry.ViewDefinitions )
                    {
                        if( !updatedInPlace.Contains( view ) )
                            views.Add( view );
                    }
                }

                _RaiseViewRegistered( typeEntry );
            } // end _RegisterViewDefinition()


            private void _RegisterGenericViewDefinition( AltTypeFormatEntry typeEntry )
            {
                var template = DbgTemplate.CrackTemplate( typeEntry.TypeName );
                TypeNameMatchList< GenericViewDefinitionInfoList > existingViews;
                if( !m_genericsMap.TryGetValue( template.TemplateName, out existingViews ) )
                {
                    //views = new TypeNameMatchList< GenericViewDefinitionInfoList >( typeEntry.TypeName, typeEntry.ViewDefinitions );
                    existingViews = new TypeNameMatchList< GenericViewDefinitionInfoList >();
                    if( !existingViews.AddOrReplace( new GenericViewDefinitionInfoList( template, typeEntry.ViewDefinitions ) ) )
                    {
                        LogManager.Trace( "Replacing existing view definitions for: {0}",
                                          template );
                    }
                    m_genericsMap.Add( template.TemplateName, existingViews );
                    // TODO: check for duplicate view types. Then, TODO: how to warn that
                    // they are ignored. Pass an IPipelineCallback, I guess.
                    _RaiseViewRegistered( typeEntry );
                    return;
                }

                // Rather than just blindly adding to the list, we check to see if
                // we already have views of the same type, and if so, update them.

                var updatedInPlace = new HashSet< ViewDefinitionInfo >();

                //existingViews.UpdateItemsExact( template, ( existingViewList ) =>
                foreach( var existingViewList in existingViews.FindMatchingItemsExact( template ) )
                {
                    for( int i = 0; i < existingViewList.Views.Count; i++  )
                        foreach( var newView in typeEntry.ViewDefinitions )
                        {
                            if( existingViewList.Views[ i ].ViewDefinition.GetType() == newView.ViewDefinition.GetType() )
                            {
                                // In-place update.
                                existingViewList.Views[ i ] = newView;
                                updatedInPlace.Add( newView );
                            }
                        }
                }

                GenericViewDefinitionInfoList list;
                if( 0 == updatedInPlace.Count )
                {
                    list = new GenericViewDefinitionInfoList( template, typeEntry.ViewDefinitions );
                }
                else
                {
                    list = new GenericViewDefinitionInfoList( template, typeEntry.ViewDefinitions.Count - updatedInPlace.Count );
                    foreach( var view in typeEntry.ViewDefinitions )
                    {
                        if( !updatedInPlace.Contains( view ) )
                            list.Views.Add( view );
                    }
                }
                if( !existingViews.AddOrReplace( list ) )
                {
                    LogManager.Trace( "Replacing existing view definition list for: {0}", list.TypeName );
                }

                _RaiseViewRegistered( typeEntry );
            } // end _RegisterGenericViewDefinition()


            internal bool RemoveByName( string typeName )
            {
                if( DbgTemplate.LooksLikeATemplateName( typeName ) )
                {
                    var template = DbgTemplate.CrackTemplate( typeName );
                    return m_genericsMap.Remove( template.TemplateName );
                }
                else
                {
                    return m_map.Remove( typeName );
                }
                // Note that we do not have the opposite of _RaiseViewRegistered. But I
                // don't think it's needed.
            } // end RemoveByName()


            /// <summary>
            ///    Removes any script file entries that have no associated views.
            /// </summary>
            public void ScrubFileList()
            {
                m_scriptLoader.SetFileList( GetEntries().SelectMany( ( x ) => x.Value ).Select( ( y ) => y.SourceScript ) );
            } // end ScrubFileList()


            private IEnumerable< AltTypeFormatEntryPair > _EnumerateEntriesForTypeNames( IEnumerable< string > typeNames )
            {
                if( null == typeNames )
                    throw new ArgumentNullException( "typeNames" );

                List< ViewDefinitionInfo > views = null;
                foreach( var rawName in typeNames )
                {
                    bool isGenericOrTemplate;
                    string typeName = Util.MassageManagedTypeName( rawName, out isGenericOrTemplate );
                    if( !isGenericOrTemplate )
                        isGenericOrTemplate = DbgTemplate.LooksLikeATemplateName( typeName );

                    if( isGenericOrTemplate )
                    {
                        var template = DbgTemplate.CrackTemplate( typeName );
                        TypeNameMatchList< GenericViewDefinitionInfoList > tnml;
                        if( m_genericsMap.TryGetValue( template.TemplateName, out tnml ) )
                        {
                            foreach( var gvdil in tnml.FindMatchingItems( template ) )
                            {
                                foreach( var vdi in gvdil.Views )
                                {
                                    yield return new AltTypeFormatEntryPair( typeName, vdi );
                                }
                            }
                        }
                    }
                    else
                    {
                        if( m_map.TryGetValue( typeName, out views ) )
                        {
                            foreach( var vdi in views )
                            {
                                yield return new AltTypeFormatEntryPair( typeName, vdi );
                            }
                        }
                    } // end else( not generic )
                } // end foreach( typeName )
            } // end _EnumerateEntriesForTypeNames()


            /// <summary>
            ///    Picks a view definitions for a given set of type names (such as from
            ///    PSObject.TypeNames), optionally filtered by view definition type. N.B.
            ///    Does not return a single-line view definition unless specifically
            ///    requested by formatInfoType.
            /// </summary>
            internal ViewDefinitionInfo ChooseFormatInfoForTypeNames( IEnumerable< string > typeNames, Type formatInfoType  )
            {
                if( null == formatInfoType )
                {
                    // We filter out AltSingleLineViewDefinition entries if you don't specifically ask for them.
                    var fep = _EnumerateEntriesForTypeNames( typeNames ).FirstOrDefault(
                        ( fep_ ) => !typeof( AltSingleLineViewDefinition ).IsAssignableFrom( fep_.ViewDefinitionInfo.ViewDefinition.GetType() ) );

                    if( null != fep )
                        return fep.ViewDefinitionInfo;
                    else
                        return null;
                }
                else
                {
                    var fep = _EnumerateEntriesForTypeNames( typeNames ).FirstOrDefault(
                        // TODO: should I include derived types here too (by using IsAssignableFrom)?
                        ( fep_ ) => fep_.ViewDefinitionInfo.ViewDefinition.GetType() == formatInfoType );

                    if( null != fep )
                        return fep.ViewDefinitionInfo;
                    else
                        return null;
                }
            } // end ChooseFormatInfoForTypeNames()


            /// <summary>
            ///    Enumerates view definitions for a given type names, optionally filtered
            ///    by view definition type. N.B. Does not filter out single-line view
            ///    definitions.
            /// </summary>
            internal IEnumerable< AltTypeFormatEntryPair > EnumerateAllFormatInfoForTypeNames( IEnumerable< string > typeNames,
                                                                                               Type formatInfoType  )
            {
                if( null == formatInfoType )
                {
                    // N.B. ChooseFormatInfoForTypeNames filters out AltSingleLineViewDefinition
                    // entries if you don't specifically ask for them, but THIS method does not.
                    return _EnumerateEntriesForTypeNames( typeNames );
                }
                else
                {
                    return _EnumerateEntriesForTypeNames( typeNames ).Where(
                        // TODO: should I include derived types here too (by using IsAssignableFrom)?
                        ( fep ) => fep.ViewDefinitionInfo.ViewDefinition.GetType() == formatInfoType );
                }
            } // end EnumerateAllFormatInfoForTypeNames()


            // Works well with the PowerShell ETS: Use the PSObject.TypeNames field to
            // find a suitable view definition object.
            internal ViewDefinitionInfo ChooseSingleLineFormatInfoForTypeNames( IEnumerable< string > typeNames )
            {
                return ChooseFormatInfoForTypeNames( typeNames, typeof( AltSingleLineViewDefinition ) );
            } // end ChooseSingleLineFormatInfoForTypeNames()


            internal IReadOnlyDictionary< string, IReadOnlyList< ViewDefinitionInfo > > GetEntries()
            {
                // This is sadly inefficient, but the interfaces lack the necessary co-/contra-variance
                // to allow a direct cast. I wouldn't want to expose such a casted object directly
                // to PowerShell anyway, since it would appear as its true, modifiable type.
                int count = m_map.Count;
                count += m_genericsMap.Values.SelectMany( ( tnml ) => { return tnml.Select( ( gvdil ) => { return gvdil.Views; } ); } ).Count();

                Dictionary< string, IReadOnlyList< ViewDefinitionInfo > > copy
                    = new Dictionary< string, IReadOnlyList< ViewDefinitionInfo > >( count, m_map.Comparer );

                foreach( string key in m_map.Keys )
                {
                    copy[ key ] = m_map[ key ].AsReadOnly();
                }

                foreach( var gvdil in m_genericsMap.Values.SelectMany( (tnml) => { return tnml.AsEnumerable(); } ) )
                {
                    copy[ gvdil.TypeName.FullName ] = gvdil.Views.AsReadOnly();
                }

                return copy;
            } // end GetEntries()


            internal IReadOnlyDictionary< string, IReadOnlyList< ViewDefinitionInfo > > GetEntriesOfType( Type formatInfoType )
            {
                if( null == formatInfoType )
                    throw new ArgumentNullException( "formatInfoType" );

                int count = m_map.Count;
                count += m_genericsMap.Values.SelectMany( ( tnml ) => { return tnml.Select( ( gvdil ) => { return gvdil.Views; } ); } ).Count();

                Dictionary< string, IReadOnlyList< ViewDefinitionInfo > > copy
                    = new Dictionary< string, IReadOnlyList< ViewDefinitionInfo > >( count, m_map.Comparer );

                foreach( string key in m_map.Keys )
                {
                    var matchingViewDefs = new List< ViewDefinitionInfo >();
                    foreach( var viewDef in m_map[ key ] )
                    {
                        if( viewDef.ViewDefinition.GetType() == formatInfoType )
                            matchingViewDefs.Add( viewDef );
                    }
                    if( 0 != matchingViewDefs.Count )
                        copy[ key ] = matchingViewDefs.AsReadOnly();
                }

                foreach( var gvdil in m_genericsMap.Values.SelectMany( (tnml) => { return tnml.AsEnumerable(); } ) )
                {
                    string key = gvdil.TypeName.FullName;
                    var matchingViewDefs = new List< ViewDefinitionInfo >();
                    foreach( var viewDef in gvdil.Views )
                    {
                        if( viewDef.ViewDefinition.GetType() == formatInfoType )
                            matchingViewDefs.Add( viewDef );
                    }
                    if( 0 != matchingViewDefs.Count )
                        copy[ key ] = matchingViewDefs.AsReadOnly();
                }

                return copy;
            } // end GetEntriesOfType()


            internal void Dump( bool dumpViewsWithNoSource )
            {
                if( dumpViewsWithNoSource )
                {
                    m_map.Clear();
                    return;
                }

                List< ViewDefinitionInfo > newList = null;
                var keysToRemove = new List< string >( m_map.Count );
                foreach( string typeKey in m_map.Keys )
                {
                    var oldList = m_map[ typeKey ];
                    foreach( var viewInfo in oldList )
                    {
                        // If the entry has no source script (like someone typed it in on
                        // the command line, we'll have no way to reload it, so keep it.
                        if( String.IsNullOrEmpty( viewInfo.SourceScript ) )
                        {
                            if( null == newList )
                                newList = new List< ViewDefinitionInfo >();

                            newList.Add( viewInfo );
                        }
                    }
                    if( null != newList )
                    {
                        Util.Assert( newList.Count > 0 );
                        m_map[ typeKey ] = newList;
                        newList = null;
                    }
                    else
                    {
                        keysToRemove.Add( typeKey );
                    }
                }

                foreach( string typeKey in keysToRemove )
                {
                    m_map.Remove( typeKey );
                }

                // TODO: m_genericsMap
            } // end Dump()


            internal void Reload( CommandInvocationIntrinsics invokeCommand,
                                  IList< string > appendScripts,
                                  IList< string > prependScripts,
                                  IPipelineCallback pipe )
            {
                m_scriptLoader.Reload( invokeCommand, appendScripts, prependScripts, "format data", pipe );
            } // end Reload()


            internal AltFormattingManagerInfo()
            {
                m_scriptLoader = new ScriptLoader( () => Dump( false ) );
            } // end constructor
        } // end class AltFormattingManagerInfo
    } // end partial class AltFormattingManager
}

