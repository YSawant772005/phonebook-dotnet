using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Phonebook.Api.Data;
using Phonebook.Api.Middleware;
using Phonebook.Api.Services;

static string BuildConnectionString(IConfiguration configuration)
{
    string? dbHost = Environment.GetEnvironmentVariable("DATABASE_HOST");
    string? dbPort = Environment.GetEnvironmentVariable("DATABASE_PORT");
    string? dbName = Environment.GetEnvironmentVariable("DATABASE_NAME");
    string? dbUser = Environment.GetEnvironmentVariable("DATABASE_USERNAME");
    string? dbPassword = Environment.GetEnvironmentVariable("DATABASE_PASSWORD");

    if (dbHost is not null || dbPort is not null || dbName is not null || dbUser is not null || dbPassword is not null)
    {
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = dbHost ?? "localhost",
            Port = dbPort is null ? 5432 : int.Parse(dbPort),
            Database = dbName ?? "phonebook_db",
            Username = dbUser ?? "phonebook_user",
            Password = dbPassword ?? "phonebook_password",
        };
        return connection.ConnectionString;
    }

    return configuration.GetConnectionString("Phonebook")!;
}

var builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = ErrorResponseFactory.Create;
});

string connectionString = BuildConnectionString(builder.Configuration);

builder.Services.AddDbContext<PhonebookDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IContactRepository, ContactRepository>();
builder.Services.AddScoped<ContactService>();
builder.Services.AddHostedService<ContactDataSeeder>();

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();

app.MapControllers();

app.Run();

public partial class Program
{
}

internal static class ErrorResponseFactory
{
    public static IActionResult Create(ActionContext context)
    {
        // Body-level failures (malformed JSON, empty body, JSON type mismatches) are
        // registered under the "$", "" or parameter-name keys. The Java reference
        // backend maps every HttpMessageNotReadableException to the same message.
        bool hasBodyError = context.ModelState.Any(state =>
            (state.Key == "$" || state.Key == "" || state.Key == "request")
            && state.Value is { Errors.Count: > 0 });

        if (hasBodyError)
        {
            return new BadRequestObjectResult(new { detail = "Request body must contain valid JSON." });
        }

        ModelError? firstError = context.ModelState
            .Where(state => state.Value is { Errors.Count: > 0 })
            .SelectMany(state => state.Value!.Errors)
            .FirstOrDefault();

        string message = firstError?.ErrorMessage ?? "Invalid request.";
        return new BadRequestObjectResult(new { detail = message });
    }
}