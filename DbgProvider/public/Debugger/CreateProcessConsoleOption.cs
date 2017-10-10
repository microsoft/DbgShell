namespace MS.Dbg
{
    /// <summary>
    ///    Options used when creating a new process under the debugger.
    /// </summary>
    public enum CreateProcessConsoleOption
    {
        /// <summary>
        ///    This is the default option.
        /// </summary>
        ReuseCurrentConsole,

        /// <summary>
        ///    Passes CREATE_NO_WINDOW:
        ///
        ///       The process is a console application that is being run without a console
        ///       window. Therefore, the console handle for the application is not set.
        ///
        ///       This flag is ignored if the application is not a console application, or
        ///       if it is used with either CREATE_NEW_CONSOLE or DETACHED_PROCESS.
        /// </summary>
        NoConsole,

        /// <summary>
        ///    Passes CREATE_NEW_CONSOLE:
        ///
        ///       The new process has a new console, instead of inheriting its parent's
        ///       console (the default). For more information, see Creation of a Console.
        ///
        ///       This flag cannot be used with DETACHED_PROCESS.
        /// </summary>
        NewConsole
    }
}

