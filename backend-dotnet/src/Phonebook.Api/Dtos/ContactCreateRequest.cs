using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Phonebook.Api.Dtos;

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

    [JsonPropertyName("phone_number")]
    [Required(ErrorMessage = "Phone number is required.")]
    public string? PhoneNumber
    {
        get => _phoneNumber;
        set => _phoneNumber = TrimToNull(value);
    }

    [EmailAddress(ErrorMessage = "Invalid email address.")]
    [MaxLength(255, ErrorMessage = "Email cannot exceed 255 characters.")]
    public string? Email
    {
        get => _email;
        set => _email = TrimToNull(value);
    }

    [MaxLength(10000, ErrorMessage = "Address cannot exceed 10000 characters.")]
    public string? Address
    {
        get => _address;
        set => _address = TrimToNull(value);
    }

    private static string? TrimToNull(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}