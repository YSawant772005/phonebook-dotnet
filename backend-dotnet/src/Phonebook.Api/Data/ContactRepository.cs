using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Phonebook.Api.Exceptions;
using Phonebook.Api.Models;

namespace Phonebook.Api.Data;

public sealed class ContactRepository : IContactRepository
{
    private const string LikeEscapeChar = "\\";

    private readonly PhonebookDbContext _db;

    public ContactRepository(PhonebookDbContext db)
    {
        _db = db;
    }

    public async Task<(IReadOnlyList<Contact> Items, long TotalElements)> GetPageAsync(
        int page,
        int size,
        ContactSortSpec sort,
        string? search,
        CancellationToken cancellationToken)
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

    public async Task<long> CountAsync(CancellationToken cancellationToken)
    {
        return await _db.Contacts.LongCountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Contact>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _db.Contacts.AsNoTracking().ToListAsync(cancellationToken);
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }

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
                "phoneNumber" => query.OrderBy(c => c.PhoneNumber),
                "email" => query.OrderBy(c => c.Email),
                "createdAt" => query.OrderBy(c => c.CreatedAt),
                "id" => query.OrderBy(c => c.Id),
                _ => throw new InvalidContactException("Unsupported sort field."),
            };

        return ordered.ThenByDescending(c => c.Id);
    }
}