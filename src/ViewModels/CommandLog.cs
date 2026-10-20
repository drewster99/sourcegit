using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class CommandLog : ObservableObject, Models.ICommandLog
    {
        public string Name
        {
            get;
            private set;
        }

        public DateTime StartTime
        {
            get;
        } = DateTime.Now;

        public DateTime EndTime
        {
            get;
            private set;
        } = DateTime.Now;

        public bool IsComplete
        {
            get;
            private set;
        } = false;

        public string Content
        {
            get
            {
                return IsComplete ? _content : _builder.ToString();
            }
        }

        public CommandLog(string name)
        {
            Name = name;
        }

        public void Subscribe(Models.ICommandLogReceiver receiver)
        {
            _receivers.Add(receiver);
        }

        public void Unsubscribe(Models.ICommandLogReceiver receiver)
        {
            _receivers.Remove(receiver);
        }

        public void AppendLine(string line = null)
        {
            // Called from the git stdout/stderr pump threads, potentially thousands of
            // times per command. Buffer the lines and flush them to the UI thread in
            // batches via a single Post, instead of a blocking Invoke per line (which
            // round-trips through — and stalls — the pump thread and floods the UI).
            var newline = line ?? string.Empty;
            bool scheduleFlush;
            lock (_pendingLock)
            {
                _pending.Add(newline);
                scheduleFlush = !_flushScheduled;
                _flushScheduled = true;
            }

            if (scheduleFlush)
                Dispatcher.UIThread.Post(Flush);
        }

        public void Complete()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Invoke(Complete);
                return;
            }

            Flush();

            IsComplete = true;
            EndTime = DateTime.Now;

            _content = _builder.ToString();
            _builder.Clear();
            _receivers.Clear();
            _builder = null;

            OnPropertyChanged(nameof(IsComplete));
        }

        // Runs on the UI thread (via Post, or synchronously from Complete) and drains the
        // buffered lines into the log and its receivers.
        private void Flush()
        {
            List<string> batch;
            lock (_pendingLock)
            {
                _flushScheduled = false;
                if (_pending.Count == 0)
                    return;

                batch = _pending;
                _pending = new List<string>();
            }

            foreach (var newline in batch)
            {
                if (_builder != null)
                    _builder.AppendLine(newline);
                else
                    _content = $"{_content}{newline}\n";

                foreach (var receiver in _receivers)
                    receiver.OnReceiveCommandLog(newline);
            }
        }

        private string _content = string.Empty;
        private StringBuilder _builder = new StringBuilder();
        private List<Models.ICommandLogReceiver> _receivers = new List<Models.ICommandLogReceiver>();
        private List<string> _pending = new List<string>();
        private readonly object _pendingLock = new object();
        private bool _flushScheduled = false;
    }
}
