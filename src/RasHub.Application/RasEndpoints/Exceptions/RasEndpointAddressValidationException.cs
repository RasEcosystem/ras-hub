namespace RasHub.Application.RasEndpoints.Exceptions;

public sealed class RasEndpointAddressValidationException : ArgumentException
{
    public RasEndpointAddressValidationException()
        : base("The RAS endpoint address is invalid.")
    {
    }
}
