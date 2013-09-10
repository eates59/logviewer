﻿using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text.RegularExpressions;

namespace logviewer.core
{
    public sealed class LogStore : IDisposable
    {
        #region Constants and Fields

        private readonly string databasePath;
        private long index;
        private SQLiteTransaction transaction;
        private readonly SQLiteConnection connection;

        #endregion

        #region Constructors and Destructors

        public LogStore(string databaseFilePath = null)
        {
            databasePath = databaseFilePath ?? Path.GetTempFileName();
            SQLiteConnection.CreateFile(databasePath);
            var conString = new SQLiteConnectionStringBuilder { DataSource = databasePath };
            connection = new SQLiteConnection(conString.ToString());
            connection.Open();

            RunSqlQuery(command => command.ExecuteNonQuery(), @"
                        CREATE TABLE Log (
                                 Ix INTEGER PRIMARY KEY,
                                 Header TEXT  NOT NULL,
                                 Body  TEXT,
                                 Level INTEGER NOT NULL
                        );
                    ");
        }

        #endregion

        #region Public Methods and Operators

        public void StartAddMessages()
        {
            transaction = connection.BeginTransaction();
        }
        
        public void FinishAddMessages()
        {
            transaction.Commit();
        }

        public void AddMessage(LogMessage message)
        {
            const string Cmd = @"INSERT INTO Log(Ix, Header, Body, Level) VALUES (@Ix, @Header, @Body, @Level)";
            RunSqlQuery(delegate(SQLiteCommand command)
            {
                command.Parameters.AddWithValue("@Ix", index++);
                command.Parameters.AddWithValue("@Header", message.Header);
                command.Parameters.AddWithValue("@Body", message.Body);
                command.Parameters.AddWithValue("@Level", (int)message.Level);
                command.ExecuteNonQuery();
            }, Cmd);
        }

        public IEnumerable<LogMessage> ReadMessages(
            int limit,
            long offset = 0,
            bool reverse = true,
            LogLevel min = LogLevel.Trace,
            LogLevel max = LogLevel.Fatal,
            string filter = null)
        {
            var order = reverse ? "DESC" : "ASC";
            var result = new List<LogMessage>(limit);
            var filterClause = string.Empty;
            if (!string.IsNullOrWhiteSpace(filter))
            {
                filterClause = " AND (Header REGEXP @Fiter OR Body REGEXP @Fiter)";
            }
            const string Cmd =
                @"SELECT Header, Body, Level FROM Log WHERE Level >= @Min AND Level <= @Max {3} ORDER BY Ix {0} LIMIT {1} OFFSET {2}";
            var query = string.Format(Cmd, order, limit, offset, filterClause);
            this.RunSqlQuery(delegate(SQLiteCommand command)
            {
                command.Parameters.AddWithValue("@Min", (int)min);
                command.Parameters.AddWithValue("@Max", (int)max);
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    command.Parameters.AddWithValue("@Fiter", filter);
                }
                var rdr = command.ExecuteReader();
                using (rdr)
                {
                    while (rdr.Read())
                    {
                        var msg = new LogMessage(rdr[0] as string, rdr[1] as string, (LogLevel)((long)rdr[2]));
                        result.Add(msg);
                    }
                }
            }, query);

            return result;
        }
        
        public long CountMessages(
            LogLevel min = LogLevel.Trace,
            LogLevel max = LogLevel.Fatal,
            string filter = null)
        {
            var result = 0L;
            var filterClause = string.Empty;
            if (!string.IsNullOrWhiteSpace(filter))
            {
                filterClause = " AND (Header REGEXP @Fiter OR Body REGEXP @Fiter)";
            }
            const string Cmd =
                @"SELECT count(1) FROM Log WHERE Level >= @Min AND Level <= @Max {0}";
            var query = string.Format(Cmd, filterClause);
            this.RunSqlQuery(delegate(SQLiteCommand command)
            {
                command.Parameters.AddWithValue("@Min", (int)min);
                command.Parameters.AddWithValue("@Max", (int)max);
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    command.Parameters.AddWithValue("@Fiter", filter);
                }
                result = (long)command.ExecuteScalar();
                
            }, query);

            return result;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion

        #region Methods

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (transaction != null)
                {
                    transaction.Dispose();
                }
                if (connection != null)
                {
                    connection.Dispose();
                }
                if (File.Exists(databasePath))
                {
                    File.Delete(databasePath);
                }
            }
        }

        private void RunSqlQuery(Action<SQLiteCommand> action, params string[] commands)
        {
            foreach (var command in commands)
            {
                using (var sqLiteCommand = connection.CreateCommand())
                {
                    sqLiteCommand.CommandText = command;
                    action(sqLiteCommand);
                }
            }
        }

        #endregion
    }

    [SQLiteFunction(Name = "REGEXP", Arguments = 2, FuncType = FunctionType.Scalar)]
    class SqliteRegEx : SQLiteFunction
    {
        public override object Invoke(object[] args)
        {
            var input = Convert.ToString(args[1]);
            var pattern = Convert.ToString(args[0]);
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }
    }
}