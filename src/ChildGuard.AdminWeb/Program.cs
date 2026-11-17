using ChildGuard.AdminWeb.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
HostPort.Apply(builder);

builder.Services.AddControllersWithViews(options => options.Filters.Add<ApiSessionExpiredFilter>())
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddHttpContextAccessor();

// Behind a TLS-terminating proxy (Render) the app only sees http unless forwarded headers are honoured.
var reverseProxy = ReverseProxySettings.From(builder.Configuration);
if (reverseProxy.Enabled)
    builder.Services.Configure<ForwardedHeadersOptions>(reverseProxy.Apply);

// Keys for cookies/antiforgery. On a host with an ephemeral disk they must come from configuration.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("ChildGuard.AdminWeb");
var keyRingXml = builder.Configuration["DataProtection:KeyRingXml"];
if (!string.IsNullOrWhiteSpace(keyRingXml))
    dataProtection.AddKeyManagementOptions(o => o.XmlRepository = new ConfiguredKeyRingRepository(keyRingXml));

// Lets fetch()-based JSON posts (the pickup desk) carry the antiforgery token
// as a header instead of a form field, read from the <meta name="csrf-token">
// in _Layout.cshtml.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    // Same rule as the sign-in cookie: https only, except plain-http local development.
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

// TempData (flash messages) travels in its own cookie; it gets the same https-only rule.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.CookieTempDataProviderOptions>(options =>
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always);

builder.Services.AddTransient<JwtForwardingHandler>();
// Any host, port or path prefix; validated here so a bad value fails at startup with a clear message.
var apiEndpoint = ApiEndpoint.From(builder.Configuration);
builder.Services.AddSingleton(apiEndpoint);
builder.Services.AddTransient<ApiPathBaseHandler>();
builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = apiEndpoint.BaseUri;
    // A sleeping API (free hosting) needs about a minute to wake: raise this there.
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Api:TimeoutSeconds", 30));
}).AddHttpMessageHandler<ApiPathBaseHandler>().AddHttpMessageHandler<JwtForwardingHandler>();

builder.Services.AddHttpClient<LongRunningApiClient>(client =>
{
    client.BaseAddress = apiEndpoint.BaseUri;
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Api:LongRunningTimeoutSeconds", 300));
}).AddHttpMessageHandler<ApiPathBaseHandler>().AddHttpMessageHandler<JwtForwardingHandler>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = true;
        options.Cookie.Name = "ChildGuardAdminAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Only ever over https outside local development (plain http there keeps `dotnet run` usable).
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        // fetch() calls cannot follow a redirect to an HTML sign-in page; answer them
        // 401 so the page script sends the browser to sign-in instead.
        options.Events.OnRedirectToLogin = context =>
        {
            if (ApiSessionExpiredFilter.WantsHtml(context.Request)) context.Response.Redirect(context.RedirectUri);
            else context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

// Must match the API's School:TimeZone: every page renders instants in school time.
ChildGuard.AdminWeb.Services.SchoolTime.Configure(builder.Configuration["School:TimeZone"]);

var app = builder.Build();

if (reverseProxy.Enabled)
    app.UseForwardedHeaders(); // first, so the https redirect and Secure cookies see the real scheme
if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(keyRingXml))
    app.Logger.LogWarning("DataProtection:KeyRingXml is not set: keys live on local disk, so admins are signed out whenever the host's disk is reset.");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Account/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Platform health check: no API call, no database.
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
