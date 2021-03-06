// Created by: egr
// Created at: 06.07.2015
// � 2012-2015 Alexander Egorov

using System;

namespace logviewer.engine.grammar
{
    internal partial class GrokScanner
    {
        private Action<string> customErrorOutputMethod = Console.WriteLine;

        internal Action<string> CustomErrorOutputMethod
        {
            get { return this.customErrorOutputMethod; }
            set { this.customErrorOutputMethod = value; }
        }

        public override void yyerror(string format, params object[] args)
        {
            base.yyerror(format, args);
            var message = string.Format(format, args);
            var currentToken = string.Format("current token: '{0}'", this.yytext);
            this.CustomErrorOutputMethod(currentToken);
            this.CustomErrorOutputMethod(string.Empty);
            throw new GrokSyntaxException(message);
        }
    }
}