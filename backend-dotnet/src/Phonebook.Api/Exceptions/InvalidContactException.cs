namespace Phonebook.Api.Exceptions;

public sealed class InvalidContactException : Exception
{
    public InvalidContactException(string message)
        : base(message)
    {
    }
}