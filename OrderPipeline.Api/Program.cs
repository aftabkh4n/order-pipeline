using Microsoft.EntityFrameworkCore;
using OrderPipeline.Api.Data;
using OrderPipeline.Api.Services;
using OrderPipeline.Core.Interfaces;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.AddControllers();
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddSingleton<IOrderEventPublisher, OrderEventPublisher>();
builder.Services.AddHostedService<OutboxProcessorService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    db.Database.EnsureCreated();
}

app.MapControllers();
app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });

app.Run();