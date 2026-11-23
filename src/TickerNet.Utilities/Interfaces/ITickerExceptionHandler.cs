using System;
using System.Threading.Tasks;
using TickerNet.Utilities.Enums;

namespace TickerNet.Utilities.Interfaces
{
    public interface ITickerExceptionHandler
    {
        public Task HandleExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType);
        public Task HandleCanceledExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType);
    }
}
