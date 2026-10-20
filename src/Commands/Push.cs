using System.Text;

namespace SourceGit.Commands
{
    public class Push : Command
    {
        public Push(string repo, Models.Branch local, Models.Remote remote, Models.Branch remoteBranch, bool withTags, bool checkSubmodules, bool track, bool force, bool noVerify)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;
            RequireSafeGitOperand(remote.Name, nameof(remote));
            RequireSafeGitOperand(local.Name, nameof(local));
            RequireSafeGitOperand(remoteBranch.Name, nameof(remoteBranch));

            var builder = new StringBuilder(1024);
            builder.Append("push --progress --verbose ");
            if (withTags)
                builder.Append("--tags ");
            if (checkSubmodules)
                builder.Append("--recurse-submodules=check ");
            if (track)
                builder.Append("-u ");
            if (force)
                builder.Append("--force-with-lease ");
            if (noVerify)
                builder.Append("--no-verify ");

            // --end-of-options: remote/ref names are repo-controlled; prevent a name
            // beginning with '-' from being parsed as a git option (option injection).
            builder.Append("--end-of-options ");
            builder.Append(remote.Name).Append(' ').Append(local.Name).Append(':').Append(remoteBranch.Name);
            Args = builder.ToString();
        }

        public Push(string repo, Models.Commit revision, Models.Remote remote, Models.Branch remoteBranch, bool force)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;
            RequireSafeGitOperand(remote.Name, nameof(remote));
            RequireSafeGitOperand(remoteBranch.Name, nameof(remoteBranch));

            var builder = new StringBuilder(1024);
            builder.Append("push --progress --verbose ");
            if (force)
                builder.Append("--force-with-lease ");

            builder.Append("--end-of-options ");
            builder.Append(remote.Name).Append(' ').Append(revision.SHA).Append(':').Append(remoteBranch.Name);
            Args = builder.ToString();
        }

        public Push(string repo, Models.Remote remote, string refname, bool isDelete)
        {
            WorkingDirectory = repo;
            Context = repo;
            SSHKey = remote.PrivateSSHKey;
            RequireSafeGitOperand(remote.Name, nameof(remote));
            RequireSafeGitOperand(refname, nameof(refname));

            var builder = new StringBuilder(512);
            builder.Append("push ");
            if (isDelete)
                builder.Append("--delete ");
            builder.Append("--end-of-options ");
            builder.Append(remote.Name).Append(' ').Append(refname);

            Args = builder.ToString();
        }
    }
}
