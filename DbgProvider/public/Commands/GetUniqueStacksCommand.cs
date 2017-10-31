using Microsoft.Diagnostics.Runtime;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Management.Automation;

namespace MS.Dbg.Commands
{
    [Cmdlet( VerbsCommon.Get, "UniqueStacks" )]
    //[OutputType( typeof(  ) )]
    public class GetUniqueStacksCommand : DbgBaseCommand
    {

        protected override void ProcessRecord()
        {
            //var d = new ListIndexableDictionary< DbgStackInfo, List< DbgStackInfo > >( DbgStackInfo.FramesComparer.Instance );
            var d = new Dictionary< DbgStackInfo, List< DbgStackInfo > >( DbgStackInfo.FramesComparer.Instance );

            foreach( var ti in Debugger.EnumerateThreads() )
            {
                List< DbgStackInfo > list;
                if( !d.TryGetValue( ti.Stack, out list ) )
                {
                    list = new List< DbgStackInfo >();
                 // SafeWriteObject( ti );
                    SafeWriteObject( ti.Stack );
                }
                list.Add( ti.Stack );
            } // end foreach( thread )

            // TODO: do something with d?
        } // end ProcessRecord()
    } // end GetUniqueStacksCommand
}

