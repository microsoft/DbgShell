using System;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    public abstract class DbgEventFilter
    {
        /// <summary>
        ///    The "plain english" name of the filter. If this is an "arbitrary
        ///    exception filter", it could be String.Empty.
        /// </summary>
        public string FriendlyName { get; private set; }

        /// <summary>
        ///    The "code" name of the filter (like "av" for the access violation
        ///    filter, etc.).
        /// </summary>
        public string Name { get; private set; }

        public DEBUG_FILTER_EXEC_OPTION ExecutionOption { get; private set; }
        public DEBUG_FILTER_CONTINUE_OPTION ContinueOption { get; private set; }

        public string Command { get; private set; }

        internal DbgEventFilter( string friendlyName,
                                 DEBUG_FILTER_EXEC_OPTION executionOption,
                                 DEBUG_FILTER_CONTINUE_OPTION continueOption,
                                 string command )
            : this( friendlyName, executionOption, continueOption, command, null )
        {
        }

        internal DbgEventFilter( string friendlyName,
                                 DEBUG_FILTER_EXEC_OPTION executionOption,
                                 DEBUG_FILTER_CONTINUE_OPTION continueOption,
                                 string command,
                                 string name ) // like "ld", "ud", "cpr", "ct", etc.
        {
            FriendlyName = friendlyName ?? String.Empty;
            ExecutionOption = executionOption;
            ContinueOption = continueOption;
            Command = command;
            Name = name;
        } // end constructor
    } // end class DbgEventFilter
}