# Internal branch guard

Don't do any development on this branch.

This branch exists to prevent an accidental push of `internal/*` branches.

Nearly all development on DbgShell should be public. However, there are a few tiny scraps
of Microsoft-internal stuff: some symbol value conversion scripts that aren't useful
outside of Microsoft; a NuGet package of unreleased versions of debugger binaries
(dbgeng.dll et al) for testing; etc. This stuff isn't "secret" per se, but I want to keep
the public source clean of such extraneous cruft to avoid confusion. Thus we'll keep such
changes in branches named `internal/*`. This public dummy branch will help prevent anyone
from accidentally pushing such a branch to the public repo.

