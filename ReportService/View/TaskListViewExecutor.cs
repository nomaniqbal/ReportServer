﻿using ReportService.Interfaces;

namespace ReportService.View
{
    public class TaskListViewExecutor : ViewExecutor, IViewExecutor
    {
        public override string Execute(string viewTemplate, string json)
        {
            return base.Execute(_tableTemplate, json);
        }

        private string _tableTemplate = @"<!DOCTYPE html>
<html>
<head>
    <title>Отчёт за неделю</title>
    <link rel=""stylesheet"" href=""https://maxcdn.bootstrapcdn.com/bootstrap/3.3.7/css/bootstrap.min.css"">
    <style>
        table {
            border-collapse: collapse;
            width: 100%;
        }

    th, td {
            border: 1px solid Black;
            padding: 10px;
        }
    </style>
</head>
<body>
    <h3 align=""center"">Текущий список задач</h3>
    <table class=""table table-bordered table-hover "">
        @foreach(var props in @Model.Content)
{
        <tr>
            @foreach(var prop in @props)
            {
             <td> @prop </td>
            }
        </tr>
        }
    </table>
</body>
</html>";
    }//class
}

