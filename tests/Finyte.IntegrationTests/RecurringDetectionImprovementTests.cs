using System.Globalization;
using Finyte.Core.Recurring;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class RecurringDetectionImprovementTests
{
    private readonly Guid accountId = Guid.NewGuid();

    [Fact]
    public void CardChargesWithChangingValueDatesGroupIntoOneMonthlyCandidate()
    {
        var rows = Enumerable.Range(1, 3).Select(x => Card(new DateOnly(2026, x, 7), -9.99m, "STREAMCO SYDNEY AUS")).ToList();
        var candidate = Assert.Single(RecurringPatternDetector.Detect(rows));
        Assert.Equal("monthly", candidate.Cadence);
        Assert.Equal("STREAMCO SYDNEY AUS", candidate.Name);
        Assert.Equal("streamco sydney aus", candidate.AliasValue);
        Assert.Equal(3, candidate.TransactionIds.Count);
    }

    [Fact]
    public void ForeignAmountsInTheStatementDoNotSplitAGroup()
    {
        var rows = new[] { (1, -7.05m, "5.00"), (2, -7.27m, "5.00"), (3, -14.94m, "10.40") }
            .Select(x => Row(new DateOnly(2026, x.Item1, 2), x.Item2, $"CLOUDHOST SAN FRANCISCO CA USA Card xx1234 USD {x.Item3} Value Date: 30/0{x.Item1}/2026")).ToList();
        Assert.Equal(3, Assert.Single(RecurringPatternDetector.Detect(rows)).TransactionIds.Count);
    }

    [Fact]
    public void BankFeeLinesNeverProduceCandidates()
    {
        var rows = Enumerable.Range(1, 4).Select(x => Row(new DateOnly(2026, x, 3), -0.30m, "International Transaction Fee")).ToList();
        Assert.Empty(RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(IncludeEarly: true)));
    }

    [Fact]
    public void TwoPlansThatBothContinueBecomeTwoCandidates()
    {
        var rows = Enumerable.Range(1, 4).SelectMany(x => new[]
        {
            Card(new DateOnly(2026, x, 5), -20m, "STREAMCO SYDNEY AUS"),
            Card(new DateOnly(2026, x, 5), -100m, "STREAMCO SYDNEY AUS")
        }).ToList();
        var candidates = RecurringPatternDetector.Detect(rows);
        Assert.Equal(2, candidates.Count);
        Assert.Equal(new[] { 20m, 100m }, candidates.Select(x => x.ExpectedAmount).Order());
        Assert.Equal(2, candidates.Select(x => x.Key).Distinct().Count());
    }

    [Fact]
    public void SameDayUpgradeStaysOneSeriesAtTheNewPrice()
    {
        var rows = new List<RecurringPatternTransaction>
        {
            Card(new DateOnly(2026, 1, 5), -20m, "STREAMCO SYDNEY AUS"),
            Card(new DateOnly(2026, 2, 5), -20m, "STREAMCO SYDNEY AUS"),
            Card(new DateOnly(2026, 3, 5), -20m, "STREAMCO SYDNEY AUS"),
            Card(new DateOnly(2026, 3, 5), -80m, "STREAMCO SYDNEY AUS"),
            Card(new DateOnly(2026, 4, 5), -100m, "STREAMCO SYDNEY AUS"),
            Card(new DateOnly(2026, 5, 5), -100m, "STREAMCO SYDNEY AUS")
        };
        var candidate = Assert.Single(RecurringPatternDetector.Detect(rows));
        Assert.Equal(100m, candidate.ExpectedAmount);
        Assert.Equal(5, candidate.TransactionIds.Count);
        Assert.DoesNotContain(rows[2].Id, candidate.TransactionIds);
        Assert.Contains(candidate.Evidence, x => x.Contains("Plan or price changed on 2026-03-05"));
    }

    [Fact]
    public void TwoMonthlyPaymentsProduceAnEarlyCandidateOnlyWhenRequested()
    {
        var rows = new[] { Card(new DateOnly(2026, 7, 7), -9.99m, "RINGCO OULU FIN"), Card(new DateOnly(2026, 8, 7), -9.99m, "RINGCO OULU FIN") };
        Assert.Empty(RecurringPatternDetector.Detect(rows));
        var candidate = Assert.Single(RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(IncludeEarly: true)));
        Assert.True(candidate.IsEarly);
        Assert.False(candidate.IsEnded);
        Assert.Equal("monthly", candidate.Cadence);
        Assert.Equal(new DateOnly(2026, 7, 7), candidate.AnchorDate);
        Assert.Equal(2, candidate.TransactionIds.Count);
    }

    [Fact]
    public void EarlyCandidatesNeedADistinctNameAndSteadyPrice()
    {
        var early = new RecurringDetectionOptions(IncludeEarly: true);
        Assert.Empty(RecurringPatternDetector.Detect([Row(new DateOnly(2026, 7, 7), -9.99m, "PAYPAL"), Row(new DateOnly(2026, 8, 7), -9.99m, "PAYPAL")], early));
        Assert.Empty(RecurringPatternDetector.Detect([Card(new DateOnly(2026, 7, 7), -10m, "RINGCO OULU FIN"), Card(new DateOnly(2026, 8, 7), -11m, "RINGCO OULU FIN")], early));
        Assert.Empty(RecurringPatternDetector.Detect([Card(new DateOnly(2026, 8, 7), -9.99m, "RINGCO OULU FIN"), Card(new DateOnly(2026, 8, 14), -9.99m, "RINGCO OULU FIN")], early));
    }

    [Fact]
    public void TwoSamePricePlansOnDifferentDaysBecomeTwoEarlyCandidates()
    {
        var rows = new[] { (7, 4), (8, 4), (7, 14), (8, 14) }.Select(x => Card(new DateOnly(2026, x.Item1, x.Item2), -2.99m, "STORAGECO BARANGAROO AU")).ToList();
        var candidates = RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(IncludeEarly: true));
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, x => Assert.True(x.IsEarly));
        Assert.Equal(new[] { 4, 14 }, candidates.Select(x => x.AnchorDate.Day).Order());
    }

    [Fact]
    public void StoppedTwoPaymentPatternsAreNotSuggested()
    {
        var rows = new[] { Card(new DateOnly(2026, 1, 7), -9.99m, "RINGCO OULU FIN"), Card(new DateOnly(2026, 2, 7), -9.99m, "RINGCO OULU FIN") };
        Assert.Empty(RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(IncludeEarly: true, AsOf: new DateOnly(2026, 6, 1))));
    }

    [Fact]
    public void BusyMerchantsDoNotProduceCoincidentalSuggestions()
    {
        var rows = new List<RecurringPatternTransaction>();
        foreach (var month in Enumerable.Range(1, 3))
        {
            rows.Add(Card(new DateOnly(2026, month, 5), -12m, "SHOPCO ONLINE AU"));
            rows.AddRange(new[] { 20, 22, 24 }.Select(day => Card(new DateOnly(2026, month, day), -10m - day, "SHOPCO ONLINE AU")));
        }
        Assert.Empty(RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(IncludeEarly: true)));
    }

    [Fact]
    public void VariableWeeklySpendingIsNotASubscriptionButVariableMonthlyBillsAre()
    {
        var weekly = new[] { -40m, -85m, -60m, -120m }.Select((amount, index) => Card(new DateOnly(2026, 1, 5).AddDays(index * 7), amount, "GROCERCO SPRINGFIELD")).ToList();
        Assert.Empty(RecurringPatternDetector.Detect(weekly));
        var monthly = new[] { -80m, -120m, -95m }.Select((amount, index) => Row(new DateOnly(2026, index + 1, 20), amount, "Direct Debit 123456 POWERCO 998877")).ToList();
        Assert.Single(RecurringPatternDetector.Detect(monthly));
    }

    [Fact]
    public void PatternsWithNoRecentPaymentAreMarkedEnded()
    {
        var rows = Enumerable.Range(1, 3).Select(x => Card(new DateOnly(2026, x, 5), -12m, "STREAMCO SYDNEY AUS")).ToList();
        Assert.True(Assert.Single(RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(AsOf: new DateOnly(2026, 6, 1)))).IsEnded);
        Assert.False(Assert.Single(RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(AsOf: new DateOnly(2026, 4, 10)))).IsEnded);
    }

    private RecurringPatternTransaction Card(DateOnly date, decimal amount, string merchant) =>
        Row(date, amount, $"{merchant} Card xx1234 AUD {Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture)} Value Date: {date.AddDays(-2).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}");

    private RecurringPatternTransaction Row(DateOnly date, decimal amount, string text) =>
        new(Guid.NewGuid(), accountId, "AUD", amount, date, text, text, null);
}
