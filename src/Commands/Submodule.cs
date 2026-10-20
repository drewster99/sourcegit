using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class Submodule : Command
    {
        public Submodule(string repo)
        {
            WorkingDirectory = repo;
            Context = repo;
        }

        // Forces git's secure transport defaults on a `submodule update`. The per-submodule
        // internal fetch/clone uses the submodule's own (repo-controlled) config, so a
        // crafted submodule could set `protocol.file.allow=always` (or ext) locally and
        // name an option-looking remote with a local/ext URL to run a program during that
        // fetch. Passing these on the command line overrides that malicious local config.
        // NOTE: a fully writable submodule config remains a broader git trust boundary
        // (hooks, core.fsmonitor, core.sshCommand, ...) that this cannot fully close; it
        // shuts the transport-based escalation. `--literal-pathspecs` additionally stops a
        // crafted submodule path from being treated as a glob.
        private const string SubmoduleUpdateHardening =
            "-c protocol.file.allow=user -c protocol.ext.allow=never --literal-pathspecs";

        public async Task<bool> AddAsync(string url, string relativePath, bool recursive)
        {
            // Adding a submodule is an explicit user action, so local transport is allowed
            // on purpose here (e.g. adding a file:// or local-path submodule).
            Args = $"-c protocol.file.allow=always --literal-pathspecs submodule add {url.Quoted()} {relativePath.Quoted()}";

            var succ = await ExecAsync().ConfigureAwait(false);
            if (!succ)
                return false;

            if (recursive)
                Args = $"-c protocol.file.allow=always --literal-pathspecs submodule update --init --recursive -- {relativePath.Quoted()}";
            else
                Args = $"-c protocol.file.allow=always --literal-pathspecs submodule update --init -- {relativePath.Quoted()}";
            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> SetURLAsync(string path, string url)
        {
            Args = $"--literal-pathspecs submodule set-url -- {path.Quoted()} {url.Quoted()}";
            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> SetBranchAsync(string path, string branch)
        {
            if (string.IsNullOrEmpty(branch))
                Args = $"--literal-pathspecs submodule set-branch -d -- {path.Quoted()}";
            else
                Args = $"--literal-pathspecs submodule set-branch -b {branch.Quoted()} -- {path.Quoted()}";

            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> UpdateAsync(List<string> modules, bool init, bool recursive, bool useRemote)
        {
            var builder = new StringBuilder();
            builder.Append(SubmoduleUpdateHardening).Append(" submodule update");

            if (init)
                builder.Append(" --init");
            if (recursive)
                builder.Append(" --recursive");
            if (useRemote)
                builder.Append(" --remote");
            if (modules.Count > 0)
            {
                builder.Append(" --");
                foreach (var module in modules)
                    builder.Append(' ').Append(module.Quoted());
            }

            Args = builder.ToString();
            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> DeinitAsync(string module, bool force)
        {
            Args = force ? $"--literal-pathspecs submodule deinit -f -- {module.Quoted()}" : $"--literal-pathspecs submodule deinit -- {module.Quoted()}";
            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> DeleteAsync(string module)
        {
            // --literal-pathspecs so a crafted submodule path (e.g. "*") cannot glob-expand
            // into other tracked files during the forced removal.
            Args = $"--literal-pathspecs rm -rf -- {module.Quoted()}";
            return await ExecAsync().ConfigureAwait(false);
        }
    }
}
