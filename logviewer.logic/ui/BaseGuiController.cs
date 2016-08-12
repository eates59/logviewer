// Created by: egr
// Created at: 04.10.2014
// � 2012-2016 Alexander Egorov

using System;
using System.Threading;
using System.Threading.Tasks;

namespace logviewer.logic.ui
{
    public abstract class BaseGuiController
    {
        private readonly SynchronizationContext winformsOrDefaultContext;

        protected BaseGuiController()
        {
            this.winformsOrDefaultContext = SynchronizationContext.Current ?? new SynchronizationContext();
            this.UiSyncContext = TaskScheduler.FromCurrentSynchronizationContext();
        }

        protected TaskScheduler UiSyncContext { get; }

        protected SynchronizationContext WinformsOrDefaultContext => this.winformsOrDefaultContext;

        protected void RunOnGuiThread(Action action)
        {
            this.winformsOrDefaultContext.Post(o => action(), null);
        }

        protected void CompleteTask(Task task, TaskContinuationOptions options, Action<Task> action)
        {
            task.ContinueWith(delegate
            {
                action(task);
                task.Dispose();
            }, CancellationToken.None, options, this.UiSyncContext);
        }
    }
}