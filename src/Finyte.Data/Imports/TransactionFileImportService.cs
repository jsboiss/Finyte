using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Finyte.Core.Accounts;
using Finyte.Data.Analytics;
using Finyte.Data.Tagging;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Imports;

public sealed class TransactionFileImportService(FinyteDbContext dbContext, IProjectionInvalidator projectionInvalidator, TransactionTagService tagService)
{
    public async Task<TransactionFileImport> Import(Guid tenantId, Guid accountId, string fileName, Stream stream, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == accountId, cancellationToken)
            ?? throw new KeyNotFoundException("The selected account was not found.");
        var run = new TransactionFileImport { TenantId = tenantId, AccountId = accountId, FileName = Path.GetFileName(fileName) };
        dbContext.TransactionFileImports.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
            var content = await reader.ReadToEndAsync(cancellationToken);
            var transactions = TransactionFileParser.Parse(fileName, content);
            var currency = Regex.Match(content, @"<CURDEF>\s*([^<\r\n]+)", RegexOptions.IgnoreCase);
            if (currency.Success && !currency.Groups[1].Value.Trim().Equals(account.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The OFX currency does not match the selected account.");
            }

            // Serialize uploads for this account; identities and projections commit with the transactions.
            await using var databaseTransaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
            await tagService.Lock(tenantId, cancellationToken);
            if (dbContext.Database.IsNpgsql())
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM accounts WHERE \"Id\" = {accountId} AND \"TenantId\" = {tenantId} FOR UPDATE", cancellationToken);
            }

            // Re-read after acquiring the lock: another import may have supplied a newer snapshot.
            await dbContext.Entry(account).ReloadAsync(cancellationToken);
            var balance = AccountPreferences.IsProviderManaged(account) ? null : TransactionFileParser.ParseBalance(content);
            var balanceChanged = balance is not null
                && (!AccountPreferences.HasReportedBalance(account) || balance.AsOf > account.BalanceAsOf);
            if (balanceChanged)
            {
                account.CurrentBalance = balance!.CurrentBalance;
                account.AvailableBalance = balance.AvailableBalance;
                account.BalanceAsOf = balance.AsOf;
                account.ManualBalanceVersion++;
            }

            var from = new DateTimeOffset(transactions.Min(x => x.PostedDate).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var to = new DateTimeOffset(transactions.Max(x => x.PostedDate).ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            var existing = await dbContext.Transactions
                .Where(x => x.TenantId == tenantId && x.AccountId == accountId && x.PostedAt >= from && x.PostedAt <= to)
                .ToListAsync(cancellationToken);
            var occurrences = new Dictionary<string, int>();
            var rows = transactions.Select(x =>
            {
                var key = MatchKey(x.PostedDate, x.AmountMinorUnits / 100m, x.Description);
                var occurrence = occurrences.GetValueOrDefault(key);
                occurrences[key] = occurrence + 1;
                var identity = string.IsNullOrWhiteSpace(x.BankId) ? $"row:{key}|{occurrence}" : $"bank:{x.BankId.Trim()}";
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{accountId:N}|{identity}"))).ToLowerInvariant();
                return new ImportRow(x, key, $"file:{hash}");
            }).ToList();
            var externalIds = rows.Select(x => x.ExternalId).ToList();
            var known = await dbContext.TransactionFileIdentities
                .Where(x => x.TenantId == tenantId && externalIds.Contains(x.ExternalId))
                .ToDictionaryAsync(x => x.ExternalId, x => x.TransactionId, cancellationToken);
            var used = known.Values.ToHashSet();
            var seen = new HashSet<string>();
            var rules = await tagService.GetRules(tenantId, cancellationToken);

            foreach (var row in rows)
            {
                if (!seen.Add(row.ExternalId) || known.ContainsKey(row.ExternalId))
                {
                    run.SkippedCount++;
                    continue;
                }

                var transaction = row.Transaction;
                var amount = transaction.AmountMinorUnits / 100m;
                var candidates = existing.Where(x => !used.Contains(x.Id) && !x.FiskilTransactionId.StartsWith("file:", StringComparison.Ordinal)
                    && x.PostedAt.HasValue && DateOnly.FromDateTime(x.PostedAt.Value.UtcDateTime) == transaction.PostedDate
                    && x.Amount == amount && !string.Equals(x.Status, "pending", StringComparison.OrdinalIgnoreCase)).ToList();
                var matched = candidates.FirstOrDefault(x => MatchKey(transaction.PostedDate, x.Amount, x.Description ?? "") == row.MatchKey);
                // Only provider rows are eligible for the less strict legacy match.
                matched ??= candidates.Count == 1 && !candidates[0].FiskilTransactionId.StartsWith("file:", StringComparison.Ordinal)
                    ? candidates[0] : null;
                if (matched is not null)
                {
                    used.Add(matched.Id);
                    AddIdentity(tenantId, matched.Id, row.ExternalId);
                    run.SkippedCount++;
                    continue;
                }

                var entity = new Transaction
                {
                    TenantId = tenantId, AccountId = accountId, FiskilTransactionId = row.ExternalId,
                    Amount = amount, Currency = account.Currency, Description = transaction.Description,
                    MerchantName = transaction.Description.Length <= 256 ? transaction.Description : transaction.Description[..256],
                    Status = "posted", PostedAt = new DateTimeOffset(transaction.PostedDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    CreatedAt = DateTimeOffset.UtcNow,
                    RawJson = JsonSerializer.Serialize(new { source = "file-import", fileName = run.FileName, transaction.BankId })
                };
                tagService.Reconcile(entity, rules);

                dbContext.Transactions.Add(entity);
                AddIdentity(tenantId, entity.Id, row.ExternalId);
                run.ImportedCount++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (run.ImportedCount > 0)
            {
                await new Transfers.AutomaticTransferService(dbContext, projectionInvalidator).Reconcile(tenantId, cancellationToken,
                    DateOnly.FromDateTime(from.UtcDateTime), DateOnly.FromDateTime(to.UtcDateTime));
            }
            run.TotalCount = transactions.Count;
            run.Status = "completed";
            run.CompletedAt = DateTimeOffset.UtcNow;
            if (run.ImportedCount > 0 || balanceChanged)
            {
                await projectionInvalidator.TenantProjectionDataChanged(tenantId, "OFX transactions imported", cancellationToken);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            if (databaseTransaction is not null)
            {
                await databaseTransaction.CommitAsync(cancellationToken);
            }
            return run;
        }
        catch (Exception exception)
        {
            dbContext.ChangeTracker.Clear();
            run = await dbContext.TransactionFileImports.SingleAsync(x => x.TenantId == tenantId && x.Id == run.Id, CancellationToken.None);
            run.Status = "failed";
            run.Error = exception is InvalidDataException ? exception.Message : "The import failed. Please try again.";
            run.CompletedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private void AddIdentity(Guid tenantId, Guid transactionId, string externalId)
    {
        dbContext.TransactionFileIdentities.Add(new TransactionFileIdentity { TenantId = tenantId, TransactionId = transactionId, ExternalId = externalId });
    }

    private static string MatchKey(DateOnly date, decimal amount, string description)
    {
        return $"{date:yyyy-MM-dd}|{amount.ToString("F2", CultureInfo.InvariantCulture)}|{string.Join(' ', description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant()}";
    }

    private sealed record ImportRow(ImportedTransaction Transaction, string MatchKey, string ExternalId);
}
