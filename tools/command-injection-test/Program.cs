using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

// Verifies the fixes for issue #1 (command injection) and the option-injection class
// of issue #2 (H1) against the real code:
//   C1 - GIT_SSH_COMMAND must shell-quote the (repo-controlled) ssh key path.
//   C2 - remote/refspec positionals must be preceded by --end-of-options (git)
//        or -- (git-lfs), so a name beginning with '-' cannot be an option.
//   H1 - other commands that take repo-controlled refs/branches/tags must reject
//        option-looking operands (Branch/Checkout/Merge/Tag/Reset/CherryPick/Rebase/
//        Revert/Blame/Worktree).

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

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch
        {
            return true;
        }
    }

    // An async command method validates before its first await, but the exception is
    // captured on the returned Task, so it must be observed by awaiting.
    private static bool ThrowsAsync(Func<Task> action)
    {
        try
        {
            action().GetAwaiter().GetResult();
            return false;
        }
        catch
        {
            return true;
        }
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

        // C2: a valid remote builds with --end-of-options; an untrusted operand that
        // would survive .NET argv parsing as an option or extra argument is rejected by
        // every remote-transport command (fetch/pull/push). The operand is placed in a
        // single Arguments string that .NET re-parses, so quotes/backslashes/whitespace
        // matter, and `git pull` even strips --end-of-options before its internal fetch.
        var goodRemote = new SourceGit.Models.Remote { Name = "origin", PrivateSSHKey = string.Empty };
        var branch = new SourceGit.Models.Branch { Name = "main" };

        Check(new SourceGit.Commands.Fetch("/repo", goodRemote, false, false).Args.Contains("--end-of-options origin", StringComparison.Ordinal), "C2 Fetch(full) builds with --end-of-options");
        Check(new SourceGit.Commands.Fetch("/repo", goodRemote).Args.Contains("--end-of-options origin", StringComparison.Ordinal), "C2 Fetch(auto) builds with --end-of-options");
        Check(new SourceGit.Commands.Fetch("/repo", goodRemote, branch, branch).Args.Contains("--end-of-options origin", StringComparison.Ordinal), "C2 Fetch(refspec) builds with --end-of-options");
        Check(new SourceGit.Commands.Push("/repo", branch, goodRemote, branch, false, false, false, false, false).Args.Contains("--end-of-options origin", StringComparison.Ordinal), "C2 Push(branch) builds with --end-of-options");
        Check(new SourceGit.Commands.Pull("/repo", goodRemote, branch, false).Args.Contains("--end-of-options origin", StringComparison.Ordinal), "C2 Pull(valid) builds with --end-of-options");

        // Each of these remote names would, after .NET argv parsing, become an option or
        // split into extra arguments. All must be rejected by fetch/pull/push.
        foreach (var name in new[] { "--upload-pack=x", "origin x", "\"\"--upload-pack=evil", "a\"b", "-x" })
        {
            var bad = new SourceGit.Models.Remote { Name = name, PrivateSSHKey = string.Empty };
            Check(Throws(() => new SourceGit.Commands.Fetch("/repo", bad, false, false)), $"C2 Fetch rejects «{name}»");
            Check(Throws(() => new SourceGit.Commands.Push("/repo", branch, bad, branch, false, false, false, false, false)), $"C2 Push rejects «{name}»");
            Check(Throws(() => new SourceGit.Commands.Pull("/repo", bad, branch, false)), $"C2 Pull rejects «{name}»");
        }

        // Branch/ref operands get the same treatment (incl. the quote-strip bypass).
        foreach (var bn in new[] { "-x", "\"\"--upload-pack=evil" })
        {
            var bad = new SourceGit.Models.Branch { Name = bn };
            Check(Throws(() => new SourceGit.Commands.Pull("/repo", goodRemote, bad, false)), $"C2 Pull rejects branch «{bn}»");
            Check(Throws(() => new SourceGit.Commands.Push("/repo", bad, goodRemote, bad, false, false, false, false, false)), $"C2 Push rejects branch «{bn}»");
        }

        // Valid operands must NOT be over-rejected: a non-ASCII U+00A0 (git permits it;
        // .NET does not treat it as a separator) and a Windows local-path pushRemote
        // containing backslashes (safe because double quotes are rejected).
        var unicode = new SourceGit.Models.Remote { Name = "a\u00A0b", PrivateSSHKey = string.Empty };
        Check(!Throws(() => new SourceGit.Commands.Fetch("/repo", unicode, false, false)), "C2 valid Unicode remote accepted");

        var winPath = new SourceGit.Models.Remote { Name = @"C:\repos\dest.git", PrivateSSHKey = string.Empty };
        Check(!Throws(() => new SourceGit.Commands.Fetch("/repo", winPath, false, false)), "C2 Windows local-path remote accepted");

        // H1: other commands reject option-looking repo-controlled refs/branches/tags.
        const string dash = "--upload-pack=x";
        Check(Throws(() => new SourceGit.Commands.Branch("/repo", dash)), "H1 Branch(ctor) rejects option-looking name");
        Check(!Throws(() => new SourceGit.Commands.Branch("/repo", "feature/x")), "H1 Branch(ctor) accepts valid name");
        Check(Throws(() => new SourceGit.Commands.Merge("/repo", dash, "--no-ff", false)), "H1 Merge rejects option-looking source");
        Check(!Throws(() => new SourceGit.Commands.Merge("/repo", "main", "--no-ff", false)), "H1 Merge accepts valid source + keeps mode flag");
        Check(Throws(() => new SourceGit.Commands.Tag("/repo", dash)), "H1 Tag(ctor) rejects option-looking name");
        Check(Throws(() => new SourceGit.Commands.Reset("/repo", dash, "--hard")), "H1 Reset rejects option-looking revision");
        Check(!Throws(() => new SourceGit.Commands.Reset("/repo", string.Empty, "--hard")), "H1 Reset allows empty revision (discard)");
        Check(Throws(() => new SourceGit.Commands.CherryPick("/repo", "abc " + dash, false, false, string.Empty)), "H1 CherryPick rejects option-looking commit token");
        Check(Throws(() => new SourceGit.Commands.Rebase("/repo", dash, false, false)), "H1 Rebase rejects option-looking basedOn");
        Check(Throws(() => new SourceGit.Commands.Revert("/repo", dash, false)), "H1 Revert rejects option-looking commit");
        Check(Throws(() => new SourceGit.Commands.Blame("/repo", "file.txt", dash, false)), "H1 Blame rejects option-looking revision");
        Check(ThrowsAsync(() => new SourceGit.Commands.Checkout("/repo").BranchAsync(dash, false)), "H1 Checkout.BranchAsync rejects option-looking branch");
        Check(ThrowsAsync(() => new SourceGit.Commands.Worktree("/repo").AddWithLocalBranchAsync("/tmp/wt", dash)), "H1 Worktree rejects option-looking branch");
        Check(Throws(() => new SourceGit.Commands.MergeTree("/repo", dash, "main")), "H1 MergeTree rejects option-looking source");
        Check(Throws(() => new SourceGit.Commands.MergeBase("/repo", dash, "main")), "H1 MergeBase rejects option-looking rev");
        Check(Throws(() => new SourceGit.Commands.QuerySingleCommit("/repo", "--output=/tmp/x")), "H1 QuerySingleCommit rejects --output= injection");
        Check(Throws(() => new SourceGit.Commands.QueryRevisionByRefName("/repo", dash)), "H1 QueryRevisionByRefName rejects option-looking ref");
        Check(Throws(() => new SourceGit.Commands.CompareRevisions("/repo", dash, "HEAD")), "H1 CompareRevisions rejects option-looking start");
        Check(Throws(() => new SourceGit.Commands.Replay("/repo", dash, "a..b")), "H1 Replay rejects option-looking onto");

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "All command-injection regression checks passed."
            : $"{_failures} check(s) FAILED.");
        return _failures == 0 ? 0 : 1;
    }
}
