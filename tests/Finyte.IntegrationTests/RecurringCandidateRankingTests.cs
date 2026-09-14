using Finyte.Core.Recurring;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class RecurringCandidateRankingTests
{
    [Fact]
    public void RecognizedNameSurvivesPriceChangesButTimingAndPriceCannotEstablishIdentity()
    {
        var series = Series();
        var changedPrice = Row(series, "Streamco", -90);
        var unrelated = Row(series, "Unrelated shop", -20);
        var ranking = RecurringCandidateRanker.Rank(series, [], [changedPrice, unrelated], []);
        Assert.Equal("high", ranking[changedPrice.Id].Confidence);
        Assert.Equal("low", ranking[unrelated.Id].Confidence);
        Assert.Equal(20, series.ExpectedAmount);
    }

    [Fact]
    public void RenamesRequireDistinctiveFieldAwareOverlapAndRemainSuggestions()
    {
        var series = Series();
        var renamed = Row(series, "New Streamco Billing", -24);
        var rank = RecurringCandidateRanker.Rank(series, [], [renamed], [])[renamed.Id];
        Assert.Equal("medium", rank.Confidence);
        Assert.Equal("possible-name-change", rank.MatchKind);
        Assert.Single(series.Aliases);
        var wrongField = renamed with { MerchantName = "Unrelated", Description = "Streamco" };
        Assert.Equal("low", RecurringCandidateRanker.Rank(series, [], [wrongField], [])[wrongField.Id].Confidence);
    }

    [Theory]
    [InlineData("PayPal")]
    [InlineData("Apple Services")]
    [InlineData("Direct debit")]
    public void GenericNamesNeverBecomeStrongEvidence(string name)
    {
        var series = Series(name);
        var row = Row(series, name, -20);
        var rank = RecurringCandidateRanker.Rank(series, [], [row], [])[row.Id];
        Assert.Equal("low", rank.Confidence);
        Assert.Equal("shared-billing-name", rank.MatchKind);
    }

    [Fact]
    public void CompetingPaymentsAndSeriesAreAmbiguousRegardlessOfInputOrder()
    {
        var series = Series();
        var peer = Series();
        peer.TenantId = series.TenantId;
        peer.AccountId = series.AccountId;
        var first = Row(series, "Streamco", -20);
        var second = Row(series, "Streamco", -21) with { PostedDate = new(2026, 9, 7) };
        var ranks = RecurringCandidateRanker.Rank(series, [peer], [first, second], []);
        var reversed = RecurringCandidateRanker.Rank(series, [peer], [second, first], []);
        Assert.Equal("ambiguous", ranks[first.Id].Confidence);
        Assert.Equal(1, ranks[first.Id].CompetingPaymentCount);
        Assert.Equal(peer.Id, Assert.Single(ranks[first.Id].CompetingSeriesIds));
        Assert.Equal(ranks[first.Id].Score, reversed[first.Id].Score);
        Assert.Equal(ranks[first.Id].Confidence, reversed[first.Id].Confidence);
    }

    [Fact]
    public void RejectionsAndReservationsCannotReturnAsStrongSuggestions()
    {
        var series = Series();
        var row = Row(series, "Streamco", -20);
        var decision = Decision(series, row, "rejected");
        Assert.Equal("review-only", RecurringCandidateRanker.Rank(series, [], [row], [decision])[row.Id].MatchKind);
        decision.Status = "reset";
        Assert.Equal("high", RecurringCandidateRanker.Rank(series, [], [row], [decision])[row.Id].Confidence);
        decision.Status = "confirmed";
        decision.TransactionId = Guid.NewGuid(); // The occurrence is occupied, even outside the displayed transactions.
        Assert.Equal(0, RecurringCandidateRanker.Rank(series, [], [row], [decision])[row.Id].Score);
    }

    [Fact]
    public void InactiveForeignAndOutOfWindowEvidenceCannotElevateConfidence()
    {
        var series = Series();
        var row = Row(series, "Streamco", -20);
        var foreign = Series();
        foreign.AccountId = series.AccountId;
        Assert.Empty(RecurringCandidateRanker.Rank(series, [foreign], [row], [])[row.Id].CompetingSeriesIds);
        var late = row with { PostedDate = new(2026, 9, 15) };
        Assert.Equal("low", RecurringCandidateRanker.Rank(series, [], [late], [])[late.Id].Confidence);
        series.State = "paused";
        Assert.Equal(0, RecurringCandidateRanker.Rank(series, [], [row], [])[row.Id].Score);
        series.State = "active";
        series.AnchorDate = new(2026, 10, 5);
        Assert.Equal(0, RecurringCandidateRanker.Rank(series, [], [row], [])[row.Id].Score);
    }

    private static RecurringPaymentSeries Series(string alias = "Streamco") => new()
    {
        TenantId = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Streaming", Currency = "AUD",
        Cadence = "monthly", AnchorDate = new(2026, 9, 5), ExpectedAmount = 20, AmountMode = "fixed",
        Aliases = [new() { Field = "merchant", Value = alias, NormalizedValue = RecurringPatternDetector.NormalizeAlias(alias) }]
    };

    private static RecurringPatternTransaction Row(RecurringPaymentSeries series, string merchant, decimal amount) =>
        new(Guid.NewGuid(), series.AccountId, "AUD", amount, new(2026, 9, 5), merchant, merchant, null);

    private static RecurringPaymentDecision Decision(RecurringPaymentSeries series, RecurringPatternTransaction row, string status) => new()
    {
        TenantId = series.TenantId, SeriesId = series.Id, TransactionId = row.Id, OccurrenceDate = new(2026, 9, 5),
        Status = status, Fingerprint = "evidence", SnapshotJson = "{}"
    };
}
