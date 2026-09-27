using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Orders.Persistence;

// Loads ICacheable reference tables once, so the state machine can read transitions synchronously.
public class DatabaseCacheLoader(IServiceProvider _serviceProvider) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        dbContext.GetAllCached<StageTransition>();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
