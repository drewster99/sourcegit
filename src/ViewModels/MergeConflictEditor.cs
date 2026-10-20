using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class MergeConflictEditor : ObservableObject
    {
        public string FilePath
        {
            get => _filePath;
        }

        public object Mine
        {
            get;
        }

        public object Theirs
        {
            get;
        }

        public string Error
        {
            get => _error;
            private set => SetProperty(ref _error, value);
        }

        public List<Models.ConflictLine> OursLines
        {
            get => _oursLines;
            private set => SetProperty(ref _oursLines, value);
        }

        public List<Models.ConflictLine> TheirsLines
        {
            get => _theirsLines;
            private set => SetProperty(ref _theirsLines, value);
        }

        public List<Models.ConflictLine> ResultLines
        {
            get => _resultLines;
            private set => SetProperty(ref _resultLines, value);
        }

        public int MaxLineNumber
        {
            get => _maxLineNumber;
            private set => SetProperty(ref _maxLineNumber, value);
        }

        public int UnsolvedCount
        {
            get => _unsolvedCount;
            private set => SetProperty(ref _unsolvedCount, value);
        }

        public Vector ScrollOffset
        {
            get => _scrollOffset;
            set => SetProperty(ref _scrollOffset, value);
        }

        public Models.ConflictSelectedChunk SelectedChunk
        {
            get => _selectedChunk;
            set => SetProperty(ref _selectedChunk, value);
        }

        public IReadOnlyList<Models.ConflictRegion> ConflictRegions
        {
            get => _conflictRegions;
        }

        public MergeConflictEditor(Repository repo, Models.Commit head, string filePath)
        {
            _repo = repo;
            _filePath = filePath;

            (Mine, Theirs) = repo.InProgressContext switch
            {
                CherryPickInProgress cherryPick => (head, cherryPick.Head),
                RebaseInProgress rebase => (rebase.Onto, rebase.StoppedAt),
                RevertInProgress revert => (head, revert.Head),
                MergeInProgress merge => (head, merge.Source),
                _ => (head, (object)"Stash or Patch"),
            };

            var workingCopyPath = Path.Combine(_repo.FullPath, _filePath);
            var workingCopyContent = string.Empty;
            if (File.Exists(workingCopyPath))
            {
                // Preserve the file's encoding and refuse (rather than silently corrupt)
                // a file that is not valid UTF-8, so resolving a conflict can never mangle
                // a non-UTF-8 source file.
                if (!TryDecodePreservingEncoding(File.ReadAllBytes(workingCopyPath), out workingCopyContent, out _outputEncoding))
                {
                    _error = "File is binary or not UTF-8; not supported.";
                    return;
                }
            }

            if (workingCopyContent.IndexOf('\0', StringComparison.Ordinal) >= 0)
            {
                _error = "Binary file is not supported.";
                return;
            }

            ParseOriginalContent(workingCopyContent);
            RefreshDisplayData();
        }

        public Models.ConflictLineState GetLineState(int line)
        {
            if (line >= 0 && line < _lineStates.Count)
                return _lineStates[line];
            return Models.ConflictLineState.Normal;
        }

        public void Resolve(object param)
        {
            if (_selectedChunk == null)
                return;

            var region = _conflictRegions[_selectedChunk.ConflictIndex];
            if (param is not Models.ConflictResolution resolution)
                return;

            // Try to resolve a resolved region.
            if (resolution != Models.ConflictResolution.None && region.IsResolved)
                return;

            // Try to undo an unresolved region.
            if (resolution == Models.ConflictResolution.None && !region.IsResolved)
                return;

            region.IsResolved = resolution != Models.ConflictResolution.None;
            region.ResolutionType = resolution;
            RefreshDisplayData();
        }

        public async Task<bool> SaveAndStageAsync()
        {
            if (_conflictRegions.Count == 0)
                return true;

            if (_unsolvedCount > 0)
            {
                Error = "Cannot save: there are still unresolved conflicts.";
                return false;
            }

            var lines = _originalContent.Split('\n', StringSplitOptions.None);
            var builder = new StringBuilder();
            var lastLineIdx = 0;

            foreach (var r in _conflictRegions)
            {
                for (var i = lastLineIdx; i < r.StartLineInOriginal; i++)
                    builder.Append(lines[i]).Append('\n');

                if (r.ResolutionType == Models.ConflictResolution.UseOurs)
                {
                    foreach (var l in r.OursContent)
                        builder.Append(l).Append('\n');
                }
                else if (r.ResolutionType == Models.ConflictResolution.UseTheirs)
                {
                    foreach (var l in r.TheirsContent)
                        builder.Append(l).Append('\n');
                }
                else if (r.ResolutionType == Models.ConflictResolution.UseBothMineFirst)
                {
                    foreach (var l in r.OursContent)
                        builder.Append(l).Append('\n');

                    foreach (var l in r.TheirsContent)
                        builder.Append(l).Append('\n');
                }
                else if (r.ResolutionType == Models.ConflictResolution.UseBothTheirsFirst)
                {
                    foreach (var l in r.TheirsContent)
                        builder.Append(l).Append('\n');

                    foreach (var l in r.OursContent)
                        builder.Append(l).Append('\n');
                }

                lastLineIdx = r.EndLineInOriginal + 1;
            }

            for (var j = lastLineIdx; j < lines.Length; j++)
                builder.Append(lines[j]).Append('\n');

            if (builder.Length > 1)
                builder.Length--; // Remove extra '\n'

            try
            {
                // Write merged content to the working file atomically (write a sibling
                // temp file, then replace), preserving the original encoding, so a failed
                // write can never leave the real file truncated with markers removed.
                var fullPath = Path.Combine(_repo.FullPath, _filePath);
                var tmpPath = $"{fullPath}.{Guid.NewGuid():N}.sourcegit-merge.tmp";
                try
                {
                    await File.WriteAllTextAsync(tmpPath, builder.ToString(), _outputEncoding);
                    File.Move(tmpPath, fullPath, true);
                }
                catch
                {
                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                    throw;
                }

                // Stage the file
                var pathSpecFile = Path.GetTempFileName();
                await File.WriteAllTextAsync(pathSpecFile, _filePath);
                await new Commands.Add(_repo.FullPath, pathSpecFile).ExecAsync();
                File.Delete(pathSpecFile);

                _repo.MarkWorkingCopyDirtyManually();
                return true;
            }
            catch (Exception ex)
            {
                Error = $"Failed to save and stage: {ex.Message}";
                return false;
            }
        }

        public void ClearErrorMessage()
        {
            Error = string.Empty;
        }

        // Decodes bytes while remembering the encoding to write back with. A BOM-marked
        // UTF-8/UTF-16 file keeps its encoding and BOM; an unmarked file must be valid
        // UTF-8 (otherwise the caller refuses it) and is written back as UTF-8 without a
        // BOM, matching the previous behaviour for ordinary UTF-8 files.
        private static bool TryDecodePreservingEncoding(byte[] bytes, out string text, out Encoding encoding)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encoding = new UTF8Encoding(true);
                text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
                return true;
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = new UnicodeEncoding(false, true);
                text = new UnicodeEncoding(false, false).GetString(bytes, 2, bytes.Length - 2);
                return true;
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = new UnicodeEncoding(true, true);
                text = new UnicodeEncoding(true, false).GetString(bytes, 2, bytes.Length - 2);
                return true;
            }

            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
                encoding = new UTF8Encoding(false);
                return true;
            }
            catch (DecoderFallbackException)
            {
                text = string.Empty;
                encoding = new UTF8Encoding(false);
                return false;
            }
        }

        private void ParseOriginalContent(string content)
        {
            _originalContent = content;
            _conflictRegions.Clear();

            if (string.IsNullOrEmpty(content))
                return;

            var lines = content.Split('\n', StringSplitOptions.None);
            var oursLines = new List<Models.ConflictLine>();
            var theirsLines = new List<Models.ConflictLine>();
            int oursLineNumber = 1;
            int theirsLineNumber = 1;
            int i = 0;

            while (i < lines.Length)
            {
                var line = lines[i];

                if (line.StartsWith("<<<<<<<", StringComparison.Ordinal))
                {
                    var region = new Models.ConflictRegion
                    {
                        StartLineInOriginal = i,
                        StartMarker = line,
                    };

                    oursLines.Add(new());
                    theirsLines.Add(new());
                    i++;

                    // Collect ours content
                    while (i < lines.Length &&
                           !lines[i].StartsWith("|||||||", StringComparison.Ordinal) &&
                           !lines[i].StartsWith("=======", StringComparison.Ordinal))
                    {
                        line = lines[i];
                        region.OursContent.Add(line);
                        oursLines.Add(new(Models.ConflictLineType.Ours, line, oursLineNumber++));
                        theirsLines.Add(new());
                        i++;
                    }

                    // Skip diff3 base section if present
                    if (i < lines.Length && lines[i].StartsWith("|||||||", StringComparison.Ordinal))
                    {
                        i++;
                        while (i < lines.Length && !lines[i].StartsWith("=======", StringComparison.Ordinal))
                            i++;
                    }

                    // Capture separator marker
                    if (i < lines.Length && lines[i].StartsWith("=======", StringComparison.Ordinal))
                    {
                        oursLines.Add(new());
                        theirsLines.Add(new());
                        region.SeparatorMarker = lines[i];
                        i++;
                    }

                    // Collect theirs content
                    while (i < lines.Length && !lines[i].StartsWith(">>>>>>>", StringComparison.Ordinal))
                    {
                        line = lines[i];
                        region.TheirsContent.Add(line);
                        oursLines.Add(new());
                        theirsLines.Add(new(Models.ConflictLineType.Theirs, line, theirsLineNumber++));
                        i++;
                    }

                    // Capture end marker (e.g., ">>>>>>> feature-branch")
                    if (i < lines.Length && lines[i].StartsWith(">>>>>>>", StringComparison.Ordinal))
                    {
                        oursLines.Add(new());
                        theirsLines.Add(new());

                        region.EndMarker = lines[i];
                        region.EndLineInOriginal = i;
                        i++;
                    }

                    _conflictRegions.Add(region);
                }
                else
                {
                    oursLines.Add(new(Models.ConflictLineType.Common, line, oursLineNumber));
                    theirsLines.Add(new(Models.ConflictLineType.Common, line, theirsLineNumber));
                    i++;
                    oursLineNumber++;
                    theirsLineNumber++;
                }
            }

            MaxLineNumber = Math.Max(oursLineNumber, theirsLineNumber);
            OursLines = oursLines;
            TheirsLines = theirsLines;
        }

        private void RefreshDisplayData()
        {
            var resultLines = new List<Models.ConflictLine>();
            _lineStates.Clear();

            if (_oursLines == null || _oursLines.Count == 0)
            {
                ResultLines = resultLines;
                return;
            }

            int resultLineNumber = 1;
            int currentLine = 0;
            int conflictIdx = 0;

            while (currentLine < _oursLines.Count)
            {
                // Check if we're at a conflict region
                Models.ConflictRegion currentRegion = null;
                if (conflictIdx < _conflictRegions.Count)
                {
                    var region = _conflictRegions[conflictIdx];
                    if (region.StartLineInOriginal == currentLine)
                        currentRegion = region;
                }

                if (currentRegion != null)
                {
                    int regionLines = currentRegion.EndLineInOriginal - currentRegion.StartLineInOriginal + 1;
                    if (currentRegion.IsResolved)
                    {
                        var oldLineCount = resultLines.Count;
                        var resolveType = currentRegion.ResolutionType;

                        // Resolved - show resolved content with color based on resolution type
                        if (resolveType == Models.ConflictResolution.UseBothMineFirst)
                        {
                            int mineCount = currentRegion.OursContent.Count;
                            for (int i = 0; i < mineCount; i++)
                            {
                                resultLines.Add(new(Models.ConflictLineType.Ours, currentRegion.OursContent[i], resultLineNumber));
                                resultLineNumber++;
                            }

                            int theirsCount = currentRegion.TheirsContent.Count;
                            for (int i = 0; i < theirsCount; i++)
                            {
                                resultLines.Add(new(Models.ConflictLineType.Theirs, currentRegion.TheirsContent[i], resultLineNumber));
                                resultLineNumber++;
                            }
                        }
                        else if (resolveType == Models.ConflictResolution.UseBothTheirsFirst)
                        {
                            int theirsCount = currentRegion.TheirsContent.Count;
                            for (int i = 0; i < theirsCount; i++)
                            {
                                resultLines.Add(new(Models.ConflictLineType.Theirs, currentRegion.TheirsContent[i], resultLineNumber));
                                resultLineNumber++;
                            }

                            int mineCount = currentRegion.OursContent.Count;
                            for (int i = 0; i < mineCount; i++)
                            {
                                resultLines.Add(new(Models.ConflictLineType.Ours, currentRegion.OursContent[i], resultLineNumber));
                                resultLineNumber++;
                            }
                        }
                        else if (resolveType == Models.ConflictResolution.UseOurs)
                        {
                            int mineCount = currentRegion.OursContent.Count;
                            for (int i = 0; i < mineCount; i++)
                            {
                                resultLines.Add(new(Models.ConflictLineType.Ours, currentRegion.OursContent[i], resultLineNumber));
                                resultLineNumber++;
                            }
                        }
                        else if (resolveType == Models.ConflictResolution.UseTheirs)
                        {
                            int theirsCount = currentRegion.TheirsContent.Count;
                            for (int i = 0; i < theirsCount; i++)
                            {
                                resultLines.Add(new(Models.ConflictLineType.Theirs, currentRegion.TheirsContent[i], resultLineNumber));
                                resultLineNumber++;
                            }
                        }

                        // Pad with empty lines to match Mine/Theirs panel height
                        int added = resultLines.Count - oldLineCount;
                        int padding = regionLines - added;
                        for (int p = 0; p < padding; p++)
                            resultLines.Add(new());

                        int blockSize = resultLines.Count - oldLineCount - 2;
                        _lineStates.Add(Models.ConflictLineState.ResolvedBlockStart);
                        for (var i = 0; i < blockSize; i++)
                            _lineStates.Add(Models.ConflictLineState.ResolvedBlock);
                        _lineStates.Add(Models.ConflictLineState.ResolvedBlockEnd);
                    }
                    else
                    {
                        resultLines.Add(new(Models.ConflictLineType.Marker, currentRegion.StartMarker));
                        _lineStates.Add(Models.ConflictLineState.ConflictBlockStart);

                        foreach (var line in currentRegion.OursContent)
                        {
                            resultLines.Add(new(Models.ConflictLineType.Ours, line, resultLineNumber++));
                            _lineStates.Add(Models.ConflictLineState.ConflictBlock);
                        }

                        resultLines.Add(new(Models.ConflictLineType.Marker, currentRegion.SeparatorMarker));
                        _lineStates.Add(Models.ConflictLineState.ConflictBlock);

                        foreach (var line in currentRegion.TheirsContent)
                        {
                            resultLines.Add(new(Models.ConflictLineType.Theirs, line, resultLineNumber++));
                            _lineStates.Add(Models.ConflictLineState.ConflictBlock);
                        }

                        resultLines.Add(new(Models.ConflictLineType.Marker, currentRegion.EndMarker));
                        _lineStates.Add(Models.ConflictLineState.ConflictBlockEnd);
                    }

                    currentLine = currentRegion.EndLineInOriginal + 1;
                    conflictIdx++;
                }
                else
                {
                    var oursLine = _oursLines[currentLine];
                    resultLines.Add(new(oursLine.Type, oursLine.Content, resultLineNumber));
                    _lineStates.Add(Models.ConflictLineState.Normal);
                    resultLineNumber++;
                    currentLine++;
                }
            }

            SelectedChunk = null;
            ResultLines = resultLines;

            var unsolved = new List<int>();
            for (var i = 0; i < _conflictRegions.Count; i++)
            {
                var r = _conflictRegions[i];
                if (!r.IsResolved)
                    unsolved.Add(i);
            }

            UnsolvedCount = unsolved.Count;
        }

        private readonly Repository _repo;
        private readonly string _filePath;
        private string _originalContent = string.Empty;
        private int _unsolvedCount = 0;
        private int _maxLineNumber = 0;
        private List<Models.ConflictLine> _oursLines = [];
        private List<Models.ConflictLine> _theirsLines = [];
        private List<Models.ConflictLine> _resultLines = [];
        private List<Models.ConflictRegion> _conflictRegions = [];
        private List<Models.ConflictLineState> _lineStates = [];
        private Vector _scrollOffset = Vector.Zero;
        private Models.ConflictSelectedChunk _selectedChunk;
        private string _error = string.Empty;
        private Encoding _outputEncoding = new UTF8Encoding(false);
    }
}
