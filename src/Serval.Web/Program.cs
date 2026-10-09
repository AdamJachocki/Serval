using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Serval.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
builder.Services.AddRazorPages(options =>
    options.Conventions.ConfigureFilter(new ResponseCacheAttribute
    {
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true,
    }));
var keyDirectory = OperatingSystem.IsLinux()
    ? "/var/lib/serval-web/keys"
    : Path.Combine(builder.Environment.ContentRootPath, "artifacts", "web-keys");
Directory.CreateDirectory(keyDirectory);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
builder.Services.AddSingleton<IAgentIpcClient, AgentIpcClient>();

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

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

await app.RunAsync();

public partial class Program;
