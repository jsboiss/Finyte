using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Finyte.Data.ProviderSync;

public sealed class FiskilOptions
{
    public const string SectionName = "Fiskil";

    public string BaseUrl { get; set; } = "https://api.fiskil.com";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
}

public interface IFiskilBankingClient
{
    Task<IReadOnlyCollection<FiskilAccountData>> GetAccounts(string endUserId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FiskilBalanceData>> GetBalances(string endUserId, string? accountId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FiskilTransactionData>> GetTransactions(string endUserId, string? accountId, CancellationToken cancellationToken);
}

public sealed record FiskilAccountData(
    string Id,
    string? AccountNumber,
    string? Bsb,
    string? Name,
    string? ProductName,
    string? ProductCategory,
    string? InstitutionId,
    string? ConsentId,
    bool? IsOwned,
    string? OpenStatus,
    DateOnly? CreationDate,
    string RawJson);

public sealed record FiskilBalanceData(
    string AccountId,
    decimal CurrentBalance,
    decimal? AvailableBalance,
    decimal? CreditLimit,
    string? Currency,
    DateTimeOffset? AsOf,
    string RawJson);

public sealed record FiskilTransactionData(
    string Id,
    string AccountId,
    decimal Amount,
    string? Currency,
    string? Description,
    string? Status,
    DateTimeOffset? PostedAt,
    DateTimeOffset? ExecutedAt,
    string? PrimaryCategory,
    string? SecondaryCategory,
    string? MerchantName,
    string? Reference,
    string RawJson);

public sealed class FiskilBankingClient(
    HttpClient httpClient,
    IOptions<FiskilOptions> options,
    IFiskilAccessTokenProvider accessTokenProvider) : IFiskilBankingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyCollection<FiskilAccountData>> GetAccounts(string endUserId, CancellationToken cancellationToken)
    {
        return await GetPaged(
            "/v1/banking/accounts",
            [new QueryParam("end_user_id", endUserId)],
            "accounts",
            ToAccount,
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<FiskilBalanceData>> GetBalances(string endUserId, string? accountId, CancellationToken cancellationToken)
    {
        var queryParams = new List<QueryParam> { new("end_user_id", endUserId) };
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            queryParams.Add(new QueryParam("account_id", accountId));
        }

        return await GetPaged("/v1/banking/balances", queryParams, "balances", ToBalance, cancellationToken);
    }

    public async Task<IReadOnlyCollection<FiskilTransactionData>> GetTransactions(string endUserId, string? accountId, CancellationToken cancellationToken)
    {
        var queryParams = new List<QueryParam>
        {
            new("end_user_id", endUserId),
            new("page[size]", "1000")
        };
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            queryParams.Add(new QueryParam("account_id", accountId));
        }

        return await GetPaged("/v1/banking/transactions", queryParams, "transactions", ToTransaction, cancellationToken);
    }

    private async Task<IReadOnlyCollection<T>> GetPaged<T>(
        string path,
        IReadOnlyCollection<QueryParam> queryParams,
        string rootName,
        Func<JsonElement, T> map,
        CancellationToken cancellationToken)
    {
        var results = new List<T>();
        string? after = null;

        do
        {
            var requestParams = queryParams.ToList();
            if (!string.IsNullOrWhiteSpace(after))
            {
                requestParams.Add(new QueryParam("page[after]", after));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path, requestParams));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                await accessTokenProvider.GetAccessToken(cancellationToken));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty(rootName, out var items) && items.ValueKind == JsonValueKind.Array)
            {
                results.AddRange(items.EnumerateArray().Select(map));
            }

            after = GetNextCursor(document.RootElement);
        }
        while (!string.IsNullOrWhiteSpace(after));

        return results;
    }

    private string BuildUri(string path, IReadOnlyCollection<QueryParam> queryParams)
    {
        var fiskilOptions = options.Value;
        httpClient.BaseAddress ??= new Uri(fiskilOptions.BaseUrl);

        var query = string.Join("&", queryParams.Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(x.Value)}"));
        return $"{path}?{query}";
    }

    private static string? GetNextCursor(JsonElement root)
    {
        if (!root.TryGetProperty("links", out var links))
        {
            return null;
        }

        return GetString(links, "next_cursor")
            ?? GetString(links, "after")
            ?? TryExtractAfterCursor(GetString(links, "next"));
    }

    private static string? TryExtractAfterCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        return query
            .Select(x => x.Split('=', 2))
            .Where(x => x.Length == 2 && Uri.UnescapeDataString(x[0]) == "page[after]")
            .Select(x => Uri.UnescapeDataString(x[1]))
            .FirstOrDefault();
    }

    private static FiskilAccountData ToAccount(JsonElement x)
    {
        return new FiskilAccountData(
            GetString(x, "account_id") ?? GetRequiredString(x, "id"),
            GetString(x, "account_number"),
            GetString(x, "bsb"),
            GetString(x, "name") ?? GetString(x, "display_name") ?? "Bank account",
            GetString(x, "product_name"),
            GetString(x, "product_category"),
            GetString(x, "institution_id"),
            GetString(x, "arrangement_id") ?? GetString(x, "consent_id"),
            GetBool(x, "is_owned"),
            GetString(x, "open_status"),
            GetDateOnly(x, "creation_date"),
            x.GetRawText());
    }

    private static FiskilBalanceData ToBalance(JsonElement x)
    {
        return new FiskilBalanceData(
            GetRequiredString(x, "account_id"),
            GetDecimal(x, "current_balance") ?? 0,
            GetDecimal(x, "available_balance"),
            GetDecimal(x, "credit_limit"),
            GetString(x, "currency"),
            GetDateTimeOffset(x, "as_of"),
            x.GetRawText());
    }

    private static FiskilTransactionData ToTransaction(JsonElement x)
    {
        var category = x.TryGetProperty("category", out var y) && y.ValueKind == JsonValueKind.Object ? y : x;
        return new FiskilTransactionData(
            GetString(x, "fiskil_id") ?? GetRequiredString(x, "id"),
            GetRequiredString(x, "account_id"),
            GetDecimal(x, "amount") ?? 0,
            GetString(x, "currency"),
            GetString(x, "description"),
            GetString(x, "status"),
            GetDateTimeOffset(x, "posting_date_time") ?? GetDateTimeOffset(x, "posted_at") ?? GetDateTimeOffset(x, "posted_time"),
            GetDateTimeOffset(x, "execution_date_time") ?? GetDateTimeOffset(x, "executed_at") ?? GetDateTimeOffset(x, "execution_time"),
            GetString(category, "primary_category"),
            GetString(category, "secondary_category"),
            GetString(x, "merchant_name"),
            GetString(x, "reference"),
            x.GetRawText());
    }

    private static string GetRequiredString(JsonElement x, string propertyName)
    {
        return GetString(x, propertyName) ?? throw new InvalidOperationException($"Fiskil response is missing '{propertyName}'.");
    }

    private static string? GetString(JsonElement x, string propertyName)
    {
        if (!x.TryGetProperty(propertyName, out var y) || y.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return y.ValueKind == JsonValueKind.String ? y.GetString() : y.ToString();
    }

    private static bool? GetBool(JsonElement x, string propertyName)
    {
        if (!x.TryGetProperty(propertyName, out var y) || y.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return y.ValueKind == JsonValueKind.True || (y.ValueKind == JsonValueKind.String && bool.TryParse(y.GetString(), out var z) && z);
    }

    private static decimal? GetDecimal(JsonElement x, string propertyName)
    {
        if (!x.TryGetProperty(propertyName, out var y) || y.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (y.ValueKind == JsonValueKind.Number && y.TryGetDecimal(out var z))
        {
            return z;
        }

        return y.ValueKind == JsonValueKind.String && decimal.TryParse(y.GetString(), out var a) ? a : null;
    }

    private static DateOnly? GetDateOnly(JsonElement x, string propertyName)
    {
        return DateOnly.TryParse(GetString(x, propertyName), out var y) ? y : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement x, string propertyName)
    {
        // Keep the provider's offset so FinancialCalendar can read the bank's own calendar day.
        return DateTimeOffset.TryParse(GetString(x, propertyName), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var y) ? y : null;
    }

    private sealed record QueryParam(string Name, string Value);
}
