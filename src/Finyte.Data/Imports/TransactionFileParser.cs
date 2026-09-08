namespace Finyte.Data.Imports;

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

public static partial class TransactionFileParser
{
    public static IReadOnlyList<ImportedTransaction> Parse(string fileName, string content)
    {
        if (!Path.GetExtension(fileName).Equals(".ofx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Choose an OFX transaction export.");
        }

        if (Regex.Matches(content, "<BANKTRANLIST>", RegexOptions.IgnoreCase).Count > 1)
        {
            throw new InvalidDataException("Export transactions for one account at a time.");
        }

        var transactions = new List<ImportedTransaction>();
        foreach (Match match in OfxTransactionRegex().Matches(content))
        {
            var block = match.Groups[1].Value;
            var dateValue = GetOfxValue(block, "DTPOSTED");
            var amountValue = GetOfxValue(block, "TRNAMT");
            if (dateValue is null || amountValue is null)
            {
                throw new InvalidDataException("An OFX transaction is missing its date or amount.");
            }

            var description = GetOfxValue(block, "NAME") ?? GetOfxValue(block, "MEMO") ?? "Transaction";
            var memo = GetOfxValue(block, "MEMO");
            if (!string.IsNullOrWhiteSpace(memo) && !description.Contains(memo, StringComparison.OrdinalIgnoreCase))
            {
                description = $"{description} {memo}";
            }

            if (description.Trim().Length > 512)
            {
                throw new InvalidDataException("An OFX transaction description exceeds 512 characters.");
            }

            transactions.Add(new ImportedTransaction(
                GetOfxValue(block, "FITID"),
                ParseDate(dateValue),
                description.Trim(),
                ParseAmount(amountValue)));
        }

        if (transactions.Count != Regex.Matches(content, "<STMTTRN>", RegexOptions.IgnoreCase).Count)
        {
            throw new InvalidDataException("The OFX transaction list is incomplete.");
        }

        if (transactions.Count == 0)
        {
            throw new InvalidDataException("No transactions were found in the OFX file.");
        }

        return transactions;
    }

    private static long ParseAmount(string value)
    {
        if (!decimal.TryParse(value.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var amount) || amount <= -10000000000000000m || amount >= 10000000000000000m)
        {
            throw new InvalidDataException("An OFX transaction has an invalid amount.");
        }

        return checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static DateOnly ParseDate(string value)
    {
        var match = Regex.Match(value, @"^\s*(\d{8})");
        if (!match.Success)
        {
            throw new InvalidDataException("An OFX transaction has an invalid date.");
        }

        if (!DateOnly.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new InvalidDataException("An OFX transaction has an invalid date.");
        }

        return date;
    }

    private static string? GetOfxValue(string block, string tag)
    {
        var match = Regex.Match(block, $@"<{tag}>\s*([^<\r\n]+)", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value.Trim()) : null;
    }

    [GeneratedRegex(@"<STMTTRN>(.*?)(?:</STMTTRN>|(?=<STMTTRN>)|(?=</BANKTRANLIST>))", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OfxTransactionRegex();
}

public sealed record ImportedTransaction(string? BankId, DateOnly PostedDate, string Description, long AmountMinorUnits);
