using ComponentFactory.Api;
using ComponentFactory.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddComponentFactory();

var app = builder.Build();

app.UseExceptionHandler();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
