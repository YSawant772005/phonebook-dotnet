# Backend Knowledge Bytes — Phonebook API (.NET 8 / ASP.NET Core)

A beginner-to-complete teaching document built **only** from the real code in
`backend-dotnet`. Nothing in this document invents behavior that is not in the
code. Where something common is missing, it says so explicitly.

> If you see **"Not present in this implementation."** it means the project does
> not use that thing — do not assume it does just because it is popular.

---

## HOW TO READ THIS DOCUMENT

The document is split into small self-contained lessons called **Knowledge
Bytes**. Each byte explains exactly ONE concept, file, class, method, or
responsibility. Each byte answers the same questions:

1. **WHAT** is this?
2. **WHY** do we need it?
3. **WHERE** is it in the project?
4. **HOW** does it work?
5. **WHO** calls it?
6. **WHAT** does it call?
7. **WHAT data goes in?**
8. **WHAT data comes out?**
9. **Actual code** with line-by-line explanation.
10. **Real-world analogy.**
11. **Common beginner confusion.**
12. **Check yourself** — questions you should be able to answer.

Each concept is explained at three levels:
- **Simple** — everyday language.
- **Technical** — precise .NET/ASP.NET terminology.
- **Project** — what it means in *this* codebase.

You can read bytes in order, or jump to whatever you are debugging right now.

---

# PART 0 — THE BIG PICTURE

Before zooming into files, understand how the whole backend fits together.

```
            Client (browser / Postman / frontend)
                         │
                         ▼  HTTP request (JSON)
┌──────────────────────────────────────────────────────┐
│  web server (Kestrel) listening on 0.0.0.0:8080       │
│                                                      │
│  1. ErrorHandlingMiddleware     (checks/wrap)         │
│  2. Routing + Controllers       (ContactsController)  │
│  3. ContactService              (business rules)      │
│  4. IContactRepository          (ContactRepository)   │
│  5. PhonebookDbContext (EF Core)                      │
│  6. PostgreSQL database (via Npgsql)                  │
└──────────────────────────────────────────────────────┘
                         │
                         ▼  HTTP response (JSON)
            Client
```

Components that actually exist in this code (nothing else is drawn):

| Layer | File(s) | Responsibility |
|---|---|---|
| HTTP server | `Program.cs` (Kestrel) | Hosts the app, wires everything |
| Middleware | `Middleware/ErrorHandlingMiddleware.cs` | Rejects wrong Content-Type, turns exceptions into JSON errors |
| Controllers | `Controllers/ContactsController.cs` | Maps URLs/HTTP verbs to method calls |
| DTOs (in/out) | `Dtos/*.cs` | The shape of JSON the API accepts/returns |
| Service | `Services/ContactService.cs` | Business rules & orchestration |
| Repository abstraction | `Data/IContactRepository.cs` | Interface defining data operations |
| Repository implementation | `Data/ContactRepository.cs` | Talks to EF Core, translates DB errors |
| ORM | `Data/PhonebookDbContext.cs` | EF Core model of the database |
| Entity | `Models/Contact.cs` | The in-memory class representing a row |
| Database | PostgreSQL (via Npgsql) | Stores the data |
| Exceptions | `Exceptions/*.cs` | Typed failures understood by the middleware |
| Seeder | `Data/ContactDataSeeder.cs` | Fills the DB with 1000 fake contacts on startup |
| Tests | `tests/Phonebook.Api.Tests/*` | xUnit tests (unit + integration) |

### What happens when ONE request arrives

Take `GET /contacts?page=0&size=20`:

1. Kestrel accepts the HTTP request.
2. `ErrorHandlingMiddleware` runs first. It is a `GET`, so the Content-Type
   check is skipped, then it calls `_next(context)` passing the request deeper.
3. Routing (`app.MapControllers()`) matches `/contacts` to
   `ContactsController.GetContacts` with `page=0`, `size=20`.
4. The controller calls `_service.GetContactsAsync(...)`.
5. The service validates arguments, parses the sort, then calls
   `_repository.GetPageAsync(...)` (through the interface).
6. `ContactRepository` builds an EF Core query, executes it against PostgreSQL,
   and returns `(items, totalElements)`.
7. The service maps entities → DTOs and computes pagination metadata.
8. Control returns to the controller, which returns `Ok(result)`.
9. ASP.NET Core serializes the DTO to JSON (camelCase) and sends the 200
   response back through the middleware and out to the client.

If anything throws a known exception (e.g. contact not found), the middleware
catches it and writes a `{ "detail": "..." }` JSON error with the right status
code.

Every byte below zooms into one step of this picture.

---

# PART 1 — PROJECT STRUCTURE

## KB 1.0 — The solution layout

**WHAT:** A Visual Studio solution (`.sln`) that groups two .NET projects: the
API and its test suite.

**WHY:** A solution is a container so you can build/run tests and app together.

**WHERE:** `backend-dotnet/Phonebook.sln`, plus the folders it references.

**Actual layout** (this is the real structure on disk):

```
backend-dotnet/
├── Phonebook.sln
├── src/
│   └── Phonebook.Api/
│       ├── Program.cs                    # startup wiring
│       ├── Phonebook.Api.csproj
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       ├── Properties/launchSettings.json
│       ├── Controllers/ContactsController.cs
│       ├── Services/ContactService.cs
│       ├── Data/
│       │   ├── IContactRepository.cs
│       │   ├── ContactRepository.cs
│       │   ├── PhonebookDbContext.cs
│       │   └── ContactDataSeeder.cs
│       ├── Models/Contact.cs
│       ├── Dtos/
│       │   ├── ContactCreateRequest.cs
│       │   ├── ContactResponse.cs
│       │   └── ContactPageResponse.cs
│       ├── Middleware/ErrorHandlingMiddleware.cs
│       └── Exceptions/
│           ├── ContactNotFoundException.cs
│           ├── InvalidContactException.cs
│           └── DuplicateContactException.cs
└── tests/
    └── Phonebook.Api.Tests/
        ├── Phonebook.Api.Tests.csproj
        ├── GlobalUsings.cs
        ├── PhonebookApiFactory.cs
        ├── InMemoryContactRepository.cs
        ├── ContactsControllerTests.cs
        ├── ContactServiceTests.cs
        └── ContactDataSeederTests.cs
```

**Interesting detail:** the `.sln` only registers the *tests* project
(`Phonebook.Api.Tests`). The API project is still built, because the test
project references it via `ProjectReference`. So `dotnet build` on the solution
builds the API transitively.

**Simple analogy:** the solution is a "Team" folder. Inside, `src` is the main
app people use, `tests` is the QA team that double-checks the app.

**Common confusion:** "Where is the database schema?" There are **no EF Core
migrations** in this project. Tables are created automatically by
`db.Database.EnsureCreatedAsync(...)` inside the seeder (see KB 8.5). So the
"schema" lives in `PhonebookDbContext` code, not in migration files.

**Check yourself:**
1. What are the two .NET projects and where does each live?
2. How does the solution build the API if it only lists the test project?
3. Where does the database schema come from, given there are no migrations?

---

# PART 2 — APPLICATION STARTUP

## KB 2.1 — `Program.cs`: the whole app in one file (Top-Level Statements)

**WHAT:** The entry point of the application — the code that runs first when
the backend starts. This project uses **top-level statements**, a C# feature
where you write the main logic directly at the top of `Program.cs` without a
visible `class Program` / `Main` boilerplate.

**WHY:** ASP.NET Core apps need a well-defined startup sequence: read config,
register services, configure the pipeline, then run.

**WHERE:** `src/Phonebook.Api/Program.cs` (95 lines total).

**HOW:** Step by step through the real file:

```csharp
var builder = WebApplication.CreateBuilder(args);
```
Creates a `WebApplicationBuilder`, which loads `appsettings.json` config and the
environment into a place you can read from (`builder.Configuration`).

```csharp
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}
```
If an environment variable `PORT` is set, the web server listens on
`http://0.0.0.0:{port}` (all network interfaces, that port). If not set, the
app falls back to the `Urls` value in `appsettings.json`
(`http://0.0.0.0:8080`). This matters for Deployments (e.g. Docker/Railway) that
tell the app which port to use.

```csharp
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });
```
Registers MVC controllers and tells the JSON serializer to output camelCase
(`phoneNumber`, `createdAt`) instead of the C# property names
(`PhoneNumber`, `CreatedAt`). This is why test expectations use
`phone_number` for some fields — those come from `[JsonPropertyName]` (see
KB 3.2).

```csharp
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = ErrorResponseFactory.Create;
});
```
When model binding/validation fails (e.g. a required field missing, or raw JSON
is malformed) ASP.NET normally picks an automatic 400 response. Here it is
replaced with a custom factory `ErrorResponseFactory.Create` so the error body
has the exact `{ "detail": ... }` shape this API uses everywhere. (KB 7.4.)

```csharp
string connectionString = BuildConnectionString(builder.Configuration);

builder.Services.AddDbContext<PhonebookDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IContactRepository, ContactRepository>();
builder.Services.AddScoped<ContactService>();
builder.Services.AddHostedService<ContactDataSeeder>();
```
The heart of **Dependency Injection** (KB 3.x):
- `ContactRepository` needs a `PhonebookDbContext` (EF Core); registering the
  DbContext with the Npgsql provider lets EF Core be created per request.
- `IContactRepository` is implemented by `ContactRepository`. Code asks for the
  *interface*, DI gives the *concrete class*.
- `ContactService` is registered so the controller can receive it.
- `AddHostedService<ContactDataSeeder>` schedules the seeder to run when the
  app starts (KB 8.2).

```csharp
var app = builder.Build();
```
Frozen configuration → creates the actual application/pipeline.

```csharp
app.UseMiddleware<ErrorHandlingMiddleware>();
app.MapControllers();
app.Run();
```
`UseMiddleware` inserts our error handler at the very front of the HTTP
pipeline. `MapControllers` wires up routing so `/contacts` reaches
`ContactsController`. `Run` blocks forever, serving requests.

```csharp
public partial class Program
{
}
```
This empty `partial class Program` exists so the test project
(`PhonebookApiFactory : WebApplicationFactory<Program>`) has a stable type to
point at when it boots the whole app in tests (KB 9.4). Top-level statements
normally generate a hidden `Program` class; marking it `partial` "publishes" it.

```csharp
internal static class ErrorResponseFactory
{
    public static IActionResult Create(ActionContext context) { ... }
}
```
A helper class at the bottom of the same file. It is `internal` (only visible
inside the API project) and is referenced above by the `ApiBehaviorOptions`
factory. Explained fully in KB 7.4.

**Simple analogy:** Startup is like opening a restaurant. First you unpack the
ingredients (config), then you hire staff (register services), arrange the line
of chefs (middleware pipeline), print the menu routes (`MapControllers`), then
open the doors (`Run`) and keep serving forever.

**Common confusion:**
- There is no `class Program` `with Main` visible — top-level statements hide
  it. The `partial class Program` at the bottom is only there for tests.
- Middleware order matters. `UseMiddleware<ErrorHandlingMiddleware>()` must
  come *before* `MapControllers()` in the list so errors inside controllers are
  caught by the middleware.

**WHO calls it:** The .NET runtime calls `Program`'s implicit `Main` when the
process starts. No one else "calls" it — it is the root of everything.

**WHAT does it call:** `ErrorResponseFactory.Create`, `BuildConnectionString`,
`AddControllers/AddDbContext/AddScoped/AddHostedService/UseMiddleware/
MapControllers/Run`, and (as its first action when the app starts) the DI
container starts `ContactDataSeeder`.

**Check yourself:**
1. What happens on line `var builder = WebApplication.CreateBuilder(args);`?
2. Where does the app pick up its port, and what are the two sources?
3. Why does `partial class Program` exist?
4. Why must `UseMiddleware` come before `MapControllers`?

---

## KB 2.2 — `BuildConnectionString`: environment variables vs `appsettings`

**WHAT:** A local function that decides **where the database is** and builds a
PostgreSQL connection string.

**WHY:** In production you must not hardcode credentials, and they may be
injected as environment variables. This function: if any database env var is
present → build the string from env vars; otherwise → fall back to the
connection string in `appsettings.json`.

**WHERE:** `Program.cs` lines 10–32.

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

Line by line:
- The five `string?` reads pull values from environment variables. They can be
  `null` (not set).
- The `if` checks whether *any* of them is set. If yes, we are in an
  environment that prefers env-var config.
- `new NpgsqlConnectionStringBuilder { ... }` builds a valid connection string
  object. Each property uses `?? default` — if the env var is missing, a
  sensible default is used (`localhost`, `5432`, `phonebook_db`,
  `phonebook_user`, `phonebook_password`).
  - `Port = dbPort is null ? 5432 : int.Parse(dbPort)` — the port env var is a
    string, so it must be parsed to `int`. `??` cannot be used here because
    `5432` here is a ternary default, not a natural "null → default" case — but
    the effect is the same: default `5432`.
- `return connection.ConnectionString;` — `NpgsqlConnectionStringBuilder`
  serializes itself into a standard
  `Host=...;Port=...;Database=...;Username=...;Password=...` string.
- Otherwise `return configuration.GetConnectionString("Phonebook")!;` reads the
  `"ConnectionStrings" : { "Phonebook" : "Host=localhost;Port=5432;..." }`
  section of `appsettings.json` (the `!` tells the compiler "trust me, this is
  not null").

**Simple analogy:** The app asks "Where is the database?" First it checks the
*deployment envelope* (environment variables). If the envelope says anything
about the DB, use that. Otherwise use the *read-me card* that ships with the
app (`appsettings.json`).

**Common confusion:**
- The appsettings fallback is NOT read from environment first — env vars win
  only if at least one DB env var is present. If only `DATABASE_PORT` is set and
  everything else is missing, all defaults + that port are used, not
  appsettings.
- `GetConnectionString("Phonebook")` uses the exact key name `Phonebook` from
  `appsettings.json`.

**WHO calls it:** `Program.cs`, line 52: `string connectionString =
BuildConnectionString(builder.Configuration);` — once, at startup.

**WHAT does it call:** `Environment.GetEnvironmentVariable`,
`NpgsqlConnectionStringBuilder`, `IConfiguration.GetConnectionString`.

**Data in:** `IConfiguration` (holds appsettings) + process env vars read
directly.
**Data out:** a `string` PostgreSQL connection string.

**Check yourself:**
1. When would appsettings connection string be used instead of env vars?
2. What are the defaults for host/port/db/user/password?
3. Why does `Port` use `int.Parse` while other fields do not?

---

## KB 2.3 — Dependency Injection container (AddDbContext / AddScoped)

**WHAT:** **Dependency Injection (DI)** = a class receives the objects it needs
instead of creating them itself. **Simple:** like a restaurant handing an
already-made plate to the waiter instead of the waiter cooking.

**Technical:** ASP.NET Core ships an in-built DI container. In `Program.cs` you
*register* types with a **lifetime**; later, when code asks for a type in a
constructor, the container constructs it (and its own dependencies, transitively)
and hands it over.

**Project:** In this codebase:
- `ContactRepository` asks for `PhonebookDbContext` (its data dependency).
- `ContactService` asks for `IContactRepository`.
- `ContactsController` asks for `ContactService`.
- The seeder asks for `IServiceScopeFactory` + `ILogger<ContactDataSeeder>`.

The exact registrations:

```csharp
builder.Services.AddDbContext<PhonebookDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IContactRepository, ContactRepository>();
builder.Services.AddScoped<ContactService>();
builder.Services.AddHostedService<ContactDataSeeder>();
```

- `AddDbContext<PhonebookDbContext>(...)` registers EF Core's DbContext for
  scoped lifetime and configures it to talk to PostgreSQL via **Npgsql**.
- `AddScoped<IContactRepository, ContactRepository>()` says "when someone needs
  `IContactRepository`, give them a `ContactRepository`."
- `AddScoped<ContactService>()` says "create a `ContactService`." The container
  sees `ContactService`'s constructor needs `IContactRepository`, resolves it
  (which itself needs `PhonebookDbContext`), and builds the whole chain.
- The container can **construct** these all the way down because none of the
  constructors are ambiguous — this is *constructor injection*.

**Lifetimes in this project:**
- **Scoped** = one instance per HTTP request. `PhonebookDbContext`,
  `ContactRepository`, `ContactService` are scoped, so a single request shares
  one DbContext (which tracks the entities changed during that request — KB
  6.1).
- **Singleton-ish** `IHostedService` — the seeder lives for the whole app
  lifetime (KB 8.1).
- The seeder needs *its own* `PhonebookDbContext` at startup, which is outside
  any HTTP request; that is why it creates a scope manually via
  `IServiceScopeFactory` (KB 8.2).

Constructor injection in the real constructors:

```csharp
public ContactsController(ContactService service) { _service = service; }
public ContactService(IContactRepository repository) { _repository = repository; }
public ContactRepository(PhonebookDbContext db) { _db = db; }
public ContactDataSeeder(IServiceScopeFactory scopeFactory, ILogger<ContactDataSeeder> logger) { ... }
```

**Simple analogy:** The container is a manager who knows how to build every
employee (`Service → Repository → DbContext`) and hands each one their
pre-built team. Classes never search for their own team members; they just
receive them.

**Common confusion:**
- Try to `new ContactService()` manually without DI and you must also `new`
  the repository, which needs `new` a DbContext with options... The container
  removes this pain.
- Scoped vs singleton: if a singleton held a scoped DbContext it would keep a
  stale object forever, so the code correctly keeps DB stuff scoped.

**WHO/WHAT calls it:** `Program.cs` registers; the container creates instances
during requests, and at startup for the seeder.

**Data in:** constructor parameter values.
**Data out:** fully constructed objects.

**Check yourself:**
1. Which three constructors in the app perform constructor injection?
2. What does `AddScoped<IContactRepository, ContactRepository>()` mean in plain words?
3. Why must the seeder create its own DI scope?

---

## KB 2.4 — `CancellationToken`: polite interruption

**WHAT:** A `CancellationToken` lets a long-running operation stop early if the
client goes away or the server shuts down.

**WHY:** If a client disconnects mid-request, continuing to query PostgreSQL
wastes resources. Cancellation lets EF Core stop the query.

**Project:** Nearly every public method in this codebase threads a
`CancellationToken cancellationToken = default` through:

- Controller: `GetContactsAsync(..., CancellationToken cancellationToken = default)`
- Service: `GetContactsAsync(int page, ..., CancellationToken cancellationToken)`
- Repository: `GetPageAsync(..., CancellationToken cancellationToken)` → `.ToListAsync(cancellationToken)`, `.LongCountAsync(cancellationToken)`, etc.

The framework automatically provides the token for HTTP request handlers; it is
canceled when the client disconnects. `default` means "no token" when omitted.

**Simple analogy:** You order food and leave the restaurant — the chef is told
to stop making your plate.

**Common confusion:** Passing the token to `Task.CompletedTask`-style fake
repository methods (tests) is harmless — the fakes just ignore it.

**Check yourself:**
1. Where does the controller get its token from?
2. Why does the service accept and forward the token to the repository?

---

# PART 3 — CONFIGURATION

## KB 3.1 — `appsettings.json`: the configuration files

**WHAT:** JSON files holding runtime settings.

**WHY:** Keep settings out of code so the same binary can run differently in
different environments.

**WHERE:** `appsettings.json` (applies everywhere) and
`appsettings.Development.json` (applies only in the Development environment,
overriding/adding to the first). Loaded automatically by the builder.

`appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Urls": "http://0.0.0.0:8080",
  "ConnectionStrings": {
    "Phonebook": "Host=localhost;Port=5432;Database=phonebook_db;Username=phonebook_user;Password=phonebook_password"
  }
}
```

- `Logging.LogLevel`: how chatty each category logs.
  - `Default: Information` — app logs at Info.
  - `Microsoft.AspNetCore: Warning` and `Microsoft.EntityFrameworkCore: Warning`
    — framework noise reduced to warnings only.
- `AllowedHosts: "*"` — any host header is allowed (no restriction).
- `Urls: http://0.0.0.0:8080` — default listen address/port (overridden if the
  `PORT` env var is set, see KB 2.1).
- `ConnectionStrings.Phonebook` — the fallback DB connection string used by
  `BuildConnectionString` when no DB env vars are set (KB 2.2).

`appsettings.Development.json` only tweaks logging (sets
`Microsoft.AspNetCore` but not EntityFrameworkCore, so EF details reappear in
Development).

**Simple analogy:** the box the laptop ships in has general instructions; the
Development box adds sticky notes for your local machine.

**Common confusion:** `appsettings.Development.json` is only used when
`ASPNETCORE_ENVIRONMENT=Development` (set in `launchSettings.json`). In
production you do not ship that file — or, if you do, the environment variable
decides.

**Not present in this implementation:** No secrets file, no Azure Key Vault
variant, no per-environment `appsettings.Production.json`.

**Check yourself:**
1. Where does the app's default port come from?
2. Which file wins: `appsettings.json` or `appsettings.Development.json`?
3. Why would you set `Microsoft.EntityFrameworkCore` to `Warning`?

---

## KB 3.2 — `launchSettings.json`: local dev profile

**WHAT:** Settings for *local* run/debug (Visual Studio or `dotnet run`).

**WHERE:** `src/Phonebook.Api/Properties/launchSettings.json`.

Key facts from the file:
- Three profiles: `http` (port 5265), `https` (7127), `IIS Express`.
- Each sets `ASPNETCORE_ENVIRONMENT=Development`.
- The stale `launchUrl: "weatherforecast"` is leftover template boilerplate —
  this app has no weatherforecast endpoint (a nice "only use what exists"
  reminder).

**Simple analogy:** the dev's own desk setup — not shipped to production.

**Common confusion:** Production never reads `launchSettings.json`. The real
runtime port comes from `PORT` env var / `Urls` in appsettings.

**Check yourself:**
1. What environment do these profiles use?
2. Does production use this file?

---

# PART 4 — CONTROLLERS (the HTTP entry point)

## KB 4.1 — What is a Controller?

**WHAT:** A class whose methods become **HTTP endpoints**. ASP.NET Core's
routing maps a URL + HTTP verb to a method.

**WHY:** Controllers are the "receptionist" of the API: parse the request,
delegate real work, translate the result into an HTTP response. They contain no
business logic or database code in this project.

**WHERE:** `src/Phonebook.Api/Controllers/ContactsController.cs`.

**HOW — the class declaration:**

```csharp
[ApiController]
[Route("contacts")]
public sealed class ContactsController : ControllerBase
{
    private readonly ContactService _service;

    public ContactsController(ContactService service)
    {
        _service = service;
    }
```

- `[ApiController]` enables convenience behaviors: automatic 400 on invalid
  model state (using our custom `InvalidModelStateResponseFactory`), automatic
  binding source inference (`int id` in the route is read from the URL, a
  `ContactCreateRequest` parameter is read from the JSON body), and others.
- `[Route("contacts")]` — the base route. No trailing slash. So this controller
  serves `/contacts`, `/contacts/{id}`.
- `sealed` — cannot be subclassed.
- `: ControllerBase` — base class giving helpers like `Ok(...)`,
  `CreatedAtAction(...)`.
- Constructor injection: the DI container gives it a `ContactService`
  (KB 2.3).

**Simple analogy:** A receptionist who never cooks (service) and never checks
the larder (database) — only takes orders and presents results.

**Common confusion:**
- `[ApiController]` also makes `[FromBody]`/`[FromQuery]` mostly automatic.
  Even though the code spells out `[FromQuery]` / `[FromBody]` explicitly,
  that is fine — those attributes make it crystal clear.
- A controller without `[ApiController]` would give generic
  `ProblemDetails`-style 400 bodies; that custom factory is why this API
  returns `{ "detail": ... }`.

**Not present in this implementation:** no `[Authorize]`, no `[AllowAnonymous]`,
no authentication/identity, no CORS, no versioning, no Swagger.

**Check yourself:**
1. What two attributes decorate the controller and what do they do?
2. How does the controller obtain its `ContactService`?
3. What base class does it extend?

---

## KB 4.2 — `GET /contacts` — list with pagination & search

**WHAT:** Returns a page of contacts.

**Route:** `[HttpGet]` on the base route → `GET /contacts`.

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

- Query parameters with defaults: `page=0`, `size=20`, `search`/`sort` optional.
- `ActionResult<ContactPageResponse>` — the method may return any status code,
  but on success returns a `ContactPageResponse`.
- `await _service.GetContactsAsync(...)` — delegates ALL logic to the service
  (pagination math, validation, sort parsing, DB call).
- `Ok(result)` — HTTP 200 with the DTO; ASP.NET serializes it to JSON.

**Simple analogy:** You ask the receptionist for "page 0, 20 per page". They
hand the request to the librarian (service) and present the answer card.

**Common confusion:** The controller does not validate `page`/`size` — that is
the service's `InvalidContactException` job (KB 5.3). Controller only forwards.

**Check yourself:**
1. What are the defaults for `page` and `size`?
2. Who actually validates those values?
3. What status code does `Ok` produce?

---

## KB 4.3 — `POST /contacts` — create a contact

**WHAT:** Creates a new contact from a JSON body and returns `201 Created`.

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

- `[FromBody]` — the whole JSON body becomes a `ContactCreateRequest` (this is
  where DataAnnotations validation ran before the method executes — KB 7.1).
- `CreatedAtAction(nameof(GetContact), new { id = created.Id }, created)` —
  HTTP **201**, with a `Location` header pointing at the new resource's
  `GET /contacts/{id}` URL, and the created DTO in the body.
  `nameof(GetContact)` keeps the reference safe if the method was renamed.
- A contact's `Id` is only known after insertion — so the body returned
  includes the generated `id` and `created_at`.

**Simple analogy:** Registering a new customer — you get back their ID card and
"go to room `{id}` to view".

**Common confusion:** `CreatedAtAction` vs `Ok`: create returns 201 + location,
a plain success would return 200. The tests assert 201 for create.

**Check yourself:**
1. How does the caller learn the new contact's id?
2. What two things does `CreatedAtAction` produce?
3. What happens if the body is missing `name`? (KB 7.1/7.5)

---

## KB 4.4 — `GET /contacts/{id}` — fetch one contact

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

- `{id}` is a **route segment**; `[ApiController]` binds `int id` from it
  automatically.
- If no such contact exists, the service throws `ContactNotFoundException` and
  the middleware turns it into 404 (KB 7.3) — the controller never handles it.
- Otherwise returns `Ok(result)` → HTTP 200.

**Data in:** integer id.
**Data out:** `ContactResponse` JSON, or 404 `{ "detail": "Contact not found." }`.

**Check yourself:**
1. Where does `id` come from?
2. What happens when id doesn't exist, and who decides the 404?

---

## KB 4.5 — `PUT /contacts/{id}` — full update

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

- `PUT` = replace the whole resource at `{id}` with the body contents.
- Body is a full `ContactCreateRequest` (same shape as create — that is why the
  service reuses the same DTO for create and update).
- If `{id}` does not exist → 404 (via middleware).
- Returns the **updated** contact as HTTP 200.

**Simple analogy:** Filling out a new form for the same person — the whole
record is replaced with the new data.

**Common confusion:** PUT sends the *entire* object. If a field is omitted, it
is treated as null/empty here (the `Apply` method copies what is present).

**Check yourself:**
1. Why can create and update share the same request DTO?
2. Which response code is the success code for update?

---

## KB 4.6 — `DELETE /contacts/{id}` — remove a contact

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

- `ActionResult` (no generic) — no body DTO, just a status + message.
- The service throws 404 if the id is missing.
- On success returns 200 with a tiny anonymous object `{ message = "... " }`.

**Simple analogy:** Removing the address-book card; the receptionist confirms
with a message.

**Check yourself:**
1. Why is the return type not `ActionResult<ContactResponse>`?
2. What JSON body does a successful delete produce?

---

# PART 5 — DTOs (Data Transfer Objects)

## KB 5.1 — Why DTOs?

**WHAT:** DTOs are plain classes that define the exact JSON shape the API
accepts and returns. They are deliberately *not* the database entity.

**WHY:** You do not want to expose the DB entity (`Models/Contact`) directly —
the API should control its own contract (field names, which fields are exposed,
validation) and stay decoupled from the database schema.

**Project:** three DTO files under `src/Phonebook.Api/Dtos/`.

**Simple analogy:** The kitchen's inventory sheet (entity) is for staff; the
menu (DTO) is what guests see. Even if the kitchen renames an item, the menu
stays the same.

**Common confusion:** DTOs here are not exactly the same as the entity — e.g.
`ContactResponse` has `[JsonPropertyName("phone_number")]` and
`[JsonPropertyName("created_at")]` that the entity does not have, and
`ContactCreateRequest` trims values on set.

**Check yourself:**
1. In one sentence: why separate DTOs from entities?
2. Which DTO is for requests and which for responses?

---

## KB 5.2 — `ContactCreateRequest` (input DTO)

**WHAT:** The JSON shape a client must send to create/update a contact.

**WHERE:** `src/Phonebook.Api/Dtos/ContactCreateRequest.cs`.

```csharp
public sealed class ContactCreateRequest
{
    private string? _name;
    private string? _phoneNumber;
    private string? _email;
    private string? _address;

    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(255, ErrorMessage = "Name cannot exceed 255 characters.")]
    public string? Name
    {
        get => _name;
        set => _name = TrimToNull(value);
    }
    ...
```

What matters:

1. **Backing fields + `TrimToNull` setters.** Every string property trims
   whitespace, and returns `null` if the trimmed value is empty:
   ```csharp
   private static string? TrimToNull(string? value)
   {
       if (value is null) return null;
       string trimmed = value.Trim();
       return trimmed.Length == 0 ? null : trimmed;
   }
   ```
   So `"   "` for `Name` becomes `null` → triggers `[Required]`. This is why the
   test `ValidatesBlankNameTrimsToRequired` returns 400 "Name is required." even
   though `"   "` was sent. Also, service duplicates checks compare trimmed
   values — `" 9876543210 "` is checked as `"9876543210"`.

2. **DataAnnotations** (from `System.ComponentModel.DataAnnotations`):
   - `[Required]` on `Name` and `PhoneNumber` (with custom messages).
   - `[MaxLength(...)]` on `Name`, `Email`, `Address`.
   - `[EmailAddress]` on `Email` — structural email check.
   These are evaluated by ASP.NET's **model validation** before the controller
   method runs (see KB 7.1).

3. **`[JsonPropertyName("phone_number")]` on `PhoneNumber`** — the JSON field
   is `phone_number` (snake_case) even though the C# property is `PhoneNumber`.
   This matches the "reference" Java backend naming and the frontend contract.
   `Email`, `Name`, `Address` use default names (their C# names, but note the
   global camelCase policy only affects *serialization* output, not deserialization
   — and `PhoneNumber`'s JSON name is explicitly snake_case here).

**Simple analogy:** The form a customer must fill in — blank spaces are erased,
required fields marked, length limits set.

**Common confusion:**
- Properties are nullable (`string?`) but marked `[Required]`. The nullability
  is about the *code* contract (the DTO may be partially filled), whereas
  `[Required]` is the *API contract*. The `!` in the service (`contact.Name =
  request.Name!;`) tells the compiler "we vetted this, it is not null here".
- `[Required]` on a note: `Name` has `[Required]`, so a trimmed-to-null name
  fails. `PhoneNumber` also required.

**Data in:** the raw JSON request body.
**Data out:** a `ContactCreateRequest` instance, trimmed, ready for service
validation.

**Check yourself:**
1. What does `TrimToNull` do and why does it affect `[Required]`?
2. Why is `phone_number` in snake_case in JSON but `PhoneNumber` in C#?
3. Which two properties are required?

---

## KB 5.3 — `ContactResponse` (output DTO for one contact)

**WHAT:** The JSON shape returned for a single contact.

**WHERE:** `src/Phonebook.Api/Dtos/ContactResponse.cs`.

```csharp
public sealed class ContactResponse
{
    public string? Name { get; init; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; init; }

    public string? Email { get; init; }
    public string? Address { get; init; }
    public int Id { get; init; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; }

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

- **`init`-**only properties: they can only be set during object creation
  (object initializer). This makes the response immutable after creation — safer
  and expresses "this is a read-only snapshot".
- `[JsonPropertyName("phone_number")]` / `("created_at")` — JSON output uses
  snake_case for these two (the serializer already lowercases/camelCases the
  rest, so `Name` is `"name"`, `Id` → `"id"`).
- `From(Contact)` is a **static factory method**: converts a `Contact` entity
  to a DTO — a mapping from model → DTO used by the service.

**Simple analogy:** The nicely formatted information card shown to the public.

**Common confusion:** `From` is called *before* the controller; the controller
receives already-mapped DTOs, never entities. Check `ContactResponse.From` usage
in the service, e.g. `return ContactResponse.From(await FindContactAsync(...))`.

**Check yourself:**
1. Why `init` instead of `set`?
2. Which JSON field names differ from C# property names and how?
3. Who calls `ContactResponse.From`?

---

## KB 5.4 — `ContactPageResponse` (output DTO for a page)

**WHAT:** The JSON envelope for a paginated list — mirrors Spring/Java
"Page" shape (content + metadata) so the frontend contract stays consistent
with the reference backend.

**WHERE:** `src/Phonebook.Api/Dtos/ContactPageResponse.cs`.

```csharp
public sealed class ContactPageResponse
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

- `Content` — the actual contacts on this page (defaults to an empty list so it
  is never null in JSON: `= new()`).
- `Page`, `Size` — echoed query parameters.
- `TotalElements` — total matching rows across all pages (`long`).
- `TotalPages` — how many pages exist.
- `First`, `Last` — booleans so the client knows if there is a previous/next
  page without extra math.

Serialized JSON example (from a test):
```json
{
  "content": [ { "name": "Customer 44", ... } ],
  "page": 0,
  "size": 20,
  "totalElements": 45,
  "totalPages": 3,
  "first": true,
  "last": false
}
```

**Simple analogy:** A library's results screen: current page number, rows shown
per page, total books found, total pages, and whether you can go back/forward.

**Common confusion:** `Content` uses `List<ContactResponse>` (mutable) while
`ContactResponse` is immutable — deliberate: the page is assembled dynamically
by the service, then serialized.

**Check yourself:**
1. Which field holds the actual contacts?
2. How could the frontend know a page is the last one?
3. What makes `Content` never serialize as `null`?

---

# PART 6 — SERVICE LAYER

## KB 6.1 — What is a Service layer?

**WHAT:** A class that holds **business rules and orchestration** — the "brain"
between the controller (HTTP) and the repository (database).

**WHY:** Controllers stay thin, repositories stay database-focused, and the
rules of the domain (validation, uniqueness, pagination math, DTO mapping) live
in one testable place.

**Project:** `ContactService` is the only service. It is **scoped** and injected
into the controller.

**Simple analogy:** The waiter (controller) takes your order; the chef
(service) decides it is valid, checks inventory (uniqueness), and coordinates
the kitchen. The pantry person (repository) does the actual fetching.

**Common confusion:** There is **no interface** for the service (no
`IContactService`). The controller depends on the concrete `ContactService`.
That is a valid design choice in this codebase — logic still lives cleanly in
one class, and unit tests use the concrete service with a mocked repository.

**Check yourself:**
1. What responsibilities live in the service vs the controller vs the repository?
2. Is there an `IContactService`? (No.)

---

## KB 6.2 — `ContactService` preview: dependencies + the phone regex

**WHERE:** `src/Phonebook.Api/Services/ContactService.cs`.

```csharp
public sealed class ContactService
{
    private static readonly Regex PhonePattern = new(@"^\+?[0-9][0-9\s().-]*$", RegexOptions.Compiled);

    private readonly IContactRepository _repository;

    public ContactService(IContactRepository repository)
    {
        _repository = repository;
    }
```

- `PhonePattern` is a **static compiled regex** run once for performance:
  `^\+?[0-9][0-9\s().-]*$`
  - `^` start, `\+?` optional leading `+`, `[0-9]` first char must be a digit,
    then zero or more digits/spaces/parens/dots/dashes, `$` end.
  - It *allows* formatting characters but does *not* enforce exactly 10 digits —
  that extra check happens in `ValidateBusinessRules` (KB 6.10).
- The service talks to the **interface** `IContactRepository` (KB 7.1), which
  keeps it decoupled from the concrete EF Core class.

**Simple analogy:** The service has one trusted "phone specialist" regex that
pre-checks the format, plus a phone card (the interface) for talking to
whoever stores contacts.

**Check yourself:**
1. Why is `PhonePattern` static + compiled?
2. What part of the phone check is NOT done by this regex?

---

## KB 6.3 — `GetContactsAsync`: validating, searching, paginating

```csharp
public async Task<ContactPageResponse> GetContactsAsync(
    int page, int size, string? search, string? sort, CancellationToken cancellationToken)
{
    if (page < 0)
    {
        throw new InvalidContactException("Page must be zero or greater.");
    }

    if (size < 1 || size > 100)
    {
        throw new InvalidContactException("Size must be between 1 and 100.");
    }

    string? normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
    if (normalizedSearch is not null && normalizedSearch.Length > 100)
    {
        throw new InvalidContactException("Search must not exceed 100 characters.");
    }

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

Step by step:
- **Guard clauses** (page ≥ 0, size 1..100): throw `InvalidContactException`,
  which the middleware turns into **400**. Note the controller does NOT do this
  — the service is responsible.
- **Search normalization**: whitespace-only/search-chars-below-100 → `" jane "`
  becomes `"jane"`. Search longer than 100 chars → 400.
- `ParseSort(sort)` converts the raw string like `"name,asc"` into a typed
  `ContactSortSpec` (KB 6.4). If `sort` is null/blank → default `createdAt,desc`.
- Calls the repository; receives a **tuple** `(items, totalElements)`.
- **totalPages math**: `Math.Ceiling(totalElements / (double)size)` — 45
  elements / 20 → 2.25 → 3 pages. If 0 elements → 0 pages.
- Builds the page DTO:
  - `Content` = each item mapped to `ContactResponse` via `ContactResponse.From`.
  - `First = page == 0`.
  - `Last = page >= totalPages - 1` (page 2 of 3 → last true).

**Simple analogy:** The librarian answers: "Here are rows X..Y (the page) and the
grand total (all books)." The chef computes "you're on the last page".

**Data in:** page, size, search, sort, token.
**Data out:** a fully computed `ContactPageResponse`.

**Check yourself:**
1. Which two guard clauses can throw, and what status do they map to?
2. How is `totalPages` computed and why the `(double)` cast?
3. How are entities converted into DTOs here?

---

## KB 6.4 — `ParseSort`: turning `"name,asc"` into a typed spec

```csharp
private static ContactSortSpec ParseSort(string? sort)
{
    if (string.IsNullOrWhiteSpace(sort))
    {
        return new ContactSortSpec("createdAt", Descending: true);
    }

    string[] parts = sort.Trim().Split(',', StringSplitOptions.None);
    if (parts.Length != 2)
    {
        throw new InvalidContactException("Sort must use field,direction format.");
    }

    string property = parts[0] switch
    {
        "name" => "name",
        "phoneNumber" => "phoneNumber",
        "email" => "email",
        "createdAt" => "createdAt",
        "id" => "id",
        _ => throw new InvalidContactException("Unsupported sort field."),
    };

    bool descending = parts[1].ToUpperInvariant() switch
    {
        "ASC" => false,
        "DESC" => true,
        _ => throw new InvalidContactException("Sort direction must be asc or desc."),
    };

    return new ContactSortSpec(property, descending);
}
```

- **Default**: no `sort` → `new ContactSortSpec("createdAt", Descending: true)`
  — newest first. This is why `GET /contacts` without sort returns
  "Customer 44" first in tests.
- Splits `"name,asc"` on comma into exactly two parts. Wrong format (`"name"`)
  → 400 "Sort must use field,direction format."
- **Switch expression** (C#) validates the field against an allow-list of
  exactly the fields the repository can order by (`name`, `phoneNumber`,
  `email`, `createdAt`, `id`). Anything else → 400 "Unsupported sort field."
- Direction: `ASC` → ascending (false), `DESC` → descending (true). Anything
  else → 400.
- Returns a typed `ContactSortSpec(property, descending)` (KB 7.2).

**Simple analogy:** A form: pick a column name from a dropdown (no typos
allowed) and pick ↑ or ↓.

**Check yourself:**
1. What is the default sort?
2. Which field names are allowed?
3. What happens with `sort=name`? With `sort=name,diagonal`?

---

## KB 6.5 — `GetContactAsync` + the private helper `FindContactAsync`

```csharp
public async Task<ContactResponse> GetContactAsync(int id, CancellationToken cancellationToken)
{
    return ContactResponse.From(await FindContactAsync(id, cancellationToken));
}

private async Task<Contact> FindContactAsync(int id, CancellationToken cancellationToken)
{
    if (id <= 0)
    {
        throw new InvalidContactException("must be greater than 0");
    }

    return await _repository.GetByIdAsync(id, cancellationToken)
        ?? throw new ContactNotFoundException();
}
```

- `FindContactAsync` is the shared **lookup-or-fail** helper reused by
  get/update/delete.
- `id <= 0` → 400 `"must be greater than 0"` (test
  `RejectsNonPositiveId` expects exactly that message).
- `_repository.GetByIdAsync(...)` returns `Contact?` (nullable). The `??` throws
  `ContactNotFoundException` if null → middleware maps to **404**
  `"Contact not found."`.
- `GetContactAsync` maps the found entity to a `ContactResponse`.

**Simple analogy:** "Show me the card for id 7" → if the card is missing, the
service says "no such card" (404), else hands back a neat copy (DTO).

**Common confusion:** the 400-vs-404 split: 400 = you gave a meaningless id
(≤0), 404 = a valid id that doesn't exist.

**Check yourself:**
1. What rule makes id 0 a 400 but id 999999 a 404?
2. Where is the `??` null-coalescing used with the repository result?

---

## KB 6.6 — `CreateContactAsync`: validate → unique → build → save

```csharp
public async Task<ContactResponse> CreateContactAsync(
    ContactCreateRequest request, CancellationToken cancellationToken)
{
    ValidateBusinessRules(request);
    await EnsureUniqueAsync(request, null, cancellationToken);

    var contact = new Contact();
    Apply(request, contact);
    await _repository.AddAsync(contact, cancellationToken);
    return ContactResponse.From(contact);
}
```

Pipeline for create:
1. `ValidateBusinessRules(request)` — service-level rules (name no digits,
   phone exactly 10 digits). Throws 400 on failure (KB 6.10).
2. `EnsureUniqueAsync(request, currentId: null, ...)` — for create there is no
   "current" contact, so duplicates check *against everything*. Throws 409 on
   conflict (KB 6.9).
3. `new Contact()` + `Apply(request, contact)` — copy DTO fields to entity
   (KB 6.11).
4. `_repository.AddAsync(...)` — inserts; repository sets `CreatedAt` if unset
   and the DB/EF generates `Id`.
5. `ContactResponse.From(contact)` — map entity back to response (including the
   now-populated `Id` / `CreatedAt`) and return for the controller's 201.

**Simple analogy:** Create = full onboarding: verify your papers, make sure you
are not already registered (phone/email), file the record, hand you your ID card.

**Check yourself:**
1. List the 5 steps of create in order.
2. What is `null` passed as, and why for create?
3. Why does the returned DTO include `Id` and `created_at`?

---

## KB 6.7 — `UpdateContactAsync`: validate → find → unique → save

```csharp
public async Task<ContactResponse> UpdateContactAsync(
    int id, ContactCreateRequest request, CancellationToken cancellationToken)
{
    ValidateBusinessRules(request);
    Contact contact = await FindContactAsync(id, cancellationToken);
    await EnsureUniqueAsync(request, id, cancellationToken);

    Apply(request, contact);
    await _repository.UpdateAsync(contact, cancellationToken);
    return ContactResponse.From(contact);
}
```

Differences from create:
- **Order matters**: validate rules first, then *find the existing entity* (404
  if missing), then uniqueness with `currentId = id` — so a contact may keep its
  own phone/email (KB 6.9), then apply the new values to the **same tracked
  entity**, then `UpdateAsync`, then map back.

**Simple analogy:** Editing an existing card: take the card out (404 if absent),
verify the new phone isn't used by someone *else*, rewrite the card, put it back.

**Check yourself:**
1. Why is `FindContactAsync` called before `EnsureUniqueAsync`?
2. What does passing `id` into `EnsureUniqueAsync` change? (KB 6.9)

---

## KB 6.8 — `DeleteContactAsync`

```csharp
public async Task DeleteContactAsync(int id, CancellationToken cancellationToken)
{
    await _repository.DeleteAsync(await FindContactAsync(id, cancellationToken), cancellationToken);
}
```

- Looks up the contact (404 if absent), then deletes it. The inner `await` runs
  first because it is used as an argument.
- No DTO mapping — nothing returned (the controller returns the message itself).

**Check yourself:**
1. What happens if you delete a non-existent id?
2. Why is there no `ContactResponse` here?

---

## KB 6.9 — `EnsureUniqueAsync`: duplicate detection (with the `currentId` trick)

```csharp
private async Task EnsureUniqueAsync(
    ContactCreateRequest request, int? currentId, CancellationToken cancellationToken)
{
    bool duplicatePhone = currentId is null
        ? await _repository.ExistsByPhoneNumberAsync(request.PhoneNumber!, cancellationToken)
        : await _repository.ExistsByPhoneNumberAndIdNotAsync(request.PhoneNumber!, currentId.Value, cancellationToken);

    if (duplicatePhone)
    {
        throw new DuplicateContactException("Duplicate phone number.");
    }

    if (request.Email is not null)
    {
        bool duplicateEmail = currentId is null
            ? await _repository.ExistsByEmailAsync(request.Email, cancellationToken)
            : await _repository.ExistsByEmailAndIdNotAsync(request.Email, currentId.Value, cancellationToken);

        if (duplicateEmail)
        {
            throw new DuplicateContactException("Duplicate email.");
        }
    }
}
```

- `currentId` is `null` for **create** and the contact's `id` for **update**.
- **Ternary selects the right repository call:**
  - Create: "does any row have this phone?" → `ExistsByPhoneNumberAsync`.
  - Update: "does any row *other than me* have this phone?" →
    `ExistsByPhoneNumberAndIdNotAsync` (the `AndIdNot` matters: without it,
    updating a contact keeping its own phone would look like a duplicate!).
- Same pattern for email, but email is *optional* — the whole check is guarded
  by `if (request.Email is not null)`.
- `request.PhoneNumber!` — the `!` is safe because `[Required]` already ran at
  the model level (KB 7.1), so it is non-null by this point.
- On any duplicate → `DuplicateContactException` → middleware → **409 Conflict**
  with the specific message (phone vs email).

**Simple analogy:** Signing up for a username: "is it taken?" (create) vs
"is it taken by someone who is not you?" (update).

**Check yourself:**
1. What is the difference between the two phone-check repository methods?
2. Why would update fail if the `AndIdNot` variants were not used?
3. When is the email uniqueness check skipped entirely?

---

## KB 6.10 — `ValidateBusinessRules`: the service's own rules

```csharp
private static void ValidateBusinessRules(ContactCreateRequest request)
{
    if (request.Name is not null && request.Name.Any(char.IsDigit))
    {
        throw new InvalidContactException("Name cannot contain numbers.");
    }

    if (request.PhoneNumber is not null &&
        (!PhonePattern.IsMatch(request.PhoneNumber) ||
         Regex.Replace(request.PhoneNumber, @"\D", "").Length != 10))
    {
        throw new InvalidContactException("Phone number must contain exactly 10 digits.");
    }
}
```

- **Name**: `request.Name.Any(char.IsDigit)` — if any character is a digit →
  400 "Name cannot contain numbers."
- **Phone**: two checks combined with `||`:
  - `!PhonePattern.IsMatch(...)` — must satisfy the format regex (KB 6.2).
  - `Regex.Replace(request.PhoneNumber, @"\D", "").Length != 10` —
    `\D` matches non-digits, so this *strips formatting* (`+`, `-`, spaces,
    parentheses, dots) and then counts digits. Must be exactly 10.
  - So `"+91 98765-43210"` → after stripping = 13 digits → rejected;
    `"9876543210"` → 10 digits → OK; `"123-456-789"` → 9 digits → rejected with
    "Phone number must contain exactly 10 digits."
- This runs *after* DataAnnotations already validated the shape/length; this is
  the second validation layer (see the two-layer model in KB 7.1).

**Simple analogy:** A bouncer with two lists: no numbers in names; phone numbers
must be valid *and* exactly 10 digits long.

**Check yourself:**
1. What does `\D` mean and what is replaced by `""`?
2. Would `"9876543210"` pass both conditions?
3. Are these rules enforced by the same mechanism as `[Required]`? (No — see 7.1.)

---

## KB 6.11 — `Apply`: copying DTO → entity

```csharp
private static void Apply(ContactCreateRequest request, Contact contact)
{
    contact.Name = request.Name!;
    contact.PhoneNumber = request.PhoneNumber!;
    contact.Email = request.Email;
    contact.Address = request.Address;
}
```

- Sets the entity's fields from the request DTO, overwriting whatever was there.
- `!` on `Name`/`PhoneNumber` asserts non-null (they survived `[Required]` and
  the 400-uniqueness checks, so this is safe).
- `Id` and `CreatedAt` are deliberately NOT touched here — `Id` is
  database-generated, and `CreatedAt` is only set on create by the repository
  (KB 7.5) and never rewritten on update.

**Check yourself:**
1. Which entity fields are not overwritten and why?
2. Why the `!` operators?

---

# PART 7 — REPOSITORY LAYER

## KB 7.1 — The Repository pattern + `IContactRepository`

**WHAT:** A **repository** is an abstraction that says "I give you the data you
need; you don't care if it comes from PostgreSQL, a file, or memory." The
**interface** declares WHAT can be asked; the implementation decides HOW.

**WHY:**
- Controllers/services depending on the interface can be **unit-tested** with a
  fake (this project has `InMemoryContactRepository` for that exact purpose).
- Data-access details (LINQ queries, EF Core calls, PostgreSQL specifics) stay
  inside one class.

**WHERE:** `src/Phonebook.Api/Data/IContactRepository.cs` (interface) and
`ContactRepository.cs` (implementation, KB 7.3+).

**The whole interface, verbatim:**

```csharp
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

Note the **search/duplicate shapes**: because the service needs both
"exists anywhere" and "exists excluding me", the interface exposes both, and
the EF implementation turns each into a different query.

**Simple analogy:** The interface is the menu; `ContactRepository` is the
kitchen that actually cooks those dishes.

**Common confusion:** the interface never references EF Core, PostgreSQL, or
LINQ — those live only in the implementation. (The `ContactSortSpec` type is
shared, in the same file — see KB 7.2.)

**Check yourself:**
1. Name two reasons to program against `IContactRepository` instead of the concrete class.
2. Which interface method returns a tuple?

---

## KB 7.2 — `ContactSortSpec`: a tiny record for sort instructions

```csharp
public sealed record ContactSortSpec(string Property, bool Descending);
```

- A **record** — a compact C# type whose equality is based on its values, ideal
  for "data bundles".
- Holds `Property` (one of `name`, `phoneNumber`, `email`, `createdAt`, `id`)
  and `Descending` (true/false).
- Created by the service's `ParseSort` (KB 6.4) and consumed by the repository's
  `ApplySort` (KB 7.7).

**Simple analogy:** a sticky note reading "sort books by title, Z→A".

**Check yourself:**
1. Why is this a record rather than a class with many members?

---

## KB 7.3 — `ContactRepository`: class skeleton + `AsNoTracking` pagination

`ContactRepository` implements `IContactRepository` and depends on
`PhonebookDbContext` (injected via constructor, scoped).

```csharp
public sealed class ContactRepository : IContactRepository
{
    private const string LikeEscapeChar = "\\";

    private readonly PhonebookDbContext _db;

    public ContactRepository(PhonebookDbContext db)
    {
        _db = db;
    }

    public async Task<(IReadOnlyList<Contact> Items, long TotalElements)> GetPageAsync(
        int page, int size, ContactSortSpec sort, string? search, CancellationToken cancellationToken)
    {
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
    }
```

Step by step:
- `_db.Contacts.AsNoTracking()` — start the query from the `Contacts` DbSet,
  **without change tracking**. For read-only pages this is faster and avoids
  keeping copies in the context.
- **Search**: builds `%pattern%` (contains-style wildcards), wraps the search
  term with `EscapeLike` to neutralize `%`, `_`, `\` (KB 7.6). Then LINQ
  `.Where(...)` filters name/phone/email/address with `ILike`
  (case-insensitive LIKE, KB 6.4 of the DB section). Email and Address checks
  guard against null.
- `LongCountAsync` — runs `COUNT(*)` in the database; this is the
  `TotalElements` (total matching rows, all pages).
- `ApplySort(query, sort)` → then `Skip(page * size).Take(size)` — classic
  pagination translated to SQL `OFFSET` / `LIMIT`.
- Returns the tuple `(items, totalElements)` the service destructures.

**Simple analogy:** Ask the librarian: first "how many books match?" then "give
me books 40–59, sorted."

**WHO calls it:** `ContactService.GetContactsAsync` (only consistent caller of
this method in the codebase).
**WHAT does it call:** EF Core `Queryable` extensions → SQL against PostgreSQL.

**Data in:** page, size, sort spec, search (nullable), token.
**Data out:** `(List<Contact>, long totalElements)`.

**Check yourself:**
1. What does `AsNoTracking` do and why is it fine here?
2. What SQL roughly does `Skip(page*size).Take(size)` produce?
3. Why is the count query separate from the data query?

---

## KB 7.4 — `GetByIdAsync` and the `ExistsBy...` family

```csharp
public async Task<Contact?> GetByIdAsync(int id, CancellationToken cancellationToken)
{
    return await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
}

public async Task<bool> ExistsByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
{
    return await _db.Contacts.AnyAsync(c => c.PhoneNumber == phoneNumber, cancellationToken);
}

public async Task<bool> ExistsByPhoneNumberAndIdNotAsync(string phoneNumber, int id, CancellationToken cancellationToken)
{
    return await _db.Contacts.AnyAsync(c => c.PhoneNumber == phoneNumber && c.Id != id, cancellationToken);
}

public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken)
{
    return await _db.Contacts.AnyAsync(c => c.Email == email, cancellationToken);
}

public async Task<bool> ExistsByEmailAndIdNotAsync(string email, int id, CancellationToken cancellationToken)
{
    return await _db.Contacts.AnyAsync(c => c.Email == email && c.Id != id, cancellationToken);
}
```

- `FirstOrDefaultAsync` → `Contact?` (null if none). Drives the 404 in the
  service.
- `AnyAsync` → `bool`, translated to SQL `EXISTS (...)` which stops at the
  first match (efficient).
- `AndIdNot` variants add `c.Id != currentId` so a contact's own phone/email is
  excluded when updating (KB 6.9).

**Simple analogy:** The four `AnyAsync` methods are like asking "is this
license plate registered?" vs "is it registered to someone other than me?"

**Check yourself:**
1. What does `FirstOrDefaultAsync` return when nothing matches?
2. What SQL does `AnyAsync` translate to, roughly?
3. When is the `AndIdNot` variant used by the service?

---

## KB 7.5 — Writes: `AddAsync`, `AddRangeAsync`, `UpdateAsync`, `DeleteAsync`

```csharp
public async Task<Contact> AddAsync(Contact contact, CancellationToken cancellationToken)
{
    if (contact.CreatedAt == default)
    {
        contact.CreatedAt = DateTime.UtcNow;
    }

    _db.Contacts.Add(contact);
    await SaveChangesTranslatedAsync(cancellationToken);
    return contact;
}

public async Task AddRangeAsync(IReadOnlyCollection<Contact> contacts, CancellationToken cancellationToken)
{
    _db.Contacts.AddRange(contacts);
    await SaveChangesTranslatedAsync(cancellationToken);
}

public async Task UpdateAsync(Contact contact, CancellationToken cancellationToken)
{
    _db.Contacts.Update(contact);
    await SaveChangesTranslatedAsync(cancellationToken);
}

public async Task DeleteAsync(Contact contact, CancellationToken cancellationToken)
{
    _db.Contacts.Remove(contact);
    await SaveChangesAsync(cancellationToken);
}
```

- **AddAsync**: if `CreatedAt` was never set (`default` = `DateTime.MinValue`),
  stamp `DateTime.UtcNow`. Then `Add` marks the entity as "to insert", and
  `SaveChangesAsync` flushes the INSERT; the returned entity now has the DB
  generated `Id`/defaults. Called by the service's create.
- **AddRangeAsync**: same but for many — used by the seeder to insert up to
  1000 contacts at once.
- **UpdateAsync**: `Update` marks the whole entity dirty and overwrites every
  mapped column on save. Used by service update.
- **DeleteAsync**: `Remove` marks as deleted; note it calls
  **`SaveChangesAsync`** (the plain one), not `SaveChangesTranslatedAsync` —
  because a DELETE cannot cause a duplicate-key violation, so no translation is
  needed.
- `SaveChangesAsync` / `SaveChangesTranslatedAsync` are small private helpers
  that wrap the EF save; the `Translated` version catches unique-violation
  errors (KB 7.8).

**Simple analogy:** Adding a card to a box, stapling updates, or taking a card
out — in each case "empty the tray" = `SaveChanges`.

**Common confusion:** `Add`/`Update`/`Remove` only change the in-memory change
tracker. **Nothing reaches the database until `SaveChanges` runs.** That is why
unique-violation translation happens around `SaveChanges` and not around
`Add`.

**Check yourself:**
1. Why does `DeleteAsync` use the non-translated save helper?
2. When does the actual INSERT/UPDATE/DELETE SQL execute?
3. Who calls `AddRangeAsync`?

---

## KB 7.6 — `EscapeLike`: protecting the search from wildcard injection

```csharp
private static string EscapeLike(string value)
{
    var builder = new StringBuilder(value.Length + 4);
    foreach (char c in value)
    {
        if (c == '\\' || c == '%' || c == '_')
        {
            builder.Append(LikeEscapeChar);
        }

        builder.Append(c);
    }

    return builder.ToString();
}
```

**Problem:** SQL `LIKE` treats `%` and `_` as wildcards. If a user searches for
`50%` or `_jane_`, those characters would act as wildcards, matching more than
intended (and an escaped `\` could even alter the pattern unexpectedly).

**Fix:** this function prefixes `\`, `%`, `_` with the escape character `\`
(the same `LikeEscapeChar = "\\"` passed into `ILike`), so they match literally.
Combined in `GetPageAsync`: `"%" + EscapeLike(search) + "%"`.

**Simple analogy:** On a parking lot with sensor gates, a ticket that contains a
sensor's code must be "escaped" so it is read as a plain word, not a command.

**Check yourself:**
1. Which three characters are escaped and why?
2. Where is the escape character evidence that PostgreSQL knows about it? (The
   third arg `LikeEscapeChar` of `EF.Functions.ILike`.)

---

## KB 7.7 — `ApplySort`: safe (whitelisted) ORDER BY

```csharp
private static IQueryable<Contact> ApplySort(IQueryable<Contact> query, ContactSortSpec sort)
{
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
        : sort.Property switch
        {
            "name" => query.OrderBy(c => c.Name),
            ...
            _ => throw new InvalidContactException("Unsupported sort field."),
        };

    return ordered.ThenByDescending(c => c.Id);
}
```

- Uses the **allow-listed property string** already validated by the service's
  `ParseSort` and switches it to a typed `OrderBy`/`OrderByDescending` — this
  prevents SQL-injection-y dynamic column names.
- Always appends `ThenByDescending(c => c.Id)` as a **deterministic tiebreaker**,
  so rows with identical sort keys still have a stable, consistent order across
  pagination.
- The `email` sort uses `c.Email` directly (nullable, fine for ordering).

**Simple analogy:** Sort cards by cardholder name, Z→A, and when names tie,
sort by their desk number descending so ties are always resolved the same way.

**Check yourself:**
1. Why can the column string not be directly concatenated into a query?
2. What is the purpose of `ThenByDescending(c => c.Id)`?
3. What happens if a field outside the whitelist reaches here?

---

## KB 7.8 — `SaveChangesTranslatedAsync`: database uniqueness → typed exception

```csharp
private async Task SaveChangesTranslatedAsync(CancellationToken cancellationToken)
{
    try
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception) when (IsUniqueViolation(exception, out string constraintName))
    {
        if (constraintName.Contains("phone", StringComparison.OrdinalIgnoreCase))
        {
            throw new DuplicateContactException("Duplicate phone number.");
        }

        if (constraintName.Contains("email", StringComparison.OrdinalIgnoreCase))
        {
            throw new DuplicateContactException("Duplicate email.");
        }

        throw new DuplicateContactException("Duplicate contact information.");
    }
}

private static bool IsUniqueViolation(DbUpdateException exception, out string constraintName)
{
    constraintName = exception
        .GetBaseException() as PostgresException is { } postgresException
        ? postgresException.ConstraintName ?? ""
        : exception.InnerException?.Message ?? "";

    return exception.GetBaseException() is PostgresException { SqlState: "23505" } ||
           constraintName.Length > 0;
}
```

**Why this exists — the "DB uniqueness safety net":** the service pre-checks
duplicates (KB 6.9), but two concurrent requests could both pass that check and
then both INSERT. The database itself enforces uniqueness via unique indexes
(KB 6.3 of DB section), and when the index rejects a row, PostgreSQL raises
SQLSTATE `23505` (unique_violation). This method catches that and turns it into
the same typed `DuplicateContactException` the service throws — so the reaction
is identical whether the duplicate was caught early (service) or at the last
millisecond (database).

- `catch ... when (IsUniqueViolation(...))` — an **exception filter**: only
  enters the handler if the inner exception is genuinely a unique violation.
- `IsUniqueViolation` walks to `GetBaseException()`; the innermost is a
  `PostgresException` (Npgsql's PostgreSQL-level error). It reads
  `ConstraintName` (e.g. `IX_contacts_PhoneNumber`) and checks `SqlState:
  "23505"`.
- Back in the handler, `constraintName.Contains("phone")` / `("email")` picks
  the specific message and throws the typed exception. The middleware turns the
  exception into 409 (KB 7 section on middleware).

**Simple analogy:** A busy registration desk. The service checks the phone-book
twice before writing (pre-check), but if two people write the same entry
simultaneously, the door-lock (database unique index) refuses the second one and
the bouncer yells the same message.

**Common confusion:** `DbUpdateException` wraps the real error; that is why
`GetBaseException()`/`InnerException` digging is required. If the constraint
name is empty but the SQLState is 23505, it still counts as unique violation.

**Check yourself:**
1. What SQLSTATE identifies a unique violation in PostgreSQL?
2. Why keep the DB-level check when the service already checks?
3. Which method is used for deletes and why not this one?

---

# PART 8 — DATABASE / ORM / MODELS

## KB 8.1 — What is Entity Framework Core (EF Core)?

**WHAT:** An **Object-Relational Mapper (ORM)**. It lets you work with database
rows as C# objects instead of writing SQL by hand. You write LINQ queries; EF
translates them to SQL for your provider (here: PostgreSQL via **Npgsql**).

**WHY:** Faster development, type safety, and a single source of truth for the
schema in code.

**Project:** the `DbSet<Contact> Contacts` on `PhonebookDbContext` is how tables
and entities connect. Every repository method uses LINQ against it.

**Simple analogy:** EF Core is a bilingual assistant: you speak C#/LINQ, the
database speaks SQL, and the assistant translates precisely.

**Common confusion:** LINQ is *not* always executed client-side. The repository
queries like `Where(...).Skip(...).Take(...)` are translated into one SQL
statement executed in the database.

**Check yourself:**
1. What does the ORM translate for you?
2. Which NuGet packages make PostgreSQL the provider here? (`Npgsql.EntityFrameworkCore.PostgreSQL`)

---

## KB 8.2 — `PhonebookDbContext`: the bridge class

**WHERE:** `src/Phonebook.Api/Data/PhonebookDbContext.cs`.

```csharp
public sealed class PhonebookDbContext : DbContext
{
    public PhonebookDbContext(DbContextOptions<PhonebookDbContext> options)
        : base(options)
    {
    }

    public DbSet<Contact> Contacts => Set<Contact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ...
    }
}
```

- Constructor receives `DbContextOptions<PhonebookDbContext>` — registered in
  `Program.cs` with `UseNpgsql(connectionString)` when the DI container builds
  it (scoped, one per HTTP request).
- `DbSet<Contact> Contacts` — represents the `contacts` table; this property is
  what the repository queries (`_db.Contacts`).
- `OnModelCreating` is where the entity ↔ table mapping is configured fluently
  (columns, keys, indexes, constraints) — the "schema in code".

**Simple analogy:** The DbContext is the office manager who knows every desk in
the building (each `DbSet`) and how the building is configured.

**Check yourself:**
1. What does `DbSet<Contact>` represent?
2. How does the context know to use PostgreSQL?1
3. Why is the context scoped per request?

---

## KB 8.3 — `OnModelCreating`: the schema, configured in code

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Contact>(entity =>
    {
        entity.ToTable("contacts");

        entity.HasKey(c => c.Id);

        entity.Property(c => c.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd()
            .UseIdentityByDefaultColumn();

        entity.Property(c => c.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(255);

        entity.Property(c => c.PhoneNumber)
            .HasColumnName("phone_number")
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(c => c.Email)
            .HasColumnName("email")
            .HasMaxLength(255);

        entity.Property(c => c.Address)
            .HasColumnName("address")
            .HasColumnType("text");

        entity.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        entity.HasIndex(c => c.PhoneNumber).IsUnique();
        entity.HasIndex(c => c.Email).IsUnique();
        entity.HasIndex(c => new { c.CreatedAt, c.Id });
    });
}
```

What each block configures:
- `ToTable("contacts")` — table name is lowercase `contacts` (PostgreSQL style).
- `HasKey(Id)` — primary key.
- **Id**: column `id`, `ValueGeneratedOnAdd()` (DB assigns value on INSERT),
  `UseIdentityByDefaultColumn()` → PostgreSQL `GENERATED BY DEFAULT AS
  IDENTITY` (auto-incrementing without blocking a manually chosen id).
- **Name**: `name` column, `NOT NULL`, max 255 chars.
- **PhoneNumber**: `phone_number` column (snake_case), `NOT NULL`, max 32.
- **Email**: `email` column, max 255, **nullable** (no `IsRequired`).
- **Address**: `address` column, type `text` (unlimited length).
- **CreatedAt**: `created_at` column, `NOT NULL`, `timestamp with time zone`
  (PostgreSQL `timestamptz` — matches the `DateTime.UtcNow` stamps and the
  tests' `Z` assertions).
- **Indexes**: unique index on `phone_number`, unique index on `email` — these
  are the DB guarantees that make `SaveChangesTranslatedAsync` meaningful
  (KB 7.8); plus a composite index on `(created_at, id)` — the exact order used
  by the default sort with its id tiebreaker (KB 7.7), so pagination by default
  stays fast.

**Whose mapping makes snake_case JSON coincide?** Note the JSON name
`phone_number` (DTO) coincides with the DB column `phone_number` — but they are
independent decisions; the DTO uses `[JsonPropertyName]`, the DB uses
`HasColumnName`.

**Check yourself:**
1. Which columns are NOT NULL?
2. What two unique indexes exist and why do they matter for errors (KB 7.8)?
3. Why does the composite index order match the default sort?

---

## KB 8.4 — `Contact`: the entity/model

**WHERE:** `src/Phonebook.Api/Models/Contact.cs`.

```csharp
public sealed class Contact
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string PhoneNumber { get; set; } = null!;

    public string? Email { get; set; }

    public string? Address { get; set; }

    public DateTime CreatedAt { get; set; }
}
```

- Mirrors the `contacts` table columns (KB 8.3).
- `= null!` on `Name`/`PhoneNumber`: nullable reference types are enabled in
  this project, and these two are **required** by the DB — `null!` tells the
  compiler "I know better than you; this is never null when used," avoiding
  warnings (the values are ensured by validation before use).
- `Email`/`Address` nullable (`string?`) — optional.
- It is the inverse of the DTOs: entities carry database concerns, DTOs carry
  API-contract concerns.

**Simple analogy:** The entity is the "database row as a poker card"; the DTOs
are the rules printed for the players to see.

**Check yourself:**
1. Which properties are optional?
2. What does `null!` mean and why is it used?

---

## KB 8.5 — PostgreSQL + Npgsql specifics used here

- **Npgsql**: the ADO.NET provider for PostgreSQL. The packages are
  `Npgsql.EntityFrameworkCore.PostgreSQL` (EF provider) and its transitive
  `Npgsql`.
- **`EF.Functions.ILike`**: PostgreSQL's case-insensitive `ILIKE`. EF Core
  exposes it as `EF.Functions.ILike(column, pattern, escapeChar)`. Used in
  `GetPageAsync` for search — that is why searching `RAHUL` finds "Rahul
  Sharma".
- **`UseIdentityByDefaultColumn`**: PostgreSQL identity column.
- **`timestamp with time zone`**: PostgreSQL `timestamptz`.
- **`SqlState: "23505"`** and `PostgresException`: PostgreSQL-native error
  details surfaced through Npgsql (used in KB 7.8).
- **Connection string**: `Host=...;Port=...;Database=...;Username=...;Password=...`
  built by `BuildConnectionString`.

**Not present in this implementation:** no raw SQL, no stored procedures, no
`FromSqlRaw`, no migrations. Everything goes through LINQ or EF Core APIs.

**Check yourself:**
1. Where does `ILike` appear and why is it "case-insensitive LIKE"?
2. What PostgreSQL concept does `SqlState: 23505` map to?

---

## KB 8.6 — The real "migration": `EnsureCreatedAsync`

There are **no EF migrations** in this repo. Table creation happens at runtime:

```csharp
await db.Database.EnsureCreatedAsync(cancellationToken);   // inside ContactDataSeeder.StartAsync
```

`EnsureCreatedAsync` creates the database schema **if it does not exist**,
purely from the `OnModelCreating` mapping. It does *not* alter an existing
schema — if the DB already has tables, it does nothing.

**Simple analogy:** The first time the office opens, the desks are built from
the floor plan. Later openings just use the existing desks; nothing is
rebuilt or changed.

**Common confusion:** Migrations are the "production-grade" way to evolve a
schema. Here, for a demo/assignment backend, `EnsureCreatedAsync` is the chosen
mechanism — do not claim migrations exist.

**Check yourself:**
1. What exactly does `EnsureCreatedAsync` create and skip?
2. How would schema evolution be handled *if* migrations existed? (Not in this
   implementation.)

---

# PART 9 — VALIDATION & ERRORS

## KB 9.1 — Two validation layers (and exactly when each runs)

There are **two separate validation systems** in this app:

| Layer | Mechanism | Runs | Data source | Failure result |
|---|---|---|---|---|
| 1. Model validation | DataAnnotations on `ContactCreateRequest` (`[Required]`, `[MaxLength]`, `[EmailAddress]`) + trimming setters | Automatically, **before** the controller method, when the framework binds the JSON body | Request DTO | `InvalidModelStateResponseFactory` → 400 `{ "detail": ... }` |
| 2. Business/service rules | checks in `ContactService` (`ValidateBusinessRules`, pagination guards, `ParseSort`, `EnsureUniqueAsync`) | Inside service methods | DTO fields + query params | typed exceptions → middleware → 400 / 409 |

Layer 1 belongs to the **framework + DTO**. Layer 2 belongs to the **domain**.

**Why both?** Layer 1 catches *shape/size/format* problems cheaply at the edge;
layer 2 catches *business* problems (name digits, 10-digit phones, duplicates,
pagination, sorting) that annotations can't express.

**Check yourself:**
1. Give an example error produced by layer 1 and one by layer 2.
2. Which layer produces a 409?

---

## KB 9.2 — The three custom exceptions

**WHERE:** `src/Phonebook.Api/Exceptions/`.

- `ContactNotFoundException` — fixed message `"Contact not found."`
  (no arguments).
- `DuplicateContactException` — takes a message (`"Duplicate phone number."`,
  `"Duplicate email."`, ...).
- `InvalidContactException` — takes a message (validation errors).

All are `sealed` and subclass `Exception`. They are pure "signals": they carry a
message and nothing else. Their real value is that the middleware can pattern-
match on the *type* to choose a status code (KB 9.4).

```csharp
public sealed class ContactNotFoundException : Exception
{
    public ContactNotFoundException() : base("Contact not found.") { }
}
```

**Simple analogy:** Alert codes: red (404), orange (409), yellow (400) — the
middleware is the dispatcher that knows each code's meaning.

**Check yourself:**
1. Why use custom exception types instead of throwing `Exception`?
2. Which two exceptions carry a caller-supplied message?

---

## KB 9.3 — `ErrorHandlingMiddleware`: the central error gate

**WHERE:** `src/Phonebook.Api/Middleware/ErrorHandlingMiddleware.cs`.

**WHAT:** A **middleware** = a component in the HTTP request pipeline that can
act before and after the next component. This one is registered first in
`Program.cs`, so every request passes through it.

```csharp
public async Task InvokeAsync(HttpContext context)
{
    if (IsUnsupportedRequestBodyContentType(context))
    {
        await WriteErrorAsync(context, StatusCodes.Status415UnsupportedMediaType, "Content-Type must be application/json.");
        return;
    }

    try
    {
        await _next(context);
    }
    catch (ContactNotFoundException)
    {
        await WriteErrorAsync(context, StatusCodes.Status404NotFound, "Contact not found.");
    }
    catch (DuplicateContactException exception)
    {
        await WriteErrorAsync(context, StatusCodes.Status409Conflict, exception.Message);
    }
    catch (InvalidContactException exception)
    {
        await WriteErrorAsync(context, StatusCodes.Status400BadRequest, exception.Message);
    }
    catch (Exception exception)
    {
        _logger.LogError(exception, "Unexpected API error");
        await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, "An unexpected server error occurred.");
    }
}
```

- **Pre-check**: `IsUnsupportedRequestBodyContentType` (KB 9.4) rejects
  POST/PUT with a non-JSON `Content-Type` → **415**.
- **`await _next(context)`** — hands the request deeper (routing → controller →
  service → repository). Whatever the inner layers throw bubbles back up here.
- **Per-exception handling**:
  - `ContactNotFoundException` → 404 `"Contact not found."`.
  - `DuplicateContactException` → 409 with its message.
  - `InvalidContactException` → 400 with its message.
  - Anything else → logs the error and returns a **generic** 500
    `"An unexpected server error occurred."` (never leaks stack traces).
- `WriteErrorAsync`: guarded by `context.Response.HasStarted` (do nothing if the
  response is already being written), sets status + `application/json`, then
  writes `JsonSerializer.Serialize(new { detail })`:
  ```csharp
  context.Response.StatusCode = statusCode;
  context.Response.ContentType = "application/json";
  await context.Response.WriteAsync(JsonSerializer.Serialize(new { detail }), context.RequestAborted);
  ```

**Simple analogy:** A security gate: filters bad packages (415), and every
package that comes back with a problem is re-labeled with a standard tag
(`{ "detail": ... }`) — but the gate never tells outsiders the messy internals
(generic 500).

**Common confusion:** The middleware catches exceptions thrown *during the
pipeline*, but model-binding/validation failures are handled by MVC *before*
the controller — those go through `ErrorResponseFactory` (KB 9.5), not through
this catch. Both produce the same `{ "detail": ... }` shape, which is why the
tests pass for both paths.

**Check yourself:**
1. What status code is returned for a wrong Content-Type on POST/PUT?
2. Where do "Contact not found.", "Duplicate phone number.", and "must be
   greater than 0" get turned into HTTP responses?
3. Why is the unexpected-error message generic?

---

## KB 9.4 — `IsUnsupportedRequestBodyContentType` (inside the middleware)

```csharp
private static bool IsUnsupportedRequestBodyContentType(HttpContext context)
{
    if (!HttpMethods.IsPost(context.Request.Method) && !HttpMethods.IsPut(context.Request.Method))
    {
        return false;
    }

    string mediaType = context.Request.ContentType?.Split(';')[0].Trim() ?? string.Empty;
    return !mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);
}
```

- Only POST and PUT are body-bearing here (GET/DELETE have no body) — this is
  why the check ignores other verbs.
- `ContentType` may include parameters (`application/json; charset=utf-8`);
  splitting on `;` isolates the media type.
- Returns true when the media type is **not** `application/json` → 415.
- This mirrors the test `RejectsUnsupportedContentType`: posting with
  `text/plain` returns "Content-Type must be application/json.".

**Check yourself:**
1. Why are GET/DELETE skipped?
2. What does `Split(';')[0]` accomplish?

---

## KB 9.5 — `ErrorResponseFactory.Create`: the model-validation 400s

**WHERE:** bottom of `Program.cs` (lines 71–94).

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

- **When it runs:** ASP.NET's `[ApiController]` automatic model validation calls
  `InvalidModelStateResponseFactory` on any invalid model state (the factory set
  up in `Program.cs`).
- **Body-level failures** (malformed JSON, empty body, JSON type mismatch) are
  recorded under special ModelState keys `"$"`, `""` or the parameter name
  `"request"`. If any of those keys have errors → 400
  `"Request body must contain valid JSON."`. This matches the Java reference
  backend's behavior (as the code comment says).
- **Otherwise**, it finds the first validation error message and returns 400
  `{ detail: message }` — e.g. `"Name is required."`,
  `"Email cannot exceed 255 characters."`, `"Invalid email address."`.
- Returns `BadRequestObjectResult` (an `IActionResult`) with an anonymous
  object whose sole property is `detail`.

**Simple analogy:** A form-checker at the door labels each bad form with a
single reason slip, and says "incomprehensible/garbage" for forms that cannot
even be read.

**Check yourself:**
1. When does this factory run instead of the middleware catch?
2. What key names indicate body-level vs property-level errors?
3. Why does the anonymous object use `detail`?

---

# PART 10 — THE SEEDER (BACKGROUND SERVICE)

## KB 10.1 — What is `IHostedService`?

**WHAT:** A way to run code that lives as long as the app process: start code at
boot, optional periodic work, stop code at shutdown.

**WHY:** Here it is used to **auto-fill the database with sample data** on
startup, so the API is never empty.

**Project:** `ContactDataSeeder` implements `IHostedService` and is registered
with `AddHostedService<ContactDataSeeder>()` in `Program.cs`.

Interface shape used:
```csharp
public async Task StartAsync(CancellationToken cancellationToken) { ... }
public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
```

- `StartAsync` runs when the app starts.
- `StopAsync` returns an already-completed task (nothing to clean up).

**Simple analogy:** The office-runner who, at opening time, restocks the shelves
auto; at closing time does nothing.

**Common confusion:** A hosted service has no request context. It must obtain
dependencies through the DI scope factory (KB 10.2) instead of constructor-
injecting a scoped DbContext.

**Check yourself:**
1. What lifecycle methods does `IHostedService` require?
2. Where is the seeder registered?

---

## KB 10.2 — `StartAsync`: making its own DI scope

```csharp
public async Task StartAsync(CancellationToken cancellationToken)
{
    using IServiceScope scope = _scopeFactory.CreateScope();
    IServiceProvider services = scope.ServiceProvider;
    PhonebookDbContext db = services.GetRequiredService<PhonebookDbContext>();
    IContactRepository repository = services.GetRequiredService<IContactRepository>();

    await db.Database.EnsureCreatedAsync(cancellationToken);
    await SeedAsync(repository, cancellationToken);
}
```

- The seeder constructor gets `IServiceScopeFactory` (not the DbContext
  directly, which would be wrong lifetime).
- `CreateScope()` creates a fresh scope; `scope.ServiceProvider` resolves
  services inside it — the scoped `PhonebookDbContext` and scoped
  `IContactRepository`.
- `using` disposes the scope so its DbContext is released after seeding.
- **First**: `EnsureCreatedAsync` creates the schema if missing (KB 8.6).
- **Then**: `SeedAsync`.

**Check yourself:**
1. Why can't the seeder just constructor-inject `PhonebookDbContext`?
2. What does `EnsureCreatedAsync` do before seeding?

---

## KB 10.3 — `SeedAsync`: deterministic fake data up to 1000 rows

```csharp
public async Task SeedAsync(IContactRepository repository, CancellationToken cancellationToken)
{
    long existingCount = await repository.CountAsync(cancellationToken);
    if (existingCount >= SeedTarget)          // SeedTarget = 1000
    {
        _logger.LogInformation("Seed skipped: database already contains {Count} contacts.", existingCount);
        return;
    }

    IReadOnlyList<Contact> existing = await repository.GetAllAsync(cancellationToken);
    var usedPhoneNumbers = new HashSet<string>();
    var usedEmails = new HashSet<string>();

    foreach (Contact contact in existing)
    {
        if (contact.PhoneNumber is not null) usedPhoneNumbers.Add(contact.PhoneNumber);
        if (contact.Email is not null) usedEmails.Add(contact.Email);
    }

    int toCreate = (int)(SeedTarget - existingCount);
    var random = new JavaRandom(RandomSeed);   // RandomSeed = 4207
    var contacts = new List<Contact>(toCreate);

    for (int i = 0; i < toCreate; i++)
    {
        string firstName = FirstNames[random.NextInt(FirstNames.Length)];
        string lastName = LastNames[random.NextInt(LastNames.Length)];

        var contact = new Contact
        {
            Name = firstName + " " + lastName,
            PhoneNumber = NextUniquePhoneNumber(random, usedPhoneNumbers),
            Email = NextUniqueEmail(firstName, lastName, i, usedEmails),
            Address = Areas[random.NextInt(Areas.Length)] + ", " + Cities[random.NextInt(Cities.Length)],
            CreatedAt = DateTime.UtcNow,
        };

        contacts.Add(contact);
    }

    await repository.AddRangeAsync(contacts, cancellationToken);
    long total = await repository.CountAsync(cancellationToken);
    _logger.LogInformation("Seeded {Created} fake contacts. Total contacts now {Total}.", contacts.Count, total);
}
```

- **Threshold skip**: `existingCount >= 1000` → log and return; never deletes
  existing data (the doc-comment says existing table contents are never touched).
- **Existing collision avoidance**: it loads existing contacts into
  `HashSet<string>`s so generated phones/emails never collide with real data.
- **`JavaRandom(RandomSeed = 4207)`** — a custom random (KB 10.4) so the data
  is byte-for-byte identical to what the Java reference backend would produce
  with the same seed (see the tests in KB 10.5).
- The generator loop picks first/last names from hardcoded arrays, builds the
  phone via `NextUniquePhoneNumber` (first digit `6-9`, then 9 digits), a unique
  email like `firstlastN@example.com`, and an address `"{Area}, {City}"`.
- Inserts in one batch via `AddRangeAsync`, then logs totals.

**Simple analogy:** The office-runner counts the existing stock; if already
≥1000, he walks away. Otherwise he manufactures exactly the missing number of
cards, using a seed-locked random so every office branch in every city creates
the *same* cards.

**Check yourself:**
1. What condition skips seeding?
2. Why the `HashSet`s?
3. How many contacts are created when the DB starts empty? (1000)

---

## KB 10.4 — `JavaRandom`: replicating `java.util.Random`

The class doc-comment in the source is explicit:
> Reimplements the linear congruential generator (LCG) used by
> `java.util.Random(long seed)` so that freshly seeded data is byte-for-byte
> identical to the data produced by the Java reference backend for the same
> RANDOM_SEED.

```csharp
public sealed class JavaRandom
{
    private const long Multiplier = 0x5DEECE66DL;
    private const long Addend = 0xB;
    private const long Mask = (1L << 48) - 1;

    private long _seed;

    public JavaRandom(long seed)
    {
        _seed = (seed ^ Multiplier) & Mask;
    }

    private int NextBits(int bits)
    {
        _seed = (_seed * Multiplier + Addend) & Mask;
        return (int)((ulong)_seed >> (48 - bits));
    }

    public int NextInt(int bound)
    {
        if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound), "bound must be positive");
        int r = NextBits(31);
        int m = bound - 1;
        if ((bound & m) == 0)
        {
            return (int)((bound * (long)r) >> 31);
        }
        for (int u = r; u - (r = u % bound) + m < 0; u = NextBits(31))
        {
        }
        return r;
    }
}
```

- It is a **linear congruential generator (LCG)**: `seed = (seed * MULT + ADD) mod 2^48`,
  exactly Java's constants. For the same seed you get the same sequence — that's
  "deterministic".
- `NextInt(bound)` reproduces Java's rejection/loop logic so
  `random.NextInt(47)` returns the exact index Java would (`33` for `FirstNames`
  with seed 4207, per the seeder test).
- The comments/tests confirm the goal: deterministic data matching the Java
  reference backend.

**Simple analogy:** two chefs using the identical recipe book page number produce
identical dishes — that's what this class guarantees across two backends.

**Check yourself:**
1. What does "deterministic" mean here and why is it wanted?
2. Where does the class prove matches to Java? (seeder tests)

---

# PART 11 — TESTS

## KB 11.1 — Testing overview: unit + integration in one project

**WHAT:** The test project covers three layers:

| Test file | Type | What it proves |
|---|---|---|
| `ContactServiceTests.cs` | **unit** (Moq) | Service logic against a *mocked* repository |
| `ContactsControllerTests.cs` | **integration** (WebApplicationFactory + in-memory repo) | Full HTTP pipeline: middleware → routing → controller → service → fake repo |
| `ContactDataSeederTests.cs` | **unit** | Seeder skip/unique/quota/determinism + JavaRandom |

**WHY:** Verify behavior without a live PostgreSQL; exercise the real HTTP
pipeline.

**Not present in this implementation:** no tests that require a real PostgreSQL,
no EF Core in-memory provider tests, no test database.

**Package highlights** (from `Phonebook.Api.Tests.csproj`): `xunit`,
`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `coverlet.collector`,
`Moq` (mocking), `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`).

**Check yourself:**
1. Which test file mocks the repository?
2. Which test file runs the whole HTTP pipeline?

---

## KB 11.2 — `GlobalUsings.cs`: one `using` for all tests

```csharp
global using Xunit;
```
A single-line file that makes `Fact` / `Theory` available in every test file
without repeating `using Xunit;`. (ImplicitUsings + `global using`.)

**Check yourself:**
1. What does this file give every test file?

---

## KB 11.3 — `InMemoryContactRepository`: a hand-written fake (not a mock)

**WHERE:** `tests/Phonebook.Api.Tests/InMemoryContactRepository.cs`.

**WHAT:** A complete implementation of `IContactRepository` that stores
`Contact` objects in a `Dictionary<int, Contact>` with a `lock` for thread
safety, instead of a database.

```csharp
public sealed class InMemoryContactRepository : IContactRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Contact> _contacts = new();
    private int _nextId = 1;
    ...
    public Task<Contact> AddAsync(Contact contact, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (contact.CreatedAt == default) contact.CreatedAt = DateTime.UtcNow;
            contact.Id = _nextId++;
            _contacts[contact.Id] = contact;
            return Task.FromResult(contact);
        }
    }
    ...
}
```

- Imitates the DB shape: assigns `Id` automatically, stamps `CreatedAt`,
  supports `GetPageAsync` (with in-memory search/order/skip/take), `CountAsync`,
  `GetAllAsync`, `Exists...`, update/delete.
- `lock (_gate)` keeps concurrent test/boot operations safe.
- Extra helpers `Reset()`, `Seed(...)`, `Add(...)` used by the tests to set up
  or wipe data.
- It **clones** contacts on read paths (`new Contact { ... }`) so tests see
  copies, matching how EF returns tracked/untracked entities.

**Simple analogy:** A cardboard "practice database" — it behaves like the real
thing enough for tests but stores cards in a shoebox.

**Common confusion:** Moq (used in `ContactServiceTests`) *dynamically* fakes
an interface; this class *hand-writes* a real implementation. Both allow
isolation — service tests use `Mock`, controller tests use this fake.

**Check yourself:**
1. Why a `lock` in this fake?
2. How is `CreatedAt`/`Id` handled compared to the real repository?

---

## KB 11.4 — `PhonebookApiFactory`: booting the real app with a fake DB

**WHERE:** `tests/Phonebook.Api.Tests/PhonebookApiFactory.cs`.

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
            services.RemoveAll<IHostedService>();
            services.RemoveAll<PhonebookDbContext>();
            services.RemoveAll<DbContextOptions<PhonebookDbContext>>();
            services.RemoveAll<IContactRepository>();
            services.RemoveAll<ContactRepository>();

            services.AddSingleton<IContactRepository>(_repository);
        });
    }
}
```

- `WebApplicationFactory<Program>` launches the **entire real app** (thanks to
  `partial class Program`) inside the test host — full middleware, routing,
  controllers.
- `UseEnvironment("Testing")` — a distinct environment name.
- It **removes** the real bits we don't want in tests: the hosted seeder (so it
  does not stage data), the DbContext and its options (no PostgreSQL), and both
  repository registrations; then registers `_repository` — the shared in-memory
  fake — as a **singleton**.
- Because the fake is singleton and exposed via `Repository`, tests can seed and
  reset it.

**Simple analogy:** A full dress rehearsal: same stage, same actors (pipeline),
but the props (database) are cardboard.

**Check yourself:**
1. Why does the factory remove `IHostedService`?
2. What is replaced with what, and at what lifetime?

---

## KB 11.5 — `ContactsControllerTests`: black-box HTTP tests

**WHERE:** `tests/Phonebook.Api.Tests/ContactsControllerTests.cs` (565 lines —
the largest test file).

Structure:
```csharp
public sealed class ContactsControllerTests : IClassFixture<PhonebookApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly PhonebookApiFactory _factory;
    private readonly HttpClient _client;

    public ContactsControllerTests(PhonebookApiFactory factory)
    {
        _factory = factory;
        _factory.Repository.Reset();
        SeedDataset(_factory.Repository);
        _client = factory.CreateClient();
    }
    ...
}
```

- `IClassFixture<PhonebookApiFactory>` — one factory shared across all tests in
  this class (reused, not recreated per test).
- Every test constructor **resets** the fake and **seeds 45 contacts**
  (`SeedDataset`) so each test starts from a known state.
- `SeedDataset` builds 45 contacts (id 1 = "Rahul Sharma", id 2 = "Jane Doe",
  then "Customer i", phones `98765000XX`, emails, addresses, `CreatedAt =
  baseTime.AddHours(i)`).
- `_client = factory.CreateClient()` — an `HttpClient` pointed at the in-memory
  web host; tests call it like a real API.

What the tests cover (each is a black-box assertion over HTTP):
- Pagination: `totalElements: 45`, `totalPages: 3`, `size: 20`, `first/last`.
- Full page 1 (5 items) and page 0 of size 20 content shape.
- CRUD: create (201 + id & created_at), get by id, update (200), delete (200 +
  message), plus confirm delete → 404 afterward.
- Validation 400s: required name, blank name trimmed→required, name too long,
  email too long, invalid email, address too long, page/size/search/sort rules.
- Duplicates → 409 (phone and email).
- NotFound → 404 for unknown get/update/delete.
- Sort: `name,desc`, `name,asc`, `id,asc`, `phoneNumber,desc`, default
  `createdAt,desc`.
- Search: case-insensitive (`RAHUL` finds `Rahul Sharma`), by email.
- Content-Type → 415. Malformed/empty JSON → 400
  `"Request body must contain valid JSON."`.
- **Error shape contract**: `ErrorResponseBodyContainsOnlyDetail` asserts the
  body has exactly one property: `detail`.
- Nullable fields: contact with no email/address returns `null` for those.

Helper methods at the bottom: `Json(payload)` builds a `StringContent`, and
`ReadDetail(body)` parses `detail`.

**Simple analogy:** The QA person clicks every button and checks every label —
but through a real browser window aimed at the actual app (just the fake DB).

**Common confusion:** these are **integration tests**, not unit tests — they go
through real middleware/controllers but swap only the database. The Lambda
method name in the payload uses `D2` formatting (`"98765000" + i.ToString("D2")`)
so phones increment predictably.

**Check yourself:**
1. Why does every constructor reset and seed the repository?
2. How is the HTTP client created?
3. What single assertion guarantees the error contract stays `{ detail }`?

---

## KB 11.6 — `ContactServiceTests`: pure service logic with Moq

**WHERE:** `tests/Phonebook.Api.Tests/ContactServiceTests.cs`.

Core pattern:
```csharp
private readonly Mock<IContactRepository> _repository = new();
private readonly ContactService _service;

public ContactServiceTests()
{
    _service = new ContactService(_repository.Object);
}
```

- The **real** `ContactService` is given a **mock** repository — `_repository.Object` is a stand-in that returns whatever each test sets up.
- Tests verify both the *result* and the *call*:
  ```csharp
  _repository
      .Setup(r => r.GetPageAsync(0, 20, SortSpec("createdAt", true), "jane", It.IsAny<CancellationToken>()))
      .ReturnsAsync((new[] { contact }, 21L));
  ...
  _repository.Verify(r => r.GetPageAsync(...), Times.Once);
  ```
  - `Setup` says "when called like this, return that."
  - `Verify(...Times.Once)` says "make sure it was called exactly once, this
    way." This proves e.g. the service forwarded the *normalized* search
    `" jane "` → `"jane"` and the *parsed* default sort.
- Notable checks:
  - Pagination math: 21 total elements, size 20 → 2 pages.
  - **Update duplicate exclusion**: verifies `ExistsByPhoneNumberAndIdNotAsync`
    is used, and `ExistsByPhoneNumberAsync` is *never* called.
  - Duplicate email rejected before `AddAsync` is ever called
    (`Times.Never` on `AddAsync`).
  - Sort parsing: `name,asc` → `SortSpec("name", false)`.
  - Guards throw `InvalidContactException`.
  - **Trimming end-to-end**: input with spaces (`"  Jane Doe  "`,
    `" 9876543210 "`, ...) results in the entity fields being trimmed — captured
    with `.Callback((c, _) => cap = c)`.
  - NotFound → `ContactNotFoundException`; non-positive id → `InvalidContactException`.

**Simple analogy:** Bench tests for the chef: with a recipe that never actually
fetches the pantry (mocked), we check the chef's decisions and orders.

**Check yourself:**
1. What does `Setup` + `ReturnsAsync` do?
2. What does `Verify(... Times.Never)` prove in the duplicate-email test?

---

## KB 11.7 — `ContactDataSeederTests`: seeding guarantees

**WHERE:** `tests/Phonebook.Api.Tests/ContactDataSeederTests.cs`.

Tests (each `Fact`):
1. **Skips when full** — seeding a repo already containing 1000 contacts leaves
   the count at 1000.
2. **Seeds valid unique data without reusing existing** — after seeding with 3
   pre-existing contacts, exactly 997 are added; every seeded name has no
   digits, phones match `^[6-9]\d{9}$`, phones/emails are unique, and existing
   phones/emails (`9702216202`, `tt@example.com`, ...) are never reused.
3. **Deterministic across runs** — two independent fresh repos seed to
   identical data.
4. **JavaRandom sequence** — for seed 4207 the *specific* indices are asserted
   (`33` for FirstNames length 47, `27` for LastNames length 37, `3`/`1` for
   prefixes/digits): the doc-proof the LCG replicates Java.
5. **Matches the Java reference data** — the first three records must be exactly
   ("Rajesh Rao", "9154278943", "rajeshrao0@example.com", "Bandra, Kochi"), etc.
6. **JavaRandom respects bounds** — 100,000 calls stay in range.

**Common confusion:** `ContinueWith` in one test (`.ContinueWith(t =>
t.Result.Where(c => c.Id > 3)...)`) is used to filter only *newly added*
contacts (existing have ids 1–3).

**Check yourself:**
1. Which test proves Java compatibility in observable data?
2. Why is determinism worth testing?

---

# PART 12 — COMPLETE REQUEST FLOWS (every endpoint traced)

## KB 12.1 — `GET /contacts?page=0&size=20` (list)

Full trace through real code:

1. **HTTP**: browser/Postman sends `GET /contacts?page=0&size=20`.
2. **Middleware** (`ErrorHandlingMiddleware.InvokeAsync`): GET → the 415
   content-type check is skipped (`IsUnsupportedRequestBodyContentType` returns
   false). `await _next(context)` forwards.
3. **Routing** (`MapControllers`): matches `GET /contacts` →
   `ContactsController.GetContacts`, binding `page=0`, `size=20`,
   `search=null`, `sort=null` from query.
4. **Controller** (`ContactsController.cs:19-29`): calls
   `_service.GetContactsAsync(0, 20, null, null, token)`.
5. **Service** (`ContactService.cs:20-60`): validates page/size/sort/search;
   `ParseSort(null)` → `ContactSortSpec("createdAt", Descending: true)`;
   calls `_repository.GetPageAsync(...)`.
6. **Repository** (`ContactRepository.cs:20-48`): `AsNoTracking`, no search
   filter, `LongCountAsync` (total), `ApplySort` → `OrderByDescending(CreatedAt)
   .ThenByDescending(Id)`, `Skip(0).Take(20)`, `ToListAsync`. PostgreSQL
   executes. Returns `(20 items, 45 total)`.
7. **Service**: `totalPages = ceil(45/20) = 3`; builds `ContactPageResponse`
   with `First: page == 0 → true`, `Last: 0 >= 2 → false`.
8. **Controller**: `Ok(result)` → 200.
9. **Serialization**: camelCase + snake_case property names →
   `"content"`, `"totalElements"`, `"phone_number"`, `"created_at"`.
10. **Response** back through middleware to client.

**If the DB is down** → EF throws; middleware catch-`Exception` → logs
"Unexpected API error" → 500 `{ "detail": "An unexpected server error occurred." }`.

---

## KB 12.2 — `POST /contacts` (create)

1. **HTTP**: `POST /contacts` with `Content-Type: application/json` and body
   e.g. `{ "name": "Jane", "phone_number": "9876543210", "email": "jane@x.com" }`.
2. **Middleware**: POST → content-type check passes (it IS application/json);
   `_next` forwards.
3. **Model binding + validation** (automatic, `[ApiController]`): JSON →
   `ContactCreateRequest`; `TrimToNull` trims; annotations validate. A problem
   (bad JSON / missing `name` / bad email) → `ErrorResponseFactory` → 400
   `{ "detail": ... }` — **before** the controller runs.
4. **Controller** (`CreateContact`): `await _service.CreateContactAsync(...)`.
5. **Service**: `ValidateBusinessRules` (name digits / 10-digit phone) →
   `EnsureUniqueAsync(request, null)` (checks `ExistsByPhoneNumberAsync`, then
   `ExistsByEmailAsync` if email present) — a duplicate → `DuplicateContactException`
   → middleware → 409.
6. **Service**: `new Contact()`, `Apply` copies fields.
7. **Repository** (`AddAsync`): sets `CreatedAt = DateTime.UtcNow`, `Add`, then
   `SaveChangesTranslatedAsync` — INSERT executes; if the unique index still
   rejects (race) → translated to 409. Returned entity has generated `Id`.
8. **Service**: `ContactResponse.From(contact)` → DTO with `id` + `created_at`.
9. **Controller**: `CreatedAtAction(nameof(GetContact), new { id }, created)` →
   **201** with `Location: /contacts/{id}` and the DTO body.

---

## KB 12.3 — `GET /contacts/{id}` (get one)

1. GET → middleware (no 415 check) → routing binds `{id}` as `int`.
2. Controller → `_service.GetContactAsync(id, token)`.
3. Service: `FindContactAsync` → `id <= 0` → 400 `"must be greater than 0"`;
   else `_repository.GetByIdAsync(id)`.
4. Repository: `FirstOrDefaultAsync(c => c.Id == id)` → `Contact` or `null`.
5. `??` → `ContactNotFoundException` → middleware → **404**
   `{ "detail": "Contact not found." }`.
6. Found → `ContactResponse.From` → `Ok` → **200** with the DTO.

---

## KB 12.4 — `PUT /contacts/{id}` (update)

1. PUT requires `application/json` (415 otherwise, via middleware).
2. Model binding/validation on the `ContactCreateRequest` body (400s from
   `ErrorResponseFactory` if invalid).
3. Controller → `_service.UpdateContactAsync(id, request, token)`.
4. Service: `ValidateBusinessRules` → `FindContactAsync` (404 if missing) →
   `EnsureUniqueAsync(request, id)` (uses the `AndIdNot` checks so the contact
   may keep its own phone/email; a true conflict → 409) → `Apply`.
5. Repository: `_db.Contacts.Update(contact)` then
   `SaveChangesTranslatedAsync` → UPDATE (and again race-proofed 409).
6. `ContactResponse.From` → controller `Ok` → **200** with updated DTO.

---

## KB 12.5 — `DELETE /contacts/{id}` (delete)

1. DELETE → no content-type check → routing binds id.
2. Controller → `_service.DeleteContactAsync(id, token)`.
3. Service: `FindContactAsync` (404 if missing) → `_repository.DeleteAsync(...)`.
4. Repository: `_db.Contacts.Remove(contact)` → `SaveChangesAsync` → DELETE.
5. Controller: `Ok(new { message = "Contact deleted successfully." })` → **200**
   with the message.

---

## KB 12.6 — Error-response contract everywhere

Every failure in this app eventually produces the SAME JSON shape:

```json
{ "detail": "some human-readable message" }
```

- **400** — model validation (`ErrorResponseFactory`), business rules
  (`InvalidContactException` via middleware).
- **404** — `ContactNotFoundException`.
- **409** — `DuplicateContactException` (service pre-check or DB race).
- **415** — non-JSON content type on POST/PUT.
- **500** — anything unexpected (generic message, full details only in logs).

`ErrorResponseBodyContainsOnlyDetail` locks this contract in tests.

---

# PART 13 — REAL DEBUGGING UNDERSTANDING

## KB 13.1 — "If this component breaks, what do I observe?"

### Middleware fails (or is removed)
- No 415s, no clean 404/409/400 mappings. A `ContactNotFoundException` would
  surface as ASP.NET's default 500, or an unhandled blank response — clients see
  wrong statuses and bodies. The 500 catch also disappears, so real exceptions
  might leak as 500s with no logging.

### Controller fails
- Routes stop matching → 404s for `/contacts*`. Methods that die before
  invoking the service return whatever the exception mapping gives (500).
  Bind mismatches (`[FromBody]` vs `[FromQuery]`) surface as 400 model-state
  errors.

### Service fails
- Business-rule 400/409 behavior disappears. If the guard clauses were removed,
  `page=-1` might reach the SQL layer and produce 500s or weird results.
  Duplicate pre-checks removed → only the DB layer catches duplicates (still
  409 via `SaveChangesTranslatedAsync`, but more DB round-trips).

### Repository fails
- "Service is fine but no data ever comes back." If `Skip/Take` were swapped,
  pagination would return wrong pages. If `ILike` were plain `==`, search would
  become case-sensitive and wildcards ignored. Typical symptom: wrong or empty
  query results while endpoints still return 200 with empty/full pages.

### DbContext / EF mapping fails
- Wrong columns (`phone_number` vs `PhoneNumber`), missing `IsUnique` → 409s
  stop working at the DB level; wrong table name → `EnsureCreatedAsync`
  creates nothing useful or queries hit the wrong table. Symptom: exceptions at
  query time (invalid column) or duplicate rows allowed silently.

### Database unavailable (PostgreSQL down)
- Every DB-touching request throws inside EF; middleware catch-`Exception`
  logs "Unexpected API error" and returns **500** with the generic message.
  Startup: seeding would fail in `StartAsync` and (depending on hosting) the app
  may crash during boot.

### Seeder fails
- Table missing (if `EnsureCreatedAsync` also failed) → all queries fail. If
  seeding throws mid-way, contacts may be partially inserted (AddRangeAsync is
  one transaction-like save, so usually all-or-nothing). If `SeedTarget` logic
  were wrong, you'd observe wrong totals in `GET /contacts`.

### Connection-string config wrong
- Wrong host/port/db/user/password → Npgsql throws at first query or at
  startup; symptom = 500s or immediate boot failure, not HTTP errors.

### JSON casing / property-name misconfig
- Frontend sending `phoneNumber` (not `phone_number`) → model binding leaves it
  null → `[Required]` fires → 400 "Phone number is required."

---

# NOT PRESENT IN THIS IMPLEMENTATION

Do not assume these exist — they do not appear anywhere in the code:

- Authentication / authorization / JWT / Identity
- CORS configuration
- Swagger / OpenAPI docs
- EF Core migrations (schema is `EnsureCreatedAsync`)
- Structured logging (Serilog et al.) — built-in `ILogger`
- Caching, rate limiting, health checks
- `IContactService` interface (service is concrete)
- Custom validation attributes (`[Foo]`) beyond DataAnnotations
- Global exception handler via `app.UseExceptionHandler` (they use custom
  middleware instead)
- `UseHttpsRedirection`, HTTPS-only enforcement
- Raw SQL / stored procedures
- Anything not listed in Part 0's table

---

# FINAL SECTION

## COMPLETE MENTAL MODEL

```
Client sends JSON over HTTP
        │
        ▼
Kestrel (port from PORT env var, else appsettings Urls)
        │
        ▼
ErrorHandlingMiddleware ── 415 if POST/PUT not JSON
        │  try { next } catch { → {detail} with 400/404/409/500 }
        ▼
Routing → ContactsController
   GET    /contacts         page,size,search,sort → ContactPageResponse
   POST   /contacts         body → 201 + Location
   GET    /contacts/{id}    → ContactResponse | 404
   PUT    /contacts/{id}    → ContactResponse | 404 | 400 | 409
   DELETE /contacts/{id}    → message | 404
        │
        ▼
ContactService  (business rules: guards, sort parse, 10-digit phone,
                 name-no-digits, uniqueness, page math, DTO mapping)
        │  depends only on the interface
        ▼
IContactRepository
   └─ ContactRepository (EF Core + Npgsql)
        │  AsNoTracking / ILike+escape / Skip.Take / AnyAsync /
        │  unique-violation translation on SaveChanges
        ▼
PhonebookDbContext → contacts table (mapping in OnModelCreating)
        │
        ▼
PostgreSQL (auto-created by EnsureCreatedAsync, seeded by ContactDataSeeder)
```

Startup happens once: read config → build connection string → register
services/DI → register middleware → map controllers → seed DB → listen forever.

## 30-SECOND EXPLANATION

This is an ASP.NET Core (.NET 8) REST API for a phonebook. It exposes five
endpoints on `/contacts` (list with search/sort/pagination, create, get-by-id,
update, delete). Architecture: Controller → Service → Repository → EF Core →
PostgreSQL. The controller only handles HTTP; the service applies business rules
and throws typed exceptions; the repository talks to the database and translates
PostgreSQL unique violations into a 409 duplicate error. One custom middleware
turns exceptions and bad content types into a uniform `{ "detail": ... }` JSON
error with correct status codes. A hosted seeder fills the DB with 1000
deterministic fake contacts at startup. Tests use xUnit: unit tests with Moq,
and integration tests that boot the real app against an in-memory repository.

## 2-MINUTE EXPLANATION

The backend is split strictly into layers. Entry is `Program.cs` (top-level
statements): it builds a connection string from environment variables or
`appsettings.json`, registers MVC controllers with camelCase JSON, overrides the
invalid-model-state response factory so validation errors are returned as
`{ detail }`, registers the EF Core DbContext (PostgreSQL via Npgsql), the
scoped repository and service, and a hosted seeder; finally it adds the error
middleware and maps controllers.

`ContactsController` exposes GET/POST/GET-by-id/PUT/DELETE on `/contacts`. Every
method is thin: it binds parameters/body and delegates to `ContactService`.
`[ApiController]` triggers automatic model validation using DataAnnotations on
`ContactCreateRequest` (required fields, max lengths, email format) plus custom
trim-to-null setters — failures produce 400 before the controller runs.

`ContactService` is where business logic lives: page/size/search/sort
validation, sort parsing into `ContactSortSpec`, strict phone validation
(regex + exactly 10 digits after stripping formatting), name-no-digits rule,
uniqueness pre-checks (phone and optional email, with the "exclude me" variants
for updates), contact lookup-or-404, and mapping between DTOs and the `Contact`
entity.

`IContactRepository` → `ContactRepository` wraps EF Core: read pages use
`AsNoTracking`, case-insensitive `ILike` search with proper escaping of
`% _ \`, `LongCount` for totals, whitelisted `OrderBy` plus an id tiebreaker,
`Any`-based existence checks, and write methods that save via a helper that
catches PostgreSQL unique violations (`SqlState 23505`) and rethrows them as
`DuplicateContactException` — the layer of protection against concurrent
duplicates.

`ErrorHandlingMiddleware` is the single error gate: rejects wrong content types
on POST/PUT (415), and maps the three custom exceptions to 404/409/400, with a
logged generic 500 for anything else — always `{ "detail": "..." }`.

`ContactDataSeeder` (an `IHostedService`) creates the schema with
`EnsureCreatedAsync` and, if fewer than 1000 rows exist, inserts deterministic
fake data generated by a Java-compatible `java.util.Random` reimplementation so
results match the Java reference backend byte-for-byte.

Tests: `ContactServiceTests` (Moq-mocked repository, verifying results and
calls), `ContactsControllerTests` (boots the real app via
`WebApplicationFactory<Program>` with an `InMemoryContactRepository`,
exercising the full HTTP pipeline including middleware and serialization), and
`ContactDataSeederTests` (skip/quota/unique/determinism plus JavaRandom
reference checks).

## MENTOR QUESTIONS

### Basic

1. **Which layer decides whether a phone number has exactly 10 digits?**
   → `ContactService.ValidateBusinessRules` (KB 6.10), not the controller or
   repository.

2. **How does a client know where a newly created contact is?**
   → `CreatedAtAction(nameof(GetContact), new { id = created.Id }, created)`
   (KB 4.3) returns 201 plus a `Location` header pointing at
   `GET /contacts/{id}`.

3. **What JSON error shape does every failure produce?**
   → `{ "detail": "..." }` — from `ErrorResponseFactory` (model validation) or
   `ErrorHandlingMiddleware.WriteErrorAsync` (exceptions) (KB 12.6).

4. **Which DB columns are NOT NULL?**
   → `name`, `phone_number`, `created_at` (KB 8.3: `IsRequired()` on those
   three; also `id` as primary key).

5. **What is the default sort of the list endpoint?**
   → `createdAt` descending (`ContactSortSpec("createdAt", Descending: true)`),
   tie-broken by `id` descending (KB 6.4, KB 7.7).

### Intermediate

6. **The service pre-checks duplicates, so why does the repository also
   translate unique violations?**
   → Race safety: two requests could both pass the pre-check; only the
   database's unique index can definitively reject the second one. The
   repository turns that `23505` into the same `DuplicateContactException`
   (KB 7.8).

7. **Why does update use `ExistsBy...AndIdNotAsync`?**
   → Updating a contact that keeps its own phone/email must not be reported as
   "duplicate" against itself. The `id`/currentId excludes the row being
   updated (KB 6.9).

8. **What role does `partial class Program` play?**
   → It makes the top-level-`Program` type referable, so
   `WebApplicationFactory<Program>` can boot the real app in integration tests
   (KB 2.1, KB 11.4).

9. **Why does the seeder create its own DI scope rather than inject the
   DbContext?**
   → Scoped services like `PhonebookDbContext` are tied to request lifetimes;
   hosted services run outside requests. `IServiceScopeFactory.CreateScope()`
   creates a valid scope (KB 10.2).

10. **Search is case-insensitive and ignores formatting — where is each
    achieved?**
    → Case-insensitive: `EF.Functions.ILike`. Literal-ish matching / escaping:
    `EscapeLike` neutralizes `%`, `_`, `\`. Both in `ContactRepository.GetPageAsync`
    (KB 7.3, KB 7.6).

### Code-level

11. **Trace the exact numeric path of `totalPages` for 45 elements, size 20.**
    → `Math.Ceiling(45 / (double)20) = Math.Ceiling(2.25) = 3`. The `(double)`
    cast prevents integer division (45/20 would be 2) (KB 6.3).

12. **What does `TrimToNull` do and why does blank `"   "` name fail
    validation?**
    → It trims and turns empty into `null`; `null` + `[Required]` = error
    "Name is required." (KB 5.2; verified by
    `ValidatesBlankNameTrimsToRequired`).

13. **What does `IsUnsupportedRequestBodyContentType` do for a GET request?**
    → Returns `false` immediately — it only inspects POST/PUT (KB 9.4). So GET
    is never rejected for content type.

14. **How are `page` and `size` bound in `GetContacts`?**
    → `[FromQuery]` reads them from the URL query string with defaults 0 and 20
    (KB 4.2).

15. **Which method in the repository is intentionally NOT using the
    translated save helper, and why?**
    → `DeleteAsync` uses `SaveChangesAsync`. A DELETE cannot violate a unique
    constraint, so no 23505 translation is needed (KB 7.5).

### Debugging

16. **You get 500 `{ "detail": "An unexpected server error occurred." }` on
    every endpoint. What's the most likely cause?**
    → The database is unreachable (connection string/host/port wrong, or
    PostgreSQL down) so every EF query throws; the middleware's catch-`Exception`
    logs the full error and returns the generic 500. Check the logs, then the
    connection string (KB 13.1).

17. **A POST with `"phone_number": "9876543210"` returns 400 "Phone number is
    required." What is wrong?**
    → The JSON key sent must be `phone_number` (with underscore). If the client
    sends `phoneNumber`, binding leaves `PhoneNumber` null → `[Required]` fails
    (KB 5.2, KB 13.1).

18. **You expected phone/email duplicates to be prevented but two identical
    rows got inserted. Why?**
    → The unique indexes on `phone_number`/`email` are missing or not applied
    (e.g. an existing DB was created before the mapping existed and
    `EnsureCreatedAsync` did not alter it). Without them, the DB never raises
    23505, so neither the service pre-check nor the repository translation
    reliably stops duplicates (KB 8.3, KB 7.8).

19. **Search for `50%` matches everything instead of literally "50%":
    what broke?**
    → The `EscapeLike` escaping is not applied (the raw `%` acts as a wildcard).
    The pattern must be `"%" + EscapeLike(search) + "%"` and the escape char
    passed to `ILike` (KB 7.6).

20. **`GET /contacts` returns pages, but last page comes back slightly wrong
    or with duplicates across pages. What to check?**
    → The sort must be deterministic: `ApplySort` always appends
    `ThenByDescending(c => c.Id)`. If that tiebreaker is removed, equal keys can
    jump between pages (KB 7.7).

---

*End of Backend Knowledge Bytes.*