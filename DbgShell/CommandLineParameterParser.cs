/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
// Originally from: host\msh\CommandLineParameterParser.cs


using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Host;
using System.Management.Automation.Internal;
using System.Diagnostics;
using MS.Dbg;

namespace MS.DbgShell
{
    internal class CommandLineParameterParser
    {
        internal const uint ExitCodeSuccess = 0;
        internal const uint ExitCodeBadCommandLineParameter = 0xfffd0000;

        internal CommandLineParameterParser(ColorConsoleHost p, /*Version ver,*/ string bannerText, string helpText)
        {
            Util.Assert(p != null, "parent ColorConsoleHost must be supplied");

            this.bannerText = bannerText;
            this.helpText = helpText;
            this.parent = p;
            this.ui = (ColorHostUserInterface)p.UI;
         // this.ver = ver;
        }

        internal bool AbortStartup
        {
            get
            {
                Util.Assert(dirty, "Parse has not been called yet");

                return abortStartup;
            }
        }

        internal string InitialCommand
        {
            get
            {
                Util.Assert(dirty, "Parse has not been called yet");

                return commandLineCommand;
            }
        }

        internal bool WasInitialCommandEncoded
        {
            get
            {
                Util.Assert(dirty, "Parse has not been called yet");

                return wasCommandEncoded;
            }
        }

        internal bool NoExit
        {
            get
            {
                Util.Assert(dirty, "Parse has not been called yet");

                return noExit;
            }
        }

     // internal bool ImportSystemModules
     // {
     //     get
     //     {
     //         return importSystemModules;
     //     }
     // }

        internal bool SkipProfiles
        {
            get
            {
                Util.Assert(dirty, "Parse has not been called yet");

                return skipUserInit;
            }
        }

     // internal bool ShowInitialPrompt
     // {
     //     get
     //     {
     //         return showInitialPrompt;
     //     }
     // }

        internal uint ExitCode
        {
            get
            {
                Util.Assert(dirty, "Parse has not been called yet");

                return exitCode;
            }
        }

        internal bool ReadFromStdin
        {
            get
            {
                Util.Assert(dirty, "Parse has not been called yet");

                return readFromStdin;
            }
        }

        internal bool NoPrompt
        {
            get
            {
                Util.Assert(dirty, "Parse has not been called yet");

                return noPrompt;
            }
        }

        internal Collection<CommandParameter> Args
        {
            get
            {
                Util.Assert(dirty, "Parse has not been called yet");

                return collectedArgs;
            }
        }

     // internal bool ServerMode
     // {
     //     get
     //     {
     //         return serverMode;
     //     }
     // }

     // internal Serialization.DataFormat OutputFormat
     // {
     //     get
     //     {
     //         Util.Assert(dirty, "Parse has not been called yet");

     //         return outFormat;
     //     }
     // }

     // internal Serialization.DataFormat InputFormat
     // {
     //     get
     //     {
     //         Util.Assert(dirty, "Parse has not been called yet");
     //         return inFormat;
     //     }
     // }

        internal string File
        {
            get
            {
                Util.Assert(dirty, "Parse has not been called yet");
                return file;
            }
        }

     // internal string ExecutionPolicy
     // {
     //     get
     //     {
     //         Util.Assert(dirty, "Parse has not been called yet");
     //         return executionPolicy;
     //     }
     // }

        internal bool ThrowOnReadAndPrompt
        {
            get
            {
                return noInteractive;
            }
        }

        private void ShowHelp()
        {
            ui.WriteLine("");
            //if (this.helpText == null)
            if( String.IsNullOrEmpty( helpText ) )
            {
                //ui.WriteLine(CommandLineParameterParserStrings.DefaultHelp);
                ui.WriteLine( "Help: TBD" );
            }
            else
            {
                ui.Write(helpText);
            }
            ui.WriteLine("");
        }

        private void ShowBanner()
        {
         // // We need to show the banner only if it has not been already shown in native layer
         // if (!showInitialPrompt)
         // {
                // If banner text is not supplied do nothing.
                if (!String.IsNullOrEmpty(bannerText))
                {
                    ui.WriteLine(bannerText);
                    ui.WriteLine();
                }
         // }
        }

        internal bool StaMode
        {
            get
            {
                if (staMode.HasValue)
                {
                    return staMode.Value;
                }
                else
                {
                    // Win8: 182409 PowerShell 3.0 should run in STA mode by default 
                    return true;
                }
            }
        }


        internal string DumpFile { get; private set; }


        /// <summary>
        /// 
        /// Processes all the command line parameters to ConsoleHost.  Returns the exit code to be used to terminate the process, or
        /// Success to indicate that the program should continue running.
        /// 
        /// </summary>
        /// <param name="args">
        /// 
        /// The command line parameters to be processed.
        /// 
        /// </param>

        internal void Parse(string[] args)
        {
            Util.Assert(!dirty, "This instance has already been used. Create a new instance.");

            // indicates that we've called this method on this instance, and that when it's done, the state variables 
            // will reflect the parse.

            dirty = true;

            ParseHelper(args);
        }

        private void ParseHelper(string[] args)
        {
            Util.Assert(args != null, "Argument 'args' to ParseHelper should never be null");
            bool noexitSeen = false;

            for (int i = 0; i < args.Length; ++i)
            {
                // Invariant culture used because command-line parameters are not localized.

                string switchKey = args[i].Trim().ToLowerInvariant();
                if (String.IsNullOrEmpty(switchKey))
                {
                    continue;
                }

                if (!SpecialCharacters.IsDash(switchKey[0]) && switchKey[0] != '/')
                {
                    // then its a command

                    --i;
                    ParseCommand(args, ref i, noexitSeen, false);
                    break;
                }

                // chop off the first character so that we're agnostic wrt specifying / or -
                // in front of the switch name.

                switchKey = switchKey.Substring(1);

                // chop off the second dash so we're agnostic wrt specifying - or --
                if (!String.IsNullOrEmpty(switchKey) && SpecialCharacters.IsDash(switchKey[0]))
                {
                    switchKey = switchKey.Substring(1);
                }

                if (MatchSwitch(switchKey, "help", "h") || MatchSwitch(switchKey, "?", "?"))
                {
                    showHelp = true;
                    abortStartup = true;
                }
                else if (MatchSwitch(switchKey, "noexit", "noe"))
                {
                    noExit = true;
                    noexitSeen = true;
                }
             // else if (MatchSwitch(switchKey, "importsystemmodules", "imp"))
             // {
             //     importSystemModules = true;
             // }
             // else if (MatchSwitch(switchKey, "showinitialprompt", "show"))
             // {
             //     showInitialPrompt = true;
             // }
                else if (MatchSwitch(switchKey, "noprofile", "nop"))
                {
                    skipUserInit = true;
                }
                else if (MatchSwitch(switchKey, "nologo", "nol"))
                {
                    showBanner = false;
                }
                else if (MatchSwitch(switchKey, "noninteractive", "noni"))
                {
                    noInteractive = true;

                    // We use RunspaceConfiguration instead of InitialSessionState. I
                    // don't know if there is a WarmUpTabCompletionOnIdle equivalent with
                    // that.
                 // if (ColorConsoleHost.DefaultInitialSessionState != null)
                 // {
                 //     // TODO: [danthom] This would be nice to have. We could hack it with reflection.
                 //     ColorConsoleHost.DefaultInitialSessionState.WarmUpTabCompletionOnIdle = false;
                 // }
                }
             // else if (MatchSwitch(switchKey, "servermode", "s"))
             // {
             //     serverMode = true;
             // }
                else if (MatchSwitch(switchKey, "command", "c"))
                {
                    if (!ParseCommand(args, ref i, noexitSeen, false))
                    {
                        break;
                    }
                }
                else if (MatchSwitch(switchKey, "windowstyle", "w"))
                {
                    ++i;
                    if (i >= args.Length)
                    {
                        ui.WriteErrorLine(
                            //CommandLineParameterParserStrings.MissingWindowStyleArgument);
                                "Missing WindowStyle argument." );
                        showHelp = false;
                        showBanner = false;
                        abortStartup = true;
                        exitCode = ExitCodeBadCommandLineParameter;
                        break;
                    }

                    try
                    {
                        ProcessWindowStyle style = (ProcessWindowStyle)LanguagePrimitives.ConvertTo(
                            args[i], typeof(ProcessWindowStyle), CultureInfo.InvariantCulture);
                        ConsoleControl.SetConsoleMode(style);
                    }
                    catch (PSInvalidCastException e)
                    {
                        ui.WriteErrorLine(string.Format(
                            CultureInfo.CurrentCulture,
                            //CommandLineParameterParserStrings.InvalidWindowStyleArgument,
                            "Bad WindowStyle argument.",
                            args[i], e.Message));
                        showHelp = false;
                        showBanner = false;
                        abortStartup = true;
                        exitCode = ExitCodeBadCommandLineParameter;
                        break;
                    }
                }
                else if (MatchSwitch(switchKey, "file", "f"))
                {
                    // Process file execution. We don't need to worry about checking -command
                    // since if -command comes before -file, -file will be treated as part
                    // of the script to evaluate. If -file comes before -command, it will
                    // treat -command as an argument to the script...

                    ++i;
                    if (i >= args.Length)
                    {
                        ui.WriteErrorLine(
                            //CommandLineParameterParserStrings.MissingFileArgument);
                                "Missing file argument." );
                        showHelp = true;
                        abortStartup = true;
                        exitCode = ExitCodeBadCommandLineParameter;
                        break;
                    }

                    // Don't show the startup banner unless -noexit has been specified.
                    if (!noexitSeen)
                        showBanner = false;

                    // Exit on script completion unless -noexit was specified...
                    if (!noexitSeen)
                        noExit = false;

                    // Process interactive input...
                    if (args[i] == "-")
                    {
                        // the arg to -file is -, which is secret code for "read the commands from stdin with prompts"

                        readFromStdin = true;
                        noPrompt = false;
                    }
                    else
                    {
                        // We need to get the full path to the script because it will be
                        // executed after the profiles are run and they may change the current
                        // directory.
                        string exceptionMessage = null;
                     // try
                     // {
                            Exception e = Util.TryIO( () => file = Path.GetFullPath( args[ i ] ) );
                     // }
                     // catch (Exception e)
                     // {
                     //     // Catch all exceptions - we're just going to exit anyway so there's
                     //     // no issue of the system begin destablized. We'll still
                     //     // Watson on "severe" exceptions to get the reports.
                     //     ColorConsoleHost.CheckForSevereException(e);
                        if( null != e )
                            exceptionMessage = e.Message;
                     // }

                        if (exceptionMessage != null)
                        {
                            ui.WriteErrorLine(string.Format(
                                CultureInfo.CurrentCulture,
                                //CommandLineParameterParserStrings.InvalidFileArgument,
                                "Invalid file argument.",
                                args[i], exceptionMessage));
                            showHelp = false;
                            abortStartup = true;
                            exitCode = ExitCodeBadCommandLineParameter;
                            break;
                        }

                        if (!Path.GetExtension(file).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                        {
                            ui.WriteErrorLine(string.Format(
                                CultureInfo.CurrentCulture,
                                //CommandLineParameterParserStrings.InvalidFileArgumentExtension,
                                "Invalid file argument extension.",
                                args[i]));
                            showHelp = false;
                            abortStartup = true;
                            exitCode = ExitCodeBadCommandLineParameter;
                            break;
                        }

                        if (!System.IO.File.Exists(file))
                        {
                            ui.WriteErrorLine(string.Format(
                                CultureInfo.CurrentCulture,
                                //CommandLineParameterParserStrings.ArgumentFileDoesNotExist,
                                "File argument does not exist.",
                                args[i]));
                            showHelp = false;
                            abortStartup = true;
                            exitCode = ExitCodeBadCommandLineParameter;
                            break;
                        }

                        i++;

                        Regex argPattern = new Regex(@"^.\w+\:", RegexOptions.CultureInvariant);
                        string pendingParameter = null;

                        // Accumulate the arguments to this script...
                        while (i < args.Length)
                        {
                            string arg = args[i];

                            // If there was a pending parameter, add a named parameter
                            // using the pending parameter and current argument
                            if (pendingParameter != null)
                            {
                                collectedArgs.Add(new CommandParameter(pendingParameter, arg));
                                pendingParameter = null;
                            }
                            else if (!string.IsNullOrEmpty(arg) && SpecialCharacters.IsDash(arg[0]))
                            {
                                Match m = argPattern.Match(arg);
                                if (m.Success)
                                {
                                    int offset = arg.IndexOf(':');
                                    if (offset == arg.Length - 1)
                                    {
                                        pendingParameter = arg.TrimEnd(':');
                                    }
                                    else
                                    {
                                        collectedArgs.Add(new CommandParameter(arg.Substring(0, offset), arg.Substring(offset + 1)));
                                    }
                                }
                                else
                                {
                                    collectedArgs.Add(new CommandParameter(arg));
                                }
                            }
                            else
                            {
                                collectedArgs.Add(new CommandParameter(null, arg));
                            }
                            ++i;
                        }
                    }
                    break;
                }
#if DEBUG
             // // this option is useful when debugging ConsoleHost remotely using VS remote debugging, as you can only 
             // // attach to an already running process with that debugger.
             // else if (MatchSwitch(switchKey, "wait", "w"))
             // {
             //     // This does not need to be localized: its chk only 

             //     ui.WriteToConsole("Waiting - type enter to continue:", false);
             //     ui.ReadLine();
             // }

             // // this option is useful for testing the initial InitialSessionState experience
             // else if (MatchSwitch(switchKey, "iss", "iss"))
             // {
             //     // Just toss this option, it was processed earlier...
             // }

             // // this option is useful for testing the initial InitialSessionState experience
             // // this is independent of the normal wait switch because configuration processing
             // // happens so early in the cycle...
             // else if (MatchSwitch(switchKey, "isswait", "isswait"))
             // {
             //     // Just toss this option, it was processed earlier...
             // }

             // else if (MatchSwitch(switchKey, "modules", "mod"))
             // {
             //     if (ColorConsoleHost.DefaultInitialSessionState == null)
             //     {
             //         ui.WriteErrorLine("The -module option can only be specified with the -iss option.");
             //         showHelp = true;
             //         abortStartup = true;
             //         exitCode = ExitCodeBadCommandLineParameter;
             //         break;
             //     }

             //     ++i;
             //     int moduleCount = 0;
             //     // Accumulate the arguments to this script...
             //     while (i < args.Length)
             //     {
             //         string arg = args[i];

             //         if (!string.IsNullOrEmpty(arg) && SpecialCharacters.IsDash(arg[0]))
             //         {
             //             break;
             //         }
             //         else
             //         {
             //             ColorConsoleHost.DefaultInitialSessionState.ImportPSModule(new string[] { arg });
             //             moduleCount++;
             //         }
             //         ++i;
             //     }
             //     if (moduleCount < 1)
             //     {
             //         ui.WriteErrorLine("No modules specified for -module option");
             //     }
             // }
#endif
             // else if (MatchSwitch(switchKey, "outputformat", "o") || MatchSwitch(switchKey, "of", "o"))
             // {
             //     ParseFormat(args, ref i, ref outFormat, CommandLineParameterParserStrings.MissingOutputFormatParameter);
             // }
             // else if (MatchSwitch(switchKey, "inputformat", "i") || MatchSwitch(switchKey, "if", "i"))
             // {
             //     ParseFormat(args, ref i, ref inFormat, CommandLineParameterParserStrings.MissingInputFormatParameter);
             // }
             // else if (MatchSwitch(switchKey, "executionpolicy", "ex") || MatchSwitch(switchKey, "ep", "ep"))
             // {
             //     ParseExecutionPolicy(args, ref i, ref executionPolicy, CommandLineParameterParserStrings.MissingExecutionPolicyParameter);
             // }
                else if (MatchSwitch(switchKey, "encodedcommand", "e") || MatchSwitch(switchKey, "ec", "e"))
                {
                    wasCommandEncoded = true;
                    if (!ParseCommand(args, ref i, noexitSeen, true))
                    {
                        break;
                    }
                }
             // else if (MatchSwitch(switchKey, "encodedarguments", "encodeda") || MatchSwitch(switchKey, "ea", "ea"))
             // {
             //     if (!CollectArgs(args, ref i))
             //     {
             //         break;
             //     }
             // }
             // else if (MatchSwitch(switchKey, "sta", "s"))
             // {
             //     if (staMode.HasValue)
             //     {
             //         // -sta and -mta are mutually exclusive.
             //         ui.WriteErrorLine(
             //                 CommandLineParameterParserStrings.MtaStaMutuallyExclusive);
             //         showHelp = false;
             //         showBanner = false;
             //         abortStartup = true;
             //         exitCode = ExitCodeBadCommandLineParameter;
             //         break;
             //     }

             //     staMode = true;
             // }
             // // Win8: 182409 PowerShell 3.0 should run in STA mode by default..so, consequently adding the switch -mta.
             // // Not deleting -sta for backward compatability reasons
             // else if (MatchSwitch(switchKey, "mta", "mta"))
             // {
             //     if (staMode.HasValue)
             //     {
             //         // -sta and -mta are mutually exclusive.
             //         ui.WriteErrorLine(
             //                 CommandLineParameterParserStrings.MtaStaMutuallyExclusive);
             //         showHelp = false;
             //         showBanner = false;
             //         abortStartup = true;
             //         exitCode = ExitCodeBadCommandLineParameter;
             //         break;
             //     }

             //     staMode = false;
             // }
                else if (MatchSwitch(switchKey, "z", "z"))
                {
                    ++i;
                    if (i >= args.Length)
                    {
                        ui.WriteErrorLine( "Missing dump file argument." );
                        showHelp = false;
                        showBanner = false;
                        abortStartup = true;
                        exitCode = ExitCodeBadCommandLineParameter;
                        break;
                    }

                    DumpFile = args[ i ];
                }
                else
                {
                    // The first parameter we fail to recognize marks the beginning of the command string.

                    --i;
                    if (!ParseCommand(args, ref i, noexitSeen, false))
                    {
                        break;
                    }
                }
            }

            if (showHelp)
            {
                ShowHelp();
            }
            
            if (showBanner && !showHelp)
            {
                ShowBanner();
            }

            Util.Assert(
                    ((exitCode == ExitCodeBadCommandLineParameter) && abortStartup)
                || (exitCode == ExitCodeSuccess),
                "if exit code is failure, then abortstartup should be true");
        }



        private bool MatchSwitch(string switchKey, string match, string smallestUnambiguousMatch)
        {
            Util.Assert(switchKey != null, "need a value");
            Util.Assert(!String.IsNullOrEmpty(match), "need a value");
            Util.Assert(match.Trim().ToLowerInvariant() == match, "match should be normalized to lowercase w/ no outside whitespace");
            Util.Assert(smallestUnambiguousMatch.Trim().ToLowerInvariant() == smallestUnambiguousMatch, "match should be normalized to lowercase w/ no outside whitespace");
            Util.Assert(match.Contains(smallestUnambiguousMatch), "sUM should be a substring of match");

            if (match.Trim().ToLowerInvariant().IndexOf(switchKey, StringComparison.Ordinal) == 0)
            {
                if (switchKey.Length >= smallestUnambiguousMatch.Length)
                {
                    return true;
                }
            }

            return false;
        }

     // private void ParseFormat(string[] args, ref int i, ref Serialization.DataFormat format, string resourceStr)
     // {
     //     StringBuilder sb = new StringBuilder();
     //     foreach (string s in Enum.GetNames(typeof(Serialization.DataFormat)))
     //     {
     //         sb.Append(s);
     //         sb.Append(ConsoleHostUserInterface.Crlf);
     //     }

     //     ++i;
     //     if (i >= args.Length)
     //     {
     //         ui.WriteErrorLine(
     //             StringUtil.Format(
     //                 resourceStr,
     //                 sb.ToString()));
     //         showHelp = true;
     //         abortStartup = true;
     //         exitCode = ExitCodeBadCommandLineParameter;
     //         return;
     //     }

     //     try
     //     {
     //         format = (Serialization.DataFormat)Enum.Parse(typeof(Serialization.DataFormat), args[i], true);
     //     }
     //     catch (ArgumentException)
     //     {
     //         ui.WriteErrorLine(
     //             StringUtil.Format(
     //                 CommandLineParameterParserStrings.BadFormatParameterValue,
     //                 args[i],
     //                 sb.ToString()));
     //         showHelp = true;
     //         abortStartup = true;
     //         exitCode = ExitCodeBadCommandLineParameter;
     //     }
     // }

        private void ParseExecutionPolicy(string[] args, ref int i, ref string executionPolicy, string resourceStr)
        {
            ++i;
            if (i >= args.Length)
            {
                ui.WriteErrorLine(resourceStr);

                showHelp = true;
                abortStartup = true;
                exitCode = ExitCodeBadCommandLineParameter;
                return;
            }

            executionPolicy = args[i];
        }

        private bool ParseCommand(string[] args, ref int i, bool noexitSeen, bool isEncoded)
        {
            if (commandLineCommand != null)
            {
                // we've already set the command, so squawk

                //ui.WriteErrorLine(CommandLineParameterParserStrings.CommandAlreadySpecified);
                ui.WriteErrorLine( "Hey wait; there's already a command!" );
                showHelp = true;
                abortStartup = true;
                exitCode = ExitCodeBadCommandLineParameter;
                return false;
            }

            ++i;
            if (i >= args.Length)
            {
                //ui.WriteErrorLine(CommandLineParameterParserStrings.MissingCommandParameter);
                ui.WriteErrorLine( "Missing command." );
                showHelp = true;
                abortStartup = true;
                exitCode = ExitCodeBadCommandLineParameter;
                return false;
            }

            if (isEncoded)
            {
                try
                {
                    //commandLineCommand = StringToBase64Converter.Base64ToString(args[i]);
                    //commandLineCommand = Encoding.Unicode.GetString( Convert.FromBase64String( args[ i ] ) );
                    commandLineCommand = DbgProvider.DecodeString( args[ i ] );
                }
                // decoding failed
                catch
                {
                    //ui.WriteErrorLine(CommandLineParameterParserStrings.BadCommandValue);
                    ui.WriteErrorLine( "Bad command value." );
                    showHelp = true;
                    abortStartup = true;
                    exitCode = ExitCodeBadCommandLineParameter;
                    return false;
                }

            }
            else if (args[i] == "-")
            {
                // the arg to -command is -, which is secret code for "read the commands from stdin with no prompts"

                readFromStdin = true;
                noPrompt = true;

                ++i;
                if (i != args.Length)
                {
                    // there are more parameters to -command than -, which is an error.

                    //ui.WriteErrorLine(CommandLineParameterParserStrings.TooManyParametersToCommand);
                    ui.WriteErrorLine( "Too many parameters for command." );
                    showHelp = true;
                    abortStartup = true;
                    exitCode = ExitCodeBadCommandLineParameter;
                    return false;
                }

                if (!parent.IsStandardInputRedirected)
                {
                    //ui.WriteErrorLine(CommandLineParameterParserStrings.StdinNotRedirected);
                    ui.WriteErrorLine( "StdIn not redirected." );
                    showHelp = true;
                    abortStartup = true;
                    exitCode = ExitCodeBadCommandLineParameter;
                    return false;
                }
            }
            else
            {
                // Collect the remaining parameters and combine them into a single command to be run.

                StringBuilder cmdLineCmdSB = new StringBuilder();

                while (i < args.Length)
                {
                    cmdLineCmdSB.Append(args[i] + " ");
                    ++i;                    
                }
                if (cmdLineCmdSB.Length > 0)
                {
                    // remove the last blank
                    cmdLineCmdSB.Remove(cmdLineCmdSB.Length - 1, 1);
                }
                commandLineCommand = cmdLineCmdSB.ToString();
            }

            if (!noexitSeen)
            {
                // don't reset this if they've already specified -noexit

                noExit = false;
            }

            showBanner = false;

            return true;
        }

     // private bool CollectArgs(string[] args, ref int i)
     // {

     //     if (collectedArgs.Count != 0)
     //     {
     //         //ui.WriteErrorLine(CommandLineParameterParserStrings.ArgsAlreadySpecified);
     //         ui.WriteErrorLine( "Args already specified." );
     //         showHelp = true;
     //         abortStartup = true;
     //         exitCode = ExitCodeBadCommandLineParameter;
     //         return false;
     //     }

     //     ++i;
     //     if (i >= args.Length)
     //     {
     //         //ui.WriteErrorLine(CommandLineParameterParserStrings.MissingArgsValue);
     //         ui.WriteErrorLine( "Missing args value." );
     //         showHelp = true;
     //         abortStartup = true;
     //         exitCode = ExitCodeBadCommandLineParameter;
     //         return false;
     //     }

     //     try
     //     {
     //         object[] a = StringToBase64Converter.Base64ToArgsConverter(args[i]);
     //         if (a != null)
     //         {
     //             foreach (object obj in a)
     //             {
     //                 collectedArgs.Add(new CommandParameter(null, obj));
     //             }
     //         }
     //     }
     //     catch
     //     {
     //         // decoding failed

     //         //ui.WriteErrorLine(CommandLineParameterParserStrings.BadArgsValue);
     //         ui.WriteErrorLine( "Bad args value." );
     //         showHelp = true;
     //         abortStartup = true;
     //         exitCode = ExitCodeBadCommandLineParameter;
     //         return false;
     //     }

     //     return true;
     // }

    //  private bool serverMode;
        private ColorConsoleHost parent;
        private ColorHostUserInterface ui;
        private bool showHelp;
        private bool showBanner = true;
    //  private bool showInitialPrompt;
        private bool noInteractive;
        private string bannerText;
        private string helpText;
        private bool abortStartup;
        private bool skipUserInit;
        // Win8: 182409 PowerShell 3.0 should run in STA mode by default
        // -sta and -mta are mutually exclusive..so tracking them using nullable boolean
        // if true, then sta is specified on the command line.
        // if false, then mta is specified on the command line.
        // if null, then none is specified on the command line..use default in this case
        // default is sta.
        private bool? staMode = null;
        private bool noExit = true;
        private bool readFromStdin;
        private bool noPrompt;
        private string commandLineCommand;
        private bool wasCommandEncoded;
        private uint exitCode = ExitCodeSuccess;
        private bool dirty;
     // private Version ver;
     // private Serialization.DataFormat outFormat = Serialization.DataFormat.Text;
     // private Serialization.DataFormat inFormat = Serialization.DataFormat.Text;
        private Collection<CommandParameter> collectedArgs = new Collection<CommandParameter>();
        private string file;
     // private string executionPolicy;
     // private bool importSystemModules = false;
    }

}   // namespace

