using System;
using TickerNet.Utilities.Enums;

namespace TickerNet.Utilities.DashboardDtos
{
    public class CronOccurrenceQueryFilter
    {
        public TickerStatus[] Statuses { get; set; }
        public DateTime? ScheduledFrom { get; set; }
        public DateTime? ScheduledTo { get; set; }
        public string SortBy { get; set; }
        public bool SortDescending { get; set; } = true;
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
