using System;
using System.Reflection;
using System.Collections.Generic;

namespace MS.Dbg
{
    internal static class PluginManager
    {
        private static List< IDbgDerivedTypeDetectionPlugin > sm_dtdPlugins;


        private static void _EnsureInitialized()
        {
            if( null != sm_dtdPlugins )
                return;

            LogManager.Trace( "Initializing plugins." );

            sm_dtdPlugins = new List< IDbgDerivedTypeDetectionPlugin >();
            sm_dtdPlugins.Add( new DefaultDerivedTypeDetectionPlugin() );

            // TODO: Load other plugins from plugin directory.
        } // end _EnsureInitialized()


        public static bool TryFindPossibleOffsetFromDerivedClass( DbgEngDebugger debugger,
                                                                  ulong vtableAddr,
                                                                  ulong firstSlotPtr,
                                                                  DbgUdtTypeInfo derivedType,
                                                                  DbgUdtTypeInfo baseType,
                                                                  out int offset )
        {
            _EnsureInitialized();

            foreach( var plugin in sm_dtdPlugins )
            {
                using( var logger = new LogAdapter( plugin.Name ) )
                {
                    if( plugin.TryFindPossibleOffsetFromDerivedClass( debugger,
                                                                      logger,
                                                                      vtableAddr,
                                                                      firstSlotPtr,
                                                                      derivedType,
                                                                      baseType,
                                                                      out offset ) )
                    {
                        LogManager.Trace( "Possible offset from derived class ({0}{1:x}) found by plugin: {2}",
                                          offset > 0 ? "0x" : "",
                                          offset,
                                          plugin.Name );
                        return true;
                    }
                }
            } // end foreach( plugin )

            // No plugin could figure it out.
            offset = 0;
            return false;
        } // end TryFindPossibleOffsetFromDerivedClass()


        private static Assembly _TryLoadAssembly( string path, IPipelineCallback pipeline )
        {
            Exception e = null;
            try
            {
                return Assembly.LoadFrom( path );
            }
            catch( BadImageFormatException bife ) { e = bife; }
            catch( System.IO.IOException ioe ) { e = ioe; }
            catch( System.Security.SecurityException se ) { e = se; }

            var dpe = new DbgProviderException( Util.Sprintf( "Could not load {0}: {1}",
                                                              System.IO.Path.GetFileName( path ),
                                                              Util.GetExceptionMessages( e ) ),
                                                "PluginLoadFailed",
                                                System.Management.Automation.ErrorCategory.OpenError,
                                                e,
                                                path );
            try { throw dpe; } catch( Exception ) { } // give it a stack
            pipeline.WriteError( dpe );
            return null;
        } // end _TryLoadAssembly()


        public static void LoadPlugin( string path, IPipelineCallback pipeline )
        {
            _EnsureInitialized();

            pipeline.WriteVerbose( "Looking for plugin types in: {0}", path );
            Assembly asm = _TryLoadAssembly( path, pipeline );
            if( null == asm )
                return; // already wrote an error

            Type dtdPluginInterface = typeof( IDbgDerivedTypeDetectionPlugin );
            foreach( var candidateType in asm.GetExportedTypes() )
            {
                if( dtdPluginInterface.IsAssignableFrom( candidateType ) )
                {
                    pipeline.WriteVerbose( "Found Derived Type Detection plugin type: {0}", candidateType.FullName );
                    var constructor = candidateType.GetConstructor( Type.EmptyTypes );
                    if( null == constructor )
                    {
                        DbgProviderException dpe = new DbgProviderException( Util.Sprintf( "The plugin type '{0}' from '{1}' cannot be used because it has no public, parameterless constructor.",
                                                                                           candidateType.FullName,
                                                                                           path ),
                                                                             "PluginTypeMissingDefaultConstructor",
                                                                             System.Management.Automation.ErrorCategory.InvalidData,
                                                                             candidateType );
                        try { throw dpe; } catch( Exception ) { } // give it a stack
                        pipeline.WriteError( dpe );
                    }

                    try
                    {
                        var plugin = (IDbgDerivedTypeDetectionPlugin) constructor.Invoke( null );
                        sm_dtdPlugins.Add( plugin );
                        pipeline.WriteVerbose( "Registered Derived Type Detection plugin: {0}", plugin.Name );
                    }
                    catch( Exception e ) // I hate this.
                    {
                        DbgProviderException dpe = new DbgProviderException( Util.Sprintf( "The plugin type '{0}' from '{1}' cannot be used because its constructor threw an exception: {2}",
                                                                                           candidateType.FullName,
                                                                                           path,
                                                                                           Util.GetExceptionMessages( e ) ),
                                                                             "PluginTypeConstructorThrew",
                                                                             System.Management.Automation.ErrorCategory.OpenError,
                                                                             e,
                                                                             candidateType );
                        try { throw dpe; } catch( Exception ) { } // give it a stack
                        pipeline.WriteError( dpe );
                    }
                } // end if( it's a DTD plugin type )
            } // end foreach( candidateType )
        } // end LoadPlugin()
    } // end class PluginManager
}

