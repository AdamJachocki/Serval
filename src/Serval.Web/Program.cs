using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Serval.Web;
using Serval.Web.Listener;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Production listens only as /etc/serval/web.json (or the loopback default) says; Development and
// Testing keep the host's own configuration.
WebListenerSettings? listener = null;
if (builder.Environment.IsProduction())
{
    try
    {
        listener = WebListener.LoadProduction();
        WebListener.ConfigureKestrel(builder, listener);
    }
    catch (WebListenerException exception)
    {
        await Console.Error.WriteLineAsync($"Serval.Web refuses to start: {exception.Message}");
        return 1;
    }
}

// Add services to the container.
builder.Services.AddRazorPages(options =>
    options.Conventions.ConfigureFilter(new ResponseCacheAttribute
    {
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true,
    }));
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

WebListener.UseTransportSecurity(app, listener, app.Environment.IsDevelopment());

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

await app.RunAsync();
return 0;

public partial class Program;
