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
        var rows = new[] { (1, -7.05m, "5.00"), (2, -7.27m, "5.00"), (3, -7.41m, "5.20") }
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

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void AnUpgradeBilledOnADifferentDayStaysOneMonthlySeries(int shift)
    {
        var rows = Enumerable.Range(0, 5).Select(x => Card(new DateOnly(2026, 1, 4).AddMonths(x), -9.99m, "STREAMCO SYDNEY AUS"))
            .Concat(Enumerable.Range(0, 5).Select(x => Card(new DateOnly(2026, 5, 4 + shift).AddMonths(x), -19.99m, "STREAMCO SYDNEY AUS"))).ToList();
        var candidate = Assert.Single(RecurringPatternDetector.Detect(rows));
        Assert.Equal("monthly", candidate.Cadence);
        Assert.Equal(19.99m, candidate.ExpectedAmount);
        Assert.Equal(9, candidate.TransactionIds.Count);
        Assert.Equal(4 + shift, candidate.AnchorDate.Day);
        Assert.Contains(candidate.Evidence, x => x.Contains("Plan or price changed"));
    }

    [Fact]
    public void ALongOldPlanHistoryStillUsesTheNewBillingDay()
    {
        var rows = Enumerable.Range(0, 6).Select(x => Card(new DateOnly(2026, 1, 4).AddMonths(x), -9.99m, "STREAMCO SYDNEY AUS"))
            .Concat(Enumerable.Range(0, 4).Select(x => Card(new DateOnly(2026, 6, 7).AddMonths(x), -19.99m, "STREAMCO SYDNEY AUS"))).ToList();
        var candidate = Assert.Single(RecurringPatternDetector.Detect(rows));
        Assert.Equal("monthly", candidate.Cadence);
        Assert.Equal(9, candidate.TransactionIds.Count);
        Assert.Equal(7, candidate.AnchorDate.Day);
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
        var rows = new[] { Card(new DateOnly(2026, 1, 7), -9.99m, "RINGCO OULU FIN"), Card(new DateOnly(2026, 2, 7), -9.99m, "RINGCO OULU FIN"),
            Card(new DateOnly(2026, 5, 30), -45m, "CORNER CAFE") };
        Assert.Empty(RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(IncludeEarly: true, AsOf: new DateOnly(2026, 6, 1))));
    }

    [Fact]
    public void StalenessIsMeasuredFromTheAccountsLatestImportedCharge()
    {
        var rows = new[] { Card(new DateOnly(2026, 1, 7), -9.99m, "RINGCO OULU FIN"), Card(new DateOnly(2026, 2, 7), -9.99m, "RINGCO OULU FIN") };
        Assert.Single(RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(IncludeEarly: true, AsOf: new DateOnly(2026, 6, 1))));
        var monthly = Enumerable.Range(1, 3).Select(x => Card(new DateOnly(2026, x, 5), -12m, "STREAMCO SYDNEY AUS")).ToList();
        Assert.False(Assert.Single(RecurringPatternDetector.Detect(monthly, new RecurringDetectionOptions(AsOf: new DateOnly(2026, 6, 1)))).IsEnded);
    }

    [Fact]
    public void AStoppedPlanStillCountsTowardItsMerchantsCoverage()
    {
        var rows = new[] { (6, 4), (7, 4), (7, 14), (8, 14) }.Select(x => Card(new DateOnly(2026, x.Item1, x.Item2), -2.99m, "STORAGECO BARANGAROO AU")).ToList();
        rows.Add(Card(new DateOnly(2026, 9, 5), -45m, "CORNER CAFE"));
        var candidate = Assert.Single(RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(IncludeEarly: true)));
        Assert.Equal(new DateOnly(2026, 7, 14), candidate.AnchorDate);
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
    public void MonthlyShoppingWithWidelyVaryingAmountsIsNotASubscription()
    {
        var amounts = new[] { -28.75m, -150m, -62.40m, -91.10m, -45m, -120.35m, -33.20m, -62.40m };
        var rows = amounts.Select((amount, index) => Card(new DateOnly(2026, 1, 12).AddMonths(index), amount, "COLES 0382 SOMEWHERE")).ToList();
        Assert.Empty(RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(IncludeEarly: true)));
    }

    [Fact]
    public void WeeklyTakeawayWithVaryingAmountsIsNotSplitIntoMonthlySubscriptions()
    {
        var amounts = new[] { 32m, 38m, 31m, 32m, 39m, 36m, 34m, 32m, 35m, 33m, 32m, 35m, 36m, 37m, 34m, 34m, 34m, 38m, 31m, 36m, 36m, 33m, 37m, 37m, 37m, 37m,
            38m, 37m, 30m, 35m, 32m, 30m, 37m, 30m, 32m, 31m, 38m, 32m, 34m, 30m, 35m, 33m, 33m, 38m, 35m, 39m, 39m, 36m, 35m, 38m, 33m, 30m };
        var rows = Enumerable.Range(0, 52).Select(x => Card(new DateOnly(2025, 9, 29).AddDays(x * 7), -amounts[x], "FOODCO SPRINGFIELD")).ToList();
        Assert.Empty(RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(IncludeEarly: true)));
    }

    [Fact]
    public void AMonthlyAndAQuarterlyPlanFromOneMerchantAreBothKept()
    {
        var rows = Enumerable.Range(0, 7).Select(x => Card(new DateOnly(2026, 1, 4).AddMonths(x), -9.99m, "STORAGECO BARANGAROO AU"))
            .Concat(Enumerable.Range(0, 3).Select(x => Card(new DateOnly(2026, 1, 24).AddMonths(x * 3), -29.99m, "STORAGECO BARANGAROO AU"))).ToList();
        Assert.Equal(new[] { "monthly", "quarterly" }, RecurringPatternDetector.Detect(rows).Select(x => x.Cadence).Order());
    }

    [Fact]
    public void TwoSamePricePlansOnDifferentDaysAreBothKept()
    {
        var rows = Enumerable.Range(0, 6).SelectMany(x => new[]
        {
            Card(new DateOnly(2026, 1, 4).AddMonths(x), -4.49m, "STORAGECO BARANGAROO AU"),
            Card(new DateOnly(2026, 1, 24).AddMonths(x), -4.49m, "STORAGECO BARANGAROO AU")
        }).ToList();
        Assert.Equal(new[] { 4, 24 }, RecurringPatternDetector.Detect(rows).Select(x => x.AnchorDate.Day).Order());
    }

    [Fact]
    public void AModestPriceRiseOnACardSubscriptionIsStillOneSeries()
    {
        var rows = new[] { -9.99m, -9.99m, -11.99m }.Select((amount, index) => Card(new DateOnly(2026, 1, 7).AddMonths(index), amount, "STREAMCO SYDNEY AUS")).ToList();
        Assert.Equal(11.99m, Assert.Single(RecurringPatternDetector.Detect(rows)).ExpectedAmount);
    }

    [Fact]
    public void AOneOffPurchaseBesideASubscriptionIsNotCalledAPlanChange()
    {
        var rows = Enumerable.Range(0, 9).Select(x => Card(new DateOnly(2026, 1, 6).AddMonths(x), -9.99m, "STREAMCO SYDNEY AUS")).ToList();
        rows.Add(Card(new DateOnly(2026, 7, 6), -50m, "STREAMCO SYDNEY AUS"));
        var candidate = Assert.Single(RecurringPatternDetector.Detect(rows));
        Assert.Equal(9, candidate.TransactionIds.Count);
        Assert.DoesNotContain(candidate.Evidence, x => x.Contains("Plan or price changed"));
        Assert.Contains("A separate charge of 50 AUD on 2026-07-06 is not part of this pattern.", candidate.Evidence);
    }

    [Theory]
    [InlineData(18)]
    [InlineData(19)]
    public void TwoMonthlyPlansAboutTwoWeeksApartAreNotReadAsFortnightly(int secondDay)
    {
        var rows = Enumerable.Range(0, 6).SelectMany(x => new[]
        {
            Card(new DateOnly(2026, 1, 4).AddMonths(x), -4.49m, "STORAGECO BARANGAROO AU"),
            Card(new DateOnly(2026, 1, secondDay).AddMonths(x), -4.49m, "STORAGECO BARANGAROO AU")
        }).ToList();
        var candidates = RecurringPatternDetector.Detect(rows);
        Assert.All(candidates, x => Assert.Equal("monthly", x.Cadence));
        Assert.Equal(new[] { 4, secondDay }, candidates.Select(x => x.AnchorDate.Day).Order());
        Assert.All(candidates, x => Assert.Equal(6, x.TransactionIds.Count));
    }

    [Fact]
    public void ARealFortnightlyChargeStaysFortnightly()
    {
        var rows = Enumerable.Range(0, 12).Select(x => Card(new DateOnly(2026, 1, 5).AddDays(x * 14 + (x % 3 == 0 ? 1 : 0)), -15m, "GYMCO SPRINGFIELD")).ToList();
        var candidate = Assert.Single(RecurringPatternDetector.Detect(rows));
        Assert.Equal("fortnightly", candidate.Cadence);
        Assert.Equal(12, candidate.TransactionIds.Count);
    }

    [Fact]
    public void AMonthlyBillThatLandsADayLateSometimesStaysMonthly()
    {
        var late = new[] { 0, 2, 4, 5, 9, 12, 17, 18, 19, 20 };
        var rows = Enumerable.Range(0, 21).Select(x => Row(new DateOnly(2025, 1, 26).AddMonths(x).AddDays(late.Contains(x) ? 1 : 0), -150m - x % 9, "POWERCO RETAIL")).ToList();
        var candidate = Assert.Single(RecurringPatternDetector.Detect(rows));
        Assert.Equal("monthly", candidate.Cadence);
        Assert.Equal(21, candidate.TransactionIds.Count);
    }

    [Fact]
    public void TwoFortnightlyPlansEightDaysApartStayTwoPlans()
    {
        var rows = Enumerable.Range(0, 6).Select(x => Card(new DateOnly(2026, 1, 5).AddDays(x * 14), -20m, "STORAGECO BARANGAROO AU"))
            .Concat(Enumerable.Range(0, 6).Select(x => Card(new DateOnly(2026, 1, 13).AddDays(x * 14), -24m, "STORAGECO BARANGAROO AU"))).ToList();
        var candidates = RecurringPatternDetector.Detect(rows);
        Assert.All(candidates, x => Assert.Equal("fortnightly", x.Cadence));
        Assert.Equal(new[] { 20m, 24m }, candidates.Select(x => x.ExpectedAmount).Order());
    }

    [Fact]
    public void PatternsWithNoRecentPaymentAreMarkedEnded()
    {
        var rows = Enumerable.Range(1, 3).Select(x => Card(new DateOnly(2026, x, 5), -12m, "STREAMCO SYDNEY AUS")).ToList();
        rows.Add(Card(new DateOnly(2026, 5, 30), -45m, "CORNER CAFE"));
        Assert.True(Assert.Single(RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(AsOf: new DateOnly(2026, 6, 1)))).IsEnded);
        Assert.False(Assert.Single(RecurringPatternDetector.Detect(rows, new RecurringDetectionOptions(AsOf: new DateOnly(2026, 4, 10)))).IsEnded);
    }

    private RecurringPatternTransaction Card(DateOnly date, decimal amount, string merchant) =>
        Row(date, amount, $"{merchant} Card xx1234 AUD {Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture)} Value Date: {date.AddDays(-2).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}");

    private RecurringPatternTransaction Row(DateOnly date, decimal amount, string text) =>
        new(Guid.NewGuid(), accountId, "AUD", amount, date, text, text, null);
}
