# DbgShell Changelog

Note that this changelog does not list every change (see the commit history for that).

## v0.8.26-beta
* Updated debugger engine binaries (dbgeng.dll et al).
* lmvm command for windbg muscle memory compat.
* Alternate formatting engine:
  * initial support for calculated properties ("lm | ft Name, SymbolStatus" will now use the alternate formatting engine).
  * Got rid of a lot of slow -CaptureContext stuff (thanks @Zhentar!).
  * other performance improvements (thanks @Zhentar!).
* New command: ForEach-DbgDumpFile.
* Can now display const statics.
* Added a per-target, per-module caching facility (Get-DbgTargetCacheItem).
* Better handling of native/wow64 ntdll and TEB (thanks @Zhentar!).
* ColorString interpolated string support (thanks @Zhentar!).
* Added a Remove-Color command.
* Work around a few dbgeng bugs.
* Many other enhancements and bugfixes.

## v0.8.24-beta
* Handled a few error cases when displaying stacks with managed frames.

## v0.8.22-beta
* First binary release of DbgShell.

