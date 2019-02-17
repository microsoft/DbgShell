using System;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Management.Automation.Runspaces;

namespace MS.Dbg.Formatting
{
    public interface IFormatInfo
    {
        string FormatCommand { get; }
        string Module { get; }

        // This is ugly...
        bool LooksLikeExistingFromPropertyDefinition( IFormatInfo other );
    }


    public class ViewDefinitionInfo
    {
        public readonly IFormatInfo ViewDefinition;
        public readonly string SourceScript; // if null, it was from interactive
        internal ViewDefinitionInfo( IFormatInfo viewDef, string sourceScript )
        {
            if( null == viewDef )
                throw new ArgumentNullException( "viewDef" );

            ViewDefinition = viewDef;
            SourceScript = sourceScript;
        }
    } // end class ViewDefinitionInfo


    internal class GenericViewDefinitionInfoList : IHaveTemplateTypeName
    {
        private readonly DbgTemplateNode m_templateTypeName;
        public DbgTemplateNode TypeName
        {
            get { return m_templateTypeName; }
        }

        public readonly List< ViewDefinitionInfo > Views;

        internal GenericViewDefinitionInfoList( DbgTemplateNode templateTypeName, IEnumerable< ViewDefinitionInfo > views )
        {
            if( null == templateTypeName )
                throw new ArgumentNullException( "templateTypeName" );

            m_templateTypeName = templateTypeName;
            Views = new List< ViewDefinitionInfo >( views );
        }
        internal GenericViewDefinitionInfoList( DbgTemplateNode templateTypeName, int sizeHint )
        {
            if( null == templateTypeName )
                throw new ArgumentNullException( "templateTypeName" );

            m_templateTypeName = templateTypeName;
            Views = new List< ViewDefinitionInfo >( sizeHint );
        }
    } // end class GenericViewDefinitionInfoList


    public class AltTypeFormatEntry
    {
        public readonly string TypeName;
        public IReadOnlyList< ViewDefinitionInfo > ViewDefinitions { get; private set; }

        public AltTypeFormatEntry( string typeName,
                                   IReadOnlyList< IFormatInfo > viewDefinitions,
                                   string sourceScript )
        {
            if( String.IsNullOrEmpty( typeName ) )
                throw new ArgumentException( "You must supply a type name.", "typeName" );

            if( null == viewDefinitions )
                throw new ArgumentNullException( "viewDefinitions" );

            TypeName = typeName;
            var list = new List< ViewDefinitionInfo >( viewDefinitions.Count );
            foreach( var formatInfo in viewDefinitions )
            {
                if( null == formatInfo )
                    throw new ArgumentNullException( "viewDefinitions", "The viewDefinitions argument must not contain null entries." );

                list.Add( new ViewDefinitionInfo( formatInfo, sourceScript ) );
            }

            ViewDefinitions = list.AsReadOnly();
        } // end constructor
    } // end clas AltTypeFormatEntry


    // TODO: Could this be collapsed with ViewDefinitionInfo?
    public class AltTypeFormatEntryPair
    {
        public readonly string TypeName;
        public readonly ViewDefinitionInfo ViewDefinitionInfo;
        internal AltTypeFormatEntryPair( string typeName,
                                         ViewDefinitionInfo viewDefinitionInfo )
        {
            TypeName = typeName;
            ViewDefinitionInfo = viewDefinitionInfo;
        }
    } // end class AltTypeFormatEntryPair


    //
    // This is the entry point for our alternate formatting engine. It was previously
    // implemented as a provider in order to be able to store conversion information
    // per-runspace (as opposed to per-process). It's been changed to store information
    // globally because loading additional providers causes a bit of a slowdown during
    // startup, especially when being run under a debugger. We don't need to have the info
    // stored per-runspace, because dbgeng is global anyway. And we don't actually need
    // any provider functionality (like drives, namespace navigation, etc.).
    //
    // All the conversion stuff is exposed as static methods, which forward to a global
    // singleton instance of DbgValueConversionManagerInfo that does the actual work.
    //
    // All the type formatting stuff is exposed as static methods, which forward to a
    // global singleton instance of AltFormattingManagerInfo that does the actual work.
    //
    // Q: Why do we need an alternate formatting engine?
    //
    // A: The are a few reasons:
    //
    //    1) Colorization:
    //
    //       a) We need a formatting engine that will treat control sequences as
    //          zero-width:
    //
    //          We want to take advantage of color, and we do that by marking up strings
    //          with ISO/IEC 6429 control sequences. The built-in formatting engine does
    //          not recognize these color sequences, so they add length to strings, but
    //          they should really be considered zero-length, since they are stripped out
    //          when displayed.
    //
    //       b) We need a formatting engine that is control sequence-preserving:
    //
    //          In addition to not treating control sequences as zero-width, sometimes
    //          formatting needs to truncate strings (such as to fit a value into a table
    //          column). Truncating in the middle of a control sequence, or chopping of
    //          the end of a string with a POP control sequence, wreaks havoc on the color
    //          state.
    //
    //       (Perhaps this is just a specific case of reason #2, but it's a very important
    //       one.)
    //
    //    2) General improvement over the built-in formatting engine:
    //
    //       a) Creating format view definitions for the built-in PowerShell formatting
    //          engine is painful and somewhat esoteric. We can make it easier.
    //
    //       b) The built-in formatting engine is relatively "set"--with the exception of
    //          Format-Custom, there isn't much opportunity to do serious customization of
    //          the formatting and display experience. By implementing our own engine, we
    //          can add useful features such as footers for tables, an auto-generated
    //          index column, etc.
    //
    // Q: How do we invoke the alternate formatting engine?
    //
    // A: You can invoke the alternate formatting cmdlets directly (Format-AltTable,
    //    Format-AltList, Format-AltCustom, etc.), but you can also just use the regular
    //    formatting cmdlets because we have defined "proxy" functions for them. The proxy
    //    functions will check to see if you have an alternate format view definition for
    //    the type of object you are trying to format, and if so, forwards the call on to
    //    the alternate formatting cmdlet; if not, it sends it on to the built-in
    //    formatting cmdlet.
    //
    // Q: How do I create format view definitions for the alternate formatting engine?
    //
    // A: Instead of a .ps1xml file, you write a .psfmt file, which contains PowerShell
    //    script. In the script, you call various cmdlets to define your format views,
    //    such as New-AltScriptColumn, New-AltTableViewDefinition, etc., culminating in a
    //    call to Register-AltTypeFormatEntries. Take a look at Debugger.psfmt for an
    //    example.
    //
    public static partial class AltFormattingManager
    {
        private static AltFormattingManagerInfo __singleton;
        private static AltFormattingManagerInfo _Singleton
        {
            get
            {
                if( null == __singleton )
                {
                    __singleton = new AltFormattingManagerInfo();
                }
                return __singleton;
            }
        }


        // We'll use this for things that implement ISupportColor but don't otherwise have
        // a registered alternate formatting view. The trick here is that the alternate
        // powershell host has a built-in rule for objects that implement ISupportColor:
        // to call ToString( $HostSupportsColor ) on them.
        private static readonly AltCustomViewDefinition sm_customIdentityView = new AltCustomViewDefinition( ScriptBlock.Create( "$_" ) );

        internal const string DefaultViewDefPropName = " _ defaultViewDef";

        private const BindingFlags sm_allInstanceMembers = BindingFlags.Public |
                                                           BindingFlags.NonPublic |
                                                           BindingFlags.Instance;


        private static void _MarkPropertyHidden( PSNoteProperty prop )
        {
            // We don't want the view definition stuff that we tack on to show up
            // anywhere, so we'll mark it hidden. Unfortunately this requires some
            // reflection.
            //
            // There is a property, but it does not have a setter in v3/v4, so we try for
            // a field first, and if we can't find that, try the property for v5.

            Type t = prop.GetType();

            FieldInfo fi = t.GetField( "isHidden", sm_allInstanceMembers );
            if( null != fi )
            {
                fi.SetValue( prop, true );
            }
            else
            {
                PropertyInfo pi = t.GetProperty( "IsHidden", sm_allInstanceMembers );
                if( null != pi )
                {
                    pi.SetValue( prop, true );
                }
                else
                {
                    // Oh dear... where is it?!
                    throw new Exception( "Cannot find \"isHidden\" member on PSNoteProperty." );
                }
            }
        } // end _MarkPropertyHidden()


        /// <summary>
        ///    Sets an object-specific default view for a particular object (which will
        ///    override views registered by type name when selecting a default view). This
        ///    works by attaching a hidden property to the object.
        /// </summary>
        public static void SetDefaultFormatInfoForObject( PSObject pso, IFormatInfo formatInfo )
        {
            PSPropertyInfo pspi = pso.Properties[ DefaultViewDefPropName ];
            if( null != pspi )
            {
                // It already exists. We'll just change the value.
                pspi.Value = formatInfo;
            }
            else
            {
                // Need to tack it on.
                PSNoteProperty psnp = new PSNoteProperty( DefaultViewDefPropName, formatInfo );
                _MarkPropertyHidden( psnp );
                pso.Properties.Add( psnp );
            }
        } // end SetDefaultFormatInfoForObject()


        /// <summary>
        ///    Given a PSObject, choose a ViewDefinitionInfo for it. Optionally specify
        ///    the desired view definition type. The ViewDefinitionInfo may come from the
        ///    list of registered views, or from a special property on the object, or be
        ///    auto-supplied for ISupportColor objects.
        ///
        ///    If an IFormatInfo cannot be found, returns null.
        /// </summary>
        /// <param name="formatInfoType">
        ///    Optional: if null, will return the first view definition it finds,
        ///    excluding AltSingleLineViewDefinitions.
        /// </param>
        public static IFormatInfo ChooseFormatInfoForPSObject( PSObject outputObject,
                                                               Type formatInfoType )
        {
            if( null == outputObject )
                throw new ArgumentNullException( "outputObject" );

            IFormatInfo fi = null;

            //
            // First check the hidden property for an object-specific view.
            //

            PSPropertyInfo pspi = outputObject.Properties[ DefaultViewDefPropName ];
            if( (null != pspi) && (null != pspi.Value) )
            {
                if( !(pspi.Value is IFormatInfo) )
                {
                    Util.Fail( "Expected hidden property to be an IFormatInfo." );
                }
                else
                {
                    fi = (IFormatInfo) pspi.Value;
                }
            }

            //
            // But wait... does it match the requested view type?
            //
            // If there is a requested type (formatInfoType is not null), but it matches
            // the object-specific default view... should we use the object specific
            // default view?
            //
            // I think it could go either way, but I'm going to choose to go with the
            // object-specific default view, because say somebody does:
            //
            //    $stuff = Invoke-SomeCommand
            //    $stuff
            //    $stuff | ft
            //
            // Where "$stuff" has object-specific default views attached, and they are
            // table views. The user would probably expect to get the same output for
            // second and third commands.
            //

            if( (null != fi) &&
                ((null == formatInfoType) ||
                 (formatInfoType.IsAssignableFrom( fi.GetType() ))) )
            {
                return fi;
            }

            //
            // Okay, now check for views registered for all instances of the object's
            // type.
            //

            var viewInfo = ChooseFormatInfoForPSObjectFromRegistered( outputObject,
                                                                      formatInfoType );

            if( null != viewInfo )
            {
                return viewInfo.ViewDefinition;
            }
            else if( ((null == formatInfoType) || (typeof(AltCustomViewDefinition) == formatInfoType)) &&
                     (outputObject.BaseObject is ISupportColor) )
            {
                // For objects that implement ISupportColor but don't have a registered
                // view, we'll supply a view for them.
                return sm_customIdentityView;
            }

            return null;
        }

        /// <summary>
        ///    Given a PSObject, searches through registered ViewDefinitionInfo for a
        ///    match. Optionally specify the desired view definition type. Returns null if
        ///    no match found.
        /// </summary>
        /// <param name="formatInfoType">
        ///    Optional: if null, will return the first view definition it finds,
        ///    excluding AltSingleLineViewDefinitions.
        /// </param>
        private static ViewDefinitionInfo ChooseFormatInfoForPSObjectFromRegistered( PSObject outputObject, Type formatInfoType )
        {
            if( null == outputObject )
                throw new ArgumentNullException( "outputObject" );

            return ChooseFormatInfoForTypeNamesFromRegistered( outputObject.TypeNames, formatInfoType );
        }

        /// <summary>
        ///    Given a set of type names, choose a ViewDefinitionInfo for it. Optionally
        ///    specify the desired view definition type. Returns null if no match found.
        /// </summary>
        /// <param name="typeNames">
        ///    This can come from, for example PSObject.TypeNames. (They don't have to be
        ///    names of actual .NET types.)
        /// </param>
        /// <param name="formatInfoType">
        ///    Optional: if null, will return the first view definition it finds,
        ///    excluding AltSingleLineViewDefinitions.
        /// </param>
        public static ViewDefinitionInfo ChooseFormatInfoForTypeNamesFromRegistered( IEnumerable<string> typeNames, Type formatInfoType )
        {
            return _Singleton.ChooseFormatInfoForTypeNames( typeNames, formatInfoType );
        }


        // N.B.: Does not enumerat an object-specific view (if there is one attached to
        // the object).
        public static IEnumerable<AltTypeFormatEntryPair> EnumerateAllFormatInfoForPSObject( PSObject outputObject,
                                                                                             Type formatInfoType )
        {
            if( null == outputObject )
                throw new ArgumentNullException( "outputObject" );

            return _Singleton.EnumerateAllFormatInfoForTypeNames( outputObject.TypeNames, formatInfoType );
        }

        public static TFormatInfo ChooseFormatInfoForPSObject<TFormatInfo>( PSObject outputObject ) where TFormatInfo : class
        {
            ViewDefinitionInfo vdi = ChooseFormatInfoForPSObjectFromRegistered( outputObject, typeof( TFormatInfo ) );
            if( null != vdi )
                return (TFormatInfo) vdi.ViewDefinition;
            else
                return null;
        }

        public static ViewDefinitionInfo ChooseSingleLineFormatInfoForPSObject( PSObject outputObject )
        {
            if( null == outputObject )
                throw new ArgumentNullException( "outputObject" );

            return _Singleton.ChooseSingleLineFormatInfoForTypeNames( outputObject.TypeNames );
        }

        public static void RegisterViewDefinition( AltTypeFormatEntry typeEntry )
        {
            _Singleton.RegisterViewDefinition( typeEntry );
        }

        public static bool RemoveByName( string typeName )
        {
            return _Singleton.RemoveByName( typeName );
        }

        public static void ScrubFileList()
        {
            _Singleton.ScrubFileList();
        }

        public static IReadOnlyDictionary< string, IReadOnlyList< ViewDefinitionInfo > > GetEntries()
        {
            return _Singleton.GetEntries();
        }

        public static IReadOnlyDictionary< string, IReadOnlyList< ViewDefinitionInfo > > GetEntriesOfType( Type formatInfoType )
        {
            return _Singleton.GetEntriesOfType( formatInfoType );
        }

        public static void Reload( CommandInvocationIntrinsics invokeCommand,
                                   IList< string > appendScripts,
                                   IList< string > prependScripts,
                                   IPipelineCallback pipe )
        {
            if( null == pipe )
                pipe = new DummyPipelineCallback();
            _Singleton.Reload( invokeCommand, appendScripts, prependScripts, pipe );
        }


        public static event EventHandler<AltTypeFormatEntry> ViewDefinitionRegistered
        {
            add
            {
                _Singleton.ViewDefinitionRegistered += value;
            }
            remove
            {
                _Singleton.ViewDefinitionRegistered -= value;
            }
        }


        /// <summary>
        ///    Converts the name of a generic type to something more like you would see in
        //     source code.
        /// </summary>
        /// <remarks>
        ///    This function /should/ have no effect on native template type names (and
        ///    isGeneric should be returned false). However the current method of
        ///    discrimination between managed generics and native templates is the
        ///    presence of a backtick character, which might be too loose. We'll see.
        /// </remarks>
        public static string ReformManagedTypeName( string inputTypeName, bool removeNamespaces )
        {
            bool isGeneric;
            return ReformManagedTypeName( inputTypeName, removeNamespaces, out isGeneric );
        }

        public static string ReformManagedTypeName( string inputTypeName, bool removeNamespaces, out bool isGeneric )
        {
            return Util.MassageManagedTypeName( inputTypeName, removeNamespaces, out isGeneric );
        }
    } // end class AltFormattingManager
}

