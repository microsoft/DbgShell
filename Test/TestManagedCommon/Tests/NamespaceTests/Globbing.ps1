
$global:error.Clear()

$TestContext = Get-TestContext

$oldPathBugginessStyle = Get-PathBugginessStyle
Push-Location Dbg:\
Set-PathBugginessStyle RegistryProvider

$fooDir = mkdir foo

$m = TabExpansion2 "dir f" -cursorColumn 5
Verify-AreEqual ".\foo" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir \f" -cursorColumn 6
Verify-AreEqual "Dbg:\foo" ($m.CompletionMatches[0].CompletionText)

cd foo
mkdir bar | Out-Null

$m = TabExpansion2 "dir b" -cursorColumn 5
Verify-AreEqual ".\bar" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir .\b" -cursorColumn 7
Verify-AreEqual ".\bar" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir \f" -cursorColumn 6
Verify-AreEqual "Dbg:\foo" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir Dbg:\f"  -cursorColumn 10
Verify-AreEqual "Dbg:\foo" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir Debugger::\f"  -cursorColumn 16
Verify-AreEqual "Debugger::\foo" ($m.CompletionMatches[0].CompletionText)

#
# Now switching to a provider-qualified current working directory.
#

cd Debugger::
Verify-AreEqual "Debugger\Debugger::" ($pwd.Path)
Verify-AreEqual 2 ((dir).Count)
cd Debugger::\
Verify-AreEqual "Debugger\Debugger::" ($pwd.Path)
Verify-AreEqual 2 ((dir).Count)

$m = TabExpansion2 "dir f" -cursorColumn 5
Verify-AreEqual ".\foo" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir .\f" -cursorColumn 7
Verify-AreEqual ".\foo" ($m.CompletionMatches[0].CompletionText)


# NOTE: When PowerShell bug "Windows 8 Bugs 929380" is fixed, then this test will fail.
$m = TabExpansion2 "dir \f" -cursorColumn 6
Verify-AreEqual "foo" ($m.CompletionMatches[0].CompletionText)

cd Debugger::foo
Verify-AreEqual "Debugger\Debugger::foo" ($pwd.Path)
cd Debugger::\foo
Verify-AreEqual "Debugger\Debugger::foo" ($pwd.Path)

$m = TabExpansion2 "dir b" -cursorColumn 5
Verify-AreEqual ".\bar" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir .\b" -cursorColumn 7
Verify-AreEqual ".\bar" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir Dbg:\f"  -cursorColumn 10
Verify-AreEqual "Dbg:\foo" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir Debugger::\f"  -cursorColumn 16
Verify-AreEqual "Debugger::\foo" ($m.CompletionMatches[0].CompletionText)


#
# Switching to Certificate provider-style path bugginess.
#

Set-PathBugginessStyle CertificateProvider
cd Dbg:\

# NOTE: When PowerShell bug "Windows 8 Bugs 929390" is fixed, then this test will fail.
$m = TabExpansion2 "dir f" -cursorColumn 5
Verify-AreEqual ".\\foo" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir \f" -cursorColumn 6
Verify-AreEqual "Dbg:\foo" ($m.CompletionMatches[0].CompletionText)

cd foo

$m = TabExpansion2 "dir b" -cursorColumn 5
Verify-AreEqual ".\bar" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir .\b" -cursorColumn 7
Verify-AreEqual ".\bar" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir \f" -cursorColumn 6
Verify-AreEqual "Dbg:\foo" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir Dbg:\f"  -cursorColumn 10
Verify-AreEqual "Dbg:\foo" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir Debugger::\f"  -cursorColumn 16
Verify-AreEqual "Debugger::\foo" ($m.CompletionMatches[0].CompletionText)

#
# Now switching to a provider-qualified current working directory.
#

cd Debugger::
Verify-AreEqual "Debugger\Debugger::" ($pwd.Path)
Verify-AreEqual 2 ((dir).Count)
cd Debugger::\
Verify-AreEqual "Debugger\Debugger::" ($pwd.Path)
Verify-AreEqual 2 ((dir).Count)

# NOTE: When PowerShell bug "Windows 8 Bugs 929390" is fixed, then this test will fail.
$m = TabExpansion2 "dir f" -cursorColumn 5
Verify-AreEqual ".\\foo" ($m.CompletionMatches[0].CompletionText)

# NOTE: When PowerShell bug "Windows 8 Bugs 929390" is fixed, then this test will fail.
$m = TabExpansion2 "dir .\f" -cursorColumn 7
Verify-AreEqual ".\\foo" ($m.CompletionMatches[0].CompletionText)


# Unlike the RegistryStyle bugginess, this behavior works right.
$m = TabExpansion2 "dir \f" -cursorColumn 6
Verify-AreEqual "\foo" ($m.CompletionMatches[0].CompletionText)

cd Debugger::foo
Verify-AreEqual "Debugger\Debugger::\foo" ($pwd.Path)
cd Debugger::\foo
Verify-AreEqual "Debugger\Debugger::\foo" ($pwd.Path)

$m = TabExpansion2 "dir b" -cursorColumn 5
Verify-AreEqual ".\bar" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir .\b" -cursorColumn 7
Verify-AreEqual ".\bar" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir Dbg:\f"  -cursorColumn 10
Verify-AreEqual "Dbg:\foo" ($m.CompletionMatches[0].CompletionText)

$m = TabExpansion2 "dir Debugger::\f"  -cursorColumn 16
Verify-AreEqual "Debugger::\foo" ($m.CompletionMatches[0].CompletionText)


Set-PathBugginessStyle $oldPathBugginessStyle
Pop-Location

Verify-AreEqual 0 ($global:error.Count)


