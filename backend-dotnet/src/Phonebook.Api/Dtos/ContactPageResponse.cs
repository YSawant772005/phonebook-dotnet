namespace Phonebook.Api.Dtos;

public sealed class ContactPageResponse
{
    public List<ContactResponse> Content { get; set; } = new();

    public int Page { get; set; }

    public int Size { get; set; }

    public long TotalElements { get; set; }

    public int TotalPages { get; set; }

    public bool First { get; set; }

    public bool Last { get; set; }
}