using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// Honor proxy headers and trust ONLY your Palo Alto's public IP
builder.Services.Configure<ForwardedHeadersOptions>(opts =>
{
    opts.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // The IP your app sees as RemoteIpAddress (PA untrust public IP)
    opts.KnownProxies.Add(IPAddress.Parse("52.238.213.102"));

    // If you add an Azure LB VIP (HA), add it here too:
    // opts.KnownProxies.Add(IPAddress.Parse("<your-lb-public-ip>"));

    // Accept a reasonable XFF chain depth
    opts.ForwardLimit = 10;

    // (optional) Some hosting setups add both XFF and X-Forwarded-Proto multiple times
    // opts.RequireHeaderSymmetry = false;
});


// Add services to the container
builder.Services.AddRazorPages();

var app = builder.Build();

app.UseForwardedHeaders();

// Helper to pick the best client IP from headers
static string BestClientIp(HttpContext ctx)
{
    var xff = ctx.Request.Headers["X-Forwarded-For"].ToString();
    var xac = ctx.Request.Headers["X-Azure-ClientIP"].ToString();
    var cfc = ctx.Request.Headers["CF-Connecting-IP"].ToString();

    var firstXff = string.IsNullOrWhiteSpace(xff) ? null
        : xff.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

    return firstXff ?? xac ?? cfc ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "";
}

// Simple request logger (or move into your existing logging)
app.Use(async (ctx, next) =>
{
    var clientIp = BestClientIp(ctx);
    var xff = ctx.Request.Headers["X-Forwarded-For"].ToString();
    var xac = ctx.Request.Headers["X-Azure-ClientIP"].ToString();

    app.Logger.LogInformation("clientIp={clientIp} xff={xff} xAzureClientIp={xac} path={path}",
        clientIp, xff, xac, ctx.Request.Path);

    await next();
});

// Quick sanity endpoint
app.MapGet("/whoami", (HttpContext ctx) => Results.Json(new {
    bestClientIp = BestClientIp(ctx),
    xForwardedFor = ctx.Request.Headers["X-Forwarded-For"].ToString(),
    xAzureClientIp = ctx.Request.Headers["X-Azure-ClientIP"].ToString(),
    remoteIp = ctx.Connection.RemoteIpAddress?.ToString()
}));


// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
