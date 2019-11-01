﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ReportService.Entities;
using ReportService.Entities.Dto;
using ReportService.Interfaces.Operations;
using ReportService.ReportTask;

namespace ReportService.Interfaces.ReportTask
{
    public interface IReportTask
    {
        int Id { get; }
        string Name { get; }
        DtoSchedule Schedule { get; }
        DateTime LastTime { get; }
        List<IOperation> Operations { get; set; }
        Dictionary<string, object> Parameters { get; set; }
        List<TaskDependency> DependsOn { get; set; }

        IReportTaskRunContext GetCurrentContext(bool takeDefault);
        void Execute(IReportTaskRunContext context);
        void UpdateLastTime();
        Task<string> GetCurrentViewAsync(IReportTaskRunContext context);
        void SendDefault(IReportTaskRunContext context, string mailAddress);
    }
}