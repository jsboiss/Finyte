using Finyte.Core.Accounts;

namespace Finyte.Data.Transactions;

public sealed record TransactionSearch
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public Guid? AccountId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? Search { get; init; }
    public string? Category { get; init; }
    public bool Uncategorised { get; init; }
    public Guid[] TagIds { get; init; } = [];
    public string TagMatch { get; init; } = "any";
    public bool Untagged { get; init; }
    public string AmountMode { get; init; } = "signed";
    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }
    public string? Currency { get; init; }
    public string Sort { get; init; } = "-date";
    public bool PostedOnly { get; init; }
    public bool AnalyticsOnly { get; init; }
    public string Direction { get; init; } = "all";
    public string InternalTransfers { get; init; } = "include";

    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (Direction is not ("all" or "debit" or "credit") || InternalTransfers is not ("include" or "exclude" or "only"))
        {
            errors["scope"] = ["Choose all, debit or credit and include, exclude or only internal transfers."];
        }

        if (Page < 1 || (long)(Page - 1) * PageSize > int.MaxValue)
        {
            errors["page"] = ["Page must be positive and within the supported pagination range."];
        }

        if (PageSize is < 1 or > 250)
        {
            errors["pageSize"] = ["Page size must be between 1 and 250."];
        }

        if (From > To)
        {
            errors["from"] = ["Start date must be on or before end date."];
        }

        if (AmountMode is not ("signed" or "absolute") || (AmountMode == "absolute" && (MinAmount < 0 || MaxAmount < 0)))
        {
            errors["amountMode"] = ["Choose signed or absolute amounts; absolute bounds must be non-negative."];
        }

        if (MinAmount > MaxAmount)
        {
            errors["minAmount"] = ["Minimum amount must be less than or equal to maximum amount."];
        }

        if (Search?.Length > 200 || Category?.Length > 200)
        {
            errors["search"] = ["Search and category text must each be 200 characters or fewer."];
        }

        if (TagIds.Length > 50 || TagIds.Contains(Guid.Empty))
        {
            errors["tagIds"] = ["Specify at most 50 non-empty tag IDs."];
        }

        if (Untagged && TagIds.Length > 0)
        {
            errors["untagged"] = ["Untagged cannot be combined with selected tags."];
        }

        if (Uncategorised && !string.IsNullOrWhiteSpace(Category))
        {
            errors["uncategorised"] = ["Uncategorised cannot be combined with a category search."];
        }

        if (TagMatch is not ("any" or "all"))
        {
            errors["tagMatch"] = ["Tag match must be any or all."];
        }

        if (Currency is not null && (Currency.Length != 3 || !Currency.All(x => x is >= 'A' and <= 'Z')))
        {
            errors["currency"] = ["Currency must be a three-letter currency code."];
        }

        if (Sort is not ("date" or "-date" or "amount" or "-amount" or "description" or "-description" or "magnitude" or "-magnitude"))
        {
            errors["sort"] = ["Sort must be date, -date, amount, -amount, description, -description, magnitude or -magnitude."];
        }

        return errors;
    }

    public IQueryable<Transaction> Apply(IQueryable<Transaction> transactions, Guid tenantId)
    {
        var query = transactions.Where(x => x.TenantId == tenantId);
        if (PostedOnly)
        {
            query = query.Where(x => x.PostedAt != null && (x.Status == null || x.Status == "" || x.Status.ToLower() == "posted"));
        }
        if (Direction == "debit")
        {
            query = query.Where(x => x.Amount < 0);
        }
        else if (Direction == "credit")
        {
            query = query.Where(x => x.Amount > 0);
        }

        if (AccountId is { } accountId)
        {
            query = query.Where(x => x.AccountId == accountId);
        }

        if (From is { } from)
        {
            var fromInstant = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(x => (x.PostedAt ?? x.CreatedAt) >= fromInstant);
        }

        if (To is { } to && to < DateOnly.MaxValue)
        {
            var untilInstant = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(x => (x.PostedAt ?? x.CreatedAt) < untilInstant);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var search = Search.Trim().ToLowerInvariant();
            query = query.Where(x => (x.Description != null && x.Description.ToLower().Contains(search))
                || (x.MerchantName != null && x.MerchantName.ToLower().Contains(search))
                || (x.Reference != null && x.Reference.ToLower().Contains(search)));
        }

        if (Uncategorised)
        {
            query = query.Where(x => (x.CategoryOverride ?? "").Trim() == ""
                && (x.SecondaryCategory ?? "").Trim() == ""
                && (x.PrimaryCategory ?? "").Trim() == ""
                && (x.CategoryFromRule ?? "").Trim() == "");
        }
        else if (!string.IsNullOrWhiteSpace(Category))
        {
            var category = Category.Trim().ToLowerInvariant();
            query = query.Where(x => (x.CategoryOverride ?? "").Trim() != ""
                ? x.CategoryOverride!.ToLower().Contains(category)
                : (x.CategoryFromRule ?? "").Trim() != ""
                    ? x.CategoryFromRule!.ToLower().Contains(category)
                    : (x.PrimaryCategory != null && x.PrimaryCategory.ToLower().Contains(category))
                        || (x.SecondaryCategory != null && x.SecondaryCategory.ToLower().Contains(category)));
        }

        var tagIds = TagIds.Distinct().ToArray();

        if (tagIds.Length > 0 && TagMatch == "all")
        {
            // The assignment key guarantees one row per transaction/tag, so one count can match the whole set.
            query = query.Where(x => x.TagAssignments.Count(y => tagIds.Contains(y.TagId)
                && y.Tag != null && y.Tag.TenantId == tenantId) == tagIds.Length);
        }
        else if (tagIds.Length > 0)
        {
            query = query.Where(x => x.TagAssignments.Any(y => tagIds.Contains(y.TagId) && y.Tag != null && y.Tag.TenantId == tenantId));
        }

        if (Untagged)
        {
            query = query.Where(x => !x.TagAssignments.Any(y => y.Tag != null && y.Tag.TenantId == tenantId));
        }

        if (MinAmount is { } minAmount)
        {
            query = query.Where(x => (AmountMode == "absolute" ? Math.Abs(x.Amount) : x.Amount) >= minAmount);
        }

        if (MaxAmount is { } maxAmount)
        {
            query = query.Where(x => (AmountMode == "absolute" ? Math.Abs(x.Amount) : x.Amount) <= maxAmount);
        }

        if (Currency is not null)
        {
            query = query.Where(x => x.Currency == Currency);
        }

        return query;
    }

    public IOrderedQueryable<Transaction> Order(IQueryable<Transaction> transactions)
    {
        var ordered = Sort switch
        {
            "date" => transactions.OrderBy(x => x.PostedAt ?? x.CreatedAt),
            "magnitude" => transactions.OrderBy(x => Math.Abs(x.Amount)),
            "-magnitude" => transactions.OrderByDescending(x => Math.Abs(x.Amount)),
            "amount" => transactions.OrderBy(x => x.Amount),
            "-amount" => transactions.OrderByDescending(x => x.Amount),
            "description" => transactions.OrderBy(x => (x.Description ?? "").ToLower()),
            "-description" => transactions.OrderByDescending(x => (x.Description ?? "").ToLower()),
            _ => transactions.OrderByDescending(x => x.PostedAt ?? x.CreatedAt)
        };

        return ordered
            .ThenByDescending(x => x.PostedAt ?? x.CreatedAt)
            .ThenByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id);
    }
}
