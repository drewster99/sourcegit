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
