namespace RasHub.Application.RasGates.Exceptions;

public class RasGateClientException : Exception
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
