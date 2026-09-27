var builder = WebApplication.CreateBuilder(args);

// Routes and clusters are configuration only (appsettings.json → "ReverseProxy").
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Start the correlation chain at the edge; every service reads, logs and echoes X-Correlation-ID.
app.Use(async (context, next) =>
{
    const string header = "X-Correlation-ID";
    if (!context.Request.Headers.ContainsKey(header))
        context.Request.Headers[header] = Guid.NewGuid().ToString();

    await next();
});

//insight - authentication is enforced by each service (JWT + role/resource policies).
// Add it here too only if you need to reject anonymous traffic at the edge.
app.MapReverseProxy();

app.Run();
