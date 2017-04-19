﻿// This is an open source non-commercial project. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com
// Created by: egr
// Created at: 17.09.2013
// © 2012-2017 Alexander Egorov

using System;
using System.Collections.Generic;
using System.Data;

namespace logviewer.logic.storage
{
    internal sealed class StringCollectionStore : IDisposable, IStringCollectionStore
    {
        private const string ItemParameter = @"@Item";

        private const string UsedAtParameter = @"@UsedAt";

        private readonly string tableName;

        private readonly int maxItems;

        private readonly IDatabaseConnection connection;

        internal StringCollectionStore(ISettingsProvider settings, string tableName, int maxItems = 0)
        {
            this.tableName = tableName;
            this.maxItems = maxItems == 0 ? settings.KeepLastNFiles : maxItems;
            this.connection = new LocalDbConnection(settings.FullPathToDatabase);
            this.CreateTables();
        }

        private void CreateTables()
        {
            var createTable = $@"CREATE TABLE IF NOT EXISTS {this.tableName} (
                                 Item TEXT PRIMARY KEY,
                                 UsedAt INTEGER  NOT NULL
                        );";
            var createItemIndex = $"CREATE INDEX IF NOT EXISTS IX_Item ON {this.tableName} (Item)";
            this.connection.ExecuteNonQuery(createTable, createItemIndex);
        }

        public void Add(string item)
        {
            var result = this.connection.ExecuteScalar<long>($@"SELECT count(1) FROM {this.tableName} WHERE Item = {ItemParameter}",
                                                             cmd => cmd.AddParameter(ItemParameter, item));

            if (result > 0)
            {
                this.ExecuteChangeQuery(item, $"Update {this.tableName} SET UsedAt = {UsedAtParameter} WHERE Item = {ItemParameter}");
                return;
            }

            this.ExecuteChangeQuery(item, $@"INSERT INTO {this.tableName}(Item, UsedAt) VALUES ({ItemParameter}, {UsedAtParameter})");

            result = this.connection.ExecuteScalar<long>($"SELECT count(1) FROM {this.tableName}");

            if (result <= this.maxItems)
            {
                return;
            }

            const string deleteTemplate =
                    @"DELETE FROM {1} 
                    WHERE UsedAt IN (
                        SELECT UsedAt FROM {1} ORDER BY UsedAt ASC LIMIT {0}
                )";
            var cmdDelete = string.Format(deleteTemplate, result - this.maxItems, this.tableName);
            this.connection.ExecuteNonQuery(cmdDelete);
        }

        private void ExecuteChangeQuery(string item, string commandText)
        {
            void Action(IDbCommand command)
            {
                command.AddParameter(ItemParameter, item);
                command.AddParameter(UsedAtParameter, DateTime.Now.Ticks);
            }

            this.connection.ExecuteNonQuery(commandText, Action);
        }

        public void Remove(params string[] items)
        {
            var cmd = $@"DELETE FROM {this.tableName} WHERE Item = {ItemParameter}";

            this.connection.BeginTran();
            foreach (var file in items)
            {
                this.connection.ExecuteNonQuery(cmd, Remove(file));
            }

            this.connection.CommitTran();
        }

        private static Action<IDbCommand> Remove(string item)
        {
            return command => command.AddParameter(ItemParameter, item);
        }

        public IEnumerable<string> ReadItems()
        {
            var result = new List<string>(this.maxItems);
            this.connection.ExecuteReader($"SELECT Item FROM {this.tableName} ORDER BY UsedAt DESC",
                                          reader => result.Add(reader[0] as string));
            return result;
        }

        public string ReadLastUsedItem()
        {
            var result = string.Empty;
            this.connection.ExecuteReader($"SELECT Item FROM {this.tableName} ORDER BY UsedAt DESC LIMIT 1",
                                          reader => result = reader[0] as string);
            return result;
        }

        public void Dispose() => this.Dispose(true);

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.connection.Dispose();
            }
        }
    }
}