#
# Module manifest for module 'Debugger'
#

@{

# Script module or binary module file associated with this manifest
RootModule = 'DbgProvider.dll'

# Version number of this module.
ModuleVersion = '0.6'

# ID used to uniquely identify this module
GUID = '9fc4e260-6dbc-4546-8846-09549c766513'

# Author of this module
Author = 'Microsoft Corporation'

# Company or vendor of this module
CompanyName = 'Microsoft Corporation'

# Copyright statement for this module
Copyright = '(c) Microsoft Corporation. All rights reserved.'

# Minimum version of the Windows PowerShell engine required by this module
PowerShellVersion = '4.0'

# Name of the Windows PowerShell host required by this module
PowerShellHostName = ''

# Minimum version of the Windows PowerShell host required by this module
PowerShellHostVersion = ''

# Minimum version of the .NET Framework required by this module
DotNetFrameworkVersion = '4.5'

# Minimum version of the common language runtime (CLR) required by this module
CLRVersion = '4.0'

# Processor architecture (None, X86, Amd64, IA64) required by this module
ProcessorArchitecture = ''

# Modules that must be imported into the global environment prior to importing this module
RequiredModules = @()

# Assemblies that must be loaded prior to importing this module
RequiredAssemblies = @( 'DbgEngWrapper.dll', 'Microsoft.Diagnostics.Runtime.dll' )

# Script files (.ps1) that are run in the caller's environment prior to importing this module
ScriptsToProcess = @()

# Type files (.ps1xml) to be loaded when importing this module
TypesToProcess = @()

# Format files (.ps1xml) to be loaded when importing this module
#FormatsToProcess = @( 'Debugger.Format.ps1xml' )
# TODO: I tried having the two format files co-exist: pointing to the 'vanilla' one from here,
# and having the custom shell call Update-FormatData with the color one. No worky... seems
# the 'vanilla' formatting data always got used. So I'll probably need to generate a whole
# separate module for 'vanilla' hosts.
FormatsToProcess = @( 'Debugger.Format.Color.ps1xml' )

# Modules to import as nested modules of the module specified in ModuleToProcess
NestedModules = @( 'Debugger.Formatting.psm1', 'Debugger.psm1' )

# Functions to export from this module
FunctionsToExport = '*'

# Cmdlets to export from this module
CmdletsToExport = '*'

# Variables to export from this module
VariablesToExport = '*'

# Aliases to export from this module
AliasesToExport = '*'

# List of all modules packaged with this module
ModuleList = @()

# List of all files packaged with this module
FileList = @()

# Private data to pass to the module specified in ModuleToProcess
PrivateData = ''

# HelpInfo URI of this module
#HelpInfoURI = 'http://go.microsoft.com/fwlink/?LinkId=216168'

}

