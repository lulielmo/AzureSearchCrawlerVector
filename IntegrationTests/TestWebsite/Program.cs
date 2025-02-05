var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Konfigurera middleware i rätt ordning
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});
app.UseStaticFiles();

// För felsökning
app.MapGet("/health", () => "Application is running!");

app.Run();
