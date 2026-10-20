using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Serval.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new ResponseCacheAttribute
    {
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true,
    });
    options.Conventions.AuthorizeFolder("/Services");
    options.Conventions.AuthorizeFolder("/Grants");
    options.Conventions.AuthorizePage("/Logout");
});
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
}
else
{
    var keyDirectory = OperatingSystem.IsLinux()
        ? "/var/lib/serval-web/keys"
        : Path.Combine(builder.Environment.ContentRootPath, "artifacts", "web-keys");
    Directory.CreateDirectory(keyDirectory);
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
}
builder.Services.AddSingleton<IAgentIpcClient, AgentIpcClient>();
builder.Services.AddScoped<ServalCookieAuthenticationEvents>();
builder.Services.AddScoped<IAuthenticatedTicketAccessor, AuthenticatedTicketAccessor>();
builder.Services.AddScoped<IInvalidSessionHandler, InvalidSessionHandler>();
builder.Services.AddAuthentication(ServalAuthentication.Scheme)
    .AddCookie(ServalAuthentication.Scheme, options =>
    {
        options.Cookie.Name = ServalAuthentication.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(1);
        options.SlidingExpiration = true;
        options.LoginPath = "/Login";
        options.EventsType = typeof(ServalCookieAuthenticationEvents);
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

await app.RunAsync();

public partial class Program;
