﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using Autofac;
using Newtonsoft.Json;
using ReportService.Interfaces;

namespace ReportService.Core
{
    public class DefaultTaskWorker
    {
        private readonly IViewExecutor executor;

        public DefaultTaskWorker(ILifetimeScope autofac)
        {
            executor = autofac.ResolveNamed<IViewExecutor>("CommonTableViewEx");
        }

        public string GetDefaultView(string jsonSet, string reportName)
        {
            return executor.ExecuteHtml(reportName, jsonSet);
        }

        public void SendError(List<Tuple<Exception, string>> exceptions, string taskName)
        {
            using (var client = new SmtpClient(ConfigurationManager.AppSettings["SMTPServer"], 25))
            using (var msg = new MailMessage())
            {
                client.EnableSsl = true;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;

                msg.From = new MailAddress(ConfigurationManager.AppSettings["from"]);

                foreach (var addr in ConfigurationManager.AppSettings["administrativeaddress"]
                    .Split(';'))
                {
                    msg.To.Add(new MailAddress(addr));
                }

                msg.Subject = $"Errors occured in task {taskName} at" +
                              $" {DateTime.Now:dd.MM.yy HH:mm}";

                msg.IsBodyHtml = true;

                var errorsSet = exceptions.Select(pair => new Dictionary<string, object>
                {
                    {"Operation", pair.Item2},
                    {"Message", pair.Item1.Message}
                }).ToList();

                msg.Body =
                    executor.ExecuteHtml("Errors list", JsonConvert.SerializeObject(errorsSet));

                client.Send(msg);
            }
        } //method

        public void ForceSend(string dataSet, string taskName, string address)
        {
            using (var client = new SmtpClient(ConfigurationManager.AppSettings["SMTPServer"], 25))
            using (var msg = new MailMessage())
            {
                client.EnableSsl = true;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;

                msg.From = new MailAddress(ConfigurationManager.AppSettings["from"]);

                msg.To.Add(new MailAddress(address));

                msg.Subject = taskName + $" {DateTime.Now:dd.MM.yy}";

                msg.IsBodyHtml = true;

                msg.Body = executor.ExecuteHtml("taskName", dataSet);

                client.Send(msg);
            }
        } //method
    }
}