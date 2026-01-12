namespace DuneFlame.Domain.Exceptions;

/// <summary>
/// Represents a not found exception that results in a 404 status code.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}
