using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsLifecycle.Invoke, "Script", DefaultParameterSetName = InvokeScriptCommand.c_UseNewChildScopeFileParamSetName )]
    public class InvokeScriptCommand : DbgBaseCommand
    {
        internal const string c_UseNewChildScopeFileParamSetName = "UseNewChildScopeFileParamSet";
        internal const string c_UseNewChildScopeScriptBlockParamSetName = "UseNewChildScopeScriptBlockParamSet";

        internal const string c_SessionStateFileParamSetName = "SessionStateFileParamSet";
        internal const string c_SessionStateScriptBlockParamSetName = "SessionStateScriptBlockParamSet";

        internal const string c_ContextFileParamSetName = "ContextFileParamSet";
        internal const string c_ContextScriptBlockParamSetName = "ContextScriptBlockParamSet";


        [Parameter( Mandatory = true,
                    ValueFromPipeline = true,
                    Position = 0,
                    ParameterSetName = c_UseNewChildScopeFileParamSetName )]
        [Parameter( Mandatory = true,
                    ValueFromPipeline = true,
                    Position = 0,
                    ParameterSetName = c_SessionStateFileParamSetName )]
        [Parameter( Mandatory = true,
                    ValueFromPipeline = true,
                    Position = 0,
                    ParameterSetName = c_ContextFileParamSetName )]
        [ValidateNotNullOrEmpty]
        public string File { get; set; }

        [Parameter( Mandatory = true,
                    ValueFromPipeline = true,
                    Position = 0,
                    ParameterSetName = c_UseNewChildScopeScriptBlockParamSetName )]
        [Parameter( Mandatory = true,
                    ValueFromPipeline = true,
                    Position = 0,
                    ParameterSetName = c_SessionStateScriptBlockParamSetName )]
        [Parameter( Mandatory = true,
                    ValueFromPipeline = true,
                    Position = 0,
                    ParameterSetName = c_ContextScriptBlockParamSetName )]
        [ValidateNotNull]
        public ScriptBlock ScriptBlock { get; set; }


        [Parameter( Mandatory = false,
                    Position = 1,
                    ParameterSetName = c_UseNewChildScopeFileParamSetName )]
        [Parameter( Mandatory = false,
                    Position = 1,
                    ParameterSetName = c_UseNewChildScopeScriptBlockParamSetName )]
        public SwitchParameter UseNewChildScope { get; set; }


        [Parameter( Mandatory = false,
                    Position = 1,
                    ParameterSetName = c_SessionStateFileParamSetName )]
        [Parameter( Mandatory = false,
                    Position = 1,
                    ParameterSetName = c_SessionStateScriptBlockParamSetName )]
        public SessionState SessionStateScope { get; set; }


        [Parameter( Mandatory = false,
                    ParameterSetName = c_ContextFileParamSetName )]
        [Parameter( Mandatory = false,
                    ParameterSetName = c_ContextScriptBlockParamSetName )]
        public PsContext WithContext { get; set; }


        [Parameter( Mandatory = false,
                    ParameterSetName = c_ContextFileParamSetName )]
        [Parameter( Mandatory = false,
                    ParameterSetName = c_ContextScriptBlockParamSetName )]
        [ValidateNotNull]
        public PSReference SaveContext { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            ScriptBlock sb = ScriptBlock;

            if( null == sb )
            {
                // We must be using a "file" param set.
                ScriptBlockAst ast = LoadScript( File );
                sb = ast.GetScriptBlock();
            }

            if( ParameterSetName.Contains( "UseNewChildScope" ) )
            {
                WriteObject( InvokeScript( InvokeCommand, sb, UseNewChildScope ), true );
            }
            else if( ParameterSetName.Contains( "SessionState" ) )
            {
                WriteObject( this.InvokeCommand.InvokeScript( SessionStateScope,
                                                              sb,
                                                              null ),
                             true );
            }
            else
            {
                Util.Assert( ParameterSetName.Contains( "Context" ) );
                WriteObject( InvokeWithContext( sb, WithContext, SaveContext ), true );
            }
        } // end ProcessRecord()


        internal static Collection< PSObject > InvokeScript( CommandInvocationIntrinsics invokeCommand,
                                                             ScriptBlock scriptBlock,
                                                             bool useNewChildScope )
        {
            return invokeCommand.InvokeScript( useNewChildScope,
                                               scriptBlock,
                                               null,
                                               null );
        } // end InvokeScript

        internal static Collection< PSObject > InvokeWithContext( ScriptBlock scriptBlock,
                                                                  PsContext withContext,
                                                                  PSReference saveContext )
        {
            if( null == withContext )
                withContext = new PsContext(); // TODO: share a single, readonly Empty context

            using( var ctxHelper = new PsContextHelper( scriptBlock, withContext, null != saveContext ) )
            {
                // TODO: Should I switch to using shell.AddScript, like in FormatBaseCommand.cs?
                var results = ctxHelper.AdjustedScriptBlock.InvokeWithContext( ctxHelper.WithContext.Funcs,
                                                                               ctxHelper.WithContext.VarList );

                if( null != saveContext )
                {
                    saveContext.Value = ctxHelper.SavedContext;
                }

                return results;
            } // end using( ctxHelper )
        } // end InvokeWithContext


        internal static ScriptBlockAst LoadScript( string file )
        {
            Token[] tokens;
            ParseError[] errors;
            ScriptBlockAst ast = Parser.ParseFile( file, out tokens, out errors );
            if( 0 != errors.Length )
            {
                var pe = new ParseException( errors );
                try { throw pe; } catch( ParseException ) { } // give it a stack.

                throw new DbgProviderException( Util.Sprintf( "Error(s) parsing file: {0}.", file ),
                                                "LoadScriptFailure_ParseErrors",
                                                ErrorCategory.ParserError,
                                                pe,
                                                file );
            }
            return ast;
        } // end LoadScript
    } // end class InvokeScriptCommand
}

