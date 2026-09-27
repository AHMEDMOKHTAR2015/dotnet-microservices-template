using System.Reflection;
using Blocks.Domain;
using Blocks.Mapster;
using Blocks.MediatR.Behaviours;
using Blocks.Messaging.MassTransit;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orders.Application.StateMachines;
using Starter.Security;

namespace Orders.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddMapsterConfigsFromCurrentAssembly()                                   // IRegister configs (scan)
            .AddValidatorsFromAssembly(Assembly.GetExecutingAssembly())               // FluentValidation (scan)
            .AddMediatR(config =>
            {
                config.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());

                config.AddOpenBehavior(typeof(AssignUserIdBehavior<,>));              // 1. who is acting (from the JWT)
                config.AddOpenBehavior(typeof(ValidationBehavior<,>));                // 2. is the input well-formed
                config.AddOpenBehavior(typeof(LoggingBehavior<,>));                   // 3. time the handler
            })
            .AddMassTransitWithRabbitMQ(configuration, Assembly.GetExecutingAssembly());

        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();            // Blocks.MediatR implementation
        services.AddScoped<IAggregateAccessChecker, OrderAccessChecker>();

        services.AddScoped<OrderStateMachineFactory>(provider => orderStage =>
            new OrderStateMachine(orderStage, provider.GetRequiredService<IMemoryCache>()));

        return services;
    }
}
