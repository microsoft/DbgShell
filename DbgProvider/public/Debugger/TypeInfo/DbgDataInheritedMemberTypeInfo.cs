using System;
using System.Diagnostics;

namespace MS.Dbg
{
    [DebuggerDisplay( "Inherited Data Member: Id {TypeId}: {Name}" )]
    public class DbgDataInheritedMemberTypeInfo : DbgDataMemberTypeInfo
    {
        private static DbgEngDebugger _GetDebugger( DbgDataMemberTypeInfo member )
        {
            if( null == member )
                throw new ArgumentNullException( "member" );

            return member.Debugger;
        }

        internal DbgDataInheritedMemberTypeInfo( DbgDataMemberTypeInfo member, uint baseOffset )
            : base( _GetDebugger( member ),
                    member.Module,
                    member.TypeId,
                    member.Name,
                    member.DataKind,
                    member.m_memberTypeId,
                    member.m_owningTypeId,
                    member.Offset + baseOffset,
                    member.BitfieldLength,
                    member.BitfieldPosition )
        {
        } // end constructor
    } // end class DbgDataInheritedMemberTypeInfo
}

