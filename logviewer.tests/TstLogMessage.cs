﻿// Created by: egr
// Created at: 02.01.2013
// © 2012-2014 Alexander Egorov

using System;
using System.Collections.Generic;
using logviewer.core;
using NUnit.Framework;

namespace logviewer.tests
{
    [TestFixture]
    public class TstLogMessage
    {
        [SetUp]
        public void Setup()
        {
            this.m = LogMessage.Create();
        }

        private LogMessage m;
        private const string H = "h";
        private const string B = "b";

        [Test]
        public void Full()
        {
            this.m.AddLine(H);
            this.m.AddLine(B);
            Assert.That(this.m.IsEmpty, Is.False);
            Assert.That(this.m.Header, Is.EqualTo(H));
            Assert.That(this.m.Body, Is.EqualTo(B + "\n"));
        }

        [Test]
        public void FullCached()
        {
            this.m.AddLine(H);
            this.m.AddLine(B);
            this.m.Cache(null);
            Assert.That(this.m.IsEmpty, Is.False);
            Assert.That(this.m.Header, Is.EqualTo(H));
            Assert.That(this.m.Body, Is.EqualTo(B));
        }

        [Test]
        public void IsEmpty()
        {
            Assert.That(this.m.IsEmpty);
            Assert.That(this.m.Body, Is.Empty);
            Assert.That(this.m.Header, Is.Empty);
        }
        
        [Test]
        public void IsEmptyAllStringsEmpty()
        {
            this.m.AddLine(string.Empty);
            this.m.AddLine(string.Empty);
            Assert.That(this.m.IsEmpty, Is.False);
            Assert.That(this.m.Header, Is.Empty);
            Assert.That(this.m.Body, Is.Empty);
            
        }

        [Test]
        public void OnlyHead()
        {
            this.m.AddLine(H);
            Assert.That(this.m.IsEmpty, Is.False);
            Assert.That(this.m.Header, Is.EqualTo(H));
            Assert.That(this.m.Body, Is.Empty);
        }

        [Test]
        public void OnlyHeadCached()
        {
            this.m.AddLine(H);
            this.m.Cache(null);
            Assert.That(this.m.IsEmpty, Is.False);
            Assert.That(this.m.Header, Is.EqualTo(H));
            Assert.That(this.m.Body, Is.Empty);
        }
        
        [TestCase("Trace", LogLevel.Trace)]
        [TestCase("TRACE", LogLevel.Trace)]
        [TestCase("Debug", LogLevel.Debug)]
        [TestCase("DEBUG", LogLevel.Debug)]
        [TestCase("Info", LogLevel.Info)]
        [TestCase("INFO", LogLevel.Info)]
        [TestCase("Warn", LogLevel.Warn)]
        [TestCase("WARN", LogLevel.Warn)]
        [TestCase("Warning", LogLevel.Trace)]
        [TestCase("Error", LogLevel.Error)]
        [TestCase("ERROR", LogLevel.Error)]
        [TestCase("Fatal", LogLevel.Fatal)]
        [TestCase("FATAL", LogLevel.Fatal)]
        public void ParseLogLevel(string input, LogLevel result)
        {
            this.ParseIntegerTest("level", "LogLevel", ParserType.LogLevel, input);
            Assert.That((LogLevel)this.m.IntegerProperty("level"), Is.EqualTo(result));
        }

        [TestCase("2014-10-23 20:00:51,790", 2014, 10, 23, 20, 0, 51, 790)]
        [TestCase("2014-10-23 20:00:51", 2014, 10, 23, 20, 0, 51, 0)]
        [TestCase("24/Oct/2014:09:34:30 +0400", 2014, 10, 24, 9, 34, 30, 0)]
        [TestCase("24/Oct/2014:09:34:30 +0000", 2014, 10, 24, 13, 34, 30, 0)]
        public void ParseDateTime(string input, int y, int month, int d, int h, int min, int sec, int millisecond)
        {
            this.ParseIntegerTest("dt", "DateTime", ParserType.Datetime, input);
            Assert.That(DateTime.FromFileTime(this.m.IntegerProperty("dt")), Is.EqualTo(new DateTime(y, month, d, h, min, sec, millisecond)));
        }

        private void ParseIntegerTest(string prop, string type, ParserType parser, string input)
        {
            this.m.AddLine(H);
            var s = new SemanticProperty(prop, parser);
            var r = new Rule(type);
            ISet<Rule> rules = new HashSet<Rule>();
            rules.Add(r);
            IDictionary<SemanticProperty, ISet<Rule>> schema = new Dictionary<SemanticProperty, ISet<Rule>>();
            schema.Add(s, rules);
            IDictionary<string, string> props = new Dictionary<string, string>();
            props.Add(prop, input);
            this.m.AddProperties(props);
            this.m.Cache(schema);
        }
    }
}