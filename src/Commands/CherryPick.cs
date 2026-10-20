using System.Text;

namespace SourceGit.Commands
{
    public class CherryPick : Command
    {
        public CherryPick(string repo, string commits, bool noCommit, bool appendSourceToMessage, string extraParams)
        {
            WorkingDirectory = repo;
            Context = repo;

            // `commits` is a space-separated list of commit ids; validate each so a
            // repo-controlled value cannot inject an option or an extra argument.
            // `extraParams` is app-supplied.
            foreach (var commit in commits.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
                RequireSafeGitOperand(commit, nameof(commits));

            var builder = new StringBuilder(1024);
            builder.Append("cherry-pick ");
            if (noCommit)
                builder.Append("-n ");
            if (appendSourceToMessage)
                builder.Append("-x ");
            if (!string.IsNullOrEmpty(extraParams))
                builder.Append(extraParams).Append(' ');

            Args = builder.Append(commits).ToString();
        }
    }
}
