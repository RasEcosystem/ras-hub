namespace RasHub.Application.RasGates.Exceptions;

public sealed class RasGateClientException : Exception
{
    public RasGateClientException(string message)
        : base(message)
    {
    }

    public RasGateClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}