﻿// Created by: egr
// Created at: 04.08.2015
// © 2012-2015 Alexander Egorov

using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using logviewer.logic;
using logviewer.logic.ui;
using MenuItem = Fluent.MenuItem;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace logviewer.ui
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly UiController controller;
        private readonly FileSystemWatcher logWatch;

        public MainWindow()
        {
            this.InitializeComponent();
            MainViewModel.Current.Window = new WindowWrapper(this);
            this.controller = new UiController(MainViewModel.Current);
            this.logWatch = new FileSystemWatcher();
            this.logWatch.Changed += this.OnChangeLog;
        }

        private void WatchLogFile(string path)
        {
            this.logWatch.Path = Path.GetDirectoryName(path);
            this.logWatch.Filter = Path.GetFileName(path);
        }

        private void OnExitApp(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OnTemplateSelect(object sender, RoutedEventArgs e)
        {
            var src = (MenuItem)e.OriginalSource;
            var cmd = (TemplateCommand)src.DataContext;

            ReloadTemplates(cmd.Index);
        }

        private static void ReloadTemplates(int selected)
        {
            MainViewModel.Current.Templates.Clear();

            MainViewModel.Current.SelectedParsingTemplate = selected;
            var commands = MainViewModel.Current.CreateTemplateCommands();
            foreach (var command in commands)
            {
                MainViewModel.Current.Templates.Add(command);
            }
        }

        private void OnOpenFile(object sender, ExecutedRoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Log files|*.log|All files|*.*"
            };
            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }
            MainViewModel.Current.LogPath = openFileDialog.FileName;
            this.controller.ReadNewLog();
            this.WatchLogFile(MainViewModel.Current.LogPath);
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            this.logWatch.Dispose();
            this.controller.Dispose();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            this.controller.LoadLastOpenedFile();
            this.WatchLogFile(MainViewModel.Current.LogPath);
        }

        private void OnUpdate(object sender, ExecutedRoutedEventArgs e)
        {
            this.controller.ReadNewLog();
        }

        private void OnChangeLog(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Changed)
            {
                this.controller.UpdateLog(e.FullPath);
            }
        }

        private void OnStatistic(object sender, ExecutedRoutedEventArgs e)
        {
            var dlg = new StatisticDlg(this.controller.Store, this.controller.GetLogSize(true), this.controller.CurrentEncoding);
            dlg.Show(MainViewModel.Current.Window);
        }

        private void OnSettings(object sender, ExecutedRoutedEventArgs e)
        {
            var dlg = new SettingsDlg(MainViewModel.Current.SettingsProvider);
            using (dlg)
            {
                dlg.SetApplyAction(refresh => this.controller.UpdateSettings(refresh));
                dlg.ShowDialog();
            }
            ReloadTemplates(MainViewModel.Current.SelectedParsingTemplate);
        }

        private void ScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var panel = FindVisualChild<VirtualizingStackPanel>(this.LogControlStyle);
            if (this.LogControlStyle.Items.Count <= 0 || panel == null)
            {
                return;
            }
            var offset = panel.Orientation == Orientation.Horizontal ? (int)panel.HorizontalOffset : (int)panel.VerticalOffset;
            var limit = panel.Orientation == Orientation.Horizontal ? (int)panel.ViewportWidth : (int)panel.ViewportHeight;
            MainViewModel.Current.Visible = new Range
            {
                First = offset,
                Last = offset + limit
            };
        }

        private static T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);

                var visualChild = child as T;
                if (visualChild != null)
                {
                    return visualChild;
                }
                child = FindVisualChild<T>(child);
                if (child != null)
                {
                    return (T)child;
                }
            }
            return null;
        }

        private void OnCheckUpdates(object sender, ExecutedRoutedEventArgs e)
        {
            this.controller.CheckUpdates(true);
        }

        private void OnHelp(object sender, ExecutedRoutedEventArgs e)
        {
            var dlg = new AboutDlg();
            using (dlg)
            {
                dlg.ShowDialog();
            }
        }

        private void OnNetworkSettings(object sender, ExecutedRoutedEventArgs e)
        {
            var dlg = new NetworkSettingsDlg();
            using (dlg)
            {
                dlg.ShowDialog();
            }
        }
    }
}