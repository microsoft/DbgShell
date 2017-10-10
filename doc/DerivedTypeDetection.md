# Derived Type Detection
DbgShell lets you see symbol values as they are, not just as they are declared.

For instance, if you have a member field that is declared in source code as being an
`IFoo*`, but at runtime the member points at a `CFooImpl`, when you dump the object in
DbgShell, you'll see a CFooImpl.

It works by using vtable and base class information found in the PDB. It does not require
anything from you. However, if you run into some sort of problem (or just want to see how
good you have it), you can bypass it by using the `DbgSymbol.GetValue( bool
skipTypeConversion, bool skipDerivedTypeDetection )` method.

## Examples
TBD


