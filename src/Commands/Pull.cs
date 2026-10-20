using System;
using System.Text;

namespace SourceGit.Commands
{
    public class Pull : Command
    {
        public Pull(string repo, Models.Remote remote, Models.Branch remoteBranch, bool useRebase)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;

            // `git pull` forwards its operands to an internal `git fetch` AFTER dropping
            // --end-of-options, so the terminator cannot protect this command. Refuse to
            // run with a remote/branch operand that could be parsed as a git option.
            if (!IsSafeGitOperand(remote.Name))
                throw new ArgumentException($"Unsafe remote name for pull: '{remote.Name}'", nameof(remote));
            if (remoteBranch != null && !IsSafeGitOperand(remoteBranch.Name))
                throw new ArgumentException($"Unsafe branch name for pull: '{remoteBranch.Name}'", nameof(remoteBranch));

            var builder = new StringBuilder(512);
            builder
                .Append("pull --verbose --progress --rebase=")
                .Append(useRebase ? "true" : "false")
                // --end-of-options: remote/branch names are repo-controlled; prevent a
                // name beginning with '-' from being parsed as a git option.
                .Append(" --end-of-options ")
                .Append(remote.Name);

            if (remoteBranch != null)
                builder.Append(' ').Append(remoteBranch.Name);

            Args = builder.ToString();
        }
    }
}
