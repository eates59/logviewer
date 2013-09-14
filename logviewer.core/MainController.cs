using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using logviewer.core.Properties;
using logviewer.rtf.Rtf;
using logviewer.rtf.Rtf.Contents;
using logviewer.rtf.Rtf.Contents.Paragraphs;
using logviewer.rtf.Rtf.Formatting;
using logviewer.rtf.Rtf.Header;
using NLog.Targets;

namespace logviewer.core
{
    public sealed class MainController : IDisposable
    {
        #region Constants and Fields

        private const int DefaultPageSize = 10000;

        private readonly Dictionary<LogLevel, int> byLevel = new Dictionary<LogLevel, int>();

        private readonly List<Regex> markers;
        private readonly Regex messageHead;
        private int pageSize;

        private readonly List<string> recentFiles = new List<string>();
        private readonly string recentFilesFilePath;
        private CancellationTokenSource cancellation = new CancellationTokenSource();

        private string currentPath;

        private LogLevel maxFilter = LogLevel.Fatal;
        private LogLevel minFilter = LogLevel.Trace;
        private bool reverseChronological;
        private bool useRegexp = true;
        private LogStore store;
        private string textFilter;
        private ILogView view;
        private Task task;
        private int totalMessages;
        private readonly TaskScheduler uiContext;

        #endregion

        #region Constructors and Destructors

        public MainController(string startMessagePattern,
            string recentFilesFilePath,
            IEnumerable<string> levels,
            int pageSize = DefaultPageSize)
        {
            this.CurrentPage = 1;
            this.recentFilesFilePath = recentFilesFilePath;
            this.pageSize = pageSize <= 0 ? DefaultPageSize : pageSize;
            this.markers = new List<Regex>();
            this.uiContext = TaskScheduler.FromCurrentSynchronizationContext();
            this.markers.AddRange(levels.Select(level => level.ToMarker()));
            this.messageHead = new Regex(startMessagePattern, RegexOptions.Compiled);
            SQLiteFunction.RegisterFunction(typeof (SqliteRegEx));
            SQLiteFunction.RegisterFunction(typeof (Substring));
        }

        #endregion

        #region Public Properties

        public long MessagesCount
        {
            get
            {
                var start = (this.CurrentPage - 1) * this.pageSize;
                return this.totalFiltered - start;
            }
        }

        public int CurrentPage { get; set; }

        public int TotalPages
        {
            get
            {
                if (this.totalFiltered == 0)
                {
                    return 1;
                }
                return (int) Math.Ceiling(this.totalFiltered / (float) this.pageSize);
            }
        }

        public long DisplayedMessages
        {
            get
            {
                var finish = Math.Min(this.MessagesCount, this.pageSize);
                return Math.Min(finish, this.totalFiltered);
            }
        }

        #endregion

        #region Public Methods and Operators

        private bool NotCancelled
        {
            get { return !this.cancellation.IsCancellationRequested; }
        }

        public void InitializeLogger()
        {
            var target = new RichTextBoxTarget
            {
                Layout =
                    @"${date:format=yyyy-MM-dd HH\:mm\:ss,fff} ${level:upperCase=True} ${logger} ${message}${newline}${onexception:Process\: ${processname}${newline}Process time\: ${processtime}${newline}Process ID\: ${processid}${newline}Thread ID\: ${threadid}${newline}Details\:${newline}${exception:format=ToString}}",
                ControlName = "syntaxRichTextBox1",
                FormName = "MainDlg",
                UseDefaultRowColoringRules = false
            };
            target.RowColoringRules.Add(new RichTextBoxRowColoringRule("level == LogLevel.Warn", "Orange", "White",
                FontStyle.Regular));
            target.RowColoringRules.Add(new RichTextBoxRowColoringRule("level == LogLevel.Error", "Red", "White",
                FontStyle.Regular));
            target.RowColoringRules.Add(new RichTextBoxRowColoringRule("level == LogLevel.Fatal", "DarkViolet", "White",
                FontStyle.Regular));
            NLog.Config.SimpleConfigurator.ConfigureForTargetLogging(target, NLog.LogLevel.Warn);
        }

        public void LoadLog(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            this.view.LoadLog(path);
            this.view.SetCurrentPage(this.CurrentPage);
            this.view.DisableBack(this.CurrentPage <= 1);
            this.view.DisableForward(this.CurrentPage >= this.TotalPages);
        }

        private void CancelPreviousTask()
        {
            if (this.task == null || this.task.Status != TaskStatus.Running)
            {
                return;
            }
            if (this.cancellation.IsCancellationRequested)
            {
                this.task.Wait();
            }
            else
            {
                this.cancellation.Cancel();
                this.task.Wait();
                this.cancellation.Dispose();
                this.cancellation = new CancellationTokenSource();
            }
        }

        public void BeginLogReading(int min, int max, string filter, bool reverse, bool regexp)
        {
            this.CancelPreviousTask();
            this.MinFilter(min);
            this.MaxFilter(max);
            this.TextFilter(filter);
            this.UserRegexp(regexp);
            this.Ordering(reverse);
            this.view.SetProgress(0);

            var path = string.Empty;
            Func<string> function = delegate
            {
                try
                {
                    return path = this.ReadLog();
                }
                catch (Exception e)
                {
                    Log.Instance.Error(e.Message, e);
                    throw;
                }
            };
            this.task = Task.Factory.StartNew(function, this.cancellation.Token);
            this.task.ContinueWith(t => this.EndLogReading(path), uiContext);
        }

        private void EndLogReading(string path)
        {
            this.view.LogInfo = string.Format(this.view.LogInfoFormatString, this.DisplayedMessages,
                this.totalMessages, this.CountMessages(LogLevel.Trace), this.CountMessages(LogLevel.Debug),
                this.CountMessages(LogLevel.Info), this.CountMessages(LogLevel.Warn), this.CountMessages(LogLevel.Error),
                this.CountMessages(LogLevel.Fatal), this.totalFiltered);
            this.LoadLog(path);
            this.view.SetLoadedFileCapltion(this.view.LogPath);
            this.ReadRecentFiles();
            this.view.FocusOnTextFilterControl();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            Settings.LastOpenedFile = this.view.LogPath;
            this.view.SetProgress(100);
        }

        /// <summary>
        /// Reads log from file
        /// </summary>
        /// <returns>Path to RTF document to be loaded into control</returns>
        public string ReadLog()
        {
            if (this.minFilter > this.maxFilter && this.maxFilter >= LogLevel.Trace)
            {
                throw new ArgumentException(Resources.MinLevelGreaterThenMax);
            }
            if (!File.Exists(this.view.LogPath))
            {
                return string.Empty;
            }
            var reader = new LogReader(this.view.LogPath, messageHead);

            this.logSize = reader.Length;
            
            Task.Factory.StartNew(this.SetLogSize, CancellationToken.None, TaskCreationOptions.None, this.uiContext);

            if (this.logSize == 0)
            {
                return string.Empty;
            }

            if (this.CurrentPathCached)
            {
                return this.CreateRtf(true);
            }
            Task.Factory.StartNew(this.ResetLogStatistic, CancellationToken.None, TaskCreationOptions.None, this.uiContext);
            this.currentPath = reader.LogPath;


            var dbSize = this.logSize + (this.logSize / 10) * 4; // +40% to log file
            if (this.store != null)
            {
                this.store.Dispose();
            }
            this.store = new LogStore(dbSize);
            GC.Collect();
            this.store.StartAddMessages();
            this.totalMessages = 0;

            try
            {
                reader.ProgressChanged += OnReadLogProgressChanged;
                reader.Read(this.AddMessageToCache, () => this.NotCancelled);
            }
            finally
            {
                reader.ProgressChanged -= OnReadLogProgressChanged;
                this.store.FinishAddMessages();
            }
            return this.CreateRtf();
        }

        private void OnReadLogProgressChanged(object sender, System.ComponentModel.ProgressChangedEventArgs e)
        {
            this.OnLogReadProgress(e.ProgressPercentage);
        }

        private void SetLogSize()
        {
            this.view.HumanReadableLogSize = new FileSize((ulong)this.logSize).ToString();
        }

        private void ResetLogStatistic()
        {
            this.view.LogInfo = string.Format(this.view.LogInfoFormatString, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        public void CancelReading()
        {
            if (this.cancellation.IsCancellationRequested)
            {
                return;
            }
            this.cancellation.Cancel();
        }

        public void ClearCache()
        {
            this.currentPath = null;
        }

        public void MinFilter(int value)
        {
            this.minFilter = (LogLevel) value;
        }

        public void MaxFilter(int value)
        {
            this.maxFilter = (LogLevel) value;
        }

        public void PageSize(int value)
        {
            this.pageSize = value;
            this.SetPageSize();
        }

        public void TextFilter(string value)
        {
            this.textFilter = string.IsNullOrWhiteSpace(value)
                ? null
                : value;
        }

        public void UserRegexp(bool enabled)
        {
            this.useRegexp = enabled;
        }

        private void Ordering(bool reverse)
        {
            this.reverseChronological = reverse;
        }

        public void LoadLastOpenedFile()
        {
            if (Settings.OpenLastFile && !string.IsNullOrWhiteSpace(Settings.LastOpenedFile))
            {
                this.view.StartLoadingLog(Settings.LastOpenedFile);
            }
        }

        public void ReadRecentFiles()
        {
            if (!File.Exists(this.recentFilesFilePath))
            {
                using (File.Open(this.recentFilesFilePath, FileMode.Create))
                {
                }
            }
            var files = File.ReadAllLines(this.recentFilesFilePath);
            this.recentFiles.Clear();
            this.view.ClearRecentFilesList();

            this.recentFiles.AddRange(files);
            this.recentFiles.Reverse();

            try
            {
                foreach (
                    var item in
                        from file in this.recentFiles
                        where !string.IsNullOrWhiteSpace(file) && File.Exists(file)
                        select file)
                {
                    this.view.CreateRecentFileItem(item);
                }
            }
            catch (Exception e)
            {
                Log.Instance.Error(e.Message, e);
            }
        }

        public void SaveRecentFiles()
        {
            this.recentFiles.Reverse();
            if (!this.recentFiles.Contains(this.view.LogPath))
            {
                this.recentFiles.Add(this.view.LogPath);
            }
            else
            {
                this.recentFiles.Remove(this.view.LogPath);
                this.recentFiles.Add(this.view.LogPath);
            }
            File.WriteAllLines(this.recentFilesFilePath, this.recentFiles);
        }

        public void OpenLogFile()
        {
            if (!this.view.OpenLogFile())
            {
                return;
            }
            this.ClearCache();
            this.CurrentPage = 1;
            this.view.LogPath = this.view.LogFileName;
            this.view.ReadLog();
        }

        public void ExportToRtf()
        {
            var path = Path.GetFileNameWithoutExtension(this.view.LogPath) + ".rtf";
            if (this.view.OpenExport(path))
            {
                this.view.SaveRtf();
            }
        }

        private int CountMessages(LogLevel level)
        {
            return this.byLevel.ContainsKey(level) ? this.byLevel[level] : 0;
        }

        public void SetView(ILogView logView)
        {
            this.view = logView;
            this.view.Initialize();
        }

        public void SetPageSize()
        {
            this.view.SetPageSize(this.pageSize);
        }

        #endregion

        #region Methods

        private long totalFiltered;
        private long logSize;

        private bool CurrentPathCached
        {
            get
            {
                return !string.IsNullOrWhiteSpace(this.currentPath) &&
                       this.currentPath.Equals(this.view.LogPath, StringComparison.CurrentCultureIgnoreCase);
            }
        }

        private void AddMessageToCache(LogMessage message)
        {
            if (message.IsEmpty)
            {
                return;
            }
            message.Level = this.DetectLevel(message.Header);
            message.Cache();
            ++this.totalMessages;
            this.store.AddMessage(message);
        }

        private string CreateRtf(bool signalProgress = false)
        {
            this.byLevel.Clear();
            var rtf = new RtfDocument();
            rtf.FontTable.Add(new RtfFont("Courier New"));
            for (var i = (int)LogLevel.Trace; i <= (int)LogLevel.Fatal; i++)
            {
                rtf.ColorTable.Add(new RtfColor(LogMessage.Colorize((LogLevel)i)));
            }

            this.totalFiltered = this.store.CountMessages(this.minFilter, this.maxFilter, this.textFilter,
                this.useRegexp);

            if (this.CurrentPage > this.TotalPages || this.CurrentPage <= 0)
            {
                this.CurrentPage = 1;
            }

            var total = DisplayedMessages;
            var fraction = total / 20L;
            var signalCounter = 1;
            var count = 0;

            Action<LogMessage> onRead = delegate(LogMessage m)
            {
                this.AddMessage(m, rtf);
                ++count;
                if (!signalProgress || count < signalCounter * fraction)
                {
                    return;
                }
                ++signalCounter;
                var percent = (count / (double) total) * 100;
                this.OnLogReadProgress(percent);
            };

            var start = (this.CurrentPage - 1) * this.pageSize;
            this.store.ReadMessages(
                this.pageSize,
                onRead,
                () => this.NotCancelled,
                start,
                this.reverseChronological,
                this.minFilter,
                this.maxFilter,
                this.textFilter,
                this.useRegexp);
            var rtfWriter = new RtfWriter();
            var sb = new StringBuilder();
            TextWriter tw = new StringWriter(sb);
            using (tw)
            {
                rtfWriter.Write(tw, rtf);
            }
            return sb.ToString();
        }

        private void OnLogReadProgress(double percent)
        {
            Task.Factory.StartNew(() => this.view.SetProgress(percent), CancellationToken.None,
                TaskCreationOptions.None, this.uiContext);
        }

        private void AddMessage(LogMessage message, RtfDocument rtf)
        {
            if (this.byLevel.ContainsKey(message.Level))
            {
                this.byLevel[message.Level] = this.byLevel[message.Level] + 1;
            }
            else
            {
                this.byLevel.Add(message.Level, 1);
            }
            var header = new RtfFormattedParagraph(new RtfParagraphFormatting(10, RtfTextAlign.Left));
            header.AppendText(message.HeadFormat);
            rtf.Contents.Add(header);

            var body = new RtfFormattedParagraph(new RtfParagraphFormatting(10, RtfTextAlign.Left));
            body.AppendText(message.BodyFormat);
            body.AppendText(new RtfLineBreak());
            rtf.Contents.Add(body);
        }

        private LogLevel DetectLevel(string line, LogLevel defaultLevel = LogLevel.Trace)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return defaultLevel;
            }
            for (var i = 0; i < this.markers.Count; i++)
            {
                if (this.markers[i].IsMatch(line))
                {
                    return (LogLevel) i;
                }
            }

            return defaultLevel;
        }

        #endregion

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    if (!this.cancellation.IsCancellationRequested)
                    {
                        this.cancellation.Cancel();
                    }

                    if (this.task != null)
                    {
                        this.task.Wait();
                        this.task.Dispose();
                    }
                    this.cancellation.Dispose();
                }
                catch (Exception e)
                {
                    this.task = null;
                    this.cancellation = null;
                    Log.Instance.Fatal(e.Message, e);
                }
                finally
                {
                    if (this.store != null)
                    {
                        this.store.Dispose();
                    }
                }
            }
        }
    }
}