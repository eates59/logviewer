﻿// This is an open source non-commercial project. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com
// Created by: egr
// Created at: 02.10.2014
// © 2012-2017 Alexander Egorov

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using logviewer.engine.grammar;

namespace logviewer.engine
{
    /// <summary>
    /// Represents pattern matcher
    /// </summary>
    public class GrokMatcher
    {
        private Regex regex;

        // ReSharper disable once FieldCanBeMadeReadOnly.Local
        private List<Semantic> messageSchema = new List<Semantic>();

        // ReSharper disable once FieldCanBeMadeReadOnly.Local
        private Action<string> customErrorOutputMethod;

        /// <summary>
        /// Init new matcher
        /// </summary>
        /// <param name="grok">Template definition</param>
        /// <param name="options">Result regexp options</param>
        /// <param name="customErrorOutputMethod"></param>
        public GrokMatcher(string grok, RegexOptions options = RegexOptions.None, Action<string> customErrorOutputMethod = null)
        {
            this.customErrorOutputMethod = customErrorOutputMethod;
            this.CreateRegexp(grok, options);
        }

        private void CreateRegexp(string grok, RegexOptions options)
        {
            try
            {
                this.Template = this.Compile(grok);
                this.regex = new Regex(this.Template, options);
            }
            catch (GrokSyntaxException e)
            {
                this.HandleRegexpException(grok, grok, e);
            }
            catch (ArgumentException e)
            {
                this.HandleRegexpException(grok, string.Empty, e);
            }
            catch (Exception e)
            {
                this.HandleRegexpException(grok, string.Empty, e);
            }
        }

        private void HandleRegexpException(string grok, string template, Exception e)
        {
            if (this.customErrorOutputMethod != null)
            {
                this.customErrorOutputMethod(e.ToString());
            }
            else
            {
                System.Diagnostics.Trace.WriteLine(e.ToString());
            }
            this.CompilationFailed = true;
            this.Template = grok;
            this.regex = new Regex(template);
        }

        private string Compile(string grok)
        {
            var compiler = new GrokCompiler(this.customErrorOutputMethod);
            var result = compiler.Compile(grok);
            this.messageSchema.AddRange(compiler.MessageSchema);
            return result;
        }

        /// <summary>
        /// Regexp that compiled from pattern (grok definition)
        /// </summary>
        public string Template { get; private set; }

        /// <summary>
        /// Whether the grok compilation failed or not
        /// </summary>
        public bool CompilationFailed { get; private set; }

        /// <summary>
        /// Message schema - all possible properties and casting rules
        /// </summary>
        public ICollection<Semantic> MessageSchema => this.messageSchema;

        /// <summary>
        /// Checks line matching the pattern
        /// </summary>
        /// <param name="s">string to validate</param>
        /// <returns>True if string matches the pattern false otherwise </returns>
        public bool Match(string s)
        {
            return this.regex.IsMatch(s);
        }

        /// <summary>
        /// Parse line and extract metadata that message schema defines
        /// </summary>
        /// <param name="input">String to parse</param>
        /// <returns>Metadata dictionary or null</returns>
        public ICollection<KeyValuePair<string, string>> Parse(string input)
        {
            var match = this.regex.Match(input);
            if (!match.Success)
            {
                return null;
            }

            var result = new KeyValuePair<string, string>[this.messageSchema.Count];
            for (var i = 0; i < this.messageSchema.Count; i++)
            {
                var property = this.messageSchema[i].Property;
                result[i] = new KeyValuePair<string, string>(property, match.Groups[property].Value);
            }

            return result;
        }
    }
}
