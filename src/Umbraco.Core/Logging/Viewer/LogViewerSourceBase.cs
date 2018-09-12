﻿using System;
using System.Collections.Generic;
using System.Linq;
using Serilog.Events;
using Serilog.Filters.Expressions;
using Umbraco.Core.Models;
using Umbraco.Core.Persistence.DatabaseModelDefinitions;

namespace Umbraco.Core.Logging.Viewer
{
    public abstract class LogViewerSourceBase : ILogViewer
    {
        public abstract IEnumerable<LogEvent> GetAllLogs(DateTimeOffset startDate, DateTimeOffset endDate);
        
        public int GetNumberOfErrors(DateTimeOffset startDate, DateTimeOffset endDate)
        {
            var logs = GetAllLogs(startDate, endDate);
            return logs.Count(x => x.Level == LogEventLevel.Fatal || x.Level == LogEventLevel.Error || x.Exception != null);
        }

        public LogLevelCounts GetLogLevelCounts(DateTimeOffset startDate, DateTimeOffset endDate)
        {
            var logs = GetAllLogs(startDate, endDate);
            return new LogLevelCounts
            {
                Information = logs.Count(x => x.Level == LogEventLevel.Information),
                Debug = logs.Count(x => x.Level == LogEventLevel.Debug),
                Warning = logs.Count(x => x.Level == LogEventLevel.Warning),
                Error = logs.Count(x => x.Level == LogEventLevel.Error),
                Fatal = logs.Count(x => x.Level == LogEventLevel.Fatal)
            };
        }

        public IEnumerable<LogTemplate> GetMessageTemplates(DateTimeOffset startDate, DateTimeOffset endDate)
        {
            var logs = GetAllLogs(startDate, endDate);
            var templates = logs.GroupBy(x => x.MessageTemplate.Text)
                .Select(g => new LogTemplate { MessageTemplate = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count);

            return templates;
        }

        public PagedResult<LogMessage> GetLogs(DateTimeOffset startDate, DateTimeOffset endDate,
            int pageNumber = 1, int pageSize = 100,
            Direction orderDirection = Direction.Descending,
            string filterExpression = null,
            string[] logLevels = null)
        {
            //Get all logs into memory (Not sure this good or not)
            var logs = GetAllLogs(startDate, endDate);

            //If we have a filter expression, apply it
            if (string.IsNullOrEmpty(filterExpression) == false)
            {
                Func<LogEvent, bool> filter = null;
                
                // If the expression evaluates then make it into a filter
                if (FilterLanguage.TryCreateFilter(filterExpression, out Func<LogEvent, object> eval, out string error))
                {
                    filter = evt => true.Equals(eval(evt));
                }
                else // assume the expression was a search string and make a Like filter from that
                {
                    var filterSearch = $"@Message like '%{FilterLanguage.EscapeLikeExpressionContent(filterExpression)}%'";
                    if (FilterLanguage.TryCreateFilter(filterSearch, out eval, out error))
                    {
                        filter = evt => true.Equals(eval(evt));
                    }
                }
                
                if (filter != null)
                {
                    logs = logs.Where(filter);
                }
            }

            //This is user used the checkbox UI to toggle which log levels they wish to see
            //If an empty array - its implied all levels to be viewed
            if (logLevels.Length > 0)
            {
                var logsAfterLevelFilters = new List<LogEvent>();
                foreach (var level in logLevels)
                {
                    logsAfterLevelFilters.AddRange(logs.Where(x => x.Level.ToString() == level));
                }
                logs = logsAfterLevelFilters;
            }

            long totalRecords = logs.Count();
            long pageIndex = pageNumber - 1;

            //Order By, Skip, Take & Select
            IEnumerable<LogMessage> logMessages = logs
                .OrderBy(l => l.Timestamp, orderDirection)
                .Skip(pageSize * (pageNumber - 1))
                .Take(pageSize)
                .Select(x => new LogMessage
                {
                    Timestamp = x.Timestamp,
                    Level = x.Level,
                    MessageTemplateText = x.MessageTemplate.Text,
                    Exception = x.Exception?.ToString(),
                    Properties = x.Properties,
                    RenderedMessage = x.RenderMessage()
                });

            return new PagedResult<LogMessage>(totalRecords, pageNumber, pageSize)
            {
                Items = logMessages
            };
        }
    }
}
