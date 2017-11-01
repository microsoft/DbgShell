using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Text;
using MS.Dbg.Commands;

namespace MS.Dbg.Formatting.Commands
{
    public abstract class FormatBaseCommand : DbgBaseCommand
    {
        [Parameter( Position = 0 )]
        public object[] Property { get; set; }

        [Parameter]
        // N.B. Must allow to specify null in order to allow overriding a view-specific
        // GroupBy object.
        public object GroupBy { get; set; }

        [Parameter]
        public string View { get; set; } // TODO: implement

        [Parameter]
        public SwitchParameter ShowError { get; set; } // TODO: implement

        [Parameter]
        public SwitchParameter DisplayError { get; set; } // TODO: implement

        [Parameter]
        public SwitchParameter Force { get; set; } // TODO: implement

        [Parameter]
        [ValidateSet( "CoreOnly", "EnumOnly", "Both" )]
        public string Expand { get; set; } // TODO: implement

        [Parameter( ValueFromPipeline = true )]
        public PSObject InputObject { get; set; }

        [Parameter( Mandatory = false )]
        public IFormatInfo FormatInfo { get; set; }

        protected abstract void ApplyViewToInputObject();

        // Used when -GroupBy is specified, when starting a new group.
        protected abstract void ResetState();

        // Allows customization of the GroupBy header used between different groups.
        protected virtual ScriptBlock GetCustomWriteGroupByGroupHeaderScript(
            out PsContext context,
            out bool preserveHeaderContext )
        {
            context = null;
            preserveHeaderContext = false;
            return null;
        }

        // View implementations can optionally supply their own GroupBy, even if there
        // wasn't one on the command line.
        protected virtual object GetViewDefinedGroupBy()
        {
            return null;
        }


        private class PipeIndexPSVariable : PSVariable
        {
            internal const string c_PipeOutputIndexName = "PipeOutputIndex";

            private FormatBaseCommand m_fbc;
            public PipeIndexPSVariable( FormatBaseCommand fbc )
                : base( c_PipeOutputIndexName )
            {
                m_fbc = fbc;
            }

            public override object Value
            {
                get
                {
                    return m_fbc.m_pipeIndex;
                }
                set
                {
                    throw new PSInvalidOperationException( "You cannot modify this value." );
                }
            }

            public override ScopedItemOptions Options
            {
                get
                {
                    return ScopedItemOptions.ReadOnly;
                }
                set
                {
                    throw new PSInvalidOperationException( "You cannot modify this value." );
                }
            }
        } // end class PipeIndexPSVariable


        private static readonly ColorString sm_PropNotFoundFmt =
            new ColorString( ConsoleColor.Red, "<property not found: {0}>" ).MakeReadOnly();

        private PipeIndexPSVariable m_pipeIndexPSVar;
        private int m_pipeIndex = -1;
        private object m_lastGroupResult;
        private string m_groupByLabel;
        private Func< object > m_GroupByFunc;
        private bool m_warnedAboutNoSuchGroupByProperty;


        protected PsContext m_groupByHeaderCtx;

        private void _WriteGroupByGroupHeader( object newResult )
        {
            PsContext headerCtx;
            bool preserveHeaderContext;
            ScriptBlock customHeaderScript = GetCustomWriteGroupByGroupHeaderScript( out headerCtx,
                                                                                     out preserveHeaderContext );

            if( null == customHeaderScript )
            {
                customHeaderScript = ScriptBlock.Create(
                    Util.Sprintf( "(New-ColorString -Content {0}: -Fore Black -Back White)." +
                                  "Append(" +
                                      "(Format-AltSingleLine -InputObject $_)" +
                                  ")",
                                  m_groupByLabel ) );
            }

            if( null != customHeaderScript )
            {
                // ISSUE: Or maybe the group-by evaluation functions should return a
                // PSObject instead.
                PSObject pso = newResult as PSObject;
                if( (null == pso) && (null != newResult) )
                    pso = new PSObject( newResult ); // you can't pass null to the PSObject constructor.

                using( var pch = new PsContextHelper( customHeaderScript,
                                                      headerCtx,
                                                      preserveHeaderContext ) )
                {
                    string val = RenderScriptValue( pso, pch.AdjustedScriptBlock, true, pch.WithContext );

                    m_groupByHeaderCtx = pch.SavedContext;

                    if( null != val )
                        WriteObject( val );
                }
            }
            else
            {
                WriteObject( String.Empty );
                WriteObject( new ColorString( "   " )
                    //.AppendPushFgBg( ConsoleColor.Black, ConsoleColor.Cyan )
                    //.AppendPushFg( ConsoleColor.Cyan )
                             .AppendPushFgBg( ConsoleColor.Black, ConsoleColor.White )
                             .Append( m_groupByLabel )
                             .AppendPop()
                             .Append( ": " )
                             .Append( ObjectToMarkedUpString( newResult ).ToString() )
                             .ToString( DbgProvider.HostSupportsColor ) );
                WriteObject( String.Empty );
            }
        } // end _WriteGroupByGroupHeader()


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( Stopping )
                return;

            if( (null == GroupBy) && !MyInvocation.BoundParameters.ContainsKey( "GroupBy" ) )
            {
                GroupBy = GetViewDefinedGroupBy();
            }

            m_pipeIndex++;
            if( null != GroupBy )
            {
                object newResult = _EvaluateGroupBy();

                bool newGroup = _GroupByResultIsDifferent( m_lastGroupResult, newResult );
                if( newGroup )
                {
                    ResetState();
                    m_lastGroupResult = newResult;
                    if( m_pipeIndex > 0 )
                        WriteObject( String.Empty ); // this seems ugly...

                    _WriteGroupByGroupHeader( newResult );
                }
            }
        } // end ProcessRecord()


        private object _EvaluateCalculatedPropertyGroupBy()
        {
            Hashtable table = (Hashtable) GroupBy;
            throw new NotImplementedException();
        } // end _EvaluateCalculatedPropertyGroupBy()


        private object _EvaluateScriptGroupBy()
        {
            var script = (ScriptBlock) GroupBy;
            Collection< PSObject > results = null;

            // TODO: this is pretty awkward. These things should probably be split out
            // into individual properties.
            PsContext context;
            bool dontCare;
            var alsoDontCare = GetCustomWriteGroupByGroupHeaderScript( out context,
                                                                       out dontCare );
            if( null == context )
                context = new PsContext();

            context.Vars[ "_" ]      = new PSVariable( "_",      InputObject );
            context.Vars[ "PSItem" ] = new PSVariable( "PSItem", InputObject );

            try
            {
                // Note that StrictMode will be enforced for value converters, because
                // they execute in the scope of Debugger.Formatting.psm1, which sets
                // StrictMode.
                results = script.InvokeWithContext( context.Funcs, context.VarList );
            }
            catch( RuntimeException re )
            {
                LogManager.Trace( "Ignoring error in _EvaluateScriptGroupBy: {0}",
                                  Util.GetExceptionMessages( re ) );
            }

            // Let's not keep the input object rooted.
            context.Vars.Remove( "_" );
            context.Vars.Remove( "PSItem" );

         // PowerShell shell;
         // using( DbgProvider.LeaseShell( out shell ) )
         // {
         //     // Note that StrictMode will be enforced for value converters, because
         //     // they execute in the scope of Debugger.Formatting.psm1, which sets
         //     // StrictMode.
         //     shell.AddScript( @"args[ 0 ].InvokeWithContext( $args[ 1 ].Funcs, $args[ 1 ].VarList )",
         //                      true )
         //          .AddArgument( script )
         //          .AddArgument( context );

         //     results = shell.Invoke();
         // }

            // I guess we don't care if errors were encountered, as long as it produced output?

            if( Stopping || (null == results) || (0 == results.Count) )
            {
                return null;
            }
            else if( 1 == results.Count )
            {
                return results[ 0 ].BaseObject;
            }
            else
            {
                var list = new List< object >( results.Count );
                list.AddRange( results.Select( (r) => r.BaseObject ) );
                return list;
            }
        } // end _EvaluateScriptGroupBy()


        private object _EvaluatePropertyGroupBy()
        {
            var propName = (string) GroupBy;
            PSPropertyInfo pi = InputObject.Properties[ propName ];
            if( null == pi )
            {
                if( !m_warnedAboutNoSuchGroupByProperty )
                {
                    m_warnedAboutNoSuchGroupByProperty = true;
                    SafeWriteWarning( "No such property '{0}' to group by.", propName );
                }
                return null;
            }

            try
            {
                return pi.Value;
            }
            catch( RuntimeException rte )
            {
                // TODO: is this the right way to do this? Seems terrible to have to
                // copy the whole array every time (because we have to insert at the beginning)...
                ArrayList errorList = (ArrayList) this.SessionState.PSVariable.GetValue( "error" );
                //if( errorList[ 0 ] != rte.ErrorRecord )
                errorList.Insert( 0, Util.FixErrorRecord( rte.ErrorRecord, rte ) );

                return null;
            }
        } // end _EvaluatePropertyGroupBy()


        private object _EvaluateGroupBy()
        {
            if( null != m_GroupByFunc )
                return m_GroupByFunc();

            Hashtable table = GroupBy as Hashtable;
            if( null != table )
            {
                // "Calculated property"
                m_GroupByFunc = _EvaluateCalculatedPropertyGroupBy;

                m_groupByLabel = table[ "Name" ] as string;
                if( null == m_groupByLabel )
                {
                    m_groupByLabel = table[ "Label" ] as string;
                    if( null == m_groupByLabel )
                    {
                        // TODO: proper error
                        throw new ArgumentException( "'Calculated property' table missing Name/Label entry of type String.",
                                                     "GroupBy" );
                    }
                }
            } // end if( -GroupBy @{ calculated property } )

            ScriptBlock script = GroupBy as ScriptBlock;
            if( null != script )
            {
                m_GroupByFunc = _EvaluateScriptGroupBy;
                m_groupByLabel = "{Script GroupBy}";
            } // end if( -GroupBy { script } )

            string propName = GroupBy as string;
            if( null != propName )
            {
                m_GroupByFunc = _EvaluatePropertyGroupBy;
                m_groupByLabel = propName;
            } // end if( -GroupBy propertyName )

            // Should we support an array of property names? I don't think so; I
            // think a "calculated property" should be able to handle that.

            if( null == m_GroupByFunc )
            {
                // TODO: proper error
                throw new ArgumentException( Util.Sprintf( "Unexpected object type for -GroupBy ({0}).",
                                                           Util.GetGenericTypeName( GroupBy ) ),
                                             "GroupBy" );
            }
            return m_GroupByFunc();
        } // end _EvaluateGroupBy()


        private bool _GroupByResultIsDifferent( object lastResult, object newResult )
        {
            if( null == lastResult )
            {
                return null != newResult;
            }
            else if( null == newResult )
            {
                return null != lastResult;
            }

            // They are both not null.

            // TODO: I think I should be using LanguagePrimitives to do this.

            if( lastResult.GetType() != newResult.GetType() )
            {
                // Even though they are not the same type, they could be comparable
                // via some IEquatable interface or something.
                return lastResult != newResult;
            }

            if( lastResult is PSObject )
            {
                // TODO: Bug? What if they are PSOs wrapping different types?
                PSObject lastPso = (PSObject) lastResult;
                PSObject newPso = (PSObject) newResult;
                return lastPso.BaseObject != newPso.BaseObject;
            }

            if( lastResult is string )
            {
                // Strings in PS are generally case-insensitive.
                string lastString = (string) lastResult;
                string newString = (string) newResult;
                return 0 != Util.Strcmp_OI( lastString, newString );
            }
            // TODO: need to do a collection comparison...
         // if( lastResult is Collection<PSObject> )
         // {
         // }

            return !lastResult.Equals( newResult );
        } // end _GroupByResultIsDifferent()


        private int __formatEnumerationLimit = -1;
        private int _FormatEnumerationLimit
        {
            get
            {
                if( -1 == __formatEnumerationLimit )
                {
                    var ss = this.SessionState;
                    if( null == ss )
                    {
                        // This can happen because of our FormatAltSingleLineDirect hack,
                        // wherein we just construct a FormatAltSingleLineCommand object
                        // on our own.
                        ss = new SessionState();
                    }
                    object tmp = ss.PSVariable.GetValue( "FormatEnumerationLimit", 4 );
                    if( tmp is int )
                    {
                        __formatEnumerationLimit = (int) tmp;
                    }
                    else
                    {
                        LogManager.Trace( "Warning: FormatEnumerationLimit is not an int ({0}).",
                                          Util.GetGenericTypeName( tmp ) );
                        __formatEnumerationLimit = 4;
                    }
                }
                return __formatEnumerationLimit;
            }
        } // end property _FormatEnumerationLimit


        protected StringBuilder ObjectToMarkedUpString( object obj )
        {
            return ObjectToMarkedUpString( obj, null, null );
        }

        protected StringBuilder ObjectToMarkedUpString( object obj, StringBuilder sb )
        {
            return ObjectToMarkedUpString( obj, null, sb );
        }

        protected StringBuilder ObjectToMarkedUpString( object obj, ColorString formatString )
        {
            return ObjectToMarkedUpString( obj, formatString, null );
        }

        protected StringBuilder ObjectToMarkedUpString( object obj, ColorString formatString, StringBuilder sb )
        {
            if( null == sb )
                sb = new StringBuilder();

            if( null == obj )
                //return sb;
                return sb.Append( "$null" );

            PSObject pso = obj as PSObject;
            if( null != pso )
            {
                // Check if there is a custom .ToString() method attached to this
                // PSObject. (which is used, for instance, for displaying enum values)

                var toStringMethods = Util.TryGetCustomToStringMethod( pso );

                if( null != toStringMethods )
                {
                    // TODO: check overloads/signature?
                    obj = toStringMethods.Invoke();
                    if( obj is string )
                    {
                        return sb.Append( (string) obj );
                    }
                    else
                    {
                        LogManager.Trace( "Interesting... a custom .ToString() method returned something besides a string." );
                    }
                }
                else
                {
                    obj = pso.BaseObject;
                }
            }

            ColorString cs = obj as ColorString;
            if( null != cs )
            {
                sb.Append( cs.ToString( DbgProvider.HostSupportsColor ) );
            }
            else
            {
                ISupportColor isc = obj as ISupportColor;
                if( null != isc )
                {
                    sb.Append( isc.ToColorString().ToString( DbgProvider.HostSupportsColor ) );
                }
                else
                {
                    string renderedFormatString = null;
                    if( null != formatString )
                    {
                        renderedFormatString = formatString.ToString( DbgProvider.HostSupportsColor );
                    }

                    if( !String.IsNullOrEmpty( renderedFormatString ) )
                    {
                        sb.Append( Util.CcSprintf( renderedFormatString, obj ) );
                    }
                    else
                    {
                        // TODO: Or should it be pso.ToString here?
                        sb.Append( obj.ToString() );
                    }
                }
            }
            return sb;
        } // end ObjectToMarkedUpString()


        protected StringBuilder ObjectsToMarkedUpString( IEnumerable objects )
        {
            return ObjectsToMarkedUpString( objects, null, null );
        }

        protected StringBuilder ObjectsToMarkedUpString( IEnumerable objects, StringBuilder sb )
        {
            return ObjectsToMarkedUpString( objects, null, sb );
        }

        protected StringBuilder ObjectsToMarkedUpString( IEnumerable objects, ColorString formatString )
        {
            return ObjectsToMarkedUpString( objects, formatString, null );
        }

        protected StringBuilder ObjectsToMarkedUpString( IEnumerable objects, ColorString formatString, StringBuilder sb )
        {
            return ObjectsToMarkedUpString( objects, formatString, sb, false );
        }

        protected StringBuilder ObjectsToMarkedUpString( IEnumerable objects,
                                                         ColorString formatString,
                                                         StringBuilder sb,
                                                         bool dontGroupMultipleResults )
        {
            if( null == sb )
                sb = new StringBuilder();

            int count = 0;
            object last = null;
            bool truncate = false;
            foreach( object obj in objects )
            {
                if( 0 != count )
                {
                    if( dontGroupMultipleResults )
                    {
                        ObjectToMarkedUpString( last, formatString, sb );
                        sb.AppendLine();
                    }
                    else
                    {
                        if( 1 == count )
                            sb.Append( "{" );

                        if( (count > 0) && (count > _FormatEnumerationLimit) )
                        {
                            truncate = true;
                            break;
                        }
                        ObjectToMarkedUpString( last, formatString, sb );
                        sb.Append( ", " );
                    }
                }

                last = obj;
                if( null == formatString )
                    last = FormatSingleLine( last );

                count++;
            } // end foreach( obj )

            if( count > 0 )
            {
                if( truncate || (!dontGroupMultipleResults && (count > _FormatEnumerationLimit)) )
                    sb.Append( "..." );
                else
                    ObjectToMarkedUpString( last, formatString, sb );
            }

            if( (count > 1) && !dontGroupMultipleResults )
                sb.Append( "}" );

            return sb;
        } // end ObjectsToMarkedUpString


        protected static string PadAndAlign( string s,
                                             int width,
                                             ColumnAlignment alignment )
        {
            return PadAndAlign( s, width, alignment, TrimLocation.Right );
        }

        protected static string PadAndAlign( string s,
                                             int width,
                                             ColumnAlignment alignment,
                                             TrimLocation trimLocation )
        {
            Util.Assert( ColumnAlignment.Default != alignment );

            int len = CaStringUtil.Length( s );
            int pad = width - len;
            if( 0 == pad )
                return s;

            if( pad < 0 )
                // Oh dear... too big to fit.
                return CaStringUtil.Truncate( s, width, true, trimLocation );

            switch( alignment )
            {
                case ColumnAlignment.Left:
                    return s + new String( ' ', pad );
                case ColumnAlignment.Center:
                    int leftpad = pad / 2;
                    int rightpad = pad - leftpad;
                    return new String( ' ', leftpad ) + s + new String( ' ', rightpad );
                case ColumnAlignment.Right:
                    return new String( ' ', pad ) + s;
                default:
                    throw new ArgumentException( Util.Sprintf( "Invalid ColumnAlignment value: {0}",
                                                               alignment ),
                                                 "alignment" );
            }
        } // end PadAndAlign()


        private bool _ShouldUnroll( IEnumerable obj )
        {
            if( null == obj )
                return false;

            // TODO: This is a kludge. Perhaps there should at least be a user-accessible
            // way to customize what should be unrolled or not.
            string typeName = Util.GetGenericTypeName( obj );
            if( typeName == "DbgArrayValue" )
                return false;

            int idx = typeName.IndexOf( '<' );
            if( idx > 0 )
                typeName = typeName.Substring( 0, idx );

            return typeName.EndsWith( "Collection" ) ||
                   typeName.EndsWith( "Set" ) ||
                   typeName.EndsWith( "List" ) ||
                   typeName.IndexOf( "Array" ) > 0 ||
                   typeName.IndexOf( "[]" ) > 0;
        } // end _ShouldUnroll()


        protected string RenderPropertyValue( PSObject inputObject, string propertyName )
        {
            return RenderPropertyValue( inputObject, propertyName, null );
        }

        protected string RenderPropertyValue( PSObject inputObject,
                                              string propertyName,
                                              ColorString formatString )
        {
            return RenderPropertyValue( inputObject, propertyName, formatString, false );
        }

        protected string RenderPropertyValue( PSObject inputObject,
                                              string propertyName,
                                              ColorString formatString,
                                              bool dontGroupMultipleResults )
        {
            PSPropertyInfo pi = inputObject.Properties[ propertyName ];
            if( null == pi )
            {
                var val = sm_PropNotFoundFmt.ToString( DbgProvider.HostSupportsColor );
                val = Util.Sprintf( val, propertyName );
                var e = new PropertyNotFoundException( Util.Sprintf( "Property not found: {0}", propertyName ) );
                try { throw e; } catch( Exception ) { }; // give it a stack
                ErrorRecord er = new ErrorRecord( e, "MissingProperty", ErrorCategory.InvalidData, inputObject );
                AddToError( er );
                return val;
            }

            try
            {
                var obj = pi.Value;
                IEnumerable enumerable = obj as IEnumerable;
                if( (null != enumerable) && _ShouldUnroll( enumerable ) )
                {
                    return ObjectsToMarkedUpString( enumerable,
                                                    formatString,
                                                    null,
                                                    dontGroupMultipleResults ).ToString();
                }

                // If a formatString was specified, let /it/ control the display, instead of FormatSingleLine.
                if( null == formatString )
                    return ObjectToMarkedUpString( FormatSingleLine( pi.Value ), formatString ).ToString();
                else
                    return ObjectToMarkedUpString( pi.Value, formatString ).ToString();
            }
            catch( RuntimeException rte )
            {
                AddToError( Util.FixErrorRecord( rte.ErrorRecord, rte ) );

                return new ColorString( ConsoleColor.Red,
                                        Util.Sprintf( "<Error: {0}>",
                                                      Util.GetExceptionMessages( rte ) ) )
                            .ToString( DbgProvider.HostSupportsColor );
            }
        } // end RenderPropertyValue()


        protected void AddToError( ErrorRecord er )
        {
            // TODO: is this the right way to do this? Seems terrible to have to
            // copy the whole array every time (because we have to insert at the beginning)...
            ArrayList errorList = (ArrayList) this.SessionState.PSVariable.GetValue( "global:error" );
            if( 0 == errorList.Count )
            {
                errorList.Add( er );
            }
            else
            {
                Util.Assert( errorList[ 0 ] != er );
                errorList.Insert( 0, er );
            }

         // InvokeCommand.InvokeScript( false,
         //                             ScriptBlock.Create( "$input | ForEach-Object { [void] $global:Error.Add( $_ ) }" ),
         //                             new object[] { er } );
        } // end AddToError()


        /// <summary>
        ///    Returns null if the script returned no results.
        /// </summary>
        protected string RenderScriptValue( PSObject inputObject, ScriptBlock script )
        {
            return RenderScriptValue( inputObject, script, null );
        }

        /// <summary>
        ///    Returns null if the script returned no results.
        /// </summary>
        protected string RenderScriptValue( PSObject inputObject,
                                            ScriptBlock script,
                                            PsContext context )
        {
            return RenderScriptValue( inputObject, script, false, context );
        }


#if DEBUG
        [ThreadStatic]
        private static int sm_renderScriptCallDepth;
#endif

        protected string RenderScriptValue( PSObject inputObject,
                                            ScriptBlock script,
                                            bool dontGroupMultipleResults )
        {
            return RenderScriptValue( inputObject, script, dontGroupMultipleResults, null );
        }


        protected string RenderScriptValue( PSObject inputObject,
                                            ScriptBlock script,
                                            bool dontGroupMultipleResults,
                                            PsContext context )
        {
            // Let the script also use context created by the custom header script (if any).
            context = context + m_groupByHeaderCtx;
            Collection< PSObject > results = null;

            if( null == context )
                context = new PsContext();

            if( null == m_pipeIndexPSVar )
                m_pipeIndexPSVar = new PipeIndexPSVariable( this );

            context.Vars[ "_" ]      = new PSVariable( "_",      inputObject );
            context.Vars[ "PSItem" ] = new PSVariable( "PSItem", inputObject );
            context.Vars[ "PipeOutputIndex" ] = m_pipeIndexPSVar;

            PowerShell shell;
            using( DbgProvider.LeaseShell( out shell ) )
            {
#if DEBUG
                sm_renderScriptCallDepth++;
                if( sm_renderScriptCallDepth > 10 )
                {
                    // Helps to catch runaway rendering /before/ gobbling tons of memory
                    System.Diagnostics.Debugger.Break();
                    // Maybe I should just throw?
                }
#endif

                //
                // This code is a lot nicer-looking. But the problem with it is that non-
                // terminating errors get squelched (there's no way for us to get them),
                // and I'd prefer that no errors get hidden. Strangely, non-terminating
                // errors do NOT get squelched when calling script.InvokeWithContext
                // directly in InvokeScriptCommand.InvokeWithContext. I don't know what is
                // making the difference. I thought it might have something to do with the
                // SessionState attached to the ScriptBlock, but I couldn't demonstrate
                // that.
                //
                // Things to try with both methods:
                //
                //      $ctx = $null
                //      $savedCtx = $null
                //      Invoke-Script -ScriptBlock { "hi" ; Write-Error "non-term" ; "bye" } -WithContext $ctx
                //
                //      { "hi" ; Write-Error "non-term" ; "bye" }.InvokeWithContext( @{}, @() )
                //
                //      $tmpMod = New-Module { }
                //      $scriptBlockWithDifferentExecCtx = & $tmpMod { { "hi" ; Write-Error "non-term" ; "bye" } }
                //      $scriptBlockWithDifferentExecCtx.InvokeWithContext( @{}, @() )
                //
                //      Register-AltTypeFormatEntries { New-AltTypeFormatEntry -TypeName 'System.Int32' { New-AltCustomViewDefinition { "It's an int: $_" ; Write-Error "this is non-terminating" ; "done" } } }
                //      42
                //
            //  try
            //  {
            //      // Note that StrictMode will be enforced for value converters, because
            //      // they execute in the scope of Debugger.Formatting.psm1, which sets
            //      // StrictMode.
            //      results = script.InvokeWithContext( context.Funcs, context.VarList );
            //  }
            //  catch( RuntimeException e )
            //  {
            //      return new ColorString( ConsoleColor.Red,
            //                              Util.Sprintf( "<Error: {0}>", e ) )
            //                  .ToString( DbgProvider.HostSupportsColor );
            //  }

                shell.AddScript( @"$args[ 0 ].InvokeWithContext( $args[ 1 ].Funcs, $args[ 1 ].VarList )",
                                 true )
                     .AddArgument( script )
                     .AddArgument( context );

                results = shell.Invoke();

                // Let's not keep the input object rooted.
                context.Vars.Remove( "_" );
                context.Vars.Remove( "PSItem" );
#if DEBUG
                sm_renderScriptCallDepth--;
#endif

                // For some reason, sometimes shell.HadErrors returns true when
                // shell.Streams.Error.Count is 0, when the pipeline is stopping.
                if( Stopping )
                    return String.Empty;

                // INT2d518c4b: shell.HadErrors is clueless.
                //if( shell.HadErrors )
                if( shell.Streams.Error.Count > 0 )
                {
                    if( 1 == shell.Streams.Error.Count )
                    {
                        return new ColorString( ConsoleColor.Red,
                                                Util.Sprintf( "<Error: {0}>",
                                                              shell.Streams.Error[ 0 ] ) )
                                    .ToString( DbgProvider.HostSupportsColor );
                    }
                    else
                    {
                        return new ColorString( ConsoleColor.Red,
                                                Util.Sprintf( "<Multiple errors ({0}) encountered>",
                                                              shell.Streams.Error.Count ) )
                                    .ToString( DbgProvider.HostSupportsColor );
                    }
                }
                else
                {
                    if( (null == results) || (0 == results.Count) )
                        return null;

                    return ObjectsToMarkedUpString( results,
                                                    "{0}", // <-- IMPORTANT: this prevents infinite recursion via Format-AltSingleLine
                                                    null,
                                                    dontGroupMultipleResults ).ToString();
                } // end else( no errors )
            } // end using( shell lease )
        } // end RenderScriptValue()


        protected object FormatSingleLine( object obj )
        {
            return FormatAltSingleLineCommand.FormatSingleLineDirect( obj );

            // The direct route seems a littl risky; I'll keep this code around for now.
         // var shell = _GetCleanShell().AddCommand( "Format-AltSingleLine" );
         // Collection< PSObject > results = shell.Invoke( new object[] { obj } );
         // Util.Assert( results.Count <= 1 );
         // if( results.Count > 0 )
         //     return results[ 0 ];
         // else
         //     return String.Empty; // or should I return null?
        } // end FormatSingleLine()


        protected override bool TrySetDebuggerContext { get { return false; } }
        protected override bool LogCmdletOutline { get { return false; } }
    } // end class FormatBaseCommand



    public abstract class FormatBaseCommand< TView > : FormatBaseCommand where TView : class
    {
        protected TView m_view;
        private bool m_alreadyAutoGenerated;


        protected override void BeginProcessing()
        {
            base.BeginProcessing();
            if( null != FormatInfo )
            {
                TView view = FormatInfo as TView;
                if( null == view )
                    // TODO: proper error
                    throw new ArgumentException( Util.Sprintf( "Bad FormatInfo: it should be a {0}.",
                                                               Util.GetGenericTypeName( typeof( TView ) ) ),
                                                 "FormatInfo" );

                m_view = view;
            }
        } // end BeginProcessing()


        protected virtual TView GenerateView()
        {
            throw new NotImplementedException( "no view" );
        }

        protected override void ProcessRecord()
        {
            if( null == InputObject )
                return;

            // The BoundParameters check is for if someone explicitly passes "-FormatInfo
            // $null", in which case they are requesting we generate a view, even if the
            // AltFormattingManager could find one.
            if( (null == m_view) &&
                (!MyInvocation.BoundParameters.ContainsKey( "FormatInfo" )) &&
                ((null == Property) || (0 == Property.Length))  )
            {
                m_view = AltFormattingManager.ChooseFormatInfoForPSObject<TView>( InputObject );
            }

            // The view may have been specified by our out-default or format-table
            // proxy functions. However, the user may wish to specify the properties
            // to display manually, so we'll let them override by using 'Force'. As
            // far as I can tell, this is very similar to the behavior of the native
            // formatting engine ("$error[0] | fl *" versus "$error[0] | fl * -Force").
            if( (null == m_view) ||
                ((null != Property) && (0 != Property.Length) && Force && !m_alreadyAutoGenerated) )
            {
                m_view = GenerateView();
                m_alreadyAutoGenerated = true; // we don't want to come back in this block again!
            }

            if( null == m_view )
                throw new NotImplementedException( "No view." );

            base.ProcessRecord();

            ApplyViewToInputObject();
        } // end ProcessRecord()


        protected IList< string > ResolvePropertyParameter()
        {
            if( (null == Property) || (0 == Property.Length) )
                return null;

            var list = new List< string >( Property.Length );
            foreach( var obj in Property )
            {
                if( null == obj )
                    throw new ArgumentException( "Null entry in Property array.", "Property" );

                var table = obj as Hashtable;
                if( null != table )
                {
                    // "Calculated view"
                    // TODO: when we implement this, we'll have to change
                    // this method's return type to account for it...
                    throw new NotImplementedException();
                }
                else
                {
                    string name = obj as string;
                    if( null == name )
                    {
                        throw new ArgumentException( Util.Sprintf( "Invalid Property value ({0}).",
                                                                   Util.GetGenericTypeName( obj ) ),
                                                     "Property" );
                    }
                    list.Add( name );
                } // end if( string or hashtable )
            } // end foreach( Property )
            return ResolvePropertyNames( list, warn: true );
        } // end ResolvePropertyParameter()


        protected IList< string > ResolvePropertyNames( IList< string > propNames, bool warn )
        {
            if( null == propNames )
                throw new ArgumentNullException( "propNames" );

            var list = new List< string >( propNames.Count );
            foreach( var propName in propNames )
            {
                if( null == propName )
                    throw new ArgumentException( "Null entry in propety name array.", "propNames" );

                var propertyInfos = InputObject.Properties.Match( propName );
                if( (0 == propertyInfos.Count) &&
                    !WildcardPattern.ContainsWildcardCharacters( propName ) &&
                    warn )
                {
                    SafeWriteWarning( "No such property '{0}' on object of type {1}.",
                                      propName,
                                      Util.GetGenericTypeName( InputObject.BaseObject ) );
                }
                else
                {
                    // We're ignoring if the properties are "gettable"... is that okay?
                    list.AddRange( propertyInfos.Select( (pi) => pi.Name ) );
                }
            } // end foreach( propName )
            return list;
        } // end ResolvePropertyNames()
    } // end class FormatBaseCommand< TView >
}

