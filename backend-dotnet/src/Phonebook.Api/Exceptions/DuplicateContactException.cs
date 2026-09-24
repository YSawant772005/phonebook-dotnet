namespace Phonebook.Api.Exceptions;

public sealed class DuplicateContactException : Exception
{
    public DuplicateContactException(string message)
        : base(message)
    {
    }
}