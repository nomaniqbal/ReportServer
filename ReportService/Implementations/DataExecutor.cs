﻿using System.Linq;
using Gerakul.FastSql;
using ReportService.Interfaces;

namespace ReportService.Models
{
    public class DataExecutor : IDataExecutor
    {
        private string connStr = @"";

        public DataExecutor()
        { }

        public string Execute(string query)
        {
            var data = SimpleCommand.ExecuteQuery(connStr, query).ToArray(); // TODO: test on DataBase. (?needs stored procs that will automatically give tables)
            return data.ToString(); // TODO: json serialize(?)
        }
    }
}
