using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public partial class SSHKeyGenerator : ObservableValidator
    {
        public Models.SSHKeyType Type
        {
            get => _type;
            set => SetProperty(ref _type, value);
        }

        [Required(ErrorMessage = "Name is required.")]
        [RegularExpression(@"^[a-zA-Z0-9_\-]+$", ErrorMessage = "Name can only contain letters, numbers, underscores, and hyphens.")]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value, true);
        }

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address.")]
        public string Email
        {
            get => _email;
            set => SetProperty(ref _email, value, true);
        }

        public bool UsePassphrase
        {
            get => _usePassphrase;
            set
            {
                if (SetProperty(ref _usePassphrase, value))
                {
                    ValidateProperty(_passphrase, nameof(Passphrase));
                    ValidateProperty(_confirmedPassphrase, nameof(ConfirmedPassphrase));
                }
            }
        }

        [CustomValidation(typeof(SSHKeyGenerator), nameof(ValidatePassphrase))]
        public string Passphrase
        {
            get => _passphrase;
            set
            {
                if (SetProperty(ref _passphrase, value, true))
                    ValidateProperty(_confirmedPassphrase, nameof(ConfirmedPassphrase));
            }
        }

        [CustomValidation(typeof(SSHKeyGenerator), nameof(ValidateConfirmedPassphrase))]
        public string ConfirmedPassphrase
        {
            get => _confirmedPassphrase;
            set => SetProperty(ref _confirmedPassphrase, value, true);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public static ValidationResult ValidatePassphrase(string password, ValidationContext context)
        {
            var instance = (SSHKeyGenerator)context.ObjectInstance;
            if (!instance.UsePassphrase)
                return ValidationResult.Success;

            if (string.IsNullOrEmpty(password))
                return new ValidationResult("Passphrase is required!");

            if (!REG_PSWD_FORMAT().IsMatch(password))
                return new ValidationResult("Passphrase can only contain letters, numbers, and special characters: _ + - = @ # $ % ! &");

            return ValidationResult.Success;
        }

        public static ValidationResult ValidateConfirmedPassphrase(string confirmedPassword, ValidationContext context)
        {
            var instance = (SSHKeyGenerator)context.ObjectInstance;
            if (!instance.UsePassphrase)
                return ValidationResult.Success;

            if (confirmedPassword != instance.Passphrase)
                return new ValidationResult("Passphrase and confirmation do not match!");

            return ValidationResult.Success;
        }

        public Models.SSHKeyPair Run()
        {
            ErrorMessage = string.Empty;

            ValidateAllProperties();
            if (HasErrors)
                return null;

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
            if (!Directory.Exists(dir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch (Exception e)
                {
                    ErrorMessage = $"Failed to create .ssh directory: {e.Message}";
                    return null;
                }
            }

            var type = _type?.Cmdline ?? "-t ed25519";
            var passphrase = _usePassphrase ? _passphrase : string.Empty;
            var keyFile = Path.Combine(dir, _name);
            var publicKeyFile = keyFile + ".pub";

            // Never overwrite an existing key pair (ssh-keygen itself refuses without an
            // interactive confirmation we cannot show); fail clearly instead.
            if (File.Exists(keyFile) || File.Exists(publicKeyFile))
            {
                ErrorMessage = $"An SSH key named '{_name}' already exists.";
                return null;
            }

            // Build the key in a temp location so a failure can never leave a key at the
            // final path, and keep the passphrase off the process command line (visible to
            // other local users via ps / /proc): generate unencrypted, then set the
            // passphrase via `ssh-keygen -p` fed over stdin, then verify it is actually
            // encrypted before publishing.
            var tmpKey = Path.Combine(dir, $".{_name}.{Guid.NewGuid():N}.tmp");
            var tmpPub = tmpKey + ".pub";

            void Cleanup()
            {
                try
                {
                    if (File.Exists(tmpKey))
                        File.Delete(tmpKey);
                    if (File.Exists(tmpPub))
                        File.Delete(tmpPub);
                }
                catch
                {
                    // Best effort.
                }
            }

            // Runs ssh-keygen with a bounded wait so a prompt we cannot answer can never
            // hang the UI. When piping a passphrase over stdin, the askpass/terminal hints
            // are cleared so (stdin not being a tty) ssh-keygen consumes the piped value.
            // Returns the exit code, or null on timeout/launch failure.
            int? RunKeygen(string args, string stdinText)
            {
                var info = new ProcessStartInfo
                {
                    FileName = "ssh-keygen",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = stdinText != null,
                };

                if (stdinText != null)
                {
                    info.Environment.Remove("DISPLAY");
                    info.Environment.Remove("WAYLAND_DISPLAY");
                    info.Environment.Remove("SSH_ASKPASS");
                    info.Environment.Remove("SSH_ASKPASS_REQUIRE");
                }

                using var proc = Process.Start(info);
                if (stdinText != null)
                {
                    proc.StandardInput.Write(stdinText);
                    proc.StandardInput.Close();
                }

                if (!proc.WaitForExit(60000))
                {
                    try
                    {
                        proc.Kill(true);
                    }
                    catch
                    {
                        // Ignore.
                    }

                    return null;
                }

                return proc.ExitCode;
            }

            try
            {
                if (RunKeygen($"-q {type} -N \"\" -C {_email.Quoted()} -f {tmpKey.Quoted()}", null) != 0)
                {
                    ErrorMessage = "Failed to generate SSH key.";
                    Cleanup();
                    return null;
                }

                if (!string.IsNullOrEmpty(passphrase))
                {
                    // The freshly created key is unencrypted, so `ssh-keygen -p` asks only
                    // for the new passphrase and its confirmation (piped over stdin).
                    if (RunKeygen($"-p -f {tmpKey.Quoted()}", $"{passphrase}\n{passphrase}\n") != 0)
                    {
                        ErrorMessage = "Failed to set SSH key passphrase.";
                        Cleanup();
                        return null;
                    }

                    // Confirm the key is really encrypted now: extracting the public key
                    // with an empty passphrase must FAIL. If it succeeds, the passphrase
                    // did not take (e.g. a platform where stdin was not consulted) — refuse
                    // rather than publish an unencrypted key the user believes is protected.
                    if (RunKeygen($"-y -P \"\" -f {tmpKey.Quoted()}", null) == 0)
                    {
                        ErrorMessage = "Failed to protect SSH key with the provided passphrase.";
                        Cleanup();
                        return null;
                    }
                }

                // Publish. File.Move without overwrite throws if a pair appeared meanwhile,
                // so an existing key is never clobbered.
                File.Move(tmpKey, keyFile);
                File.Move(tmpPub, publicKeyFile);
            }
            catch (Exception e)
            {
                ErrorMessage = $"Failed to generate SSH key: {e.Message}";
                Cleanup();
                return null;
            }

            if (File.Exists(keyFile) && File.Exists(publicKeyFile))
                return new Models.SSHKeyPair(keyFile, publicKeyFile);

            ErrorMessage = "Failed to generate SSH key: Key files not found.";
            return null;
        }

        [GeneratedRegex(@"^[0-9a-zA-Z_\-\@\#\$\%\!\&\+\=]+$")]
        private static partial Regex REG_PSWD_FORMAT();

        private Models.SSHKeyType _type = Models.SSHKeyType.Supported[0];
        private string _name;
        private string _email;
        private bool _usePassphrase;
        private string _passphrase;
        private string _confirmedPassphrase;
        private string _errorMessage;
    }
}
