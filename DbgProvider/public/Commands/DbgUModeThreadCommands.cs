using System;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgUModeThreadInfo",
             DefaultParameterSetName = c_IdParamSetName )]
    [OutputType( typeof( DbgUModeThreadInfo ) )]
    public class GetDbgUModeThreadInfoCommand : DbgBaseCommand
    {
        private const string c_IdParamSetName = "DebuggerIdParamSet";
        private const string c_TidParamSetName = "TidParamSet";
        private const string c_CurrentParamSetName = "CurrentParamSet";
        private const string c_LastEventParamSetName = "LastEventParamSet";

        [Parameter( Mandatory = false, Position = 0, ValueFromPipeline = true, ParameterSetName = c_IdParamSetName )]
        [Alias( "Id" )]
        public uint[] DebuggerId { get; set; }

        [Parameter( Mandatory = false, Position = 0, ValueFromPipeline = true, ParameterSetName = c_TidParamSetName )]
        [Alias( "Tid" )]
        public uint[] SystemId { get; set; }

        [Parameter( Mandatory = false, ParameterSetName = c_CurrentParamSetName )]
        public SwitchParameter Current { get; set; }

        [Parameter( Mandatory = false, ParameterSetName = c_LastEventParamSetName )]
        public SwitchParameter LastEvent { get; set; }


        private static readonly uint[] sm_dummyIds = new uint[] { 0 };


        protected override void ProcessRecord()
        {
            base.ProcessRecord();
            Func< uint, DbgUModeThreadInfo > getThreadFunc;
            uint[] ids;
            string idType;

            if( 0 == Util.Strcmp_OI( ParameterSetName, c_IdParamSetName ) )
            {
                idType = "debugger id";
                getThreadFunc = Debugger.GetUModeThreadByDebuggerId;
                ids = DebuggerId;
            }
            else if( 0 == Util.Strcmp_OI( ParameterSetName, c_TidParamSetName ) )
            {
                idType = "system id (tid)";
                getThreadFunc = Debugger.GetThreadBySystemTid;
                ids = SystemId;
            }
            else if( Current )
            {
                idType = "id [current]";
                getThreadFunc = ( x ) => Debugger.GetCurrentThread();
                ids = sm_dummyIds;
            }
            else
            {
                Util.Assert( LastEvent );
                idType = "id [lastEvent]";
                getThreadFunc = ( x ) => Debugger.GetEventThread();
                ids = sm_dummyIds;
            }

            if( null == ids )
            {
                if( null == DebuggerId ) // Just get 'em all
                {
                    foreach( var ti in Debugger.EnumerateThreads() )
                    {
                        if( Stopping )
                            break;

                        SafeWriteObject( ti );
                    }
                }
            } // end if( no specific id; get them all )
            else
            {
                foreach( uint id in ids )
                {
                    if( Stopping )
                        break;

                    try
                    {
                        SafeWriteObject( getThreadFunc( id ) );
                    }
                    catch( DbgEngException dee )
                    {
                        DbgProviderException dpe = new DbgProviderException(
                                                    Util.Sprintf( "Could not get thread with {0} {1}. No such thread?",
                                                                  idType,
                                                                  id ),
                                                    "BadThreadId",
                                                    ErrorCategory.InvalidArgument,
                                                    dee,
                                                    id );
                        try { throw dpe; } catch( DbgProviderException ) { } // give it a stack
                        SafeWriteError( dpe );
                    }
                }
            } // end else( by id )
        } // end ProcessRecord()
    } // end class GetDbgUModeThreadInfoCommand


    [Cmdlet( VerbsCommon.Switch, "DbgUModeThreadInfo", DefaultParameterSetName = c_IdParamSetName )]
    // Technically this outputs a RegisterSet, so that you can see the new context. But I
    // don't know that I want to put an OutputType attribute on this, since you probably
    // wouldn't use it...
    public class SwitchDbgUModeThreadInfoCommand : DbgBaseCommand
    {
        internal const string c_IdParamSetName = "DebuggerIdParamSet";
        internal const string c_TidParamSetName = "TidParamSet";
        internal const string c_ThreadParamSet = "ThreadParamSet";

        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true,
                    ValueFromPipelineByPropertyName = true,
                    ParameterSetName = c_IdParamSetName )]
        [Alias( "Id" )]
        public uint DebuggerId { get; set; }

        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true,
                    ParameterSetName = c_TidParamSetName )]
        [Alias( "Tid" )]
        // The trouble with this [AddressTransformation] attribute is that it makes things
        // magically work for tids with hex characters... but if they are all decimal
        // (5850, for instance), then this doesn't help.
        //
        // Thus, is it worth making this work on hex tids, with the confusion it will
        // cause because decimal tids don't work?
        [AddressTransformation]
        public uint SystemId { get; set; }

        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true,
                    ParameterSetName = c_ThreadParamSet )]
        [ValidateNotNull]
        [ThreadTransformation]
        public DbgUModeThreadInfo Thread { get; set; }


        // Don't output the register set. Useful for scripting.
        [Parameter]
        public SwitchParameter Quiet { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( null == Thread )
            {
                Func< uint, DbgUModeThreadInfo > getThreadFunc;
                uint id;
                string idType;
                if( 0 == Util.Strcmp_OI( ParameterSetName, c_IdParamSetName ) )
                {
                    idType = "debugger id";
                    getThreadFunc = Debugger.GetUModeThreadByDebuggerId;
                    id = DebuggerId;
                }
                else
                {
                    Util.Assert( 0 == Util.Strcmp_OI( ParameterSetName, c_TidParamSetName ) );

                    idType = "system id (tid)";
                    getThreadFunc = Debugger.GetThreadBySystemTid;
                    id = SystemId;
                }

                try
                {
                    Thread = getThreadFunc( id );
                }
                catch( DbgEngException dee )
                {
                    DbgProviderException dpe = new DbgProviderException(
                                                Util.Sprintf( "Could not get thread with {0} {1}. No such thread?",
                                                              idType,
                                                              id ),
                                                "BadThreadId",
                                                ErrorCategory.InvalidArgument,
                                                dee,
                                                id );
                    // If switching threads fails, subsequent commands might be in the wrong
                    // context, so we'll make this a terminating error.
                    try { throw dpe; } catch( DbgProviderException ) { } // give it a stack
                    ThrowTerminatingError( dpe );
                }
            } // end if( no Thread )

            try
            {
                Util.Assert( null != Thread );
                Thread.SetContext();
            }
            catch( DbgEngException dee )
            {
                DbgProviderException dpe = new DbgProviderException(
                                            Util.Sprintf( "Could not switch to thread {0}: {1}",
                                                          Thread.DebuggerId,
                                                          Util.GetExceptionMessages( dee ) ),
                                            "ThreadSwitchFailed",
                                            ErrorCategory.NotSpecified,
                                            dee,
                                            Thread );

                // If switching threads fails, subsequent commands might be in the wrong
                // context, so we'll make this a terminating error.
                try { throw dpe; }
                catch( DbgProviderException ) { } // give it a stack
                ThrowTerminatingError( dpe );
            }
            finally
            {
                RebuildNamespaceAndSetLocationBasedOnDebuggerContext( !Quiet );
            }
        } // end ProcessRecord()
    } // end class SwitchDbgUModeThreadInfoCommand
}

