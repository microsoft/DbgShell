/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
// Originally from: engine\lang\parserutils.cs



#pragma warning disable 1634, 1691 // Stops compiler from warning about unknown warnings

namespace MS.DbgShell
{
    /// <summary>
    /// Define the various unicode special characters that
    /// the parser has to deal with.
    /// </summary>
    internal static class SpecialCharacters
    {
        public const char enDash = (char)0x2013;
        public const char emDash = (char)0x2014;
        public const char horizontalBar = (char)0x2015;

        public const char quoteSingleLeft = (char)0x2018;   // left single quotation mark  
        public const char quoteSingleRight = (char)0x2019;  // right single quotation mark 
        public const char quoteSingleBase = (char)0x201a;   // single low-9 quotation mark
        public const char quoteReversed = (char)0x201b;     // single high-reversed-9 quotation mark    
        public const char quoteDoubleLeft = (char)0x201c;   // left double quotation mark
        public const char quoteDoubleRight = (char)0x201d;  // right double quotation mark
        public const char quoteLowDoubleLeft = (char)0x201E;// low double left quote used in german.

        public static bool IsDash(char c)
        {
            return (c == enDash || c == emDash || c == horizontalBar || c == '-');
        }
        public static bool IsSingleQuote(char c)
        {
            return (c == quoteSingleLeft || c == quoteSingleRight || c == quoteSingleBase ||
                c == quoteReversed || c == '\'');
        }
        public static bool IsDoubleQuote(char c)
        {
            return (c == '"' || c == quoteDoubleLeft || c == quoteDoubleRight || c == quoteLowDoubleLeft);
        }
        public static bool IsQuote(char c)
        {
            return (IsSingleQuote(c) || IsDoubleQuote(c));
        }
        public static bool IsDelimiter(char c, char delimiter)
        {
            if (delimiter == '"') return IsDoubleQuote(c);
            if (delimiter == '\'') return IsSingleQuote(c);
            return (c == delimiter);
        }
        public static bool IsCurlyBracket(char c)
        {
            return (c == '{' || c == '}');
        }
        /// <summary>
        /// Canonicalize the quote character - map all of the aliases for " or '
        /// into their ascii equivalent.
        /// </summary>
        /// <param name="c">The character to map</param>
        /// <returns>The mapped character.</returns>
        public static char AsQuote(char c)
        {
            if (IsSingleQuote(c)) return '\'';
            if (IsDoubleQuote(c)) return '"';
            return (c);
        }
    };
}

