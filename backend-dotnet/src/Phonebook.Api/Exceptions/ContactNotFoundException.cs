namespace Phonebook.Api.Exceptions;

public sealed class ContactNotFoundException : Exception
{
    public ContactNotFoundException()
        : base("Contact not found.")
    {
    }
}