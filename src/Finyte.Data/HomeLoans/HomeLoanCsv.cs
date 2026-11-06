using System.Globalization;
using System.Text;

namespace Finyte.Data.HomeLoans;

public static class HomeLoanCsv
{
    private const string Notice = "Prepared from your imported transactions as a preparation aid. Lenders will still ask for your official bank statements.";

    public static string Summary(HomeLoanReport report)
    {
        var csv = new StringBuilder();
        var first = report.Setup.FirstApplicant;
        var second = report.Setup.SecondApplicant;
        Line(csv, "Home loan summary");
        Line(csv, "Period", $"{report.FromMonth} to {report.ToMonth}", "Months", Number(report.Months), "Currency", report.Currency);
        Line(csv, "Applicants", second is null ? first : $"{first} and {second}");
        Line(csv, Notice);
        csv.Append("\r\n");

        var header = new List<string> { "Month", $"Income: {first}" };
        if (second is not null)
        {
            header.Add($"Income: {second}");
        }
        header.AddRange(["Other income", "Credits not yet labelled", "Refunds", "Gifts", "Living expenses", "Commitments", "Saved", "Surplus"]);
        Line(csv, [.. header]);
        foreach (var month in report.MonthlyTotals.Append(report.Average).Append(report.Total))
        {
            var values = new List<string> { month.Month, Money(month.FirstApplicantIncome) };
            if (second is not null)
            {
                values.Add(Money(month.SecondApplicantIncome));
            }
            values.AddRange([Money(month.OtherIncome), Money(month.UnlabelledCredits), Money(month.Refunds), Money(month.Gifts),
                Money(month.LivingExpenses), Money(month.Commitments), Money(month.Saved), Money(month.Surplus)]);
            Line(csv, [.. values]);
        }
        csv.Append("\r\n");

        Line(csv, ["Expenses by lender category", "Type", "Monthly average", "Total", .. report.MonthlyTotals.Select(x => x.Month)]);
        foreach (var category in report.Categories)
        {
            Line(csv, [category.Label, GroupLabel(category.Group), Money(category.MonthlyAverage), Money(category.Total), .. category.ByMonth.Select(Money)]);
        }
        csv.Append("\r\n");

        Line(csv, "Income sources", "Labelled as", "Applicant", "Payments", "Months seen", "Total");
        foreach (var payer in report.Payers)
        {
            Line(csv, payer.Name, KindLabel(payer.Kind), payer.Applicant == 2 ? second ?? "" : payer.Applicant == 1 ? first : "",
                Number(payer.Count), Number(payer.MonthsSeen), Money(payer.Total));
        }
        csv.Append("\r\n");

        Line(csv, "Things a lender may ask about", "Payments", "Total");
        if (report.Flags.Count == 0)
        {
            Line(csv, "None found");
        }
        foreach (var flag in report.Flags)
        {
            Line(csv, flag.Label, Number(flag.Count), Money(flag.Total));
        }
        if (report.Gaps.Count > 0)
        {
            csv.Append("\r\n");
            Line(csv, "Months with no transactions", "Months");
            foreach (var gap in report.Gaps)
            {
                Line(csv, gap.AccountName, string.Join(" ", gap.MissingMonths));
            }
        }
        return csv.ToString();
    }

    public static string Transactions(HomeLoanReport report, IReadOnlyList<HomeLoanRow> rows)
    {
        var csv = new StringBuilder();
        Line(csv, "Date", "Account", "Account role", "Description", "Amount", "Type", "Applicant", "Lender category", "Tags", "Lender may ask about");
        foreach (var row in rows)
        {
            Line(csv, row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.Account, row.Role, row.Description, Money(row.Amount),
                row.Type, row.Applicant ?? "", row.Category ?? "", row.Tags, row.Flag ?? "");
        }
        return csv.ToString();
    }

    private static string KindLabel(string? kind) => kind switch
    {
        "salary" => "Salary",
        "other-income" => "Other income",
        "refund" => "Refund",
        "gift" => "Gift",
        "not-income" => "Not income",
        _ => "Not yet labelled"
    };

    private static string GroupLabel(string group) => group switch
    {
        "commitment" => "Commitment",
        "excluded" => "Not counted",
        "savings" => "Saved",
        _ => "Living expense"
    };

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Line(StringBuilder csv, params string[] values)
    {
        csv.AppendJoin(',', values.Select(Cell)).Append("\r\n");
    }

    private static string Cell(string value)
    {
        var text = value.Length > 0 && "=+-@\t\r".Contains(value[0]) && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
            ? $"'{value}" : value;
        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }
}
