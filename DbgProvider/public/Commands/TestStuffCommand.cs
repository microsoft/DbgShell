using System;
using System.Dynamic;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management.Automation;
using System.Management.Automation.Language;
using Microsoft.Diagnostics.Runtime.Interop;
using DbgEngWrapper;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsDiagnostic.Test, "Stuff" )]
    public class TestStuffCommand : DbgBaseCommand
    {
        private void _CheckHr( int hr )
        {
            if( 0 != hr )
                throw new DbgEngException( hr );
        }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

       //   WriteObject( "\u009b91mThis should be red.\u009bm This should not.\r\n" );
       //   WriteObject( "\u009b91;41mThis should be red on red.\u009bm This should not.\r\n" );
       //   WriteObject( "\u009b91;100mThis should be red on gray.\u009b" );
       //   WriteObject( "m This should not.\r\n" );

       //   WriteObject( "\u009b" );
       //   WriteObject( "9" );
       //   WriteObject( "1;10" );
       //   WriteObject( "6mThis should be red on cyan.\u009b" );
       //   WriteObject( "m This should \u009bm\u009bmnot.\r\n" );


       // //WriteObject( "\u009b92mModLoad:\u009bm 758a0000 758ac000   \u009b97mCRYPTBASE\u009bm\r\n" );
       // //WriteObject( "\u009b92mModLoad:\u009bm 758a0000 758ac000   \u009b30;107mCRYPTBASE\u009bm\r\n" );


            PathInfo pi = this.CurrentProviderLocation( DbgProvider.ProviderId );
            var debugger = DbgProvider.GetDebugger( pi.ProviderPath );
            debugger.SetContextByPath( pi.ProviderPath );

        } // end ProcessRecord()
    } // end class TestStuffCommand


    [Cmdlet( VerbsDiagnostic.Test, "Stuff2" )]
    public class TestStuff2Command : DbgBaseCommand
    {
        private void _CheckHr( int hr )
        {
            if( 0 != hr )
                throw new DbgEngException( hr );
        }

        [Parameter( Mandatory = true, ValueFromPipeline = true, Position = 0 )]
        public DbgTypeInfo Type { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            PathInfo pi = this.CurrentProviderLocation( DbgProvider.ProviderId );
            var debugger = DbgProvider.GetDebugger( pi.ProviderPath );
            debugger.SetContextByPath( pi.ProviderPath );
            WDebugSymbols ds = (WDebugSymbols) debugger.DebuggerInterface;

        } // end ProcessRecord()
    } // end class TestStuff2Command


    [Cmdlet( VerbsDiagnostic.Test, "NewDbgSimpleSymbol" )]
    public class TestStuff3Command : DbgBaseCommand
    {
        private void _CheckHr( int hr )
        {
            if( 0 != hr )
                throw new DbgEngException( hr );
        }

        [Parameter( Mandatory = true, Position = 0 )]
        public ulong Address { get; set; }


        [Parameter( Mandatory = true, Position = 1 )]
        public DbgNamedTypeInfo Type { get; set; }


        [Parameter( Mandatory = true, Position = 2 )]
        public string Name { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();
            var sym = new DbgSimpleSymbol( Debugger,
                                           Name,
                                           Type,
                                           Address );
            WriteObject( sym );
        } // end ProcessRecord()
    } // end class TestStuff3Command


 // [Cmdlet( VerbsDiagnostic.Test, "Stuff4" )]
 // public class TestStuff4Command : DbgBaseCommand
 // {
 //     private void _CheckHr( int hr )
 //     {
 //         if( 0 != hr )
 //             throw new DbgEngException( hr );
 //     }

 //     [Parameter( Mandatory = true, Position = 0 )]
 //     public ulong ModBase { get; set; }

 //     [Parameter( Mandatory = true, Position = 1 )]
 //     public uint TypeId { get; set; }


 //     protected override void ProcessRecord()
 //     {
 //         base.ProcessRecord();
 //         DbgHelp.FindFields( Debugger.DebuggerInterface, ModBase, TypeId );
 //     } // end ProcessRecord()
 // } // end class TestStuff4Command


    [Cmdlet( VerbsDiagnostic.Test, "SymGetTypeInfo" )]
    public class TestStuff5Command : DbgBaseCommand
    {
        private void _CheckHr( int hr )
        {
            if( 0 != hr )
                throw new DbgEngException( hr );
        }

        [Parameter( Mandatory = true, Position = 0 )]
        public ulong ModBase { get; set; }

        [Parameter( Mandatory = true, Position = 1 )]
        public uint TypeId { get; set; }


        [Parameter( Mandatory = true, Position = 2 )]
        public IMAGEHLP_SYMBOL_TYPE_INFO TypeInfo { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            WriteObject( DbgHelp.SymGetTypeInfo( Debugger.DebuggerInterface,
                                                 ModBase,
                                                 TypeId,
                //IMAGEHLP_SYMBOL_TYPE_INFO.TI_GET_SYMINDEX ) );
                                                 TypeInfo ) );
        } // end ProcessRecord()
    } // end class TestStuff5Command

    [Cmdlet( VerbsDiagnostic.Test, "SymFromIndex" )]
    public class TestStuff6Command : DbgBaseCommand
    {
        private void _CheckHr( int hr )
        {
            if( 0 != hr )
                throw new DbgEngException( hr );
        }

        [Parameter( Mandatory = true, Position = 0 )]
        public ulong ModBase { get; set; }

        [Parameter( Mandatory = true, Position = 1 )]
        public uint SymIndex { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            WriteObject( DbgHelp.SymFromIndex( Debugger.DebuggerInterface,
                                               ModBase,
                                               SymIndex ) );
        } // end ProcessRecord()
    } // end class TestStuff6Command



    [Cmdlet( VerbsDiagnostic.Test, "NewSymBackedSymbol" )]
    public class TestStuff7Command : DbgBaseCommand
    {
        private void _CheckHr( int hr )
        {
            if( 0 != hr )
                throw new DbgEngException( hr );
        }

        [Parameter( Mandatory = true, Position = 0 )]
        public SymbolInfo SymInfo { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            WriteObject( new DbgPublicSymbol( Debugger,
                                              SymInfo,
                                              Debugger.GetCurrentTarget() ) );
        } // end ProcessRecord()
    } // end class TestStuff7Command


    [Cmdlet( VerbsDiagnostic.Test, "Stuff8" )]
    public class TestStuff8Command : DbgBaseCommand
    {
        private void _CheckHr( int hr )
        {
            if( 0 != hr )
                throw new DbgEngException( hr );
        }

        [Parameter( Mandatory = true, Position = 0 )]
        public ulong ModBase { get; set; }

        [Parameter( Mandatory = true, Position = 1 )]
        public uint TypeId { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            BasicType baseType;
            ulong size;
            DbgHelp.GetBaseTypeInfo( Debugger.DebuggerInterface, ModBase, TypeId, out baseType, out size );
            WriteObject( baseType );
            WriteObject( size );
        } // end ProcessRecord()
    } // end class TestStuff8Command


    public class MyVisitor : ICustomAstVisitor
    {
        #region ICustomAstVisitor Members

        public object VisitArrayExpression( ArrayExpressionAst arrayExpressionAst )
        {
            Console.WriteLine( "Visited an ArrayExpressionAst." );
            return arrayExpressionAst;
        }

        public object VisitArrayLiteral( ArrayLiteralAst arrayLiteralAst )
        {
            Console.WriteLine( "Visited an ArrayLiteralAst." );
            return arrayLiteralAst;
        }

        public object VisitAssignmentStatement( AssignmentStatementAst assignmentStatementAst )
        {
            Console.WriteLine( "Visited an AssignmentStatementAst." );
            return assignmentStatementAst;
        }

        public object VisitAttribute( AttributeAst attributeAst )
        {
            Console.WriteLine( "Visited an AttributeAst." );
            return attributeAst;
        }

        public object VisitAttributedExpression( AttributedExpressionAst attributedExpressionAst )
        {
            Console.WriteLine( "Visited an AttributedExpressionAst." );
            return attributedExpressionAst;
        }

        public object VisitBinaryExpression( BinaryExpressionAst binaryExpressionAst )
        {
            Console.WriteLine( "Visited an BinaryExpressionAst." );
            return binaryExpressionAst;
        }

        public object VisitBlockStatement( BlockStatementAst blockStatementAst )
        {
            Console.WriteLine( "Visited an BlockStatementAst." );
            return blockStatementAst;
        }

        public object VisitBreakStatement( BreakStatementAst breakStatementAst )
        {
            Console.WriteLine( "Visited an BreakStatementAst." );
            return breakStatementAst;
        }

        public object VisitCatchClause( CatchClauseAst catchClauseAst )
        {
            Console.WriteLine( "Visited an CatchClauseAst." );
            return catchClauseAst;
        }

        public object VisitCommand( CommandAst commandAst )
        {
            Console.WriteLine( "Visited an CommandAst." );
            return commandAst;
        }

        public object VisitCommandExpression( CommandExpressionAst commandExpressionAst )
        {
            Console.WriteLine( "Visited an CommandExpressionAst." );
            return commandExpressionAst;
        }

        public object VisitCommandParameter( CommandParameterAst commandParameterAst )
        {
            Console.WriteLine( "Visited an CommandParameterAst." );
            return commandParameterAst;
        }

        public object VisitConstantExpression( ConstantExpressionAst constantExpressionAst )
        {
            Console.WriteLine( "Visited an ConstantExpressionAst." );
            return constantExpressionAst;
        }

        public object VisitContinueStatement( ContinueStatementAst continueStatementAst )
        {
            Console.WriteLine( "Visited an ContinueStatementAst." );
            return continueStatementAst;
        }

        public object VisitConvertExpression( ConvertExpressionAst convertExpressionAst )
        {
            Console.WriteLine( "Visited an ConvertExpressionAst." );
            return convertExpressionAst;
        }

        public object VisitDataStatement( DataStatementAst dataStatementAst )
        {
            Console.WriteLine( "Visited an DataStatementAst." );
            return dataStatementAst;
        }

        public object VisitDoUntilStatement( DoUntilStatementAst doUntilStatementAst )
        {
            Console.WriteLine( "Visited an DoUntilStatementAst." );
            return doUntilStatementAst;
        }

        public object VisitDoWhileStatement( DoWhileStatementAst doWhileStatementAst )
        {
            Console.WriteLine( "Visited an DoWhileStatementAst." );
            return doWhileStatementAst;
        }

        public object VisitErrorExpression( ErrorExpressionAst errorExpressionAst )
        {
            Console.WriteLine( "Visited an ErrorExpressionAst." );
            return errorExpressionAst;
        }

        public object VisitErrorStatement( ErrorStatementAst errorStatementAst )
        {
            Console.WriteLine( "Visited an ErrorStatementAst." );
            return errorStatementAst;
        }

        public object VisitExitStatement( ExitStatementAst exitStatementAst )
        {
            Console.WriteLine( "Visited an ExitStatementAst." );
            return exitStatementAst;
        }

        public object VisitExpandableStringExpression( ExpandableStringExpressionAst expandableStringExpressionAst )
        {
            Console.WriteLine( "Visited an ExpandableStringExpressionAst." );
            return expandableStringExpressionAst;
        }

        public object VisitFileRedirection( FileRedirectionAst fileRedirectionAst )
        {
            Console.WriteLine( "Visited an FileRedirectionAst." );
            return fileRedirectionAst;
        }

        public object VisitForEachStatement( ForEachStatementAst forEachStatementAst )
        {
            Console.WriteLine( "Visited an ForEachStatementAst." );
            return forEachStatementAst;
        }

        public object VisitForStatement( ForStatementAst forStatementAst )
        {
            Console.WriteLine( "Visited an ForStatementAst." );
            return forStatementAst;
        }

        public object VisitFunctionDefinition( FunctionDefinitionAst functionDefinitionAst )
        {
            Console.WriteLine( "Visited an FunctionDefinitionAst." );
            return functionDefinitionAst;
        }

        public object VisitHashtable( HashtableAst hashtableAst )
        {
            Console.WriteLine( "Visited an HashtableAst." );
            return hashtableAst;
        }

        public object VisitIfStatement( IfStatementAst ifStmtAst )
        {
            Console.WriteLine( "Visited an IfStatementAst." );
            return ifStmtAst;
        }

        public object VisitIndexExpression( IndexExpressionAst indexExpressionAst )
        {
            Console.WriteLine( "Visited an IndexExpressionAst." );
            return indexExpressionAst;
        }

        public object VisitInvokeMemberExpression( InvokeMemberExpressionAst invokeMemberExpressionAst )
        {
            Console.WriteLine( "Visited an InvokeMemberExpressionAst." );
            return invokeMemberExpressionAst;
        }

        public object VisitMemberExpression( MemberExpressionAst memberExpressionAst )
        {
            Console.WriteLine( "Visited an MemberExpressionAst." );
            return memberExpressionAst;
        }

        public object VisitMergingRedirection( MergingRedirectionAst mergingRedirectionAst )
        {
            Console.WriteLine( "Visited an MergingRedirectionAst." );
            return mergingRedirectionAst;
        }

        public object VisitNamedAttributeArgument( NamedAttributeArgumentAst namedAttributeArgumentAst )
        {
            Console.WriteLine( "Visited an NamedAttributeArgumentAst." );
            return namedAttributeArgumentAst;
        }

        public object VisitNamedBlock( NamedBlockAst namedBlockAst )
        {
            Console.WriteLine( "Visited an NamedBlockAst." );
            return namedBlockAst;
        }

        public object VisitParamBlock( ParamBlockAst paramBlockAst )
        {
            Console.WriteLine( "Visited an ParamBlockAst." );
            return paramBlockAst;
        }

        public object VisitParameter( ParameterAst parameterAst )
        {
            Console.WriteLine( "Visited an ParameterAst." );
            return parameterAst;
        }

        public object VisitParenExpression( ParenExpressionAst parenExpressionAst )
        {
            Console.WriteLine( "Visited an ParenExpressionAst." );
            return parenExpressionAst;
        }

        public object VisitPipeline( PipelineAst pipelineAst )
        {
            Console.WriteLine( "Visited an PipelineAst." );
            return pipelineAst;
        }

        public object VisitReturnStatement( ReturnStatementAst returnStatementAst )
        {
            Console.WriteLine( "Visited an ReturnStatementAst." );
            return returnStatementAst;
        }

        public object VisitScriptBlock( ScriptBlockAst scriptBlockAst )
        {
            Console.WriteLine( "Visited an ScriptBlockAst." );
            return scriptBlockAst;
        }

        public object VisitScriptBlockExpression( ScriptBlockExpressionAst scriptBlockExpressionAst )
        {
            Console.WriteLine( "Visited an ScriptBlockExpressionAst." );
            return scriptBlockExpressionAst;
        }

        public object VisitStatementBlock( StatementBlockAst statementBlockAst )
        {
            Console.WriteLine( "Visited an StatementBlockAst." );
            return statementBlockAst;
        }

        public object VisitStringConstantExpression( StringConstantExpressionAst stringConstantExpressionAst )
        {
            Console.WriteLine( "Visited an StringConstantExpressionAst." );
            return stringConstantExpressionAst;
        }

        public object VisitSubExpression( SubExpressionAst subExpressionAst )
        {
            Console.WriteLine( "Visited an SubExpressionAst." );
            return subExpressionAst;
        }

        public object VisitSwitchStatement( SwitchStatementAst switchStatementAst )
        {
            Console.WriteLine( "Visited an SwitchStatementAst." );
            return switchStatementAst;
        }

        public object VisitThrowStatement( ThrowStatementAst throwStatementAst )
        {
            Console.WriteLine( "Visited an ThrowStatementAst." );
            return throwStatementAst;
        }

        public object VisitTrap( TrapStatementAst trapStatementAst )
        {
            Console.WriteLine( "Visited an TrapStatementAst." );
            return trapStatementAst;
        }

        public object VisitTryStatement( TryStatementAst tryStatementAst )
        {
            Console.WriteLine( "Visited an TryStatementAst." );
            return tryStatementAst;
        }

        public object VisitTypeConstraint( TypeConstraintAst typeConstraintAst )
        {
            Console.WriteLine( "Visited an TypeConstraintAst." );
            return typeConstraintAst;
        }

        public object VisitTypeExpression( TypeExpressionAst typeExpressionAst )
        {
            Console.WriteLine( "Visited an TypeExpressionAst." );
            return typeExpressionAst;
        }

        public object VisitUnaryExpression( UnaryExpressionAst unaryExpressionAst )
        {
            Console.WriteLine( "Visited an UnaryExpressionAst." );
            return unaryExpressionAst;
        }

        public object VisitUsingExpression( UsingExpressionAst usingExpressionAst )
        {
            Console.WriteLine( "Visited an UsingExpressionAst." );
            return usingExpressionAst;
        }

        public object VisitVariableExpression( VariableExpressionAst variableExpressionAst )
        {
            Console.WriteLine( "Visited an VariableExpressionAst." );
            return variableExpressionAst;
        }

        public object VisitWhileStatement( WhileStatementAst whileStatementAst )
        {
            Console.WriteLine( "Visited an WhileStatementAst." );
            return whileStatementAst;
        }

        #endregion
    }

    public class MyVisitor2 : AstVisitor
    {
        public override AstVisitAction VisitErrorStatement(ErrorStatementAst errorStatementAst)
        {
            Console.WriteLine( "Visited an ErrorStatementAst." );
            Console.WriteLine( "    " + errorStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitErrorExpression(ErrorExpressionAst errorExpressionAst)
        {
            Console.WriteLine( "Visited an ErrorExpressionAst." );
            Console.WriteLine( "    " + errorExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitScriptBlock(ScriptBlockAst scriptBlockAst)
        {
            Console.WriteLine( "Visited an ScriptBlockAst." );
            Console.WriteLine( "    " + scriptBlockAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitParamBlock(ParamBlockAst paramBlockAst)
        {
            Console.WriteLine( "Visited an ParamBlockAst." );
            Console.WriteLine( "    " + paramBlockAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitNamedBlock(NamedBlockAst namedBlockAst)
        {
            Console.WriteLine( "Visited an NamedBlockAst." );
            Console.WriteLine( "    " + namedBlockAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitTypeConstraint(TypeConstraintAst typeConstraintAst)
        {
            Console.WriteLine( "Visited an TypeConstraintAst." );
            Console.WriteLine( "    " + typeConstraintAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitAttribute(AttributeAst attributeAst)
        {
            Console.WriteLine( "Visited an AttributeAst." );
            Console.WriteLine( "    " + attributeAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitParameter(ParameterAst parameterAst)
        {
            Console.WriteLine( "Visited an ParameterAst." );
            Console.WriteLine( "    " + parameterAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitTypeExpression(TypeExpressionAst typeExpressionAst)
        {
            Console.WriteLine( "Visited an TypeExpressionAst." );
            Console.WriteLine( "    " + typeExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitFunctionDefinition(FunctionDefinitionAst functionDefinitionAst)
        {
            Console.WriteLine( "Visited an FunctionDefinitionAst." );
            Console.WriteLine( "    " + functionDefinitionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitStatementBlock(StatementBlockAst statementBlockAst)
        {
            Console.WriteLine( "Visited an StatementBlockAst." );
            Console.WriteLine( "    " + statementBlockAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitIfStatement(IfStatementAst ifStmtAst)
        {
            Console.WriteLine( "Visited an IfStatementAst." );
            Console.WriteLine( "    " + ifStmtAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitTrap(TrapStatementAst trapStatementAst)
        {
            Console.WriteLine( "Visited an TrapStatementAst." );
            Console.WriteLine( "    " + trapStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitSwitchStatement(SwitchStatementAst switchStatementAst)
        {
            Console.WriteLine( "Visited an SwitchStatementAst." );
            Console.WriteLine( "    " + switchStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitDataStatement(DataStatementAst dataStatementAst)
        {
            Console.WriteLine( "Visited an DataStatementAst." );
            Console.WriteLine( "    " + dataStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitForEachStatement(ForEachStatementAst forEachStatementAst)
        {
            Console.WriteLine( "Visited an ForEachStatementAst." );
            Console.WriteLine( "    " + forEachStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitDoWhileStatement(DoWhileStatementAst doWhileStatementAst)
        {
            Console.WriteLine( "Visited an DoWhileStatementAst." );
            Console.WriteLine( "    " + doWhileStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitForStatement(ForStatementAst forStatementAst)
        {
            Console.WriteLine( "Visited an ForStatementAst." );
            Console.WriteLine( "    " + forStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitWhileStatement(WhileStatementAst whileStatementAst)
        {
            Console.WriteLine( "Visited an WhileStatementAst." );
            Console.WriteLine( "    " + whileStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitCatchClause(CatchClauseAst catchClauseAst)
        {
            Console.WriteLine( "Visited an CatchClauseAst." );
            Console.WriteLine( "    " + catchClauseAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitTryStatement(TryStatementAst tryStatementAst)
        {
            Console.WriteLine( "Visited an TryStatementAst." );
            Console.WriteLine( "    " + tryStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitBreakStatement(BreakStatementAst breakStatementAst)
        {
            Console.WriteLine( "Visited an BreakStatementAst." );
            Console.WriteLine( "    " + breakStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitContinueStatement(ContinueStatementAst continueStatementAst)
        {
            Console.WriteLine( "Visited an ContinueStatementAst." );
            Console.WriteLine( "    " + continueStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitReturnStatement(ReturnStatementAst returnStatementAst)
        {
            Console.WriteLine( "Visited an ReturnStatementAst." );
            Console.WriteLine( "    " + returnStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitExitStatement(ExitStatementAst exitStatementAst)
        {
            Console.WriteLine( "Visited an ExitStatementAst." );
            Console.WriteLine( "    " + exitStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitThrowStatement(ThrowStatementAst throwStatementAst)
        {
            Console.WriteLine( "Visited an ThrowStatementAst." );
            Console.WriteLine( "    " + throwStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitDoUntilStatement(DoUntilStatementAst doUntilStatementAst)
        {
            Console.WriteLine( "Visited an DoUntilStatementAst." );
            Console.WriteLine( "    " + doUntilStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitAssignmentStatement(AssignmentStatementAst assignmentStatementAst)
        {
            Console.WriteLine( "Visited an AssignmentStatementAst." );
            Console.WriteLine( "    " + assignmentStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitPipeline(PipelineAst pipelineAst)
        {
            Console.WriteLine( "Visited an PipelineAst." );
            Console.WriteLine( "    " + pipelineAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitCommand(CommandAst commandAst)
        {
            Console.WriteLine( "Visited an CommandAst." );
            Console.WriteLine( "    " + commandAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitCommandExpression(CommandExpressionAst commandExpressionAst)
        {
            Console.WriteLine( "Visited an CommandExpressionAst." );
            Console.WriteLine( "    " + commandExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitCommandParameter(CommandParameterAst commandParameterAst)
        {
            Console.WriteLine( "Visited an CommandParameterAst." );
            Console.WriteLine( "    " + commandParameterAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitMergingRedirection(MergingRedirectionAst redirectionAst)
        {
            Console.WriteLine( "Visited an MergingRedirectionAst." );
            Console.WriteLine( "    " + redirectionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitFileRedirection(FileRedirectionAst redirectionAst)
        {
            Console.WriteLine( "Visited an FileRedirectionAst." );
            Console.WriteLine( "    " + redirectionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitBinaryExpression(BinaryExpressionAst binaryExpressionAst)
        {
            Console.WriteLine( "Visited an BinaryExpressionAst." );
            Console.WriteLine( "    " + binaryExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitUnaryExpression(UnaryExpressionAst unaryExpressionAst)
        {
            Console.WriteLine( "Visited an UnaryExpressionAst." );
            Console.WriteLine( "    " + unaryExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitConvertExpression(ConvertExpressionAst convertExpressionAst)
        {
            Console.WriteLine( "Visited an ConvertExpressionAst." );
            Console.WriteLine( "    " + convertExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitConstantExpression(ConstantExpressionAst constantExpressionAst)
        {
            Console.WriteLine( "Visited an ConstantExpressionAst." );
            Console.WriteLine( "    " + constantExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitStringConstantExpression(StringConstantExpressionAst stringConstantExpressionAst)
        {
            Console.WriteLine( "Visited an StringConstantExpressionAst." );
            Console.WriteLine( "    " + stringConstantExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitSubExpression(SubExpressionAst subExpressionAst)
        {
            Console.WriteLine( "Visited an SubExpressionAst." );
            Console.WriteLine( "    " + subExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitUsingExpression(UsingExpressionAst usingExpressionAst)
        {
            Console.WriteLine( "Visited an UsingExpressionAst." );
            Console.WriteLine( "    " + usingExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitVariableExpression(VariableExpressionAst variableExpressionAst)
        {
            Console.WriteLine( "Visited an VariableExpressionAst." );
            Console.WriteLine( "    " + variableExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitMemberExpression(MemberExpressionAst memberExpressionAst)
        {
            Console.WriteLine( "Visited an MemberExpressionAst." );
            Console.WriteLine( "    " + memberExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitInvokeMemberExpression(InvokeMemberExpressionAst methodCallAst)
        {
            Console.WriteLine( "Visited an InvokeMemberExpressionAst." );
            Console.WriteLine( "    " + methodCallAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitArrayExpression(ArrayExpressionAst arrayExpressionAst)
        {
            Console.WriteLine( "Visited an ArrayExpressionAst." );
            Console.WriteLine( "    " + arrayExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitArrayLiteral(ArrayLiteralAst arrayLiteralAst)
        {
            Console.WriteLine( "Visited an ArrayLiteralAst." );
            Console.WriteLine( "    " + arrayLiteralAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitHashtable(HashtableAst hashtableAst)
        {
            Console.WriteLine( "Visited an HashtableAst." );
            Console.WriteLine( "    " + hashtableAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitScriptBlockExpression(ScriptBlockExpressionAst scriptBlockExpressionAst)
        {
            Console.WriteLine( "Visited an ScriptBlockExpressionAst." );
            Console.WriteLine( "    " + scriptBlockExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitParenExpression(ParenExpressionAst parenExpressionAst)
        {
            Console.WriteLine( "Visited an ParenExpressionAst." );
            Console.WriteLine( "    " + parenExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitExpandableStringExpression(ExpandableStringExpressionAst expandableStringExpressionAst)
        {
            Console.WriteLine( "Visited an ExpandableStringExpressionAst." );
            Console.WriteLine( "    " + expandableStringExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitIndexExpression(IndexExpressionAst indexExpressionAst)
        {
            Console.WriteLine( "Visited an IndexExpressionAst." );
            Console.WriteLine( "    " + indexExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitAttributedExpression(AttributedExpressionAst attributedExpressionAst)
        {
            Console.WriteLine( "Visited an AttributedExpressionAst." );
            Console.WriteLine( "    " + attributedExpressionAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitBlockStatement(BlockStatementAst blockStatementAst)
        {
            Console.WriteLine( "Visited an BlockStatementAst." );
            Console.WriteLine( "    " + blockStatementAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }
        public override AstVisitAction VisitNamedAttributeArgument(NamedAttributeArgumentAst namedAttributeArgumentAst)
        {
            Console.WriteLine( "Visited an NamedAttributeArgumentAst." );
            Console.WriteLine( "    " + namedAttributeArgumentAst.ToString().Replace( Environment.NewLine, Environment.NewLine + "    " ) );
            return AstVisitAction.Continue;
        }

    }



    [Cmdlet( VerbsDiagnostic.Test, "Stuff9" )]
    public class TestStuff9Command : DbgBaseCommand
    {
        private void _CheckHr( int hr )
        {
            if( 0 != hr )
                throw new DbgEngException( hr );
        }

        [Parameter( Mandatory = true, Position = 0 )]
        [ModuleTransformation]
        public DbgModuleInfo Module;


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            WriteObject( Debugger.ReadImageNtHeaders( Module ) );
        } // end ProcessRecord()
    } // end class TestStuff9Command


    [Cmdlet( VerbsDiagnostic.Test, "StuffA" )]
    public class TestStuffACommand : DbgBaseCommand
    {
        private void _CheckHr( int hr )
        {
            if( 0 != hr )
                throw new DbgEngException( hr );
        }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            WHostDataModelAccess hdma = (WHostDataModelAccess) Debugger.DebuggerInterface;

            hdma.GetDataModel( out WDataModelManager manager, out WDebugHost host );

            _CheckHr( manager.GetRootNamespace( out IntPtr rootNs ) );

            _CheckHr( WModelObject.GetKind( rootNs, out ModelObjectKind kind ) );
            WriteObject( kind );
        } // end ProcessRecord()
    } // end class TestStuff9Command

}
