
var oArgs = WScript.Arguments;

if( oArgs.length == 0 )
{
	WScript.StdErr.WriteLine( "What do you want me to transform?" );
	printUsage();
	WScript.Quit( -1 );
}

if( ("/?" == oArgs( 0 )) || ("-?" == oArgs( 0 )) )
{
	printUsage();
	WScript.Quit( -1 );
}

if( oArgs.length != 2 )
{
	WScript.StdErr.WriteLine( "Bad parameters; expected two but got " + oArgs.length + "." );
	printUsage();
	WScript.Quit( -1 );
}


xmlFile = oArgs( 0 );
xslFile = oArgs( 1 );

// Breaking right here? Don't have MSXML6? Get it from:
// http://www.microsoft.com/downloads/details.aspx?FamilyID=993C0BCF-3BCF-4009-BE21-27E85E1857B1&displaylang=en
// and
// http://www.microsoft.com/downloads/details.aspx?FamilyID=d21c292c-368b-4ce1-9dab-3e9827b70604&displaylang=en
var xsl = new ActiveXObject( "MSXML2.DOMDOCUMENT.6.0" );
var xml = new ActiveXObject( "MSXML2.DOMDocument.6.0" );
xml.validateOnParse = false;
xml.async = false;
xml.load( xmlFile );

if( xml.parseError.errorCode != 0 )
{
	WScript.StdErr.WriteLine( "XML Parse Error : " + xml.parseError.reason );
    WScript.StdErr.WriteLine( "error code " + xml.parseError.errorCode + " line " + xml.parseError.line + ":" + xml.parseError.linepos );
	WScript.Quit( xml.parseError.errorCode );
}

xsl.async = false;
xsl.load( xslFile );
xsl.setProperty( "AllowXsltScript", true );

if( xsl.parseError.errorCode != 0 )
{
	WScript.StdErr.WriteLine( "XSL Parse Error : " + xsl.parseError.reason );
    WScript.StdErr.WriteLine( "error code " + xsl.parseError.errorCode + " line " + xsl.parseError.line + ":" + xsl.parseError.linepos );
	WScript.Quit( xsl.parseError.errorCode );
}

try
{
	WScript.Echo( xml.transformNode( xsl.documentElement ) );
}
catch( err )
{
	WScript.StdErr.WriteLine( "Transformation Error : " + numberToHexString( err.number ) + ": " + err.description );
	WScript.Quit( err.number );
}


// Wow; printing a number in hex is a little more complicated than I would expect.
function numberToHexString( num )
{
    return "0x" + ((num >> 16) & 0x0000FFFF).toString(16) + (num & 0x0000FFFF).toString(16);
} // end of numberToHexString()


function printUsage()
{
	WScript.Echo( "" );
	WScript.Echo( "Usage: cscript xslTransform.js xmlFile xslFile" );
	WScript.Echo( "" );
	WScript.Echo( "       The transformed contents of xmlFile are sent to stdout." );
	WScript.Echo( "       The AllowXsltScript property is set to true, which has security implications. Do not run this script on untrusted input." );
	WScript.Echo( "" );
} // end printUsage()

