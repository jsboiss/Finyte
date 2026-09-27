using Finyte.Core.Recurring;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class StatementNameCleanerTests
{
    [Theory]
    [InlineData("STREAMCO SYDNEY AUS Card xx1234 Value Date: 04/07/2026", "STREAMCO SYDNEY AUS")]
    [InlineData("STREAMCO SYDNEY AUS Card xx1234 AUD 9.99 Value Date: 04/07/2026", "STREAMCO SYDNEY AUS")]
    [InlineData("CLOUDHOST SAN FRANCISCO CA USA Card xx9876 USD 1,006.60 Value Date: 05/08/2026", "CLOUDHOST SAN FRANCISCO CA USA")]
    [InlineData("Direct Debit 123456 POWERCO 998877", "Direct Debit 123456 POWERCO 998877")]
    [InlineData("Transfer to other Bank NetBank Rent", "Transfer to other Bank NetBank Rent")]
    [InlineData("Value Date: 04/07/2026", "Value Date: 04/07/2026")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void RemovesPerChargeStatementDetails(string? input, string expected) =>
        Assert.Equal(expected, StatementNameCleaner.Clean(input));
}
