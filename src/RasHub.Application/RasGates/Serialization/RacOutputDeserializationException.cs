namespace RasHub.Application.RasGates.Serialization;

public sealed class RacOutputDeserializationException : Exception
{
    public RacOutputDeserializationException(string message)
        : base(message)
    {
    }

    public RacOutputDeserializationException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}