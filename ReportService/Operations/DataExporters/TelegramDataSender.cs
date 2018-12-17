﻿using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using ReportService.Interfaces.Core;
using ReportService.Interfaces.ReportTask;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace ReportService.Operations.DataExporters
{
    public class TelegramDataSender : IOperation
    {
        public CommonOperationProperties Properties { get; set; } = new CommonOperationProperties();
        public string PackageName { get; set; }

        public bool RunIfVoidPackage { get; set; }

        private readonly ITelegramBotClient bot;
        private readonly DtoTelegramChannel channel;
        private readonly IViewExecutor viewExecutor;
        public string ReportName;

        public TelegramDataSender(IMapper mapper, ITelegramBotClient botClient,
            ILifetimeScope autofac,
            ILogic logic, TelegramExporterConfig config)
        {
            mapper.Map(config, this);
            mapper.Map(config, Properties);

            channel = logic.GetTelegramChatIdByChannelId(config.TelegramChannelId);
            viewExecutor = autofac.ResolveNamed<IViewExecutor>("commonviewex");
            bot = botClient;
        }
        
        public Task ExecuteAsync(IRTaskRunContext taskContext)
        {
            throw new System.NotImplementedException();
        }

        public void Execute(IRTaskRunContext taskContext)
        {
            var package = taskContext.Packages[PackageName];
            if (!RunIfVoidPackage && package.DataSets.Count == 0)
                return;

            bot.SendTextMessageAsync(channel.ChatId,
                    viewExecutor.ExecuteTelegramView(package, ReportName),
                    ParseMode.Markdown)
                .Wait();
        }
    }
}