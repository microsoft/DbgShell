using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Management.Automation.Runspaces;
using System.Reflection;

namespace MS.Dbg
{
    public class ProviderInfoBase : ProviderInfo, IDisposable
    {
        protected static readonly object sm_gate = new object();

        public Guid RunspaceId { get; private set; }

        protected TraceSource m_ts;
        private static bool sm_firstTraceSourceCreated;

        private string m_traceSourceName;

        public TraceSource TraceSource
        {
            get
            {
                if( null == m_ts )
                {
                    string tracerName = m_traceSourceName;
                    lock( sm_gate )
                    {
                        if( sm_firstTraceSourceCreated )
                            tracerName = tracerName + RunspaceId.ToString();
                        else
                            sm_firstTraceSourceCreated = true;
                    }
                    m_ts = new TraceSource( tracerName, SourceLevels.All );
                }
                return m_ts;
            }
        }

        public SessionState SessionState { get; private set; }

        public void Dispose()
        {
            Dispose( true );
            GC.SuppressFinalize( this );
        } // end Dispose()

        protected virtual void Dispose( bool disposing )
        {
        }

        protected ProviderInfoBase( Guid runspaceId,
                                    ProviderInfo providerInfo,
                                    SessionState sessionState,
                                    string traceSourceName )
            : base( providerInfo )
        {
            if( null == sessionState )
                throw new ArgumentNullException( "sessionState" );

            RunspaceId = runspaceId;
            SessionState = sessionState;
            m_traceSourceName = traceSourceName;
        } // end constructor
    } // end class ProviderInfoBase


    //
    // This class is used to store per-runspace information for a provider.
    //
    // (which is essentially ALL of its information)
    //
    // Background: Instead of creating a single instance of a provider, PowerShell will
    // create /many/ instances (even for what you might consider a single operation). So
    // any state must be stored somewhere else. This is that place.
    //
    public class ProviderInfoBase< TProviderInfo > : ProviderInfoBase
                                                     where TProviderInfo : ProviderInfoBase< TProviderInfo >
    {
        //
        // Static stuff
        //

        private static Dictionary< Guid, TProviderInfo > sm_settings
            = new Dictionary<Guid, TProviderInfo>();


        // The TProviderInfo object for the runspace needs to have already been created;
        // this method is only able to look it up.
        public static TProviderInfo GetSettingsForRunspace( Guid runspaceId )
        {
            TProviderInfo pi;
            lock( sm_gate )
            {
                if( !sm_settings.TryGetValue( runspaceId, out pi ) )
                {
                    string guessedProviderName = Util.GetGenericTypeName( typeof( TProviderInfo ) );
                    if( guessedProviderName.EndsWith( "Info", StringComparison.OrdinalIgnoreCase ) )
                        guessedProviderName.Substring( 0, guessedProviderName.Length - 4 );

                    throw new ProviderNotFoundException( "This runspace has no " + guessedProviderName + " provider loaded." );
                }
            }
            return pi;
        } // end GetSettingsForRunspace()


        private static Guid _GetRunspaceIdForProvider( CmdletProvider provider )
        {
            // So far I can't find any other way to get this. Seems wrong.
            // Update: Confirmed by PS team. INTeb83a001.

            // this.Context.ExecutionContext.CurrentRunspace.InstanceId

            Type t = provider.GetType();
            BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo pi = t.GetProperty( "Context", bf );
            object context = pi.GetValue( provider );
            t = context.GetType();
            pi = t.GetProperty( "ExecutionContext", bf );
            object execContext = pi.GetValue( context );
            t = execContext.GetType();
            pi = t.GetProperty( "CurrentRunspace", bf );
            Runspace r = (Runspace) pi.GetValue( execContext );
            return r.InstanceId;
        } // end _GetRunspaceIdForProvider()


        public static TProviderInfo GetSettingsForProvider( CmdletProvider provider,
                                                            ProviderInfo providerInfo,
                                                            Func< Guid, ProviderInfo, SessionState, TProviderInfo > creator )
        {
            //
            // providerInfo should already BE a TProviderInfo, because whenever PS creates [yet another]
            // gratuitous instance of your provider, it is supposed to set its ProviderInfo to the object
            // that it returned from its Start method. But sometimes it doesn't. TODO: <bug #>
            //

            if( providerInfo is TProviderInfo )
            {
                return (TProviderInfo) providerInfo;
            }

            Util.Fail( "PS Bug XXX: PS neglected to set the provider's ProviderInfo." );
            // OR, the provider neglected to return a TProviderInfo from its Start method.

            TProviderInfo tpi;
            // Provider objects are extremely short-lived--PS usually ends up creating
            // several for each operation. So we'll need to index off of something
            // more substantial... we'll use the runspace ID.
            Guid runspaceId = _GetRunspaceIdForProvider( provider );
            lock( sm_gate )
            {
                if( !sm_settings.TryGetValue( runspaceId, out tpi ) )
                {
                    tpi = creator( runspaceId, providerInfo, provider.SessionState );
                    sm_settings.Add( runspaceId, tpi );
                }
            }
            return tpi;
        } // end GetSettingsForProvider()


        public static void Remove( Guid runspaceId )
        {
            lock( sm_gate )
            {
                TProviderInfo settings;
                sm_settings.TryGetValue( runspaceId, out settings );
                sm_settings.Remove( runspaceId );
                if( null != settings )
                {
                    settings.Dispose();
                }
            }
        } // end Remove()

        protected ProviderInfoBase( Guid runspaceId,
                                    ProviderInfo providerInfo,
                                    SessionState sessionState,
                                    string traceSourceName )
            : base( runspaceId, providerInfo, sessionState, traceSourceName )
        {
            lock( sm_gate )
            {
                Util.Assert( !sm_settings.ContainsKey( runspaceId ) );
                sm_settings.Add( runspaceId, (TProviderInfo) this );
            }
        } // end constructor
    } // end class ProviderInfoBase< TProviderInfo >
}

