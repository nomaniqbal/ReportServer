﻿using Autofac;
using AutoMapper;
using NCrontab;
using Newtonsoft.Json;
using ReportService.Interfaces;
using ReportService.Nancy;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Autofac.Core;
using ReportService.Extensions;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Monik.Common;

namespace ReportService.Core
{
    public class Logic : ILogic
    {
        private readonly ILifetimeScope autofac;
        private readonly IMapper mapper;
        private readonly IMonik monik;
        private readonly IArchiver archiver;
        private readonly ITelegramBotClient bot;
        private readonly IRepository repository;
        private readonly Scheduler checkScheduleAndExecuteScheduler;
        private readonly IViewExecutor tableView;

        private readonly List<DtoOper> operations;
        private readonly List<DtoRecepientGroup> recepientGroups;
        private readonly List<DtoTelegramChannel> telegramChannels;
        private readonly List<DtoSchedule> schedules;
        private readonly List<IRTask> tasks;
        private readonly List<DtoTaskOper> taskOpers;

        public Dictionary<string, Type> RegisteredExporters { get; set; }
        public Dictionary<string, Type> RegisteredImporters { get; set; }

        public Logic(ILifetimeScope autofac, IRepository repository, IMonik monik,
                     IMapper mapper, IArchiver archiver, ITelegramBotClient bot)
        {
            this.autofac = autofac;
            this.mapper = mapper;
            this.monik = monik;
            this.archiver = archiver;
            this.bot = bot;
            this.repository = repository;

            checkScheduleAndExecuteScheduler =
                new Scheduler {Period = 60, TaskMethod = CheckScheduleAndExecute};

            tableView = this.autofac.ResolveNamed<IViewExecutor>("CommonTableViewEx");

            operations = new List<DtoOper>();
            recepientGroups = new List<DtoRecepientGroup>();
            telegramChannels = new List<DtoTelegramChannel>();
            schedules = new List<DtoSchedule>();
            tasks = new List<IRTask>();
            taskOpers = new List<DtoTaskOper>();

            this.bot.OnUpdate += OnBotUpd;
        } //ctor

        private void UpdateDtoEntitiesList<T>(List<T> list) where T : IDtoEntity, new()
        {
            var repositoryList = repository.GetListEntitiesByDtoType<T>();
            if (repositoryList == null) return;
            lock (this)
            {
                list.Clear();
                foreach (var entity in repositoryList)
                    list.Add(entity);
            }
        }

        private void UpdateTaskList()
        {
            try
            {
                var taskList = repository.GetListEntitiesByDtoType<DtoTask>();
                if (taskList == null) return;
                lock (this)
                {
                    tasks.Clear();

                    foreach (var dtoTask in taskList)
                    {
                        var task = autofac.Resolve<IRTask>(
                            new NamedParameter("id", dtoTask.Id),
                            new NamedParameter("name", dtoTask.Name),
                            new NamedParameter("schedule", schedules
                                .FirstOrDefault(s => s.Id == dtoTask.ScheduleId)),
                            new NamedParameter("opers",
                                taskOpers
                                    .Where(taskOper => taskOper.TaskId == dtoTask.Id)
                                    .Select(taskOper => Tuple.Create
                                    (operations.FirstOrDefault(oper => oper.Id == taskOper.OperId),
                                        taskOper.Number, taskOper.IsDefault))
                                    .ToList()));

                        // might be replaced with saved time from db
                        task.UpdateLastTime();
                        tasks.Add(task);
                    }
                } //lock
            }
            catch (Exception e)
            {
                var msg = $"Error while updating tasks: {e.Message}";
                monik.ApplicationError(msg);
                Console.WriteLine(msg);
            }
        }

        private void CheckScheduleAndExecute()
        {
            List<IRTask> currentTasks;
            lock (this)
                currentTasks = tasks.ToList();

            CultureInfo.CurrentCulture = new CultureInfo("en-US");

            foreach (var task in currentTasks.Where(x => x.Schedule != null))
            {
                string[] cronStrings =
                    schedules.First(s => s.Id == task.Schedule.Id).Schedule.Split(';');

                foreach (var cronString in cronStrings)
                {
                    var cronSchedule = CrontabSchedule.TryParse(cronString);

                    if (cronSchedule == null) continue;

                    var occurrences =
                        cronSchedule.GetNextOccurrences(task.LastTime, DateTime.Now);
                    if (!occurrences.Any()) continue;

                    ExecuteTask(task);
                    break;
                }
            }
        }

        private void ExecuteTask(IRTask task)
        {
            task.UpdateLastTime();

            SendServiceInfo($"Отсылка отчёта {task.Id} по расписанию");

            Task.Factory.StartNew(() => task.Execute());
        }

        //private void CreateBase(string connStr)
        //{
        //    repository.CreateBase(connStr);
        //}

        public void Start()
        {
            //CreateBase(ConfigurationManager.AppSettings["DBConnStr"]);
            RegisteredImporters = autofac
                .ComponentRegistry
                .Registrations
                .Where(r => typeof(IDataImporter)
                    .IsAssignableFrom(r.Activator.LimitType))
                .Select(r =>
                    new KeyValuePair<string, Type>(
                        (r.Services.ToList().First() as KeyedService)?.ServiceKey.ToString(),
                        (r.Services.ToList().Last() as KeyedService)?.ServiceKey as Type)
                ).ToDictionary(pair => pair.Key, pair => pair.Value);

            RegisteredExporters = autofac
                .ComponentRegistry
                .Registrations
                .Where(r => typeof(IDataExporter)
                    .IsAssignableFrom(r.Activator.LimitType))
                .Select(r =>
                    new KeyValuePair<string, Type>(
                        (r.Services.ToList().First() as KeyedService)?.ServiceKey.ToString(),
                        (r.Services.ToList().Last() as KeyedService)?.ServiceKey as Type)
                ).ToDictionary(pair => pair.Key, pair => pair.Value);

            UpdateDtoEntitiesList(operations);
            UpdateDtoEntitiesList(recepientGroups);
            UpdateDtoEntitiesList(telegramChannels);
            UpdateDtoEntitiesList(schedules);
            UpdateDtoEntitiesList(taskOpers);

            UpdateTaskList();

            checkScheduleAndExecuteScheduler.OnStart();
        }

        public void Stop()
        {
            checkScheduleAndExecuteScheduler.OnStop();
        }

        public string SendDefault(int taskId, string mailAddress)
        {
            List<IRTask> currentTasks;

            lock (this)
                currentTasks = tasks.ToList();

            var task = currentTasks.FirstOrDefault(t => t.Id == taskId);

            if (task == null) return "No tasks with such Id found..";

            SendServiceInfo($"Sending default dataset of task {task.Id} to address" +
                     $" {mailAddress} (launched manually)");

            Task.Factory.StartNew(() => task.SendDefault(mailAddress));
            return $"Task {taskId} default dataset sent to {mailAddress}!";
        }

        public string ForceExecute(int taskId)
        {
            List<IRTask> currentTasks;

            lock (this)
                currentTasks = tasks.ToList();

            var task = currentTasks.FirstOrDefault(t => t.Id == taskId);

            if (task == null) return "No tasks with such Id found..";

            SendServiceInfo( $"Executing task {task.Id} (launched manually)");

            Task.Factory.StartNew(() => task.Execute());
            return $"Task {taskId} executed!";
        }

        #region getListJson

        public string GetAllOperationsJson()
        {
            List<DtoOper> currentOpers;
            lock (this)
                currentOpers = operations.ToList();

            return JsonConvert.SerializeObject(currentOpers);
        }

        public string GetAllRecepientGroupsJson()
        {
            List<DtoRecepientGroup> currentRecepients;
            lock (this)
                currentRecepients = recepientGroups.ToList();

            return JsonConvert.SerializeObject(currentRecepients);
        }

        public string GetAllTelegramChannelsJson()
        {
            List<DtoTelegramChannel> currentChannels;
            lock (this)
                currentChannels = telegramChannels.ToList();

            return JsonConvert.SerializeObject(currentChannels);
        }

        public string GetAllSchedulesJson()
        {
            List<DtoSchedule> currentSchedules;
            lock (this)
                currentSchedules = schedules.ToList();

            return JsonConvert.SerializeObject(currentSchedules);
        }

        public string GetAllTaskOpersJson()
        {
            List<DtoTaskOper> currentTaskOpers;
            lock (this)
                currentTaskOpers = taskOpers.ToList();

            return JsonConvert.SerializeObject(currentTaskOpers);
        }

        public string GetAllTasksJson()
        {
            List<IRTask> currentTasks;
            lock (this)
                currentTasks = tasks.ToList();
            var tr = JsonConvert.SerializeObject(currentTasks
                .Select(t => mapper.Map<DtoTask>(t)));
            return tr;
        }

        public string GetInWorkEntitiesJson()
        {
            var entities = new Dictionary<string, int>
            {
                {"operations", operations.Count},
                {"recepientGroups", recepientGroups.Count},
                {"telegramChannels", telegramChannels.Count},
                {"schedules", schedules.Count},
                {"tasks", tasks.Count},
                {"taskOpers", taskOpers.Count}
            };
            return JsonConvert.SerializeObject(entities);
        }

        //public string GetEntitiesListJsonByType<T>()
        //{
        //    var list = GetType().GetFields(
        //            BindingFlags.NonPublic |
        //            BindingFlags.Instance)
        //        .FirstOrDefault(field => field.FieldType == typeof(List<T>))?
        //        .GetValue(this);
        //    return JsonConvert.SerializeObject(list);
        //}

        #endregion

        public int CreateOperation(DtoOper oper)
        {
            var newExporterId = repository.CreateEntity(oper);
            UpdateDtoEntitiesList(operations);

            SendServiceInfo($"Created operation {newExporterId}");

            return newExporterId;
        }

        public void UpdateOperation(DtoOper oper)
        {
            repository.UpdateEntity(oper);
            UpdateDtoEntitiesList(operations);
            UpdateTaskList();

            SendServiceInfo($"Changed operation {oper.Id}");
        }

        public void DeleteOperation(int id)
        {
            repository.DeleteEntity<DtoOper>(id);
            UpdateDtoEntitiesList(operations);

            SendServiceInfo($"Deleted operation {id}");
        }

        public int CreateRecepientGroup(DtoRecepientGroup group)
        {
            var newGroupId = repository.CreateEntity(group);
            UpdateDtoEntitiesList(recepientGroups);

            SendServiceInfo($"Created recepient group {newGroupId}");

            return newGroupId;
        }

        public void UpdateRecepientGroup(DtoRecepientGroup group)
        {
            repository.UpdateEntity(group);
            UpdateDtoEntitiesList(recepientGroups);
            UpdateTaskList();
            SendServiceInfo($"Changed recepient group {group.Id}");
        }

        public void DeleteRecepientGroup(int id)
        {
            repository.DeleteEntity<DtoRecepientGroup>(id);
            UpdateDtoEntitiesList(recepientGroups);

            SendServiceInfo($"Deleted recepient group {id}");
        }

        public RecepientAddresses GetRecepientAddressesByGroupId(int groupId)
        {
            return mapper.Map<RRecepientGroup>(recepientGroups
                .FirstOrDefault(group => group.Id == groupId)).GetAddresses();
        }

        public int CreateTelegramChannel(DtoTelegramChannel channel)
        {
            var newChannelId = repository.CreateEntity(channel);
            UpdateDtoEntitiesList(recepientGroups);
            SendServiceInfo($"Created telegram channel {newChannelId}");
            return newChannelId;
        }

        public void UpdateTelegramChannel(DtoTelegramChannel channel)
        {
            repository.UpdateEntity(channel);
            UpdateDtoEntitiesList(recepientGroups);
            UpdateTaskList();
            SendServiceInfo($"Changed telegram channel  {channel.Id}");
        }

        public DtoTelegramChannel GetTelegramChatIdByChannelId(int id)
        {
            return telegramChannels
                .FirstOrDefault(channel => channel.Id == id);
        }

        public int CreateSchedule(DtoSchedule schedule)
        {
            var newScheduleId = repository.CreateEntity(schedule);
            UpdateDtoEntitiesList(schedules);
            SendServiceInfo($"Created schedule {newScheduleId}");
            return newScheduleId;
        }

        public void UpdateSchedule(DtoSchedule schedule)
        {
            repository.UpdateEntity(schedule);
            UpdateDtoEntitiesList(schedules);

            SendServiceInfo($"Changed schedule {schedule.Id}");
        }

        public void DeleteSchedule(int id)
        {
            repository.DeleteEntity<DtoSchedule>(id);
            UpdateDtoEntitiesList(schedules);
            SendServiceInfo($"Deleted schedule {id}");
        }

        public int CreateTaskOper(DtoTaskOper taskOper)
        {
            var newtaskOperId = repository.CreateEntity(taskOper);
            UpdateDtoEntitiesList(operations);
            UpdateTaskList();
            SendServiceInfo(
                $"Operation {taskOper.OperId} added to task {taskOper.TaskId}");
            return newtaskOperId;
        }

        public int CreateTask(ApiTask task)
        {
            var newTaskId = repository.CreateTask(mapper.Map<DtoTask>(task), task.BindedOpers);
            UpdateDtoEntitiesList(taskOpers);
            UpdateTaskList();
            SendServiceInfo($"Created task {newTaskId}");
            return newTaskId;
        }

        public void UpdateTask(ApiTask task)
        {
            repository.UpdateTask(mapper.Map<DtoTask>(task), task.BindedOpers);
            UpdateDtoEntitiesList(taskOpers);
            UpdateTaskList();
            SendServiceInfo($"Changed task {task.Id}");
        }

        public void DeleteTask(int taskId)
        {
            repository.DeleteEntity<DtoTask>(taskId);
            UpdateDtoEntitiesList(taskOpers);
            UpdateTaskList();
            SendServiceInfo($"Deleted task {taskId}");
        }

        public async Task<string> GetTaskList_HtmlPage()
        {
            List<IRTask> currentTasks;
            lock (this)
                currentTasks = tasks.ToList();

            var tasksData = currentTasks.Select(task => new
            {
                task.Id,
                task.Name,
                task.Schedule?.Schedule,
                Operations = string.Join("=>", task.Operations.Select(oper => oper.Name))
            }).ToList();

            var jsonTasks = JsonConvert.SerializeObject(tasksData);
            return await Task.Factory.StartNew(() =>
                tableView.ExecuteHtml("Current tasks list", jsonTasks));
        }

        public async Task<string> GetCurrentViewByTaskId(int taskId)
        {
            List<IRTask> currentTasks;
            lock (this)
                currentTasks = tasks.ToList();

            var task = currentTasks.FirstOrDefault(t => t.Id == taskId);

            if (task == null) return "No tasks with such Id found..";
            return await task.GetCurrentView();
        }

        public void DeleteTaskInstanceById(int id)
        {
            repository.DeleteEntity<DtoTaskInstance>(id);
            UpdateTaskList();
            SendServiceInfo($"Deleted task instance {id}");
        }

        public string GetAllTaskInstancesJson()
        {
            return JsonConvert.SerializeObject(
                repository.GetListEntitiesByDtoType<DtoTaskInstance>());
        }

        public string GetAllTaskInstancesByTaskIdJson(int taskId)
        {
            return JsonConvert.SerializeObject(repository.GetInstancesByTaskId(taskId));
        }

        public async Task<string> GetFullInstanceList_HtmlPage(
            int taskId)
        {
            var instances = repository.GetInstancesByTaskId(taskId)
                .Select(instance => new
                {
                    instance.Id,
                    instance.StartTime,
                    instance.Duration,
                    State = ((InstanceState) instance.State).ToString()
                });

            var jsonInstances = JsonConvert.SerializeObject(instances);
            return await Task.Factory.StartNew(() =>
                tableView.ExecuteHtml("История выполнения задачи", jsonInstances));
        }

        public void DeleteOperInstanceById(int operInstanceId)
        {
            repository.DeleteEntity<DtoOperInstance>(operInstanceId);
        }

        public string GetOperInstancesByTaskInstanceIdJson(int id)
        {
            return JsonConvert.SerializeObject(repository
                .GetOperInstancesByTaskInstanceId(id));
        }

        public string GetFullOperInstanceByIdJson(int id)
        {
            var instance = repository.GetFullOperInstanceById(id);
            var apiInstance = mapper.Map<ApiOperInstance>(instance);

            apiInstance.DataSet = archiver.ExtractFromByteArchive(instance.DataSet);

            return JsonConvert.SerializeObject(apiInstance);
        }

        public string GetAllRegisteredImporters()
        {
            return JsonConvert.SerializeObject(RegisteredImporters
                .ToDictionary(pair => pair.Key, pair => pair.Value.Name));
        }

        public string GetAllRegisteredExporters()
        {
            return JsonConvert.SerializeObject(RegisteredExporters
                .ToDictionary(pair => pair.Key, pair => pair.Value.Name));
        }

        private void OnBotUpd(object sender, Telegram.Bot.Args.UpdateEventArgs e)
        {
            long chatId = 0;
            string chatName = "";
            ChatType chatType = ChatType.Private;

            UpdateType updType = e.Update.Type;

            switch (updType)
            {
                case UpdateType.ChannelPost:
                    chatId = e.Update.ChannelPost.Chat.Id;
                    chatName = e.Update.ChannelPost.Chat.Title;
                    chatType = ChatType.Channel;
                    break;
                case UpdateType.Message:
                    chatType = e.Update.Message.Chat.Type;
                    chatId = e.Update.Message.Chat.Id;
                    switch (chatType)
                    {
                        case ChatType.Private:
                            chatName =
                                $"{e.Update.Message.Chat.FirstName} {e.Update.Message.Chat.LastName}";
                            break;

                        case ChatType.Group:
                            chatName = e.Update.Message.Chat.Title;
                            break;
                    }

                    break;
            }

            if (chatId != 0 && !telegramChannels.Select(channel => channel.ChatId).Contains(chatId))
            {
                DtoTelegramChannel channel =
                    new DtoTelegramChannel
                    {
                        ChatId = chatId,
                        Name = string.IsNullOrEmpty(chatName) ? "NoName" : chatName,
                        Type = (int) chatType
                    };

                channel.Id = repository.CreateEntity(channel);
                UpdateDtoEntitiesList(telegramChannels);
            }
        }

        private void SendServiceInfo(string msg)
        {
            monik.ApplicationInfo(msg);
            Console.WriteLine(msg);
        }
    } //class
}