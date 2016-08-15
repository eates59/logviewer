// Created by: egr
// Created at: 04.10.2014
// � 2012-2016 Alexander Egorov

using System;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;

namespace logviewer.logic.ui
{
    public abstract class BaseGuiController
    {
        private readonly SynchronizationContext winformsOrDefaultContext;
        private readonly SynchronizationContextScheduler uiContextScheduler;

        protected BaseGuiController()
        {
            this.winformsOrDefaultContext = SynchronizationContext.Current ?? new SynchronizationContext();
            this.UiSyncContext = TaskScheduler.FromCurrentSynchronizationContext();
            this.uiContextScheduler = new SynchronizationContextScheduler(this.winformsOrDefaultContext);
        }

        protected TaskScheduler UiSyncContext { get; }

        protected SynchronizationContextScheduler UiContextScheduler => this.uiContextScheduler;

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