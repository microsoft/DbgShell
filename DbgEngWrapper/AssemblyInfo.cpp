#include "stdafx.h"

using namespace System;
using namespace System::Reflection;
using namespace System::Runtime::CompilerServices;
using namespace System::Runtime::InteropServices;
using namespace System::Security::Permissions;

//
// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
//
[assembly:AssemblyTitleAttribute("DbgEngWrapper")];
[assembly:AssemblyDescriptionAttribute("")];
[assembly:AssemblyConfigurationAttribute("")];
[assembly:AssemblyCompanyAttribute("")];
[assembly:AssemblyProductAttribute("DbgEngWrapper")];
[assembly:AssemblyCopyrightAttribute("Copyright (c)  2017")];
[assembly:AssemblyTrademarkAttribute("")];
[assembly:AssemblyCultureAttribute("")];

//
// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the value or you can default the Revision and Build Numbers
// by using the '*' as shown below:

[assembly:AssemblyVersionAttribute("1.0.*")];

[assembly:ComVisible(false)];

[assembly:CLSCompliantAttribute(true)];

//
// The [DefaultDllImportSearchPaths] attribute is needed for the hosted case (when the CLR
// is being hosted by DbgShellExt, loaded into windbg). The problem was that the CLR was
// deciding to load a second copy of dbgeng.dll, the one right next to DbgEngWrapper.dll,
// even though there was already one loaded (windbg's).  That attribute perturbs the CLR's
// LoadLibrary behavior, and this setting seems to work (it makes it not try to use a
// fully-qualified path to dbghelp.dll).
//
// In the standalone case, DbgShell.exe arranges to make sure that dbgeng.dll is already
// loaded from the correct location.
//
[assembly:DefaultDllImportSearchPaths(DllImportSearchPath::LegacyBehavior)];

