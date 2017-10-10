/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Globalization;
using MS.Dbg;

namespace MS.DbgShell
{

    /// <summary>
    /// Helper methods used by PowerShell's Hosts: ConsoleHost and InternalHost to process
    /// PromptForChoice.
    /// </summary>
    internal static class HostUIHelperMethods
    {
        /// <summary>
        /// Constructs a string of the choices and their hotkeys.
        /// </summary>
        /// <param name="choices"></param>
        /// <param name="hotkeysAndPlainLabels"></param>
        /// <exception cref="ArgumentException">
        /// 1. Cannot process the hot key because a question mark ("?") cannot be used as a hot key.
        /// </exception>
        internal static void BuildHotkeysAndPlainLabels(Collection<ChoiceDescription> choices,
            out string[,] hotkeysAndPlainLabels)
        {
            // we will allocate the result array
            hotkeysAndPlainLabels = new string[2, choices.Count];

            for (int i = 0; i < choices.Count; ++i)
            {
                #region SplitLabel
                hotkeysAndPlainLabels[0, i] = string.Empty;
                int andPos = choices[i].Label.IndexOf('&');
                if (andPos >= 0)
                {
                    StringBuilder splitLabel = new StringBuilder(choices[i].Label.Substring(0, andPos), choices[i].Label.Length);
                    if (andPos + 1 < choices[i].Label.Length)
                    {
                        splitLabel.Append(choices[i].Label.Substring(andPos + 1));
                        hotkeysAndPlainLabels[0, i] = CultureInfo.CurrentCulture.TextInfo.ToUpper(choices[i].Label.Substring(andPos + 1, 1).Trim());
                    }
                    hotkeysAndPlainLabels[1, i] = splitLabel.ToString().Trim();
                }
                else
                {
                    hotkeysAndPlainLabels[1, i] = choices[i].Label;
                }
                #endregion SplitLabel

                // ? is not localizable
                if (string.Compare(hotkeysAndPlainLabels[0, i], "?", StringComparison.Ordinal) == 0)
                {
                    Exception e = new ArgumentException( "Cannot process the hot key because a question mark (\"?\") cannot be used as a hot key.", string.Format(CultureInfo.InvariantCulture, "choices[{0}].Label", i) );
                    throw e;
                }
            }
        }

        /// <summary>
        /// Searches for a corresponding match between the response string and the choices.  A match is either the response
        /// string is the full text of the label (sans hotkey marker), or is a hotkey.  Full labels are checked first, and take 
        /// precedence over hotkey matches.
        /// </summary>
        /// <param name="response"></param>
        /// <param name="choices"></param>
        /// <param name="hotkeysAndPlainLabels"></param>
        /// <returns>
        /// Returns the index into the choices array matching the response string, or -1 if there is no match.
        ///</returns>
        internal static int DetermineChoicePicked(string response, Collection<ChoiceDescription> choices, string[,] hotkeysAndPlainLabels)
        {
            Util.Assert(choices != null, "choices: expected a value");
            Util.Assert(hotkeysAndPlainLabels != null, "hotkeysAndPlainLabels: expected a value");

            int result = -1;

            // check the full label first, as this is the least ambiguous
            for (int i = 0; i < choices.Count; ++i)
            {
                // pick the one that matches either the hot key or the full label
                if (string.Compare(response, hotkeysAndPlainLabels[1, i], StringComparison.CurrentCultureIgnoreCase) == 0)
                {
                    result = i;
                    break;
                }
            }

            // now check the hotkeys
            if (result == -1)
            {
                for (int i = 0; i < choices.Count; ++i)
                {
                    // Ignore labels with empty hotkeys
                    if (hotkeysAndPlainLabels[0, i].Length > 0)
                    {
                        if (string.Compare(response, hotkeysAndPlainLabels[0, i], StringComparison.CurrentCultureIgnoreCase) == 0)
                        {
                            result = i;
                            break;
                        }
                    }
                }
            }

            return result;
        }
    }

} // namespace


