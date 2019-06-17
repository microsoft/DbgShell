/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/



using System;
using System.Text;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Internal;
using Microsoft.PowerShell;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;


using ConsoleHandle = Microsoft.Win32.SafeHandles.SafeFileHandle;
using HRESULT = System.UInt32;
using DWORD = System.UInt32;
using NakedWin32Handle = System.IntPtr;
using MS.Dbg;



namespace MS.DbgShell
{
    internal
    class ConsoleTextWriter : TextWriter
    {
        internal
        ConsoleTextWriter(ColorHostUserInterface ui)
            :
            base(System.Globalization.CultureInfo.CurrentCulture)
        {
            Util.Assert(ui != null, "ui needs a value");

            _ui = ui;
        }



        public override
        Encoding
        Encoding
        {
            get
            {
                return null;
            }
        }



        public override
        void
        Write(string value)
        {
            _ui.WriteToConsole(value, true);
        }



        public override
        void
        WriteLine(string value)
        {
            _ui.WriteToConsole( value.AsSpan(), true, newLine: true );
        }



        public override
        void
        Write(Boolean b)
        {
            this.Write(b.ToString());
        }



        public override
        void
        Write(Char c)
        {
            _ui.WriteToConsole( c, true );
        }



        public override
        void
        Write(Char[] a)
        {
            _ui.WriteToConsole( a.AsSpan(), true );
        }



        private ColorHostUserInterface _ui;
    }
}   // namespace 


