using System;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    public class IssueTracker : ObservableObject
    {
        public bool IsShared
        {
            get => _isShared;
            set => SetProperty(ref _isShared, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string RegexString
        {
            get => _regexString;
            set
            {
                if (SetProperty(ref _regexString, value))
                {
                    try
                    {
                        // The pattern can come from a repo's shared .issuetracker file and
                        // is run against every commit subject/body on the UI thread, so a
                        // bounded match timeout guards against catastrophic backtracking
                        // (ReDoS) freezing the app.
                        _regex = new Regex(_regexString, RegexOptions.Multiline, TimeSpan.FromMilliseconds(500));
                    }
                    catch
                    {
                        _regex = null;
                    }
                }

                OnPropertyChanged(nameof(IsRegexValid));
            }
        }

        public bool IsRegexValid
        {
            get => _regex != null;
        }

        public string URLTemplate
        {
            get => _urlTemplate;
            set => SetProperty(ref _urlTemplate, value);
        }

        public void Matches(InlineElementCollector outs, string message)
        {
            if (_regex == null || string.IsNullOrEmpty(_urlTemplate))
                return;

            try
            {
                var matches = _regex.Matches(message);
                foreach (Match match in matches)
                {
                    var start = match.Index;
                    var len = match.Length;
                    if (outs.Intersect(start, len) != null)
                        continue;

                    var link = _urlTemplate;
                    for (var j = 1; j < match.Groups.Count; j++)
                    {
                        var group = match.Groups[j];
                        if (group.Success)
                            link = link.Replace($"${j}", group.Value);
                    }

                    outs.Add(new InlineElement(InlineElementType.Link, start, len, link));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological pattern exceeded the match timeout; skip issue links for
                // this message rather than block the UI.
            }
        }

        private bool _isShared;
        private string _name;
        private string _regexString;
        private string _urlTemplate;
        private Regex _regex = null;
    }
}
