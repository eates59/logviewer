﻿// Created by: egr
// Created at: 13.10.2013
// © 2012-2015 Alexander Egorov

using System.Collections.Generic;
using System.Drawing;
using logviewer.engine;

namespace logviewer.core
{
    public class FormData
    {
        public FormData()
        {
            this.Colors = new Dictionary<LogLevel, Color>();
        }

        public string KeepLastNFiles { get; set; }
        public bool OpenLastFile { get; set; }
        public bool AutoRefreshOnFileChange { get; set; }
        public string PageSize { get; set; }
        public IDictionary<LogLevel, Color> Colors { get; private set; }
    }
}