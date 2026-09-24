using Phonebook.Api.Data;
using Phonebook.Api.Models;

namespace Phonebook.Api.Tests;

public sealed class InMemoryContactRepository : IContactRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Contact> _contacts = new();
    private int _nextId = 1;

    public void Reset()
    {
        lock (_gate)
        {
            _contacts.Clear();
            _nextId = 1;
        }
    }

    public void Add(Contact contact)
    {
        lock (_gate)
        {
            if (contact.CreatedAt == default)
            {
                contact.CreatedAt = DateTime.UtcNow;
            }

            contact.Id = _nextId++;
            _contacts[contact.Id] = contact;
        }
    }

    public void Seed(IEnumerable<Contact> contacts)
    {
        lock (_gate)
        {
            foreach (Contact contact in contacts)
            {
                contact.Id = _nextId++;
                if (contact.CreatedAt == default)
                {
                    contact.CreatedAt = DateTime.UtcNow;
                }

                _contacts[contact.Id] = contact;
            }
        }
    }

    public Task<(IReadOnlyList<Contact> Items, long TotalElements)> GetPageAsync(
        int page,
        int size,
        ContactSortSpec sort,
        string? search,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            List<Contact> filtered = _contacts.Values.Where(c =>
                    search is null ||
                    ContainsIgnoreCase(c.Name, search) ||
                    ContainsIgnoreCase(c.PhoneNumber, search) ||
                    (c.Email is not null && ContainsIgnoreCase(c.Email, search)) ||
                    (c.Address is not null && ContainsIgnoreCase(c.Address, search)))
                .ToList();

            long total = filtered.Count;

            IOrderedEnumerable<Contact> ordered = Order(filtered, sort);
            List<Contact> items = ordered
                .Skip(page * size)
                .Take(size)
                .Select(c => new Contact
                {
                    Id = c.Id,
                    Name = c.Name,
                    PhoneNumber = c.PhoneNumber,
                    Email = c.Email,
                    Address = c.Address,
                    CreatedAt = c.CreatedAt,
                })
                .ToList();

            return Task.FromResult<(IReadOnlyList<Contact>, long)>((items, total));
        }
    }

    public Task<Contact?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _contacts.TryGetValue(id, out Contact? contact);
            return Task.FromResult(contact);
        }
    }

    public Task<bool> ExistsByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_contacts.Values.Any(c => c.PhoneNumber == phoneNumber));
        }
    }

    public Task<bool> ExistsByPhoneNumberAndIdNotAsync(string phoneNumber, int id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_contacts.Values.Any(c => c.PhoneNumber == phoneNumber && c.Id != id));
        }
    }

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_contacts.Values.Any(c => c.Email == email));
        }
    }

    public Task<bool> ExistsByEmailAndIdNotAsync(string email, int id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_contacts.Values.Any(c => c.Email == email && c.Id != id));
        }
    }

    public Task<Contact> AddAsync(Contact contact, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (contact.CreatedAt == default)
            {
                contact.CreatedAt = DateTime.UtcNow;
            }

            contact.Id = _nextId++;
            _contacts[contact.Id] = contact;
            return Task.FromResult(contact);
        }
    }

    public Task AddRangeAsync(IReadOnlyCollection<Contact> contacts, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (Contact contact in contacts)
            {
                if (contact.CreatedAt == default)
                {
                    contact.CreatedAt = DateTime.UtcNow;
                }

                contact.Id = _nextId++;
                _contacts[contact.Id] = contact;
            }
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(Contact contact, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _contacts[contact.Id] = contact;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Contact contact, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _contacts.Remove(contact.Id);
        }

        return Task.CompletedTask;
    }

    public Task<long> CountAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult((long)_contacts.Count);
        }
    }

    public Task<IReadOnlyList<Contact>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            List<Contact> all = _contacts.Values
                .Select(c => new Contact
                {
                    Id = c.Id,
                    Name = c.Name,
                    PhoneNumber = c.PhoneNumber,
                    Email = c.Email,
                    Address = c.Address,
                    CreatedAt = c.CreatedAt,
                })
                .ToList();
            return Task.FromResult<IReadOnlyList<Contact>>(all);
        }
    }

    private static bool ContainsIgnoreCase(string value, string search)
    {
        return value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static IOrderedEnumerable<Contact> Order(List<Contact> contacts, ContactSortSpec sort)
    {
        Func<Contact, object?> key = sort.Property switch
        {
            "name" => c => c.Name,
            "phoneNumber" => c => c.PhoneNumber,
            "email" => c => c.Email ?? "",
            "createdAt" => c => c.CreatedAt,
            "id" => c => c.Id,
            _ => c => c.CreatedAt,
        };

        IOrderedEnumerable<Contact> ordered = sort.Descending
            ? contacts.OrderByDescending(key)
            : contacts.OrderBy(key);

        return ordered.ThenByDescending(c => c.Id);
    }
}