using System.Data.Common;
using Blocks.EntityFrameworkCore.Interceptors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orders.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistenceServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionStringOrThrow("Database");

        // post-save dispatch: domain-event handlers run after the commit (switch to TransactionalDispatchDomainEventsInterceptor
        // + TransactionOptions only when handlers must write atomically with the trigger)
        services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

        // one DbConnection per scope, so any module DbContext can share the connection (and transaction)
        services.AddScoped<DbConnection>(_ => new SqlConnection(connectionString));
        services.AddDbContext<OrdersDbContext>((provider, options) =>
        {
            options.AddInterceptors(provider.GetServices<ISaveChangesInterceptor>());
            options.UseSqlServer(provider.GetRequiredService<DbConnection>());
        });

        services.AddScoped<TransactionProvider>();

        services.AddScoped(typeof(Repository<>));                 // simple entities
        services.AddDerivedTypesOf(typeof(Repository<>));          // every aggregate repository, automatically

        services.AddHostedService<DatabaseCacheLoader>();          // warm reference-data caches at startup

        return services;
    }
}
