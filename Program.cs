using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// Honor proxy headers and trust ONLY your Palo Alto's public IP (immediate peer)
builder.Services.Configure<ForwardedHeadersOptions>(opts =>
{
    opts.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // The IP your app sees as RemoteIpAddress (PA untrust public IP)
    opts.KnownProxies.Add(IPAddress.Parse("52.238.213.102"));

    // If you add an Azure LB VIP (HA), add it here too:
    // opts.KnownProxies.Add(IPAddress.Parse("<your-lb-public-ip>"));

    // Accept a reasonable XFF chain depth
    opts.ForwardLimit = 10;

    // If you ever want to trust all intermediaries for a lab, uncomment the next two (LESS secure):
    // opts.KnownNetworks.Clear();
    // opts.KnownProxies.Clear();
});

builder.Services.AddRazorPages();

var app = builder.Build();

// Apply forwarded headers BEFORE anything that reads scheme/IP
app.UseForwardedHeaders();

// Helper to pick the best client IP from headers
static string BestClientIp(HttpContext ctx)
{
    var xff = ctx.Request.Headers["X-Forwarded-For"].ToString();
    var xac = ctx.Request.Headers["X-Azure-ClientIP"].ToString();
    var cfc = ctx.Request.Headers["CF-Connecting-IP"].ToString();

    var firstXff = string.IsNullOrWhiteSpace(xff) ? null
        : xff.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

    // Prefer the left-most XFF (end-user), then AFD's X-Azure-ClientIP, then Cloudflare, then socket IP
    return firstXff ?? xac ?? cfc ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "";
}

// Lightweight request logger
app.Use(async (ctx, next) =>
{
    var clientIp = BestClientIp(ctx);
    var xff = ctx.Request.Headers["X-Forwarded-For"].ToString();
    var xac = ctx.Request.Headers["X-Azure-ClientIP"].ToString();
    var xorig = ctx.Request.Headers["X-Original-ClientIP"].ToString();

    app.Logger.LogInformation(
        "clientIp={clientIp} xff={xff} xAzureClientIp={xac} xOriginalClientIp={xorig} path={path}",
        clientIp, xff, xac, xorig, ctx.Request.Path);

    await next();
});

// Quick sanity endpoint you already had
app.MapGet("/whoami", (HttpContext ctx) => Results.Json(new {
    bestClientIp = BestClientIp(ctx),
    xForwardedFor = ctx.Request.Headers["X-Forwarded-For"].ToString(),
    xAzureClientIp = ctx.Request.Headers["X-Azure-ClientIP"].ToString(),
    xOriginalClientIp = ctx.Request.Headers["X-Original-ClientIP"].ToString(),
    remoteIp = ctx.Connection.RemoteIpAddress?.ToString()
}));

// NEW: Full header dump to prove what AppGW/Palo deliver
app.MapGet("/debug/headers", (HttpContext ctx) =>
{
    var dict = ctx.Request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
    return Results.Json(new {
        bestClientIp = BestClientIp(ctx),
        remoteIp = ctx.Connection.RemoteIpAddress?.ToString(),
        headers = dict
    });
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
