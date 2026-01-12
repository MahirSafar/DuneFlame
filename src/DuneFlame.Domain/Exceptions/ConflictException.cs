namespace DuneFlame.Domain.Exceptions;

/// <summary>
/// Represents a conflict exception that results in a 409 status code.
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }

    public ConflictException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}
