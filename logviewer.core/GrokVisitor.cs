// Created by: egr
// Created at: 02.10.2014
// � 2012-2014 Alexander Egorov

using System;
using System.Collections.Generic;
using System.Text;
using Antlr4.Runtime.Tree;

namespace logviewer.core
{
    public class GrokVisitor : GrokBaseVisitor<string>
    {
        private readonly StringBuilder stringBuilder = new StringBuilder();

        private const int MaxDepth = 20;

        static readonly Dictionary<string, string> templates = new Dictionary<string, string>
        {
            { "USERNAME", "[a-zA-Z0-9._-]+" },
            { "WORD", @"\b\w+\b" },
            { "SPACE", @"\s*" },
            { "DATA", @".*?" },
            { "GREEDYDATA", @".*" },
            { "INT", @"(?:[+-]?(?:[0-9]+))" },
            { "BASE10NUM", @"(?<![0-9.+-])(?>[+-]?(?:(?:[0-9]+(?:\.[0-9]+)?)|(?:\.[0-9]+)))" },
            { "BASE16NUM", @"(?<![0-9A-Fa-f])(?:[+-]?(?:0x)?(?:[0-9A-Fa-f]+))" },
            { "BASE16FLOAT", @"\b(?<![0-9A-Fa-f.])(?:[+-]?(?:0x)?(?:(?:[0-9A-Fa-f]+(?:\.[0-9A-Fa-f]*)?)|(?:\.[0-9A-Fa-f]+)))\b" },
            { "POSINT", @"\b(?:[1-9][0-9]*)\b" },
            { "NONNEGINT", @"\b(?:[0-9]+)\b" },
            { "NOTSPACE", @"\S+" },
            { "QUOTEDSTRING", "(?>(?<!\\\\)(?>\"(?>\\\\.|[^\\\\\"]+)+\"|\"\"|(?>'(?>\\\\.|[^\\\\']+)+')|''|(?>`(?>\\\\.|[^\\\\`]+)+`)|``))" },
            { "YEAR", @"(?>\d\d){1,2}" },
            { "HOUR", @"(?:2[0123]|[01]?[0-9])" },
            { "MINUTE", @"(?:[0-5][0-9])" },
            { "SECOND", @"(?:(?:[0-5][0-9]|60)(?:[:.,][0-9]+)?)" },
            { "MONTH", @"\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\b" },
            { "MONTHNUM", @"(?:0?[1-9]|1[0-2])" },
            { "MONTHDAY", @"(?:(?:0[1-9])|(?:[12][0-9])|(?:3[01])|[1-9])" },
            { "TIME", @"(?!<[0-9])%{HOUR}:%{MINUTE}(?::%{SECOND})(?![0-9])" },
            { "DATE_US", @"%{MONTHNUM}[/-]%{MONTHDAY}[/-]%{YEAR}" },
            { "DATE_EU", @"%{MONTHDAY}[./-]%{MONTHNUM}[./-]%{YEAR}" },
            { "ISO8601_TIMEZONE", @"(?:Z|[+-]%{HOUR}(?::?%{MINUTE}))" },
            { "ISO8601_SECOND", @"(?:%{SECOND}|60)" },
            { "TIMESTAMP_ISO8601", @"%{YEAR}-%{MONTHNUM}-%{MONTHDAY}[T ]%{HOUR}:?%{MINUTE}(?::?%{SECOND})?%{ISO8601_TIMEZONE}?" },
        }; 

        public string Template
        {
            get { return this.stringBuilder.ToString(); }
        }

        public override string VisitFind(GrokParser.FindContext ctx)
        {
            ITerminalNode node = ctx.ID();

            if (node == null)
            {
                return this.VisitChildren(ctx);
            }

            Log.Instance.TraceFormatted(node.Symbol.Text);
            Console.WriteLine("id: " + node.Symbol.Text);
            if (templates.ContainsKey(node.Symbol.Text))
            {
                var regex = templates[node.Symbol.Text];

                var depth = 0;

                do
                {
                    foreach (var k in templates.Keys)
                    {
                        var link = "%{" + k + "}";
                        if (regex.Contains(link))
                        {
                            regex = regex.Replace(link, templates[k]);
                        }
                    }
                    ++depth;
                } while (regex.Contains("%{") || depth > MaxDepth);

                this.stringBuilder.Append(regex);
            }
            else
            {
                this.stringBuilder.Append("%{");
                this.stringBuilder.Append(node.Symbol.Text);
                this.stringBuilder.Append("}");
            }
            return this.VisitChildren(ctx);
        }

        public override string VisitPaste(GrokParser.PasteContext context)
        {
            foreach (var node in context.STRING())
            {
                Console.WriteLine("str: " + node.Symbol.Text);
                this.stringBuilder.Append(node.Symbol.Text);
            }
            return this.VisitChildren(context);
        }
    }
}