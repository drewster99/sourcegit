# command-injection-test

Standalone regression harness for issue #1 (command injection). References the real
`SourceGit` project and exercises the actual `Fetch`/`Pull`/`Push` builders and
`Command.CreateGitStartInfo`, so it stays in lock-step with production code.

- **C1**: `GIT_SSH_COMMAND` must shell-quote the (repo-controlled) ssh key path; a
  crafted `remote.<name>.sshkey` must round-trip as one literal shell word.
- **C2**: remote/refspec positionals must be preceded by `--end-of-options`, so a
  remote name beginning with `-` cannot be parsed as a git option.

Run: `dotnet run --project tools/command-injection-test -c Debug`
