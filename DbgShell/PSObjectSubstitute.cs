using System.Management.Automation;
using System.Management.Automation.Internal;

namespace MS.DbgShell
{
    internal partial class ColorHostUserInterface
    {
        private static class PSObjectSubstitute
        {
            // Replacement for PSObject.Base( object )
            public static object Base( object obj )
            {
                PSObject objAsPso = obj as PSObject;
                if (objAsPso == null)
                {
                    return null;
                }
                if (objAsPso == AutomationNull.Value)
                {
                    return null;
                }
                //if (objAsPso.immediateBaseObjectIsEmpty)
                if (objAsPso.ImmediateBaseObject is PSCustomObject)
                {
                    return obj;
                }
                object obj2;
                do
                {
                    obj2 = objAsPso.ImmediateBaseObject;
                    objAsPso = (obj2 as PSObject);
                }
                //while (objAsPso != null && !objAsPso.immediateBaseObjectIsEmpty);
                while (objAsPso != null && !(objAsPso.ImmediateBaseObject is PSCustomObject));
                return obj2;
            }
        } // end PSObjectSubstitute
    }
}

