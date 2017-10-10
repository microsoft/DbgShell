<?xml version="1.0"?>
<xsl:stylesheet
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:eventns="http://schemas.microsoft.com/win/2004/08/events"
    xmlns:msxsl="urn:schemas-microsoft-com:xslt"
    xmlns:user="http://example.com/whatever"
    version="1.0"
    >
    <xsl:output method="text"/>

    <!--

       This is the main template to generate the output file. This template
       contains apply-templates nodes that fill out various parts of the file.

    -->
    <xsl:template match="/">
        <xsl:text>//
//
//
//    IMPORTANT: This is generated code. Do not edit.
//
//
//
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.ComponentModel;
using System.Diagnostics;
using System.Resources;

namespace MS.Dbg
{

    internal class Resources
    {
        private ResourceManager m_resMan;

        private CultureInfo m_culture;

        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources()
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources( CultureInfo ci )
        {
            m_culture = ci;
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Advanced)]
        internal ResourceManager ResourceManager
        {
            get
            {
                if( null == m_resMan )
                {
                    m_resMan = new ResourceManager( "MS.Dbg.Resources", typeof( Resources ).Assembly );
                }
                return m_resMan;
            }
        }

        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Advanced)]
        internal CultureInfo Culture
        {
            get { return m_culture; }
            set { m_culture = value; }
        }

        private ConcurrentDictionary&lt; ResourceId, string &gt; m_resources = new ConcurrentDictionary&lt; ResourceId, string &gt;();
        public string this[ ResourceId index ]
        {
            get
            {
                string resource;
                if( !m_resources.TryGetValue( index, out resource ) )
                {
                    resource = ResourceManager.GetString( index.ToString(), m_culture );
                    m_resources.TryAdd( index, resource );
                }
                if( null == resource )
                {
                    System.Diagnostics.Debug.Fail( String.Format( Culture, "No resource for resource id {0}. You probably need to either rebuild or get matching binaries.", index ) );
                    throw new ArgumentException( String.Format( Culture, "No such resource id {0}.", index ), "index" );
                }
                return resource;
            }
        }

        public static Resources Loc = new Resources( CultureInfo.CurrentUICulture );
        public static Resources Neu = new Resources( CultureInfo.InvariantCulture );


        public static CultureMap CultureMap = new CultureMap();

        </xsl:text>
            <xsl:apply-templates select="root/data" mode="GenerateMcsProperties"/>
        <xsl:text>
    } // end class Resources


    class CultureMap
    {
        private static ConcurrentDictionary&lt; CultureInfo, Resources &gt; sm_cultureMap = new ConcurrentDictionary&lt; CultureInfo, Resources &gt;();

        static CultureMap()
        {
            sm_cultureMap.TryAdd( CultureInfo.CurrentUICulture, Resources.Loc );
            sm_cultureMap.TryAdd( CultureInfo.InvariantCulture, Resources.Neu );
        }

        public Resources this[ CultureInfo ci ]
        {
            get
            {
                Resources r;
                if( !sm_cultureMap.TryGetValue( ci, out r ) )
                {
                    r = new Resources( ci );
                    sm_cultureMap.TryAdd( ci, r );
                }
                return r;
            }
        }
    }


    internal enum ResourceId
    {
        </xsl:text>
            <xsl:apply-templates select="root/data" mode="GenerateEnumValues"/>
        <xsl:text>
    }
}
</xsl:text>
    </xsl:template> <!-- end main template -->

    <!--
       This template is for generating the resource ID enum values.
    -->
    <xsl:template match="data" mode="GenerateEnumValues">
        <xsl:value-of select="@name"/><xsl:text>,
        </xsl:text>
    </xsl:template>

    <!--
       This template is for generating the MulticulturalString properties.
    -->
    <xsl:template match="data" mode="GenerateMcsProperties">
        <xsl:text>
        public static MulticulturalString </xsl:text><xsl:value-of select="@name"/><xsl:text>
        {
            get
            {
                return new MulticulturalString( (ci) =&gt; CultureMap[ ci ][ ResourceId.</xsl:text><xsl:value-of select="@name"/><xsl:text> ] );
            }
        }
</xsl:text>
    </xsl:template>

</xsl:stylesheet>

