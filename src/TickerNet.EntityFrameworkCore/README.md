# TickerNet.EntityFrameworkCore

Entity Framework Core persistence for [TickerNet](https://github.com/TickerNet/TickerNet), a .NET background job scheduler. Stores time and cron jobs, their occurrences and execution state in a relational database.

## Install

```bash
dotnet add package TickerNet
dotnet add package TickerNet.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package Microsoft.EntityFrameworkCore.Design
```

`TickerNet.EntityFrameworkCore` does not bring in the core `TickerNet` package, which defines `AddTickerNet()` and `UseTickerNet()`. Add an EF Core provider for the database you use; the SQLite examples below need `Microsoft.EntityFrameworkCore.Sqlite`, and `Microsoft.EntityFrameworkCore.Design` is needed for migrations. This is one of TickerNet's operational stores. Use it or `TickerNet.MongoDB`, not both. See the [getting started guide](https://tickernet.github.io/TickerNet/getting-started.html) for the rest of the setup.

## Setup

`AddOperationalStore` lives in `TickerNet.EntityFrameworkCore.DependencyInjection`. Choose one of the two ways below.

### Use TickerNet's own DbContext

```csharp
using Microsoft.EntityFrameworkCore;
using TickerNet.DependencyInjection;
using TickerNet.EntityFrameworkCore.DbContextFactory;
using TickerNet.EntityFrameworkCore.DependencyInjection;

builder.Services.AddTickerNet(options =>
{
    options.AddOperationalStore(ef =>
    {
        ef.UseTickerNetDbContext<TickerNetDbContext>(db =>
            db.UseSqlite("Data Source=tickernet.db", b => b.MigrationsAssembly("MyApp")));
    });
});
```

`UseTickerNetDbContext<TDbContext>(optionsAction, schema = null)` registers a pooled context factory. You can optionally pass the schema as its second argument.

### Use your application's DbContext

```csharp
using Microsoft.EntityFrameworkCore;
using TickerNet.DependencyInjection;
using TickerNet.EntityFrameworkCore.Customizer;
using TickerNet.EntityFrameworkCore.DependencyInjection;

builder.Services.AddDbContext<AppDbContext>(db => db.UseSqlite("Data Source=app.db"));

builder.Services.AddTickerNet(options =>
{
    options.AddOperationalStore(ef =>
    {
        ef.UseApplicationDbContext<AppDbContext>(ConfigurationType.UseModelCustomizer);
    });
});
```

`UseModelCustomizer` adds TickerNet's three entities to your model for you. With `IgnoreModelCustomizer`, apply them yourself in `OnModelCreating` using the public `TimeTickerConfigurations<T>`, `CronTickerConfigurations<T>` and `CronTickerOccurrenceConfigurations<T>` classes from `TickerNet.EntityFrameworkCore.Configurations`. Those configuration classes default to the `ticker` schema and do not read `SetSchema`, so pass the schema to each constructor, for example `new TimeTickerConfigurations<TimeTickerEntity>("jobs")`. Only `TickerModelCustomizer` and `TickerNetDbContext.OnModelCreating` read the builder's schema.

## Options and schema

- `SetSchema("jobs")` changes the schema from the default `ticker` when using `UseModelCustomizer` or `UseTickerNetDbContext`. With `IgnoreModelCustomizer`, pass the schema to each configuration constructor as described above.
- `SetDbContextPoolSize(256)` changes the size of the pooled context factory that `UseTickerNetDbContext` registers. With `UseApplicationDbContext`, your own `AddDbContext` registration decides whether pooling is used and its size.
- The tables are `TimeTickers`, `CronTickers` and `CronTickerOccurrences`.

```csharp
options.AddOperationalStore(ef =>
{
    ef.SetSchema("jobs");
    ef.SetDbContextPoolSize(256);
    ef.UseTickerNetDbContext<TickerNetDbContext>(db =>
        db.UseSqlServer("Server=localhost;Database=tickernet;Trusted_Connection=True;", b => b.MigrationsAssembly("MyApp")));
});
```

This example uses SQL Server so `SetSchema("jobs")` has an effect; add `Microsoft.EntityFrameworkCore.SqlServer` for it. SQLite has no schemas, so EF ignores `SetSchema` and logs a warning with the SQLite provider.

## Migrations

TickerNet does not create the tables. Add an EF Core migration for the context you chose and apply it at startup, as the samples do:

```csharp
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseTickerNet();
```

With the TickerNet-owned context, resolve `TickerNetDbContext` from the scope instead. That context lives in the `TickerNet.EntityFrameworkCore` assembly, so set `MigrationsAssembly("MyApp")` in `UseSqlite`/`UseSqlServer` as shown above. Otherwise `dotnet ef migrations add Init --context TickerNetDbContext` looks in the package assembly and fails, and `Migrate()` finds no migrations to apply. With your own `AppDbContext`, the migrations already belong to your app.
