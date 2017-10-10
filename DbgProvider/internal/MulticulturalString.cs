//-----------------------------------------------------------------------
// <copyright file="MulticulturalString.cs" company="Microsoft">
//    Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Runtime.Serialization;

namespace MS.Dbg
{
    /************************************************************************************
     *
     *             A brief review of CurrentCulture vs. CurrentUICulture
     *
     * CurrentUICulture: is for looking up localized strings: what language should they be
     *                   in?
     *
     * CurrentCulture: is for formatting dates, quantities, currency, etc. It's
     *                 independent of the language that is being used.
     *
     *
     ***********************************************************************************/

    /// <summary>
    ///    Represents an object that can be expressed differently for different cultures.
    /// </summary>
    internal interface IMulticultural< T >
    {
        T ToCulture( CultureInfo culture );
    }


    /// <summary>
    ///    Similar to the System.String class, but can be expressed in multiple languages.
    /// </summary>
    /// <remarks>
    /// <para>
    ///    The MulticulturalString constructor accepts a Func&lt; CultureInfo, String &gt;
    ///    delegate which generates the string in the desired language (if available).
    ///    Because the culture-specific rendering is deferred, MulticulturalString objects
    ///    are easily composable while maintaining their ability to be rendered in
    ///    multiple languages.
    /// </para>
    /// <para>
    ///    The Func&lt; CultureInfo, String &gt; is not serializable. If you wish to have
    ///    a MulticulturalString object that is serializable, you must create a class that
    ///    derives from MulticulturalString and overrides the the
    ///    ReconstituteToCultureFunction method. The default implementation of the
    ///    ReconstituteToCultureFunction will throw a NotImplementedException.
    /// </para>
    /// <para>
    ///    Because the rendering of the string is deferred to a Func, the Func could be
    ///    written such that it returns a different result each time it is called. Care
    ///    should be taken /not/ to do this unless all potential consumers of the
    ///    MulticulturalString are written with this behavior in mind. Most
    ///    MulticulturalString string lookup delegates should simply pull the appropriate
    ///    string out of a resource (or compose a MulticulturalString from one or more
    ///    MulticulturalStrings and other objects, such as via the
    ///    MulticulturalString.Format method).
    /// </para>
    /// <para>
    ///    Like the resource lookup and fallback pattern, string generation delegates
    ///    should fail gracefully rather than throw if the string is not available for the
    ///    requested CultureInfo, by returning a fallback version of the string.
    /// </para>
    /// <para>
    ///    Because MulticulturalString objects are implicitly convertible to normal
    ///    System.String objects, it is very easy to start using MulticulturalString in
    ///    place of System.String. The implicit string conversion, like the overridden
    ///    System.Object.ToString() method, returns the string rendered in the current
    ///    culture.
    /// </para>
    /// </remarks>
    [Serializable]
    [DebuggerDisplay( "{m_neutralString}" )]
    public class MulticulturalString : IMulticultural< string >, IFormattable, ISerializable
    {
        [NonSerialized]
        private Func< CultureInfo, string > m_toString;
        private string m_neutralString; // eagerly cached for easy viewing in a debugger.

        /// <summary>
        ///    Creates a new MulticulturalString object.
        /// </summary>
        public MulticulturalString( Func<CultureInfo, string> toString )
        {
            if( null == toString )
                throw new ArgumentNullException( "toString" );

            m_toString = toString;
            m_neutralString = m_toString( CultureInfo.InvariantCulture );
        }

        /// <summary>
        ///    Retrieves a culture-specific rendering of the string.
        /// </summary>
        public string ToCulture( CultureInfo culture )
        {
            return m_toString( culture );
        }

        /// <summary>
        ///    Retrieves a culture-specific rendering of the string.
        /// </summary>
        public string ToString( CultureInfo culture )
        {
            return m_toString( culture );
        }

        /// <summary>
        ///    If the formatProvider is a CultureInfo object, retrieves a culture-specific
        ///    rendering of the string, else retrieves a rendering of the string for the
        ///    current culture.
        /// </summary>
        public virtual string ToString( string format, IFormatProvider formatProvider )
        {
            CultureInfo ci = formatProvider as CultureInfo;
            if( null != ci )
                return m_toString( ci );
            else
                return m_toString( CultureInfo.CurrentUICulture );
        }

        /// <summary>
        ///    Retrieves a rendering of the string specific to the current culture.
        /// </summary>
        public override string ToString()
        {
            return m_toString( CultureInfo.CurrentUICulture );
        }

        /// <summary>
        ///    Retrieves a rendering of the string specific to the current culture.
        /// </summary>
        public static implicit operator String( MulticulturalString mcs )
        {
            return mcs.ToString();
        }

        /// <summary>
        ///    For use by serialization.
        /// </summary>
        public virtual void GetObjectData( SerializationInfo info, StreamingContext context )
        {
            m_toString = ReconstituteToCultureFunction( info, context );
        }

        /// <summary>
        ///    When overridden in a derived class, provides the means to re-create the
        ///    Func&lt; CultureInfo, String &gt; delegate that renders the string for
        ///    specific cultures.
        /// </summary>
        /// <remarks>
        ///    The default implementation in this class throws a NotImplementedException.
        /// </remarks>
        protected virtual Func<CultureInfo, string> ReconstituteToCultureFunction( SerializationInfo info, StreamingContext context )
        {
            throw new NotImplementedException( Util.Sprintf( "This class ({0}) is not serializable.", Util.GetGenericTypeName( this, true ) ) );
        }


        /// <summary>
        ///    Similar to System.String.Format, composes a new MulticulturalString object
        ///    based on a format string and inserts.
        /// </summary>
        /// <remarks>
        ///    If any of the inserts are MulticulturalString objects or IFormattable, then
        ///    those portions of the resulting MulticulturalString will also be rendered
        ///    in a culture-specific way.
        /// </remarks>
        public static MulticulturalString Format( MulticulturalString mcsFmt, params object[] inserts )
        {
            return new MulticulturalString( (ci) =>
                {
                    string fmt = mcsFmt.ToCulture( ci );
                    object[] cultureSpecificInserts = InsertsToCulture( inserts, ci );
                    return String.Format( CultureInfo.CurrentCulture, fmt, cultureSpecificInserts );
                } );
        }


        /// <summary>
        ///    Similar to System.String.Format, composes a new MulticulturalString object
        ///    based on a format string and inserts.
        /// </summary>
        /// <remarks>
        /// <para>
        ///    If any of the inserts are MulticulturalString objects or IFormattable, then
        ///    those portions of the resulting MulticulturalString will also be rendered
        ///    in a culture-specific way.
        /// </para>
        /// <para>
        ///    The IFormatProvider could be CultureInfo.CurrentCulture, or some other
        ///    settings that control how dates, numbers, currency, etc. are formatted,
        ///    regardless of the language the strings are in.
        /// </para>
        /// </remarks>
        public static MulticulturalString Format( IFormatProvider fmtProvider,
                                                  MulticulturalString mcsFmt,
                                                  params object[] inserts )
        {
            return new MulticulturalString( (ci) =>
                {
                    string fmt = mcsFmt.ToCulture( ci );
                    object[] cultureSpecificInserts = InsertsToCulture( inserts, ci );
                    return String.Format( fmtProvider, fmt, cultureSpecificInserts );
                } );
        }


        /// <summary>
        ///    Similar to System.String.Format, composes a new MulticulturalString object
        ///    based on a format string and inserts.
        /// </summary>
        /// <remarks>
        ///    Since the format string itself is a normal string, it cannot be rendered
        ///    differently. But if any of the inserts are MulticulturalString objects or
        ///    IFormattable, then those portions of the resulting MulticulturalString will
        ///    be rendered in a culture-specific way. 
        /// </remarks>
        public static MulticulturalString Format( string fmt, params object[] inserts )
        {
            return new MulticulturalString( (ci) =>
                {
                    object[] cultureSpecificInserts = InsertsToCulture( inserts, ci );
                    return String.Format( CultureInfo.CurrentCulture, fmt, cultureSpecificInserts );
                } );
        }


        /// <summary>
        ///    Similar to System.String.Format, composes a new MulticulturalString object
        ///    based on a format string and inserts.
        /// </summary>
        /// <remarks>
        /// <para>
        ///    Since the format string itself is a normal string, it cannot be rendered
        ///    differently. But if any of the inserts are MulticulturalString objects or
        ///    IFormattable, then those portions of the resulting MulticulturalString will
        ///    be rendered in a culture-specific way. 
        /// </para>
        /// <para>
        ///    The IFormatProvider could be CultureInfo.CurrentCulture, or some other
        ///    settings that control how dates, numbers, currency, etc. are formatted,
        ///    regardless of the language the strings are in.
        /// </para>
        /// </remarks>
        public static MulticulturalString Format( IFormatProvider fmtProvider, string fmt, params object[] inserts )
        {
            return new MulticulturalString( (ci) =>
                {
                    object[] cultureSpecificInserts = InsertsToCulture( inserts, ci );
                    return String.Format( fmtProvider, fmt, cultureSpecificInserts );
                } );
        }


        public static object[] InsertsToCulture( object[] inserts, CultureInfo ci )
        {
            object[] cultureSpecificInserts = (object[]) inserts.Clone();
            for( int i = 0; i < cultureSpecificInserts.Length; i++ )
            {
                IMulticultural< string > mc = cultureSpecificInserts[ i ] as IMulticultural< string >;
                if( null != mc )
                {
                    cultureSpecificInserts[ i ] = mc.ToCulture( ci );
                }
            }
            return cultureSpecificInserts;
        } // end InsertsToCulture()

    } // end class MulticulturalString


    /// <summary>
    ///    This is a convenience class for when you have a string that does not have
    ///    culture-specific translations, but needs to be passed to a function overload
    ///    that takes a MulticulturalString object.
    /// </summary>
    /// <remarks>
    ///    Creating a new NonLocString object is basically shorthand for "new
    ///    MulticulturalString( (ci) &eq;&gt; constantString )".
    /// </remarks>
    class NonLocString : MulticulturalString
    {
        public NonLocString( string constantText )
            : base( ( ci ) => constantText )
        {
        }
    } // end class NonLocString
}

