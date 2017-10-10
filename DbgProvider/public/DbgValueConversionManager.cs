using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents an object that can produce an object representing the value of the
    ///    specified symbol.
    /// </summary>
    /// <remarks>
    ///    A converter is used when there is a custom symbol-->value conversion,
    ///    overriding the built-in, default value conversion. For instance, to convert
    ///    STL strings into System.String objects.
    /// </remarks>
    public interface IDbgValueConverter
    {
        /// <summary>
        ///    Given a symbol, produces an object representing the value of that symbol.
        /// </summary>
        /// <remarks>
        ///    Note to implementors: A good place to start is symbol.GetStockValue(),
        ///    which will let you easily access the symbol value's fields (cast to
        ///    dynamic).
        /// </remarks>
        object Convert( DbgSymbol symbol );
    }


    /// <summary>
    ///    Represents information about an IDbgValueConverter.
    /// </summary>
    [DebuggerDisplay( "DbgValueConverterInfo: {m_typeName}" )]
    public class DbgValueConverterInfo : IHaveTemplateTypeName
    {
        private readonly DbgTemplateNode m_typeName;

        /// <summary>
        ///    The type which Converter knows how to convert.
        /// </summary>
        /// <remarks>
        ///    Note that this might not be a definite type name--it may not be
        ///    module-qualified, and it could contain template wildcards.
        /// </remarks>
        public DbgTemplateNode TypeName { get { return m_typeName; } }

        /// <summary>
        ///    If not null or empty, the converter only applies to the type when it is
        ///    from the specified module.
        /// </summary>
        public readonly string ScopingModule;

        /// <summary>
        ///    The IDbgValueConverter object that knows how to take symbols of the
        ///    specified type and product objects that represent the values.
        /// </summary>
        public readonly IDbgValueConverter Converter;

        /// <summary>
        ///    The script that registered this converter.
        /// </summary>
        /// <remarks>
        ///    In addition to being informative, this is used when reloading converter
        ///    definitions. It can be null (for instance, if the script was registered
        ///    via an interactive prompt, or is written in C#).
        /// </remarks>
        public readonly string SourceScript;

        /// <summary>
        ///    Creates a new DbgValueConverterInfo object.
        /// </summary>
        /// <param name="typeName">
        ///    This can be module-qualified (like "dfsrs!_FILETIME"), or not (like
        ///    "!_FILETIME").
        /// </param>
        public DbgValueConverterInfo( string typeName, IDbgValueConverter converter, string sourceScript )
        {
            if( String.IsNullOrEmpty( typeName ) )
                throw new ArgumentException( "You must supply a type name.", "typeName" );

            if( null == converter )
                throw new ArgumentNullException( "converter" );

            int bangIdx = typeName.IndexOf( '!' );
            if( bangIdx < 0 )
            {
                m_typeName = DbgTemplateNode.CrackTemplate( typeName );
            }
            else
            {
                ScopingModule = typeName.Substring( 0, bangIdx );
                if( bangIdx == (typeName.Length - 1) )
                    throw new ArgumentException( "No type name after the '!'.", typeName );

                var tn = typeName.Substring( bangIdx + 1 );
                m_typeName = DbgTemplateNode.CrackTemplate( tn );
            }

            Converter = converter;
            SourceScript = sourceScript;
        } // end constructor
    } // end class DbgValueConverterInfo


    //
    // This is the entry point for our value conversion extension facility. It was
    // previously implemented as a provider in order to be able to store conversion
    // information per-runspace (as opposed to per-process). It's been changed to store
    // information globally because loading additional providers causes a bit of a
    // slowdown during startup, especially when being run under a debugger. We don't need
    // to have the info stored per-runspace, because dbgeng is global anyway. And we don't
    // actually need any provider functionality (like drives, namespace navigation, etc.).
    //
    // All the conversion stuff is exposed as static methods, which forward to a global
    // singleton instance of DbgValueConversionManagerInfo that does the actual work.
    //
    // The value conversion manager allows custom code/scripts to be registered, which are
    // to be used to transform raw symbols into some sort of object for further
    // manipulation (by the user ar script).
    //
    public partial class DbgValueConversionManager
    {
        private static DbgValueConversionManagerInfo __singleton;
        private static DbgValueConversionManagerInfo _Singleton
        {
            get
            {
                if( null == __singleton )
                {
                    __singleton = new DbgValueConversionManagerInfo();
                }
                return __singleton;
            }
        }

        public static DbgValueConverterInfo ChooseConverterForSymbol( DbgSymbol symbol )
        {
            return _Singleton.ChooseConverterForSymbol( symbol );
        }

        public static IEnumerable< DbgValueConverterInfo > EnumerateAllConvertersForSymbol( DbgSymbol symbol )
        {
            return _Singleton.EnumerateAllConvertersForSymbol( symbol );
        }

        public static void RegisterConverter( DbgValueConverterInfo converterInfo )
        {
            _Singleton.RegisterConverter( converterInfo );
        }

        public static IEnumerable< DbgValueConverterInfo > GetEntries()
        {
            return _Singleton.EnumerateEntries();
        }

        public static IEnumerable< DbgValueConverterInfo > GetMatchingEntries( string modFilter,
                                                                               string templateFilter )
        {
            return GetMatchingEntries( modFilter, templateFilter, false );
        }

        public static IEnumerable< DbgValueConverterInfo > GetMatchingEntries( string modFilter,
                                                                               string templateFilter,
                                                                               bool exactMatchOnly )
        {
            return _Singleton.GetMatchingEntries( modFilter, templateFilter, exactMatchOnly );
        } // end GetMatchingEntries()


        public static void Reload( CommandInvocationIntrinsics invokeCommand,
                                   IList< string > appendScripts,
                                   IList< string > prependScripts,
                                   IPipelineCallback pipe )
        {
            if( null == pipe )
                pipe = new DummyPipelineCallback();
            _Singleton.Reload( invokeCommand, appendScripts, prependScripts, pipe );
        }

        public static void RemoveEntries( string modFilter,
                                          string templateFilter,
                                          bool exactMatchOnly )
        {
            _Singleton.RemoveEntries( modFilter, templateFilter, exactMatchOnly );
        }

        public static void ScrubFileList()
        {
            _Singleton.ScrubFileList();
        }
    } // end class DbgValueConversionManager
}

