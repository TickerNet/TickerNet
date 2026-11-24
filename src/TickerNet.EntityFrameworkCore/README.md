# TickerNet.EntityFrameworkCore

Entity Framework Core persistence for [TickerNet](https://github.com/TickerNet/TickerNet), a .NET background job scheduler. Stores time and cron jobs, their occurrences and execution state in a relational database.

## Install

```bash
dotnet add package TickerNet.EntityFrameworkCore
```

This is one of TickerNet's operational stores. Use it or `TickerNet.MongoDB`, not both. See the [getting started guide](https://tickernet.github.io/TickerNet/getting-started.html) for the rest of the setup.

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
            db.UseSqlite("Data Source=tickernet.db"));
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

`UseModelCustomizer` adds TickerNet's three entities to your model for you. With `IgnoreModelCustomizer`, apply them yourself in `OnModelCreating` using the public `TimeTickerConfigurations<T>`, `CronTickerConfigurations<T>` and `CronTickerOccurrenceConfigurations<T>` classes from `TickerNet.EntityFrameworkCore.Configurations`.

## Options and schema

- `SetSchema("jobs")` changes the schema from the default `ticker`. With `UseTickerNetDbContext` you can also pass the schema as its `schema` argument.
- `SetDbContextPoolSize(256)` changes the pooled context factory's size. The default is 1024.
- The tables are `TimeTickers`, `CronTickers` and `CronTickerOccurrences`.

```csharp
options.AddOperationalStore(ef =>
{
    ef.SetSchema("jobs");
    ef.SetDbContextPoolSize(256);
    ef.UseTickerNetDbContext<TickerNetDbContext>(db =>
        db.UseSqlite("Data Source=tickernet.db"));
});
```

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

With the TickerNet-owned context, resolve `TickerNetDbContext` from the scope instead.
