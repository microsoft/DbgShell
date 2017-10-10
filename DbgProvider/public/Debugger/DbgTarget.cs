using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace MS.Dbg
{
    public class DbgTarget : DebuggerObject,
                             ICanSetContext,
                             IEquatable< DbgTarget >,
                             IComparable< DbgTarget >
    {
        internal DbgTarget( DbgEngDebugger debugger, DbgEngContext context )
            : this( debugger, context, null )
        {
        }


        internal DbgTarget( DbgEngDebugger debugger,
                            DbgEngContext context,
                            string targetFriendlyName )
            : base( debugger )
        {
            if( (bool) context.IsKernelContext )
                Context = context.AsSystemContext();
            else
                Context = context.AsProcessContext();

            IsLive = Debugger.IsLive;
            m_targetFriendlyName = targetFriendlyName;
        } // end constructor


        [NsLeafItem]
        public bool IsLive { get; private set; }


        [NsLeafItem]
        public uint DbgEngSystemId { get { return Context.SystemIndex; } }


        private bool? m_is32Bit;

        [NsLeafItem]
        public bool Is32Bit
        {
            get
            {
                if( null == m_is32Bit )
                {
                    using( new DbgEngContextSaver( Debugger, Context ) )
                    {
                        m_is32Bit = Debugger.QueryTargetIs32Bit();
                    }
                }
                return (bool) m_is32Bit;
            }
        } // end property Is32Bit


        [NsLeafItem]
        public bool IsKernel
        {
            get
            {
                Util.Assert( Context.IsKernelContext.HasValue );
                return Context.IsKernelContext.Value;
            }
        }


        private string m_targetFriendlyName;

        public string TargetFriendlyName
        {
            get
            {
                if( String.IsNullOrEmpty( m_targetFriendlyName ) )
                {
                    DbgUModeProcess dup = this as DbgUModeProcess;
                    if( (Context.ProcessIndexOrAddress != DEBUG_ANY_ID) &&
                        (Context.ProcessIndexOrAddress != 0xf0f0f0f0) )
                    {
                        // User-mode target
                        m_targetFriendlyName = Util.Sprintf( "User_target_{0}_Proc_{1}", // TODO: "tbi": To Be Improved
                                                             Context.SystemIndex,
                                                             Context.ProcessIndexOrAddress );
                    }
                    else
                    {
                        // Kernel-mode target
                        m_targetFriendlyName = Util.Sprintf( "Kernel_target_{0}",
                                                             Context.SystemIndex );
                    }
                }
                return m_targetFriendlyName;
            }
        } // end property TargetFriendlyName


        public string Path
        {
            get
            {
                return DbgProvider.GetPathForDebuggerContext( Context );
            }
        }


        public DbgEngContext Context { get; private set; }
        private List< DbgModuleInfo > m_modules;
        private IList< DbgModuleInfo > m_modulesRo;
        private List< DbgModuleInfo > m_unloadedModules;
        private IList< DbgModuleInfo > m_unloadedModulesRo;

        private void _LoadModuleInfo()
        {
            if( null == m_modules )
            {
                using( new DbgEngContextSaver( Debugger, Context ) )
                {
                    Debugger.GetModuleInfo( out m_modules, out m_unloadedModules );
                    m_modulesRo = m_modules.AsReadOnly();
                    m_unloadedModulesRo = m_unloadedModules.AsReadOnly();

                    // N.B. When we fall off the end of this block, if the
                    // DbgEngContextSaver has to restore a different context, we'll lose
                    // m_modules. This is unfortunate, but the callbacks from dbgeng do
                    // not have enough context.
                }
            }
        } // end _LoadModuleInfo()

        [NsLeafItem( SummarizeFunc = "_SummarizeLoadedModules" )]
        public IList< DbgModuleInfo > Modules
        {
            get
            {
                _LoadModuleInfo();
                return m_modulesRo;
            }
        } // end property Modules


        [NsLeafItem( SummarizeFunc = "_SummarizeUnloadedModules" )]
        public IList< DbgModuleInfo > UnloadedModules
        {
            get
            {
                _LoadModuleInfo();
                return m_unloadedModulesRo;
            }
        } // end property UnloadedModules


        protected static ColorString _SummarizeLoadedModules( List< object > modObjList, int maxWidth )
        {
            return _SummarizeModuleList( true, modObjList, maxWidth );
        }

        protected static ColorString _SummarizeUnloadedModules( List< object > modObjList, int maxWidth )
        {
            return _SummarizeModuleList( false, modObjList, maxWidth );
        }

        private static ColorString _SummarizeModuleList( bool loadedMods, List< object > modObjList, int maxWidth )
        {
            ColorString cs = new ColorString();

            if( 0 == modObjList.Count )
            {
                cs.AppendPushPopFg( ConsoleColor.DarkGray, "(0 modules)" );
            }
            else
            {
                cs.Append( Util.Sprintf( "{0} modules: ", modObjList.Count ) );

                for( int i = 0; i < Math.Min( modObjList.Count, 3 ); i++ )
                {
                    if( i > 0 )
                        cs.Append( ", " );

                    DbgModuleInfo dmi = (DbgModuleInfo) modObjList[ i ];
                    if( loadedMods )
                        cs.Append( DbgProvider.ColorizeModuleName( dmi.Name ) );
                    else
                        cs.Append( dmi.ImageName );
                }

                if( modObjList.Count > 3 )
                {
                    cs.Append( ", ..." );
                }
            }

            return CaStringUtil.Truncate( cs.ToString( DbgProvider.HostSupportsColor ), maxWidth );
        } // end _SummarizeModuleList()


        // N.B. Assumes that the current context is correct.
        internal void AddModule( DbgModuleInfo modInfo )
        {
            if( null == m_modules )
            {
                Debugger.GetModuleInfo( out m_modules, out m_unloadedModules );
            }
            else
            {
                if( modInfo.IsUnloaded )
                {
                    // TODO BUGBUG: uh... probably need to remove it from the loaded list?
                    // Except... sometimes modules are on both the loaded and unloaded
                    // list (and sometimes on the unloaded list more than once), so when
                    // do I remove it? Crud; I probably have to re-query instead of just
                    // relying on incremental changes via notifications.
                    m_unloadedModules.Add( modInfo );
                }
                else
                {
#if DEBUG
                    Util.Assert( !m_modules.Contains( modInfo ) );
#endif
                    m_modules.Add( modInfo );
                }
            }
        } // end AddModule()


        internal void ModuleUnloaded( string imageBaseName, ulong imageOffset )
        {
            // TODO: for now we'll just refresh everything
            m_modules = null;
        } // end ModuleUnloaded


        // N.B. Assumes that the current context is correct.
        internal void RefreshModuleAt( ulong baseAddress )
        {
            if( null != m_modules )
            {
                DbgModuleInfo mod = m_modules.FirstOrDefault( ( m ) => m.BaseAddress == baseAddress );
                if( null != mod )
                {
                    mod.Refresh();
                }
            }
        } // end RefreshModuleAt()


        // N.B. Assumes that the current context is correct.
        internal void RefreshModuleInfo()
        {
            if( null != m_modules )
            {
                foreach( var mod in m_modules )
                {
                    mod.Refresh();
                }
            }
        } // end RefreshModuleInfo()


        public void DiscardCachedModuleInfo()
        {
            m_modules = null;
            m_unloadedModules = null;
        } // end DiscardCachedModuleInfo()


        private Dictionary< ulong, int > m_symbolCookies = new Dictionary< ulong, int >();

        /// <summary>
        ///    Increments the "symbol cookie" for the specified module.
        /// </summary>
        /// <remarks>
        ///    Anybody can new up a DbgModuleInfo object based on the module base address,
        ///    so rather than try to keep this bit of info on the DbgModuleInfo and keep
        ///    all of them up-to-date, we'll store it here on the process object.
        /// </remarks>
        internal void BumpSymbolCookie( ulong modBase )
        {
            if( 0 != modBase )
            {
                int curCookie;
                if( !m_symbolCookies.TryGetValue( modBase, out curCookie ) )
                {
                    m_symbolCookies.Add( modBase, 1 );
                }
                else
                {
                    m_symbolCookies[ modBase ] = curCookie + 1;
                }
            }
            else // modBase is 0
            {
                // We need to bump all the cookies.
                foreach( var key in m_symbolCookies.Keys.ToArray() )
                {
                    m_symbolCookies[ key ] = m_symbolCookies[ key ] + 1;
                }
            }
        } // end BumpSymbolCookie()


        internal int GetSymbolCookie( ulong modBase )
        {
            int curCookie;
            if( !m_symbolCookies.TryGetValue( modBase, out curCookie ) )
            {
                // If someone is asking for this cookie, then they will need to know about
                // changes in the future. We stick a 0 in so that unqualified symbol
                // reloads (i.e. just ".reload") will increment it.
                m_symbolCookies[ modBase ] = 0;
                return 0;
            }
            return curCookie;
        } // end GetSymbolCookie()


        // This will give us access to the ClrMd stuff.
        private DataTarget m_dataTarget;
        public DataTarget Target
        {
            get
            {
                if( null == m_dataTarget )
                {
                    m_dataTarget = Debugger.CreateDataTargetForProcess( this );
                }
                // Lazy, but easier than wiring up change propagation:
                m_dataTarget.SymbolLocator.SymbolPath = Debugger.SymbolPath;
                // TODO: But... what if someone changes the Debugger.SymbolPath?!
                return m_dataTarget;
            }
        } // end property DataTarget


        private IReadOnlyList< ClrRuntime > m_clrRuntimes;
        private int m_lastEngineCookie;

        public IReadOnlyList< ClrRuntime > ClrRuntimes
        {
            get
            {
                if( null == m_clrRuntimes )
                {
                    var list = new List< ClrRuntime >( Target.ClrVersions.Count );
                    foreach( var clrVersion in Target.ClrVersions )
                    {
                        string dacPath = Target.SymbolLocator.FindBinary( clrVersion.DacInfo );
                        if( String.IsNullOrEmpty( dacPath ) )
                        {
                            LogManager.Trace( "Warning: could not get DAC for clrVersion {0}.", clrVersion );
                            DbgProvider.RequestExecuteBeforeNextPrompt( Util.Sprintf( "Write-Warning 'Could not get DAC for clrVersion {0}.'",
                                                                                      clrVersion ) );
                        }
                        else
                        {
                            try
                            {
                                list.Add( clrVersion.CreateRuntime( dacPath ) );
                            }
                            catch( ClrDiagnosticsException cde )
                            {
                                string msg = Util.GetExceptionMessages( cde );
                                // Or should this only be a warning?

                                // This script is ridiculously convoluted, but with
                                // reason: it makes the resulting error look a lot nicer.
                                //
                                // (If we just did the plain "Write-Error", the entire
                                // Write-Error line would show up as the first line of the
                                // error output, because PS wants to show the line which
                                // caused the error, but its rather confusing in this
                                // context. So instead we "hide" the Write-Error inside a
                                // temporary function, and then call that.)
                                //
                                string script = Util.Sprintf( "& {{ function DataTarget.CreateRuntime() {{\n" +
                                                              "        Write-Error -Category OpenError -ErrorId 'ClrMd_CouldNotCreateRuntime' " +
                                                              "-Message 'Failed to create ClrMd runtime for clr version {0}: {1}' " +
                                                              "-TargetObject '{2}' -Exception $args[0]\n" +
                                                              "    }}\n" +
                                                              "    DataTarget.CreateRuntime $args[ 0 ]\n" +
                                                              "}} $args[0]",
                                                              clrVersion,
                                                              msg,
                                                              dacPath );

                                // Here is an example of the output produced by that script:
                                //
                                //    DataTarget.CreateRuntime : Failed to create ClrMd runtime for clr version v4.5.27.00: (ClrDiagnosticsException) This
                                //    runtime is not initialized and contains no data.
                                //    At line:4 char:5
                                //    +     DataTarget.CreateRuntime $args[ 0 ]
                                //    +     ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
                                //        + CategoryInfo          : OpenError: (C:\Symcache\msc...4_4.5.27.00.dll:String) [Write-Error], ClrDiagnosticsException
                                //        + FullyQualifiedErrorId : ClrMd_CouldNotCreateRuntime,DataTarget.CreateRuntime
                                DbgProvider.RequestExecuteBeforeNextPrompt( script, cde );

                                // Interesting experiment: an exception from here can get
                                // completely swallowed if we don't handle it. Uncomment
                                // the following line to observe:
                                //throw;
                            } // end catch( ClrDiagnosticsException )
                        } // end else( we found dacPath )
                    } // end foreach( clrVersion )
                    m_clrRuntimes = new ReadOnlyCollection< ClrRuntime >( list );
                    m_lastEngineCookie = Debugger.DbgEngCookie;
                }
                else
                {
                    if( Debugger.DbgEngCookie != m_lastEngineCookie )
                    {
                        foreach( var dac in m_clrRuntimes )
                        {
                            dac.Flush();
                        }
                    }
                }
                return m_clrRuntimes;
            }
        } // end property ClrRuntimes


        private bool m_clrMdDisabled;

        public bool ClrMdDisabled
        {
            get {  return m_clrMdDisabled; }
            set
            {
                bool oldVal = m_clrMdDisabled;
                m_clrMdDisabled = value;
                if( oldVal != m_clrMdDisabled )
                {
                    if( m_clrMdDisabled )
                    {
                        m_clrRuntimes = new List<ClrRuntime>( 0 ).AsReadOnly();
                    }
                    else
                    {
                        m_clrRuntimes = null;
                    }
                } // end if( transition )
            } // end set
        } // end property ClrMdDisabled


        // TODO: Should this be internal?
        public void SetContext()
        {
            Debugger.SetCurrentDbgEngContext( Context );
        }

        #region IEquatable<DbgTarget> Members

        public bool Equals( DbgTarget other )
        {
            if( other == null )
                return false;

            return Context == other.Context;
        } // end Equals

        #endregion

        public override bool Equals( object obj )
        {
            return Equals( obj as DbgTarget );
        }

        public override int GetHashCode()
        {
            return Context.GetHashCode();
        } // end GetHashCode()

        public static bool operator ==( DbgTarget dup1, DbgTarget dup2 )
        {
            if( null == (object) dup1 )
                return (null == (object) dup2);

            return dup1.Equals( dup2 );
        }

        public static bool operator !=( DbgTarget dup1, DbgTarget dup2 )
        {
            return !(dup1 == dup2);
        }

        #region IComparable< DbgTarget > Members

        public int CompareTo( DbgTarget other )
        {
            int result = 0;

            result = Context.CompareTo( other.Context );

            return result;
        } // end CompareTo( other )

        #endregion IComparable< DbgTarget >
    } // end class DbgTarget
}

