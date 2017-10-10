# Getting Started with DbgShell

_DbgShell is a PowerShell front-end for the Windows debuggers. For more background see the
root [ReadMe](../ReadMe.md)._

_For help within DbgShell, you can run_ `Get-Help about_DbgShell`.

DbgShell is a PowerShell shell. You can think of it as a tricked-out PowerShell.exe. So
most things that you can do with PowerShell.exe, you can do with DbgShell.exe (some things
are not tested/supported, such as remoting capabilities).

In addition to normal PowerShell stuff that you could do in any PowerShell.exe window, you
also have available a bunch of commands from a module called "Debugger". A quick way to
see the commands available to you from a particular module is to use `Get-Command`:

```powershell
Get-Command -Module Debugger
```

However, this might be a little daunting (it will return > 100 things). And just knowing
what commands are available does not explain everything about DbgShell. So we'll cover a
few basic things here to get you started.

DbgShell can be run in both "standalone mode" (by running DbgShell.exe), or from a
debugger (such as windbg) (by `.load`ing DbgShellExt.dll and running `!dbgshell`). We'll
start with standalone stuff&mdash;go ahead and launch `DbgShell.exe`.

# Standalone Mode

First, the prompt: when you start DbgShell.exe, you will see some banner text, and then
you will be at a prompt that looks like this:

```
PS Dbg:\
>
```

Don't be alarmed.

In other shells, you are accustomed to having prompt text that indicates the current
directory. PowerShell is the same, but in addition to the filesystem, there are other
"namespace providers" that allow you to `cd` into the registry, into Active Directory,
etc. And in DbgShell, there is a namespace provider for the debugger, and "Dbg:\" is the
root of the namespace (In PS parlance, "Dbg:" is a PSDrive).

If you run `dir`, there won't be much to see, because we aren't attached to anything yet.
To do that, there are several commands:

```powershell
Connect-Process   # attaches to an existing process.
Start-DbgProcess  # starts a process under the debugger.
Mount-DbgDumpFile # loads a dump file.
```

# Attaching

So let's get attached to something. It will be more interesting to attach to something for
which we have full private symbols. DbgShell comes with a couple of console EXEs for
testing purposes, so we can use one of those. They can be easily accessed via the `Bin:\`
PS drive. Run:

```powershell
Start-DbgProcess Bin:\DbgShellTest\TestNativeConsoleApp.exe
```

You'll see a whole bunch of output, which looks very much like what you would have seen in
windbg/ntsd. However, that stuff is not just plain text&mdash;it's all objects. For
instance, if you ran "`$stuff = Start-DbgProcess
Bin:\DbgShellTest\TestNativeConsoleApp.exe`" instead, you wouldn't see any output (all the
objects would be collected into the `$stuff` variable). And then you could do something
like "`$stuff[0] | Format-List * -Force`" and see more details, which might look like:

```
    ImageFileHandle : 2900
    ModuleInfo      : ntdll  00007ffe`686f0000 00007ffe`6889c000
    FilterEvent     : LOAD_MODULE
    Debugger        : MS.Dbg.DbgEngDebugger
    Message         : ModLoad: 00007ffe`686f0000 00007ffe`6889c000   ntdll
    Timestamp       : 1/2/2015 9:01:32 PM -08:00
    ShouldBreak     : False
```

## Lesson #1

So lesson #1 is that DbgShell nearly always deals in full objects&mdash;*NO PLAIN TEXTUAL
OUTPUT.*

Now that we're attached to something, notice that the prompt has changed:

```
PS Dbg:\TestNativeConsoleApp (0)\Threads\0
>
```

Use `cd` (an alias for `Set-Location`), `dir` (an alias for `Get-ChildItem`), and `type`
(an alias for `Get-Content`) to explore around. Go ahead, I'll wait. (Hint: you can `cd`
into anything labeled as a `[container]`, as well as using "`..`" to go up a level.)

(There are other commands/aliases as well, such as `pushd`/`popd`, `ls`, `gc`, etc.)

Currently the information exposed in the namespace is minimal, but it gives you a flavor
for what is possible.

Okay, let's take a look at something together. Get back to where you started, and take a
look at the "Teb" item

```powershell
cd "Dbg:\TestNativeConsoleApp (0)\Threads\0"
type .\Teb
```

Assuming you are able to download symbols for `ntdll` from the Microsoft symbol servers
(or have them cached), you should see the contents of the TEB. It's not plain text, of
course; it's an object (see Lesson #1). You could assign that object to a variable, but it
turns out that for the TEB, there is a built-in variable, `$teb`. (Type just "`$teb`" and
hit enter, and you should see the same thing.)

Another way to get at the TEB is to get it off of a thread object. To get a thread object,
you could use the "`Get-DbgUModeThreadInfo -Current`" command, or if you are accustomed to
windbg commands, you could just use "`~.`". So this should also show you the TEB:

```powershell
(~.).Teb
```

## Lesson #2

So lesson #2 is that there are usually several different ways to get at
something&mdash;you might be able to get it through the namespace, through a special
variable (just like "pseudo-registers" in windbg), or through commands, using either
PS-style descriptive cmdlet names, or traditional windbg command names (where
implemented&mdash;windbg has many more commands than are implemented in DbgShell).

Now let's take a look at stacks.

So that we have more than just `ntdll`'s process bootstrapping on the stack, set a
breakpoint on the `_InitGlobals` function and `g`o there. Note that there is tab
completion, so you can type "`bp TestN`", hit `[tab]`, and it will complete.

```powershell
bp TestNativeConsoleApp!_InitGlobals
g
```

In windbg, there are various different commands used for displaying stacks in different
ways (`k`, `kc`, `kp`, `kP`, `kn`, etc.). In DbgShell, there is just one core
command&mdash;`Get-DbgStack`&mdash;which retrieves a `DbgStackInfo` object, which can be
formatted in various ways. There are currently only a few aliases/wrappers defined (`kc`
and `k`), with more to be added in the future.

So in addition to just displaying a stack, you can do scripty things with it. The alias
for the core `Get-DbgStack` command is "`kc`", so we can do things like this:

```powershell
$myStack = kc
$myStack.Frames[ 1 ].ReturnAddress
$myStack.Frames[ 1 ].Function.Symbol.Type.Arguments[ 0 ].ArgType
```

This is awesome.

It gets even more awesome with symbols and symbol values. Navigate to stack frame 1. If
you use `cd`, tab completion is your friend. Or you can just use a windbg-style command:
"`.frame 1`". To take a look at locals, you can use the `Get-DbgLocalSymbol` command, or
its windbg-like alias, `dv`.

To start digging into locals, you could assign the output of "`dv`" to a variable
("`$locals = dv`", followed by "`$locals[8].<tab>`"), or you can use the handy `dt`
function, which is a wrapper around `Get-DbgSymbolValue` (or `Get-DbgTypeInfo`, depending
on how you use it).

Some of the locals in this frame have not been initialized, but we can also explore
globals. Let's "go up" a frame, to let the `_InitGlobals()` function finish its work
first, and then look at all the globals with names that start with "`g_`":

```powershell
gu
x TestNativeConsoleApp!g_*
```

Now you can try out tab completion by typing `dt TestN[tab]m_pd[tab][tab]` to
auto-complete to `dt TestNativeConsoleApp!g_pDtdNestingThing4`. And then you can type a
"`.`", and start pressing tab, as many times as you like.  Whenever you find something you
want to dig into further, just type a "`.`", and keep tabbing your way to glory. When you
find something you want to see in more detail, just hit enter to run the `dt` command, and
you'll see its juicy contents.

## Lesson #3

So lesson #3 is that tab completion is fantastic.

And remember lesson #1&mdash;that stuff you see is not text. It's an object, which you can
script with. Numbers can be added or subtracted or multiplied (or used with logical
operators), you can do stringy stuff with strings, pointers can be indexed off of (and
they support pointer arithmetic), etc.

If you aren't familiar with PowerShell, the help topic displayed by `get-help
about_Operators` gives lots of information about general powershell operators, plus
references to other topics (like `get-help about_Logical_Operators, etc.).

And it's not just symbol values that are rich objects&mdash;you can get to the type
information as well. You can either start with "`dv`" (`Get-DbgLocalSymbol`) or "`x`"
(`Get-DbgSymbol`) and get the `Type` property off the resulting `DbgLocalSymbol` object,
or if you've got a symbol **value** (for instance, if you used "`dt`" and used tab
completion and "`.`" to get several levels deep), every symbol value has a
`DbgGetSymbol()` method that will give you the symbol information for the value in
question, from which you can get the Type property.

For example,

```powershell
$val = dt TestNativeConsoleApp!g_d2
$sym = $val.DbgGetSymbol()
$sym.Type
$sym.Type | Format-List
```

Notice the last two commands&mdash;the first ("`$sym.Type`") gives output that would be
familiar to users of windbg. But taking that same object and sending it through a
different formatter (`Format-List`, or "`fl`" for short) gives us a different view, which
shows more of the information that is available.

## Lesson #4

Symbol values and types are rich objects that are highly scriptable. `Format-List` is
handy to see available properties.

I'm currently out of time to work on this "getting started" guide, so let me just suggest
a few additional topics for your personal exploration:

* Global symbols and values (`x`/`Get-DbgSymbol`, `dt`/`Get-DbgSymbolValue`)
* Execution (`p`, `pc`, `g`, `gu`)
* Memory (`dd`, `dp`, `dps`)
* Disassembly (`uf .`)
* Module information (`lm`/`Get-DbgModuleInfo`)
* Register values (`r`/`Get-DbgRegisterInfo`)

Oh, and one last thing:

## ESCAPE HATCH

DbgShell currently only implements a very small set of windbg-style commands. When you
want to do something but DbgShell doesn't support it (or you don't know how), you can use
the `Invoke-DbgEng` command ("`ide`" for short)&mdash;this "escape hatch" command lets you
type any crazy, esoteric windbg command that you like and send it to dbgeng for execution.
A big downside to the escape hatch is that *Lesson #2* does not apply&mdash;you only get
text for output.


