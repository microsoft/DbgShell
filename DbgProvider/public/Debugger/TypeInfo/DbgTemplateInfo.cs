using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Dbg
{
    /// <summary>
    ///   Represents something which could be a template, a template parameter, or both.
    /// </summary>
    /// <remarks>
    ///    Note that template parameters are not always type names: for instance, you
    ///    could have an integer parameter, such as to specify a buffer size. (And even
    ///    if it's a type name, sometimes there are bugs that prevent you from looking
    ///    up the type (TODO: INT4e198f3a: can't find _STL70).)
    ///
    ///    Additionally, we perform some "normalizations" of type names, such as
    ///    standardizing where the space and '*' go in pointer type names, and removing
    ///    "const" (that info is stored in the HasConst field).
    /// </remarks>
    public abstract class DbgTemplateNode
    {
        /// <summary>
        ///    The full name of the the type (including type parameters and nested types).
        /// </summary>
        public readonly string FullName;

        /// <summary>
        ///    If the node is a template, the name of the template (minus template
        ///    parameters); else the full name of the type.
        /// </summary>
        public abstract string TemplateName { get; }

        /// <summary>
        ///    Indicates if the node has children (i.e., is a template that has
        ///    parameters).
        /// </summary>
        public abstract bool IsTemplate { get; }


        /// <summary>
        ///    Indicates if the type name is tagged with "const".
        /// </summary>
        /// <remarks>
        ///    If a type name is tagged with "const", we remove it from the FullName, but
        ///    remember that information here.
        ///
        ///    N.B. "Const-ness" does not affect matching.
        /// </remarks>
        public readonly bool HasConst;


        protected DbgTemplateNode( string fullName, bool hasConst )
        {
            if( String.IsNullOrEmpty( fullName ) )
                throw new ArgumentException( "You must supply a name.", "fullName" );

            FullName = fullName;
            HasConst = hasConst;
        } // end constructor


        /// <summary>
        ///    Indicates if two templates "match", taking special template-matching
        ///    wildcards into account.
        /// </summary>
        /// <remarks>
        /// <para>
        ///    Two special wildcards can be used to match template parameters:
        ///      <list type="unordered">
        ///         <item>?
        ///           <description>Matches any single template parameter.</description>
        ///         </item>
        ///         <item>?*
        ///           <description>Matches one or more template parameters. Must be the last
        ///           parameter in a list.</description>
        ///         </item>
        ///      </list>
        ///    For example "std::foo&lt;WCHAR,?&gt;" matches "std::foo&lt;WCHAR,unsigned char&gt;".
        /// </para>
        /// <para>
        ///    Note that for DbgTemplate objects, these wildcards are only applicable in
        ///    the template parameter list; and DbgTemplateLeaf objects can consist of the
        ///    single-type wildcard (?), but not the multi-type wildcard
        ///    (?*).
        /// </para>
        /// <para>
        ///    The SingleTypeWildcardCount and HasMultiTypeWildcard properties can be
        ///    used to establish precedence between multiple matching templates.
        /// </para>
        /// <para>
        ///    N.B. The "const-ness" (HasConst) of a type name does not affect matching.
        /// </para>
        /// </remarks>
        public abstract bool Matches( DbgTemplateNode other );


        /// <summary>
        ///    Indicates if two templates "match" exactly (if they are identical). The
        ///    comparison is case-insensitive.
        /// </summary>
        /// <remarks>
        /// <para>
        ///    For an exact match, the templates must be identical, including wildcards.
        ///    In other words, the FullName of each template must be the same (except for
        ///    case).
        /// </para>
        /// <para>
        ///    Normally when using template names, you should use the <see cref="Match"/>
        ///    method, so that wildcards are taken into account. This method is useful,
        ///    for instance, when you need to find something stored by template name in
        ///    order to update it.
        /// </para>
        /// <para>
        ///    N.B. The "const-ness" (HasConst) of a type name does not affect matching.
        /// </para>
        /// </remarks>
        public bool MatchesExact( DbgTemplateNode other )
        {
            if( null == other )
                throw new ArgumentNullException( "other" );

            return 0 == Util.Strcmp_OI( FullName, other.FullName );
        } // end MatchesExact()


        /// <summary>
        ///    The number of single-type wildcards contained in the template parameters.
        /// </summary>
        /// <remarks>
        /// <para>
        ///    Note that this only applies to template parameter lists; a DbgTemplateLeaf
        ///    object consisting of the single-type wildcard (?) will have a
        ///    SingleTypeWildcardCount of 0.
        /// </para>
        /// <para>
        ///    Also note that this property is not recursive: a template
        ///    "a&lt;b,c&lt;?&gt;&gt;" has a SingleTypeWildcardCount of 0, whereas a
        ///    template "a&lt;?,c&lt;d&gt;&gt;" has a SingleTypeWildcardCount of 1.
        /// </para>
        /// <para>
        ///    This property is used to prioritize template matching (given two templates
        ///    that match a concrete type name, which match should take precedence).
        ///    Templates with a lower wildcard count have higher precedence.
        /// </para>
        /// </remarks>
        public abstract int SingleTypeWildcardCount { get; }

        /// <summary>
        ///    True if the template parameter list ends with a multi-type wildcard.
        /// </summary>
        /// <remarks>
        /// <para>
        ///    Note that this only applies to template parameter lists; this property will
        ///    always be false for a DbgTemplateLeaf object .
        /// </para>
        /// <para>
        ///    Also note that this property is not recursive: a template
        ///    "a&lt;b,c&lt;?*&gt;&gt;" has a HasMultiTypeWildcard value of 'false',
        ///    whereas template "a&lt;b,?*&gt;" has a HasMultiTypeWildcard value of
        ///    'true'.
        /// </para>
        /// <para>
        ///    This property is used to prioritize template matching (given two templates
        ///    that match a concrete type name, which match should take precedence).
        ///    Templates without a multi-type wildcard have higher precedence.
        /// </para>
        /// </remarks>
        public abstract bool HasMultiTypeWildcard { get; }

        public override string ToString()
        {
            return FullName;
        }

        protected const string c_SingleTypeWildcard = "?";
        protected const string c_MultiTypeWildcard = "?*";

        protected static bool IsEitherNameSingleTypeWildcard( string s1, string s2 )
        {
            return (s1 == c_SingleTypeWildcard) || (s2 == c_SingleTypeWildcard);
        }

        protected static bool IsMultiMatchWildcard( DbgTemplateNode dtp )
        {
            return dtp.FullName == c_MultiTypeWildcard;
        }


        /// <summary>
        ///    Indicates if the name looks like a template name: contains a '&lt;',
        ///    doesn't end with a '*' or '[]'. (pointer and array type names are not
        ///    considered template type names)
        /// </summary>
        public static bool LooksLikeATemplateName( string name )
        {
            int dontCare;
            return _LooksLikeATemplateName( name, out dontCare );
        }

        private static bool _LooksLikeATemplateName( string name,
                                                     out int angleBracketIdx )
        {
            return _LooksLikeATemplateName( name, 0, out angleBracketIdx );
        }

        private static bool _LooksLikeATemplateName( string name,
                                                     int startIdx,
                                                     out int angleBracketIdx )
        {
            if( null == name )
                throw new ArgumentNullException( "name" );

            name = name.Trim();

            if( String.IsNullOrEmpty( name ) )
                throw new ArgumentException( "You must supply a name.", "name" );

            angleBracketIdx = name.IndexOf( '<', startIdx );
            // Why not < 0? Because of "<unnamed-tag>" (sometimes found in unions).
            if( angleBracketIdx < 1 )
                return false;

            // A pointer is not a template.
            if( '*' == name[ name.Length - 1 ] )
                return false;

            // And neither is a reference (which is just a pointer at runtime)
            if( '&' == name[ name.Length - 1 ] )
                return false;

            // An array is not a template.
            if( name.EndsWith( "]" ) )  // Just "]" to handle both "[]" and "[nnn]"
                return false;

            // A function is not a template.
            if( ')' == name[ name.Length - 1 ] )
                return false;

            return _LooksLikeATemplateNameInner( name, startIdx, ref angleBracketIdx );
        }


        // Takes care of checks that we need to do repeatedly, after the one-time checks
        // in the outer method.
        private static bool _LooksLikeATemplateNameInner( string name,
                                                          int startIdx,
                                                          ref int angleBracketIdx )
        {
            // Is it possible to have a template that is a nested type beneath something
            // with a lambda or an "<unnamed-type-u>"-type thing?
            //
            // I hope not, but this code tries to deal with it.

            while( (angleBracketIdx > 0) && (angleBracketIdx < name.Length) )
            {
                // Does it look like a lambda thing?
                if( !_LooksLikeLambdaThing( name, angleBracketIdx, out angleBracketIdx ) )
                {
                    // No. Well, does it look like "::<stuff>"?
                    if( !_HasTwoColonsPreceding( name, angleBracketIdx, out angleBracketIdx ) )
                    {
                        //
                        // No. Huh. Looks like it could be a template then.
                        //

                        return true;
                    }
                }
            }

            return false;
        } // end _LooksLikeATemplateNameInner()


        /// <summary>
        ///    Checks to see if it looks like a "lambda thing" at the startIdx. If it
        ///    does, newStartIdx is set to the index of the next '&lt;' AFTER the lambda
        ///    thing.
        /// </summary>
        private static bool _LooksLikeLambdaThing( string s, int startIdx, out int newStartIdx )
        {
            Util.Assert( s[ startIdx ] == '<' );

            newStartIdx = startIdx;

            const string lambdaPrefix = "<lambda_";
            int tmpIdx = startIdx;
            while( (tmpIdx < s.Length) &&
                   ((tmpIdx - startIdx) < lambdaPrefix.Length) &&
                   (s[ tmpIdx ] == lambdaPrefix[ tmpIdx - startIdx ]) )
            {
                tmpIdx++;
            }

            if( tmpIdx != (startIdx + lambdaPrefix.Length) )
                return false;

            // It matched the lambda prefix...
            //
            // Now look for 32 hex digits.

            for( int i = 0; i < 32; i++ )
            {
                byte dontCare;
                if( (tmpIdx >= s.Length) ||
                    !Util.TryCharToByte( s[ tmpIdx ], out dontCare, allowHex: true ) )
                {
                    return false;
                }
                tmpIdx++;
            }

            if( (tmpIdx < s.Length) && (s[ tmpIdx ] == '>') )
            {
                newStartIdx = s.IndexOf( '<', tmpIdx + 1 ); // IndexOf can handle +1 putting us past end
                return true;
            }

            return false;
        } // end _LooksLikeLambdaThing()


        /// <summary>
        ///    Checks to see if it looks like a "&lt;not-a-template-because-it-has-two-colons-preceding-it&gt;"
        ///    at the startIdx. If it does, newStartIdx is set to the index of the next '&lt;' AFTER it.
        /// </summary>
        private static bool _HasTwoColonsPreceding( string s, int startIdx, out int newStartIdx )
        {
            Util.Assert( s[ startIdx ] == '<' );

            newStartIdx = startIdx;

            if( (startIdx < 2) ||
                (s[ startIdx - 1 ] != ':') ||
                (s[ startIdx - 2 ] != ':') )
            {
                return false;
            }

            newStartIdx++; // move past the '<'

            //
            // Now we're going to look for the closing '>'. There should not be any
            // intervening '<' (no nested stuff).
            //

            while( newStartIdx < s.Length )
            {
                if( s[ newStartIdx ] == '>' )
                {
                    newStartIdx = s.IndexOf( '<', newStartIdx + 1 ); // IndexOf can handle the +1 putting us past end
                    break;
                }
                else if( s[ newStartIdx ] == '<' )
                {
                    Util.Fail( Util.Sprintf( "Unexpected: do I need to handle nesting after \"::<\"? Positions {0}/{1}, string: \"{2}\"",
                                             startIdx,
                                             newStartIdx,
                                             s ) );
                }

                newStartIdx++;
            }

            return true;
        } // end _HasTwoColonsPreceding()


        private static bool _TryCrackTemplate( string name,
                                               int startIdx,
                                               out DbgTemplateNode templatePart,
                                               out string problem )
        {
            templatePart = null;
            problem = null;
            name = name.Trim();

            bool hasConst = false;
            if( name.StartsWith( "const ", StringComparison.OrdinalIgnoreCase ) )
            {
                // I haven't actually observed any type names with "const" at the
                // beginning, but it seems like it could be possible.
                hasConst = true;
                name = name.Substring( 6 ).Trim();
            }
            else if( name.EndsWith( " const", StringComparison.OrdinalIgnoreCase ) )
            {
                hasConst = true;
                name = name.Substring( 0, name.Length - 6 ).Trim();
            }

            int idx;
            if( !_LooksLikeATemplateName( name, startIdx, /* angleBracketIdx */ out idx ) )
            {
                templatePart = new DbgTemplateLeaf( name.Substring( startIdx ), hasConst );
                return true;
            }

            var templateName = name.Substring( startIdx, idx - startIdx );
            StringBuilder sb = new StringBuilder();
            DbgTemplateNode nestedType = null;
            int depth = 1;
            var templateParams = new List< DbgTemplateNode >();
            for( idx = idx + 1; idx < name.Length; idx++ ) // start after the first '<'
            {
                char c = name[ idx ];
                if( '<' == c )
                {
                    depth++;
                }
                else if( '>' == c )
                {
                    depth--;
                    if( depth < 0 )
                    {
                        problem = Util.Sprintf( "Unbalanced closing angle bracket at position {0}.", idx );
                        return false;
                    }

                    if( 0 == depth )
                    {
                        if( sb.Length > 0 )
                            templateParams.Add( CrackTemplate( sb.ToString().Trim() ) );

                        if( idx != (name.Length - 1) )
                        {
                            if( name[ idx + 1 ] == '+' )
                            {
                                // Nested type in a managed generic type.
                                idx += 2; // skip the ">+"
                            }
                            else if( (name.Length >= (idx + 4)) && // there has to be at least "::X"
                                     (name[ idx + 1 ] == ':')  &&
                                     (name[ idx + 2 ] == ':') )
                            {
                                // Nested type in a native template.
                                idx += 3; // skip the ">::"
                            }
                            else
                            {
                                problem = Util.Sprintf( "Unexpected characters at position {0}.", idx );
                                return false;
                            }

                            if( !_TryCrackTemplate( name, idx, out nestedType, out problem ) )
                            {
                                Util.Assert( !String.IsNullOrEmpty( problem ) );
                                return false;
                            }
                        }
                        break;
                    }
                }

                if( depth > 1 )
                {
                    sb.Append( c );
                }
                else
                {
                    Util.Assert( 1 == depth );
                    if( ',' == c )
                    {
                        // TODO: Hmm... I wonder if it's possible to get a symbol with ",," (which
                        // would lead to an empty part name, which will throw).
                        templateParams.Add( CrackTemplate( sb.ToString().Trim() ) );
                        sb.Clear();
                    }
                    else
                    {
                        sb.Append( c );
                    }
                }
            } // end for( each character )

            templatePart = new DbgTemplate( templateName,
                                            templateParams.AsReadOnly(),
                                            nestedType,
                                            hasConst );
            return true;
        } // end _TryCrackTemplate()


        /// <summary>
        ///    Converts a name into either a DbgTemplateLeaf or a DbgTemplate.
        /// </summary>
        public static DbgTemplateNode CrackTemplate( string name )
        {
            string problem;
            DbgTemplateNode templatePart;
            if( !_TryCrackTemplate( name, 0, out templatePart, out problem ) )
            {
                // TODO: SQM? I think that this can only happen if there are unbalanced brackets.
                // (or a case that we don't yet handle)
                LogManager.Trace( "Warning: weird type name: {0}", name );
                LogManager.Trace( "Warning: Problem: {0}", problem );
                Util.Fail( Util.Sprintf( "Weird type name ({0}): {1}", problem, name ) );
                templatePart = new DbgTemplateLeaf( name, false );
            }
            return templatePart;
        } // end CrackTemplate()
    } // end class DbgTemplateNode


    /// <summary>
    ///    Represents a template parameter that is not itself a template.
    /// </summary>
    /// <remarks>
    ///    Note that template parameters are not always type names: for instance, you
    ///    could have an integer parameter, such as to specify a buffer size. (And even
    ///    if it's a type name, sometimes there are bugs that prevent you from looking
    ///    up the type (TODO: INT4e198f3a: can't find _STL70).)
    ///
    ///    Additionally, we perform some "normalizations" of type names, such as
    ///    standardizing where the space and '*' go in pointer type names, and removing
    ///    "const" (that info is stored in the HasConst field).
    /// </remarks>
    public class DbgTemplateLeaf : DbgTemplateNode
    {
        public override string TemplateName { get { return FullName; } }

        public override bool IsTemplate { get { return false; } }


        // Fixes pointer names where people put whitespace before the '*'. Other stuff?
        private static string _FixName( string name )
        {
            if( null == name )
                throw new ArgumentNullException( "name" );

            name = name.Trim();

            if( (name.Length > 2) &&
                (name[ name.Length - 1 ] == '*') &&
                (name[ name.Length - 2 ] == ' ') )
            {
                int idx = name.Length - 3;
                while( idx > 0 )
                {
                    if( name[ idx ] != ' ' )
                        break;
                    idx--;
                }
                name = name.Substring( 0, idx + 1 ) + "*";
            }

            return name;
        } // end _FixName


        internal DbgTemplateLeaf( string name, bool hasConst )
            : base( _FixName( name ), hasConst )
        {
        } // end constructor

        public override bool Matches( DbgTemplateNode other )
        {
            // Special typename wildcard:
            if( IsEitherNameSingleTypeWildcard( FullName, other.FullName ) )
                return true;

            if( other.IsTemplate )
                return false;

            // TODO: PS Wildcard? Regex?
            return 0 == Util.Strcmp_OI( other.FullName, FullName );
        } // end Matches()

        public override int SingleTypeWildcardCount { get { return 0; } }
        public override bool HasMultiTypeWildcard { get { return false; } }
    } // end class DbgTemplateLeaf


    /// <summary>
    ///    Represents the type name of a template, including parameters.
    /// </summary>
    public class DbgTemplate : DbgTemplateNode
    {
        private string m_templateName;

        /// <summary>
        ///    The name of the template (minus template parameters).
        /// </summary>
        public override string TemplateName { get { return m_templateName; } }

        public readonly IReadOnlyList< DbgTemplateNode > Parameters;

        public override bool IsTemplate { get { return true; } }

        public DbgTemplateNode NestedNode { get; protected set; }

        public int NonWildcardParameterCount
        {
            get
            {
                int count = Parameters.Count - SingleTypeWildcardCount;
                if( HasMultiTypeWildcard )
                    count -= 1;

                DbgTemplate nestedTemplate = NestedNode as DbgTemplate;
                if( null != nestedTemplate )
                    count += nestedTemplate.NonWildcardParameterCount;

                return count;
            }
        }

        // TODO: perhaps this should be private as well, to prevent ill-formed templateName.
        internal DbgTemplate( string templateName,
                              IReadOnlyList< DbgTemplateNode > parameters,
                              DbgTemplateNode nestedType,
                              bool hasConst )
            : base( _VerifyParamsAndBuildFullTypeName( templateName,
                                                       parameters,
                                                       nestedType ),
                    hasConst )
        {
            if( null == parameters )
                throw new ArgumentNullException( "parameters" );

            m_templateName = templateName;
            Parameters = parameters;
            NestedNode = nestedType;
        } // end constructor


        private static string _VerifyParamsAndBuildFullTypeName( string templateName,
                                                                 IReadOnlyList< DbgTemplateNode > parameters,
                                                                 DbgTemplateNode nestedType )
        {
            if( String.IsNullOrEmpty( templateName ) )
                throw new ArgumentException( "You must supply a template name.", "templateName" );

            if( null == parameters )
                throw new ArgumentNullException( "parameters" );

            for( int i = 0; i < parameters.Count; i++ )
            {
                if( null == parameters[ i ] )
                {
                    throw new ArgumentException( "Null parameters not allowed.",
                                                 Util.Sprintf( "parameters[ {0} ]", i ) );
                }

                if( (parameters[ i ].FullName == c_MultiTypeWildcard) &&
                    (i != (parameters.Count - 1)) )
                {
                    throw new ArgumentException( Util.Sprintf( "The '{0}' wildcard must be the last parameter of a template.",
                                                               c_MultiTypeWildcard ),
                                                 "parameters" );
                }
            } // end for( each param )

            StringBuilder sb = new StringBuilder( templateName.Length +
                                                    (parameters.Count * (templateName.Length * 3)));
            sb.Append( templateName );
            if( parameters.Count > 0 )
            {
                sb.Append( '<' );
                bool first = true;
                foreach( var pn in parameters )
                {
                    if( first )
                        first = false;
                    else
                        sb.Append( ',' );
                    sb.Append( pn );
                }
                sb.Append( '>' );
            }
            if( null != nestedType )
            {
                sb.Append( "::" );
                sb.Append( nestedType.FullName );
            }
            return sb.ToString();
        } // end _VerifyParamsAndBuildFullTypeName()


        public override bool Matches( DbgTemplateNode other )
        {
            // Special typename wildcard:
            if( IsEitherNameSingleTypeWildcard( other.FullName, FullName ) )
                return true;

            if( !other.IsTemplate )
                return false;

            DbgTemplate dtOther = (DbgTemplate) other;

            // TODO: PS Wildcard? Regex?
            if( 0 != Util.Strcmp_OI( dtOther.TemplateName, TemplateName ) )
                return false;

            for( int i = 0; i < dtOther.Parameters.Count; i++ )
            {
                if( i >= Parameters.Count )
                    return false;

                // Special typename wildcard:
                if( IsMultiMatchWildcard( dtOther.Parameters[ i ] ) )
                {
                    if( i != (dtOther.Parameters.Count - 1) )
                    {
                        Util.Fail( "Not reachable." ); // construction of such now disallowed.
                        throw new ArgumentException( Util.Sprintf( "The '{0}' placeholder can only come last in a template parameter list.",
                                                                   c_MultiTypeWildcard ),
                                                     "other" );
                    }
                    break;
                }

                // Special typename wildcard:
                if( IsMultiMatchWildcard( Parameters[ i ] ) )
                {
                    if( i != (Parameters.Count - 1) )
                        throw new ArgumentException( Util.Sprintf( "The '{0}' placeholder can only come last in a template parameter list.",
                                                                   c_MultiTypeWildcard ),
                                                     "this" );

                    break;
                }

                if( !Parameters[ i ].Matches( dtOther.Parameters[ i ] ) )
                    return false;
            }

            if( (null == NestedNode) != (null == dtOther.NestedNode) )
            {
                // One has a nested node, and the other doesn't.
                return false;
            }

            if( null != NestedNode )
                return NestedNode.Matches( dtOther.NestedNode );
            else
                return true;
        } // end Matches()


        private bool m_countInitialized;
        private bool m_hasMultiTypeWildcard;
        private int m_singleTypeWildcardCount;

        private void _CountWildcards()
        {
            if( m_countInitialized )
                return;

            foreach( var tp in Parameters )
            {
                if( tp.FullName == c_SingleTypeWildcard )
                    m_singleTypeWildcardCount++;
                else if( tp.FullName == c_MultiTypeWildcard )
                    m_hasMultiTypeWildcard = true;
            }

            if( null != NestedNode )
            {
                m_singleTypeWildcardCount += NestedNode.SingleTypeWildcardCount;
                m_hasMultiTypeWildcard = m_hasMultiTypeWildcard || NestedNode.HasMultiTypeWildcard;
            }
            m_countInitialized = true;
        } // end _CountWildcards()

        public override int SingleTypeWildcardCount
        {
            get
            {
                _CountWildcards();
                return m_singleTypeWildcardCount;
            }
        }

        public override bool HasMultiTypeWildcard
        {
            get
            {
                _CountWildcards();
                return m_hasMultiTypeWildcard;
            }
        }
    } // end class DbgTemplate
}

