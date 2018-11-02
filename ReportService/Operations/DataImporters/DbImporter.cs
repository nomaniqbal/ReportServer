﻿using System.Collections.Generic;
using System.IO;
using AutoMapper;
using Gerakul.FastSql.Common;
using Gerakul.FastSql.SqlServer;
using Newtonsoft.Json;
using ReportService.Interfaces.Core;
using ReportService.Interfaces.ReportTask;
using ReportService.Protobuf;

namespace ReportService.Operations.DataImporters
{
    public class DbImporter : IDataImporter
    {
        public int Id { get; set; }
        public bool IsDefault { get; set; }
        public int Number { get; set; }
        public string Name { get; set; }
        public string DataSetName { get; set; }
        public string ConnectionString;
        public string Query;
        public int TimeOut;

        public DbImporter(IMapper mapper, DbImporterConfig config)
        {
            mapper.Map(config, this);
        }

        public void Execute(IRTaskRunContext taskContext)
        {
            var queryResult = new List<Dictionary<string, object>>();

            var sqlContext = SqlContextProvider.DefaultInstance
                .CreateContext(ConnectionString);
            sqlContext.UsingConnection(connectionContext =>
            {
                var opt = new QueryOptions(TimeOut);
                connectionContext
                    .CreateSimple(opt, $"{Query}")
                    .UseReader(reader =>
                    {
                        while (reader.Read())
                        {
                            var fields = new Dictionary<string, object>();

                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var name = string.IsNullOrEmpty(reader.GetName(i))
                                    ? $"UnnamedColumn{i}"
                                    : reader.GetName(i);
                                var val = reader[i];
                                //  queryres2[name].Add(val);
                                fields.Add(name, val);
                            }

                            queryResult.Add(fields);
                        }
                    });
            });

            string jsString = JsonConvert.SerializeObject(queryResult);
            // string jsString = JsonConvert.SerializeObject(queryres2,Formatting.Indented);
            taskContext.DataSets[DataSetName] = jsString;
        }
    }
}