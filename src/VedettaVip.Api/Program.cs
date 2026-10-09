// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VedettaVip;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Endpoints;
using VedettaVip.Api.Hubs;
using VedettaVip.Api.Options;
using VedettaVip.Api.Security;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using Npgsql;

const string WebCorsPolicy = "VedettaVipWeb";

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddLocalSettings(builder.Environment);

// Sviluppo: dotnet user-secrets (UserSecretsId nel csproj). Produzione: variabile ConnectionStrings__VedettaVip.
var connectionString = builder.Configuration.GetConnectionString("VedettaVip");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Connection string 'VedettaVip' mancante. In sviluppo: dotnet user-secrets set \"ConnectionStrings:VedettaVip\" \"Host=...\" " +
        "--project src/VedettaVip.Api; in produzione: variabile d'ambiente ConnectionStrings__VedettaVip.");

// Un solo pool di connessioni per EF Core e per le scritture COPY delle metriche (MetricWriter)
var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContext<VedettaVipDbContext>(options => options.UseNpgsql(dataSource));

// Chiave degli agenti: validata all'avvio (assente o < 32 caratteri = l'API non parte)
builder.Services.AddOptions<AgentOptions>()
    .BindConfiguration(AgentOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<StatusProcessingLock>();
builder.Services.AddScoped<DeviceStatusService>();
builder.Services.AddSingleton<AgentNotifier>();
builder.Services.AddSingleton<MapNotifier>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<TrafficCache>();
builder.Services.AddSingleton<RouterOsCache>();
builder.Services.AddSingleton<MetricWriter>();

// Data Protection: cifra i segreti (password SMTP, token Telegram) e i cookie di sessione.
// Chiavi nella tabella DataProtectionKeys: sessioni e segreti sopravvivono ai riavvii.
// "NetMap" è il nome storico del progetto e NON va cambiato: è parte della chiave di cifratura,
// con un altro nome i segreti già salvati e i cookie di sessione non sarebbero più decifrabili.
builder.Services.AddDataProtection()
    .SetApplicationName("NetMap")
    .PersistKeysToDbContext<VedettaVipDbContext>();
builder.Services.AddSingleton<SecretProtector>();
builder.Services.AddSingleton<NotificationSettingsService>();
builder.Services.AddSingleton<EmailSender>();
// Nessun logger HTTP: il token del bot è nell'URL e finirebbe nei log
builder.Services.AddHttpClient<TelegramSender>(http => http.Timeout = TimeSpan.FromSeconds(30)).RemoveAllLoggers();
builder.Services.AddHostedService<NotificationDispatcher>();
builder.Services.AddHostedService<AgentMonitor>();
builder.Services.AddHostedService<ThresholdMonitor>();
builder.Services.AddSingleton<ActiveMaintenance>();
builder.Services.AddScoped<MetricQuery>();
builder.Services.AddScoped<RouterOsWatchService>();

// ---------- Autenticazione utenti: account locali (Identity) con cookie di sessione ----------
builder.Services.AddSingleton<SetupCode>();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddIdentityCore<AppUser>(options =>
    {
        // Lunghezza più che complessità (linee guida NIST)
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 4;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
        options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-@";
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<VedettaVipDbContext>()
    // Chiave TOTP e codici di recupero cifrati con Data Protection (Identity li salverebbe in chiaro)
    .AddUserStore<ProtectedUserStore>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// In produzione il cookie è sempre Secure (HTTPS dietro il reverse proxy); in sviluppo segue la richiesta (http locale)
var secureCookies = builder.Configuration.GetValue("Auth:SecureCookies", !builder.Environment.IsDevelopment());
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "VedettaVip.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    // Scorrevole: una pagina aperta (es. monitor NOC che interroga l'API ogni minuto) resta connessa
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    // API: niente redirect a una pagina di login, solo 401/403
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
// Verifica in due passaggi: cookie temporaneo tra password e codice (5 minuti) e "ricorda questo browser"
// (invalidato dal cambio del security stamp: password, azzeramento o disattivazione della verifica)
foreach (var scheme in new[] { IdentityConstants.TwoFactorUserIdScheme, IdentityConstants.TwoFactorRememberMeScheme })
{
    builder.Services.Configure<CookieAuthenticationOptions>(scheme, options =>
    {
        options.Cookie.Name = scheme == IdentityConstants.TwoFactorUserIdScheme ? "VedettaVip.TwoFactor" : "VedettaVip.TwoFactorRemember";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        if (scheme == IdentityConstants.TwoFactorRememberMeScheme)
            options.ExpireTimeSpan = TimeSpan.FromDays(TwoFactorDefaults.RememberBrowserDays);
    });
}
// Utente disattivato o ruolo cambiato: il security stamp cambia e le sessioni aperte vengono rivalidate entro un minuto
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.AddAuthorizationBuilder()
    // Tutto richiede un utente autenticato, salvo AllowAnonymous esplicito (login, setup, endpoint degli agenti)
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.Operator, policy => policy.RequireRole(Roles.Admin, Roles.Operator))
    .AddPolicy(Policies.Admin, policy => policy.RequireRole(Roles.Admin));

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi();

// X-Agent-Key non è tra gli header ammessi: il browser non può chiamare gli endpoint agent.
// AllowCredentials (cookie di sessione) e gli header X-SignalR-* servono al client; X-VedettaVip-Request è la difesa CSRF;
// X-VedettaVip-Client identifica la scheda del browser nei messaggi MapsChanged.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(WebCorsPolicy, policy => policy
    .WithOrigins(corsOrigins)
    .WithMethods("GET", "POST", "PUT", "DELETE")
    .WithHeaders("Content-Type", "X-Requested-With", "X-SignalR-User-Agent", CsrfHeaderMiddleware.HeaderName, MapClientHeaders.ClientId)
    .WithExposedHeaders("Location")
    .AllowCredentials()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}
else
{
    app.UseExceptionHandler();
}

app.UseHttpsRedirection();
app.UseCors(WebCorsPolicy);
app.UseMiddleware<CsrfHeaderMiddleware>();

// L'hub degli agenti richiede X-Agent-Key (gli endpoint filter non si applicano agli hub)
app.UseWhen(context => context.Request.Path.StartsWithSegments("/" + AgentHubMessages.HubPath),
    agentHub => agentHub.UseMiddleware<AgentKeyMiddleware>());

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapTwoFactorEndpoints();
app.MapMapEndpoints();
app.MapMapNodeEndpoints();
app.MapMapLinkEndpoints();
app.MapDeviceEndpoints();
app.MapDeviceImportEndpoints();
app.MapAgentEndpoints();
app.MapMetricEndpoints();
app.MapSettingsEndpoints();
app.MapWinBoxEndpoints();
app.MapNotificationEndpoints();
app.MapMaintenanceEndpoints();
app.MapSnmpCredentialEndpoints();
app.MapRouterOsCredentialEndpoints();
app.MapRouterOsWatchEndpoints();
app.MapDiscoveryEndpoints();
app.MapHub<StatusHub>("/" + StatusHubMessages.HubPath);
app.MapHub<AgentHub>("/" + AgentHubMessages.HubPath).AllowAnonymous(); // protetto da AgentKeyMiddleware

await LogSetupCodeIfNeededAsync(app);

app.Run();

// Nessun utente: scrive nel log il codice monouso per creare il primo amministratore dalla pagina di accesso
static async Task LogSetupCodeIfNeededAsync(WebApplication app)
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VedettaVipDbContext>();
        if (!await db.Users.AnyAsync())
            app.Logger.LogWarning("Nessun utente: aprire VedettaVip e creare il primo amministratore. Codice di setup: {SetupCode}",
                app.Services.GetRequiredService<SetupCode>().Value);
    }
    catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
    {
        app.Logger.LogError(ex, "Verifica degli utenti non riuscita (migration applicate?)");
    }
}
