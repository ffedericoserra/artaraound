using ArtAround.Api.Data;
using ArtAround.Api.Domain;
using ArtAround.Api.Infrastructure;
using ArtAround.Api.Realtime;
using ArtAround.Api.Seeding;
using ArtAround.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

/* ---------- services (dependency injection) ---------- */

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Default"), AppDbContext.MapEnums)
    .UseSnakeCaseNamingConvention());

builder.Services
    .AddControllers(options =>
        // Validation errors are keyed by JSON names ("username"), not C# names ("Username").
        options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()))
    .AddJsonOptions(options => JsonConventions.Apply(options.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(options =>
        options.InvalidModelStateResponseFactory = ValidationResponse.FromModelState);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddSignalR()
    .AddJsonProtocol(options => JsonConventions.Apply(options.PayloadSerializerOptions));

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Key.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters (HS256 needs a 256-bit key)");
}
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = TokenService.SigningKey(jwt),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        options.Events = new JwtBearerEvents
        {
            // Browsers cannot set headers on a WebSocket, so SignalR sends the token in the query string.
            OnMessageReceived = context =>
            {
                if (context.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = context.Request.Query["access_token"];
                }
                return Task.CompletedTask;
            },
            // A valid signature is not enough: the user must still exist (the demo data is reseeded).
            OnTokenValidated = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var userId = context.Principal!.GetUserId();
                if (userId is null || !await db.Users.AnyAsync(u => u.Id == userId))
                {
                    context.Fail("User not found");
                }
            },
            // The default 401 has no body; send the API's error shape instead.
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                var message = context.AuthenticateFailure is null ? "No token provided" : "Invalid token";
                await context.Response.WriteAsJsonAsync(ApiProblem.Create(StatusCodes.Status401Unauthorized, message),
                    options: null, contentType: "application/problem+json");
            },
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<SessionNotifier>();
builder.Services.AddScoped<PurchaseService>();
builder.Services.AddScoped<ItemReader>();
builder.Services.AddScoped<ItemService>();
builder.Services.AddScoped<VisitService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<Seeder>();

builder.Services.AddOpenApi();

var app = builder.Build();

/* ---------- one-shot commands: dotnet run -- migrate | seed ---------- */

// Migrations run as a deliberate deploy step, never at web startup: several instances
// starting at once would race, and a failed migration should not half-start the API.
if (args is ["migrate"] or ["seed"])
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (args[0] == "migrate")
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        await scope.ServiceProvider.GetRequiredService<Seeder>().SeedAsync(CancellationToken.None);
    }
    return;
}

/* ---------- HTTP pipeline (middleware, in order) ---------- */

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// The frontends are static files served from the repository, as Node did:
// /uploads, /marketplace and the navigator's build at the root.
var repoRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath,
    app.Configuration["Frontends:RepositoryRoot"] ?? "../.."));
ServeDirectory(app, Path.Combine(repoRoot, "uploads"), "/uploads");
ServeDirectory(app, Path.Combine(repoRoot, "frontend", "marketplace"), "/marketplace");
app.MapGet("/marketplace", () => Results.Redirect("/marketplace/pages/homepage.html"));
var navigatorDist = Path.Combine(repoRoot, "frontend-navigator", "dist");
ServeDirectory(app, navigatorDist, "");
// The marketplace's navbar links to the navigator by this path.
ServeDirectory(app, navigatorDist, "/frontend-navigator/dist");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SessionHub>("/hubs/session");

// Navigator deep links (/museums, /session/ABC) get index.html. Unknown /api routes stay 404.
if (Directory.Exists(navigatorDist))
{
    app.MapFallback("/{**path:nonfile}", async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(ApiProblem.Create(StatusCodes.Status404NotFound, "Not found"));
            return;
        }
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(Path.Combine(navigatorDist, "index.html"));
    });
}

app.Run();

static void ServeDirectory(WebApplication app, string path, string requestPath)
{
    if (Directory.Exists(path))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(path),
            RequestPath = requestPath,
        });
    }
}

// Lets WebApplicationFactory<Program> find this assembly's entry point in tests.
public partial class Program;
