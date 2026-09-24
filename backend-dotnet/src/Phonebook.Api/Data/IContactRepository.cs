using Phonebook.Api.Models;

namespace Phonebook.Api.Data;

public sealed record ContactSortSpec(string Property, bool Descending);

public interface IContactRepository
{
    Task<(IReadOnlyList<Contact> Items, long TotalElements)> GetPageAsync(
        int page,
        int size,
        ContactSortSpec sort,
        string? search,
        CancellationToken cancellationToken);

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