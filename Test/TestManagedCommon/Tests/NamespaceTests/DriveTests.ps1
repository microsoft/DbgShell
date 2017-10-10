
$global:error.Clear()

$TestContext = Get-TestContext

[int] $initialDriveCount = (Get-PSDrive -PSProvider Debugger).Count

$fooDir = mkdir foo
cd foo
$fooDrive = New-PSDrive zot Debugger .\

Verify-AreEqual 0 ($global:error.Count)
if( (Get-PathBugginessStyle) -eq 'CertificateProvider' )
{
    Verify-AreEqual "\foo" $fooDrive.Root
}
else
{
    Verify-AreEqual "foo" $fooDrive.Root
}
Verify-AreEqual ($initialDriveCount + 1) ((Get-PSDrive -PSProvider Debugger).Count)

cd zot:\
mkdir bar | Out-Null
cd Dbg:\foo
$stuff = @( dir )

Verify-AreEqual 1 $stuff.Count
Verify-AreEqual "bar" $stuff[ 0 ].Name

Remove-PSDrive $fooDrive
Verify-AreEqual 0 ($global:error.Count)
Verify-AreEqual ($initialDriveCount) ((Get-PSDrive -PSProvider Debugger).Count)


$fooDrive = $null
Verify-AreEqual 0 ($global:error.Count)
if( (Get-PathBugginessStyle) -eq 'RegistryProvider' )
{
    # This next command should fail (blocked because of Windows 8 Bugs 922001):
    $fooDrive = New-PSDrive fooDrive Debugger .\ -ErrorAction SilentlyContinue  # so that we don't see scary red stuff in the output
    Verify-AreEqual 1 ($global:error.Count)
    $global:error.Clear()
}
Verify-IsNull $fooDrive


cd \
rmdir .\foo -Recurse

Verify-AreEqual 0 ($global:error.Count)

