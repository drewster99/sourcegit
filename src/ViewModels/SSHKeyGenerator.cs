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

            // Create the key WITHOUT a passphrase on the command line, then (if requested)
            // set the passphrase via `ssh-keygen -p` with the secret fed over stdin. This
            // keeps the passphrase out of the process argument list, which other local
            // users can read via `ps` / `/proc/<pid>/cmdline`.
            var start = new ProcessStartInfo();
            start.FileName = "ssh-keygen";
            start.Arguments = $"-q {type} -N \"\" -C {_email.Quoted()} -f {keyFile.Quoted()}";
            start.UseShellExecute = false;
            start.CreateNoWindow = true;

            try
            {
                var proc = Process.Start(start);
                proc.WaitForExit();
                var generated = proc.ExitCode == 0;
                proc.Close();

                if (!generated)
                {
                    ErrorMessage = "Failed to generate SSH key.";
                    return null;
                }

                if (!string.IsNullOrEmpty(passphrase))
                {
                    var setPass = new ProcessStartInfo();
                    setPass.FileName = "ssh-keygen";
                    setPass.Arguments = $"-p -f {keyFile.Quoted()}";
                    setPass.UseShellExecute = false;
                    setPass.CreateNoWindow = true;
                    setPass.RedirectStandardInput = true;
                    setPass.RedirectStandardOutput = true;
                    setPass.RedirectStandardError = true;

                    using var passProc = Process.Start(setPass);
                    // The freshly created key is unencrypted, so ssh-keygen -p asks only
                    // for the new passphrase and its confirmation.
                    passProc.StandardInput.WriteLine(passphrase);
                    passProc.StandardInput.WriteLine(passphrase);
                    passProc.StandardInput.Close();
                    passProc.WaitForExit();

                    if (passProc.ExitCode != 0)
                    {
                        ErrorMessage = "Failed to set SSH key passphrase.";
                        return null;
                    }
                }
            }
            catch (Exception e)
            {
                ErrorMessage = $"Failed to generate SSH key: {e.Message}";
                return null;
            }

            var publicKeyFile = keyFile + ".pub";
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
