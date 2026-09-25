# .NET Phonebook Backend — Knowledge Bytes

**Project:** ASP.NET Core (.NET 8) phonebook API, ported from a Java Spring Boot backend.
**Location in repo:** `backend-dotnet/`
**Document purpose:** teach you, from zero, how this backend really works — every layer, every file, and the complete request lifecycle.
**How to use:** each Knowledge Byte stands alone. Read them in order the first time, then jump to any byte later. Code references use `file:line` so you can open the file and see the exact line.

---

# PART 1 — PROJECT OVERVIEW

## Byte 1.1 — The Big Picture

### What is this?

This is a **REST API** for a phonebook. It stores contacts (name, phone number, email, address) in PostgreSQL and lets a frontend create, read, update, and delete them over HTTP.

### The architecture diagram

```
Browser (Vue frontend)
        ↓  GET/POST/PUT/DELETE /api/contacts...
Nginx (reverse proxy, port 80)
        ↓  strips /api, forwards to /contacts on port 8080
ASP.NET Core API  (Kestrel web server, port 8080)
        ↓  middleware runs first
Controller  (ContactsController)
        ↓
Service  (ContactService)
        ↓
Repository  (IContactRepository → ContactRepository)
        ↓
EF Core / DbContext  (PhonebookDbContext)
        ↓  Npgsql (PostgreSQL driver) translates LINQ → SQL
PostgreSQL  (phonebook_db database)
```

### Anchor the diagram in the real code

- The API host name comes from `src/Phonebook.Api/Program.cs`. Look at line 64: `app.MapControllers()` — that is where the controller gets wired into the HTTP pipeline.
- The controller lives in `src/Phonebook.Api/Controllers/ContactsController.cs`.
- Controller -> Service: `ContactsController.cs:13-16` (constructor takes `ContactService`).
- Service -> Repository: `ContactService.cs:15-18` (constructor takes `IContactRepository`).
- Repository -> DbContext: `ContactRepository.cs:15-18` (constructor takes `PhonebookDbContext`).
- DbContext -> PostgreSQL: `Program.cs:54` (`AddDbContext<PhonebookDbContext>(options => options.UseNpgsql(connectionString))`).
- Nginx: `nginx/nginx.conf`, `location /api/ { proxy_pass http://localhost:8080/; }` (lines 31-32).

### Why this is a chain, not a pile

Every link depends only on the one immediately below it. The Controller never touches the database. The Service never writes SQL. The Repository never formats JSON. This is called **separation of concerns**, and it is the single most important design idea in this project.

### What should you be able to answer after this byte?

- What are the layers, top to bottom?
- Which file maps to which layer?
- What does each arrow (downward) actually mean in code?

### Common beginner confusion

"Calls the service" can sound abstract. It is not. It is literally a C# method call: `ContactsController.cs:26-27` runs `_service.GetContactsAsync(...)`. Nothing magic.

---

## Byte 1.2 — Vocabulary in One Page (all tied to this project)

Every term below is explained fully later. This byte is the map.

| Term | Plain meaning | Where it lives here |
|---|---|---|
| **ASP.NET Core** | Microsoft's framework for building web applications on .NET. It hosts your app and runs an HTTP server (Kestrel). | The whole `src/Phonebook.Api` project. |
| **.NET 8** | The runtime + class libraries your code compiles against. "8" is the major version. | Declared in `Phonebook.Api.csproj:4` (`<TargetFramework>net8.0</TargetFramework>`). |
| **Web API** | A program that responds to HTTP requests with data (usually JSON) instead of web pages. | This backend — it returns JSON, not HTML. |
| **REST API** | An API that models resources (nouns like "contacts") and uses HTTP verbs (GET/POST/PUT/DELETE) as the actions on them. | `contacts` is the resource; `/api/contacts/{id}` is the URI. |
| **Controller** | The class that receives HTTP requests, calls services, and returns HTTP responses. | `ContactsController`. |
| **Service** | The class that holds business rules and coordinates repository calls. | `ContactService`. |
| **Repository** | The class that talks to persistence; it isolates database code behind an interface. | `ContactRepository` + `IContactRepository`. |
| **EF Core** | "Entity Framework Core" — an Object-Relational Mapper. You write LINQ in C#; it produces SQL. | Referenced via the Npgsql EF Core provider in `Phonebook.Api.csproj:12`. |
| **DbContext** | The EF Core unit of work: represents the database session, holds `DbSet`s, tracks changes. | `PhonebookDbContext`. |
| **Npgsql** | The PostgreSQL driver for .NET. Also provides the `PostgresException` type the repository inspects. | Packaged inside `Npgsql.EntityFrameworkCore.PostgreSQL`; used in `Program.cs:5` and `ContactRepository.cs:144-147`. |
| **PostgreSQL** | The actual database engine. Stores the `contacts` table. | Connected via `Host=localhost;Port=5432;Database=phonebook_db;...`. |
| **DTO** | "Data Transfer Object" — a class that carries data between layers/over the wire, separate from the entity. | `ContactCreateRequest`, `ContactResponse`, `ContactPageResponse`. |
| **Middleware** | Software in the request pipeline that wraps the request/response as it flows through the app. | `ErrorHandlingMiddleware`. |
| **Dependency Injection** | The framework builds the objects a class needs and hands them to it, instead of the class creating them itself. | Constructor injection everywhere: `ContactsController.cs:13-16`, `ContactService.cs:15-18`, `ContactRepository.cs:15-18`. |

### Mental model

Think of this backend as a company. The **Controller** is the receptionist (reads the request, calls the right department, formats the reply). The **Service** is the manager with the policy handbook (rules like "phone must have 10 digits"). The **Repository** is the librarian (the only person allowed in the vault). The **DbContext/EF Core** is the vault's card catalog that translates "find me the book with this ID" into "search shelf X, row Y."

---

# PART 2 — SOLUTION AND PROJECT STRUCTURE

## Byte 2.1 — The Solution file (`.sln`)

### What is this?

A solution is a grouping of one or more project files. `Phonebook.sln` is a text file that says "these projects belong together and here is how each is built."

### Where is it?

`backend-dotnet/Phonebook.sln`

### What's in it?

- Line 8: a project named `Phonebook.Api.Tests` at `tests\Phonebook.Api.Tests\Phonebook.Api.Tests.csproj`, with GUID `{A73CD272-...}`.
- Line 6: a solution folder named `tests`.
- Line 25: the tests project is nested inside the `tests` folder.

### Important detail

The solution references **only the test project**. Why? Because the API project is referenced *by* the test project (via `<ProjectReference>` in `Phonebook.Api.Tests.csproj:28`). The solution does not need to list `Phonebook.Api.csproj` separately; building the test project pulls the API project in automatically. This is a common and valid layout.

### How you use it

```
dotnet build Phonebook.sln     # builds everything in the solution
dotnet test  Phonebook.sln     # builds and runs all test projects
```

### What should you be able to answer?

- What does a `.sln` file contain?
- Which projects are in this solution and how are they related?

---

## Byte 2.2 — The API project file (`.csproj`)

### What is this?

A `.csproj` file is the MSBuild project definition. It says what SDK is used, what the app targets, and which NuGet packages are referenced. Modern .NET csproj files are small and readable XML.

### Where is it?

`backend-dotnet/src/Phonebook.Api/Phonebook.Api.csproj`

### Line by line

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">          <!-- (1) -->
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>   <!-- (2) -->
    <Nullable>enable</Nullable>                 <!-- (3) -->
    <ImplicitUsings>enable</ImplicitUsings>     <!-- (4) -->
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.11" />          <!-- (5) -->
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.10" />   <!-- (6) -->
  </ItemGroup>
</Project>
```

1. `Microsoft.NET.Sdk.Web` — the **Web SDK**. It makes the project a web project (adds things like the built-in web server support).
2. `net8.0` — compiled against .NET 8.
3. `Nullable enable` — nullable reference types are on. This is why you see `string? Email` in `Contact.cs:11` — the `?` means "this may be null", and the compiler helps you handle that.
4. `ImplicitUsings` — common `using` directives are inferred. That is why files like `Contact.cs` don't have `using System;` at the top.
5. EF Core runtime — the O/RM core.
6. The **PostgreSQL provider for EF Core** (which itself depends on `Npgsql`, the low-level driver, and `Microsoft.EntityFrameworkCore`).

### NuGet packages — what they are

NuGet is .NET's package manager (like `npm` for Node or a Maven dependency in Java). A NuGet package is prebuilt, reusable code. These two packages are the *only* runtime dependencies of the API.

### What should you be able to answer?

- What does the Web SDK give you?
- Which two packages does this API actually use, and what are they for?
- What does `Nullable enable` mean for how you read the code?

---

## Byte 2.3 — The test project file

### Where is it?

`backend-dotnet/tests/Phonebook.Api.Tests/Phonebook.Api.Tests.csproj`

### What it declares

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.NET.Test.Sdk` | 17.11.1 | Runs tests from the command line. |
| `xunit` | 2.9.2 | The xUnit test framework (`[Fact]`, assertions). |
| `xunit.runner.visualstudio` | 2.8.2 | Lets Visual Studio / `dotnet test` discover xUnit tests. |
| `coverlet.collector` | 6.0.2 | Measures test coverage. |
| `Moq` | 4.20.72 | Creates **mock** objects — fake implementations of interfaces, used by unit tests in `ContactServiceTests.cs`. |
| `Microsoft.AspNetCore.Mvc.Testing` | 8.0.11 | Hosts the real app in memory for integration tests; provides `WebApplicationFactory<Program>`. |

Notice line 28: `<ProjectReference Include="..\..\src\Phonebook.Api\Phonebook.Api.csproj" />` — the test project references the API project directly, which is why `Program` is visible inside the tests.

### Why are API and tests separate projects?

- You can run tests without shipping test code in production.
- Tests stay isolated: test-only packages never enter the API's output.
- Different publish/`pack` behavior (`IsPackable false` in the test csproj).
- A test project is allowed to be messy; the API project stays clean.

---

## Byte 2.4 — The project tree

```
backend-dotnet/
├── Phonebook.sln                              # groups test project under tests/
├── src/
│   └── Phonebook.Api/
│       ├── Phonebook.Api.csproj               # Web SDK, net8.0, EF Core + Npgsql
│       ├── Program.cs                         # startup: config, DI, pipeline, run
│       ├── appsettings.json                   # base config (port, connection string)
│       ├── appsettings.Development.json       # dev-only config overrides
│       ├── Controllers/
│       │   └── ContactsController.cs          # HTTP endpoints
│       ├── Services/
│       │   └── ContactService.cs              # business rules, validation, mapping
│       ├── Models/
│       │   └── Contact.cs                     # the database entity
│       ├── Data/
│       │   ├── PhonebookDbContext.cs          # EF Core DbContext + model config
│       │   ├── IContactRepository.cs          # repository contract (+ ContactSortSpec)
│       │   ├── ContactRepository.cs           # EF Core implementation
│       │   └── ContactDataSeeder.cs           # IHostedService; seeds 1000 records
│       ├── Dtos/
│       │   ├── ContactCreateRequest.cs        # body of POST/PUT
│       │   ├── ContactResponse.cs             # a single contact returned to client
│       │   └── ContactPageResponse.cs         # paginated list envelope
│       ├── Exceptions/
│       │   ├── ContactNotFoundException.cs
│       │   ├── DuplicateContactException.cs
│       │   └── InvalidContactException.cs
│       └── Middleware/
│           └── ErrorHandlingMiddleware.cs     # global exception -> JSON
└── tests/
    └── Phonebook.Api.Tests/
        ├── Phonebook.Api.Tests.csproj         # xUnit + Moq + WebApplicationFactory
        ├── GlobalUsings.cs                    # `global using Xunit;`
        ├── PhonebookApiFactory.cs             # replaces DB/repo with in-memory ones
        ├── InMemoryContactRepository.cs       # fake repository used by integration tests
        ├── ContactsControllerTests.cs         # 40 integration tests (HTTP level)
        ├── ContactServiceTests.cs             # 23 unit tests (Moq, service only)
        └── ContactDataSeederTests.cs          # 6 seeder tests (determinism, uniqueness)
```

### Purpose of each file — one line each

- `Program.cs` — the entry point; where everything is configured and started.
- `ContactsController.cs` — maps HTTP verbs/URLs to service calls; returns status codes.
- `ContactService.cs` — enforces business rules, builds `ContactPageResponse`, maps entity<->DTO.
- `Contact.cs` — the shape of one row in the `contacts` table.
- `PhonebookDbContext.cs` — the database session; defines the model and indexes.
- `IContactRepository.cs` / `ContactRepository.cs` — the data-access contract and its EF Core implementation.
- `ContactDataSeeder.cs` — fills the table to 1000 rows on startup if needed.
- `Dtos/*` — what comes in (`ContactCreateRequest`) and what goes out (`ContactResponse`, `ContactPageResponse`).
- `Exceptions/*` — typed errors the service throws and middleware translates.
- `ErrorHandlingMiddleware.cs` — converts any exception into a `{ "detail": "..." }` JSON response.

---

# PART 3 — Program.cs

## Byte 3.1 — Program.cs: the whole file, block by block

`Program.cs` is the startup file. Everything the app needs is declared here before `app.Run()`.

### Block 1 — Connection string helper (lines 10-32)

```csharp
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
```

What it does: looks for the environment variables `DATABASE_HOST/PORT/NAME/USERNAME/PASSWORD`. If **any** of them is set, it builds a connection string from them (filling each missing value with a sensible default via `??`). If none is set, it falls back to the `Phonebook` connection string from `appsettings.json`. This is how you can run the same app locally and inside Docker with different settings.

The `!` at the end (`configuration.GetConnectionString("Phonebook")!`) is the null-forgiving operator — "I promise this won't be null". It suppresses the nullable warning because a connection string in appsettings is guaranteed by convention.

### Block 2 — Build the web application builder (line 34-39)

```csharp
var builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}
```

`CreateBuilder(args)` gives you a `WebApplicationBuilder` — a configuration object you use to register services **before** the app exists. `builder.WebHost.UseUrls(...)` overrides the listening port if `PORT` is set in the environment. If `PORT` is not set, the app listens wherever `appsettings.json` says — which is `http://0.0.0.0:8080` (see `appsettings.json:10`).

### Block 3 — Services registration (lines 41-57)

```csharp
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
```

- `AddControllers()` — registers all controllers and the MVC machinery that runs them.
- `.AddJsonOptions(...)` — sets the **camelCase** naming policy for JSON (see Byte 6.4 for how this interacts with `phone_number`).
- `Configure<ApiBehaviorOptions>(...)` — replaces the default automatic 400-response with `ErrorResponseFactory.Create` (defined at the bottom of `Program.cs`, lines 71-94). This is what makes body-reading failures return `"detail": "Request body must contain valid JSON."` instead of a big ASP.NET error object.
- `AddDbContext<PhonebookDbContext>(...)` — registers the DbContext with **Scoped** lifetime and tells it to use Npgsql to talk to PostgreSQL via the computed connection string.
- `AddScoped<IContactRepository, ContactRepository>()` — maps the interface to its implementation with **Scoped** lifetime.
- `AddScoped<ContactService>()` — registers the service; its constructor argument `IContactRepository` is resolved by DI automatically.
- `AddHostedService<ContactDataSeeder>()` — starts the seeder when the app starts (see Part 18 for the lifetime gotcha).

### Block 4 — Build the app (line 59)

```csharp
var app = builder.Build();
```

`builder.Build()` turns all the registrations into a working `WebApplication`. **Before** this line there is no pipeline yet; **after** it, the services are ready and you are configuring the middleware pipeline.

### Block 5 — The HTTP pipeline (lines 61-65)

```csharp
app.UseMiddleware<ErrorHandlingMiddleware>();

app.MapControllers();

app.Run();
```

- `app.UseMiddleware<ErrorHandlingMiddleware>()` — adds the error handler to the pipeline. Because it is registered **first**, it runs **first** and wraps everything after it.
- `app.MapControllers()` — registers the controller routes into the endpoint routing system, so a request to `/contacts` is dispatched to `ContactsController`.
- `app.Run()` — blocks and starts the server. It stays alive, listening forever (until the process is killed).

### Block 6 — `public partial class Program` (lines 67-69)

```csharp
public partial class Program
{
}
```

This enables the integration tests. `WebApplicationFactory<Program>` (used in `PhonebookApiFactory.cs:11`) needs a public `Program` class to bootstrap the app from the test project. The `partial` keyword means "this class is split across files" — fine here, the file-scoped nature of top-level statements requires the explicit hook.

### Block 7 — `ErrorResponseFactory` (lines 71-94)

```csharp
internal static class ErrorResponseFactory
{
    public static IActionResult Create(ActionContext context)
    {
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
```

This is called whenever model binding/validation fails. Two cases:

1. **Body-level failures** (malformed JSON, empty body, missing body, type mismatch in an object) get registered under special ModelState keys `"$"`, `""`, or `"request"`. When found, respond `400` with `{"detail":"Request body must contain valid JSON."}`.
2. **Property-level failures** (e.g. `[Required]` failed) — pick the first error message and respond `400` with `{"detail":"<that message>"}`.

The result is a **single-field error envelope** the frontend understands — see `ContactsControllerTests.cs` tests like `ValidatesRequiredName` (line 118-127) and `RejectsMalformedJson` (line 448-456).

### The difference between `builder.Services` and `app`

- `builder.Services` is the **DI container configuration**. Before `Build()`, you only register; nothing is created yet.
- `app` is the **running application**. After `Build()`, you configure the pipeline (`app.Use...`, `app.Map...`) and then run it.
- Rule of thumb: `builder.` = "set things up", `app.` = "arrange the pipeline", and services are resolved automatically when a request arrives.

### Middleware ordering — why it matters

Middleware runs in the order you register it. `ErrorHandlingMiddleware` is registered first, so:

```
request → ErrorHandlingMiddleware → controllers → exception thrown → ErrorHandlingMiddleware
                                                                        (try/catch catches it)
```

If it were registered *after* something else had already started writing to the response, the error handler could not rewrite the response cleanly. That is why the global error handler must be near the front of the pipeline.

### The startup flow, end to end

1. `dotnet run` (or Docker) launches the process.
2. `WebApplication.CreateBuilder(args)` reads config sources (appsettings files, then environment variables).
3. Services are registered: DbContext (Npgsql), repository, service, seeder.
4. `builder.Build()` materializes everything.
5. `ErrorHandlingMiddleware` is added to the pipeline, then controller routing.
6. The app starts listening on the configured URL (env `PORT`, else `http://0.0.0.0:8080`).
7. Hosted services start — the seeder checks and fills the database (Part 18).
8. `app.Run()` blocks; the server accepts requests forever.

### What should you be able to answer?

- In what order does the app start?
- What is the difference between `builder.Services` and `app`?
- Why must `ErrorHandlingMiddleware` be registered first?
- Where does the connection string come from, and when do environment variables override appsettings?
- Why is there a `public partial class Program`?

---

# PART 4 — DEPENDENCY INJECTION

## Byte 4.1 — What dependency injection is, using this project

### The plain-language version

Dependency Injection (DI) means: **the framework creates the objects your class needs, and hands them to it**, instead of the class creating them itself.

Without DI, `ContactRepository` would have to do:

```csharp
var db = new PhonebookDbContext();   // who creates this? when? how do I swap it?
```

With DI, `ContactRepository` just declares "I need a `PhonebookDbContext`" in its constructor:

```csharp
public ContactRepository(PhonebookDbContext db)   // ContactRepository.cs:15
{
    _db = db;
}
```

…and ASP.NET Core hands one over whenever it creates the repository.

### The chain of injection in this project

```
ContactsController needs ContactService        -> constructor param (ContactsController.cs:13)
ContactService      needs IContactRepository   -> constructor param (ContactService.cs:15)
ContactRepository   needs PhonebookDbContext   -> constructor param (ContactRepository.cs:15)
PhonebookDbContext  needs DbContextOptions     -> constructor param (PhonebookDbContext.cs:8)
```

Look closely at `PhonebookDbContext`:

```csharp
public PhonebookDbContext(DbContextOptions<PhonebookDbContext> options)
    : base(options)
```

It has *no default constructor* and takes `DbContextOptions<PhonebookDbContext>` — which is built by `AddDbContext(... `options.UseNpgsql(connectionString))`` in `Program.cs:54`. The DI container builds *that* too, from the registration. One registration may compose others.

### The three characters

1. **Interfaces** — contracts. Here, `IContactRepository`. The service codes against `IContactRepository`, not `ContactRepository`, so tests can substitute `InMemoryContactRepository`.
2. **Implementations** — the real classes: `ContactRepository`, `ContactService`, `PhonebookDbContext`.
3. **Registrations** — the lines in `Program.cs` that tell the container "whenever someone asks for X, give them Y":
   - `builder.Services.AddDbContext<PhonebookDbContext>(...)` — line 54
   - `builder.Services.AddScoped<IContactRepository, ContactRepository>()` — line 55
   - `builder.Services.AddScoped<ContactService>()` — line 56
   - `builder.Services.AddHostedService<ContactDataSeeder>()` — line 57

### Service lifetimes — what matters in this project

ASP.NET Core DI has three lifetimes. Only two are used here:

| Lifetime | Meaning | Used in this project |
|---|---|---|
| **Singleton** | One instance for the whole app, shared by every request. | `IContactRepository` in the **test factory** (`PhonebookApiFactory.cs:29`, `AddSingleton`), and the hosted seeder (`ContactDataSeeder` is a singleton `IHostedService`). |
| **Scoped** | One instance per **HTTP request** (per scope). | `PhonebookDbContext`, `IContactRepository`, `ContactService` in the real app. |
| **Transient** | A new instance every time anyone asks. | Not used in this project. |

### Why is `PhonebookDbContext` Scoped?

EF Core's `DbContext` is designed for a short unit of work. When a request arrives, ASP.NET Core creates one scope (one container "bucket") for that request. The controller, service, repository, and DbContext all get instances from that same bucket. So during one request, you use **one** DbContext — which EF Core tracks and can `SaveChanges` once. When the request ends, the scope is disposed and the DbContext is disposed with it.

The test project uses `AddSingleton<IContactRepository>(_repository)` — one fake repository shared by all tests, reset explicitly by `_factory.Repository.Reset()` in each test constructor (`ContactsControllerTests.cs:19`). That's a *test-time* choice, different from the production registration.

### The seeder lifetime problem (important real bug, fixed)

The seeder is registered with `AddHostedService<ContactDataSeeder>()`. A hosted service is effectively a **singleton** — one instance for the whole app lifetime, created at startup.

The seeder's `StartAsync` needs to talk to the database through `PhonebookDbContext` and `IContactRepository`, which are **scoped**. A scoped service is tied to a request's scope — there is no request scope at startup.

Why did it crash? The naive approach would be to inject `PhonebookDbContext` directly into `ContactDataSeeder`'s constructor (singleton) — but the container **cannot construct it**, because scoped services cannot be created at the "root" scope of a singleton (the root lives forever; a scoped service must be disposed with its scope, and the root is never disposed until shutdown). At best you'd get the DbContext created at startup and *never disposed*, and at worst the container throws at startup exactly because of the lifetime mismatch.

The fix — **`IServiceScopeFactory`**:

```csharp
private readonly IServiceScopeFactory _scopeFactory;
public ContactDataSeeder(IServiceScopeFactory scopeFactory, ILogger<ContactDataSeeder> logger)
{
    _scopeFactory = scopeFactory;
    _logger = logger;
}

public async Task StartAsync(CancellationToken cancellationToken)
{
    using IServiceScope scope = _scopeFactory.CreateScope();      // 1. create a fresh scope
    IServiceProvider services = scope.ServiceProvider;            // 2. get that scope's container
    PhonebookDbContext db = services.GetRequiredService<PhonebookDbContext>();   // 3. resolve scoped service
    IContactRepository repository = services.GetRequiredService<IContactRepository>();
    ...
}
```

Instead of *inheriting* scoped services, the singleton seeder **creates its own scope** on demand (Step 1), pulls scoped services from that scope (Step 2-3), uses them, and disposes the whole scope with `using` when `StartAsync` ends. This is the sanctioned pattern: "a singleton that needs scoped work creates a scope." See `ContactDataSeeder.cs:97-115`.

### Mental model

DI = a factory ("the container") with a recipe book (registrations). When a class is needed, the container reads its constructor, checks the recipe book, bakes every ingredient, and hands over the finished object. If the class is Scoped, each new request gets its own freshly baked copy from its own kitchen (scope).

### What should you be able to answer?

- Who creates `ContactService`? `ContactRepository`? `PhonebookDbContext`?
- Why can't the seeder just take `PhonebookDbContext` in its constructor?
- What does `IServiceScopeFactory` do?
- Why is the DbContext scoped in production but the test repository singleton in tests?

### Common beginner confusion

"Do I need to `new` everything myself?" No. You only register, and the container resolves. When you see `AddScoped<TInterface, TClass>()`, you are promising: "for every request, create a `TClass` when `TInterface` is requested." The container figures out the constructor arguments from *their own* registrations automatically.

---

# PART 5 — THE CONTROLLER

## Byte 5.1 — ContactsController: the HTTP face of the backend

### What is a controller?

A class that maps incoming HTTP requests to C# methods and returns HTTP responses.

### The class declaration

```csharp
[ApiController]                                   // ContactsController.cs:7
[Route("contacts")]                               // line 8
public sealed class ContactsController : ControllerBase
```

- `[ApiController]` — an attribute that switches on API behavior:
  - Automatic **model validation**: if the request body fails validation, a `400` is produced *before* the action body runs, using the `InvalidModelStateResponseFactory` configured in `Program.cs:47-50`.
  - Automatic binding inference: simple types are bound from the route/query; complex DTO types are bound from the body.
  - Automatic `BadRequest` details — again overridden here by `ErrorResponseFactory`.
- `[Route("contacts")]` — the URL segment this controller serves. Combined with the action routes this yields `/contacts`, `/contacts/{id}`.
- `ControllerBase` — the base class providing helpers like `Ok(...)`, `CreatedAtAction(...)`, and a `Request`/`Response` handle.

Note the full public path: Nginx maps `/api/contacts` → `/contacts` (Part 22). Internally the backend never knows about `/api`.

### Constructor injection

```csharp
private readonly ContactService _service;

public ContactsController(ContactService service)
{
    _service = service;
}
```

The controller depends only on the **service**, never on the repository or DbContext. That keeps the HTTP layer free of business rules.

### Each endpoint, with the full flow

#### GET `/contacts` — list with pagination/search/sort (lines 18-29)

```csharp
[HttpGet]
public async Task<ActionResult<ContactPageResponse>> GetContacts(
    [FromQuery] int page = 0,
    [FromQuery] int size = 20,
    [FromQuery] string? search = null,
    [FromQuery] string? sort = null,
    CancellationToken cancellationToken = default)
{
    ContactPageResponse result =
        await _service.GetContactsAsync(page, size, search, sort, cancellationToken);
    return Ok(result);
}
```

- Query parameters come from the URL: `/api/contacts?page=0&size=20&search=rahul&sort=createdAt,desc`.
- Defaults: `page=0`, `size=20`.
- `Ok(result)` → **HTTP 200** with the `ContactPageResponse` JSON body.

Flow: `GET /api/contacts` → Nginx strips `/api` → `ContactsController.GetContacts` → `ContactService.GetContactsAsync` → `ContactRepository.GetPageAsync` → `SELECT count(*)` + `SELECT ... LIMIT/OFFSET` in PostgreSQL → JSON back out.

#### POST `/contacts` — create (lines 31-39)

```csharp
[HttpPost]
public async Task<ActionResult<ContactResponse>> CreateContact(
    [FromBody] ContactCreateRequest request,
    CancellationToken cancellationToken)
{
    ContactResponse created =
        await _service.CreateContactAsync(request, cancellationToken);
    return CreatedAtAction(nameof(GetContact), new { id = created.Id }, created);
}
```

- `[FromBody]` — the JSON body is deserialized into `ContactCreateRequest`.
- `CreatedAtAction(nameof(GetContact), new { id = created.Id }, created)` — **HTTP 201** (Created), and it also emits a `Location` header pointing to the new resource (i.e. `GET /contacts/{id}`). The web body is `created`.

Flow: `POST /api/contacts` with `{"name":"...","phone_number":"..."}` → controller → service (validates, checks duplicates) → repository → `INSERT INTO contacts ...` → returns the created contact with its DB-assigned `id`.

#### GET `/contacts/{id}` — one contact (lines 41-48)

```csharp
[HttpGet("{id}")]
public async Task<ActionResult<ContactResponse>> GetContact(
    int id,
    CancellationToken cancellationToken)
{
    ContactResponse result = await _service.GetContactAsync(id, cancellationToken);
    return Ok(result);
}
```

- `{id}` is a **route parameter** (part of the URL path). It is a `int`, so `/contacts/abc` is rejected by routing before your code runs.
- Returns **200** with `ContactResponse`; if not found, the service throws `ContactNotFoundException` and the middleware converts it to **404** (Part 16).

#### PUT `/contacts/{id}` — update (lines 50-59)

```csharp
[HttpPut("{id}")]
public async Task<ActionResult<ContactResponse>> UpdateContact(
    int id,
    [FromBody] ContactCreateRequest request,
    CancellationToken cancellationToken)
{
    ContactResponse result =
        await _service.UpdateContactAsync(id, request, cancellationToken);
    return Ok(result);
}
```

- Same body DTO as POST. The service loads the existing row by `id`, applies the new values, saves. Returns **200**.

#### DELETE `/contacts/{id}` — delete (lines 61-68)

```csharp
[HttpDelete("{id}")]
public async Task<ActionResult> DeleteContact(
    int id,
    CancellationToken cancellationToken)
{
    await _service.DeleteContactAsync(id, cancellationToken);
    return Ok(new { message = "Contact deleted successfully." });
}
```

- Returns **200** with `{"message":"Contact deleted successfully."}`. (Not 204 `No Content` — this particular port returns a message body; the test `DeletesContact` asserts the message at `ContactsControllerTests.cs:111`.)

### Status code summary

| Method | Path | Success code | Response body type |
|---|---|---|---|
| GET | `/contacts` | 200 | `ContactPageResponse` |
| GET | `/contacts/{id}` | 200 | `ContactResponse` |
| POST | `/contacts` | 201 (with `Location` header) | `ContactResponse` |
| PUT | `/contacts/{id}` | 200 | `ContactResponse` |
| DELETE | `/contacts/{id}` | 200 | `{"message": "Contact deleted successfully."}` |

### What calls/uses the controller?

- The MVC routing machinery (`app.MapControllers()` in `Program.cs:64`).
- The `ErrorHandlingMiddleware` wraps every request (Part 3 Byte 5, Part 16).
- Model validation happens **before** the action via `[ApiController]`.

### What the controller calls

- `ContactService` methods only: `GetContactsAsync`, `CreateContactAsync`, `GetContactAsync`, `UpdateContactAsync`, `DeleteContactAsync`.

### What should you be able to answer?

- Why does the controller not talk to the repository directly?
- For each HTTP verb: what route, what input, what service call, what status code?
- What does `CreatedAtAction` do beyond returning 201?

### Common beginner confusion

"PUT vs POST" — here both use the same request DTO. The difference is semantics: POST creates a *new* resource (server picks the id), PUT updates an *existing* one (client supplies the id in the URL). Both call back `ContactResponse`.

---

# PART 6 — DTOs

## Byte 6.1 — Why DTOs exist at all

The database entity `Contact` has exactly the fields the UI needs — so why not return `Contact` from the controller and skip the DTO classes?

Three reasons:

1. **Over the wire shapes differ from database shapes.** The external field is `phone_number`; the C# entity property is `PhoneNumber`; the DB column is `phone_number`. Mapping is explicit and centralized in the DTO.
2. **Internal details don't leak.** If the entity later gains an internal column (say a `LastModifiedBy` or a soft-delete flag), returning the entity would expose it to every API consumer.
3. **Request, Response, and Entity can evolve independently.** The request DTO has validation attributes and trimming; the entity has none. They are separate by design.

## Byte 6.2 — The three DTOs

### `ContactCreateRequest` — what the client sends (POST/PUT body)

```csharp
public sealed class ContactCreateRequest          // Dtos/ContactCreateRequest.cs
{
    private string? _name;

    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(255, ErrorMessage = "Name cannot exceed 255 characters.")]
    public string? Name
    {
        get => _name;
        set => _name = TrimToNull(value);
    }

    [JsonPropertyName("phone_number")]
    [Required(ErrorMessage = "Phone number is required.")]
    public string? PhoneNumber { get; set; }

    [EmailAddress(ErrorMessage = "Invalid email address.")]
    [MaxLength(255, ErrorMessage = "Email cannot exceed 255 characters.")]
    public string? Email { get; set; }

    [MaxLength(10000, ErrorMessage = "Address cannot exceed 10000 characters.")]
    public string? Address { get; set; }

    private static string? TrimToNull(string? value) { /* trims; empty -> null */ }
}
```

Key points:

- Property types are `string?` (nullable) because **model binding can always fail** — the JSON might omit a field. Validation attributes then flag missing/invalid values *after* binding.
- The private `TrimToNull` setter means incoming `"   Jane   "` is stored as `"Jane"` and `"   "` becomes `null`. That's why `ValidatesBlankNameTrimsToRequired` (`ContactsControllerTests.cs:129-138`) expects "Name is required." for a blank name: after trimming, `Name` is `null`, and `[Required]` fires.
- `[JsonPropertyName("phone_number")]` forces the JSON key to snake_case even though the C# property is `PhoneNumber`. See Byte 6.4.

### `ContactResponse` — what the server sends back for one contact

```csharp
public sealed class ContactResponse               // Dtos/ContactResponse.cs
{
    public string? Name { get; init; }
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public int Id { get; init; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; init; }

    public static ContactResponse From(Contact contact)
    {
        return new ContactResponse
        {
            Name = contact.Name,
            PhoneNumber = contact.PhoneNumber,
            Email = contact.Email,
            Address = contact.Address,
            Id = contact.Id,
            CreatedAt = contact.CreatedAt,
        };
    }
}
```

- Properties are `init`-only (set once at construction/mapping — good practice for response DTOs).
- The static `From(Contact)` method is the **entity → response mapper**. The service calls it; see `ContactService.cs:51-52`, `64`, `77`, `91`.

### `ContactPageResponse` — the pagination envelope

```csharp
public sealed class ContactPageResponse           // Dtos/ContactPageResponse.cs
{
    public List<ContactResponse> Content { get; set; } = new();
    public int Page { get; set; }
    public int Size { get; set; }
    public long TotalElements { get; set; }
    public int TotalPages { get; set; }
    public bool First { get; set; }
    public bool Last { get; set; }
}
```

This is the "list page" shape. The `Content` items are already-mapped `ContactResponse`s. The rest is metadata the frontend needs to render pagination controls.

## Byte 6.3 — Request DTO vs Response DTO vs Entity

| | `Contact` (entity) | `ContactCreateRequest` (request DTO) | `ContactResponse` (response DTO) |
|---|---|---|---|
| Direction | database internal | client → server | server → client |
| Has `Id` | yes | no (server generates) | yes |
| Has `CreatedAt` | yes (set by repo/server) | no (client must not set it) | yes |
| Validation attributes | none | `[Required]`, `[MaxLength]`, `[EmailAddress]` | none |
| Trimming | none | yes (setter does it) | none |

Notice the request DTO has no `Id` and no `CreatedAt` — the client has no business setting either. The repository stamps `CreatedAt` (`ContactRepository.cs:77-80`), and the database generates `Id` (`UseIdentityByDefaultColumn()`).

## Byte 6.4 — JSON naming: how `PhoneNumber` becomes `phone_number`

Two rules combine:

1. **Global camelCase policy** (`Program.cs:44`): property `PhoneNumber` normally serializes as `phoneNumber`, `CreatedAt` as `createdAt`.
2. **`[JsonPropertyName]` overrides the policy** for specific properties: `phone_number` and `created_at`.

So the final JSON for a contact is:

```json
{
  "name": "Rahul Sharma",
  "phone_number": "9876500000",
  "email": "rahul.sharma@example.com",
  "address": "MG Road, Mumbai",
  "id": 1,
  "created_at": "2026-01-01T00:00:00Z"
}
```

The test `CreatesContact` (`ContactsControllerTests.cs:57-64`) reads `phone_number` and `created_at` — confirming the override works end to end.

Deserialization is the reverse: when a request JSON has `phone_number`, `[JsonPropertyName]` maps it onto the C# `PhoneNumber` property.

### What should you be able to answer?

- Why not return the entity directly?
- Which DTO carries validation, and which carries the id?
- How does `phone_number` in JSON map to `PhoneNumber` in C#?
- Where does `Contact.CreateAt` properly exist vs `ContactResponse.CreatedAt`?

---

# PART 7 — MODEL / ENTITY

## Byte 7.1 — Contact.cs

### The file, whole

```csharp
namespace Phonebook.Api.Models;

public sealed class Contact                    // Models/Contact.cs
{
    public int Id { get; set; }                 // line 5
    public string Name { get; set; } = null!;   // line 7
    public string PhoneNumber { get; set; } = null!;  // line 9
    public string? Email { get; set; }          // line 11
    public string? Address { get; set; }        // line 13
    public DateTime CreatedAt { get; set; }     // line 15
}
```

### Line by line

- `public sealed class Contact` — `sealed` means no inheritance; the compiler can optimize and it signals intent.
- `int Id` — the primary key. EF Core convention: a property named `Id` (or `ClassNameId`) of a numeric type is automatically the key. The DbContext also declares it explicitly (`PhonebookDbContext.cs:21`).
- `string Name = null!;` — non-nullable, but initialized to `null!` to silence the compiler warning. `null!` literally means "this is null, but trust me it'll be set before use." EF Core sets it from the DB row. `Name` and `PhoneNumber` are always present per the schema (`IsRequired` in the DbContext).
- `string? Email` / `string? Address` — nullable; a contact may have neither. `IsRequired` is *not* set for them in the DbContext.
- `DateTime CreatedAt` — when the row was created. The repository stamps it as UTC if unset (`ContactRepository.cs:77-80`).

### How this maps to PostgreSQL

```
C# Contact              EF Core (PhonebookDbContext)      PostgreSQL column
Id       <------------>  HasKey, UseIdentityByDefaultColumn  id  serial/identity PK
Name     <------------>  IsRequired, MaxLength(255)          name  varchar(255) NOT NULL
PhoneNumber <--------->  IsRequired, MaxLength(32)           phone_number varchar(32) NOT NULL  UNIQUE
Email    <------------>  MaxLength(255)                      email  varchar(255)  UNIQUE  (null allowed, nulls don't collide)
Address  <------------>  columnType "text"                   address text
CreatedAt <----------->  IsRequired, "timestamp with time zone"  created_at timestamptz NOT NULL
```

The C# names differ from column names (`PhoneNumber` → `phone_number`), so the DbContext configures `.HasColumnName(...)` for every property — nothing is left to convention.

### What should you be able to answer?

- What is the primary key and who generates it?
- Which fields cannot be null, which can, and where is that decided?
- Why does `Name` have `= null!;`?
- What is `created_at`'s type in the database?

---

# PART 8 — DbContext

## Byte 8.1 — PhonebookDbContext

### What is a DbContext?

The `DbContext` is the *session* between your code and the database. It is three things at once:

1. **Query surface** — its `DbSet<Contact>` lets you write `_db.Contacts` LINQ queries.
2. **Model configuration** — via `OnModelCreating`, it defines the schema EF Core will read/write.
3. **Change tracker** — it watches the objects you add/update/remove and writes the changes in `SaveChangesAsync`.

### The code

```csharp
public sealed class PhonebookDbContext : DbContext          // Data/PhonebookDbContext.cs
{
    public PhonebookDbContext(DbContextOptions<PhonebookDbContext> options)
        : base(options)                                      // line 8-11
    {
    }

    public DbSet<Contact> Contacts => Set<Contact>();        // line 13

    protected override void OnModelCreating(ModelBuilder modelBuilder)   // line 15
    {
        modelBuilder.Entity<Contact>(entity =>
        {
            entity.ToTable("contacts");                      // 19
            entity.HasKey(c => c.Id);                        // 21
            entity.Property(c => c.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd()
                .UseIdentityByDefaultColumn();               // 23-26  (serial/identity)
            entity.Property(c => c.Name)
                .HasColumnName("name").IsRequired().HasMaxLength(255);          // 28-31
            entity.Property(c => c.PhoneNumber)
                .HasColumnName("phone_number").IsRequired().HasMaxLength(32);   // 33-36
            entity.Property(c => c.Email)
                .HasColumnName("email").HasMaxLength(255);                       // 38-40
            entity.Property(c => c.Address)
                .HasColumnName("address").HasColumnType("text");                     // 42-44
            entity.Property(c => c.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired()
                .HasColumnType("timestamp with time zone");                          // 46-49
            entity.HasIndex(c => c.PhoneNumber).IsUnique();   // 51
            entity.HasIndex(c => c.Email).IsUnique();         // 52
            entity.HasIndex(c => new { c.CreatedAt, c.Id });  // 53
        });
    }
}
```

### What this configuration means

- `.ToTable("contacts")` — the table is literally named `contacts`.
- `UseIdentityByDefaultColumn()` — id is a PostgreSQL identity column (`GENERATED BY DEFAULT AS IDENTITY`), so PostgreSQL assigns ids.
- `IsRequired()` → `NOT NULL`. `HasMaxLength(n)` → `varchar(n)`.
- `HasColumnType("text")` → an arbitrary-length `text` column, matching the 10,000-char address limit.
- `.HasIndex(c => c.PhoneNumber).IsUnique()` → `CREATE UNIQUE INDEX ... ON contacts (phone_number)`. This is the **database-level** backstop for duplicate phone numbers (and email, line 52). Note PostgreSQL unique indexes allow multiple `NULL` values, so two contacts with no email do not collide.
- `HasIndex(c => new { c.CreatedAt, c.Id })` — a composite index that makes the default sort (`created_at DESC, id DESC`) fast.

### What happens when code does `_context.Contacts...`

No database work yet! `_db.Contacts` only gives you an `IQueryable<Contact>`:

1. You call LINQ operators (`Where`, `OrderBy`, `Skip`, `Take`) — EF Core **builds an expression tree**; still no I/O.
2. When you call an executor like `ToListAsync()`, `LongCountAsync()`, or `FirstOrDefaultAsync()`, EF Core translates the expression tree into a SQL `SELECT`, Npgsql sends it over the connection, PostgreSQL runs it, and EF Core **materializes** each row into a `Contact` object it hands you.
3. For writes, `_db.Contacts.Add(contact)` just adds an object to the **change tracker** with state `Added`. Nothing reaches the database until `SaveChangesAsync()` — which computes the `INSERT`/`UPDATE`/`DELETE` statements and runs them in a transaction.

This is why `ContactRepository` wraps writes in `_ + SaveChanges*Async` methods — the repository owns the "when to flush" decision.

### What should you be able to answer?

- What are the three jobs of a DbContext?
- Where is the composite index and why does it exist?
- What is the difference between building a query and executing it?
- Why can two contacts both have `null` email despite the unique index?

---

# PART 9 — REPOSITORY

## Byte 9.1 — The Repository Pattern and IContactRepository

### What is the pattern?

The **Repository Pattern** hides all data-access code behind an interface. Services depend on the *interface* (`IContactRepository`), not the *implementation* (`ContactRepository`). Benefits in this project:

- Swap the real repository for `InMemoryContactRepository` in tests (that swap is the entire basis of the integration tests — see Part 20).
- Keep SQL/EF details out of the service/controller.
- Naming reads like a domain action, not a SQL command: `ExistsByPhoneNumberAsync`, `GetByIdAsync`.

### `IContactRepository` (Data/IContactRepository.cs)

```csharp
public sealed record ContactSortSpec(string Property, bool Descending);   // line 5

public interface IContactRepository
{
    Task<(IReadOnlyList<Contact> Items, long TotalElements)> GetPageAsync(
        int page, int size, ContactSortSpec sort, string? search, CancellationToken cancellationToken);
    Task<Contact?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<bool> ExistsByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<bool> ExistsByPhoneNumberAndIdNotAsync(string phoneNumber, int id, CancellationToken cancellationToken);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken);
    Task<bool> ExistsByEmailAndIdNotAsync(string email, int id, CancellationToken cancellationToken);
    Task<Contact> AddAsync(Contact contact, CancellationToken cancellationToken);
    Task AddRangeAsync(IReadOnlyCollection<Contact> contacts, CancellationToken cancellationToken);
    Task UpdateAsync(Contact contact, CancellationToken cancellationToken);
    Task DeleteAsync(Contact contact, CancellationToken cancellationToken);
    Task<long> CountAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Contact>> GetAllAsync(CancellationToken cancellationToken);
}
```

- Every method is `async` and takes a `CancellationToken` for cooperative cancellation.
- `GetPageAsync` returns a **tuple** of `(Items, TotalElements)` — it bundles the page AND the total count in one round trip so the caller computes pagination metadata from real numbers.
- `ContactSortSpec` is a `record` — an immutable-ish little value type holding "sort by property X, descending?" It travels from service to repository so the repository decides the SQL.

## Byte 9.2 — ContactRepository: the methods, one by one

### `GetPageAsync` (lines 20-48)

```csharp
IQueryable<Contact> query = _db.Contacts.AsNoTracking();

if (search is not null)
{
    string pattern = "%" + EscapeLike(search) + "%";
    query = query.Where(c =>
        EF.Functions.ILike(c.Name, pattern, LikeEscapeChar) ||
        EF.Functions.ILike(c.PhoneNumber, pattern, LikeEscapeChar) ||
        (c.Email != null && EF.Functions.ILike(c.Email, pattern, LikeEscapeChar)) ||
        (c.Address != null && EF.Functions.ILike(c.Address, pattern, LikeEscapeChar)));
}

long totalElements = await query.LongCountAsync(cancellationToken);

IQueryable<Contact> ordered = ApplySort(query, sort);
List<Contact> items = await ordered
    .Skip(page * size)
    .Take(size)
    .ToListAsync(cancellationToken);

return (items, totalElements);
```

1. `AsNoTracking()` — read-only query; EF Core won't watch these objects, which is cheaper.
2. If `search` is present, it builds one `WHERE` with **four** `ILIKE` conditions: name, phone, and (if not null) email and address. `EF.Functions.ILike` is the PostgreSQL case-insensitive `ILIKE` (`~*`). Pattern is `%search%` — substring match anywhere in the value.
3. `LongCountAsync` runs `SELECT COUNT(*)` with the same `WHERE`.
4. `ApplySort` then `Skip(page * size).Take(size)` → `OFFSET ... LIMIT ...`.
5. Both results return in the tuple.

### `GetByIdAsync` (lines 50-53)

```csharp
return await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
```

Runs `SELECT ... WHERE id = @id LIMIT 1`. Returns `null` if no row — the service turns that into `ContactNotFoundException` (`ContactService.cs:106-107`).

### The `Exists*` pair logic (lines 55-73)

- `ExistsByPhoneNumberAsync(phone)` → `AnyAsync(c => c.PhoneNumber == phone)` — used on CREATE.
- `ExistsByPhoneNumberAndIdNotAsync(phone, id)` → `AnyAsync(c => c.PhoneNumber == phone && c.Id != id)` — used on UPDATE so that the current row doesn't count as a duplicate of itself. (Test `ExcludesCurrentContactWhenCheckingDuplicatePhoneOnUpdate`, `ContactServiceTests.cs:42-61`.)

The same pair exists for email.

### Writes (lines 75-103)

- `AddAsync`: if `CreatedAt` was never set, stamp `DateTime.UtcNow` (lines 77-80); `_db.Contacts.Add(contact)` marks it `Added`; `SaveChangesTranslatedAsync` issues the `INSERT`; returns the contact **with the DB-generated `Id` populated**.
- `AddRangeAsync`: same idea for the seeder's 1000-row batch.
- `UpdateAsync`: `_db.Contacts.Update(contact)` marks every property `Modified`; `SaveChangesTranslatedAsync` issues an `UPDATE` of all columns.
- `DeleteAsync`: `_db.Contacts.Remove(contact)` marks `Deleted` → `DELETE`.

### `SaveChangesTranslatedAsync` — the unique-violation translator (lines 120-140)

```csharp
try
{
    await _db.SaveChangesAsync(cancellationToken);
}
catch (DbUpdateException exception) when (IsUniqueViolation(exception, out string constraintName))
{
    if (constraintName.Contains("phone", StringComparison.OrdinalIgnoreCase))
        throw new DuplicateContactException("Duplicate phone number.");
    if (constraintName.Contains("email", StringComparison.OrdinalIgnoreCase))
        throw new DuplicateContactException("Duplicate email.");
    throw new DuplicateContactException("Duplicate contact information.");
}
```

This is a **defense-in-depth** layer: the service already checks duplicates *before* saving (`EnsureUniqueAsync`), but two simultaneous requests could still race. The database unique indexes (Part 8) catch the loser, and this catch block converts the low-level `DbUpdateException` into a friendly business exception.

`IsUniqueViolation` (lines 142-151) unwraps the exception to find the `Npgsql.PostgresException` with `SqlState == "23505"` (PostgreSQL's unique violation code) and grabs the constraint name to decide whether it was the phone or the email index.

### `EscapeLike` (lines 153-167)

```csharp
private static string EscapeLike(string value)
{
    var builder = new StringBuilder(value.Length + 4);
    foreach (char c in value)
    {
        if (c == '\\' || c == '%' || c == '_') builder.Append(LikeEscapeChar);
        builder.Append(c);
    }
    return builder.ToString();
}
```

The user's search term is inside a `LIKE` pattern (`%...%`). If the user types `%` or `_`, those are `LIKE` wildcards — so searching for `100%` would otherwise match "anything". This escapes them with `\` (the project's `LikeEscapeChar`), so the search is literal. The middleware's ILIKE call also passes `LikeEscapeChar` as the escape character — that's why `EF.Functions.ILike(c.Name, pattern, LikeEscapeChar)` passes it as a third argument.

### `ApplySort` (lines 169-192)

```csharp
IOrderedQueryable<Contact> ordered = sort.Descending
    ? sort.Property switch
    {
        "name" => query.OrderByDescending(c => c.Name),
        "phoneNumber" => query.OrderByDescending(c => c.PhoneNumber),
        "email" => query.OrderByDescending(c => c.Email),
        "createdAt" => query.OrderByDescending(c => c.CreatedAt),
        "id" => query.OrderByDescending(c => c.Id),
        _ => throw new InvalidContactException("Unsupported sort field."),
    }
    : sort.Property switch { /* ... ascending variants ... */ };

return ordered.ThenByDescending(c => c.Id);
```

who always adds `ThenByDescending(c => c.Id)` as a **tie-breaker**, so ordering is stable even when two rows share the sort value. "Unsupported sort field" is a *business* error, thrown inside the data layer — the middleware will catch it as `InvalidContactException` → 400.

### LINQ used in this repository, tied to SQL

| LINQ | What it means | SQL it becomes |
|---|---|---|
| `.Where(...)` | filter rows | `WHERE ...` |
| `.OrderBy/.OrderByDescending` | sort | `ORDER BY ... ASC/DESC` |
| `.ThenByDescending` | secondary sort | `ORDER BY ..., id DESC` |
| `.Skip(n)` | drop first n rows | `OFFSET n` |
| `.Take(n)` | keep n rows | `LIMIT n` |
| `.FirstOrDefaultAsync()` | first row or null | `SELECT ... LIMIT 1` |
| `.LongCountAsync()` | count | `COUNT(*)` |
| `.AnyAsync(pred)` | does any row match? | `EXISTS (SELECT 1 ...)` |
| `AsNoTracking()` | don't track results | no SQL effect; EF behavior |
| `EF.Functions.ILike(...)` | case-insensitive contains | `LIKE ... ESCAPE '\'` / `ILIKE` |

### What should you be able to answer?

- What is the repository pattern and what does it buy here?
- Which repository method does each service method call?
- What does `Skip(page * size)` + `Take(size)` produce in SQL?
- Why is there a second "duplicate" check in the repository even though the service already checks?

### Common beginner confusion

"Repository vs DbContext" — both touch data. The repository owns *read/write operations shaped for the domain* and the `SaveChanges` timing; the DbContext is the generic EF mechanism underneath. You can replace the DbContext-backed repository with an in-memory one (tests) without changing callers.

---

# PART 10 — SERVICE LAYER

## Byte 10.1 — Why the service layer exists

`ContactService` exists so the controller stays thin (HTTP only) and the business rules live in one testable place. Almost every business rule in this project is verified by unit tests (`ContactServiceTests.cs`), which only work because the rules are in the service and the service depends on an interface (`IContactRepository`) that Moq can fake.

What lives in the service:

- input validation & normalization (trim, length caps)
- business rules (name has no digits, phone has exactly 10 digits)
- duplicate checks (and the create-vs-update difference)
- not-found handling
- pagination metadata math
- sort string parsing
- entity ↔ response mapping

## Byte 10.2 — Reading a page: `GetContactsAsync` (lines 20-60)

```csharp
public async Task<ContactPageResponse> GetContactsAsync(int page, int size, string? search, string? sort, CancellationToken ct)
{
    if (page < 0) throw new InvalidContactException("Page must be zero or greater.");
    if (size < 1 || size > 100) throw new InvalidContactException("Size must be between 1 and 100.");

    string? normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
    if (normalizedSearch is not null && normalizedSearch.Length > 100)
        throw new InvalidContactException("Search must not exceed 100 characters.");

    ContactSortSpec sortSpec = ParseSort(sort);

    (IReadOnlyList<Contact> items, long totalElements) =
        await _repository.GetPageAsync(page, size, sortSpec, normalizedSearch, cancellationToken);

    int totalPages = totalElements == 0 ? 0 : (int)Math.Ceiling(totalElements / (double)size);

    return new ContactPageResponse
    {
        Content = items.Select(ContactResponse.From).ToList(),
        Page = page,
        Size = size,
        TotalElements = totalElements,
        TotalPages = totalPages,
        First = page == 0,
        Last = page >= totalPages - 1,
    };
}
```

The story:

1. Validate `page` and `size` — business rule (throws before touching the DB).
2. Normalize search: whitespace-only means "no search"; otherwise `.Trim()` (so `" rahul "` → `"rahul"`). Note the unit test feeds `" jane "` and asserts the repo receives `"jane"` (`ContactServiceTests.cs:29,38`).
3. Cap search length at 100.
4. Parse the sort string (Byte 13) into a `ContactSortSpec`.
5. Ask the repository for `(items, totalElements)`.
6. Compute `totalPages`, `first`, `last`.
7. Map every `Contact` → `ContactResponse` (`ContactResponse.From`) and wrap in `ContactPageResponse`.

## Byte 10.3 — CRUD logic

### CREATE — `CreateContactAsync` (lines 67-78)

```csharp
ValidateBusinessRules(request);
await EnsureUniqueAsync(request, null, cancellationToken);

var contact = new Contact();
Apply(request, contact);
await _repository.AddAsync(contact, cancellationToken);
return ContactResponse.From(contact);
```

1. Business validation (name/phone format).
2. Uniqueness checks **excluding** nothing (`currentId = null`) → `ExistsByPhoneNumberAsync` / `ExistsByEmailAsync`.
3. Build a new entity, copy the (already-trimmed) DTO values onto it (`Apply`, lines 185-191).
4. `AddAsync` saves; the entity now has its DB `Id` and `CreatedAt`.
5. Map to response.

### READ one — `GetContactAsync` (lines 62-65)

```csharp
return ContactResponse.From(await FindContactAsync(id, cancellationToken));
```

### UPDATE — `UpdateContactAsync` (lines 80-92)

```csharp
ValidateBusinessRules(request);
Contact contact = await FindContactAsync(id, cancellationToken);
await EnsureUniqueAsync(request, id, cancellationToken);
Apply(request, contact);
await _repository.UpdateAsync(contact, cancellationToken);
return ContactResponse.From(contact);
```

Order matters: business rules first, then "does it exist?", then "is the new phone/email used by a **different** row?", then save. Passing `id` as `currentId` makes `EnsureUniqueAsync` use the `AndIdNot` variants, and the test `ExcludesCurrentContactWhenCheckingDuplicatePhoneOnUpdate` proves the correct variant is used (`ContactServiceTests.cs:55-60`).

### DELETE — `DeleteContactAsync` (lines 94-97)

```csharp
await _repository.DeleteAsync(await FindContactAsync(id, cancellationToken), cancellationToken);
```

It must fetch first, so a delete of a missing id correctly yields `ContactNotFoundException` (test `DeleteContactThrowsWhenNotFound`, `ContactServiceTests.cs:264-272`).

### Shared helpers

**`FindContactAsync`** (99-108): rejects `id <= 0` with `InvalidContactException("must be greater than 0")`, then `GetByIdAsync(...) ?? throw new ContactNotFoundException()`. The `?? throw` is "return what you find, or throw if null".

**`EnsureUniqueAsync`** (110-135):

```csharp
bool duplicatePhone = currentId is null
    ? await _repository.ExistsByPhoneNumberAsync(request.PhoneNumber!, cancellationToken)
    : await _repository.ExistsByPhoneNumberAndIdNotAsync(request.PhoneNumber!, currentId.Value, cancellationToken);
if (duplicatePhone) throw new DuplicateContactException("Duplicate phone number.");

if (request.Email is not null)
{
    bool duplicateEmail = currentId is null
        ? await _repository.ExistsByEmailAsync(request.Email, cancellationToken)
        : await _repository.ExistsByEmailAndIdNotAsync(request.Email, currentId.Value, cancellationToken);
    if (duplicateEmail) throw new DuplicateContactException("Duplicate email.");
}
```

**`ValidateBusinessRules`** (137-150):

```csharp
private static readonly Regex PhonePattern =
    new(@"^\+?[0-9][0-9\s().-]*$", RegexOptions.Compiled);     // ContactService.cs:11

if (request.Name is not null && request.Name.Any(char.IsDigit))
    throw new InvalidContactException("Name cannot contain numbers.");

if (request.PhoneNumber is not null &&
    (!PhonePattern.IsMatch(request.PhoneNumber) ||
     Regex.Replace(request.PhoneNumber, @"\D", "").Length != 10))
    throw new InvalidContactException("Phone number must contain exactly 10 digits.");
```

Phone rules, in English:
- The regex allows an optional leading `+`, then a digit, then any mix of digits/space/`()`, `.`, `-`. This permits "seeded" or formatted numbers like `+91 98765 01234`.
- `Regex.Replace(phone, @"\D", "")` removes every non-digit (`\D` = not a digit). The result must have **exactly 10 digits**.
- So `"123456789"` (9 digits) fails, `"+91 98765 01234"` (12 digits) fails, `"98765 01234"` passes.
- Confirmed by tests `RejectsPhoneWithoutTenDigits` (`ContactsControllerTests.cs:197-206`) and the service test at `ContactServiceTests.cs:177-182`.

**`Apply`** (185-191): copies the trimmed DTO values onto the entity. Because `ContactCreateRequest` setter already trimmed, values are clean.

### Where validation happens: framework vs business

Two layers (Part 14):

| Kind | Mechanism | Runs when | Example |
|---|---|---|---|
| Framework/model validation | `[Required]`, `[MaxLength]`, `[EmailAddress]` on `ContactCreateRequest`, enforced by `[ApiController]` | *Before* the controller action runs (model binding) | missing name → "Name is required." |
| Business validation | `if` checks throwing `InvalidContactException` in `ContactService` | *Inside* `CreateContactAsync`/`UpdateContactAsync` | name has a digit → "Name cannot contain numbers." |

### What should you be able to answer?

- Why is business logic in the service and not the controller?
- What is the exact create flow, update flow, delete flow?
- Which two checks protect uniqueness, and how do they differ for create vs update?
- What does `?? throw new ContactNotFoundException()` do?

---

# PART 11 — SEARCH

## Byte 11.1 — How search works

### The contract

`GET /api/contacts?search=<value>` — search is optional, a **query parameter**.

The search target is the whole record: name, phone number, email, and address. Any field matching makes the row appear.

### Input normalization (service)

`ContactService.GetContactsAsync` (lines 37-41):

```csharp
string? normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
if (normalizedSearch is not null && normalizedSearch.Length > 100)
    throw new InvalidContactException("Search must not exceed 100 characters.");
```

- `null`, `""`, `"   "` all become `null` → no filtering.
- Otherwise trim surrounding whitespace.
- > 100 characters → 400 (business rule).

### The database query (repository)

`ContactRepository.GetPageAsync` (lines 29-37):

```csharp
string pattern = "%" + EscapeLike(search) + "%";
query = query.Where(c =>
    EF.Functions.ILike(c.Name, pattern, LikeEscapeChar) ||
    EF.Functions.ILike(c.PhoneNumber, pattern, LikeEscapeChar) ||
    (c.Email != null && EF.Functions.ILike(c.Email, pattern, LikeEscapeChar)) ||
    (c.Address != null && EF.Functions.ILike(c.Address, pattern, LikeEscapeChar)));
```

- `%search%` → PostgreSQL `LIKE`/`ILIKE` substring wildcards: matches the value *anywhere* inside the field.
- `EF.Functions.ILike` → PostgreSQL **`ILIKE`** = case-insensitive. Searching `RAHUL` matches `"Rahul Sharma"`. Test: `SearchesCaseInsensitivelyAcrossFields` (`ContactsControllerTests.cs:297-307`).
- The `c.Email != null` guards prevent a null column from failing the comparison.
- `EscapeLike` (Part 9) stops `%`, `_`, `\` from acting as wildcards in user input.

The generated SQL is roughly:

```sql
SELECT COUNT(*) FROM contacts
WHERE name ILIKE '%rahul%' ESCAPE '\'
   OR phone_number ILIKE '%rahul%' ESCAPE '\'
   OR (email IS NOT NULL AND email ILIKE '%rahul%' ESCAPE '\')
   OR (address IS NOT NULL AND address ILIKE '%rahul%' ESCAPE '\');

SELECT ... FROM contacts
WHERE (same WHERE)
ORDER BY created_at DESC, id DESC
OFFSET 0 LIMIT 20;
```

### Worked example — `GET /api/contacts?search=rahul`

1. Browser sends `GET /api/contacts?search=rahul`.
2. Nginx forwards as `GET /contacts?search=rahul` to port 8080.
3. `[FromQuery] string? search` binds `"rahul"` in `ContactsController.cs:22`.
4. Service: search not blank → stay as search; `ParseSort(null)` → default `createdAt,desc`.
5. Repository: builds `%rahul%` and the 4-way `ILIKE` filter; counts matches; retrieves page 0 of size 20 sorted `created_at desc`.
6. Service maps rows → `ContactResponse`, computes page metadata.
7. Controller returns 200 with `{"content":[...],"totalElements":1,...}`.
8. The test `SearchesCaseInsensitivelyAcrossFields` asserts exactly one result, `"Rahul Sharma"`.

### Not used in this project

- No full-text search (`to_tsvector`).
- No fuzzy matching, no phonetics, no query-language operators. Search is a plain, case-insensitive substring `ILIKE` on four columns.

### What should you be able to answer?

- Which fields does search cover?
- Why is `RAHUL` == `rahul`?
- Why `%...%`? What would a search for `%` do if `EscapeLike` did not exist?
- Where does search length get validated?

---

# PART 12 — PAGINATION

## Byte 12.1 — Pagination, precisely

### The parameters

- `page` — *zero-based* page index. Default `0`. **page 0 = first page.**
- `size` — rows per page. Default `20`, allowed range 1–100 (`ContactService.cs:32-35`).

### The metadata fields

`ContactPageResponse` (Part 6 Byte 2):

- `content` — the rows on this page (already `ContactResponse`).
- `page` — the requested page number.
- `size` — requested size.
- `totalElements` — total matching rows in the whole dataset.
- `totalPages` — `totalElements / size`, rounded up (`Math.Ceiling`), or `0` when empty — `ContactService.cs:48`.
- `first` — `page == 0` (`ContactService.cs:57`).
- `last` — `page >= totalPages - 1` (`ContactService.cs:58`).

### The SQL: `OFFSET` and `LIMIT`

```csharp
List<Contact> items = await ordered
    .Skip(page * size)
    .Take(size)
    .ToListAsync(cancellationToken);
```

- `Skip(page * size)` → `OFFSET page*size` — drop that many rows.
- `Take(size)` → `LIMIT size` — keep at most that many.
- Math: page 0 size 20 → skip 0 take 20; page 1 size 20 → skip 20 take 20; page 2 size 20 → skip 40 take 20.

### Concrete example — 1000 records, size 20

| page | Skip() math | OFFSET | Records returned | first | last |
|---|---|---|---|---|---|
| 0 | 0×20 = 0 | 0 | records 1–20 | true | false |
| 1 | 1×20 = 20 | 20 | records 21–40 | false | false |
| 49 | 49×20 = 980 | 980 | records 981–1000 | false | true |

`totalPages = ceil(1000 / 20) = 50`, so pages are 0..49. **Page 1 is the second page** — the implementation is zero-indexed, matching the original Java/Spring `Pageable` semantics.

### Out-of-range page

The service does **not** reject `page` beyond the range — only negative pages are rejected. Ask for `page=99` on 1000 records: `OFFSET 1980 LIMIT 20` returns zero rows, `totalPages` is still `50`, `last` computes to `99 >= 49` → `true`. The frontend sees an empty `content` with the full metadata. (Nothing special in the code for this — it just falls out of the arithmetic.)

### The test evidence

- `ReturnsPaginatedContacts`: 45 rows → `totalElements` = 45, `totalPages` = 3, `first` = true, `last` = false (`ContactsControllerTests.cs:25-41`).
- `Paginates`: `page=1` → 20 items, `page` = 1 (`ContactsControllerTests.cs:269-282`).
- `PaginatesToLastPage`: `page=2` → 5 items, `last` = true (`ContactsControllerTests.cs:285-295`).

### What should you be able to answer?

- Why does `page=1` show rows 21–40, not 1–20?
- What do `Skip`/`Take` translate to in PostgreSQL?
- How is `totalPages` computed, and why the special zero case?
- What happens for a page past the end?

### Common beginner confusion

"pageNumber should mean the page the user sees." It does — but *human* first page is *code* page 0. Always keep `first = (page == 0)` consistent with how you render "Prev" buttons.

---

# PART 13 — SORTING

## Byte 13.1 — Sorting, exactly as implemented

### The format

`GET /api/contacts?sort=<field>,<direction>` — a single comma-separated pair; optional.

- Field: one of `name`, `phoneNumber`, `email`, `createdAt`, `id`. Anything else → `InvalidContactException("Unsupported sort field.")`.
- Direction: `asc` or `desc` (case-insensitive, per `ToUpperInvariant()`). Anything else → `InvalidContactException("Sort direction must be asc or desc.")`.
- Missing comma → `"Sort must use field,direction format."`.

### Default

No `sort` parameter → `new ContactSortSpec("createdAt", Descending: true)`. So the default listing is **newest first** (`created_at DESC`), confirmed by `SortsByCreatedAtDescendingByDefault` (`ContactsControllerTests.cs:348-357`).

### Parsing — `ContactService.ParseSort` (lines 152-183)

```csharp
if (string.IsNullOrWhiteSpace(sort))
    return new ContactSortSpec("createdAt", Descending: true);

string[] parts = sort.Trim().Split(',', StringSplitOptions.None);
if (parts.Length != 2)
    throw new InvalidContactException("Sort must use field,direction format.");

string property = parts[0] switch
{
    "name" => "name", "phoneNumber" => "phoneNumber", "email" => "email",
    "createdAt" => "createdAt", "id" => "id",
    _ => throw new InvalidContactException("Unsupported sort field."),
};

bool descending = parts[1].ToUpperInvariant() switch
{
    "ASC" => false,
    "DESC" => true,
    _ => throw new InvalidContactException("Sort direction must be asc or desc."),
};

return new ContactSortSpec(property, descending);
```

The service validates the whitelist so the repository's `ApplySort` switch can never be asked for an unknown field in a normal request (the repository re-throws the same message defensively).

### Application — `ContactRepository.ApplySort` (lines 169-192)

```csharp
IOrderedQueryable<Contact> ordered = sort.Descending
    ? sort.Property switch
    {
        "name" => query.OrderByDescending(c => c.Name),
        ...
        _ => throw new InvalidContactException("Unsupported sort field."),
    }
    : sort.Property switch { /* ascending versions */ };

return ordered.ThenByDescending(c => c.Id);
```

`ThenByDescending(c => c.Id)` is the always-applied **tie-breaker**. If two contacts have the same name, the one with the higher `Id` comes first. Sorting is therefore deterministic across pages — you never get the same row on two pages. (In the default case the tie-break matches the composite index `(created_at, id)` defined in `PhonebookDbContext.cs:53`.)

### Examples

- `sort=name,desc` → `ORDER BY name DESC, id DESC`; first contact is the lexicographically-largest name ("Rahul Sharma" in the seeded 45-row dataset; test `SortsByNameDescending`, `ContactsControllerTests.cs:322-332`).
- `sort=name,asc` → `ORDER BY name ASC, id DESC`; first is "Customer 10" (test `SortsByNameAscending`, line 334-346).
- `sort=id,asc` → `ORDER BY id ASC, id DESC` (tie-break is a no-op here) — first is id 1 (test line 360-368).
- `sort=phoneNumber,desc` → newest phone first (test line 371-381).

### Not used in this project

- No multi-column sort like `sort=name,asc&sort=createdAt,desc`.
- No direction keyword synonyms; exactly `asc`/`desc`.

### What should you be able to answer?

- What are the valid field and direction tokens?
- What is the default sort?
- Why does the repository always add `ThenByDescending(c => c.Id)`?
- Which invalid sorts produce which exact error messages?

---

# PART 14 — VALIDATION

## Byte 14.1 — All validation, and where it lives

### What gets validated

| Input | Kind | Rule | Where | Failure message |
|---|---|---|---|---|
| body `name` | framework | required; max 255 | `[Required]` / `[MaxLength(255)]` on `ContactCreateRequest` | "Name is required." / "Name cannot exceed 255 characters." |
| body `phone_number` | framework | required | `[Required]` | "Phone number is required." |
| body `email` | framework | valid email; max 255 | `[EmailAddress]` / `[MaxLength(255)]` | "Invalid email address." / "Email cannot exceed 255 characters." |
| body `address` | framework | max 10000 | `[MaxLength(10000)]` | "Address cannot exceed 10000 characters." |
| request body | framework | must be valid JSON with proper content type | body-binding failures (keys `$`/`""`/`request`) + content-type check | "Request body must contain valid JSON." / "Content-Type must be application/json." |
| `page` | business | >= 0 | `ContactService.GetContactsAsync` | "Page must be zero or greater." |
| `size` | business | 1..100 | same | "Size must be between 1 and 100." |
| `search` | business | <= 100 chars | same | "Search must not exceed 100 characters." |
| `sort` | business | `field,direction`, valid tokens | `ContactService.ParseSort` | "Sort must use field,direction format." / "Unsupported sort field." / "Sort direction must be asc or desc." |
| `name` | business | no digits | `ValidateBusinessRules` | "Name cannot contain numbers." |
| `phone_number` | business | regex + exactly 10 digits | `ValidateBusinessRules` | "Phone number must contain exactly 10 digits." |
| `id` (route) | business | > 0 | `FindContactAsync` | "must be greater than 0" |

### Framework/model validation vs business validation — the two stages

**Stage 1 — model binding & framework validation, before the controller runs.**

`[ApiController]` (on `ContactsController`) automatically inspects `ModelState` after binding the body into `ContactCreateRequest`. If invalid:

1. The configured `InvalidModelStateResponseFactory` runs — that's `ErrorResponseFactory.Create` from `Program.cs:47-50`.
2. It returns `400` with `{"detail":"<message>"}` immediately. Your action code **never executes**.

Evidence: `ValidatesRequiredName` posts a body without `name` and expects `400` + `"Name is required."` without any service involvement (`ContactsControllerTests.cs:118-127`).

Because the DTO setters already trim (`ContactCreateRequest.cs`), blank input becomes `null`, and `[Required]` fires — `ValidatesBlankNameTrimsToRequired` (`ContactsControllerTests.cs:129-138`).

**Stage 2 — business validation, inside the service.**

After binding succeeds, `ContactService.CreateContactAsync` calls `ValidateBusinessRules(request)` and `EnsureUniqueAsync(...)`. These throw custom exceptions (`InvalidContactException`, `DuplicateContactException`) that the middleware translates to 400/409. These cannot live as `[DataAnnotations]` because they depend on data (`ExistsBy...`) and cross-field logic (regex + digit count).

### What happens when validation fails — the response

Every failure returns the same envelope: `{"detail":"<one message>"}` plus the right status — 400 for invalid input, 409 for duplicates, 404 for missing ids, 415 for bad content type. This consistency is what the frontend relies on (Part 16).

### Not used here

- No `[FromQuery]` validation attributes on `page`/`size`/`search`/`sort` — all validated by hand in the service.
- No `FluentValidation` library; no custom `ValidationAttribute` subclasses. DataAnnotations + hand checks cover everything.

### What should you be able to answer?

- Where does framework validation run relative to the controller, and relative to business validation?
- Why can't the duplicate check be a `[DataAnnotations]` attribute?
- What exact status + body does each kind of failure produce?

---

# PART 15 — EXCEPTIONS

## Byte 15.1 — The three custom exceptions

All live in `src/Phonebook.Api/Exceptions/`.

### `ContactNotFoundException` (ContactNotFoundException.cs)

```csharp
public sealed class ContactNotFoundException : Exception
{
    public ContactNotFoundException()
        : base("Contact not found.")
    {
    }
}
```

- No message parameter — the message is always `"Contact not found."`.
- Thrown in `ContactService.FindContactAsync` when `GetByIdAsync` returns `null` (`ContactService.cs:106-107`). So every `GetContactAsync`, `UpdateContactAsync`, `DeleteContactAsync` on a missing id throws it.
- Caught by middleware → **404**.

### `DuplicateContactException` (DuplicateContactException.cs)

```csharp
public sealed class DuplicateContactException : Exception
{
    public DuplicateContactException(string message) : base(message) { }
}
```

- Carries a specific message: `"Duplicate phone number."`, `"Duplicate email."`, or the generic constraint fallback.
- Thrown in two places:
  1. `ContactService.EnsureUniqueAsync` (pre-check, `ContactService.cs:121,132`).
  2. `ContactRepository.SaveChangesTranslatedAsync` (race-condition backstop after a DB unique violation, `ContactRepository.cs:130-138`).
- Caught by middleware → **409 Conflict**.

### `InvalidContactException` (InvalidContactException.cs)

```csharp
public sealed class InvalidContactException : Exception
{
    public InvalidContactException(string message) : base(message) { }
}
```

- Thrown all over `ContactService` (page/size/search/sort rules, name/phone business rules, id <= 0) and defensively in `ContactRepository.ApplySort`.
- Caught by middleware → **400 Bad Request**.

### Why custom exceptions?

1. **Readability** — `throw new ContactNotFoundException()` instantly says what happened, compared to returning `null`/`false` up a chain of `if`s.
2. **The middleware can switch on type** (`ContactNotFoundException`, then `DuplicateContactException`, then `InvalidContactException`, then fallback) instead of parsing strings (`ErrorHandlingMiddleware.cs:29-45`).
3. **Consistent messages** — the exception message *is* the user-facing `detail` for 400/409.

### Where they are thrown vs caught — one diagram

```
ContactService.FindContactAsync        throw ContactNotFoundException
ContactService.EnsureUniqueAsync       throw DuplicateContactException
ContactService.ValidateBusinessRules   throw InvalidContactException
ContactRepository.SaveChangesTranslatedAsync  throw DuplicateContactException (DB backstop)
        │
        ▼  (all bubble up through the controller since there's no try/catch there)
ErrorHandlingMiddleware catch blocks    → set status + write {"detail": message}
```

### What should you be able to answer?

- Why does `ContactNotFoundException` not take a message, but the other two do?
- Name all throw sites and their catch sites.
- What status code does each exception produce?

---

# PART 16 — ERROR HANDLING MIDDLEWARE

## Byte 16.1 — Middleware from scratch

Middleware is a function that sits in the request pipeline. ASP.NET Core's pipeline is a series of delegations: each middleware either handles the request or passes it to the next one.

```
request in → [MW1] → [MW2] → ... → [controller/endpoint] → response out
                                  ↑ exception travels back up the stack
```

Because `ErrorHandlingMiddleware` is registered first (`Program.cs:61`), it wraps **everything**:

- On the way in, it does a quick content-type check.
- It calls `_next(context)` — which runs the rest of the pipeline (routing → controller → service → repository → database).
- On the way back, if an exception was thrown anywhere in that chain, the `catch` blocks convert it into a JSON response *before* any response is written.

## Byte 16.2 — The code

```csharp
public sealed class ErrorHandlingMiddleware                    // Middleware/ErrorHandlingMiddleware.cs
{
    private readonly RequestDelegate _next;                    // line 8
    private readonly ILogger<ErrorHandlingMiddleware> _logger;  // line 9

    public async Task InvokeAsync(HttpContext context)          // line 17
    {
        if (IsUnsupportedRequestBodyContentType(context))       // 19-23
        {
            await WriteErrorAsync(context,
                StatusCodes.Status415UnsupportedMediaType,
                "Content-Type must be application/json.");
            return;
        }

        try
        {
            await _next(context);                               // line 27
        }
        catch (ContactNotFoundException)
        {
            await WriteErrorAsync(context, 404, "Contact not found.");
        }
        catch (DuplicateContactException exception)
        {
            await WriteErrorAsync(context, 409, exception.Message);
        }
        catch (InvalidContactException exception)
        {
            await WriteErrorAsync(context, 400, exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected API error");
            await WriteErrorAsync(context, 500, "An unexpected server error occurred.");
        }
    }
```

`IsUnsupportedRequestBodyContentType` (48-57): if the verb is POST/PUT and the `Content-Type` header does not start with `application/json`, refuse with 415 *before* hitting the endpoint. (The frontend always sends JSON.)

`WriteErrorAsync` (59-69):

```csharp
if (context.Response.HasStarted) return;         // already writing -> cannot change it
context.Response.StatusCode = statusCode;
context.Response.ContentType = "application/json";
await context.Response.WriteAsync(JsonSerializer.Serialize(new { detail }), context.RequestAborted);
```

`HasStarted` guard prevents a "Headers are read-only, response has already started" crash if the exception is thrown after the response began. The body is always exactly `{"detail":"<message>"}`.

## Byte 16.3 — Actual error responses in this project

| Situation | Status | Body |
|---|---|---|
| Missing `name` | 400 | `{"detail":"Name is required."}` |
| Malformed JSON | 400 | `{"detail":"Request body must contain valid JSON."}` |
| `Content-Type: text/plain` on POST | 415 | `{"detail":"Content-Type must be application/json."}` |
| Duplicate phone | 409 | `{"detail":"Duplicate phone number."}` |
| Unknown id | 404 | `{"detail":"Contact not found."}` |
| `page=-1` | 400 | `{"detail":"Page must be zero or greater."}` |
| Anything unexpected | 500 | `{"detail":"An unexpected server error occurred."}` (+ server log) |

Verified end to end: `ErrorResponseBodyContainsOnlyDetail` asserts the body has *exactly one* property, `detail` (`ContactsControllerTests.cs:480-491`).

### Why consistency matters

The frontend can rely on one shape: `.response.detail` holds the human-readable reason, and the HTTP status tells it what happened. No scatter of shapes (one endpoint returning an array of errors, another a different key), no HTML error pages, no stack traces leaking to clients. The 500 case explicitly hides internals and only logs them.

### What should you be able to answer?

- Why is the middleware the first/outermost component?
- What happens to an exception thrown by the repository?
- What is the `HasStarted` check for?
- How does middleware relate to the try/catch chain of CustomExceptions?

### Common beginner confusion

"Middleware vs controller filters." Middleware wraps the whole pipeline including routing; a filter runs around a controller action only. This project uses middleware (pipeline-level) — no action filters.

---

# PART 17 — DATABASE / POSTGRESQL

## Byte 17.1 — How the app reaches PostgreSQL

### The pieces

- **PostgreSQL** — the database server holding the `phonebook_db` database and the `contacts` table.
- **Npgsql** — the low-level .NET ADO.NET driver. It speaks the PostgreSQL wire protocol. Also exposes `PostgresException`, which the repository inspects for `SqlState` 23505.
- **Npgsql.EntityFrameworkCore.PostgreSQL** — the EF Core provider that translates EF LINQ into PostgreSQL SQL and uses Npgsql underneath.
- **PhonebookDbContext** — registered with `.UseNpgsql(connectionString)` (`Program.cs:54`).

### The chain

```
C# LINQ (ContactRepository)
   ↓ EF Core translates expression tree → SQL
Npgsql (driver, opens TCP connection, sends query)
   ↓ PostgreSQL interprets/executes
rows or result set
   ↑ Npgsql returns raw results
EF Core materializes rows → List<Contact>
```

### The connection string

Resolved in `BuildConnectionString` (`Program.cs:10-32`), either:

- built from env vars `DATABASE_HOST/PORT/NAME/USERNAME/PASSWORD` (if any is set), or
- `appsettings.json`:

```json
"ConnectionStrings": {
  "Phonebook": "Host=localhost;Port=5432;Database=phonebook_db;Username=phonebook_user;Password=phonebook_password"
}
```

Host `localhost`, port `5432` (PostgreSQL default), database `phonebook_db`.

### The `contacts` table the app expects

Derived from `PhonebookDbContext.OnModelCreating` (Part 8), the schema is:

| column | type | constraints |
|---|---|---|
| `id` | integer (identity) | PRIMARY KEY, generated by default |
| `name` | varchar(255) | NOT NULL |
| `phone_number` | varchar(32) | NOT NULL, UNIQUE |
| `email` | varchar(255) | UNIQUE (NULLs allowed) |
| `address` | text | — |
| `created_at` | timestamptz | NOT NULL |

Indexes: unique on `phone_number`, unique on `email`, composite `(created_at, id)`.

Two important mechanics:

1. The schema is created by `db.Database.EnsureCreatedAsync(...)` in the **seeder** (`ContactDataSeeder.cs:113`). `EnsureCreated` creates the database objects if they don't exist; it is *not* EF Core migrations.
2. The **application never opens a connection per request explicitly** — EF Core opens/uses/releases connections from its pooling automatically, scoped to the request's DbContext lifetime.

### Not used in this project

- No EF Core **Migrations** (`migrations/` folder, `dotnet ef`).
- No raw `AdoNet` queries, no stored procedures, no database context in the controller.

### What should you be able to answer?

- Which package is the driver, and which is the provider?
- How does EF LINQ become SQL?
- What columns does the `contacts` table have, and their constraints?

---

# PART 18 — SEEDER

## Byte 18.1 — ContactDataSeeder

### Why it exists

The seeded data must match the Java reference backend byte-for-byte (so the two backends behave identically when given the same `RANDOM_SEED`). Instead of copying data files, the seeder **reproduces the Java random sequence** and generates up to 1000 contacts.

### When it runs

Registered as a **hosted service**: `builder.Services.AddHostedService<ContactDataSeeder>()` (`Program.cs:57`). Hosted services start when the app's `IHost` starts — i.e., at application startup, before serving requests. `StartAsync` runs once per app start (`ContactDataSeeder.cs:106-115`), then `app.Run()` serves traffic.

### `StartAsync`, with the lifetime fix

```csharp
public async Task StartAsync(CancellationToken cancellationToken)
{
    using IServiceScope scope = _scopeFactory.CreateScope();      // 108
    IServiceProvider services = scope.ServiceProvider;            // 109
    PhonebookDbContext db = services.GetRequiredService<PhonebookDbContext>();      // 110
    IContactRepository repository = services.GetRequiredService<IContactRepository>(); // 111

    await db.Database.EnsureCreatedAsync(cancellationToken);      // 113
    await SeedAsync(repository, cancellationToken);               // 114
}
```

- The seeder is a **singleton** (hosted services are singleton), but `PhonebookDbContext`/`IContactRepository` are **scoped**. A singleton cannot directly consume scoped services (Part 4 Byte 4) — so it creates its own scope with `IServiceScopeFactory`, resolves from it, and disposes the scope with `using` when done.
- `EnsureCreatedAsync` guarantees the database and `contacts` table exist before seeding (Part 17).

### `SeedAsync`: check, then top up (lines 119-169)

```csharp
long existingCount = await repository.CountAsync(cancellationToken);   // 121
if (existingCount >= SeedTarget)                                       // 122  (SeedTarget == 1000)
{
    _logger.LogInformation("Seed skipped: database already contains {Count} contacts.", existingCount);
    return;                                                             // 125  — never touches existing rows
}
```

Then:

1. Load all existing rows (`GetAllAsync`, line 128) and collect their phone numbers/emails into `HashSet`s (lines 129-143) so the new data never duplicates existing rows.
2. Compute `toCreate = 1000 - existingCount` (line 145).
3. Create a `new JavaRandom(RandomSeed)` — **deterministic** — and generate contacts:
   - Name: `FirstNames[random.NextInt(...)] + " " + LastNames[...]` (lines 151-156).
   - Phone: `NextUniquePhoneNumber` — first digit from `"6789"`, then 9 random digits, retrying until unique (lines 171-189).
   - Email: `firstName.lastName + index + "@example.com"`, lowercase, letters only, unique (lines 191-203).
   - Address: `Areas[...] + ", " + Cities[...]` (line 159).
   - `CreatedAt = DateTime.UtcNow` (line 160).
4. `AddRangeAsync(contacts, ...)` inserts them in one batch (line 166).
5. Log how many were created + the new total (line 168).

### Why the 1000-record skip

The seed target is 1000 (`SeedTarget`). Seeding is a **one-time top-up**, not a reset: if you already have 1000+ contacts (from a previous run or from real usage), it skips entirely so it never duplicates or deletes user data. If you have, say, 3 contacts, it adds exactly 997 to reach 1000 — test `SeedsValidUniqueContactsUpToTargetWithoutReusingExistingData` (`ContactDataSeederTests.cs:37-76`).

### `JavaRandom` — why does this class exist?

`java.util.Random` uses a linear congruential generator:

```
seed = (seed * 0x5DEECE66D + 0xB) & ((1<<48) - 1)
next(bits) = seed >> (48 - bits)
```

This C# class (`ContactDataSeeder.cs:14-53`) replicates that algorithm exactly (`NextInt(bound)` matches `java.util.Random.nextInt(bound)`'s rejection logic). Because the seed is fixed (`4207L`, line 63), every run generates the **same** names, phones, emails, addresses — verified by `SeedsAreDeterministicAcrossRuns` and `SeedsMatchTheJavaReferenceData` (first three rows must be "Rajesh Rao"/"Rohit Singh"/"Meera Bhat" with the exact phones, `ContactDataSeederTests.cs:120-141`).

### `StopAsync`

```csharp
public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
```

No shutdown work needed.

### Startup lifecycle, complete

1. Process starts, config loads, DI registered.
2. `builder.Build()` → host starts.
3. Hosted services start: `ContactDataSeeder.StartAsync`.
4. It creates a scope → `EnsureCreatedAsync` (table exists) → count check → top up if needed → scope disposed.
5. `app.Run()` begins serving requests.

### What should you be able to answer?

- Why does the seeder skip when there are 1000+ contacts?
- Why is `IServiceScopeFactory` needed instead of direct injection?
- What makes the seeded data identical across runs?
- Where is `EnsureCreatedAsync` and what does it do?

### Common beginner confusion

"Seeding runs on every startup." Not as a full reset — the `existingCount >= SeedTarget` guard makes it effectively once (or a top-up). And `EnsureCreated` only creates missing tables; it never migrates or drops.

---

# PART 19 — CONFIGURATION

## Byte 19.1 — appsettings + environment overrides

### The two appsettings files

**`appsettings.json`** (`src/Phonebook.Api/appsettings.json`) — base config:

```json
{
  "Logging": { "LogLevel": { "Default": "Information",
                             "Microsoft.AspNetCore": "Warning",
                             "Microsoft.EntityFrameworkCore": "Warning" } },
  "AllowedHosts": "*",
  "Urls": "http://0.0.0.0:8080",
  "ConnectionStrings": { "Phonebook": "Host=localhost;Port=5432;Database=phonebook_db;Username=phonebook_user;Password=phonebook_password" }
}
```

- `Logging` — default level Information, but ASP.NET Core infrastructure warnings (and EF Core warnings) are kept quieter so startup noise is reduced.
- `AllowedHosts` — hosts allowed to reach the app (`*` = any).
- `Urls` — the base listen address **if `PORT` is not set** (Byte 3.1). Port **8080**, all interfaces (0.0.0.0) so Nginx can reach it.
- `ConnectionStrings.Phonebook` — fallback connection string used when no `DATABASE_*` env vars are present.

**`appsettings.Development.json`** — overrides for the `Development` environment:

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } }
}
```

Only logging is adjusted; nothing else. The "current environment" determines which appsettings files load, with the environment-specific file winning on conflicts (`appsettings.json` then `appsettings.{Environment}.json` then env vars, last one wins).

### How the app knows the environment

ASP.NET Core reads `ASPNETCORE_ENVIRONMENT` (e.g. `Development`, `Staging`, `Production`, or the custom `Testing` used by the test factory at `PhonebookApiFactory.cs:19`). When you `dotnet run` locally with the launch default, it's `Development`.

### Environment variables actually used

In `Program.cs`:

| Variable | Effect |
|---|---|
| `PORT` | Overrides listen URL to `http://0.0.0.0:{PORT}` (`Program.cs:36-39`). |
| `DATABASE_HOST`, `DATABASE_PORT`, `DATABASE_NAME`, `DATABASE_USERNAME`, `DATABASE_PASSWORD` | If **any** is set, they build the connection string (`Program.cs:12-29`). Each missing one falls back to a default (localhost / 5432 / phonebook_db / phonebook_user / phonebook_password). |
| `ASPNETCORE_ENVIRONMENT` | Selects environment + which appsettings file wins (handled by the framework, not our code). |

So: with no env vars, the app reads everything from `appsettings.json` (port 8080, local PostgreSQL). In Docker (see `.env.example` for the pattern), you set `DATABASE_*` and the app builds the PostgreSQL connection from those instead.

### The precedence order

`appsettings.json` < `appsettings.{Environment}.json` < environment variables < command line. (Later sources override earlier ones.)

### Not used in this project

- No `launchSettings.json` (not present; `dotnet run` relies on appsettings `Urls`).
- No user-secrets, no Azure App Configuration, no .NET configuration-keys for DB (the DB connection never comes from a config key — only from env `DATABASE_*` or the `Phonebook` connection string).

### What should you be able to answer?

- If I set `PORT=9090`, what listens where?
- How is the DB connection chosen?
- Which file wins when settings conflict: `appsettings.json` or environment variables?
- What is "the environment" (Development/Testing) and how is it picked?

---

# PART 20 — TESTING

## Byte 20.1 — Test project overview

Three test files, three styles, **69 tests, all passing** (verified with `dotnet test Phonebook.sln`):

| File | Style | Count | What it verifies |
|---|---|---|---|
| `ContactsControllerTests.cs` | Integration (real HTTP through `WebApplicationFactory`) | 40 | Full request/response behavior: routes, status codes, JSON shape, validation, search, sort, pagination |
| `ContactServiceTests.cs` | Unit (Moq fakes `IContactRepository`) | 23 | Service logic in isolation: rules, mapping, duplicate handling |
| `ContactDataSeederTests.cs` | Seeder | 6 | Determinism, uniqueness, Java-identical data, skip behavior |

## Byte 20.2 — The tools

- **xUnit** — the test framework; `[Fact]` marks a test method. `GlobalUsings.cs` makes `using Xunit;` implicit for the whole project.
- **Moq** — creates fake `IContactRepository` objects (`Mock<IContactRepository>`), lets tests set up returns and verify calls. Used only in `ContactServiceTests.cs`.
- **WebApplicationFactory** — boots the *real* `Program` in the test host and serves an in-process `HttpClient`. Used by `ContactsControllerTests.cs` through `PhonebookApiFactory`.

## Byte 20.3 — The factory that swaps the database

`PhonebookApiFactory.cs`:

```csharp
public sealed class PhonebookApiFactory : WebApplicationFactory<Program>
{
    private readonly InMemoryContactRepository _repository = new();
    public InMemoryContactRepository Repository => _repository;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();                        // no seeder
            services.RemoveAll<PhonebookDbContext>();                    // no EF
            services.RemoveAll<DbContextOptions<PhonebookDbContext>>();
            services.RemoveAll<IContactRepository>();
            services.RemoveAll<ContactRepository>();
            services.AddSingleton<IContactRepository>(_repository);      // in-memory repo instead
        });
    }
}
```

This is the DI swap made real: the app's registrations are replaced *after* Startup registers them (`RemoveAll` then `AddSingleton`). Consequences:

- No seeded data from `ContactDataSeeder` (hosted services removed).
- No PostgreSQL needed — the integration tests hit a pure in-memory repository.
- The same instance is shared across tests, so each test resets it (`ContactsControllerTests.cs:19` `_factory.Repository.Reset();` then re-seeds a known 45-row dataset via `SeedDataset`, lines 516-553).

## Byte 20.4 — InMemoryContactRepository (the fake)

`InMemoryContactRepository.cs` implements every `IContactRepository` method using a `Dictionary<int, Contact>` guarded by a lock, reproducing the same behavior in LINQ-to-Objects:

- `GetPageAsync` — filters with `Contains(...OrdinalIgnoreCase)` (mirroring ILIKE), sorts with `OrderBy`/`OrderByDescending` + `ThenByDescending(c => c.Id)`, `Skip(page*size).Take(size)` (lines 52-88).
- It also makes defensive **copies** of contacts when returning (lines 75-83, 197-207) so tests can't mutate the dictionary through a returned reference accidentally. Subtle and important: expected behavior stayed identical to the EF implementation, so the integration tests exercise the same contracts, just with an in-memory store.
- `IContactRepository` remains the contract — the SUT (controller + service + DTOs + middleware + routing) is 100% real; only the data store is swapped.

## Byte 20.5 — Representative tests explained (not just "they pass")

### Show that routing + 200 + metadata work: `ReturnsPaginatedContacts` (`ContactsControllerTests.cs:25-41`)

Hits `GET /contacts`, asserts `200`, then that `totalElements = 45`, `totalPages = 3`, `size = 20`, `page = 0`, `first = true`, `last = false`, and that the first content row (newest by default sort `created_at desc`) is `"Customer 44"`. One request exercises routing, controller, service metadata math, repository pagination, and JSON serialization naming.

### Show that a duplicate is rejected: `RejectsDuplicatePhone` (lines 209-217)

Sets up a dataset containing `9876500000` (Rahul Sharma). POSTs a new contact with the same phone and asserts `409` + `"Duplicate phone number."`. Demonstrates service `EnsureUniqueAsync` → `DuplicateContactException` → middleware → `{detail}`.

### Show that business rules block bad input: `RejectsPhoneWithoutTenDigits` (lines 197-206)

POSTs `"123456789"` → `400` + `"Phone number must contain exactly 10 digits."`. Confirms `ValidateBusinessRules` (regex + strip-non-digits count).

### Show the content-type guard: `RejectsUnsupportedContentType` (lines 469-478)

POST with `Content-Type: text/plain` → `415` + `"Content-Type must be application/json."`. This exercises the middleware's pre-check before the endpoint.

### Unit test that pins update semantics: `ExcludesCurrentContactWhenCheckingDuplicatePhoneOnUpdate` (`ContactServiceTests.cs:42-61`)

Moq sets up `GetByIdAsync(7)`, `ExistsByPhoneNumberAndIdNotAsync(...7)` returning false. It then `Verify`s that the `AndIdNot` variant was called *and* that the plain `ExistsByPhoneNumberAsync` was **never** called. This is the create-vs-update distinction locked in.

### Unit test that pins the sort hand-off: `SortsByNameAscending` (lines 78-87)

Sets up `GetPageAsync(0,20, it.IsAny<ContactSortSpec>(), null, ...)` and `Verify`s that the spec passed was `("name", false)`. Proves `ParseSort("name,asc")` produces exactly that spec.

### Seeder tests that pin determinism: `SeedsMatchTheJavaReferenceData` (`ContactDataSeederTests.cs:120-141`)

Seeds a fresh repo then asserts the first three rows are literally `Rajesh Rao / 9154278943`, `Rohit Singh / 8727370881`, `Meera Bhat / 8781533007` with matching emails/addresses. If anyone changed the RNG or the arrays, these tests fail — the Java parity is enforced.

### What should you be able to answer?

- What is the difference between the integration tests and the unit tests?
- What exactly does `PhonebookApiFactory` replace, and why?
- How does `InMemoryContactRepository` mirror EF behavior?
- What do the seeder tests pin down that code review could not?

### Not used in this project

- No real PostgreSQL in tests (no Testcontainers, no `databasename` fixture). Everything runs against the fake repository.
- No `[Theory]` parametrized tests (only `[Fact]`).
- No test ordering dependencies — each test self-seeds.

---

# PART 21 — COMPLETE REQUEST FLOWS

For each flow: outward path (request) and return path (response). S = service methods, R = repository methods.

## Flow 1 — GET contacts (plain list)

```
Browser: GET /api/contacts?page=0&size=20
  → Nginx: location /api/  proxy_pass http://localhost:8080/   → GET /contacts?page=0&size=20
  → Kestrel → ErrorHandlingMiddleware (content-type n/a for GET) → MapControllers → ContactsController.GetContacts
  → S.GetContactsAsync(0,20,null,null): page/size valid; search null; ParseSort(null)=createdAt,desc
  → R.GetPageAsync(0,20,(createdAt,true),null): AsNoTracking + LongCount + ApplySort + Skip(0).Take(20)
  → EF → Npgsql → PostgreSQL (COUNT + SELECT ORDER BY created_at DESC, id DESC LIMIT 20)
Response: contacts (20 rows) + totalElements + totalPages + first + last → 200 Ok
```

## Flow 2 — GET contacts with search

```
Browser: GET /api/contacts?search=rahul
  → S: normalizedSearch "rahul" (≤100) → default sort createdAt,desc
  → R: WHERE name ILIKE '\%rahul\%' ESCAPE '\' OR phone_number ILIKE ... OR email ... OR address ...
  → COUNT + page rows
Response: only matches, e.g. totalElements 1 → 200 {content:[Rahul Sharma], totalElements:1,...}
```

## Flow 3 — GET contacts with pagination

`page=1&size=20`, dataset of 45:
```
→ Skip(1*20)=OFFSET 20, Take(20)=LIMIT 20 → rows 21-40 (5 remain on page 2)
→ totalPages = ceil(45/20) = 3, first = false, last = false        (test Paginates)
page=2&size=20 → OFFSET 40 LIMIT 20 → 5 rows, last = true          (test PaginatesToLastPage)
```

## Flow 4 — GET contacts with sorting

`sort=name,desc`:
```
→ ParseSort → ContactSortSpec("name", true)
→ R: ORDER BY name DESC, id DESC
→ first content row = lexicographically greatest name ("Rahul Sharma" in 45-row fixture)
```
`sort=name,asc` → `ORDER BY name ASC, id DESC`, first = "Customer 10".
Invalid: `sort=address,asc` → S throws InvalidContactException → middleware → 400 {"detail":"Unsupported sort field."}

## Flow 5 — POST contact (create)

```
Browser: POST /api/contacts  Content-Type: application/json
  body {"name":"Jane Doe","phone_number":"9876543210","email":"jane.doe@example.com","address":"Bandra, Mumbai"}
  → Middleware: POST + content-type ok → ModelState binding (DTO trims) → [Required] validates before action
  → ContactsController.CreateContact
  → S.CreateContactAsync: ValidateBusinessRules (no digits in name; phone 10 digits)
     → EnsureUniqueAsync(null): ExistsByPhoneNumber? / ExistsByEmail?  (query ⛭)
     → new Contact → Apply → R.AddAsync (stamps CreatedAt=UtcNow) → SaveChangesTranslated → INSERT
  → PostgreSQL generates id via identity column
  → ContactResponse.From(contact) → CreatedAtAction(nameof(GetContact), new{id}, created)
Response: 201 + Location: /contacts/{id} + JSON contact (with id, created_at)
```

## Flow 6 — PUT contact (update)

```
Browser: PUT /api/contacts/7   body same shape as POST
  → S.UpdateContactAsync(7, req): ValidateBusinessRules
     → FindContactAsync(7): id>0, GetByIdAsync → must exist (else ContactNotFoundException→404)
     → EnsureUniqueAsync(req, 7): uses ExistsByPhoneNumberAndIdNot / ExistsByEmailAndIdNot (excludes self)
     → Apply(new values onto tracked entity) → R.UpdateAsync → UPDATE contacts SET ... WHERE id=7
Response: 200 + updated ContactResponse
```

## Flow 7 — DELETE contact

```
Browser: DELETE /api/contacts/3
  → S.DeleteContactAsync(3): FindContactAsync(3) → R.DeleteAsync → DELETE FROM contacts WHERE id=3
Response: 200 {"message":"Contact deleted successfully."}
  → subsequent GET /contacts/3 → 404 (test DeletesContact verifies both).
```

## Flow 8 — Invalid request (validation failure)

Example: POST missing `name`.
```
  → ModelBinding: DTO accepts, ModelState invalid ([Required] failed)
  → [ApiController] intercepts BEFORE action → InvalidModelStateResponseFactory = ErrorResponseFactory.Create
  → body-level? no → first property error → "Name is required."
Response: 400 {"detail":"Name is required."}
```
Malformed JSON variant: binding fails under keys "$"/""/request → `400 {"detail":"Request body must contain valid JSON."}`.

## Flow 9 — Duplicate contact

Example: POST with a phone already present.
```
  → Middleware try → controller → S.CreateContactAsync
  → EnsureUniqueAsync: ExistsByPhoneNumberAsync → true → throw DuplicateContactException("Duplicate phone number.")
  → Middleware catch DuplicateContactException → 409
Response: 409 {"detail":"Duplicate phone number."}
```
(Race variant: both checks pass *simultaneously*, one INSERT succeeds, the other hits the unique index → `DbUpdateException` 23505 → `SaveChangesTranslatedAsync` throws the same business exception.)

## Flow 10 — Contact not found

`GET /contacts/999999`:
```
  → S.GetContactAsync(999999) → FindContactAsync (id>0 ok) → R.GetByIdAsync → null
  → `?? throw new ContactNotFoundException()` → bubbles through controller (no try/catch there)
  → Middleware catch ContactNotFoundException → WriteErrorAsync 404
Response: 404 {"detail":"Contact not found."}
```

---

# PART 22 — NGINX INTEGRATION

## Byte 22.1 — Only the relevant Nginx ↔ backend interaction

Repo location: `nginx/nginx.conf` (used by the docker-compose/orchestration layer — we only explain, we don't touch it).

### The relevant block

```nginx
server {
    listen       80;
    server_name  localhost;

    location /api/ {                                    # lines 31-38
        proxy_pass http://localhost:8080/;
        proxy_http_version 1.1;
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
    ...
}
```

### What it does, precisely

- Nginx listens on **port 80** — the single entrance for everyone (the frontend never hits the backend directly; see `.env.example`, which sets `VITE_API_BASE_URL=/api`).
- Any URI beginning `/api/` matches the `location /api/` block and is reverse-proxied to the backend on **localhost:8080**.
- The **trailing slash** on `proxy_pass http://localhost:8080/;` is the key trick: when `proxy_pass` includes a URI part (`/`), Nginx *replaces* the matching `/api/` prefix. So:

```
/api/contacts?page=0&size=20   →   /contacts?page=0&size=20
/api/contacts/5                →   /contacts/5
```

- The backend's controller route is literally `/contacts` (`[Route("contacts")]`, `ContactsController.cs:8`) — the backend never sees `/api`. Requests keep the same host/port after proxying because of the `proxy_set_header Host $host;` line (plus standard forwarding headers for logging).

### Why this split

- The backend can be reached at its own address `http://localhost:8080/contacts` independent of Nginx.
- The public API surface is `/api/*`, versionable and namespaced, without the backend caring.
- The browser sees one origin (`:80`), avoiding cross-origin/CORS setup for the frontend (it calls relative `/api/contacts`).

### The other `location /` block (lines 46-56)

Everything *not* under `/api/` goes to the Vite frontend on **5173** — relevant only so you understand Nginx is doing two jobs; the backend only cares about the `/api/` block.

### Not used in this project

- No SSL/TLS, no load balancing, no rate limiting, no `/contacts` location block. It's a plain single-server reverse proxy.

### What should you be able to answer?

- Why does the backend receive `/contacts` even though the browser sent `/api/contacts`?
- Which port does Nginx listen on, and which port does the backend listen on?
- What is the role of the trailing slash in `proxy_pass http://localhost:8080/;`?

---

# PART 23 — THE ENTIRE BACKEND IN ONE PAGE

```
 Browser (frontend) 
      │  HTTP /api/contacts...
      ▼
 NGINX (port 80) — strips /api, forwards to backend
      │  /contacts
      ▼
 ASP.NET Core / Kestrel (port 8080 or $PORT)
      │  ErrorHandlingMiddleware wraps everything (content-type check + try/catch)
      ▼
 Routing (MapControllers) → ContactsController
      │  binds query/route/body; returns status codes
      ▼
 ContactService — business rules, validation, uniqueness, pagination math, DTO mapping
      │
      ▼
 IContactRepository → ContactRepository
      │  LINQ queries, EF operations, duplicate-error translation
      ▼
 PhonebookDbContext + EF Core
      │  LINQ → SQL via Npgsql provider
      ▼
 Npgsql driver → PostgreSQL (phonebook_db, contacts table)
      │  result rows
      ▼
 EF materializes → Contact objects → mapped to ContactResponse → JSON
      │
      ▼
 Back out through the same middleware → HTTP status + JSON to Nginx → browser
```

### Each layer's one-line responsibility

| Layer | Responsible for |
|---|---|
| Browser/frontend | Sending REST calls with `VITE_API_BASE_URL=/api`; rendering `{detail}` and `{content,...}`. |
| Nginx | Reverse proxy: `/api/*` → backend `:8080` (stripping `/api`), everything else → Vue `:5173`. |
| Kestrel | The HTTP server inside ASP.NET Core; accepts TCP/HTTP connections. |
| Middleware | Cross-cutting concerns — here: content-type guard + global exception → `{"detail":...}`. |
| Controller | HTTP contract: which verb + path + parameters map to which service call; status codes. |
| Service | Business rules, validation, uniqueness, pagination/sort parsing, entity↔DTO mapping. |
| Repository | Persistence contract: domain-shaped read/write operations; the only layer that knows EF. |
| DbContext | EF session: model config, DbSet, change tracking, `SaveChanges`. |
| EF Core | Translates expression trees → SQL; materializes rows into entities. |
| Npgsql | PostgreSQL driver (wire protocol; `PostgresException`). |
| PostgreSQL | Stores the `contacts` table; enforces constraints (unique phone/email, identity id). |

### The one-line dependency rule

Each layer knwows the layer below it **only through a narrow contract** (interface or method signature). Swap any layer's implementation (DB→memory, SQL→driver change) without touching the layers above. That is the whole architecture in a sentence.

---

# PART 24 — MENTOR INTERVIEW PREPARATION

Answer each with a sentence rooted in this project.

## BEGINNER

1. **What is ASP.NET Core?** The framework that hosts the API: `Program.cs` builds a `WebApplication`, Kestrel serves HTTP on `http://0.0.0.0:8080`.
2. **What is .NET?** The runtime/class libraries your `net8.0` code compiles against (`Phonebook.Api.csproj:4`).
3. **What is a Web API?** An HTTP server that returns data (JSON), not HTML — every response here is JSON.
4. **What is ControllerBase?** Base class giving the controller helpers like `Ok()` and `CreatedAtAction()` (`ContactsController.cs:9`).
5. **What does `[ApiController]` do?** Enables automatic model validation (400 on invalid body) and binding inference.
6. **What is Dependency Injection?** The container builds constructor dependencies automatically — `ContactsController.cs:13-16` receives `ContactService` without `new`.
7. **What is an interface?** A contract. `IContactRepository` lets tests substitute `InMemoryContactRepository`.
8. **What is a DTO?** A network-shaped class distinct from the entity — `ContactCreateRequest`, `ContactResponse`.
9. **What is a record?** An immutable-ish data type; `ContactSortSpec(string Property, bool Descending)` (`IContactRepository.cs:5`).
10. **What is `async`/`await`?** Non-blocking execution; nearly every method here is `Task`-returning and `await`ed.
11. **What is a query parameter?** Value after `?` in a URL — `page`, `size`, `search`, `sort` (`ContactsController.cs:20-23`).
12. **What is a route parameter?** Value inside the path — `{id}` in `/contacts/{id}`.
13. **What are HTTP verbs?** GET (read), POST (create), PUT (update), DELETE (delete-here); mapped by `[HttpGet]` etc.
14. **What is a status code?** The 3-digit meaning of the response: 200, 201, 400, 404, 409, 415, 500.
15. **What is JSON?** The data format exchanged (`{"detail": "..."}`).
16. **What is Nginx?** The reverse proxy on port 80 that forwards `/api/*` to the backend.

## INTERMEDIATE

17. **What is the Repository Pattern?** Hide data access behind `IContactRepository` so services don't know EF/SQL, and tests swap implementations.
18. **What is EF Core?** An O/RM turning LINQ into SQL: `_db.Contacts.Where(...)` becomes a `SELECT` through Npgsql.
19. **What is DbContext?** The EF session: `DbSet`, model config, change tracking (`PhonebookDbContext`).
20. **What is DbSet?** The query surface for an entity: `DbSet<Contact> Contacts` (`PhonebookDbContext.cs:13`).
21. **Why is the DbContext scoped?** One instance per request = one unit of work, disposed when the request scope ends.
22. **Why do we use DTOs?** Separate wire shape (`phone_number`, `created_at`) from entity/DB shape; avoid leaking internals.
23. **Why not return the entity directly?** Because the API shape differs (snake_case JSON, no `id`/`created_at` in requests) and entities shouldn't leak internal representation.
24. **What is middleware?** Pipeline component wrapping requests/responses; `ErrorHandlingMiddleware` is the outer guard.
25. **How does pagination work?** `Skip(page*size).Take(size)` → `OFFSET/LIMIT`; metadata computed in `ContactService`.
26. **What does `Skip()` do?** Drops the first N rows (`OFFSET N`).
27. **What does `Take()` do?** Keeps at most N rows (`LIMIT N`).
28. **How does search work?** `%term%` + PostgreSQL `ILIKE` across name/phone/email/address, with `%`/`_` escaped.
29. **How does sorting work?** Whitelisted `field,direction` → `OrderBy(Descending)` + `ThenByDescending(id)`.
30. **What happens when a contact doesn't exist?** Repository returns `null` → service throws `ContactNotFoundException` → middleware → 404.
31. **What happens on a duplicate phone?** Service pre-check throws `DuplicateContactException` → 409; DB unique index is the backstop (23505).
32. **What is model vs business validation?** DataAnnotations on DTO (before action) vs rules in `ValidateBusinessRules` (inside service).
33. **What is camelCase JSON?** `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`; overridden where snake_case is required.
34. **How does the controller get its service?** Constructor injection: `new ContactsController(ContactService)` by the container.
35. **What is `CancellationToken`?** Lets callers/HTTP disconnect cancel long operations; threaded through every method.

## ADVANCED

36. **Why did the seeder previously crash at startup?** A singleton hosted service tried to consume scoped services directly from the root container.
37. **What is `IServiceScopeFactory` and why is it the fix?** It lets a singleton create its own scoped bucket, resolve scoped services from it, and dispose it — `ContactDataSeeder.cs:97-115`.
38. **Why can't a singleton consume a scoped service?** The scope (per-request workshop) doesn't exist at app startup; the root scope is app-long, so the scoped service's guaranteed dispose wouldn't ever run.
39. **What happens when the application starts?** Builder → services registered → `Build()` → middleware pipeline → hosted seeder: `EnsureCreated` + top-up to 1000 → `app.Run()`.
40. **What happens when a request reaches the API?** Kestrel → middleware (content-type; try) → routing → controller → service → repository → DbContext → SQL → back.
41. **How does EF translate LINQ to SQL?** Expression tree → provider (Npgsql EF provider) → parameterized SQL; materializes rows on execution (`ToListAsync`).
42. **How is the DB connection string chosen?** `DATABASE_*` env vars if any exist, else `ConnectionStrings:Phonebook` from appsettings.
43. **What is the composite index for?** `(created_at, id)` backs the default `ORDER BY created_at DESC, id DESC` (ContactRepository's `ThenByDescending(Id)` matches it).
44. **How does the unique-violation translator work?** `DbUpdateException` → unwrap to `Npgsql.PostgresException` `SqlState` 23505 → map constraint name to "phone"/"email" → `DuplicateContactException` (`ContactRepository.cs:120-151`).
45. **Why `EscapeLike`?** `%`, `_`, `\` in user input would otherwise act as `LIKE` wildcards; they get backslash-escaped (`ContactRepository.cs:153-167`).
46. **What is `WebApplicationFactory<Program>`?** The test-host that boots the real app and lets tests swap services — `PhonebookApiFactory.cs`.
47. **Why is there a `partial class Program`?** So tests can reference `Program` as the factory's app entry point.
48. **Why does `JSON` use `init` in responses?** Response DTOs are written once and never mutated — `ContactResponse`'s `init` properties.
49. **Why is `GetPageAsync` a tuple?** It returns both the page items and the matching total in one database round trip.
50. **How are tests isolated from a real database?** DI replacement: remove DbContext/repository/hosted service, add the in-memory repository as a singleton (`PhonebookApiFactory.cs:23-29`).

## REAL DEBUGGING QUESTIONS

51. **`GET /api/contacts` returns 500 — where do I look?** Middleware catch-all returns 500 and logs. Check Kestrel logs for the EF/Npgsql failure; the seeder's `EnsureCreated` may not have run, or Postgres is unreachable.
52. **The database is down — what happens?** Every query throws; `ContactNotFoundException` is not thrown, so the 500 path logs the Npgsql connection error.
53. **Controller seems never reached — what first?** Check Nginx path prefix: `/api/contacts` must reach `/contacts` (`proxy_pass` trailing slash). Then check route match (`[Route("contacts")]` + verb attributes).
54. **`POST /contacts` returns 400 with "Name is required." but I sent a name?** Actually the value was blank/whitespace → `TrimToNull` turned it into `null` → `[Required]` fired.
55. **`POST` returns 409 "Duplicate phone number."** Service pre-check found an existing row with that phone; also possible only-after a race that the DB index caught.
56. **Pagination numbers look weird — check what?** Confirm `page` is 0-based: `first = (page == 0)`, `Last = page >= totalPages - 1`.
57. **Search returns nothing for a value you know exists.** Confirm your value isn't `%`/`_` (they're escaped) and that the field is one of the four searched columns.
58. **Sort order flips names with equal values.** Expected: `ThenByDescending(Id)` tiebreak; two identical names order by descending id.
59. **Tests fail after a DI change.** The factory `RemoveAll`s specific services; a newly-registered scoped dependency must also be removed/added there.
60. **Frontend "Failed to fetch".** Start at the browser request, then Nginx proxy, then backend route, then logs — one component at a time (Part 25).

---

# PART 25 — DEBUGGING MINDSET

## Debug one component at a time

Never "try to understand the whole app." Bisect: pick the topmost layer that could be wrong, verify it, then move down one step.

### If the frontend says "Failed to fetch"

```
1. Browser DevTools → Network tab
      - Request never sent?   → frontend config (VITE_API_BASE_URL=/api)
      - CORS/URL wrong?       → confirm it's hitting :80 /api/...
2. Is Nginx up? (curl http://localhost/ )  → if down: Nginx layer
      - curl http://localhost/api/contacts → is it forwarded?
3. Backend up?  (curl http://localhost:8080/contacts )
      - 500 → look at API logs (step below)
      - connection refused → did the app start, wrong port ($PORT vs 8080)?
4. Logs: docker logs / dotnet run output → check for seeder/`EnsureCreated` errors
```

`curl` is your best friend: it lets you call exactly the URL you think a component should handle, without the browser, JS, or CORS in the way.

### If the API returns 500

Check from the inside out, cheapest first:

1. **Logs** — the middleware logs `Unexpected API error` with the exception stack (`ErrorHandlingMiddleware.cs:43`). Read that before anything.
2. **Is it the middleware 500 or a real crash?** 500 with `{"detail":"An unexpected server error occurred."}` = an exception reached the catch-all. 500 with different body = something earlier.
3. **Is PostgreSQL reachable?** `psql` can't connect → the app can't either. Connection string: env `DATABASE_*` or `appsettings.json`.
4. **Did the schema exist?** Seeder calls `EnsureCreatedAsync`. If seeding was skipped (1000+ rows) the table already exists — but a fresh DB with no seeder run means no table.
5. **Split-test the layers**:
   - Controller layer: hit `GET /contacts/1` (no DB logic beside fetch) — if that works, data queries are fine.
   - Repository/EF layer: look for `NpgsqlException`/`DbUpdateException` in logs.
   - If only *searches* fail, suspect the `ILIKE`/escape SQL. If only *writes* fail, suspect constraints.

### If the controller is "never reached" (e.g. 404/405)

1. Route mismatch: the URL after Nginx must be `/contacts...`, not `/api/contacts...` (trailing slash in `proxy_pass`).
2. Verb mismatch: `POST` to a `[HttpGet]` route yields 405.
3. Route param binding: `/contacts/foo` can't bind to `int id` → 400 before the action.

### If a database query "fails"

1. Reproduce with plain SQL in `psql` (same where/order/limit) — isolates EF from Postgres.
2. Check the unique-index backstop: duplicate writes throw `DbUpdateException` handled by `SaveChangesTranslatedAsync`; missing `ConstraintName` → generic "Duplicate contact information.".
3. Enable EF logging if needed (raise `Microsoft.EntityFrameworkCore` log level in appsettings) to see generated SQL.

### Golden rules

- **Always read the error first.** The exception type in logs points at the layer (Npgsql = DB, `DbUpdateException` = save, `InvalidOperationException` = DI/routing).
- **One variable at a time.** Change only Nginx path, or only a query param, never both.
- **Use `curl` to bypass the browser.**
- **Reproduce the minimal case** (e.g., one `GET /contacts/1` vs. the full list) to isolate pagination/search/sort.

---

# FINAL — EXPLAINING YOUR PROJECT

## The 30-second explanation

"This is an ASP.NET Core (.NET 8) REST API for a phonebook, ported from a Java Spring Boot backend. Nginx reverse-proxies `/api/*` to it on port 8080, stripping the `/api` prefix. Requests flow through one piece of middleware (global error handling) into a `ContactsController`, which delegates to a `ContactService` that enforces business rules — phone numbers must have exactly 10 digits, names can't contain digits, phone/email must be unique — then to an `IContactRepository` backed by EF Core and Npgsql writing to PostgreSQL. It supports paginated, searchable (`ILIKE` across four fields), sortable listing plus full CRUD. A hosted seeder deterministically fills up to 1000 contacts, reproducing the Java random sequence so both backends produce identical data. It has 69 automated tests, all passing."

## The 2-minute explanation

"This backend is the `backend-dotnet` folder: a `Phonebook.sln` containing the API project and an xUnit/Moq test project. Startup happens in `Program.cs` — it builds a connection string from `DATABASE_*` env vars or appsettings, registers the `PhonebookDbContext` (scoped, Npgsql provider), the `ContactService`, the `IContactRepository → ContactRepository`, and the `ContactDataSeeder` as a hosted service, then builds the pipeline starting with the error-handling middleware.

On request: Nginx (port 80) sends `/api/...` to port 8080 as `/...`. The middleware checks content type on POST/PUT and wraps the whole request in a try/catch that maps custom exceptions to JSON — 404 not-found, 409 duplicate, 400 invalid, 500 unexpected. `ContactsController` (route `contacts`) maps GET/POST/GET id/PUT id/DELETE id to service calls. The service validates (DataAnnotations on the request DTO run first via `[ApiController]`, then business rules), checks uniqueness (differently for create vs update so you don't flag yourself), parses `field,direction` sorts with `createdAt,desc` as the default, and computes page metadata. The repository translates all that to LINQ: `Where` with `EF.Functions.ILike` for search, `Skip(page*size).Take(size)` for pagination, `OrderBy`+`ThenByDescending(id)` for stable sorting, plus a `SaveChanges` wrapper that turns Postgres 23505 unique violations into friendly duplicate errors. EF Core + Npgsql produce the actual SQL.

The seeder starts with the app: `EnsureCreatedAsync`, then tops the table up to 1000 rows using a C# reimplementation of Java's LCRNG with a fixed seed so data matches the Java backend exactly. Testing swaps the real repository for an in-memory one, letting integration tests run the full HTTP stack with no database while unit tests use Moq against the service. Everything is wired by constructor injection, and the one subtle piece of engineering is the seeder being a singleton that must create its own scope (`IServiceScopeFactory`) to consume scoped DbContext services safely."

---

*Happy studying. You now own this backend: every layer, every flow, and every test.*