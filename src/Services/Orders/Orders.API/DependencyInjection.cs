using System.Text.Json.Serialization;
using Blocks.AspNetCore;
using Blocks.Core;
using Blocks.Core.Context;
using Blocks.Core.Security;
using Blocks.Messaging;
using EmailService.Empty;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.OpenApi;

namespace Orders.API;

public static class DependencyInjection
{
    public static void ConfigureApiOptions(this IServiceCollection services, IConfiguration config)
    {
        services
            .AddAndValidateOptions<RabbitMqOptions>(config)          // section name = class name; fails at startup if missing
            .AddAndValidateOptions<JwtOptions>(config)
            .Configure<JsonOptions>(opt =>
            {
                opt.SerializerOptions.PropertyNameCaseInsensitive = true;
                opt.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
    }

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration config)
    {
        services
            .AddMemoryCache()                                        // reference-data cache (ICacheable)
            .AddHttpContextAccessor()
            .AddEndpointsApiExplorer()
            .AddSwaggerGen(ConfigureSwagger)
            .AddJwtAuthentication(config)                            // tokens: see tools/Starter.DevToken
            .AddAuthorization();

        //insight - interface segregation: one implementation, inner layers depend only on the narrow capability they need
        services
            .AddScoped<IClaimsProvider, HttpContextProvider>()
            .AddScoped<IRouteProvider, HttpContextProvider>()
            .AddScoped<HttpContextProvider>();

        services.AddScoped<RequestContext>();

        // authorization layer 2: role on THIS aggregate (see OrderAccessChecker)
        services.AddScoped<IAuthorizationHandler, AggregateAccessAuthorizationHandler>();

        // modules: one visible provider line; swap for AddSmtpEmailService (EmailService.Smtp) when an SMTP server is configured
        services.AddEmptyEmailService(config);

        return services;
    }

    private static void ConfigureSwagger(Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions options)
    {
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste a token from: dotnet run --project tools/Starter.DevToken -- --roles CUSTOMER"
        });
        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
    }
}
