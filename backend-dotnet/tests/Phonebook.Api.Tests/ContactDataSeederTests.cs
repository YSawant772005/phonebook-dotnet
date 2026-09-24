using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Phonebook.Api.Data;
using Phonebook.Api.Models;

namespace Phonebook.Api.Tests;

public sealed class ContactDataSeederTests
{
    [Fact]
    public async Task SkipsSeedingWhenDatabaseAlreadyHasEnoughContacts()
    {
        var repository = new InMemoryContactRepository();
        var existing = new List<Contact>(1000);
        for (int i = 0; i < 1000; i++)
        {
            existing.Add(new Contact
            {
                Name = "Existing " + i,
                PhoneNumber = (9000000000 + i).ToString(),
                Email = "existing" + i + "@example.com",
                Address = "Address " + i,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
        }

        repository.Seed(existing);

        ContactDataSeeder seeder = CreateSeeder();
        await seeder.SeedAsync(repository, CancellationToken.None);

        Assert.Equal(1000, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SeedsValidUniqueContactsUpToTargetWithoutReusingExistingData()
    {
        var repository = new InMemoryContactRepository();
        repository.Seed(new[]
        {
            new Contact { Name = "Yash Ramesh Sawant", PhoneNumber = "9702216202", Email = "tt@example.com", CreatedAt = DateTime.UtcNow },
            new Contact { Name = "zd", PhoneNumber = "0970221620", Email = null, CreatedAt = DateTime.UtcNow },
            new Contact { Name = "Yash Ramesh Sawant", PhoneNumber = "9702216202", Email = "other@example.com", CreatedAt = DateTime.UtcNow },
        });

        ContactDataSeeder seeder = CreateSeeder();
        await seeder.SeedAsync(repository, CancellationToken.None);

        IReadOnlyList<Contact> seeded = await repository
            .GetAllAsync(CancellationToken.None)
            .ContinueWith(t => t.Result.Where(c => c.Id > 3).ToList(), CancellationToken.None);

        Assert.Equal(997, seeded.Count);

        var phoneNumbers = new HashSet<string>();
        var emails = new HashSet<string>();
        foreach (Contact contact in seeded)
        {
            Assert.False(string.IsNullOrEmpty(contact.Name));
            Assert.False(contact.Name.Any(char.IsDigit), $"Seeded name must not contain digits: {contact.Name}");
            Assert.Matches(@"^[6-9]\d{9}$", contact.PhoneNumber);
            Assert.True(phoneNumbers.Add(contact.PhoneNumber), $"Duplicate phone: {contact.PhoneNumber}");
            Assert.False(string.IsNullOrEmpty(contact.Email));
            Assert.True(emails.Add(contact.Email!), $"Duplicate email: {contact.Email}");
            Assert.False(string.IsNullOrEmpty(contact.Address));
            Assert.Contains(", ", contact.Address!);
        }

        Assert.Equal(997, phoneNumbers.Count);
        Assert.Equal(997, emails.Count);
        Assert.DoesNotContain("9702216202", phoneNumbers);
        Assert.DoesNotContain("0970221620", phoneNumbers);
        Assert.DoesNotContain("tt@example.com", emails);
        Assert.DoesNotContain("other@example.com", emails);
    }

    [Fact]
    public async Task SeedsAreDeterministicAcrossRuns()
    {
        InMemoryContactRepository first = new();
        InMemoryContactRepository second = new();

        await CreateSeeder().SeedAsync(first, CancellationToken.None);
        await CreateSeeder().SeedAsync(second, CancellationToken.None);

        IReadOnlyList<Contact> a = (await first.GetAllAsync(CancellationToken.None)).OrderBy(c => c.Id).ToList();
        IReadOnlyList<Contact> b = (await second.GetAllAsync(CancellationToken.None)).OrderBy(c => c.Id).ToList();

        Assert.Equal(1000, a.Count);
        Assert.Equal(1000, b.Count);

        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Name, b[i].Name);
            Assert.Equal(a[i].PhoneNumber, b[i].PhoneNumber);
            Assert.Equal(a[i].Email, b[i].Email);
            Assert.Equal(a[i].Address, b[i].Address);
        }
    }

    [Fact]
    public void JavaRandomProducesTheReferenceSequenceForSeed4207()
    {
        var random = new JavaRandom(4207);

        int firstNameIndex = random.NextInt(47);
        Assert.Equal(33, firstNameIndex);

        int lastNameIndex = random.NextInt(37);
        Assert.Equal(27, lastNameIndex);

        int prefix = random.NextInt(4);
        Assert.Equal(3, prefix);

        int digit = random.NextInt(10);
        Assert.Equal(1, digit);
    }

    [Fact]
    public async Task SeedsMatchTheJavaReferenceData()
    {
        var repository = new InMemoryContactRepository();
        await CreateSeeder().SeedAsync(repository, CancellationToken.None);

        IReadOnlyList<Contact> all = await repository.GetAllAsync(CancellationToken.None);
        List<Contact> ordered = all.OrderBy(c => c.Id).Take(3).ToList();

        string[] expectedNames = { "Rajesh Rao", "Rohit Singh", "Meera Bhat" };
        string[] expectedPhones = { "9154278943", "8727370881", "8781533007" };
        string[] expectedEmails = { "rajeshrao0@example.com", "rohitsingh1@example.com", "meerabhat2@example.com" };
        string[] expectedAddresses = { "Bandra, Kochi", "Banjara Hills, Delhi", "Aundh, Ahmedabad" };

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(expectedNames[i], ordered[i].Name);
            Assert.Equal(expectedPhones[i], ordered[i].PhoneNumber);
            Assert.Equal(expectedEmails[i], ordered[i].Email);
            Assert.Equal(expectedAddresses[i], ordered[i].Address);
        }
    }

    [Fact]
    public void JavaRandomRespectsBounds()
    {
        var random = new JavaRandom(12345);
        for (int i = 0; i < 100_000; i++)
        {
            int value = random.NextInt(10);
            Assert.InRange(value, 0, 9);

            int prefix = random.NextInt(4);
            Assert.InRange(prefix, 0, 3);
        }
    }

    private static ContactDataSeeder CreateSeeder()
    {
        return new ContactDataSeeder(Mock.Of<IServiceScopeFactory>(), NullLogger<ContactDataSeeder>.Instance);
    }
}