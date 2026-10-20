namespace SourceGit.Commands
{
    public class FormatPatch : Command
    {
        public FormatPatch(string repo, string commit, string saveTo)
        {
            WorkingDirectory = repo;
            Context = repo;
            Editor = EditorType.None;
            Args = $"format-patch {RequireSafeGitOperand(commit, nameof(commit))} -1 --output={saveTo.Quoted()}";
        }
    }
}
