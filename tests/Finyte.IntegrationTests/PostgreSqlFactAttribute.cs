using Xunit;

namespace Finyte.IntegrationTests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FINYTE_TEST_POSTGRES")))
        {
            Skip = "Set FINYTE_TEST_POSTGRES to a PostgreSQL test connection string to run this relational test.";
        }
    }
}
