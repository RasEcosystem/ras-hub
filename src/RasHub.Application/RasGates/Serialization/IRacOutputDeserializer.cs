namespace RasHub.Application.RasGates.Serialization;

public interface IRacOutputDeserializer<out TResult>
{
    TResult Deserialize(string standardOutput);
}