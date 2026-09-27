namespace Finyte.Data.Imports;

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

public static partial class TransactionFileParser
{
    public static ImportedBalance? ParseBalance(string content)
    {
        var ledger = Regex.Match(content, @"<LEDGERBAL>(.*?)</LEDGERBAL>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!ledger.Success)
        {
            return null;
        }
        var amount = GetOfxValue(ledger.Groups[1].Value, "BALAMT");
        var date = GetOfxValue(ledger.Groups[1].Value, "DTASOF");
        if (amount is null || date is null)
        {
            throw new InvalidDataException("The OFX balance is missing its amount or effective date.");
        }
        var asOf = ParseBalanceDate(date);
        var available = Regex.Match(content, @"<AVAILBAL>(.*?)</AVAILBAL>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        decimal? availableAmount = null;
        if (available.Success && GetOfxValue(available.Groups[1].Value, "BALAMT") is { } value
            && GetOfxValue(available.Groups[1].Value, "DTASOF") is { } availableDate
            && ParseBalanceDate(availableDate) == asOf)
        {
            availableAmount = ParseAmount(value) / 100m;
        }
        return new ImportedBalance(ParseAmount(amount) / 100m, availableAmount, asOf);
    }

    private static DateTimeOffset ParseBalanceDate(string value)
    {
        var match = Regex.Match(value.Trim(), @"^(\d{8})(?:(\d{6})(?:\.(\d{1,7}))?)?(?:\[([+-]?\d+(?:\.\d+)?)(?::[^\]]*)?\])?$");
        if (!match.Success || !DateTime.TryParseExact(match.Groups[1].Value + (match.Groups[2].Success ? match.Groups[2].Value : "000000"),
            "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new InvalidDataException("The OFX balance has an invalid effective date.");
        }
        var offset = match.Groups[4].Success ? decimal.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : 0m;
        if (offset < -14 || offset > 14)
        {
            throw new InvalidDataException("The OFX balance has an invalid timezone.");
        }
        if (match.Groups[3].Success)
        {
            date = date.AddTicks(long.Parse(match.Groups[3].Value.PadRight(7, '0'), CultureInfo.InvariantCulture));
        }
        return new DateTimeOffset(date, TimeSpan.Zero).AddMinutes(-(double)(offset * 60));
    }

    public static string? ParseAccountNumber(string content)
    {
        var account = Regex.Match(content, @"<(?:BANKACCTFROM|CCACCTFROM)>(.*?)(?:</(?:BANKACCTFROM|CCACCTFROM)>|<BANKTRANLIST>)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var value = account.Success ? GetOfxValue(account.Groups[1].Value, "ACCTID") : null;
        return value is { Length: <= 64 } && value.Any(char.IsAsciiDigit) ? value : null;
    }

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
public sealed record ImportedBalance(decimal CurrentBalance, decimal? AvailableBalance, DateTimeOffset AsOf);
