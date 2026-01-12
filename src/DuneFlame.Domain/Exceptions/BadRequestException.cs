namespace DuneFlame.Domain.Exceptions;

/// <summary>
/// Represents a bad request exception that results in a 400 status code.
/// </summary>
public class BadRequestException : Exception
{
    public BadRequestException(string message) : base(message)
    {
    }

    public BadRequestException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}
