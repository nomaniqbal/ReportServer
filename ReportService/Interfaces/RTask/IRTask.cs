﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReportService.Extensions;
using ReportService.Interfaces.Core;

namespace ReportService.Interfaces.RTask
{
    public enum RReportType : byte
    {
        Common = 1,
        Custom = 2
    }

    public enum InstanceState
    {
        InProcess = 1,
        Success = 2,
        Failed = 3
    }

    public class RRecepientGroup
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Addresses { get; set; }
        public string AddressesBcc { get; set; }

        public RecepientAddresses GetAddresses()
        {
            return new RecepientAddresses
            {
                To = Addresses.Split(new[] {';'},
                    StringSplitOptions.RemoveEmptyEntries).ToList(),
                Bcc = AddressesBcc?.Split(new[] {';'},
                    StringSplitOptions.RemoveEmptyEntries).ToList()
            };
        }
    }
    
    public interface IRTask
    {
        int Id { get; }
        string Name { get; }
        DtoSchedule Schedule { get; }
        DateTime LastTime { get; }
        List<IOperation> Operations { get; set; }

        void Execute();
        void UpdateLastTime();
        Task<string> GetCurrentView();
        void SendDefault(string mailAddress);
    }
}