# Custom Formatting+Output Engine

PowerShell is not a text-based shell; it's an object-based shell. Commands write full
objects to the "pipeline"; not strings.

So when you are running PowerShell.exe, what determines what text you see in the console
window?

The name for the feature area of PowerShell that takes care of that is called "Formatting
and Output", or "F+O" for short.

The basic idea is that whenever you interactively run some pipeline of commands, the
PowerShell host tacks on an extra "`Out-Default`" command onto the end of the pipeline you
typed. (So if you type "`Get-Foo | Select-Object -First 3`", the pipeline that is actually
executed is "`Get-Foo | Select-Object -First 3 | Out-Default`".) The `Out-Default` command
is what figures out how to display whatever objects popped out at the end of your
pipeline.

## The Default Out-Default

(I may get a few details wrong here, but this is the general idea...) If a string object
comes to `Out-Default`, it will just send that string right along. (The PowerShell host
will get it and write it to the console.) But if something else comes along, Out-Default
will try and "format" it.

In standard PowerShell, you can define one or more custom "view definitions" for a
particular type by typing some XML into a
[.ps1xml](http://msdn.microsoft.com/en-us/library/windows/desktop/dd878339(v=vs.85).aspx)
file. You can create List views, Table views, Wide views, or Custom views. For instance,
for a table view, you would define what the columns should be&mdash;labels,
widths&mdash;and where the data should come from&mdash;property names or custom script.

If `Out-Default` finds a view definition for a particular object type, it will use that to
"format" the object for display. So if you define a table view for an object, then
`Out-Default` will call `Format-Table` with the specified view definition for your object.
(And that will eventually yield strings, which the PowerShell host will write to the
console.)

If `Out-Default` does not find any pre-defined view definitions for an object, it will try
to generate one on the fly based on the properties of the object. If the object has 1-4
properties, it will generate a table view; else if there are more it will generate a list
view. Sometimes the default generated view is fine; sometimes it's lousy.

If for some reason the object has _no_ properties (so a generated view based on properties
would yield nothing), then `Output-Default` will just call `.ToString()` on the object.
(Or is it the PowerShell host that calls `.ToString()`? I forget.)

**Q:** Seems pretty flexible. So why does DbgShell need a custom F+O engine?
<br/>
**A:** There are a few reasons:
* Support for [Color].
* Better handling of generics.
* The default F+O has other serious limitations.

## Support for Color

DbgShell supports color by using ISO/IEC 6429 control sequences (see the [Color](Color.md)
page for more info). The built-in formatting engine does not recognize these color
sequences, so they add length to strings, but they should really be considered
zero-length, since they are stripped out when displayed. Not treating colorized strings as
zero-width throws formatting way off.

In addition to treating control sequences as zero-width, sometimes formatting needs to
truncate strings (such as to fit a value into a table column). Things like truncating in
the middle of a control sequence or chopping off the end of a string with a POP control
sequence wreak havoc on the color state.

So DbgShell needs an F+O engine that is control-sequence aware.

## Better Handling of Generics

When registering a view definition, you must say what type(s) it applies to, and this does
not play well with generics:
* If you look at the typename for `KeyValuePair<string,int>`, it is something horrible,
  like ``System.Collections.Generic.KeyValuePair`2[[System.String, mscorlib,
  Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[System.Int32,
  mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]``. Besides
  simply looking hideous, there are PublicKeyTokens and version numbers in there that are
  onerous to depend on.
* If you want to apply some formatting in a generic way (for instance, if I want to apply
  certain formatting for all `KeyValuePair<TKey,TValue>`, no matter what `TKey` and
  `TValue` are), there is no way to do it.

One way to hack the formatting system is by sticking something else in the TypeNames list,
and keying off that in your view definition. But with some very important cases, such as
enumeration of dictionary key/value pairs, there is no way to inject yourself into the
chain of events such that you can get at the TypeNames list.

DbgShell's solution is to simply let you easily define views for generic types just as
easily as for other types (using type names more like you would see in source code, like
`System.Collections.Generic.KeyValuePair<System.String,System.Int32>`). Additionally, you
can register views for partially specified generic types by using special wildcards, like
`System.Collections.Generic.KeyValuePair<?,?>`, which would apply to all KVP's no matter
the type of key or value.

### The Default F+O Has Other Serious Limitations

Creating format view definitions for the built-in PowerShell formatting engine is painful
and somewhat esoteric. We can make it easier. (Aside: It's strange that someone chose XML
as the definition language for custom formatting views when they had a perfectly cromulant
scripting language right in their lap.)

The built-in formatting engine is relatively "set"&mdash;with the exception of
Format-Custom, there isn't much opportunity to do serious customization of the formatting
and display experience. (Even "custom" formatting views are not as flexible as you might
think.) By implementing our own engine, we can add useful features such as footers for
tables, an auto-generated index column, etc.

Another deficiency of the default F+O is that if you have multiple objects written to the
output stream, it will pick a formatting view definition based on the first object it
sees, and then try to use that view for all subsequent objects. If the objects written to
the output stream are not all the same type... terribleness.

## DbgShell's Custom F+O

**Q:** Does DbgShell's custom formatting engine replace the built-in one?
<br/>
**A:** No, it exists "side-by-side".

**Q:** How do you invoke the alternate formatting engine?
<br/>
**A:** You can invoke the alternate formatting cmdlets directly (`Format-AltTable`,
`Format-AltList`, `Format-AltCustom`, `Format-AltSingleLine`), but you can also just use
the regular formatting cmdlets (`Format-Table`, etc.) because we have defined "proxy"
functions for them. The proxy functions will check to see if you have an alternate format
view definition for the type of object you are trying to format, and if so, it forwards
the call on to the alternate formatting cmdlet; if not, it sends it on to the built-in
formatting cmdlet.

**Q:** How do those proxy functions know if a particular object has a view definition for the alternate F+O engine?
<br/>
**A:** When you register a view definition with the alternate formatting engine, you give
the name of the type it should be applicable for. Then when it has an object it needs to
format, the proxy function uses the "TypeNames" of the object. N.B.: this does not mean it
calls `.GetType()` on the object and uses the name of the type; it uses the
`PSObject.TypeNames` list. This list will include base types, so you can define a single
view definition for a base type, and it will be applied to derived objects too.

**Q:** How do I create format view definitions for the alternate formatting engine?
<br/>
**A:** Instead of a .ps1xml file, you write a .psfmt file, which contains PowerShell
script. In the script, you call various cmdlets to define your format views, such as
New-AltScriptColumn, New-AltTableViewDefinition, etc., culminating in a call to
Register-AltTypeFormatEntries. Take a look at the included Debugger.DebuggeeTypes.psfmt
file for an example.

**Q:** What types of views does the custom F+O support?
<br/>
**A:** It supports some similar to the default F+O: List, Table, and Custom, plus another
that does not have an analog in the default F+O: Single-Line.

### Debuggee/symbol values
Recall (from one of the **Q/A**s above) that the alternate formatting engine uses an
object's "TypeNames" to look up an alternate formatting view definition. (You can see an
object's TypeNames list easily by typing something like "`$foo.PSObject.TypeNames`".) The
TypeNames list will also include base types. For instance, for a `System.String`:
```powershell
PS C:\Users\danthom> [string] $s = 'hello'
PS C:\Users\danthom> $s.PSObject.TypeNames
System.String
System.Object
```

When DbgShell creates objects that represent symbol values, it puts some additional pseudo
type names into the TypeNames list. The pseudo type names represent the type of the symbol
value. These pseudo type names are distinguished by having a '!' in them (a la the
debugger syntax for a module!name). For instance:
```
PS Dbg:\WhatHappenedToThatCsRec.cab\Threads\22> $myGlobal = dt dfsrs!g_FrsStatusList
PS Dbg:\WhatHappenedToThatCsRec.cab\Threads\22> $myGlobal.PSObject.TypeNames
dfsrs!FrsStatusList*
!FrsStatusList*
MS.Dbg.DbgPointerValue
MS.Dbg.DbgPointerValueBase
MS.Dbg.DbgValue
System.Object

PS Dbg:\WhatHappenedToThatCsRec.cab\Threads\22>
```

The pseudo type names are not real .NET type names, but the alternate formatting engine
does not care about that&mdash;they are just strings to be used in a lookup. This allows
you to create view definitions (for the alternate formatting engine) to customize how
certain symbol value objects are displayed.

### Single-line views
Recall (from one of the **Q/A**s above) that the alternate formatting engine supports a
"single-line" view type that has no equivalent in the default formatting engine. A
single-line view definition is just what it sounds like&mdash;given an object, it should
produce a single string (for display on a single line).

When it comes to view definition selection, single-line view definitions are somewhat
special. When an object pops out the end of your pipeline, DbgShell's alternate formatting
engine will check to see if there are any alternate formatting engine view definitions
registered for it. If it finds a table view first, it will use that; if it finds a list
view first, it will use that; if it finds a custom view first, it will use that. But if it
finds a "single-line" view, it will _not_ use it (and it will continue to look for the
next view definition). Single-line view definitions are _only_ used if you explicitly call
`Format-AltSingleLine`. (If no alternate formatting engine view definitions are found, the
object goes on to the PowerShell built-in formatting engine.)

So when are single-line views useful?

You might not ever call `Format-AltSingleLine` yourself, but some of DbgShell's other view
definitions will. In particular, DbgShell defines a custom view definition for UDT values
(User-Defined Type; an "object" in C++ or C#), and that custom view uses
`Format-AltSingleLine` to give a summary of each of the fields in the object. With
appropriate single-line view definitions registered, this gives you a very powerful view
of an object "at a glance", without needing to "drill" into fields to check what the
values are.

### Example
Here is an example that registers a single-line view for `CRITICAL_SECTION` objects.

```powershell
Register-AltTypeFormatEntries {

    New-AltTypeFormatEntry -TypeName '!_RTL_CRITICAL_SECTION' {

        New-AltSingleLineViewDefinition {
            # The DbgUdtValue formatting code pre-screens for unavailable
            # values; we can assume the value is available here.
            if( $_.LockCount -lt 0 )
            {
                $cs = Format-DbgTypeName '_RTL_CRITICAL_SECTION: '
                $cs = $cs.AppendPushPopFg( [ConsoleColor]::Green, 'Not locked.' )
                return $cs
            }
            else
            {
                $cs = Format-DbgTypeName '_RTL_CRITICAL_SECTION: '
                $cs = $cs.AppendPushPopFg( [ConsoleColor]::Yellow, "LOCKED, by thread: $($_.OwningThread)" )
                return $cs
            }
        } # end AltSingleLineViewDefinition
    } # end Type !_RTL_CRITICAL_SECTION
}
```

The command on the first line says that we are going to register some view definitions for
the alternate formatting engine. The parameter to that command is a script block that
produces "format entries". A format entry is basically just a tuple mapping a type name to
a set of view definitions.

The next line says we are going to create a format entry for objects that have
"`!_RTL_CRITICAL_SECTION`" in their TypeNames list. The last parameter is another script
block, which produces view definitions. (There is only one view definition here, but you
can have more than one&mdash;for instance, you could also define a list view and a table
view.)

Finally we come to the `New-AltSingleLineViewDefinition` command. The object that is being
formatted is passed in as the "dollar underbar" (`$_`) variable. The first thing we do is
check to see if the critical section is locked or not, so we can give different output for
each case.

The `Format-DbgTypeName` command returns a `ColorString` object formatted consistently
with how DbgShell displays type names (dark yellow on black). To that we append more text;
either a green "Not locked", or a yellow "LOCKED" message, along with the owning thread.

This view definition produces output like below:

![Single-line format display for a critsec](screenshot_critsec_formatting.png)

For more examples, just take a look at some actual view definitions that come with
DbgShell: run `dir Bin:\Debugger\*.psfmt` from within DbgShell to see a list of formatting
files.

