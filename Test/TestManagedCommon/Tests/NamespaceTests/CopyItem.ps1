
$global:error.Clear()

$TestContext = Get-TestContext

$aDir = mkdir a
[void] (New-Item a\i1)
[void] (New-Item a\i2)
[void] (New-Item a\i3)

$stuffInA = dir a

Verify-AreEqual 3 $stuffInA.Count
Verify-IsNull (dir b*)

Copy-Item a b

Verify-AreEqual 0 ($global:error.Count)
# We didn't say -recurse, so we shouldn't have copied any of the child items.
Verify-IsNull (dir b)

Copy-Item a\* b

Verify-AreEqual 0 ($global:error.Count)
Verify-AreEqual 3 (dir b).Count

rmdir b -Recurse

Verify-AreEqual 0 ($global:error.Count)
Verify-IsNull (dir b*)

Copy-Item a b -Recurse

Verify-AreEqual 0 ($global:error.Count)
Verify-AreEqual 3 (dir b).Count

Copy-Item a\i1 b\i4

Verify-AreEqual 0 ($global:error.Count)
Verify-AreEqual 4 (dir b).Count

#
# Let's test some things that should fail.
#
Copy-Item a a -ErrorAction SilentlyContinue  # so that we don't see scary red stuff in the output

Verify-AreEqual 1 ($global:error.Count)
$global:error.Clear()

Copy-Item a a\a -ErrorAction SilentlyContinue  # so that we don't see scary red stuff in the output

Verify-AreEqual 1 ($global:error.Count)
$global:error.Clear()

Copy-Item a\i1 a\i1 -ErrorAction SilentlyContinue  # so that we don't see scary red stuff in the output

Verify-AreEqual 1 ($global:error.Count)
$global:error.Clear()

Copy-Item a\i1 b\i1 -ErrorAction SilentlyContinue  # so that we don't see scary red stuff in the output

Verify-AreEqual 1 ($global:error.Count)
$global:error.Clear()

# TODO: there's a lot more to test



Verify-AreEqual 0 ($global:error.Count)

