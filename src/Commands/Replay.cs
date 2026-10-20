using System.Diagnostics;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class Replay : Command
    {
        public Replay(string repo, string onto, string range)
        {
            WorkingDirectory = repo;
            Context = repo;
            RaiseError = false;

            // onto/range are repo-controlled revisions; reject option-looking values.
            // range may be several space-separated revisions/ranges, so validate each.
            RequireSafeGitOperand(onto, nameof(onto));
            foreach (var rev in range.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
                RequireSafeGitOperand(rev, nameof(range));

            Args = $"replay --onto {onto} {range}";
        }

        public async Task<int> GetExitCodeAsync()
        {
            using var proc = new Process();
            proc.StartInfo = CreateGitStartInfo(false);

            var exitCode = -1;
            try
            {
                proc.Start();
                await proc.WaitForExitAsync().ConfigureAwait(false);
                exitCode = proc.ExitCode;
            }
            catch
            {
                // Ignore any exceptions and just return -1
            }

            return exitCode;
        }
    }
}
