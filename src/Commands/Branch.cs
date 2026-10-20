using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class Branch : Command
    {
        public Branch(string repo, string name)
        {
            WorkingDirectory = repo;
            Context = repo;
            // The branch name is repo-controlled; reject option-looking/unsafe values so
            // it can never be parsed as a git option on the command line.
            _name = RequireSafeGitOperand(name, nameof(name));
        }

        public async Task<bool> CreateAsync(string basedOn, bool force)
        {
            var builder = new StringBuilder();
            builder.Append("branch ");
            if (force)
                builder.Append("-f ");
            builder.Append(_name);
            builder.Append(" ");
            builder.Append(RequireSafeGitOperand(basedOn, nameof(basedOn)));

            Args = builder.ToString();
            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> RenameAsync(string to)
        {
            Args = $"branch -M {_name} {RequireSafeGitOperand(to, nameof(to))}";
            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> SetUpstreamAsync(Models.Branch tracking)
        {
            if (tracking == null)
                Args = $"branch {_name} --unset-upstream";
            else
                Args = $"branch {_name} -u {RequireSafeGitOperand(tracking.FriendlyName, nameof(tracking))}";

            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> DeleteLocalAsync(bool force)
        {
            Args = $"branch {(force ? "-D" : "-d")} {_name}";
            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> DeleteRemoteAsync(string remote, bool force)
        {
            Args = $"branch {(force ? "-D" : "-d")} -r {RequireSafeGitOperand(remote, nameof(remote))}/{_name}";
            return await ExecAsync().ConfigureAwait(false);
        }

        private readonly string _name;
    }
}
