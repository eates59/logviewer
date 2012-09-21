﻿using System;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Text;
using System.Windows.Forms;
using logviewer.Properties;
using logviewer.core;

namespace logviewer
{
    public partial class MainDlg : Form, ILogView
    {
        private readonly LogController controller;

        private readonly string[] levels = new[]
            {
                ConfigurationManager.AppSettings["TraceMarker"],
                ConfigurationManager.AppSettings["DebugMarker"],
                ConfigurationManager.AppSettings["InfoMarker"],
                ConfigurationManager.AppSettings["WarnMarker"],
                ConfigurationManager.AppSettings["ErrorMarker"],
                ConfigurationManager.AppSettings["FatalMarker"]
            };

        private string logFilterMax;
        private string logFilterMin;
        private string logFilterText;
        private string originalCapion;
        private bool reverse;
        private bool textFilterChanging;

        public MainDlg()
        {
            this.InitializeComponent();
            this.controller = new LogController(this, ConfigurationManager.AppSettings["StartMessagePattern"], Path.Combine(Path.GetTempPath(), "logviewerRecentFiles.txt"));
            this.controller.DefineTraceMarker(this.levels[0]);
            this.controller.DefineDebugMarker(this.levels[1]);
            this.controller.DefineInfoMarker(this.levels[2]);
            this.controller.DefineWarnMarker(this.levels[3]);
            this.controller.DefineErrorMarker(this.levels[4]);
            this.controller.DefineFatalMarker(this.levels[5]);
            this.KeepOriginalCaption();
            this.toolStripComboBox1.SelectedIndex = 0;
            this.toolStripComboBox2.SelectedIndex = this.toolStripComboBox2.Items.Count - 1;
            this.toolStripComboBox3.SelectedIndex = 0;
            this.EnableControls(false);
            this.controller.ReadRecentFiles();
        }

        #region ILogView Members

        public string LogPath { get; private set; }

        public void CreateRecentFileItem(string file)
        {
            var result = new ToolStripMenuItem(file) { Tag = file };
            result.Click += this.OnOpenRecentLogFile;
            this.recentFilesToolStripMenuItem.DropDownItems.Add(result);
        }

        public void ClearRecentFilesList()
        {
            this.recentFilesToolStripMenuItem.DropDownItems.Clear();
        }

        #endregion

        private void EnableControls(bool enabled)
        {
            this.toolStripComboBox1.Enabled = enabled;
            this.toolStripComboBox2.Enabled = enabled;
            this.toolStripComboBox3.Enabled = enabled;
            this.toolStripButton2.Enabled = enabled;
            this.refreshToolStripMenuItem.Enabled = enabled;
            this.exportToolStripMenuItem.Enabled = enabled;
            this.toolStripTextBox1.Enabled = enabled;
        }

        private void OnOpenRecentLogFile(object sender, EventArgs e)
        {
            var item = (ToolStripMenuItem) sender;
            this.LogPath = item.Tag as string;
            this.ReadLog();
        }

        private void KeepOriginalCaption()
        {
            this.originalCapion = this.Text;
        }

        private void OnOpen(object sender, EventArgs e)
        {
            var r = this.openFileDialog1.ShowDialog();

            if (r != DialogResult.OK)
            {
                return;
            }
            this.LogPath = this.openFileDialog1.FileName;

            if (this.logReader.IsBusy)
            {
                return;
            }
            this.ReadLog();
        }

        private void ReadLog()
        {
            Executive.SafeRun(this.WatchLogFile);
            this.Text = string.Format("{0}: {1}", this.originalCapion, this.LogPath);
            this.StartReading();
            this.controller.SaveRecentFiles();
        }

        private void WatchLogFile()
        {
            this.logWatch.Path = Path.GetDirectoryName(this.LogPath);
            this.logWatch.Filter = Path.GetFileName(this.LogPath);
        }

        private void StartReading()
        {
            if (this.logReader.IsBusy)
            {
                return;
            }
            this.EnableControls(true);
            this.logFilterMin = this.ReadLevel(this.toolStripComboBox1.SelectedIndex);
            this.logFilterMax = this.ReadLevel(this.toolStripComboBox2.SelectedIndex);
            this.logFilterText = this.toolStripTextBox1.Text;
            this.reverse = this.toolStripComboBox3.SelectedIndex == 0;
            this.toolStripStatusLabel1.Text = Resources.ReadingLogMessage;
            this.syntaxRichTextBox1.Clear();
            this.toolStrip1.Focus();
            this.logReader.RunWorkerAsync(this.LogPath);
        }

        private string ReadLevel(int index)
        {
            return index < 0 || index > this.levels.Length - 1 ? null : this.levels[index];
        }

        private void ReadLog(object sender, DoWorkEventArgs e)
        {
            this.controller.MinFilter(this.logFilterMin);
            this.controller.MaxFilter(this.logFilterMax);
            this.controller.TextFilter(this.logFilterText);
            this.controller.Ordering(this.reverse);
            e.Result = this.controller.ReadLog();
        }

        private void ReadLogCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            this.toolStripStatusLabel1.Text = this.controller.HumanReadableLogSize;
            this.syntaxRichTextBox1.Rtf = e.Result as string;
            this.controller.ReadRecentFiles();
            if (this.textFilterChanging)
            {
                this.toolStripTextBox1.Focus();
            }
        }

        private void OnClose(object sender, EventArgs e)
        {
            this.CancelReading();
            this.Text = this.originalCapion;
            this.toolStripStatusLabel1.Text = null;
            this.syntaxRichTextBox1.Clear();
            this.EnableControls(false);
            this.logWatch.Filter = string.Empty;
        }

        private void OnExit(object sender, EventArgs e)
        {
            this.CancelReading();
            this.Close();
        }

        private void CancelReading()
        {
            if (!this.logReader.IsBusy || this.logReader.CancellationPending)
            {
                return;
            }
            this.controller.CancelReading();
            this.logReader.CancelAsync();
        }

        private void OnChangeFilter(object sender, EventArgs e)
        {
            this.textFilterChanging = false;
            this.StartReading();
        }

        private void OnAbout(object sender, EventArgs e)
        {
            var dlg = new AboutDlg();
            using (dlg)
            {
                dlg.ShowDialog();
            }
        }

        private void OnChangeLog(object sender, FileSystemEventArgs e)
        {
            this.controller.ClearCache();
            this.StartReading();
        }

        private void OnExport(object sender, EventArgs e)
        {
            this.exportDialog.FileName = Path.GetFileNameWithoutExtension(this.LogPath) + ".rtf";
            var r = this.exportDialog.ShowDialog();

            if (r != DialogResult.OK)
            {
                return;
            }
            File.WriteAllText(this.exportDialog.FileName, this.syntaxRichTextBox1.Rtf, Encoding.GetEncoding("windows-1251"));
        }

        private void OnChangeTextFilter(object sender, EventArgs e)
        {
            this.textFilterChanging = true;
            this.StartReading();
        }

        private void OnRefresh(object sender, EventArgs e)
        {
            this.controller.ClearCache();
            this.OnChangeFilter(sender, e);
        }
    }
}