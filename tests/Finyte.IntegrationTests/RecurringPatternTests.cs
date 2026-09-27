using Finyte.Core.Recurring;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class RecurringPatternTests
{
    [Theory]
    [InlineData("weekly")]
    [InlineData("fortnightly")]
    [InlineData("quarterly")]
    [InlineData("yearly")]
    public void DetectsConsecutiveOccurrencesForEachSupportedCadence(string cadence)
    {
        var accountId = Guid.NewGuid();
        var anchor = new DateOnly(2023, 1, 31);
        var rows = Enumerable.Range(0, 4).Select(x => new RecurringPatternTransaction(Guid.NewGuid(), accountId, "AUD", -20 - x,
            RecurringCalendar.Add(cadence, anchor, x)!.Value, "Membership", "Membership", null)).ToList();
        var candidate = Assert.Single(RecurringPatternDetector.Detect(rows));
        Assert.Equal(cadence, candidate.Cadence);
        Assert.Equal(4, candidate.TransactionIds.Count);
    }

    [Fact]
    public void MonthlyPriceChangesKeepOneSeriesAndUseLatestObservedAmount()
    {
        var rows = Monthly().Select(x => x with { MerchantName = null, Description = "Direct Debit 123456 POWERCO 998877" }).ToList();
        rows[1] = rows[1] with { Amount = -18 };
        rows[2] = rows[2] with { Amount = -30 };
        var candidate = Assert.Single(RecurringPatternDetector.Detect(rows));
        Assert.Equal("monthly", candidate.Cadence);
        Assert.Equal(30, candidate.ExpectedAmount);
        Assert.Equal(rows.Select(x => x.Id), candidate.TransactionIds);
        Assert.Contains(candidate.Evidence, x => x.Contains("amounts vary"));
        var changedAgain = rows.Select(x => x with { Amount = x.Amount * 2 }).ToList();
        Assert.Equal(candidate.Key, Assert.Single(RecurringPatternDetector.Detect(changedAgain)).Key);
    }

    [Fact]
    public void IrregularDatesWhoseAverageIsMonthlyDoNotMakeAPattern()
    {
        var rows = Monthly();
        rows[1] = rows[1] with { PostedDate = new DateOnly(2026, 1, 10) };
        rows[2] = rows[2] with { PostedDate = new DateOnly(2026, 3, 7) };
        Assert.Empty(RecurringPatternDetector.Detect(rows));
    }

    [Fact]
    public void AccountAndCurrencyAreHardDiscoveryBoundaries()
    {
        var rows = Monthly();
        rows[2] = rows[2] with { AccountId = Guid.NewGuid() };
        Assert.Empty(RecurringPatternDetector.Detect(rows));
        rows[2] = rows[2] with { AccountId = rows[0].AccountId, Currency = "USD" };
        Assert.Empty(RecurringPatternDetector.Detect(rows));
    }

    [Fact]
    public void DifferentBillingPhasesAtTheSameMerchantRemainSeparate()
    {
        var first = Monthly();
        var second = first.Select(x => x with { Id = Guid.NewGuid(), PostedDate = x.PostedDate.AddDays(15), Amount = -40 }).ToList();
        var candidates = RecurringPatternDetector.Detect([.. first, .. second]);
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, x => Assert.Equal("monthly", x.Cadence));
        Assert.Equal(6, candidates.SelectMany(x => x.TransactionIds).Distinct().Count());
        Assert.Contains(candidates, x => x.TransactionIds.ToHashSet().SetEquals(first.Select(y => y.Id)));
        Assert.Contains(candidates, x => x.TransactionIds.ToHashSet().SetEquals(second.Select(y => y.Id)));
    }

    [Fact]
    public void AmbiguousChargesInTheSameWindowAreNotArbitrarilyAssigned()
    {
        var rows = Monthly();
        rows.Add(rows[1] with { Id = Guid.NewGuid(), PostedDate = rows[1].PostedDate.AddDays(1) });
        Assert.Empty(RecurringPatternDetector.Detect(rows));
    }

    [Fact]
    public void OneOffOutsideBillingWindowsDoesNotJoinTheSeries()
    {
        var rows = Monthly();
        var oneOff = rows[0] with { Id = Guid.NewGuid(), PostedDate = new DateOnly(2026, 2, 20), Amount = -90 };
        var candidate = Assert.Single(RecurringPatternDetector.Detect([.. rows, oneOff]));
        Assert.DoesNotContain(oneOff.Id, candidate.TransactionIds);
    }

    [Fact]
    public void DisjointRunsAreSeparateSuggestions()
    {
        var rows = Monthly();
        var later = rows.Select(x => x with { Id = Guid.NewGuid(), PostedDate = x.PostedDate.AddMonths(6) }).ToList();
        var candidates = RecurringPatternDetector.Detect([.. rows, .. later]);
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, x => Assert.Equal(3, x.TransactionIds.Count));
        Assert.Single(candidates.Select(x => x.Key).Distinct());
    }

    [Fact]
    public void ProcessorSuggestionsExplainTheirAmbiguityWithoutPretendingToIdentifyAService()
    {
        var rows = Monthly().Select(x => x with { MerchantName = "APPLE.COM/BILL" }).ToList();
        var candidate = Assert.Single(RecurringPatternDetector.Detect(rows));
        Assert.Contains(candidate.Evidence, x => x.Contains("different services"));
        Assert.True(RecurringPatternDetector.IsGenericAlias(candidate.AliasValue));
    }

    [Fact]
    public void AliasesPreserveIdentityTokensAndFieldBoundaries()
    {
        Assert.Equal("café brisbane 123", RecurringPatternDetector.NormalizeAlias(" CAFÉ Brisbane #123 "));
        var rows = Monthly();
        rows[2] = rows[2] with { MerchantName = null, Description = "Streaming" };
        Assert.Empty(RecurringPatternDetector.Detect(rows));
        rows = rows.Select(x => x with { MerchantName = null, Description = "STREAMING*MEMBERSHIP" }).ToList();
        Assert.Equal("description", Assert.Single(RecurringPatternDetector.Detect(rows)).AliasField);
    }

    [Fact]
    public void ClippedFirstPaymentDoesNotTurnTheThirtiethIntoMonthEnd()
    {
        var rows = Monthly();
        rows[0] = rows[0] with { PostedDate = new DateOnly(2026, 2, 28) };
        rows[1] = rows[1] with { PostedDate = new DateOnly(2026, 3, 30) };
        rows[2] = rows[2] with { PostedDate = new DateOnly(2026, 4, 30) };
        var candidate = Assert.Single(RecurringPatternDetector.Detect(rows));
        Assert.Equal(new DateOnly(2026, 1, 30), candidate.AnchorDate);
        Assert.Equal(new DateOnly(2026, 5, 30), RecurringCalendar.Resolve(candidate.Cadence, candidate.AnchorDate, new DateOnly(2026, 5, 30)).Date);
    }

    [Fact]
    public void TwentyEighthKeepsItsIdentityAcrossFebruary()
    {
        var rows = Monthly().Select((x, y) => x with { PostedDate = new DateOnly(2026, y + 2, 28) }).ToList();
        rows.Add(rows[^1] with { Id = Guid.NewGuid(), PostedDate = new DateOnly(2026, 5, 28) });
        var full = Assert.Single(RecurringPatternDetector.Detect(rows));
        var narrowed = Assert.Single(RecurringPatternDetector.Detect(rows.Skip(1).ToList()));
        Assert.Equal(full.Key, narrowed.Key);
        Assert.Equal(28, RecurringCalendar.Add(full.Cadence, full.AnchorDate, 1)!.Value.Day);
    }

    [Fact]
    public void DiscoveryWindowDoesNotChangeDismissalIdentity()
    {
        var rows = Monthly();
        rows.Add(rows[^1] with { Id = Guid.NewGuid(), PostedDate = new DateOnly(2026, 4, 5) });
        var full = Assert.Single(RecurringPatternDetector.Detect(rows));
        var narrowed = Assert.Single(RecurringPatternDetector.Detect(rows.Skip(1).ToList()));
        Assert.Equal(full.Key, narrowed.Key);
        Assert.NotEqual(full.AnchorDate, narrowed.AnchorDate);
    }

    [Fact]
    public void InputOrderDoesNotChangeCandidatesAndCreditsCannotCreateHistory()
    {
        var rows = Monthly();
        var forward = Assert.Single(RecurringPatternDetector.Detect(rows));
        var reverse = Assert.Single(RecurringPatternDetector.Detect(rows.AsEnumerable().Reverse().ToList()));
        Assert.Equal(forward.Key, reverse.Key);
        Assert.Equal(forward.TransactionIds, reverse.TransactionIds);
        rows[0] = rows[0] with { Amount = 12 };
        Assert.Empty(RecurringPatternDetector.Detect(rows));
    }

    [Fact]
    public void LargeProcessorHistoryDoesNotChooseArbitraryPaymentsFromBusyWindows()
    {
        var accountId = Guid.NewGuid();
        var start = new DateOnly(2022, 1, 1);
        var rows = Enumerable.Range(0, 10000).Select(x => new RecurringPatternTransaction(Guid.NewGuid(), accountId, "AUD", -10 - x % 100,
            start.AddDays(x % 1461), "PAYPAL", "PAYPAL purchase", null)).ToList();
        Assert.Empty(RecurringPatternDetector.Detect(rows));
    }

    private static List<RecurringPatternTransaction> Monthly()
    {
        var accountId = Guid.NewGuid();
        return Enumerable.Range(1, 3).Select(x => new RecurringPatternTransaction(Guid.NewGuid(), accountId, "AUD", -12,
            new DateOnly(2026, x, 5), "Streaming", "Streaming membership", null)).ToList();
    }
}
