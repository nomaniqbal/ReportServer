﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Monik.Common;
using Newtonsoft.Json;
using ReportService.Interfaces.Core;
using ReportService.Interfaces.ReportTask;

namespace ReportService.ReportTask
{
    public class RTask : IRTask
    {
        public int Id { get; }
        public string Name { get; }
        public DtoSchedule Schedule { get; }
        public DateTime LastTime { get; private set; }
        public List<IOperation> Operations { get; set; }
        public Dictionary<string, object> Parameters { get; set; }

        private readonly IMonik monik;
        private readonly ILifetimeScope autofac;
        private readonly IRepository repository;

        public RTask(ILogic logic, ILifetimeScope autofac, IRepository repository,
            IMonik monik, int id,
            string name, string parameters, DtoSchedule schedule, List<DtoOperation> opers)
        {
            this.monik = monik;
            this.repository = repository;
            Id = id;
            Name = name;
            Schedule = schedule;
            Operations = new List<IOperation>();

            Parameters = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(parameters))
                Parameters = JsonConvert
                    .DeserializeObject<Dictionary<string, object>>(parameters);

            foreach (var operation in opers)
            {
                IOperation newOper;

                var operType = operation.ImplementationType;

                if (logic.RegisteredImporters.ContainsKey(operType))
                {
                    newOper = autofac.ResolveNamed<IOperation>(operType,
                        new NamedParameter("config",
                            JsonConvert.DeserializeObject(operation.Config,
                                logic.RegisteredImporters[operType])));
                }

                else
                {
                    newOper = autofac.ResolveNamed<IOperation>(operType,
                        new NamedParameter("config",
                            JsonConvert.DeserializeObject(operation.Config,
                                logic.RegisteredExporters[operType])));
                }

                if (newOper == null) continue;

                newOper.Properties.Id = operation.Id;
                newOper.Properties.Number = operation.Number;
                newOper.Properties.Name = operation.Name;
                newOper.Properties.IsDefault = operation.IsDefault;

                Operations.Add(newOper);
            }

            this.autofac = autofac;
        } //ctor

        public IRTaskRunContext GetCurrentContext(bool isDefault)
        {
            var context = autofac.Resolve<IRTaskRunContext>();

            context.OpersToExecute = isDefault
                ? Operations.Where(oper => oper.Properties.IsDefault)
                    .OrderBy(oper => oper.Properties.Number).ToList()
                : Operations.OrderBy(oper => oper.Properties.Number).ToList();

            if (!context.OpersToExecute.Any())
            {
                var msg = $"Task {Id} did not executed (no operations found)";
                monik.ApplicationInfo(msg);
                Console.WriteLine(msg);
                return null;
            }

            context.Exporter = autofac.Resolve<IDefaultTaskExporter>();
            context.TaskId = Id;
            context.TaskName = Name; //can do it by NamedParameter+ctor,but..

            context.Parameters = Parameters
                .ToDictionary(pair => pair.Key, 
                    pair => repository.GetBaseQueryResult("select "+pair.Value.ToString()));

            context.CancelSource = new CancellationTokenSource();

            var dtoTaskInstance = new DtoTaskInstance
            {
                TaskId = Id,
                StartTime = DateTime.Now,
                Duration = 0,
                State = (int) InstanceState.InProcess
            };

            dtoTaskInstance.Id =
                repository.CreateEntity(dtoTaskInstance);

            context.TaskInstance = dtoTaskInstance;

            return context;
        }

        public void Execute(IRTaskRunContext context)
        {
            var taskWorker = autofac.Resolve<ITaskWorker>();
            taskWorker.RunOperations(context);
        }

        public async Task<string> GetCurrentView(IRTaskRunContext context)
        {
            var taskWorker = autofac.Resolve<ITaskWorker>();

            var defaultView =
                await taskWorker.RunOperationsAndGetLastView(context);

            return string.IsNullOrEmpty(defaultView)
                ? null
                : defaultView;
        }

        public void SendDefault(IRTaskRunContext context, string mailAddress)
        {
            var taskWorker = autofac.Resolve<ITaskWorker>();

            taskWorker.RunOperationsAndSendLastView(context, mailAddress);
        }

        public void UpdateLastTime()
        {
            LastTime = DateTime.Now;
        }
    } //class
}