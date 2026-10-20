using System;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class Remote : Command
    {
        public Remote(string repo)
        {
            WorkingDirectory = repo;
            Context = repo;
        }

        public async Task<bool> AddAsync(string name, string url)
        {
            Args = $"remote add {RequireSafeGitOperand(name, nameof(name))} {QuoteUrl(url)}";
            return await ExecAsync();
        }

        public async Task<bool> DeleteAsync(string name)
        {
            Args = $"remote remove {RequireSafeGitOperand(name, nameof(name))}";
            return await ExecAsync();
        }

        public async Task<bool> RenameAsync(string name, string to)
        {
            Args = $"remote rename {RequireSafeGitOperand(name, nameof(name))} {RequireSafeGitOperand(to, nameof(to))}";
            return await ExecAsync();
        }

        public async Task<bool> PruneAsync(string name, string sshKey)
        {
            SSHKey = sshKey;
            Args = $"remote prune {RequireSafeGitOperand(name, nameof(name))}";
            return await ExecAsync();
        }

        public async Task<bool> SetURLAsync(string name, string url, bool isDelete, bool isPush)
        {
            var builder = new StringBuilder();
            builder.Append("remote set-url ");
            if (isDelete)
                builder.Append("--delete ");
            if (isPush)
                builder.Append("--push ");
            builder.Append(RequireSafeGitOperand(name, nameof(name))).Append(' ').Append(QuoteUrl(url));

            Args = builder.ToString();
            return await ExecAsync();
        }

        // A remote URL may legitimately contain spaces (local paths), so it is quoted
        // rather than passed through RequireSafeGitOperand; but a URL beginning with '-'
        // (which survives .NET argv parsing as an option) is rejected.
        private static string QuoteUrl(string url)
        {
            if (!string.IsNullOrEmpty(url) && url[0] == '-')
                throw new ArgumentException($"Refusing to use a remote URL beginning with '-': {url}", nameof(url));

            return url.Quoted();
        }
    }
}
