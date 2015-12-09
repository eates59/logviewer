﻿// Created by: egr
// Created at: 20.11.2014
// © 2012-2015 Alexander Egorov

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FluentAssertions;
using logviewer.engine;
using Moq;
using Xunit;

namespace logviewer.tests
{
    public class TstLogReader : IDisposable
    {
        internal const string MessageExamples =
            "2008-12-27 19:31:47,250 [4688] INFO \nmessage body 1\n2008-12-27 19:40:11,906 [5272] ERROR \nmessage body 2";
        internal const string MessageExamplesRu =
            "2008-12-27 19:31:47,250 [4688] INFO \r\nтело сообщения 1\n2008-12-27 19:40:11,906 [5272] ERROR \r\nтело сообщения 2";

        internal const string NlogGrok = @"^\[?%{TIMESTAMP_ISO8601:Occured:DateTime}\]?%{DATA}%{LOGLEVEL:Level:LogLevel}%{DATA}";
        private readonly RulesBuilder builder;


        private readonly LogReader reader;
        private readonly MemoryStream stream;
        private byte[] buffer;
        private readonly Mock<ICharsetDetector> detector;
        private readonly string path;

        public TstLogReader()
        {
            this.detector = new Mock<ICharsetDetector>();
            this.stream = new MemoryStream();
            var grokMatcher = new GrokMatcher(NlogGrok);
            this.reader = new LogReader(this.detector.Object, grokMatcher);
            this.builder = new RulesBuilder(grokMatcher.MessageSchema);
            this.path = Path.GetTempFileName();
        }

        public static IEnumerable<object[]> ValidStreams => new[]
        {
            new object[] { MessageExamples, Encoding.UTF8 },
            new object[] { Environment.NewLine + MessageExamples, Encoding.UTF8 },
            new object[] { " " + Environment.NewLine + " " + Environment.NewLine + MessageExamples, Encoding.UTF8 },
            new object[] { MessageExamples + Environment.NewLine, Encoding.UTF8 },
            new object[] { Environment.NewLine + MessageExamples + Environment.NewLine, Encoding.UTF8 },
            new object[] { MessageExamplesRu, Encoding.GetEncoding("windows-1251") }
        };

        public void Dispose()
        {
            this.stream.Dispose();
            if (File.Exists(this.path))
            {
                File.Delete(this.path);
            }
        }

        private void CreateStream(string data = MessageExamples, Encoding encoding = null)
        {
            if (encoding != null && !Equals(encoding, Encoding.UTF8))
            {
                data = Convert(data, Encoding.UTF8, encoding);
            }
            this.buffer = (encoding ?? Encoding.UTF8).GetBytes(data);
            this.stream.Write(this.buffer, 0, this.buffer.Length);
        }

        [Theory, MemberData("ValidStreams")]
        public void Read_FromStream_AllRead(string data, Encoding encoding)
        {
            // Arrange
            this.CreateStream(data, encoding);
            this.stream.Seek(0, SeekOrigin.Begin);
            var count = 0;
            Action<LogMessage> onRead = delegate(LogMessage message)
            {
                count++;
                message.IsEmpty.Should().BeFalse();
            };

            // Act
            this.reader.Read(this.stream, 0, onRead, encoding);

            // Assert
            count.Should().Be(2);
        }

        [Fact]
        public void Read_FromStreamWithCache_AllRead()
        {
            // Arrange
            this.CreateStream();
            this.stream.Seek(0, SeekOrigin.Begin);
            var count = 0;
            Action<LogMessage> onRead = delegate(LogMessage message)
            {
                count++;
                message.IsEmpty.Should().BeFalse();
                message.Cache(this.builder.Rules);
                var level = (LogLevel) message.IntegerProperty("Level");
                var date = DateTime.FromFileTime(message.IntegerProperty("Occured"));
                Assert.InRange(level, LogLevel.Info, LogLevel.Error);
                date.Year.Should().Be(2008);
                date.Month.Should().Be(12);
                date.Day.Should().Be(27);
            };

            // Act
            this.reader.Read(this.stream, 0, onRead);

            // Assert
            count.Should().Be(2);
        }

        [Fact]
        public void Read_FromStreamCancelled_NoneRead()
        {
            // Arrange
            this.CreateStream();
            this.stream.Seek(0, SeekOrigin.Begin);
            var count = 0;
            Action<LogMessage> onRead = delegate(LogMessage message)
            {
                count++;
                message.IsEmpty.Should().BeTrue();
            };

            // Act
            this.reader.Cancel();
            this.reader.Read(this.stream, 0, onRead);

            // Assert
            count.Should().Be(0);
        }

        [Fact]
        public void Read_FromStreamEnd_EmptyRead()
        {
            // Arrange
            this.CreateStream();
            var count = 0;
            Action<LogMessage> onRead = delegate(LogMessage message)
            {
                count++;
                message.IsEmpty.Should().BeTrue();
            };
            // Act
            this.reader.Read(this.stream, 0, onRead);

            // Assert
            count.Should().Be(1);
        }

        [Fact]
        public void Read_FromNotUtf8File_AllRead()
        {
            // Arrange
            var encoding = Encoding.GetEncoding("windows-1251");
            this.detector.Setup(_ => _.Detect(It.IsAny<Stream>())).Returns(encoding);
            var encoded = Convert(MessageExamplesRu, Encoding.UTF8, encoding);
            File.WriteAllText(this.path, encoded, encoding);

            var count = 0;
            Action<LogMessage> onRead = delegate (LogMessage message)
            {
                count++;
                message.IsEmpty.Should().BeFalse();
            };
            Encoding detected = null;

            // Act
            this.reader.Read(this.path, onRead, ref detected);

            // Assert
            count.Should().Be(2);
            detected.EncodingName.Should().Be(encoding.EncodingName);
        }

        [Fact]
        public void Read_FromEmptyFile_NoneRead()
        {
            // Arrange
            var count = 0;
            Action<LogMessage> onRead = delegate {
                count++;
            };
            Encoding detected = null;

            // Act
            this.reader.Read(this.path, onRead, ref detected);

            // Assert
            count.Should().Be(0);
        }

        internal static string Convert(string line, Encoding srcEncoding, Encoding dstEncoding)
        {
            byte[] srcBytes = srcEncoding.GetBytes(line);
            byte[] dstBytes = Encoding.Convert(srcEncoding, dstEncoding, srcBytes);
            return dstEncoding.GetString(dstBytes);
        }
    }
}