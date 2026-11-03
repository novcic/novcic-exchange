using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Novcic.Core;
using Novcic.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.NumberHandling = Wire.Json.NumberHandling);
builder.Services.AddDbContext<ExchangeDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Exchange") ?? throw new InvalidOperationException("Database connection is required.")));
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
var jwtKey = builder.Configuration["JwtKey"] ?? throw new InvalidOperationException("JwtKey is required.");
if (jwtKey.Length < 32) throw new InvalidOperationException("JwtKey must be at least 32 characters.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => {
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuer = true, ValidIssuer = "novcic", ValidateAudience = true, ValidAudience = "novcic-web",
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(10)
    };
});
builder.Services.AddAuthorization(); builder.Services.AddSignalR();
builder.Services.AddHttpClient("matching", c => {
    c.BaseAddress = new Uri(builder.Configuration["MatchingUrl"] ?? "http://localhost:5001");
    c.DefaultRequestHeaders.Add("X-Internal-Key", builder.Configuration["InternalKey"] ?? throw new InvalidOperationException("InternalKey is required."));
    c.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHostedService<MarketRelay>();
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.Use(async (context, next) => {
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (context.Request.Method == "POST" && context.Request.Headers.Origin.FirstOrDefault() is string origin &&
        (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Authority != context.Request.Host.Value))
    { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "Cross-origin request rejected." }); return; }
    try { await next(); }
    catch (RuleException ex) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (HttpRequestException) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "Trading service is temporarily unavailable. Retry with the same request key." }); }
    catch (TaskCanceledException) when (!context.RequestAborted.IsCancellationRequested)
    { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "The request timed out. Retry with the same request key." }); }
});
app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/api/health", async (IHttpClientFactory factory, ExchangeDb db) => {
    var matching = await factory.CreateClient("matching").GetAsync("/health");
    return matching.IsSuccessStatusCode && await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503);
});
app.MapGet("/api/config", (IConfiguration c) => new { demoMode = c.GetValue<bool>("DemoMode"), symbol = "BTC/USDT", quantityStep = "0.000001", priceTick = "0.01", fees = "0" });
app.MapPost("/api/auth/register", async (Registration body, AuthService auth, HttpContext ctx) => await auth.Register(body, ctx)).RequireRateLimiting("auth");
app.MapPost("/api/auth/login", async (Login body, AuthService auth, HttpContext ctx) => await auth.Login(body, ctx)).RequireRateLimiting("auth");
app.MapPost("/api/auth/refresh", async (AuthService auth, HttpContext ctx) => await auth.Refresh(ctx)).RequireRateLimiting("auth");
app.MapPost("/api/auth/logout", async (AuthService auth, HttpContext ctx) => await auth.Logout(ctx));
app.MapGet("/api/auth/me", async (ClaimsPrincipal principal, ExchangeDb db) => {
    var user = await db.Users.FindAsync(Guid.Parse(principal.FindFirstValue("sub")!));
    return user == null ? Results.Unauthorized() : Results.Ok(new { user.Id, user.Email, user.Country });
}).RequireAuthorization();
app.MapGet("/api/market", (HttpContext ctx, IHttpClientFactory f) => Proxy(ctx, f, "/market"));
app.MapGet("/api/candles", (HttpContext ctx, IHttpClientFactory f) => Proxy(ctx, f, "/candles" + ctx.Request.QueryString));
foreach (var route in new[] { "balances", "orders", "trades", "ledger" })
    app.MapGet("/api/" + route, (HttpContext ctx, IHttpClientFactory f) => Proxy(ctx, f, $"/{route}/{UserId(ctx)}")).RequireAuthorization();
foreach (var route in new[] { "faucet", "transfer", "orders" })
    app.MapPost("/api/" + route, (HttpContext ctx, IHttpClientFactory f) => Proxy(ctx, f, $"/{route}/{UserId(ctx)}")).RequireAuthorization();
app.MapPost("/api/orders/{id:guid}/cancel", (Guid id, HttpContext ctx, IHttpClientFactory f) => Proxy(ctx, f, $"/cancel/{UserId(ctx)}/{id}")).RequireAuthorization();
app.MapHub<MarketHub>("/hubs/market");
app.Run();

static Guid UserId(HttpContext ctx) => Guid.Parse(ctx.User.FindFirstValue("sub")!);
static async Task<IResult> Proxy(HttpContext ctx, IHttpClientFactory factory, string path)
{
    using var request = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), path);
    if (ctx.Request.Method == "POST")
    {
        if (ctx.Request.ContentLength > 8192) return Results.BadRequest(new { error = "Request too large." });
        request.Content = new StringContent(await new StreamReader(ctx.Request.Body).ReadToEndAsync(), Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", ctx.Request.Headers["Idempotency-Key"].ToString());
    }
    using var response = await factory.CreateClient("matching").SendAsync(request, ctx.RequestAborted);
    return Results.Content(await response.Content.ReadAsStringAsync(), "application/json", statusCode: (int)response.StatusCode);
}
