using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Phonebook.Api.Models;

namespace Phonebook.Api.Data;

/// <summary>
/// Reimplements the linear congruential generator (LCG) used by
/// <c>java.util.Random(long seed)</c> so that freshly seeded data is
/// byte-for-byte identical to the data produced by the Java reference
/// backend for the same RANDOM_SEED.
/// </summary>
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
        if (bound <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bound), "bound must be positive");
        }

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

/// <summary>
/// Deterministically seeds the contacts table up to 1000 records when it
/// contains fewer than that, mirroring the Java reference backend. The
/// existing table contents are never touched or deleted.
/// </summary>
public sealed class ContactDataSeeder : Microsoft.Extensions.Hosting.IHostedService
{
    private const int SeedTarget = 1000;
    private const long RandomSeed = 4207L;
    private const string PhonePrefixDigits = "6789";

    private static readonly string[] FirstNames =
    {
        "Aarav", "Aditi", "Ajay", "Akshay", "Alok", "Amit", "Ananya", "Anil", "Anjali", "Ankit",
        "Anusha", "Arjun", "Ashok", "Ayesha", "Deepak", "Deepika", "Divya", "Ganesh", "Gaurav", "Harsha",
        "Kavya", "Kiran", "Lakshmi", "Madhav", "Meena", "Meera", "Mohan", "Nandini", "Neha", "Nikhil",
        "Pooja", "Priya", "Rahul", "Rajesh", "Ravi", "Rohit", "Rohan", "Sana", "Sanjay", "Sarita",
        "Shreya", "Suresh", "Sunil", "Swati", "Vikram", "Vinod", "Vishal",
    };

    private static readonly string[] LastNames =
    {
        "Agarwal", "Ahmed", "Bansal", "Bhat", "Bhattacharya", "Choudhary", "Das", "Desai", "Dube",
        "Goyal", "Gupta", "Iyer", "Jain", "Joshi", "Kapoor", "Khan", "Kulkarni", "Kumar", "Malhotra",
        "Mehta", "Mishra", "Naidu", "Nair", "Pandey", "Patel", "Pillai", "Prasad", "Rao", "Reddy",
        "Roy", "Sharma", "Singh", "Sinha", "Srivastava", "Thakur", "Varma", "Verma",
    };

    private static readonly string[] Areas =
    {
        "MG Road", "Indiranagar", "Bandra", "Andheri East", "Jayanagar", "Sadar Bazaar",
        "Koregaon Park", "Aundh", "Viman Nagar", "Sector 17", "Connaught Place", "Salt Lake",
        "Banjara Hills", "Gachibowli", "Anna Nagar", "Velachery", "HSR Layout", "Whitefield",
    };

    private static readonly string[] Cities =
    {
        "Mumbai", "Delhi", "Bengaluru", "Hyderabad", "Chennai", "Kolkata", "Pune", "Ahmedabad",
        "Jaipur", "Lucknow", "Nagpur", "Indore", "Surat", "Coimbatore", "Kochi", "Bhopal",
        "Patna", "Chandigarh",
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContactDataSeeder> _logger;

    public ContactDataSeeder(IServiceScopeFactory scopeFactory, ILogger<ContactDataSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        PhonebookDbContext db = services.GetRequiredService<PhonebookDbContext>();
        IContactRepository repository = services.GetRequiredService<IContactRepository>();

        await db.Database.EnsureCreatedAsync(cancellationToken);
        await SeedAsync(repository, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SeedAsync(IContactRepository repository, CancellationToken cancellationToken)
    {
        long existingCount = await repository.CountAsync(cancellationToken);
        if (existingCount >= SeedTarget)
        {
            _logger.LogInformation("Seed skipped: database already contains {Count} contacts.", existingCount);
            return;
        }

        IReadOnlyList<Contact> existing = await repository.GetAllAsync(cancellationToken);
        var usedPhoneNumbers = new HashSet<string>();
        var usedEmails = new HashSet<string>();

        foreach (Contact contact in existing)
        {
            if (contact.PhoneNumber is not null)
            {
                usedPhoneNumbers.Add(contact.PhoneNumber);
            }

            if (contact.Email is not null)
            {
                usedEmails.Add(contact.Email);
            }
        }

        int toCreate = (int)(SeedTarget - existingCount);
        var random = new JavaRandom(RandomSeed);
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

    private static string NextUniquePhoneNumber(JavaRandom random, HashSet<string> usedPhoneNumbers)
    {
        var builder = new StringBuilder(10);
        string phoneNumber;
        do
        {
            builder.Length = 0;
            builder.Append(PhonePrefixDigits[random.NextInt(PhonePrefixDigits.Length)]);
            for (int i = 0; i < 9; i++)
            {
                builder.Append(random.NextInt(10));
            }

            phoneNumber = builder.ToString();
        }
        while (!usedPhoneNumbers.Add(phoneNumber));

        return phoneNumber;
    }

    private static string NextUniqueEmail(string firstName, string lastName, int index, HashSet<string> usedEmails)
    {
        string lower = (firstName + "." + lastName).ToLowerInvariant();
        string baseName = new string(lower.Where(c => c is >= 'a' and <= 'z').ToArray());
        string email = baseName + index + "@example.com";
        while (!usedEmails.Add(email))
        {
            index++;
            email = baseName + index + "@example.com";
        }

        return email;
    }
}