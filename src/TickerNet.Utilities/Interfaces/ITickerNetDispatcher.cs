using System.Threading;
using System.Threading.Tasks;
using TickerNet.Utilities.Models;

namespace TickerNet.Utilities.Interfaces
{
    public interface ITickerNetDispatcher
    {
        /// <summary>
        /// Indicates whether the dispatcher is functional (background services enabled).
        /// When false, DispatchAsync will be a no-op.
        /// </summary>
        bool IsEnabled { get; }
        
        Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default);
    }
}

