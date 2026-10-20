using System.Text;

namespace SourceGit.Commands
{
    public class Fetch : Command
    {
        public Fetch(string repo, Models.Remote remote, bool noTags, bool force)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;
            RequireSafeGitOperand(remote.Name, nameof(remote));

            var builder = new StringBuilder(512);
            builder.Append("fetch --progress --verbose ");
            builder.Append(noTags ? "--no-tags " : "--tags ");
            if (force)
                builder.Append("--force ");
            // --end-of-options: the remote name is repo-controlled; prevent a name
            // beginning with '-' from being parsed as a git option (option injection).
            builder.Append("--end-of-options ").Append(remote.Name);

            Args = builder.ToString();
        }

        public Fetch(string repo, Models.Remote remote)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;
            RaiseError = false;
            RequireSafeGitOperand(remote.Name, nameof(remote));
            Args = $"fetch --progress --verbose --end-of-options {remote.Name}";
        }

        public Fetch(string repo, Models.Remote remote, Models.Branch remoteBranch, Models.Branch local)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;
            RequireSafeGitOperand(remote.Name, nameof(remote));
            RequireSafeGitOperand(remoteBranch.Name, nameof(remoteBranch));
            RequireSafeGitOperand(local.Name, nameof(local));
            Args = $"fetch --progress --verbose --end-of-options {remote.Name} {remoteBranch.Name}:{local.Name}";
        }
    }
}
