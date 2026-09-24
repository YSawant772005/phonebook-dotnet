using System.Text.Json.Serialization;
using Phonebook.Api.Models;

namespace Phonebook.Api.Dtos;

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