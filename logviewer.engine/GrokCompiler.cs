﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace logviewer.engine
{
    /// <summary>
    ///     Represents grok template compiler
    /// </summary>
    internal class GrokCompiler
    {
        private readonly Action<string> customErrorOutputMethod;
        private StringBuilder translationUnit;
        private readonly List<Semantic> messageSchema = new List<Semantic>();
        private const string MainPattern = "MAIN";

        /// <summary>
        ///     Creates new compiler instance using custom error method output if necessary
        /// </summary>
        /// <param name="customErrorOutputMethod"></param>
        internal GrokCompiler(Action<string> customErrorOutputMethod = null)
        {
            this.customErrorOutputMethod = customErrorOutputMethod;
            this.CreateLibraryTemplates();
        }

        private void CreateLibraryTemplates()
        {
            const string pattern = "*.patterns";
            var patternFiles = Directory.GetFiles(Extensions.AssemblyDirectory, pattern, SearchOption.TopDirectoryOnly);
            if (patternFiles.Length == 0)
            {
                patternFiles = Directory.GetFiles(".", pattern, SearchOption.TopDirectoryOnly);
            }
            this.translationUnit = new StringBuilder();
            foreach (var file in patternFiles)
            {
                this.translationUnit.AppendLine(File.ReadAllText(file));
            }
        }

        /// <summary>
        ///     Compiles grok specified
        /// </summary>
        /// <param name="grok"></param>
        /// <returns>Regular expression</returns>
        internal string Compile(string grok)
        {
            var parser = new grammar.GrokParser(this.customErrorOutputMethod);
            var translation = string.Format("{0} {1}", MainPattern, grok);
            this.translationUnit.AppendLine(translation);
            parser.Parse(this.translationUnit.ToString());

            if (!parser.IsPropertyStackEmpty)
            {
                throw new Exception("Unused property detected");
            }
            var main = parser.DefinitionsTable[MainPattern];
            var content = main.Content;
            return content;
        }

        /// <summary>
        /// Message schema - all possible properties and casting rules
        /// </summary>
        internal ICollection<Semantic> MessageSchema
        {
            get { return this.messageSchema; }
        }
    }
}