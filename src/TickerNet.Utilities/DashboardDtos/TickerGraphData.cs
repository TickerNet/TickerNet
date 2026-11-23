using System;
using TickerNet.Utilities.Enums;

namespace TickerNet.Utilities.DashboardDtos
{
    public class TickerGraphData
    {
        public DateTime Date { get; set; }
        public Tuple<int, int>[] Results { get; set; }
    }
}