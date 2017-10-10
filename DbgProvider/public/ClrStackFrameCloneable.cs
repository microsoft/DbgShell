using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;

namespace MS.Dbg
{
    /// <summary>
    ///    Workaround for the fact that ClrStackFrame is not cloneable, because we want to
    ///    be able to dynamically attach properties to stack frames without them following
    ///    around the C# objects forever.
    /// </summary>
    public class ClrStackFrameCloneable : ClrStackFrame, ICloneable
    {
        private ClrStackFrame m_actual;


        public ClrStackFrameCloneable( ClrStackFrame actual )
        {
            if( null == actual )
                throw new ArgumentNullException( "actual" );

            m_actual = actual;
        } // end constructor


        public override ClrThread Thread { get { return m_actual.Thread; } }

        public override string DisplayString { get { return m_actual.DisplayString; } }

        public override ulong InstructionPointer { get { return m_actual.InstructionPointer; } }

        public override ClrStackFrameType Kind { get { return m_actual.Kind; } }

        public override ClrMethod Method { get { return m_actual.Method; } }

        public override ulong StackPointer { get { return m_actual.StackPointer; } }

        public override IList<ClrValue> Arguments { get { return m_actual.Arguments; } }

        public override IList<ClrValue> Locals { get { return m_actual.Locals; } }

        public override ClrRuntime Runtime { get { return m_actual.Runtime; } }


        public object Clone()
        {
            return new ClrStackFrameCloneable( m_actual );
        }
    } // end class ClrStackFrameCloneable
}
