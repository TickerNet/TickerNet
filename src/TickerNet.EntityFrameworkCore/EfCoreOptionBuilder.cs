using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerNet.EntityFrameworkCore.Customizer;
using TickerNet.EntityFrameworkCore.DbContextFactory;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces.Managers;

namespace TickerNet.EntityFrameworkCore
{
    /// <summary>
    /// Configures the EF Core operational store: which context holds the jobs, its
    /// schema and its pool size.
    /// </summary>
    public class TickerNetEfCoreOptionBuilder<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        internal Action<IServiceCollection> ConfigureServices { get; set; }
        internal int PoolSize { get; set; } = 1024;
        internal string Schema { get; set; } = "ticker";

        /// <summary>
        /// Stores jobs in your own <typeparamref name="TDbContext"/>.
        /// </summary>
        /// <typeparam name="TDbContext">The application's <see cref="DbContext"/>.</typeparam>
        /// <param name="configurationType">
        /// <see cref="ConfigurationType.UseModelCustomizer"/> adds TickerNet's entities to
        /// your model; <see cref="ConfigurationType.IgnoreModelCustomizer"/> leaves that to
        /// your <c>OnModelCreating</c>.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        public TickerNetEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseApplicationDbContext<TDbContext>(ConfigurationType configurationType) where TDbContext : DbContext
        {
            ServiceBuilder.UseApplicationDbContext<TDbContext, TTimeTicker, TCronTicker>(this, configurationType);
            return this;
        }
        
        /// <summary>
        /// Uses a dedicated TickerNet context from a pooled context factory. The pool
        /// holds 1024 contexts unless <see cref="SetDbContextPoolSize"/> changes it.
        /// </summary>
        /// <typeparam name="TDbContext">A <see cref="TickerNetDbContext{TTimeTicker,TCronTicker}"/> subclass.</typeparam>
        /// <param name="optionsAction">Configures the context (provider and connection).</param>
        /// <param name="schema">Schema for the three tables; defaults to <c>ticker</c>.</param>
        /// <returns>The same builder, for chaining.</returns>
        public TickerNetEfCoreOptionBuilder<TTimeTicker, TCronTicker> UseTickerNetDbContext<TDbContext>(Action<DbContextOptionsBuilder> optionsAction, string schema = null) where TDbContext : TickerNetDbContext<TTimeTicker, TCronTicker>
        {
            Schema = schema ?? Schema;
            
            ServiceBuilder.UseTickerNetDbContext<TDbContext, TTimeTicker, TCronTicker>(this, optionsAction);
            return this;
        }

        /// <summary>
        /// Sets how many contexts the pooled factory keeps. Defaults to 1024.
        /// </summary>
        /// <param name="poolSize">Number of pooled contexts.</param>
        /// <returns>The same builder, for chaining.</returns>
        public TickerNetEfCoreOptionBuilder<TTimeTicker, TCronTicker> SetDbContextPoolSize(int poolSize)
        {
            PoolSize = poolSize;
            return this;
        }

        /// <summary>
        /// Sets the schema of the <c>TimeTickers</c>, <c>CronTickers</c> and
        /// <c>CronTickerOccurrences</c> tables. Defaults to <c>ticker</c>.
        /// </summary>
        /// <param name="schema">The database schema to use.</param>
        /// <returns>The same builder, for chaining.</returns>
        public TickerNetEfCoreOptionBuilder<TTimeTicker, TCronTicker> SetSchema(string schema)
        {
            Schema = schema;
            return this;
        }
    }
}
