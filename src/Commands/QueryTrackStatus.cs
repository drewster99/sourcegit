using System;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class QueryTrackStatus : Command
    {
        public QueryTrackStatus(string repo)
        {
            WorkingDirectory = repo;
            Context = repo;
        }

        public async Task GetResultAsync(Models.Branch local, Models.Branch remote)
        {
            // Branch heads are commit ids from for-each-ref, but validate defensively so
            // a crafted value can never be parsed as an option (they may be empty).
            if (!string.IsNullOrEmpty(local.Head))
                RequireSafeGitOperand(local.Head, nameof(local));
            if (!string.IsNullOrEmpty(remote.Head))
                RequireSafeGitOperand(remote.Head, nameof(remote));

            Args = $"rev-list --left-right {local.Head}...{remote.Head}";

            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return;

            var lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line[0] == '>')
                    local.Behind.Add(line.Substring(1));
                else
                    local.Ahead.Add(line.Substring(1));
            }
        }
    }
}
