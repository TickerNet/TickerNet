using System.Text.Json.Serialization;
using TickerNet.Utilities.Entities.BaseEntity;

namespace TickerNet.Utilities.Entities
{
    public class CronTickerEntity : BaseTickerEntity
    {
        public virtual string Expression { get; set; }
        public virtual byte[] Request { get; set; }
        public virtual int Retries { get; set; }
        public virtual int[] RetryIntervals { get; set; }
        public virtual bool IsEnabled { get; set; } = true;

        /// <summary>
        /// System-managed pause flag. TickerNet.Hub sets it when no worker for the
        /// node in this cron's Function (<c>function@node</c>) is connected, and
        /// clears it when one connects. Distinct from <see cref="IsEnabled"/>
        /// (user-managed) so the dashboard can tell "user disabled this" from
        /// "paused while the worker node is offline". Polling skips a cron when
        /// this is true even if IsEnabled is true. The column has the same name
        /// TickerQ used, so existing databases need no change.
        /// </summary>
        public virtual bool IsSystemPaused { get; set; }
    }
}