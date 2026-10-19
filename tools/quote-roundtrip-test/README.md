# quote-roundtrip-test

A standalone correctness check for `App.Extensions.Quoted()`.

`Quoted()` builds the string assigned to `ProcessStartInfo.Arguments`. .NET
re-parses that string into `argv` using MSVC-style rules on every platform, so
a backslash before a quote is significant. This test asserts the invariant that
should hold:

> `Quoted(name)` must parse back into exactly one `argv` entry equal to `name`.

It verifies this against .NET's real argument parser by spawning a child
process and inspecting the `argv` it receives. It uses only benign filenames;
there is no git invocation and no injection payload.

## Run

```sh
dotnet run -c Release
```

Exit code is 0 if every input round-trips, 1 otherwise. Filenames ending in a
backslash, or containing a backslash-quote, currently fail: they reach git as a
different path than the one the user selected.
