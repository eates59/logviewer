﻿// This is an open source non-commercial project. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com
// Created by: egr
// Created at: 11.12.2015
// © 2012-2016 Alexander Egorov

namespace logviewer.logic.fsm
{
    /// <summary>
    ///     A simple base class that can be used when creating state machine states. Since the interface is implemented
    ///     with virtual methods in this class, subclasses can choose which methods to override.
    /// </summary>
    public abstract class SolidState : ISolidState
    {
        public void Entering(object context)
        {
            this.DoEntering(context);
        }

        public void Exiting(object context)
        {
            this.DoExiting(context);
        }

        protected virtual void DoEntering(object context)
        {
            // No code
        }

        protected virtual void DoExiting(object context)
        {
            // No code
        }
    }
}