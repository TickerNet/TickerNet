using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerNet.DependencyInjection;
using TickerNet.EntityFrameworkCore.Customizer;
using TickerNet.EntityFrameworkCore.DbContextFactory;
using TickerNet.EntityFrameworkCore.DependencyInjection;
using TickerNet.Utilities.Entities;

namespace TickerNet.EntityFrameworkCore.Tests.DependencyInjection;

// Test DbContext that will be used by TickerNet
public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}

public class CustomTickerDbContext : TickerNetDbContext<TimeTickerEntity, CronTickerEntity>
{
    public CustomTickerDbContext(DbContextOptions<CustomTickerDbContext> options) : base(options)
    {
    }
}

public class ServiceExtensionTests
{
    /// <summary>
    /// Uses WebApplicationFactory to create fake application with fake DbContext TestDbContext
    /// and adds TickerNet with AddOperationalStore and UseApplicationDbContext to the application.
    /// It needs to verify that DbContext is successfully used and TickerNet sets are accessible from it.
    /// </summary>
    [Fact]
    public void AddOperationalStore_UseApplicationDbContext_UsesDbContext()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddDbContext<TestDbContext>(options =>
        {
            options.UseSqlite("Data Source=ServiceExtensionTest;Mode=Memory;Cache=Shared");
        });

        builder.Services.AddTickerNet(tickerOptions =>
        {
            tickerOptions.AddOperationalStore(efOptions =>
            {
                efOptions.UseApplicationDbContext<TestDbContext>(ConfigurationType.UseModelCustomizer);
            });
        });

        var app = builder.Build();

        // Act
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // Assert
        var model = dbContext.Model;

        var timeTickerEntityType = model.FindEntityType(typeof(TimeTickerEntity));
        var cronTickerEntityType = model.FindEntityType(typeof(CronTickerEntity));
        var cronTickerOccurrenceEntityType = model.FindEntityType(typeof(CronTickerOccurrenceEntity<CronTickerEntity>));

        Assert.NotNull(timeTickerEntityType);
        Assert.NotNull(cronTickerEntityType);
        Assert.NotNull(cronTickerOccurrenceEntityType);

        // Additional verification - ensure we can query the sets (they should be queryable)
        var timeTickerSet = dbContext.Set<TimeTickerEntity>();
        var cronTickerSet = dbContext.Set<CronTickerEntity>();
        var cronTickerOccurrenceSet = dbContext.Set<CronTickerOccurrenceEntity<CronTickerEntity>>();

        Assert.NotNull(timeTickerSet);
        Assert.NotNull(cronTickerSet);
        Assert.NotNull(cronTickerOccurrenceSet);
    }

    /// <summary>
    /// `dotnet ef` finds a context through a registered DbContextOptions&lt;TContext&gt;.
    /// UseTickerNetDbContext must therefore register the options, not only the factory.
    /// </summary>
    [Fact]
    public void UseTickerNetDbContext_RegistersOptionsForDesignTimeTools()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTickerNet(tickerOptions =>
        {
            tickerOptions.AddOperationalStore(efOptions =>
            {
                efOptions.UseTickerNetDbContext<CustomTickerDbContext>(db =>
                    db.UseSqlite("Data Source=DesignTimeOptions;Mode=Memory;Cache=Shared"));
            });
        });

        var app = builder.Build();
        using var scope = app.Services.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CustomTickerDbContext>>();
        Assert.NotNull(options);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CustomTickerDbContext>>();
        using var context = factory.CreateDbContext();
        Assert.NotNull(context.Model.FindEntityType(typeof(TimeTickerEntity)));
    }
}
