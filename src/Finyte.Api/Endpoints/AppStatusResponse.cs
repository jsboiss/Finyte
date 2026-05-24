namespace Finyte.Api.Endpoints;

public sealed record AppStatusResponse(
    string Application,
    string Environment,
    bool DatabaseAvailable,
    DateTimeOffset ServerTime);
