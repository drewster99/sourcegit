namespace SourceGit.Commands
{
    public class Reset : Command
    {
        public Reset(string repo, string revision, string mode)
        {
            WorkingDirectory = repo;
            Context = repo;

            // `mode` is an app-supplied flag. `revision` is repo-controlled; validate it
            // when present (it is empty for a plain reset-to-HEAD, e.g. discard).
            if (!string.IsNullOrEmpty(revision))
                RequireSafeGitOperand(revision, nameof(revision));

            Args = $"reset {mode} {revision}";
        }

        public Reset(string repo, string pathspec)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = $"reset --pathspec-from-file={pathspec.Quoted()}";
        }
    }
}
