This DLL used to contain code necessary to deal with "long double" (10-byte floating point) values. I don't believe it is needed any longer. If it turns out it /is/ needed, we will need to write some new code from scratch, or get permission from the debugger team to use theirs.

This DLL might be useful to stick other bits of native code, so I'll keep it around for now.

