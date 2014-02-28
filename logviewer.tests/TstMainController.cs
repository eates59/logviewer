﻿// Created by: egr
// Created at: 19.09.2012
// © 2012-2014 Alexander Egorov

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using logviewer.core;
using NMock;
using NMock.Matchers;
using NUnit.Framework;

namespace logviewer.tests
{
    [TestFixture]
    public class TstMainController
    {
        private const string f1 = "1";
        private const string f2 = "2";
        private const string f3 = "3";
        private const string SettingsDb = "test.db";
        private const int KeepLastNFiles = 2;

        #region Setup/Teardown

        [SetUp]
        public void Setup()
        {
            completed = false;
            CleanupTestFiles();
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            this.mockery = new MockFactory();
            this.view = this.mockery.CreateMock<ILogView>();
            this.settings = this.mockery.CreateMock<ISettingsProvider>();

            this.settings.Expects.One.GetProperty(_ => _.PageSize).WillReturn(100);

            var template = ParsingTemplate(Levels);
            this.settings.Expects.One.Method(_ => _.ReadParsingTemplate()).WillReturn(template);

            this.controller = new MainController(this.settings.MockObject);
            this.controller.ReadCompleted += this.OnReadCompleted;
            this.view.Expects.One.Method(_ => _.Initialize());
            this.controller.SetView(this.view.MockObject);
        }

        private static ParsingTemplate ParsingTemplate(IList<string> levels)
        {
            var result = new ParsingTemplate
            {
                Index = 0,
                StartMessage = MessageStart
            };
            for (var i = 0; i < levels.Count; i++)
            {
                result.UpdateLevelProperty(levels[i], (LogLevel)i);
            }
            return result;
        }

        [TearDown]
        public void Teardown()
        {
            Cleanup(TestPath, RecentPath, FullPathToTestDb);
            CleanupTestFiles();
            this.controller.Dispose();
        }

        private static string FullPathToTestDb
        {
            get { return Path.Combine(SqliteSettingsProvider.ApplicationFolder, SettingsDb); }
        }

        private static void CleanupTestFiles()
        {
            Cleanup(f1, f2, f3);
        }
        
        private static void Cleanup(params string[] files)
        {
            foreach (var file in files.Where(File.Exists))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }
        
        private static void CreateEmpty(params string[] files)
        {
            foreach (var file in files)
            {
                using (File.Open(file, FileMode.Create))
                {
                }
            }
        }

        private void WaitReadingComplete()
        {
            var result = SpinWait.SpinUntil(() => this.completed, TimeSpan.FromSeconds(2));
            if (!result)
            {
                throw new ExpectationException("Wait expired");
            }
        }

        #endregion

        internal static readonly string[] Levels =
        {
            "TRACE",
            "DEBUG",
            "INFO",
            "WARN",
            "ERROR",
            "FATAL"
        };

        private const string TestPath = "f";
        private const string RecentPath = "r";
        private MockFactory mockery;
        private Mock<ILogView> view;
        private Mock<ISettingsProvider> settings;
        private MainController controller;

        private const string MessageExamples =
            "2008-12-27 19:31:47,250 [4688] INFO \nmessage body 1\n2008-12-27 19:40:11,906 [5272] ERROR \nmessage body 2";

        internal const string MessageStart = @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}.*";

        [Test]
        public void AllFilters()
        {
            this.view.Expects.Exactly(2).Method(v => v.SetLogProgressCustomText(null)).WithAnyArguments();
            this.view.Expects.One.Method(v => v.SetLoadedFileCapltion(TestPath));
            this.view.Expects.AtLeastOne.GetProperty(v => v.LogPath).WillReturn(TestPath);
            File.WriteAllText(TestPath,
                MessageExamples + "\n2008-12-27 19:42:11,906 [5272] WARN \n2008-12-27 19:42:11,906 [5555] WARN ");
            this.controller.MinFilter((int)LogLevel.Warn);
            this.controller.MaxFilter((int)LogLevel.Warn);
            this.controller.TextFilter("5555");
            this.controller.UserRegexp(false);
            this.controller.StartReadLog();
            this.WaitReadingComplete();
            Assert.That(this.controller.MessagesCount, NUnit.Framework.Is.EqualTo(1));
        }

        [TestCase((int)LogLevel.Warn, (int)LogLevel.Warn, MessageExamples + "\n2008-12-27 19:42:11,906 [5272] WARN ", 1)]
        [TestCase((int)LogLevel.Warn, (int)LogLevel.Warn, MessageExamples, 0)]
        public void MaxAndMaxFilter(int min, int max, string msg, int count)
        {
            this.view.Expects.Exactly(2).Method(v => v.SetLogProgressCustomText(null)).WithAnyArguments();
            this.view.Expects.One.Method(v => v.SetLoadedFileCapltion(TestPath));
            this.view.Expects.AtLeastOne.GetProperty(v => v.LogPath).WillReturn(TestPath);
            File.WriteAllText(TestPath, msg);
            this.controller.MinFilter(min);
            this.controller.MaxFilter(max);
            this.controller.StartReadLog();
            this.WaitReadingComplete();
            Assert.That(this.controller.MessagesCount, NUnit.Framework.Is.EqualTo(count));
        }

        [TestCase(LogLevel.Warn, new[] { "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL" }, 1)]
        [TestCase(LogLevel.Info, new[] { "TRACE", "DEBUG", @"\[?INFO\]?", "WARN", "ERROR", "FATAL" }, 1)]
        [TestCase(LogLevel.Debug, new[] { "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL" }, 0)]
        [TestCase(LogLevel.Error, new[] { "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL" }, 2)]
        [TestCase(LogLevel.Trace, new[] { "TRACE", "DEBUG", @"\[?InFO\]?", "WARN", "ERROR", "FATAL" }, 1)]
        public void MaxFilter(LogLevel filter, string[] markers, int c)
        {
            File.WriteAllText(TestPath, MessageExamples);
            this.view.Expects.One.Method(_ => _.Initialize());
            this.view.Expects.Exactly(2).Method(v => v.SetLogProgressCustomText(null)).WithAnyArguments();
            this.view.Expects.One.Method(v => v.SetLoadedFileCapltion(TestPath));
            this.view.Expects.AtLeastOne.GetProperty(v => v.LogPath).WillReturn(TestPath);

            this.settings.Expects.One.GetProperty(_ => _.PageSize).WillReturn(100);

            var template = ParsingTemplate(markers);
            this.settings.Expects.One.Method(_ => _.ReadParsingTemplate()).WillReturn(template);

            Cleanup(Path.Combine(SqliteSettingsProvider.ApplicationFolder, SettingsDb));
            this.controller = new MainController(this.settings.MockObject);
            this.controller.ReadCompleted += this.OnReadCompleted;
            this.controller.SetView(this.view.MockObject);
            this.controller.MaxFilter((int)filter);
            this.controller.StartReadLog();
            this.WaitReadingComplete();
            Assert.That(this.controller.MessagesCount, NUnit.Framework.Is.EqualTo(c));
        }

        private bool completed;

        void OnReadCompleted(object sender, LogReadCompletedEventArgs e)
        {
            completed = true;
        }

        [TestCase(LogLevel.Warn, new[] { "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL" }, 1)]
        [TestCase(LogLevel.Error, new[] { "TRACE", "DEBUG", "INFO", "WARN", @"\[?ERROR\]?", "FATAL" }, 1)]
        [TestCase(LogLevel.Info, new[] { "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL" }, 2)]
        [TestCase(LogLevel.Fatal, new[] { "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL" }, 0)]
        [TestCase(LogLevel.Error, new[] { "TRACE", "DEBUG", "INFO", "WARN", @"\[?ERRoR\]?", "FATAL" }, 0)]
        public void MinFilter(LogLevel filter, string[] markers, int c)
        {
            File.WriteAllText(TestPath, MessageExamples);
            this.view.Expects.One.Method(_ => _.Initialize());
            this.view.Expects.AtLeastOne.GetProperty(v => v.LogPath).WillReturn(TestPath);
            this.view.Expects.Exactly(2).Method(v => v.SetLogProgressCustomText(null)).WithAnyArguments();
            this.view.Expects.One.Method(v => v.SetLoadedFileCapltion(TestPath));

            Cleanup(Path.Combine(SqliteSettingsProvider.ApplicationFolder, SettingsDb));

            this.settings.Expects.One.GetProperty(_ => _.PageSize).WillReturn(100);

            var template = ParsingTemplate(markers);
            this.settings.Expects.One.Method(_ => _.ReadParsingTemplate()).WillReturn(template);

            this.controller = new MainController(this.settings.MockObject);
            this.controller.ReadCompleted += this.OnReadCompleted;
            this.controller.SetView(this.view.MockObject);
            this.controller.MinFilter((int)filter);
            this.controller.StartReadLog();
            this.WaitReadingComplete();
            Assert.That(this.controller.MessagesCount, NUnit.Framework.Is.EqualTo(c));
        }

        [Test]
        public void ExportRtfFail()
        {
            this.view.Expects.AtLeastOne.GetProperty(v => v.LogPath).WillReturn(TestPath);
            this.view.Expects.One.MethodWith(v => v.OpenExport(TestPath + ".rtf")).WillReturn(false);
            this.controller.ExportToRtf();
        }

        [Test]
        public void ExportRtfSuccess()
        {
            this.view.Expects.AtLeastOne.GetProperty(v => v.LogPath).WillReturn(TestPath);
            this.view.Expects.One.MethodWith(v => v.OpenExport(TestPath + ".rtf")).WillReturn(true);
            this.view.Expects.One.Method(v => v.SaveRtf());
            this.controller.ExportToRtf();
        }

        [TestCase(".*5272.*", 1, true)]
        [TestCase(".*ERROR.*", 1, true)]
        [TestCase(".*message body 2.*", 1, true)]
        [TestCase("t[", 0, true)]
        [TestCase("message body 2", 1, false)]
        [TestCase("^(?!.*message body 2).*", 1, true)]
        public void FilterText(string filter, int messages, bool useRegexp)
        {
            this.view.Expects.AtLeastOne.GetProperty(v => v.LogPath).WillReturn(TestPath);
            this.view.Expects.Exactly(2).Method(v => v.SetLogProgressCustomText(null)).WithAnyArguments();
            this.view.Expects.One.Method(v => v.SetLoadedFileCapltion(TestPath));
            File.WriteAllText(TestPath, MessageExamples);
            this.controller.TextFilter(filter);
            this.controller.UserRegexp(useRegexp);
            this.controller.StartReadLog();
            this.WaitReadingComplete();
            Assert.That(this.controller.MessagesCount, NUnit.Framework.Is.EqualTo(messages));
        }

        [Test]
        public void OpenLogFileCanceled()
        {
            this.view.Expects.One.Method(v => v.OpenLogFile()).WillReturn(false);
            this.controller.OpenLogFile();
        }

        [Test]
        [ExpectedException(typeof(ArgumentException))]
        public void ReadEmptyFile()
        {
            this.view.Expects.AtLeastOne.GetProperty(v => v.LogPath).WillReturn(TestPath);
            File.Create(TestPath).Dispose();
            this.controller.StartReadLog();
            Assert.That(this.controller.MessagesCount, NUnit.Framework.Is.EqualTo(0));
        }

        [Test]
        [ExpectedException(typeof(ArgumentException))]
        public void ReadEmptyWhenMinGreaterThenMax()
        {
            this.controller.MinFilter((int)LogLevel.Error);
            this.controller.MaxFilter((int)LogLevel.Info);
            File.Create(TestPath).Dispose();
            this.controller.StartReadLog();
        }

        [Test]
        [ExpectedException(typeof(FileNotFoundException))]
        public void ReadFromBadPath()
        {
            this.view.Expects.AtLeastOne.GetProperty(v => v.LogPath).WillReturn(TestPath);
            this.view.Expects.No.GetProperty(v => v.LogPath).WillReturn(TestPath);
            this.view.Expects.No.GetProperty(v => v.LogPath).WillReturn(string.Empty);
            this.controller.StartReadLog();
            this.WaitReadingComplete();
            Assert.That(this.controller.MessagesCount, NUnit.Framework.Is.EqualTo(0));
        }

        [Test]
        public void ReadNormalBigLog()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < 1000; i++)
            {
                sb.AppendLine(MessageExamples);
            }
            this.view.Expects.AtLeastOne.GetProperty(v => v.LogPath).WillReturn(TestPath);
            this.view.Expects.Exactly(2).Method(v => v.SetLogProgressCustomText(null)).WithAnyArguments();
            this.view.Expects.One.Method(v => v.SetLoadedFileCapltion(TestPath));
            File.WriteAllText(TestPath, sb.ToString());
            this.controller.StartReadLog();
            this.WaitReadingComplete();
            Assert.That(this.controller.MessagesCount, NUnit.Framework.Is.EqualTo(2000));
        }

        void ReadNormalLog(Encoding encoding)
        {
            this.view.Expects.AtLeastOne.GetProperty(v => v.LogPath).WillReturn(TestPath);
            this.view.Expects.Exactly(2).Method(v => v.SetLogProgressCustomText(null)).WithAnyArguments();
            this.view.Expects.One.Method(v => v.SetLoadedFileCapltion(TestPath));
            File.WriteAllText(TestPath, MessageExamples, encoding);
            this.controller.StartReadLog();
            this.WaitReadingComplete();
            Assert.That(this.controller.MessagesCount, NUnit.Framework.Is.EqualTo(2));
        }

        [Test]
        public void ReadNormalLog()
        {
            ReadNormalLog(Encoding.UTF8);
        }

        [Test]
        public void ReadNormalLogWin1251()
        {
            ReadNormalLog(Encoding.GetEncoding("windows-1251"));
        }

        [Test]
        public void ReadNotEmptyLogWithoutMessagesMatch()
        {
            this.view.Expects.AtLeastOne.GetProperty(v => v.LogPath).WillReturn(TestPath);
            this.view.Expects.Exactly(2).Method(v => v.SetLogProgressCustomText(null)).WithAnyArguments();
            this.view.Expects.One.Method(v => v.SetLoadedFileCapltion(TestPath));
            File.WriteAllText(TestPath, "test log");
            this.controller.StartReadLog();
            this.WaitReadingComplete();
            Assert.That(this.controller.MessagesCount, NUnit.Framework.Is.EqualTo(1));
        }

        [Test]
        public void ReadRecentFilesEmpty()
        {
            this.view.Expects.One.Method(v => v.ClearRecentFilesList());
            this.settings.Expects.Any.GetProperty(_ => _.KeepLastNFiles).WillReturn(KeepLastNFiles);
            this.settings.Expects.Any.GetProperty(_ => _.FullPathToDatabase).WillReturn(FullPathToTestDb);
            this.controller.ReadRecentFiles();
        }

        [Test]
        public void SaveAndReadRecentFilesNoFile()
        {
            this.settings.Expects.Any.GetProperty(_ => _.KeepLastNFiles).WillReturn(KeepLastNFiles);
            this.settings.Expects.Any.GetProperty(_ => _.FullPathToDatabase).WillReturn(FullPathToTestDb);
            using (this.mockery.Ordered())
            {
                this.view.Expects.One.GetProperty(v => v.LogPath).WillReturn(TestPath);
                this.view.Expects.One.Method(v => v.ClearRecentFilesList());
                this.view.Expects.No.MethodWith(v => v.CreateRecentFileItem(TestPath));
            }
            this.controller.AddCurrentFileToRecentFilesList();
            this.controller.ReadRecentFiles();
        }

        [Test]
        public void SaveAndReadRecentFiles()
        {
            CreateEmpty(f1, f2);
            this.settings.Expects.Any.GetProperty(_ => _.KeepLastNFiles).WillReturn(KeepLastNFiles);
            this.settings.Expects.Any.GetProperty(_ => _.FullPathToDatabase).WillReturn(FullPathToTestDb);
            using (this.mockery.Ordered())
            {
                this.view.Expects.One.GetProperty(v => v.LogPath).WillReturn(f1);
                this.view.Expects.One.GetProperty(v => v.LogPath).WillReturn(f2);
                this.view.Expects.One.Method(v => v.ClearRecentFilesList());
                this.view.Expects.One.MethodWith(v => v.CreateRecentFileItem(f2));
                this.view.Expects.One.MethodWith(v => v.CreateRecentFileItem(f1));
            }
            this.controller.AddCurrentFileToRecentFilesList();
            this.controller.AddCurrentFileToRecentFilesList();
            this.controller.ReadRecentFiles();
        }
        
        [Test]
        public void RecentFilesMoreThenLimit()
        {
            CreateEmpty(f1, f2, f3);
            this.settings.Expects.Any.GetProperty(_ => _.KeepLastNFiles).WillReturn(KeepLastNFiles);
            this.settings.Expects.Any.GetProperty(_ => _.FullPathToDatabase).WillReturn(FullPathToTestDb);
            using (this.mockery.Ordered())
            {
                this.view.Expects.One.GetProperty(v => v.LogPath).WillReturn(f1);
                this.view.Expects.One.GetProperty(v => v.LogPath).WillReturn(f2);
                this.view.Expects.One.GetProperty(v => v.LogPath).WillReturn(f3);
                this.view.Expects.One.Method(v => v.ClearRecentFilesList());
                this.view.Expects.One.MethodWith(v => v.CreateRecentFileItem(f3));
                this.view.Expects.One.MethodWith(v => v.CreateRecentFileItem(f2));
            }
            this.controller.AddCurrentFileToRecentFilesList();
            this.controller.AddCurrentFileToRecentFilesList();
            this.controller.AddCurrentFileToRecentFilesList();
            this.controller.ReadRecentFiles();
        }
        
        [Test]
        public void RecentFilesMoreThenLimitNoOneFile()
        {
            CreateEmpty(f1, f2);
            this.settings.Expects.Any.GetProperty(_ => _.KeepLastNFiles).WillReturn(KeepLastNFiles);
            this.settings.Expects.Any.GetProperty(_ => _.FullPathToDatabase).WillReturn(FullPathToTestDb);
            using (this.mockery.Ordered())
            {
                this.view.Expects.One.GetProperty(v => v.LogPath).WillReturn(f1);
                this.view.Expects.One.GetProperty(v => v.LogPath).WillReturn(f2);
                this.view.Expects.One.GetProperty(v => v.LogPath).WillReturn("file_3");
                this.view.Expects.One.Method(v => v.ClearRecentFilesList());
                this.view.Expects.One.MethodWith(v => v.CreateRecentFileItem(f2));
            }
            this.controller.AddCurrentFileToRecentFilesList();
            this.controller.AddCurrentFileToRecentFilesList();
            this.controller.AddCurrentFileToRecentFilesList();
            this.controller.ReadRecentFiles();
        }

        [Test]
        public void TotalPagesNoMessages()
        {
            Assert.That(this.controller.TotalPages, NUnit.Framework.Is.EqualTo(1));
        }

        [TestCase(10, 1)]
        [TestCase(60, 2)]
        [TestCase(100, 2)]
        public void PagingTests(int messages, int pages)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < messages; i++)
            {
                sb.AppendLine(MessageExamples);
            }
            this.view.Expects.AtLeastOne.GetProperty(v => v.LogPath).WillReturn(TestPath);
            this.view.Expects.Exactly(2).Method(v => v.SetLogProgressCustomText(null)).WithAnyArguments();
            this.view.Expects.One.Method(v => v.SetLoadedFileCapltion(TestPath));
            File.WriteAllText(TestPath, sb.ToString());
            this.controller.StartReadLog();
            this.WaitReadingComplete();
            Assert.That(this.controller.TotalPages, NUnit.Framework.Is.EqualTo(pages));
        }

        [TestCase("test", false, true)]
        [TestCase("t[", false, true)]
        [TestCase("t[", true, false)]
        [TestCase("t[1]", true, true)]
        [TestCase(null, true, true)]
        [TestCase(null, false, true)]
        public void FilterValidation(string filter, bool useRegex, bool result)
        {
            Assert.That(MainController.IsValidFilter(filter, useRegex), NUnit.Framework.Is.EqualTo(result));
        }

        [Test]
        public void StartReadingWithinDelay()
        {
            this.settings.Expects.Exactly(2).SetProperty(_ => _.MessageFilter).To(new EqualMatcher("f"));
            this.view.Expects.No.Method(v => v.StartReading());
            this.controller.StartReading("f", false);
            this.controller.StartReading("f", false);
            Assert.That(this.controller.PendingUpdate);
        }
        
        [Test]
        public void StartReadingOutsideDelay()
        {
            this.settings.Expects.Exactly(2).SetProperty(_ => _.MessageFilter).To(new EqualMatcher("f"));
            this.view.Expects.One.Method(v => v.StartReading());
            this.controller.StartReading("f", false);
            Thread.Sleep(TimeSpan.FromMilliseconds(600));
            Assert.That(!this.controller.PendingUpdate);
        }
    }
}