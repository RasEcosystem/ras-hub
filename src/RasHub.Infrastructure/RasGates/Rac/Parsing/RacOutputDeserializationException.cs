namespace RasHub.Infrastructure.RasGates.Rac.Parsing;

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