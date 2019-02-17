using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents a set of variables and functions, to be used with
    ///    ScriptBlock.InvokeWithContext.
    /// </summary>
    [System.Diagnostics.DebuggerDisplay( "PsContext: {Funcs.Count} Funcs, {Vars.Count} Vars" )]
    public class PsContext
    {
        public Dictionary< string, ScriptBlock > Funcs;
        public Dictionary< string, PSVariable > Vars;

        public List< PSVariable > VarList
        {
            get
            {
                return Vars.Values.ToList();
            }
        }

        public int TotalCount
        {
            get
            {
                return Funcs.Count + Vars.Count;
            }
        }

        public PsContext()
        {
            // TODO: come up with a way to do a read-only, empty context
            // OR just get rid of context capture altogether?
            Funcs = new Dictionary< string, ScriptBlock >( 100 );
            Vars = new Dictionary< string, PSVariable >( 100 );
        }

        public PsContext( PsContext other )
        {
            if( null == other )
                throw new ArgumentNullException( "other" );

            Funcs = new Dictionary< string, ScriptBlock >( other.Funcs );
            Vars = new Dictionary< string, PSVariable >( other.Vars );
        }
        
        /// <summary>
        ///    Diffs two PsContexts. This makes it easy to see what the differences are,
        ///    and potentially cuts down on what must be stored. If 'right' is non-empty,
        ///    this creates a new PsContext object (unless 'left' is null, in which case
        ///    it returns null).
        /// </summary>
        public static PsContext operator-( PsContext left, PsContext right )
        {
            if( (null == right) || (0 == right.TotalCount) )
                return left;

            if( null == left )
                return null;

            PsContext newCtx = new PsContext();
            foreach( var key in left.Funcs.Keys )
            {
                if( !right.Funcs.ContainsKey( key ) ||
                    (right.Funcs[ key ].Ast.Extent.Text != left.Funcs[ key ].Ast.Extent.Text) )
                {
                    newCtx.Funcs.Add( key, left.Funcs[ key ] );
                }
            }

            foreach( var key in left.Vars.Keys )
            {
                if( !right.Vars.ContainsKey( key ) )
                {
                    newCtx.Vars.Add( key, left.Vars[ key ] );
                }
            }
            return newCtx;
        } // end operator-


        /// <summary>
        ///    Combines two PsContexts. If 'right' is non-empty, creates a new PsContext
        ///    object (rather than mutating an existing one). "Conflicts" are won by
        ///    'right'.
        /// </summary>
        public static PsContext operator+( PsContext left, PsContext right )
        {
            if( (null == right) || (0 == right.TotalCount) )
                return left;

            if( null == left )
                return right;

            PsContext newCtx = new PsContext( left );
            foreach( var key in right.Funcs.Keys )
            {
                newCtx.Funcs[ key ] = right.Funcs[ key ];
            }

            foreach( var key in right.Vars.Keys )
            {
                newCtx.Vars[ key ] = right.Vars[ key ];
            }
            return newCtx;
        } // end operator+
    } // end class PsContext
}

