using System.Text.RegularExpressions;
using Phonebook.Api.Data;
using Phonebook.Api.Dtos;
using Phonebook.Api.Exceptions;
using Phonebook.Api.Models;

namespace Phonebook.Api.Services;

public sealed class ContactService
{
    private static readonly Regex PhonePattern = new(@"^\+?[0-9][0-9\s().-]*$", RegexOptions.Compiled);

    private readonly IContactRepository _repository;

    public ContactService(IContactRepository repository)
    {
        _repository = repository;
    }

    public async Task<ContactPageResponse> GetContactsAsync(
        int page,
        int size,
        string? search,
        string? sort,
        CancellationToken cancellationToken)
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

    public async Task<ContactResponse> GetContactAsync(int id, CancellationToken cancellationToken)
    {
        return ContactResponse.From(await FindContactAsync(id, cancellationToken));
    }

    public async Task<ContactResponse> CreateContactAsync(
        ContactCreateRequest request,
        CancellationToken cancellationToken)
    {
        ValidateBusinessRules(request);
        await EnsureUniqueAsync(request, null, cancellationToken);

        var contact = new Contact();
        Apply(request, contact);
        await _repository.AddAsync(contact, cancellationToken);
        return ContactResponse.From(contact);
    }

    public async Task<ContactResponse> UpdateContactAsync(
        int id,
        ContactCreateRequest request,
        CancellationToken cancellationToken)
    {
        ValidateBusinessRules(request);
        Contact contact = await FindContactAsync(id, cancellationToken);
        await EnsureUniqueAsync(request, id, cancellationToken);

        Apply(request, contact);
        await _repository.UpdateAsync(contact, cancellationToken);
        return ContactResponse.From(contact);
    }

    public async Task DeleteContactAsync(int id, CancellationToken cancellationToken)
    {
        await _repository.DeleteAsync(await FindContactAsync(id, cancellationToken), cancellationToken);
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

    private async Task EnsureUniqueAsync(
        ContactCreateRequest request,
        int? currentId,
        CancellationToken cancellationToken)
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

    private static void Apply(ContactCreateRequest request, Contact contact)
    {
        contact.Name = request.Name!;
        contact.PhoneNumber = request.PhoneNumber!;
        contact.Email = request.Email;
        contact.Address = request.Address;
    }
}