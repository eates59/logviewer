﻿// Created by: egr
// Created at: 17.09.2013
// © 2012-2016 Alexander Egorov

using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using logviewer.logic.support;

namespace logviewer.logic.storage
{
    internal sealed class DatabaseConnection : IDisposable
    {
        private readonly SQLiteConnection connection;
        private SQLiteTransaction transaction;
        private readonly SynchronizationContext creationContext;
        private bool disposed;

        internal DatabaseConnection(string databaseFilePath)
        {
            this.DatabasePath = databaseFilePath;
            if (!File.Exists(this.DatabasePath) || new FileInfo(this.DatabasePath).Length == 0)
            {
                SQLiteConnection.CreateFile(this.DatabasePath);
                this.IsEmpty = true;
            }
            var conString = new SQLiteConnectionStringBuilder { DataSource = this.DatabasePath };
            this.creationContext = new SynchronizationContext();
            this.connection = new SQLiteConnection(conString.ToString());
            this.connection.Open();
        }

        ~DatabaseConnection()
        {
            this.DisposeInternal();
        }

        internal bool IsEmpty { get; private set; }
        internal string DatabasePath { get; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed",
            MessageId = "connection")]
        public void Dispose()
        {
            this.DisposeInternal();
            GC.SuppressFinalize(this);
        }

        private void DisposeInternal()
        {
            if (this.disposed)
            {
                return;
            }
            SafeRunner.Run(() => this.transaction?.Dispose());
            SafeRunner.Run(() => this.connection?.Close());
            SafeRunner.Run(() => this.connection?.Dispose());
            this.disposed = true;
        }

        internal void BeginTran()
        {
            this.ExecuteInCreationContext(o => this.transaction = this.connection.BeginTransaction());
        }

        internal void CommitTran()
        {
            this.ExecuteInCreationContext(o => this.transaction.Commit());
        }
        
        internal void RollbackTran()
        {
            this.ExecuteInCreationContext(o => this.transaction.Rollback());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2100:Review SQL queries for security vulnerabilities")]
        internal void RunSqlQuery(Action<IDbCommand> action, params string[] commands)
        {
            this.ExecuteInCreationContext(state =>
            {
                try
                {
                    // ReSharper disable once ForCanBeConvertedToForeach
                    for (var i = 0; i < commands.Length; i++)
                    {
                        if (this.disposed)
                        {
                            return;
                        }
                        using (var sqLiteCommand = this.connection.CreateCommand())
                        {
                            sqLiteCommand.CommandText = commands[i];
                            action(sqLiteCommand);
                        }
                    }
                }
                catch (SQLiteException e) when (HandleSqlException(e))
                {
                    Log.Instance.Debug(e);
                }
                catch (ObjectDisposedException e)
                {
                    Log.Instance.Debug(e);
                }
            });
        }

        private static bool HandleSqlException(SQLiteException e)
        {
            switch (e.ResultCode)
            {
                case SQLiteErrorCode.Abort:
                    return true;
                case SQLiteErrorCode.Misuse:
                    return true;
                default:
                    return false;
            }
        }

        internal void ExecuteReader(string query, Action<IDataReader> onRead, Action<IDbCommand> beforeRead = null, Func<bool> notCancelled = null)
        {
            this.RunSqlQuery(delegate(IDbCommand command)
            {
                beforeRead?.Invoke(command);
                var rdr = command.ExecuteReader();
                using (rdr)
                {
                    while (rdr.Read() && (notCancelled == null || notCancelled()))
                    {
                        onRead(rdr);
                    }
                }
            }, query);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecuteInCreationContext(SendOrPostCallback method)
        {
            this.creationContext.Send(method, null);
        }

        internal T ExecuteScalar<T>(string query, Action<IDbCommand> actionBeforeExecute = null)
        {
            var result = default(T);
            this.RunSqlQuery(delegate(IDbCommand cmd)
            {
                actionBeforeExecute?.Invoke(cmd);
                result = (T)cmd.ExecuteScalar();
            }, query);
            return result;
        }

        internal void ExecuteNonQuery(string query, Action<IDbCommand> actionBeforeExecute = null)
        {
            this.RunSqlQuery(delegate(IDbCommand command)
            {
                try
                {
                    actionBeforeExecute?.Invoke(command);
                    command.ExecuteNonQuery();
                }
                catch (Exception e)
                {
                    Log.Instance.Debug(e);
                }
            }, query);
        }

        internal void ExecuteNonQuery(params string[] queries)
        {
            this.RunSqlQuery(command => command.ExecuteNonQuery(), queries);
        }
    }
}