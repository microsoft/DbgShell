
$global:error.Clear()

$TestContext = Get-TestContext

$a = New-Item a
[string] $content1 = "hi"

Set-Content a $content1
Verify-AreEqual 0 ($global:error.Count)
Verify-AreEqual $content1 (Get-Content a)

Clear-Content a
Verify-AreEqual 0 ($global:error.Count)
Verify-IsNull (Get-Content a)

[guid] $content2 = [guid]::NewGuid()

Set-Content a $content2
Verify-AreEqual 0 ($global:error.Count)
Verify-AreEqual $content2 (Get-Content a)

$content = @( $content1, $content2 )
Set-Content a $content1
Add-Content a $content2
Verify-AreEqual 0 ($global:error.Count)

function VerifyListsEquivalent( $l1, $l2 )
{
    Verify-AreEqual ($l1.Count) ($l2.Count)
    for( [int] $i = 0; $i -lt $l1.Count; $i++ )
    {
        Verify-AreEqual ($l1[ $i ]) ($l2[ $i ])
    }
}

$retrievedContent = Get-Content a
VerifyListsEquivalent $content (Get-Content a)
Verify-AreEqual 0 ($global:error.Count)
VerifyListsEquivalent $content (Get-Content a -ReadCount 2)
Verify-AreEqual 0 ($global:error.Count)
VerifyListsEquivalent $content (Get-Content a -ReadCount 3)
Verify-AreEqual 0 ($global:error.Count)
VerifyListsEquivalent $content (Get-Content a -ReadCount 1)
Verify-AreEqual 0 ($global:error.Count)
Verify-AreEqual $content1 (Get-Content a -TotalCount 1)
Verify-AreEqual 0 ($global:error.Count)
# Apparently '-Tail' is only supported for the FileSystem provider. Hmph.
#Verify-AreEqual $content2 (Get-Content a -Tail 1)
#Verify-AreEqual 0 ($global:error.Count)


Clear-Content a
Verify-AreEqual 0 ($global:error.Count)
Verify-IsNull (Get-Content a)

