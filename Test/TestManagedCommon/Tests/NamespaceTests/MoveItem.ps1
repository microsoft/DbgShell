
$global:error.Clear()

$TestContext = Get-TestContext

$a = New-Item a
[string] $content1 = "hi"
Set-Content a $content1

Move-Item a b
Verify-AreEqual 0 ($global:error.Count)
Verify-IsNull (dir a*)
Verify-AreEqual 1 ((,(dir b*)).Count)
Verify-AreEqual $content1 (Get-Content b)

[void] (mkdir c_dir)
Move-Item b c_dir
Verify-AreEqual 0 ($global:error.Count)
Verify-IsNull (dir b*)
Verify-AreEqual 1 ((,(dir c_dir)).Count)
Verify-AreEqual $content1 (Get-Content c_dir\b)

Move-Item c_dir d_dir
Verify-AreEqual 0 ($global:error.Count)
Verify-IsNull (dir c*)
Verify-AreEqual 1 ((,(dir d_dir)).Count)
Verify-AreEqual $content1 (Get-Content d_dir\b)


#
# Let's test some things that should fail.
#
[void] (mkdir d_dir\e_dir)
Move-Item d_dir d_dir\e_dir -ErrorAction SilentlyContinue  # so that we don't see scary red stuff in the output
Verify-AreEqual 1 ($global:error.Count)
$global:error.Clear()


Verify-AreEqual 0 ($global:error.Count)

