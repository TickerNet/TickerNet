using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerNet.Caching.StackExchangeRedis.DependencyInjection;
using TickerNet.DependencyInjection;
using TickerNet.EntityFrameworkCore.Customizer;
using TickerNet.EntityFrameworkCore.DependencyInjection;
using TickerNet.Utilities.Entities;
using TickerNet.Utilities.Interfaces;

namespace TickerNet.Caching.StackExchangeRedis.Tests.DependencyInjection;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }
}

public class PersistenceProviderRegistrationTests
{
    [Fact]
    public void AddTickerNet_WithoutExternalProviders_RegistersInMemoryProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddTickerNet();

        // Assert
        var provider = services.BuildServiceProvider();
        var persistenceProvider = provider.GetService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        Assert.NotNull(persistenceProvider);
        Assert.Contains("InMemory", persistenceProvider.GetType().Name);
    }

    [Fact]
    public void AddTickerNet_WithRedis_RegistersRedisPersistenceProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddTickerNet(options =>
        {
            options.AddStackExchangeRedis(redis =>
            {
                redis.Configuration = "localhost:6379,abortConnect=false";
            });
        });

        // Assert — check service descriptor to avoid requiring a live Redis connection
        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddTickerNet_WithEfCore_RegistersEfCorePersistenceProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<TestDbContext>(options =>
        {
            options.UseSqlite("Data Source=PersistenceTest;Mode=Memory;Cache=Shared");
        });

        // Act
        services.AddTickerNet(options =>
        {
            options.AddOperationalStore(ef =>
            {
                ef.UseApplicationDbContext<TestDbContext>(ConfigurationType.UseModelCustomizer);
            });
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var persistenceProvider = provider.GetService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        Assert.NotNull(persistenceProvider);
        Assert.Contains("EfCore", persistenceProvider.GetType().Name);
    }

    [Fact]
    public void AddTickerNet_WithEfCoreAndRedis_EfCoreTakesPrecedence()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<TestDbContext>(options =>
        {
            options.UseSqlite("Data Source=PrecedenceTest;Mode=Memory;Cache=Shared");
        });

        // Act — EF Core registered first, then Redis
        services.AddTickerNet(options =>
        {
            options.AddOperationalStore(ef =>
            {
                ef.UseApplicationDbContext<TestDbContext>(ConfigurationType.UseModelCustomizer);
            });

            options.AddStackExchangeRedis(redis =>
            {
                redis.Configuration = "localhost:6379";
            });
        });

        // Assert — EF Core should win because it uses AddSingleton, Redis uses TryAddSingleton
        var provider = services.BuildServiceProvider();
        var persistenceProvider = provider.GetService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        Assert.NotNull(persistenceProvider);
        Assert.Contains("EfCore", persistenceProvider.GetType().Name);
    }

    [Fact]
    public void AddTickerNet_WithRedisAndEfCore_EfCoreTakesPrecedence()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<TestDbContext>(options =>
        {
            options.UseSqlite("Data Source=ReversePrecedenceTest;Mode=Memory;Cache=Shared");
        });

        // Act — Redis registered first, then EF Core (reversed order)
        services.AddTickerNet(options =>
        {
            options.AddStackExchangeRedis(redis =>
            {
                redis.Configuration = "localhost:6379";
            });

            options.AddOperationalStore(ef =>
            {
                ef.UseApplicationDbContext<TestDbContext>(ConfigurationType.UseModelCustomizer);
            });
        });

        // Assert — EF Core should still win regardless of registration order
        // because EF Core uses AddSingleton (replaces any prior) and Redis uses TryAddSingleton (skips if exists)
        var provider = services.BuildServiceProvider();
        var persistenceProvider = provider.GetService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        Assert.NotNull(persistenceProvider);
        Assert.Contains("EfCore", persistenceProvider.GetType().Name);
    }
}
