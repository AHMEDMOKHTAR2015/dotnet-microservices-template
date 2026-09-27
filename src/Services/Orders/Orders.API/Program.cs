using Blocks.AspNetCore;
using Blocks.AspNetCore.Middleware;
using Blocks.AspNetCore.Middlewares;
using Blocks.EntityFrameworkCore;
using Orders.API;
using Orders.API.Endpoints;
using Orders.Application;
using Orders.Persistence;

var builder = WebApplication.CreateBuilder(args);

#region Add
builder.Services
    .ConfigureApiOptions(builder.Configuration);            // options first: later registrations rely on them

builder.Services
    .AddApiServices(builder.Configuration)                  // API / infrastructure
    .AddApplicationServices(builder.Configuration)          // use cases, pipeline, messaging
    .AddPersistenceServices(builder.Configuration);         // DbContext, repositories, interceptors
#endregion

var app = builder.Build();

#region InitData
//insight - migrating at startup is a development convenience; run migrations from your CI/CD pipeline in production
app.Migrate<OrdersDbContext>();
#endregion

#region Use
app
    .UseSwagger()
    .UseSwaggerUI()
    .UseMiddleware<GlobalExceptionMiddleware>()             // early: translates every exception below into a response
    .UseMiddleware<RequestContextMiddleware>()              // correlation id + logging scope
    .UseMiddleware<RequestDiagnosticsMiddleware>()          // timing, [PerfWarn]
    .UseRouting()
    .UseAuthentication()
    .UseAuthorization();

app.MapAllEndpoints();
#endregion

app.Run();
