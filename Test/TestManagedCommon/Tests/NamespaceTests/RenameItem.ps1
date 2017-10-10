
$global:error.Clear()

$TestContext = Get-TestContext

$a = New-Item a
[string] $content1 = "hi"
Set-Content a $content1

Rename-Item a b
Verify-AreEqual 0 ($global:error.Count)
Verify-IsNull (dir a*)
Verify-AreEqual 1 ((,(dir b*)).Count)
Verify-AreEqual $content1 (Get-Content b)

[void] (mkdir c_dir)
Move-Item b c_dir
Rename-Item c_dir\b c
Verify-AreEqual 0 ($global:error.Count)
Verify-IsNull (dir b*)
Verify-AreEqual 1 ((,(dir c_dir)).Count)
Verify-AreEqual $content1 (Get-Content c_dir\c)

#
# Let's test some things that should fail.
#
Rename-Item c_dir\c "c*\!@" -ErrorAction SilentlyContinue  # so that we don't see scary red stuff in the output
Verify-AreEqual 1 ($global:error.Count)
$global:error.Clear()

$d = New-Item c_dir\d
Rename-Item c_dir\c d -ErrorAction SilentlyContinue  # so that we don't see scary red stuff in the output
Verify-AreEqual 1 ($global:error.Count)
$global:error.Clear()

Verify-AreEqual 0 ($global:error.Count)


