using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.ProviderSync;
using Finyte.Data.Tagging;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.ProviderSync;

public interface IFiskilBankingSyncService
{
    Task<SyncChangeSummary> SyncAccounts(ProviderSyncRun syncRun, CancellationToken cancellationToken);
    Task<SyncChangeSummary> SyncBalances(ProviderSyncRun syncRun, CancellationToken cancellationToken);
    Task<SyncChangeSummary> SyncTransactions(ProviderSyncRun syncRun, CancellationToken cancellationToken);
}

public sealed class FiskilBankingSyncService(FinyteDbContext dbContext, IFiskilBankingClient fiskilBankingClient, TransactionTagService tagService) : IFiskilBankingSyncService
{
    public async Task<SyncChangeSummary> SyncAccounts(ProviderSyncRun syncRun, CancellationToken cancellationToken)
    {
        var endUserId = GetEndUserId(syncRun);
        var connections = await dbContext.ProviderConnections
            .Where(x => x.TenantId == syncRun.TenantId && x.EndUserId == endUserId)
            .ToListAsync(cancellationToken);
        var connection = connections.SingleOrDefault(x => x.ConsentId == syncRun.ConsentId)
            ?? connections.SingleOrDefault(x => x.ConsentId == null)
            ?? throw new InvalidOperationException("The sync run does not have a matching provider connection.");
        var accounts = await fiskilBankingClient.GetAccounts(endUserId, cancellationToken);
        var changedAccountIds = new List<Guid>();
        var now = DateTimeOffset.UtcNow;

        foreach (var account in accounts)
        {
            var accountConnectionId = connections
                .SingleOrDefault(x => !string.IsNullOrWhiteSpace(account.ConsentId) && x.ConsentId == account.ConsentId)?.Id
                ?? connection.Id;
            var localAccount = await dbContext.Accounts
                .FirstOrDefaultAsync(x => x.TenantId == syncRun.TenantId && x.FiskilAccountId == account.Id, cancellationToken);

            if (localAccount is null)
            {
                localAccount = new Account
                {
                    TenantId = syncRun.TenantId,
                    ProviderConnectionId = accountConnectionId,
                    FiskilAccountId = account.Id,
                    Name = account.Name ?? "Bank account",
                    CreatedAt = now
                };
                dbContext.Accounts.Add(localAccount);
                changedAccountIds.Add(localAccount.Id);
            }

            if (localAccount.ProviderConnectionId != accountConnectionId)
            {
                localAccount.ProviderConnectionId = accountConnectionId;
                changedAccountIds.Add(localAccount.Id);
            }

            if (ApplyAccount(localAccount, account, syncRun.ConsentId))
            {
                changedAccountIds.Add(localAccount.Id);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return CreateSummary(changedAccountIds, minChangedAt: null, maxChangedAt: null);
    }

    public async Task<SyncChangeSummary> SyncBalances(ProviderSyncRun syncRun, CancellationToken cancellationToken)
    {
        var endUserId = GetEndUserId(syncRun);
        var externalAccountIds = await GetWebhookAccountIds(syncRun.Id, cancellationToken);
        var balances = new List<FiskilBalanceData>();

        if (externalAccountIds.Count == 0)
        {
            balances.AddRange(await fiskilBankingClient.GetBalances(endUserId, accountId: null, cancellationToken));
        }
        else
        {
            foreach (var accountId in externalAccountIds)
            {
                balances.AddRange(await fiskilBankingClient.GetBalances(endUserId, accountId, cancellationToken));
            }
        }

        var changedAccountIds = new List<Guid>();
        foreach (var balance in balances)
        {
            var account = await dbContext.Accounts
                .FirstOrDefaultAsync(x => x.TenantId == syncRun.TenantId && x.FiskilAccountId == balance.AccountId, cancellationToken);

            if (account is null)
            {
                continue;
            }

            if (ApplyBalance(account, balance))
            {
                changedAccountIds.Add(account.Id);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return CreateSummary(changedAccountIds, minChangedAt: null, maxChangedAt: null);
    }

    public async Task<SyncChangeSummary> SyncTransactions(ProviderSyncRun syncRun, CancellationToken cancellationToken)
    {
        var endUserId = GetEndUserId(syncRun);
        var externalAccountIds = await GetWebhookAccountIds(syncRun.Id, cancellationToken);
        var localAccounts = await dbContext.Accounts
            .Where(x => x.TenantId == syncRun.TenantId && x.FiskilAccountId != null)
            .ToDictionaryAsync(x => x.FiskilAccountId!, cancellationToken);
        var accountIdsToFetch = externalAccountIds.Count == 0
            ? localAccounts.Keys.ToList()
            : externalAccountIds;
        var changedAccountIds = new List<Guid>();
        DateTimeOffset? minChangedAt = null;
        DateTimeOffset? maxChangedAt = null;
        var fetchedTransactions = new List<(Guid AccountId, FiskilTransactionData Transaction)>();

        foreach (var externalAccountId in accountIdsToFetch)
        {
            if (!localAccounts.TryGetValue(externalAccountId, out var account))
            {
                continue;
            }

            var transactions = await fiskilBankingClient.GetTransactions(endUserId, externalAccountId, cancellationToken);
            foreach (var transaction in transactions)
            {
                fetchedTransactions.Add((account.Id, transaction));
            }
        }

        // Fetch remotely before taking the tag lock; reconcile current rules and manual choices atomically.
        await using var databaseTransaction = await tagService.BeginMutation(syncRun.TenantId, cancellationToken);
        var rules = await tagService.GetRules(syncRun.TenantId, cancellationToken);
        foreach (var fetchedTransaction in fetchedTransactions)
        {
            var transaction = fetchedTransaction.Transaction;
            var changed = await UpsertTransaction(syncRun.TenantId, fetchedTransaction.AccountId, transaction, rules, cancellationToken);
            if (!changed)
            {
                continue;
            }

            changedAccountIds.Add(fetchedTransaction.AccountId);
            var changedAt = transaction.PostedAt ?? transaction.ExecutedAt;
            minChangedAt = Min(minChangedAt, changedAt);
            maxChangedAt = Max(maxChangedAt, changedAt);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }
        return CreateSummary(changedAccountIds, minChangedAt, maxChangedAt);
    }

    private async Task<bool> UpsertTransaction(Guid tenantId, Guid accountId, FiskilTransactionData transaction, IReadOnlyList<MerchantTagRule> rules, CancellationToken cancellationToken)
    {
        var localTransaction = dbContext.Transactions.Local.FirstOrDefault(x => x.TenantId == tenantId && x.FiskilTransactionId == transaction.Id)
            ?? await dbContext.Transactions
                .Include(x => x.TagAssignments).Include(x => x.TagExclusions)
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.FiskilTransactionId == transaction.Id, cancellationToken);

        if (localTransaction is null)
        {
            localTransaction = new Transaction
            {
                TenantId = tenantId,
                AccountId = accountId,
                FiskilTransactionId = transaction.Id,
                Amount = transaction.Amount,
                Currency = transaction.Currency ?? "AUD",
                Description = transaction.Description,
                Status = transaction.Status,
                PostedAt = transaction.PostedAt,
                ExecutedAt = transaction.ExecutedAt,
                PrimaryCategory = transaction.PrimaryCategory,
                SecondaryCategory = transaction.SecondaryCategory,
                MerchantName = transaction.MerchantName,
                Reference = transaction.Reference,
                RawJson = transaction.RawJson,
                CreatedAt = DateTimeOffset.UtcNow
            };
            tagService.Reconcile(localTransaction, rules);
            dbContext.Transactions.Add(localTransaction);
            return true;
        }

        var changed = false;
        changed |= SetIfChanged(localTransaction.AccountId, accountId, x => localTransaction.AccountId = x);
        changed |= SetIfChanged(localTransaction.Amount, transaction.Amount, x => localTransaction.Amount = x);
        changed |= SetIfChanged(localTransaction.Currency, transaction.Currency ?? "AUD", x => localTransaction.Currency = x);
        changed |= SetIfChanged(localTransaction.Description, transaction.Description, x => localTransaction.Description = x);
        changed |= SetIfChanged(localTransaction.Status, transaction.Status, x => localTransaction.Status = x);
        changed |= SetIfChanged(localTransaction.PostedAt, transaction.PostedAt, x => localTransaction.PostedAt = x);
        changed |= SetIfChanged(localTransaction.ExecutedAt, transaction.ExecutedAt, x => localTransaction.ExecutedAt = x);
        changed |= SetIfChanged(localTransaction.PrimaryCategory, transaction.PrimaryCategory, x => localTransaction.PrimaryCategory = x);
        changed |= SetIfChanged(localTransaction.SecondaryCategory, transaction.SecondaryCategory, x => localTransaction.SecondaryCategory = x);
        changed |= SetIfChanged(localTransaction.MerchantName, transaction.MerchantName, x => localTransaction.MerchantName = x);
        changed |= SetIfChanged(localTransaction.Reference, transaction.Reference, x => localTransaction.Reference = x);
        changed |= SetIfChanged(localTransaction.RawJson, transaction.RawJson, x => localTransaction.RawJson = x);
        changed |= tagService.Reconcile(localTransaction, rules);
        return changed;
    }

    private async Task<List<string>> GetWebhookAccountIds(Guid syncRunId, CancellationToken cancellationToken)
    {
        var payloadJson = await dbContext.ProviderWebhookEvents
            .Where(x => x.SyncRunId == syncRunId)
            .Select(x => x.PayloadJson)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return [];
        }

        using var document = JsonDocument.Parse(payloadJson);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("account_ids", out var accountIds)
            || accountIds.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return accountIds
            .EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct()
            .ToList();
    }

    private static bool ApplyAccount(Account localAccount, FiskilAccountData account, string? consentId)
    {
        var changed = false;
        changed |= SetIfChanged(localAccount.AccountNumber, account.AccountNumber, x => localAccount.AccountNumber = x);
        changed |= SetIfChanged(localAccount.Bsb, account.Bsb, x => localAccount.Bsb = x);
        changed |= SetIfChanged(localAccount.Name, account.Name ?? "Bank account", x => localAccount.Name = x);
        changed |= SetIfChanged(localAccount.ProductName, account.ProductName, x => localAccount.ProductName = x);
        changed |= SetIfChanged(localAccount.ProductCategory, account.ProductCategory, x => localAccount.ProductCategory = x);
        changed |= SetIfChanged(localAccount.InstitutionId, account.InstitutionId, x => localAccount.InstitutionId = x);
        changed |= SetIfChanged(localAccount.ConsentId, account.ConsentId ?? consentId, x => localAccount.ConsentId = x);
        changed |= SetIfChanged(localAccount.IsOwned, account.IsOwned, x => localAccount.IsOwned = x);
        changed |= SetIfChanged(localAccount.OpenStatus, account.OpenStatus, x => localAccount.OpenStatus = x);
        changed |= SetIfChanged(localAccount.CreationDate, account.CreationDate, x => localAccount.CreationDate = x);
        return changed;
    }

    private static bool ApplyBalance(Account account, FiskilBalanceData balance)
    {
        var changed = false;
        changed |= SetIfChanged(account.CurrentBalance, balance.CurrentBalance, x => account.CurrentBalance = x);
        changed |= SetIfChanged(account.AvailableBalance, balance.AvailableBalance, x => account.AvailableBalance = x);
        changed |= SetIfChanged(account.CreditLimit, balance.CreditLimit, x => account.CreditLimit = x);
        changed |= SetIfChanged(account.Currency, balance.Currency ?? "AUD", x => account.Currency = x);
        changed |= SetIfChanged(account.BalanceAsOf, balance.AsOf, x => account.BalanceAsOf = x);
        return changed;
    }

    private static SyncChangeSummary CreateSummary(IReadOnlyCollection<Guid> accountIds, DateTimeOffset? minChangedAt, DateTimeOffset? maxChangedAt)
    {
        var distinctAccountIds = accountIds.Distinct().ToList();
        return new SyncChangeSummary(distinctAccountIds, minChangedAt, maxChangedAt, distinctAccountIds.Count > 0);
    }

    private static string GetEndUserId(ProviderSyncRun syncRun)
    {
        return string.IsNullOrWhiteSpace(syncRun.EndUserId)
            ? throw new InvalidOperationException("Provider sync run is missing EndUserId.")
            : syncRun.EndUserId;
    }

    private static DateTimeOffset? Min(DateTimeOffset? x, DateTimeOffset? y)
    {
        if (x is null)
        {
            return y;
        }

        if (y is null)
        {
            return x;
        }

        return x < y ? x : y;
    }

    private static DateTimeOffset? Max(DateTimeOffset? x, DateTimeOffset? y)
    {
        if (x is null)
        {
            return y;
        }

        if (y is null)
        {
            return x;
        }

        return x > y ? x : y;
    }

    private static bool SetIfChanged<T>(T currentValue, T newValue, Action<T> set)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, newValue))
        {
            return false;
        }

        set(newValue);
        return true;
    }
}
