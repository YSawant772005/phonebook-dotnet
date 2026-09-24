using Moq;
using Phonebook.Api.Data;
using Phonebook.Api.Dtos;
using Phonebook.Api.Exceptions;
using Phonebook.Api.Models;
using Phonebook.Api.Services;

namespace Phonebook.Api.Tests;

public sealed class ContactServiceTests
{
    private readonly Mock<IContactRepository> _repository = new();
    private readonly ContactService _service;

    public ContactServiceTests()
    {
        _service = new ContactService(_repository.Object);
    }

    [Fact]
    public async Task SearchesInDatabaseAndMapsPageMetadata()
    {
        var contact = Contact(1, "Jane Doe", "9876543210");

        _repository
            .Setup(r => r.GetPageAsync(0, 20, SortSpec("createdAt", true), "jane", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { contact }, 21L));

        ContactPageResponse result = await _service.GetContactsAsync(0, 20, " jane ", null, CancellationToken.None);

        Assert.Single(result.Content);
        Assert.Equal(21L, result.TotalElements);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(0, result.Page);
        Assert.Equal(20, result.Size);

        _repository.Verify(
            r => r.GetPageAsync(0, 20, SortSpec("createdAt", true), "jane", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExcludesCurrentContactWhenCheckingDuplicatePhoneOnUpdate()
    {
        var existing = Contact(7, "Existing", "9876543210");
        var request = Request("Renamed", "9876543210", null);

        _repository.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _repository.Setup(r => r.ExistsByPhoneNumberAndIdNotAsync("9876543210", 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repository.Setup(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _service.UpdateContactAsync(7, request, CancellationToken.None);

        _repository.Verify(
            r => r.ExistsByPhoneNumberAndIdNotAsync("9876543210", 7, It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(
            r => r.ExistsByPhoneNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RejectsDuplicateEmailBeforeSaving()
    {
        var request = Request("Jane", "9876543210", "jane@example.com");

        _repository.Setup(r => r.ExistsByPhoneNumberAsync("9876543210", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repository.Setup(r => r.ExistsByEmailAsync("jane@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<DuplicateContactException>(
            () => _service.CreateContactAsync(request, CancellationToken.None));

        _repository.Verify(r => r.AddAsync(It.IsAny<Contact>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SortsByNameAscending()
    {
        _repository
            .Setup(r => r.GetPageAsync(0, 20, It.IsAny<ContactSortSpec>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<Contact>(), 0L));

        await _service.GetContactsAsync(0, 20, null, "name,asc", CancellationToken.None);

        _repository.Verify(r => r.GetPageAsync(0, 20, SortSpec("name", false), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SortsByNameDescending()
    {
        _repository
            .Setup(r => r.GetPageAsync(0, 20, It.IsAny<ContactSortSpec>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<Contact>(), 0L));

        await _service.GetContactsAsync(0, 20, null, "name,desc", CancellationToken.None);

        _repository.Verify(r => r.GetPageAsync(0, 20, SortSpec("name", true), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CombinesSearchSortAndPagination()
    {
        var contact = Contact(1, "Rahul Sharma", "9876500001");

        _repository
            .Setup(r => r.GetPageAsync(2, 20, SortSpec("name", false), "rahul", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { contact }, 41L));

        ContactPageResponse result = await _service.GetContactsAsync(2, 20, " rahul ", "name,asc", CancellationToken.None);

        Assert.Single(result.Content);
        Assert.Equal(41L, result.TotalElements);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(2, result.Page);
    }

    [Fact]
    public async Task RejectsNegativePage()
    {
        await Assert.ThrowsAsync<InvalidContactException>(
            () => _service.GetContactsAsync(-1, 20, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsOversizedPageSize()
    {
        await Assert.ThrowsAsync<InvalidContactException>(
            () => _service.GetContactsAsync(0, 101, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsZeroPageSize()
    {
        await Assert.ThrowsAsync<InvalidContactException>(
            () => _service.GetContactsAsync(0, 0, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsUnsupportedSortField()
    {
        await Assert.ThrowsAsync<InvalidContactException>(
            () => _service.GetContactsAsync(0, 20, null, "address,asc", CancellationToken.None));
    }

    [Fact]
    public async Task RejectsInvalidSortDirection()
    {
        await Assert.ThrowsAsync<InvalidContactException>(
            () => _service.GetContactsAsync(0, 20, null, "name,diagonal", CancellationToken.None));
    }

    [Fact]
    public async Task RejectsSortWithoutComma()
    {
        await Assert.ThrowsAsync<InvalidContactException>(
            () => _service.GetContactsAsync(0, 20, null, "name", CancellationToken.None));
    }

    [Fact]
    public async Task RejectsOversizedSearch()
    {
        string search = new string('a', 101);
        await Assert.ThrowsAsync<InvalidContactException>(
            () => _service.GetContactsAsync(0, 20, search, null, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsNameContainingNumbers()
    {
        var request = Request("Jane2", "9876543210", null);
        await Assert.ThrowsAsync<InvalidContactException>(
            () => _service.CreateContactAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsPhoneWithoutExactlyTenDigits()
    {
        var request = Request("Jane", "123456789", null);
        await Assert.ThrowsAsync<InvalidContactException>(
            () => _service.CreateContactAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreatesContactTrimmingInput()
    {
        var request = new ContactCreateRequest
        {
            Name = "  Jane Doe  ",
            PhoneNumber = " 9876543210 ",
            Email = " jane@example.com ",
            Address = "  Bandra, Mumbai  ",
        };

        _repository.Setup(r => r.ExistsByPhoneNumberAsync("9876543210", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repository.Setup(r => r.ExistsByEmailAsync("jane@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Contact cap = null!;
        _repository
            .Setup(r => r.AddAsync(It.Is<Contact>(c => true), It.IsAny<CancellationToken>()))
            .Callback<Contact, CancellationToken>((c, _) => cap = c)
            .ReturnsAsync((Contact c, CancellationToken _) => c);

        ContactResponse result = await _service.CreateContactAsync(request, CancellationToken.None);

        Assert.Equal("Jane Doe", cap.Name);
        Assert.Equal("9876543210", cap.PhoneNumber);
        Assert.Equal("jane@example.com", cap.Email);
        Assert.Equal("Bandra, Mumbai", cap.Address);
        Assert.Equal("Jane Doe", result.Name);
    }

    [Fact]
    public async Task GetContactReturnsContact()
    {
        var existing = Contact(3, "Jane Doe", "9876543210");
        _repository.Setup(r => r.GetByIdAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        ContactResponse result = await _service.GetContactAsync(3, CancellationToken.None);

        Assert.Equal(3, result.Id);
        Assert.Equal("Jane Doe", result.Name);
    }

    [Fact]
    public async Task GetContactThrowsWhenNotFound()
    {
        _repository.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((Contact?)null);

        await Assert.ThrowsAsync<ContactNotFoundException>(
            () => _service.GetContactAsync(999, CancellationToken.None));
    }

    [Fact]
    public async Task GetContactRejectsNonPositiveId()
    {
        await Assert.ThrowsAsync<InvalidContactException>(
            () => _service.GetContactAsync(0, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateContactThrowsWhenNotFound()
    {
        var request = Request("Renamed", "9876543210", null);
        _repository.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((Contact?)null);

        await Assert.ThrowsAsync<ContactNotFoundException>(
            () => _service.UpdateContactAsync(999, request, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteContactDeletes()
    {
        var existing = Contact(5, "Jane", "9876543210");
        _repository.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _repository.Setup(r => r.DeleteAsync(existing, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _service.DeleteContactAsync(5, CancellationToken.None);

        _repository.Verify(r => r.DeleteAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteContactThrowsWhenNotFound()
    {
        _repository.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((Contact?)null);

        await Assert.ThrowsAsync<ContactNotFoundException>(
            () => _service.DeleteContactAsync(999, CancellationToken.None));

        _repository.Verify(r => r.DeleteAsync(It.IsAny<Contact>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateContactRejectsDuplicateEmailExceptItsOwn()
    {
        var existing = Contact(7, "Existing", "9876543210");
        var request = Request("Renamed", "9876554321", "jane@example.com");

        _repository.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _repository.Setup(r => r.ExistsByPhoneNumberAndIdNotAsync("9876554321", 7, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repository.Setup(r => r.ExistsByEmailAndIdNotAsync("jane@example.com", 7, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<DuplicateContactException>(
            () => _service.UpdateContactAsync(7, request, CancellationToken.None));
    }

    private static ContactSortSpec SortSpec(string property, bool descending)
    {
        return It.Is<ContactSortSpec>(s => s.Property == property && s.Descending == descending);
    }

    private static ContactCreateRequest Request(string name, string phone, string? email)
    {
        return new ContactCreateRequest { Name = name, PhoneNumber = phone, Email = email };
    }

    private static Contact Contact(int id, string name, string phone)
    {
        return new Contact
        {
            Id = id,
            Name = name,
            PhoneNumber = phone,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }
}