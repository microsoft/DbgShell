using System;
using System.Collections.Generic;
using System.Management.Automation;
using MS.Dbg.Commands;

namespace MS.Dbg.Formatting.Commands
{
    [Cmdlet( VerbsCommon.Get, "AltTypeFormatEntry", DefaultParameterSetName = c_TypeNameParamSet )]
    [OutputType( typeof( AltTypeFormatEntryPair ) )]
    public class GetAltTypeFormatEntryCommand : DbgBaseCommand
    {
        private const string c_TypeNameParamSet = "TypeNameParamSet";
        private const string c_PSObjectParamSet = "PSObjectParamSet";

        [Parameter( Mandatory = false,
                    Position = 0,
                    ValueFromPipeline = true,
                    ParameterSetName = c_TypeNameParamSet )]
        [SupportsWildcards]
        public string TypeName { get; set; }

        [Parameter( Mandatory = true,
                    Position = 0,
                    ValueFromPipeline = true,
                    ParameterSetName = c_PSObjectParamSet )]
        [ValidateNotNull()]
        public PSObject InputObject { get; set; }

        [Parameter( Mandatory = false, Position = 1 )]
        public Type FormatInfoType { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( null != InputObject )
            {
                Util.Assert( ParameterSetName == c_PSObjectParamSet );
                foreach( var fep in AltFormattingManager.EnumerateAllFormatInfoForPSObject( InputObject,
                                                                                            FormatInfoType ) )
                {
                    WriteObject( fep );
                }
                return;
            }

            var map = AltFormattingManager.GetEntries();

            if( String.IsNullOrEmpty( TypeName ) )
            {
                foreach( string key in map.Keys )
                {
                    foreach( var viewInfo in map[ key ] )
                    {
                        if( (null == FormatInfoType) || (FormatInfoType.IsAssignableFrom( viewInfo.ViewDefinition.GetType() )) )
                            WriteObject( new AltTypeFormatEntryPair( key, viewInfo ) );
                    }
                }
            }
            else if( WildcardPattern.ContainsWildcardCharacters( TypeName ) )
            {
                WildcardPattern pattern = new WildcardPattern( TypeName,
                                                               WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase );
                foreach( string key in map.Keys )
                {
                    if( pattern.IsMatch( key ) )
                    {
                        foreach( var viewInfo in map[ key ] )
                        {
                            if( (null == FormatInfoType) || (FormatInfoType.IsAssignableFrom( viewInfo.ViewDefinition.GetType() )) )
                                WriteObject( new AltTypeFormatEntryPair( key, viewInfo ) );
                        }
                    }
                }
            }
            else
            {
                IReadOnlyList< ViewDefinitionInfo > list;
                if( !map.TryGetValue( TypeName, out list ) )
                {
                    SafeWriteError( Util.Sprintf( "There is no type format entry for type name '{0}'.",
                                                  TypeName ),
                                    "TypeNameNotFound",
                                    ErrorCategory.ObjectNotFound,
                                    TypeName );
                }
                else
                {
                    foreach( var formatInfo in list )
                    {
                        if( (null == FormatInfoType) ||
                            (FormatInfoType.IsAssignableFrom( formatInfo.ViewDefinition.GetType() )) )
                        {
                            WriteObject( new AltTypeFormatEntryPair( TypeName, formatInfo ) );
                        }
                    }
                }
            }
        } // end ProcessRecord()

        protected override bool TrySetDebuggerContext { get { return false; } }
        protected override bool LogCmdletOutline { get { return false; } }
    } // end class GetAltTypeFormatEntryCommand
}

