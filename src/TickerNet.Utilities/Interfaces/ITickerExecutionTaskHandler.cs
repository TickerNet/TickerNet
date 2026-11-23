using System.Threading;
using System.Threading.Tasks;
using TickerNet.Utilities.Models;

namespace TickerNet.Utilities.Interfaces
{
    public interface ITickerExecutionTaskHandler
    {
        Task ExecuteTaskAsync(InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default);
    }
}

