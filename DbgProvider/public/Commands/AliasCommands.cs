using System;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "DbgAlias" )]
    [OutputType( typeof( DbgAlias ) )]
    public class GetDbgAliasCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = false, Position = 0, ValueFromPipeline = true )]
        [SupportsWildcards] // or does this go on the cmdlet?
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( String.IsNullOrEmpty( Name ) )
                Name = "*";

            WildcardPattern pat = new WildcardPattern( Name );
            foreach( var alias in Debugger.EnumerateTextReplacements() )
            {
                if( pat.IsMatch( alias.Name ) )
                    WriteObject( alias );
            }
        } // end ProcessRecord()

        protected override bool TrySetDebuggerContext { get { return false; } }
    } // end class GetDbgAliasCommand


    [Cmdlet( VerbsCommon.Set, "DbgAlias" )]
    public class SetDbgAliasCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true, Position = 0)]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; }

        [Parameter( Mandatory = true, Position = 1)]
        [AllowNull]
        [AllowEmptyString]
        public string Value { get; set; }


        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            Debugger.SetTextReplacement( Name, Value );
        } // end ProcessRecord()


        protected override bool TrySetDebuggerContext { get { return false; } }
    } // end class SetDbgAliasCommand


    // You can use "Set-DbgAlias <name> $null" to remove an alias, but a) that's not very
    // discoverable, and b) nulls tend to get converted to empty strings in PowerShell, so
    // we'll let people use Remove-DbgAlias. As an added benefit, it can do bulk removes
    // using wildcards.
    [Cmdlet( VerbsCommon.Remove, "DbgAlias" )]
    public class RemoveDbgAliasCommand : DbgBaseCommand
    {
        [Parameter( Mandatory = true, Position = 0)]
        [ValidateNotNullOrEmpty]
        [SupportsWildcards]
        public string Name { get; set; }

        protected override void ProcessRecord()
        {
            base.ProcessRecord();

            if( WildcardPattern.ContainsWildcardCharacters( Name ) )
            {
                WildcardPattern pat = new WildcardPattern( Name );
                foreach( var alias in Debugger.EnumerateTextReplacements() )
                {
                    if( pat.IsMatch( alias.Name ) )
                    {
                        SafeWriteVerbose( "Removing alias '{0}'.", alias.Name );
                        Debugger.SetTextReplacement( alias.Name, null );
                    }
                }
            }
            else
            {
                SafeWriteVerbose( "Removing alias '{0}'.", Name );
                Debugger.SetTextReplacement( Name, null );
            }
        } // end ProcessRecord()


        protected override bool TrySetDebuggerContext { get { return false; } }
    } // end class RemoveDbgAliasCommand
}

