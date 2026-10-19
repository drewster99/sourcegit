# SourceGit Security & Suitability Assessment

- **Assessed:** 2026-10-19
- **Upstream:** https://github.com/sourcegit-scm/sourcegit
- **Revision:** `cb11d687` (tag `v2026.22`, `master`)
- **Target platform:** macOS (build from source, upstream release zip, or Homebrew cask)

## Verdict

**Reasonable to use on macOS.** No malware, telemetry, analytics, or hidden network activity was found. One real code weakness (argument quoting, below) and a few privacy and hygiene issues are worth knowing about. The largest non-code risk is the **pending maintainer handover** (issue #2722).

---

## 1. Install channels

| Channel | What you get | Trust chain |
|---|---|---|
| Build from source | Your own build | Upstream source + NuGet + AvaloniaEdit fork |
| GitHub release zip | Ad-hoc signed, **not notarized** | Upstream GitHub Actions only |
| Homebrew cask (`brew install --cask sourcegit`) | DMG from `ybeapps/homebrew-sourcegit`, re-signed and notarized as **"Developer ID Application: Yitschak Ben Ezra (D2K4552WY3)"** | Upstream CI **plus** a third-party signer |

### Homebrew build verification (v2026.22, arm64)
- Upstream `sourcegit_2026.22.osx-arm64.zip` SHA-256 `13f49677…4a1e44` matches the GitHub release digest.
- Homebrew DMG SHA-256 `de0b5b19…2ee4a1` matches the cask.
- Every file in the DMG is identical to the upstream zip except the code signatures. After `codesign --remove-signature`, all 6 Mach-O binaries are **byte-identical** (`SourceGit`, `setsid`, `libAvaloniaNative`, `libHarfBuzzSharp`, `libonigwrap`, `libSkiaSharp`).
- The tap's workflow (`.github/workflows/update_tap_upon_new_version.yml`) downloads the upstream zip **without checksum verification**, re-signs it, and notarizes it. It runs automatically on upstream release dispatch and also 4× daily.
- The x64 build was not compared; it is produced by the same workflow.

### Entitlements added by the Homebrew re-signer

```
com.apple.security.cs.allow-jit
com.apple.security.cs.allow-unsigned-executable-memory
com.apple.security.cs.disable-library-validation
```

These match the boilerplate example in [Avalonia's macOS deployment docs](https://github.com/AvaloniaUI/avalonia-docs/blob/main/docs/deployment/macos.mdx), minus `allow-dyld-environment-variables` and the sandbox key. That boilerplate targets CoreCLR (JIT) apps.

| Entitlement | Why CoreCLR apps use it | Needed here? |
|---|---|---|
| `allow-jit` | The JIT emits code at runtime | **Probably not.** Release builds are NativeAOT (`PublishAot=true`); the bundle contains no `libcoreclr.dylib` or managed DLLs |
| `allow-unsigned-executable-memory` | Older or x64 CoreCLR writable+executable memory | **Probably not**, for the same reason |
| `disable-library-validation` | Load dylibs signed by another team (e.g. NuGet native libs) | **Probably not.** All dylibs are re-signed with the same Team ID |

No runtime code generation was found: regexes use `[GeneratedRegex]`, Oniguruma is an interpreter, and Skia does not JIT.

**Cost:**
- `disable-library-validation` lets an unsigned or foreign dylib load into a notarized process.
- The two memory entitlements weaken the W^X protection that hardens memory-corruption bugs, e.g. in the image decoders.

**Unverified:** that the app runs without them. To test: re-sign with hardened runtime and an empty entitlements file, then exercise the repo view, diffs, image diffs, and syntax highlighting.

### Recommendation
Prefer **building from source**, or use the **upstream zip** (`xattr -dr com.apple.quarantine SourceGit.app`). If building, sign with your own Developer ID. Ad-hoc signing breaks macOS Local Network permission tracking (see issue #2752). Use Homebrew only if you accept trusting the third-party signer for future releases.

---

## 2. Supply chain

### NuGet packages (`src/SourceGit.csproj`)
| Package | Version | GitHub advisories |
|---|---|---|
| Avalonia / .Desktop / .Fonts.Inter / .Themes.Fluent | 11.3.22 | none |
| Avalonia.Controls.DataGrid | 11.3.13 | none |
| Azure.AI.OpenAI | 2.9.0-**beta.1** (prerelease) | none |
| OpenAI | 2.10.0 | none |
| CommunityToolkit.Mvvm | 8.4.2 | none |
| BitMiracle.LibTiff.NET | 2.4.660 | none |
| Pfim | 0.11.4 | none |
| StbImageSharp | 2.30.15 | none |
| TextMateSharp / .Grammars (via AvaloniaEdit) | 2.0.4 | none |
| SkiaSharp (transitive) | 2.88.9 | none (libwebp GHSA-j7hp-h8jx-5ppr fixed in 2.88.6) |

### AvaloniaEdit submodule
- `depends/AvaloniaEdit` points to the fork `love-linger/AvaloniaEdit` at `ddf00a91`. That account is the SourceGit maintainer.
- It has 3 commits over upstream merge-base `7fa5d882`: editor bug fixes, line-ending handling, and version and target-framework bumps.
- It adds **no** network, process, or file I/O.
- Its `NuGet.config` adds Avalonia's official feed, `nuget-feed-all.avaloniaui.net`.

### CI (`.github/workflows`)
- Builds run publicly on GitHub Actions; there are no secrets in the build steps.
- Actions are pinned by **tag, not SHA**, including the third-party `geekyeggo/delete-artifact@v6`. This is minor.
- `homebrew-notify.yml` dispatches to `ybeapps/homebrew-sourcegit` with a repo token.
- The macOS package is ad-hoc signed (`codesign --sign -`) and includes a 15-line `setsid` helper compiled from `tools/setsid-macos/setsid.c`.

### Shipped binary
URLs embedded in the release binary match the source exactly: GitHub, Gravatar, `sourcegit-scm.github.io`, the OpenAI default endpoint, and grammar/license metadata. There are no unexpected endpoints.

---

## 3. Network behavior

| What | When | Notes |
|---|---|---|
| Update check: `https://sourcegit-scm.github.io/data/version.json` | At most once a day; can be disabled | Only shows a title-bar button or opens a browser. **No auto-download or execution.** |
| Avatars: `https://www.gravatar.com/avatar/{md5(email)}` or `https://avatars.githubusercontent.com/{user}` | For every commit author shown | **Always on, no off switch.** Leaks contributor identities from private or client repos. MD5 of an email is dictionary-reversible. |
| AI (OpenAI / Azure / compatible) | Only if configured | API key stored **in plaintext** in `~/Library/Application Support/SourceGit/preferences.json` (or read from an env var). No Keychain. |
| `HTTPSValidator` probe | Never: `Add()` has no callers | Dead code; would disable TLS validation if used |

There is no telemetry, analytics, or crash reporting.

---

## 4. Code findings

Threat model: a malicious repository or remote.

### 4.1 Argument quoting does not escape backslashes (medium–high; found by code review, not executed)
- `src/App.Extensions.cs:12-20`: `Quoted()` wraps the value in `"…"` and escapes only `"`, not `\`.
- `ProcessStartInfo.Arguments` is split using Windows/MSVC rules on all platforms. A value containing `\"` becomes `\\"`, which parses as a literal backslash followed by a closing quote. The rest of the value then becomes separate arguments.
- File names may contain `\`, `"`, and spaces on macOS and Linux.
- **Exposed call sites** (path placed with no `--` end-of-options marker before it):
  - `src/Commands/QueryFileContent.cs:15`: `show {rev}:{file}`
  - `src/Commands/SaveRevisionFile.cs:24`: `show {rev}:{file}`
  - `src/Commands/QueryFileSize.cs:11`: `cat-file -s {rev}:{file}`
- **Trigger:** viewing or saving a file from a hostile repo's history. Injected tokens are read as `git show` options (e.g. `--output=<path>`).
- Most other path-taking commands put `--` before paths, which limits injected tokens to pathspecs.
- Not confirmed at runtime because the .NET SDK was not installed during the assessment. **Not yet reported upstream.**

### 4.2 Custom-action placeholders are substituted unquoted (low)
- `src/ViewModels/ExecuteCustomAction.cs:236-257`: `${FILE}`, `${BRANCH}`, and `${TAG}` are inserted raw into the argument string.
- This only matters if you define a custom action and run it against hostile names.

### 4.3 SSH key passphrase on the command line (low)
- `src/ViewModels/SSHKeyGenerator.cs:126`: `ssh-keygen -N <passphrase>`. The passphrase is briefly visible in `ps` to the same user.

### 4.4 AI API key stored in plaintext (policy issue)
See §3. It should use the Keychain on macOS and libsecret on Linux.

### Checked and found OK
- **URL opening** (`src/Native/OS.cs:251-271`): allowlists well-formed absolute `http`, `https`, and `ftp` URLs. Fixed in June 2026 by PR #2475; before that, Windows passed links through `cmd /c start`.
- **Repo-specific settings and custom actions** are stored in `.git/sourcegit.settings`, which is not cloned. The committed `.issuetracker` holds only regex→URL rules, and the URL allowlist above covers them.
- **`safe.directory`** is added only through a user-confirmed Trust dialog (`src/ViewModels/TrustRepository.cs`).
- **The AI file-read tool** is limited to `git diff --cached -- <path>`.
- **The macOS `open` calls** for URLs are safe because the URL validation rejects spaces.
- **Git process environment:** forces `credential.helper`, `core.editor`, and `SSH_ASKPASS` to SourceGit itself, and the C locale.

### General caveat
Like any git GUI, SourceGit runs your `git`, which honors the repo's `.git/config` (e.g. `core.fsmonitor`) and hooks. Do not open repositories received as archives that include `.git/`.

---

## 5. Project health: open issues and PRs (158 issues, 43 PRs)

### Notable
1. **Maintainer handover (#2722, 2026-09-17).**
   - `love-linger`, author of ~61 of the last 100 commits, is seeking a successor and will only fix bugs until handover, then leave.
   - No candidate has been named yet.
   - The new maintainers will control releases, `version.json`, and the Homebrew re-sign trigger.
   - **Pin a version and vet the new maintainers before upgrading past the handover.**
2. **No security policy (#1431, open since 2025-06).** There is no private disclosure channel and no published GitHub security advisories.
3. **Security fixes come from outside contributors.** Example: #2475, a Windows shell injection via `.issuetracker` links, by ChiahongHong. There is no deliberate security review process.
4. **No automated tests.** PR #2212 "Add xUnit tests" has been open since 2026-03.
5. **Unsigned Windows builds are blocked** by Defender ASR / Smart App Control (#2735, #2578; Trend Micro false positive #1802). These are reputation blocks, not real detections.

### macOS-specific
- **#2752:** v2026.21 on Intel Macs could not reach LAN git servers. This is macOS Local Network permission tied to the code signature; re-signing fixed it.
- **#2670 (closed):** a v2026.19 Intel regression broke clone, fetch, pull, and push. It shows thin release testing.
- **#1924:** no HEIC preview.

### PRs to watch
- **#2763 "Enable auto-fetch by default":** would run `git fetch` every 10 minutes for every open repo. #2718 already links background git activity to stale `index.lock` conflicts with other git tools. Disable it if this merges.
- **#2512:** AI service configuration rework. It does not address plaintext API key storage.
- No other open PR touches security, networking, or process launching.

---

## 6. Recommendations

1. Build from source and sign with your own Developer ID, or use the upstream zip. Avoid the Homebrew cask unless you accept the third-party signer.
2. Pin to a reviewed version, and re-assess after the maintainer handover.
3. Do not configure the AI feature until API keys move to the Keychain.
4. Be aware that avatars leak author email hashes to Gravatar. A local patch would be needed to disable this.
5. Confirm finding 4.1 with a .NET harness, then report it upstream. There is no security policy, so the report would have to be a public issue or PR. Fix: escape backslashes per MSVC rules, or use `ProcessStartInfo.ArgumentList`; and add `--` before paths.
6. Keep auto-fetch off.

## 7. Method
- Read the source for networking, process launching, quoting, native interop (`src/Native`), the AI integration, custom actions, settings storage, and update checks.
- Diffed the AvaloniaEdit fork against upstream.
- Reviewed the CI workflows, packaging scripts, and the Homebrew tap's workflow.
- Downloaded the upstream and Homebrew arm64 builds, compared them file by file and with signatures stripped, and inspected signatures and entitlements.
- Extracted the URLs embedded in the shipped binaries.
- Queried GitHub Security Advisories for every NuGet dependency.
- Reviewed all open issues and PRs, plus closed issues mentioning security, malware, or signing.
