﻿// Created by: egr
// Created at: 16.09.2012
// © 2012-2013 Alexander Egorov

using System;
using System.Windows.Forms;
using Ninject;

namespace logviewer
{
    internal static class Program
    {
        internal static IKernel Kernel { get; private set; }

        /// <summary>
        ///     The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            Kernel = new StandardKernel(new LogviewerModule());
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(Kernel.Get<MainDlg>());
        }
    }
}