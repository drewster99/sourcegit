using System;
using System.Diagnostics;
using System.Text;

// Verifies the fixes for issue #1 (command injection) against the real code:
//   C1 - GIT_SSH_COMMAND must shell-quote the (repo-controlled) ssh key path.
//   C2 - remote/refspec positionals must be preceded by --end-of-options (git)
//        or -- (git-lfs), so a name beginning with '-' cannot be an option.

internal static class Program
{
    private sealed class Probe : SourceGit.Commands.Command
    {
        public ProcessStartInfo Build() => CreateGitStartInfo(false);
    }

    private static int _failures;

    private static void Check(bool ok, string label)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + label);
        if (!ok)
            _failures++;
    }

    // Decode a POSIX sh word made of '...' segments plus \-escapes, back to its literal.
    private static string PosixUnquote(string s)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] == '\'')
            {
                i++;
                while (i < s.Length && s[i] != '\'') { sb.Append(s[i]); i++; }
                i++;
            }
            else if (s[i] == '\\')
            {
                i++;
                if (i < s.Length) { sb.Append(s[i]); i++; }
            }
            else { sb.Append(s[i]); i++; }
        }
        return sb.ToString();
    }

    public static int Main()
    {
        // C1: a malicious key path round-trips to exactly itself inside the shell word,
        // i.e. nothing escapes the quoting to become a separate shell token/command.
        foreach (var key in new[]
        {
            "/home/me/.ssh/id_ed25519",
            "/tmp/k' ; touch /tmp/PWNED ; echo '",
            "/path/with spaces/key",
            "weird'\"$(whoami)`id`\\",
        })
        {
            var env = new Probe { SSHKey = key }.Build().Environment["GIT_SSH_COMMAND"];
            const string prefix = "ssh -i ";
            const string suffix = " -o AddKeysToAgent=yes";
            var ok = env.StartsWith(prefix, StringComparison.Ordinal) &&
                     env.EndsWith(suffix, StringComparison.Ordinal);
            if (ok)
            {
                var word = env.Substring(prefix.Length, env.Length - prefix.Length - suffix.Length);
                ok = PosixUnquote(word) == key;
            }
            Check(ok, $"C1 ssh key quoted+round-trips: «{key}»");
        }

        // C2: option-looking remote name is neutralized by an end-of-options separator.
        const string evil = "--upload-pack=touch /tmp/PWNED";
        var remote = new SourceGit.Models.Remote { Name = evil, PrivateSSHKey = string.Empty };
        var branch = new SourceGit.Models.Branch { Name = "main" };

        var fetch1 = new SourceGit.Commands.Fetch("/repo", remote, false, false).Args;
        Check(fetch1.Contains("--end-of-options " + evil, StringComparison.Ordinal), "C2 Fetch(full) --end-of-options");

        var fetch2 = new SourceGit.Commands.Fetch("/repo", remote).Args;
        Check(fetch2.Contains("--end-of-options " + evil, StringComparison.Ordinal), "C2 Fetch(auto) --end-of-options");

        var fetch3 = new SourceGit.Commands.Fetch("/repo", remote, branch, branch).Args;
        Check(fetch3.Contains("--end-of-options " + evil, StringComparison.Ordinal), "C2 Fetch(refspec) --end-of-options");

        var pull = new SourceGit.Commands.Pull("/repo", remote, branch, false).Args;
        Check(pull.Contains("--end-of-options " + evil, StringComparison.Ordinal), "C2 Pull --end-of-options");

        var push1 = new SourceGit.Commands.Push("/repo", branch, remote, branch, false, false, false, false, false).Args;
        Check(push1.Contains("--end-of-options " + evil, StringComparison.Ordinal), "C2 Push(branch) --end-of-options");

        var push3 = new SourceGit.Commands.Push("/repo", remote, "refs/heads/x", true).Args;
        Check(push3.Contains("--end-of-options " + evil, StringComparison.Ordinal), "C2 Push(delete) --end-of-options");

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "All command-injection regression checks passed."
            : $"{_failures} check(s) FAILED.");
        return _failures == 0 ? 0 : 1;
    }
}
