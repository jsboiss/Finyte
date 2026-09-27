using Finyte.Core.Accounts;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class MerchantKeywordCatalogTests
{
    [Theory]
    [InlineData("coles 4492 springfield qld", "Groceries", "coles", true)]
    [InlineData("woolworths 1234 sometown", "Groceries", "woolworths", true)]
    [InlineData("sq cafe sometown", "Eating out", "cafe", false)]
    [InlineData("7 eleven 1111 sometown", "Fuel", "7 eleven", true)]
    [InlineData("transfer to other bank netbank rent landlord", "Home", "rent", false)]
    [InlineData("transfer to xx1111 netbank savings", "Transfers", "transfer to", true)]
    public void SuggestsByWholeWordsWithTransfersLast(string merchant, string tag, string keyword, bool atStart)
    {
        var match = MerchantKeywordCatalog.Suggest(merchant);
        Assert.NotNull(match);
        Assert.Equal(tag, match.TagName);
        Assert.Equal(keyword, match.Keyword);
        Assert.Equal(atStart, match.AtStart);
    }

    [Fact]
    public void TheLeadingBrandWinsOverALongerKeywordLaterInTheName()
    {
        var match = MerchantKeywordCatalog.Suggest("ampol woolworths cars sometown");
        Assert.Equal(("Fuel", "ampol", true), (match!.TagName, match.Keyword, match.AtStart));
        Assert.Equal("Eating out", MerchantKeywordCatalog.Suggest("dd doordash coles melbourne")!.TagName);
    }

    [Fact]
    public void GenericPhrasesNeverCoverEveryMerchantThatStartsWithThem()
    {
        Assert.True(MerchantKeywordCatalog.Suggest("transfer to xx1111 commbank app jamie")!.Generic);
        Assert.False(MerchantKeywordCatalog.Suggest("coles 4492 springfield qld")!.Generic);
    }

    [Theory]
    [InlineData("colesworth trading")]
    [InlineData("parental leave payment")]
    [InlineData("")]
    public void DoesNotMatchInsideWords(string merchant) => Assert.Null(MerchantKeywordCatalog.Suggest(merchant));

    [Theory]
    [InlineData("FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES", "Groceries")]
    [InlineData("FOOD_AND_DRINK", "FOOD_AND_DRINK_OTHER_FOOD_AND_DRINK", "Eating out")]
    [InlineData("RENT_AND_UTILITIES", "RENT_AND_UTILITIES_RENT", "Home")]
    [InlineData("RENT_AND_UTILITIES", "RENT_AND_UTILITIES_GAS_AND_ELECTRICITY", "Bills and utilities")]
    [InlineData("SERVICES", "SERVICES_INSURANCE", "Bills and utilities")]
    [InlineData("MERCHANDISE", "MERCHANDISE_ONLINE_MARKETPLACES", "Shopping")]
    [InlineData("TRANSPORTATION", "TRANSPORTATION_GAS", "Fuel")]
    [InlineData("TRANSPORTATION", "TRANSPORTATION_PUBLIC_TRANSIT", "Transport")]
    [InlineData("ENTERTAINMENT", null, "Entertainment")]
    [InlineData("INCOME", "INCOME_SALARY", null)]
    [InlineData(null, null, null)]
    public void MapsBankCategoriesToStarterTags(string? primary, string? secondary, string? expected) =>
        Assert.Equal(expected, MerchantKeywordCatalog.CategoryTag(primary, secondary));

    [Fact]
    public void StarterTagsAreTwelveDistinctNamesAndEveryKeywordTargetsOne()
    {
        Assert.Equal(12, MerchantKeywordCatalog.StarterTags.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(MerchantKeywordCatalog.KeywordTags, x => Assert.Contains(MerchantKeywordCatalog.StarterTags, y => y.Name == x));
    }
}
