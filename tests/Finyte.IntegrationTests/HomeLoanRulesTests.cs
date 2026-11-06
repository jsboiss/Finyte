using Finyte.Core.HomeLoans;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class HomeLoanRulesTests
{
    [Fact]
    public void RulesFileLoadsAndIsConsistent()
    {
        var rules = HomeLoanRules.Current;
        Assert.NotEmpty(rules.Categories);
        Assert.Contains(rules.Categories, x => x.Key == rules.FallbackCategory);
        Assert.All(rules.Categories, x => Assert.Contains(x.Group, HomeLoanCatalog.Groups));
        Assert.All(rules.TagCategories.Values, x => Assert.Contains(rules.Categories, y => y.Key == x));
        Assert.All(rules.Flags, x => Assert.NotEmpty(x.Keywords));
        Assert.All(rules.PaymentTypes, x => Assert.NotEmpty(x.Phrases));
        Assert.True(rules.LargeDeposits.Threshold > 0);
    }

    [Theory]
    [InlineData("Salary GlenTech 12345 31/07/2026", "GlenTech", "salary")]
    [InlineData("Direct Credit 111222 GLENTECH PTY LTD PAY000123", "Glentech", "salary")]
    [InlineData("Direct Credit 333444 MCARE BENEFITS 555666777 ABCD", "Mcare Benefits", "refund")]
    [InlineData("INTEREST", "Interest", "other-income")]
    [InlineData("TRANSFER FROM MUM", "Mum", null)]
    public void PayersAreNamedAndSuggested(string text, string name, string? kind)
    {
        var payer = HomeLoanCatalog.DescribePayer(text, text);
        Assert.Equal((name, kind), (payer.Name, payer.SuggestedKind));
    }

    [Fact]
    public void SamePayerWithDifferentWordingSharesAKey() =>
        Assert.Equal(HomeLoanCatalog.DescribePayer(null, "Salary GlenTech 12345 31/07/2026").Key,
            HomeLoanCatalog.DescribePayer(null, "Direct Credit 111222 GLENTECH PTY LTD PAY000123").Key);
}
