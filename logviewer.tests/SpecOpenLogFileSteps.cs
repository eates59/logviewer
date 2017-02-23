﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FluentAssertions;
using logviewer.engine;
using logviewer.logic;
using logviewer.logic.Annotations;
using logviewer.logic.models;
using logviewer.logic.storage;
using logviewer.logic.ui;
using logviewer.logic.ui.main;
using Moq;
using TechTalk.SpecFlow;

namespace logviewer.tests
{
    [Binding]
    [PublicAPI]
    public class SpecOpenLogFileSteps
    {
        private const string MessageExamples =
            "2008-12-27 19:31:47,250 [4688] INFO \nmessage body 1\n2008-12-27 19:40:11,906 [5272] ERROR \nmessage body 2";

        private const string MessageTemplate =
            "2008-12-27 19:31:47,250 [4688] {0} \nmessage body {1}";

        private readonly MainMachine machine;
        private readonly MainModel model;
        private readonly Mock<IMainViewModel> viewModel;
        private bool completed;
        private bool waitResult;

        private string path;

        public SpecOpenLogFileSteps()
        {
            this.viewModel = new Mock<IMainViewModel>();
            var settings = new Mock<ISettingsProvider>();
            var itemsProvider = new Mock<IItemsProvider<string>>();
            this.viewModel.SetupGet(_ => _.SettingsProvider).Returns(settings.Object);
            var logProvider = new LogProvider(null, settings.Object);
            this.viewModel.SetupGet(_ => _.Provider).Returns(logProvider);
            this.viewModel.SetupGet(_ => _.Datasource).Returns(new VirtualizingCollection<string>(itemsProvider.Object));
            this.viewModel.SetupGet(_ => _.GithubAccount).Returns("egoroff");
            this.viewModel.SetupGet(_ => _.GithubProject).Returns("logviewer");

            var template = ParsingTemplate(logic.models.ParsingTemplate.Defaults.First().StartMessage);
            settings.Setup(_ => _.ReadParsingTemplate()).Returns(template);

            this.model = new MainModel(this.viewModel.Object);
            this.model.ReadCompleted += this.OnReadCompleted;
            this.machine = new MainMachine(this.model, this.viewModel.Object);
        }

        [Given(@"I have file ""(.*)"" on disk")]
        public void GivenIHaveFileOnDisk(string filePath)
        {
            this.path = filePath;
            this.viewModel.SetupGet(_ => _.LogPath).Returns(this.path);
        }

        [Given(@"The file contains (.*) messages with levels ""(.*)"" and ""(.*)""")]
        public void GivenTheFileContainsMessagesWithLevelsAnd(int count, string level1, string level2)
        {
            var sb = new StringBuilder();
            var levels = new[] { level1, level2 };
            for (var i = 0; i < count; i++)
            {
                sb.AppendFormat(MessageTemplate, levels[i % levels.Length], i + 1);
                sb.AppendLine();
            }
            File.WriteAllText(this.path, sb.ToString());
        }

        [When(@"I press open with default filtering parameters")]
        public void WhenIPressOpenWithDefaultFilteringParameters()
        {
            this.Open(LogLevel.Trace, LogLevel.Fatal);
        }

        [When(@"I press reload")]
        public void WhenIPressReload()
        {
            this.machine.Reload();
        }


        [When(@"I press open with min level ""(.*)"" and max level ""(.*)""")]
        public void WhenIPressOpenWithMinLevelAndMaxLevel(string min, string max)
        {
            LogLevel minLevel;
            Enum.TryParse(min, out minLevel);

            LogLevel maxLevel;
            Enum.TryParse(max, out maxLevel);

            this.Open(minLevel, maxLevel);
        }

        [When(@"wait (.*) seconds")]
        public void WhenWaitSeconds(int seconds)
        {
            this.waitResult = SpinWait.SpinUntil(() => this.completed, TimeSpan.FromSeconds(seconds));
        }

        [Then(@"the number of shown messages should be (.*)")]
        public void ThenTheNumberOfReadMessagesShouldBeInTheLogStore(int messagesCount)
        {
            this.waitResult.Should().BeTrue("Read should be completed in 2 second but it didn't");
            this.viewModel.VerifySet(x => x.MessageCount = messagesCount);
        }

        [AfterScenario("mainmodel")]
        public void AfterScenario()
        {
            if (File.Exists(this.path))
                File.Delete(this.path);
            this.machine.Close();
        }

        private void Open(LogLevel minLevel, LogLevel maxLevel)
        {
            this.viewModel.SetupGet(_ => _.MinLevel).Returns((int)minLevel);
            this.viewModel.SetupGet(_ => _.MaxLevel).Returns((int)maxLevel);
            this.viewModel.SetupGet(_ => _.From).Returns(DateTime.MinValue);
            this.viewModel.SetupGet(_ => _.To).Returns(DateTime.MaxValue);
            this.machine.Open(this.path);
        }

        private void OnReadCompleted(object sender, EventArgs e)
        {
            this.completed = true;
        }

        private static ParsingTemplate ParsingTemplate(string startMessage)
        {
            return new ParsingTemplate
            {
                Index = 0,
                StartMessage = startMessage
            };
        }
    }
}