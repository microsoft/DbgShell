# Hacking on DbgShell

Are you ready to hack on DbgShell? Start here.

## Contributor License Agreements

This project welcomes contributions and suggestions. Most contributions require you to
agree to a Contributor License Agreement (CLA) declaring that you have the right to, and
actually do, grant us the rights to use your contribution. For details, visit
https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to
provide a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the
instructions provided by the bot. You will only need to do this once across all repos
using our CLA.

## Code of Conduct

This project has adopted the [Microsoft Open Source Code of
Conduct](https://opensource.microsoft.com/codeofconduct/).

For more information see the [Code of Conduct
FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact
[opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or
comments.

## How to be a good open-source contributor

If you don't feel like you know the answer to that question, just enter it into the search
engine of your choice and you'll get plenty of good advice, like it's probably best to
file an Issue in order to open a dialogue with maintainers before embarking on making
changes, try to match the existing code style, etc.

## Getting source and building
__Requirements:__
* You need **Visual Studio 2017**, fully updated (VS updates; you don't need things like
  Azure SDK updates, phone emulator updates, etc.).
* No binaries are checked in to source control. To get external binaries, you'll need to
  be connected and allow NuGet package restore to download them. (Once you have downloaded
  the packages once, you'll have them locally—you won't always have to be connected in
  order to build.)

## A Brief Tour of the Source

* **DbgShell:** This is the main EXE. It implements the custom PowerShell host (which
  includes the REPL input loop). A good portion of this code is copied directly from
  `PowerShell.exe` source code.
* **DbgProvider:** This is the primary DLL—it implements the PowerShell namespace
  provider, all of the cmdlets, the custom
  [Formatting+Output](doc/CustomFormattingEngine.md) engine, etc. In addition to producing
  the DLL, this project contains the PowerShell module manifest (Debugger.psd1), script
  module (Debugger.psm1), symbol value converter scripts (Debugger.converters\*.ps1),
  formatting view definition scripts (\*.psfmt), and a few other odds and ends.
* **DbgEngWrapper:** This is a C++/CLI wrapper for dbgeng.dll. Many people use COM Interop
  to access the DbgEng API, but there are some problems with that approach. See
  [DbgEngWrapper](doc/DbgEngWrapper.md) for more info.
* **DbgNativeUtil:** Currently an empty placeholder in case we need a place for pure
  native code.
* **DbgShellExt:** A small native DLL which implements the `!dbgshell` extension command
  (so that DbgShell can be used from within windbg).
* **Microsoft.Diagnostics.Runtime:** This is the fantastic
  [ClrMd](https://github.com/Microsoft/clrmd) project from Lee Culver. This has the stuff
  for delving into managed code (you could think of it as the library with which you could
  implement `!sos`--it lets you walk the GC Heap, inspect managed thread stacks, etc.).
* **Test:**
  * **TestManagedCommon:** This implements the DbgShellTest PowerShell module, which
    includes cmdlets handy for testing DbgShell (like New-TestApp), as well as all the
    actual DbgShell tests (under the "Tests" directory).
  * **TestManagedConsoleApp:** A managed test EXE—something a test case can launch and/or
    attach to.
  * **TestNativeConsoleApp:** A native test EXE—something a test case can launch and/or
    attach to.

## The NuGet packages
The DbgShell projects consume the following NuGet packages:
* **Unofficial.Microsoft.DebuggerBinaries**: Binaries from the Windows Debuggers
  (dbgeng.dll, dbghelp.dll, etc.).
* **Unofficial.Microsoft.TextTemplating.BuildTasks**: Allows us to do [T4
  transformations](http://msdn.microsoft.com/en-us/library/bb126445.aspx) as part of the
  build.
* **AddGitVersionInfo**: Stamps version info (TBD: replace with something better, like
  NerdBank.GitVersioning).

## Standalone versus not
You may have noticed that one of the NuGet packages is for Windows Debugger binaries
(dbgeng.dll, et al). That's because DbgShell.exe can be its own process, and can be
deployed by itself (because it redists necessary debugger components, like dbgeng.dll).
Some things to note about this situation:
* The current debugger binaries that "ship" with DbgShell are the bare minimum—no
  extension DLLs. That could change. Or not; who knows. You can still use debugger
  extensions; you'll just have to specify the full path when `.load`ing them.
* You can also load and run DbgShell from within windbg/ntsd/cdb by loading the
  DbgShellExt.dll extension DLL, and then using "!dbgshell". Example windbg command:
  "`.load E:\src\DbgShell\bin\Debug\x64\DbgShellExt.dll`", then "`!dbgshell`". If you do
  it that way, then you'll have access to all the debugger extensions in DbgShell that you
  would in windbg.
* There isn't currently a good "deployment" story; it's just xcopy. It does not need to be
  copied to the windbg installation path (like `C:\Debuggers`).

## Running Tests
Okay—now you have everything you need to build and know what the basic components are. How
do you run tests?

The DbgShell project (the EXE) should be the default project for debugging, so you can hit
`F5` to launch it. Unfortunately, running DbgShell.exe under the Visual Studio debugger is
rather slow, especially startup (there are various reasons for this; one is that any time
an exception happens (including handled ones, which happen more than you might think), VS
is slow to deal with it). It is useful to sometimes just run DbgShell.exe _not_ under the
VS debugger, to see what the perf is _actually_ like.

Once DbgShell.exe launches, you'll end up at a "`Dbg:\`" prompt. You can run
"`Import-Module DbgShellTest`" to manually load testing functionality, but the shortcut is
to just run "`testall`". There are a few other handy shortcuts: `ntna` and `ntma`
(nmemonics: "new test native|managed app") which launch
`Test(Native|Managed)ConsoleApp.exe` and attach. Loading the `DbgShellTest` module creates
a new drive called "`Tests:\`", and gives you access to a new command: `Run-Test`. To run
all tests, you can run "`Run-Test Tests:\*.ps1`".

The tests are just PowerShell scripts (.ps1), and can be invoked on their own (like by
using "dot-sourcing": "`. Tests:\StlTypeConversion.ps1`"), which can be handy for
debugging them. The test framework is [Pester](https://github.com/pester/Pester).

## Version numbering convention
I can't remember where else I've seen this idea, but it seems nice so I'll borrow it:

All official releases should have an "even" build number, like 0.6.0.0, 0.6.2.0, 0.6.4.0.
Revision can float however it likes, so these are also good "official release" build
numbers: 0.6.4.17, 0.6.8.1.

Odd-numbered releases can then be immediately recognized as dev/private builds.

DbgShell has a `Get-DbgShellVersion` command that can be used to easily get version
information.

## Release procedure

This is an experimental project, with few contributors, so we just release out of master
(we do not plan to do things like patch previous releases, etc.). Here are the steps I
follow:

1. When ready to make a release set the version number in `VersionInfo.csproj` to an
   "official release" number (see details about the version numbering convention above).
1. Make sure that your git status is clean--you can have untracked files, but you don't
   want to have modified files, else the bits will get marked as "private" builds. (plus
   the build won't be reproducible by anybody else!) Make sure tests pass for all
   platforms, etc.
1. To make sure that the bin directory is clean, wipe it out completely (might have to
   exit VS). This will clean out, for instance, the "sym" (and sometimes "src")
   directories that dbgeng.dll likes to sometimes create.
1. Batch build --> rebuild everything.
1. Ideally you can then use the "release" folder as the package (but I've had to use
   "debug" to workaround some things before). Make a copy of either "release" or "debug"
   to "DbgShell". Then go into "DbgShell" and make sure there isn't any cruft in there,
   like `DbgShell.vshost.*`.
1. When it looks good, zip it up.
1. Upload to the distribution point of your choosing.
1. Before publicizing the release, go to a "non-developer machine" (no Visual Studio
   installed), download the release there, and test it to make sure it works there.
1. Once satisfied with everything, bump the version in `VersionInfo.csproj` to an odd
   number for ongoing development (see details about the version numbering convention
   below).


## How long should it take for Pull Requests to be merged?

Or for filed Issues to get attention? See disclaimer #2 at the top of the
[ReadMe.md](ReadMe.md): this project is not officially staffed, so "it might be a while".
Sorry.


## Microsoft-internal development

Nearly all development on DbgShell should be public. However, there are a few tiny scraps
of Microsoft-internal stuff: some symbol value conversion scripts that aren't useful
outside of Microsoft; the ability to pick up unreleased versions of debugger binaries
(dbgeng.dll et al) for testing; etc. This stuff isn't "secret" per se, but I want to keep
the public source clean of such extraneous cruft to avoid confusion. Thus we'll maintain
an `internal/master` branch with an extra commit or two, which we'll continually rebase
off the public `master`. In the public repo, there will be a dummy, empty stub branch
called `internal`, which will help prevent accidentally pushing any `internal/*` branches
to the public repo.
