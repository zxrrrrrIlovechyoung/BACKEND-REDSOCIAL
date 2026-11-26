using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.Sinks.Grafana.Loki;
using RedSocial.API.Middleware;
using RedSocial.API.Hubs;
using RedSocial.API.Services;
using RedSocial.Application.Interfaces.Services;
using RedSocial.Application;
using RedSocial.Infrastructure;
using RedSocial.Infrastructure.Security;
using RedSocial.Infrastructure.Storage;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Antiforgery y otros componentes de ASP.NET Core necesitan las mismas claves
// después de un reinicio o al cambiar de instancia en App Service. En Azure se
// guardan bajo HOME, que es almacenamiento persistente; en local se usa TEMP.
var dataProtectionPath = builder.Environment.IsDevelopment()
    ? Path.Combine(Path.GetTempPath(), "redsocial-dataprotection")
    : Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? builder.Environment.ContentRootPath,
        "data",
        "redsocial-dataprotection");
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName("RedSocial.API");

var logFilePath = builder.Configuration["Logging:FilePath"] ?? "logs/redsocial-.json";
if (!Path.IsPathRooted(logFilePath))
    logFilePath = Path.Combine(builder.Environment.ContentRootPath, logFilePath);
Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);

var loggerConfiguration = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "RedSocial.API")
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.File(
        new JsonFormatter(),
        logFilePath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true);

var grafanaLokiUrl = builder.Configuration["GrafanaCloud:LokiUrl"];
var grafanaLokiUser = builder.Configuration["GrafanaCloud:LokiUser"];
var grafanaLokiToken = builder.Configuration["GrafanaCloud:LokiToken"];
var grafanaLokiConfigured = !string.IsNullOrWhiteSpace(grafanaLokiUrl)
    && !string.IsNullOrWhiteSpace(grafanaLokiUser)
    && !string.IsNullOrWhiteSpace(grafanaLokiToken);

if (grafanaLokiConfigured)
{
    loggerConfiguration = loggerConfiguration.WriteTo.GrafanaLoki(
        grafanaLokiUrl!,
        labels:
        [
            new LokiLabel { Key = "app", Value = "redsocial-api" },
            new LokiLabel { Key = "job", Value = "redsocial-api" },
            new LokiLabel { Key = "env", Value = builder.Environment.EnvironmentName },
            new LokiLabel { Key = "source", Value = "azure-app-service" }
        ],
        credentials: new LokiCredentials
        {
            Login = grafanaLokiUser!,
            Password = grafanaLokiToken!
        },
        propertiesAsStructuredMetadata: ["EventType", "UserId", "SourceIp"],
        restrictedToMinimumLevel: LogEventLevel.Information);
}

Log.Logger = loggerConfiguration.CreateLogger();

builder.Host.UseSerilog();

// Secretos locales de desarrollo (cadena de conexión, clave JWT) en appsettings.Local.json.
// Ese archivo está en .gitignore: cada integrante crea el suyo a partir de appsettings.Local.example.json.
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ── Capas ────────────────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ── JWT ──────────────────────────────────────────────────────────────────
var jwt = builder.Configuration.GetSection(JwtSettings.Seccion).Get<JwtSettings>()!;
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException(
        $"Jwt:Key debe tener al menos 32 caracteres (entorno: '{builder.Environment.EnvironmentName}'). Configúrala en appsettings.Local.json.");

var authCookie = builder.Configuration.GetSection(AuthCookieSettings.Section).Get<AuthCookieSettings>()
    ?? new AuthCookieSettings();
builder.Services.Configure<AuthCookieSettings>(builder.Configuration.GetSection(AuthCookieSettings.Section));
builder.Services.AddSingleton(authCookie);
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "moment_csrf";
    options.Cookie.HttpOnly = false;
    options.Cookie.IsEssential = true;
    options.Cookie.Path = "/";
    options.Cookie.SecurePolicy = authCookie.Secure
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.None;
    options.Cookie.SameSite = authCookie.GetSameSiteMode();
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var path = context.HttpContext.Request.Path;
                if (authCookie.Enabled)
                    context.Token = context.Request.Cookies[authCookie.Name];

                var accessToken = context.Request.Query["access_token"];
                if (string.IsNullOrEmpty(context.Token)
                    && !string.IsNullOrEmpty(accessToken)
                    && path.StartsWithSegments("/hubs/chat"))
                    context.Token = accessToken;

                return Task.CompletedTask;
            }
        };
        options.MapInboundClaims = false; // conservar nombres de claims ("sub", "email")
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("Registro", config =>
    {
        config.PermitLimit = 5;
        config.Window = TimeSpan.FromMinutes(1);
        config.QueueLimit = 0;
        config.AutoReplenishment = true;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── CORS para el frontend React ──────────────────────────────────────────
var origenesConfigurados = builder.Configuration.GetSection("Cors:Origenes").Get<string[]>() ?? [];
var origenesLocales = builder.Environment.IsDevelopment()
    ? new[]
    {
        "http://localhost:5173",
        "http://127.0.0.1:5173",
        "http://localhost:5174",
        "http://127.0.0.1:5174",
        "http://localhost:5175",
        "http://127.0.0.1:5175"
    }
    : Array.Empty<string>();
var origenes = origenesConfigurados
    .Concat(origenesLocales)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", p => p.WithOrigins(origenes).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddScoped<AccessLogService>();
// Solo aceptar X-Forwarded-For cuando la conexión provenga de un proxy configurado.
var proxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
if (proxies.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var proxy in proxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
    });
}
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "RedSocial API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Pega aquí el token JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, [] }
    });
});

var app = builder.Build();

if (proxies.Length > 0) app.UseForwardedHeaders();
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // En producción se fuerza HTTPS. En desarrollo no: la redirección 307 rompe el
    // preflight CORS cuando el frontend llama a http://localhost:5079.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
var localStorageRoot = builder.Configuration.GetSection(StorageSettings.Seccion).Get<StorageSettings>() ?? new StorageSettings();
if (string.Equals(localStorageRoot.Provider, "Local", StringComparison.OrdinalIgnoreCase))
{
    var root = localStorageRoot.LocalRoot;
    if (string.IsNullOrWhiteSpace(root))
    {
        root = app.Environment.IsDevelopment()
            ? Path.Combine(app.Environment.ContentRootPath, "wwwroot")
            : Path.Combine(
                Environment.GetEnvironmentVariable("HOME") ?? app.Environment.ContentRootPath,
                "data",
                "redsocial-media");
    }
    var uploadsRoot = Path.Combine(root, "uploads");
    Directory.CreateDirectory(uploadsRoot);

    var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    if (!string.Equals(root, webRoot, StringComparison.OrdinalIgnoreCase))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(uploadsRoot),
            RequestPath = "/uploads"
        });
    }
}

// Sirve la SPA desde el mismo origen que la API. Esto evita cookies de terceros
// cuando la sesión usa AuthCookie y permite recargar rutas de React Router.
app.Use(async (context, next) =>
{
    var esNavegacion = HttpMethods.IsGet(context.Request.Method)
        || HttpMethods.IsHead(context.Request.Method);
    var esApi = context.Request.Path.StartsWithSegments("/api");
    var esHub = context.Request.Path.StartsWithSegments("/hubs");
    var esArchivo = Path.HasExtension(context.Request.Path);

    if (esNavegacion && !esApi && !esHub && !esArchivo)
    {
        var webRoot = app.Environment.WebRootPath
            ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        var indexPath = Path.Combine(webRoot, "index.html");

        if (File.Exists(indexPath))
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(indexPath);
            return;
        }
    }

    await next();
});

app.UseRouting();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
// La autenticación debe ejecutarse antes de validar antiforgery: el token
// CSRF se vincula al usuario autenticado en la cookie.
app.UseMiddleware<CookieCsrfMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
