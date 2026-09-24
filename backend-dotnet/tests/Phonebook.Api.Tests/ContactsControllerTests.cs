using System.Net.Http.Json;
using System.Net;
using System.Text;
using System.Text.Json;
using Phonebook.Api.Models;

namespace Phonebook.Api.Tests;

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

    [Fact]
    public async Task ReturnsPaginatedContacts()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal(45, root.GetProperty("totalElements").GetInt64());
        Assert.Equal(3, root.GetProperty("totalPages").GetInt32());
        Assert.Equal(20, root.GetProperty("size").GetInt32());
        Assert.Equal(0, root.GetProperty("page").GetInt32());
        Assert.True(root.GetProperty("first").GetBoolean());
        Assert.False(root.GetProperty("last").GetBoolean());
        Assert.Equal("Customer 44", root.GetProperty("content")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task CreatesContact()
    {
        string payload = """
            {"name":"Jane Doe","phone_number":"9876543210","email":"jane.doe.new@example.com","address":"Some address"}
            """;

        HttpResponseMessage response = await _client.PostAsync("/contacts", Json(payload));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal("Jane Doe", root.GetProperty("name").GetString());
        Assert.Equal("9876543210", root.GetProperty("phone_number").GetString());
        Assert.Equal("jane.doe.new@example.com", root.GetProperty("email").GetString());
        Assert.Equal("Some address", root.GetProperty("address").GetString());
        Assert.True(root.GetProperty("id").GetInt32() > 0);
        string createdAt = root.GetProperty("created_at").GetString()!;
        Assert.EndsWith("Z", createdAt);
        Assert.True(DateTime.TryParse(createdAt, out _), $"created_at not parseable: {createdAt}");
    }

    [Fact]
    public async Task GetsContactById()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts/1");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("id").GetInt32());
        Assert.Equal("Rahul Sharma", root.GetProperty("name").GetString());
        Assert.Equal("9876500000", root.GetProperty("phone_number").GetString());
        Assert.Equal("rahul.sharma@example.com", root.GetProperty("email").GetString());
        Assert.EndsWith("Z", root.GetProperty("created_at").GetString());
    }

    [Fact]
    public async Task UpdatesContact()
    {
        string payload = """
            {"name":"Jane Updated","phone_number":"9876543210","email":"jane.doe@example.com","address":"Bandra, Mumbai"}
            """;

        HttpResponseMessage response = await _client.PutAsync("/contacts/2", Json(payload));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal("Jane Updated", root.GetProperty("name").GetString());
        Assert.Equal(2, root.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task DeletesContact()
    {
        HttpResponseMessage response = await _client.DeleteAsync("/contacts/3");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(body);
        Assert.Equal("Contact deleted successfully.", document.RootElement.GetProperty("message").GetString());

        HttpResponseMessage nowMissing = await _client.GetAsync("/contacts/3");
        Assert.Equal(HttpStatusCode.NotFound, nowMissing.StatusCode);
    }

    [Fact]
    public async Task ValidatesRequiredName()
    {
        string payload = """{"phone_number":"9876543210","email":"x@example.com"}""";

        HttpResponseMessage response = await _client.PostAsync("/contacts", Json(payload));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Name is required.", ReadDetail(body));
    }

    [Fact]
    public async Task ValidatesBlankNameTrimsToRequired()
    {
        string payload = """{"name":"   ","phone_number":"9876543210"}""";

        HttpResponseMessage response = await _client.PostAsync("/contacts", Json(payload));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Name is required.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ValidatesNameTooLong()
    {
        string payload = $"{{\"name\":\"{new string('a', 256)}\",\"phone_number\":\"9876543210\"}}";

        HttpResponseMessage response = await _client.PostAsync("/contacts", Json(payload));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Name cannot exceed 255 characters.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ValidatesEmailTooLong()
    {
        string email = new string('a', 250) + "@example.com";
        string payload = $"{{\"name\":\"Jane\",\"phone_number\":\"9876543210\",\"email\":\"{email}\"}}";

        HttpResponseMessage response = await _client.PostAsync("/contacts", Json(payload));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Email cannot exceed 255 characters.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ValidatesInvalidEmail()
    {
        string payload = """{"name":"Jane","phone_number":"9876543210","email":"not-an-email"}""";

        HttpResponseMessage response = await _client.PostAsync("/contacts", Json(payload));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid email address.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ValidatesAddressTooLong()
    {
        string address = new string('x', 10001);
        string payload = $"{{\"name\":\"Jane\",\"phone_number\":\"9876543210\",\"address\":\"{address}\"}}";

        HttpResponseMessage response = await _client.PostAsync("/contacts", Json(payload));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Address cannot exceed 10000 characters.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsNameContainingNumbers()
    {
        string payload = """{"name":"Jane2","phone_number":"9876543210"}""";

        HttpResponseMessage response = await _client.PostAsync("/contacts", Json(payload));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Name cannot contain numbers.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsPhoneWithoutTenDigits()
    {
        string payload = """{"name":"Jane","phone_number":"123456789"}""";

        HttpResponseMessage response = await _client.PostAsync("/contacts", Json(payload));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Phone number must contain exactly 10 digits.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsDuplicatePhone()
    {
        string payload = """{"name":"Someone Else","phone_number":"9876500000","email":"unique@example.com"}""";

        HttpResponseMessage response = await _client.PostAsync("/contacts", Json(payload));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Duplicate phone number.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsDuplicateEmail()
    {
        string payload = """{"name":"Someone Else","phone_number":"9876555555","email":"rahul.sharma@example.com"}""";

        HttpResponseMessage response = await _client.PostAsync("/contacts", Json(payload));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Duplicate email.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ReturnsNotFoundForUnknownGet()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts/999999");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Contact not found.", ReadDetail(body));
    }

    [Fact]
    public async Task ReturnsNotFoundForUnknownUpdate()
    {
        string payload = """{"name":"Jane","phone_number":"9876543210"}""";
        HttpResponseMessage response = await _client.PutAsync("/contacts/999999", Json(payload));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Contact not found.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ReturnsNotFoundForUnknownDelete()
    {
        HttpResponseMessage response = await _client.DeleteAsync("/contacts/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Contact not found.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsNonPositiveId()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts/0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("must be greater than 0", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Paginates()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?page=1&size=20");
        string body = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(20, root.GetProperty("content").GetArrayLength());
        Assert.Equal(45, root.GetProperty("totalElements").GetInt64());
        Assert.Equal(3, root.GetProperty("totalPages").GetInt32());
        Assert.False(root.GetProperty("first").GetBoolean());
        Assert.False(root.GetProperty("last").GetBoolean());
    }

    [Fact]
    public async Task PaginatesToLastPage()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?page=2&size=20");
        string body = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal(2, root.GetProperty("page").GetInt32());
        Assert.Equal(5, root.GetProperty("content").GetArrayLength());
        Assert.True(root.GetProperty("last").GetBoolean());
    }

    [Fact]
    public async Task SearchesCaseInsensitivelyAcrossFields()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?search=RAHUL");
        string body = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("totalElements").GetInt64());
        Assert.Equal("Rahul Sharma", root.GetProperty("content")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task SearchesByEmail()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?search=customer4%40example.com");
        string body = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.True(root.GetProperty("totalElements").GetInt64() >= 1,
            $"Expected at least one match; got {root.GetProperty("totalElements").GetInt64()}");
    }

    [Fact]
    public async Task SortsByNameDescending()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?sort=name,desc");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal("Rahul Sharma", root.GetProperty("content")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task SortsByNameAscending()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?sort=name,asc");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        JsonElement content = root.GetProperty("content");
        Assert.Equal("Customer 10", content[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task SortsByCreatedAtDescendingByDefault()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts");
        string body = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal("Customer 44", root.GetProperty("content")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task SortsByIdAscending()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?sort=id,asc");
        string body = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("content")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task SortsByPhoneNumberDescending()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?sort=phoneNumber,desc");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal("Customer 44", root.GetProperty("content")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task RejectsNegativePage()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?page=-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Page must be zero or greater.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsOversizedPageSize()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?size=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Size must be between 1 and 100.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsZeroPageSize()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?size=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Size must be between 1 and 100.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsUnsupportedSortField()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?sort=address,asc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Unsupported sort field.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsInvalidSortDirection()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?sort=name,diagonal");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Sort direction must be asc or desc.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsMalformedSortFormat()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts?sort=name");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Sort must use field,direction format.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsOversizedSearch()
    {
        string search = new string('q', 101);
        HttpResponseMessage response = await _client.GetAsync($"/contacts?search={search}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Search must not exceed 100 characters.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsMalformedJson()
    {
        HttpResponseMessage response = await _client.PostAsync(
            "/contacts",
            new StringContent("{not-json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Request body must contain valid JSON.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsEmptyJsonBody()
    {
        HttpResponseMessage response = await _client.PostAsync(
            "/contacts",
            new StringContent("", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Request body must contain valid JSON.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RejectsUnsupportedContentType()
    {
        HttpResponseMessage response = await _client.PostAsync(
            "/contacts",
            new StringContent("""{"name":"Jane","phone_number":"9876543210"}""", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("Content-Type must be application/json.", ReadDetail(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ErrorResponseBodyContainsOnlyDetail()
    {
        HttpResponseMessage response = await _client.GetAsync("/contacts/999999");
        string body = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        string[] names = root.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "detail" }, names);
        Assert.Equal("Contact not found.", root.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ReturnsNullsForMissingOptionalFields()
    {
        _factory.Repository.Add(new Contact
        {
            Name = "No Email Or Address",
            PhoneNumber = "9800000000",
            Email = null,
            Address = null,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        HttpResponseMessage response = await _client.GetAsync("/contacts/46");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("address").ValueKind);
    }

    private static void SeedDataset(InMemoryContactRepository repository)
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var contacts = new List<Contact>(45);

        for (int i = 0; i < 45; i++)
        {
            string name = i switch
            {
                0 => "Rahul Sharma",
                1 => "Jane Doe",
                _ => $"Customer {i}",
            };
            string email = i switch
            {
                0 => "rahul.sharma@example.com",
                1 => "jane.doe@example.com",
                _ => $"customer{i}@example.com",
            };
            string address = i switch
            {
                0 => "MG Road, Mumbai",
                1 => "Bandra, Mumbai",
                _ => $"Address {i}, Mumbai",
            };

            contacts.Add(new Contact
            {
                Name = name,
                PhoneNumber = "98765000" + i.ToString("D2"),
                Email = email,
                Address = address,
                CreatedAt = baseTime.AddHours(i),
            });
        }

        repository.Seed(contacts);
    }

    private static StringContent Json(string payload)
    {
        return new StringContent(payload, Encoding.UTF8, "application/json");
    }

    private static string ReadDetail(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("detail").GetString()!;
    }
}