﻿// Created by: egr
// Created at: 29.05.2015
// © 2012-2015 Alexander Egorov

using System.Collections.Generic;

namespace logviewer.engine.grammar
{
    internal class ReferencePattern : IPattern
    {
        private readonly IDictionary<string, IPattern> definitions;
        private readonly string grok;

        internal ReferencePattern(string grok, IDictionary<string, IPattern> definitions)
        {
            this.grok = grok;
            this.definitions = definitions;
        }

        internal string Property { get; set; }
        internal Semantic Schema { get; set; }

        public string Compose(IList<Semantic> messageSchema)
        {
            var pattern = this.definitions.ContainsKey(this.grok)
                ? this.definitions[this.grok]
                : new PassthroughPattern(this.grok);

            if (this.Schema != default(Semantic))
            {
                messageSchema.Add(this.Schema);
            }

            var result = pattern.Compose(messageSchema);
            return string.IsNullOrWhiteSpace(this.Property)
                ? result
                : string.Format(@"(?<{0}>{1})", this.Property, result);
        }
    }
}