using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class ManualRefundService(CommerceDb db, CommerceOptions commerce, TtbOptions ttb, ManualRefundOptions policy, TimeProvider clock, PrivateFiles files)
{
    private string Merchant => commerce.PaymentMode == PaymentMode.Mock ? "SIM" : ttb.MerchantId;
    private string Biller => commerce.PaymentMode == PaymentMode.Mock ? "000000000000000" : ttb.BillerNo;
    private bool ApprovedPolicy => policy.ExternalRefundAttestationApproved && (commerce.Environment != CommerceEnvironment.Local || commerce.PaymentMode == PaymentMode.Mock && policy.LocalSimulationEnabled);

    public async Task<ManualRefundView> CreateAsync(Guid bankId, ManualRefundInput input, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor); Reason(input.Reason);
        var key = Key(input.OperationId);
        if (input.AmountMinor is null || input.AmountMinor.Length is < 1 or > 19 || input.AmountMinor[0] == '0' || input.AmountMinor.Any(c => c is < '0' or > '9') ||
            !long.TryParse(input.AmountMinor, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            throw new CommerceException(400, "manual_refund_amount_invalid", "Provide an exact positive integer minor amount.");
        var hash = SecretTokens.Hash(JsonSerializer.Serialize(new { action = "manual-refund.create", bankId, input, actor = actor.Key }));
        db.ChangeTracker.Clear(); var lookup = await CreditAsync(bankId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await OperationLockAsync(key, cancellationToken);
        var previous = await PriorAsync(key, hash, cancellationToken); if (previous is not null) return previous;
        await InstructionLockAsync(lookup.InstructionId, cancellationToken);
        var bank = await CreditAsync(bankId, cancellationToken);
        var allocation = await db.Allocations.AsNoTracking().SingleOrDefaultAsync(x => x.BankTransactionId == bankId, cancellationToken);
        var orderId = allocation?.OrderId ?? await db.Attempts.AsNoTracking().Where(x => x.QrId == bank.QrId && x.MerchantId == Merchant).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(cancellationToken);
        var order = await OrderLockedAsync(orderId, cancellationToken);
        await NoBankVoidAsync(bank, cancellationToken);
        var reserved = await db.ManualRefunds.Where(x => x.BankTransactionId == bankId && x.State != "Cancelled").SumAsync(x => (decimal)x.AmountMinor, cancellationToken);
        if (reserved + amount > bank.AmountMinor) throw new CommerceException(409, "manual_refund_credit_exhausted", "Other manual cases already reserve or attest this credit.");
        if (input.SourceCaseId is not null && !await db.Cases.AnyAsync(x => x.Id == input.SourceCaseId && x.BankTransactionId == bankId && x.OrderId == orderId, cancellationToken))
            throw new CommerceException(409, "manual_refund_source_mismatch", "The source case does not refer to this exact credit.");
        var review = new ReconciliationCase { Kind = "ManualRefund", OrderId = orderId, BankTransactionId = bankId, CreatedAt = clock.GetUtcNow() };
        var row = new ManualRefund { Environment = commerce.Environment, MerchantId = Merchant, BillerNo = Biller, BankTransactionId = bankId, OrderId = orderId,
            CaseId = review.Id, SourceCaseId = input.SourceCaseId, AmountMinor = amount, AffectsOrderPayment = allocation?.Primary == true, CreatedBy = actor.Key, Reason = input.Reason.Trim(), CreatedAt = clock.GetUtcNow() };
        review.EffectKey = $"manual-refund:{row.Id}";
        review.EvidenceJson = JsonSerializer.Serialize(new { manualRefundId = row.Id, bankId, amountMinor = input.AmountMinor, source = "ManualExternalProcess", noBankRequest = true });
        db.Cases.Add(review); db.ManualRefunds.Add(row);
        var before = JsonSerializer.Serialize(new { bank.Voided, reservedMinor = reserved.ToString(CultureInfo.InvariantCulture), orderState = order?.State.ToString(), paymentState = order?.PaymentState.ToString() });
        if (order is not null) order.State = OrderState.Review;
        var result = await ViewAsync(row, bank, actor, cancellationToken);
        Record(key, hash, "manual-refund.create", input.Reason, actor, row, before, result);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return result;
    }

    public async Task<ManualRefundView> ActAsync(Guid id, ManualRefundActionInput input, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor); Reason(input.Reason); var key = Key(input.OperationId);
        if (input.ExpectedVersion < 1 || input.Action is not ("submit-evidence" or "approve-attestation" or "reject-evidence" or "cancel-before-submission"))
            throw new CommerceException(400, "manual_refund_action_invalid", "Choose a supported manual-case action and current version.");
        var hash = SecretTokens.Hash(JsonSerializer.Serialize(new { id, input, actor = actor.Key }));
        db.ChangeTracker.Clear(); var lookup = await FindAsync(id, cancellationToken);
        var bankLookup = await CreditAsync(lookup.BankTransactionId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await OperationLockAsync(key, cancellationToken); var prior = await PriorAsync(key, hash, cancellationToken); if (prior is not null) return prior;
        await InstructionLockAsync(bankLookup.InstructionId, cancellationToken);
        var order = await OrderLockedAsync(lookup.OrderId, cancellationToken);
        var review = (await db.Cases.FromSqlInterpolated($"SELECT * FROM ttb.\"Cases\" WHERE \"Id\" = {lookup.CaseId} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        var row = (await db.ManualRefunds.FromSqlInterpolated($"SELECT * FROM ttb.\"ManualRefunds\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        var bank = await CreditAsync(row.BankTransactionId, cancellationToken);
        if (row.Version != input.ExpectedVersion || review.State != "Open") throw new CommerceException(409, "manual_refund_changed", "Reload the manual case before acting.");
        var before = JsonSerializer.Serialize(new { row.Version, row.State, row.SubmittedBy, row.ExternalReference, row.ApprovedBy, caseVersion = review.Version, paymentState = order?.PaymentState.ToString() });
        if (input.Action == "cancel-before-submission")
        {
            if (row.State != "Review" || row.SubmittedAt is not null) throw new CommerceException(409, "manual_refund_cannot_cancel", "A declared external transfer cannot be cancelled to permit another refund.");
            row.State = "Cancelled"; review.State = "Resolved"; review.ResolvedAt = clock.GetUtcNow();
        }
        else if (input.Action == "submit-evidence")
        {
            await NoBankVoidAsync(bank, cancellationToken);
            if (row.State != "Review" || string.IsNullOrWhiteSpace(input.ExternalReference) || input.ExternalReference.Length > 200 || input.ExternalReference.Any(char.IsControl) ||
                input.ExternalPaidAt is null || input.ExternalPaidAt > clock.GetUtcNow() || input.EvidenceIds is null || input.EvidenceIds.Length is < 1 or > 8 || input.EvidenceIds.Distinct().Count() != input.EvidenceIds.Length)
                throw new CommerceException(400, "manual_refund_evidence_invalid", "Provide a past external-transfer time/reference and one to eight case evidence files.");
            if (await db.FinanceEvidence.CountAsync(x => x.CaseId == row.CaseId && input.EvidenceIds.Contains(x.Id), cancellationToken) != input.EvidenceIds.Length)
                throw new CommerceException(409, "manual_refund_evidence_mismatch", "Evidence must belong to this manual case.");
            var issuedAt = await db.Attempts.AsNoTracking().Where(x => x.QrId == bank.QrId && x.MerchantId == Merchant).Select(x => (DateTimeOffset?)x.CreatedAt).SingleOrDefaultAsync(cancellationToken);
            var earliest = bank.PaymentTimeSource == "ReportIsoPayment" ? bank.PaymentOccurredAt : issuedAt;
            if (input.ExternalPaidAt < DateTimeOffset.UnixEpoch || earliest is not null && input.ExternalPaidAt < earliest)
                throw new CommerceException(400, "manual_refund_time_invalid", "The declared external return predates its original credit evidence.");
            row.State = "EvidenceSubmitted"; row.SubmittedBy = actor.Key; row.SubmittedAt ??= clock.GetUtcNow(); row.ExternalReference = input.ExternalReference.Trim();
            row.ExternalPaidAt = input.ExternalPaidAt.Value.ToUniversalTime(); row.EvidenceIdsJson = JsonSerializer.Serialize(input.EvidenceIds);
        }
        else if (input.Action == "reject-evidence")
        {
            if (row.State != "EvidenceSubmitted" || actor.Key == row.SubmittedBy) throw new CommerceException(409, "manual_refund_independent_review_required", "A different officer must review the submitted declaration.");
            // Keep SubmittedAt and the reservation: rejected paperwork does not prove the external money was never sent.
            row.State = "Review";
        }
        else
        {
            if (!ApprovedPolicy) throw new CommerceException(503, "manual_refund_policy_unapproved", "The company external-refund attestation policy is not approved.");
            if (row.State != "EvidenceSubmitted" || actor.Key == row.CreatedBy || actor.Key == row.SubmittedBy)
                throw new CommerceException(409, "manual_refund_independent_review_required", "The creator/submitter cannot approve their own external-refund attestation.");
            await NoBankVoidAsync(bank, cancellationToken);
            var evidenceIds = JsonSerializer.Deserialize<Guid[]>(row.EvidenceIdsJson) ?? [];
            if (evidenceIds.Length == 0 || await db.FinanceEvidence.CountAsync(x => x.CaseId == row.CaseId && evidenceIds.Contains(x.Id), cancellationToken) != evidenceIds.Length)
                throw new CommerceException(409, "manual_refund_evidence_mismatch", "Submitted evidence is incomplete.");
            foreach (var proof in await db.FinanceEvidence.AsNoTracking().Where(x => evidenceIds.Contains(x.Id)).ToArrayAsync(cancellationToken))
            {
                try
                {
                    await using var stream = File.OpenRead(files.PathFor("finance", proof.Key, ".proof"));
                    if (stream.Length != proof.ByteLength || Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken)) != proof.Sha256)
                        throw new CommerceException(503, "finance_evidence_unavailable", "The submitted file bytes no longer match their immutable evidence hash.");
                }
                catch (IOException) { throw new CommerceException(503, "finance_evidence_unavailable", "The submitted file is temporarily unavailable."); }
            }
            row.State = "Attested"; row.ApprovedBy = actor.Key; row.ApprovedAt = clock.GetUtcNow();
            review.State = "Resolved"; review.ResolvedAt = clock.GetUtcNow();
            if (row.AffectsOrderPayment && order is not null)
            {
                var previouslyAttested = await db.ManualRefunds.Where(x => x.BankTransactionId == bank.Id && x.State == "Attested" && x.Id != row.Id).SumAsync(x => (decimal)x.AmountMinor, cancellationToken);
                if (previouslyAttested + row.AmountMinor > bank.AmountMinor) throw new CommerceException(409, "manual_refund_credit_exhausted", "The external refund exceeds its original credit.");
                order.State = OrderState.Review;
                if (previouslyAttested + row.AmountMinor == bank.AmountMinor)
                {
                    order.PaymentState = PaymentState.Refunded;
                    var grants = await db.Entitlements.Where(x => x.OrderId == order.Id && x.State == EntitlementState.Active).ToArrayAsync(cancellationToken);
                    foreach (var grant in grants) { grant.State = EntitlementState.Revoked; grant.RevokedAt = clock.GetUtcNow(); grant.RevocationReason = "Approved external full-refund attestation"; }
                    var dispatches = await db.Fulfillments.Where(x => x.OrderId == order.Id && x.State != FulfillmentState.Delivered && x.State != FulfillmentState.Shipped && x.State != FulfillmentState.Cancelled).ToArrayAsync(cancellationToken);
                    foreach (var dispatch in dispatches) { if (dispatch.State != FulfillmentState.Review) dispatch.Version++; dispatch.State = FulfillmentState.Review; dispatch.UpdatedAt = clock.GetUtcNow(); }
                }
                db.Cases.Add(new ReconciliationCase { Kind = "ManualRefundFulfillmentReview", EffectKey = $"manual-refund-goods:{row.Id}", OrderId = order.Id, BankTransactionId = bank.Id,
                    EvidenceJson = JsonSerializer.Serialize(new { manualRefundId = row.Id, source = "ApprovedManualAttestation", amountMinor = row.AmountMinor.ToString(CultureInfo.InvariantCulture), noBankVoid = true }), CreatedAt = clock.GetUtcNow() });
            }
        }
        row.Version++; row.DecisionReason = input.Reason.Trim(); review.Version++;
        review.ResolutionJson = JsonSerializer.Serialize(new { manualRefundId = row.Id, input.Action, row.State, actor = actor.Key, input.Reason, noBankRequest = true });
        db.CaseNotes.Add(new FinanceCaseNote { CaseId = row.CaseId, Actor = actor.Key, Body = input.Action + ": " + input.Reason.Trim(), CreatedAt = clock.GetUtcNow() });
        var result = await ViewAsync(row, bank, actor, cancellationToken); Record(key, hash, "manual-refund." + input.Action, input.Reason, actor, row, before, result);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return result;
    }

    public async Task<ManualRefundView> GetAsync(Guid id, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor); var row = await FindAsync(id, cancellationToken); return await ViewAsync(row, await CreditAsync(row.BankTransactionId, cancellationToken), actor, cancellationToken);
    }
    public async Task<ManualRefundView?> ForCaseAsync(Guid caseId, ShopperActor actor, CancellationToken cancellationToken)
    {
        Finance(actor); var row = await db.ManualRefunds.AsNoTracking().SingleOrDefaultAsync(x => x.CaseId == caseId && x.Environment == commerce.Environment && x.MerchantId == Merchant && x.BillerNo == Biller, cancellationToken);
        return row is null ? null : await ViewAsync(row, await CreditAsync(row.BankTransactionId, cancellationToken), actor, cancellationToken);
    }
    private async Task<ManualRefundView> ViewAsync(ManualRefund row, BankTransaction bank, ShopperActor actor, CancellationToken cancellationToken)
    {
        var evidence = await db.FinanceEvidence.AsNoTracking().Where(x => x.CaseId == row.CaseId).OrderBy(x => x.Id).Select(x => new FinanceEvidenceView(x.Id, x.FileName, x.ByteLength, x.Sha256, x.UploadedBy, x.CreatedAt)).ToArrayAsync(cancellationToken);
        return new(row.Id, row.CaseId, row.BankTransactionId, row.OrderId, row.Version, row.State, row.AmountMinor.ToString(CultureInfo.InvariantCulture), bank.AmountMinor.ToString(CultureInfo.InvariantCulture),
            row.AffectsOrderPayment, row.CreatedBy, row.Reason, row.SubmittedBy, row.SubmittedAt, row.ExternalReference, row.ExternalPaidAt, row.ApprovedBy, row.ApprovedAt, row.DecisionReason,
            ApprovedPolicy, ApprovedPolicy && row.State == "EvidenceSubmitted" && actor.Key != row.CreatedBy && actor.Key != row.SubmittedBy && !bank.Voided, evidence);
    }
    private async Task NoBankVoidAsync(BankTransaction bank, CancellationToken cancellationToken)
    {
        if (bank.Voided || await db.Voids.AnyAsync(x => x.BankTransactionId == bank.Id && x.State != "Rejected", cancellationToken))
            throw new CommerceException(409, "manual_refund_bank_void_conflict", "A bank void already confirms or reserves this credit.");
    }
    private async Task<ManualRefund> FindAsync(Guid id, CancellationToken cancellationToken) => await db.ManualRefunds.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Environment == commerce.Environment && x.MerchantId == Merchant && x.BillerNo == Biller, cancellationToken) ?? throw Missing();
    private async Task<BankTransaction> CreditAsync(Guid id, CancellationToken cancellationToken) => await db.BankTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Environment == commerce.Environment && x.MerchantId == Merchant && x.BillerNo == Biller, cancellationToken) ?? throw Missing();
    private async Task<Order?> OrderLockedAsync(Guid? id, CancellationToken cancellationToken) => id is null ? null : (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {id.Value} FOR UPDATE").ToListAsync(cancellationToken)).Single();
    private Task<int> OperationLockAsync(string key, CancellationToken cancellationToken) => LockAsync("finance-operation:" + key, cancellationToken);
    private Task<int> InstructionLockAsync(string instruction, CancellationToken cancellationToken) => LockAsync($"bank:{commerce.Environment}:{Merchant}:{instruction}", cancellationToken);
    private Task<int> LockAsync(string key, CancellationToken cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    private async Task<ManualRefundView?> PriorAsync(string key, string hash, CancellationToken cancellationToken)
    {
        var prior = await db.FinanceOperations.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == key, cancellationToken);
        if (prior is null) return null;
        if (prior.RequestHash != hash || !prior.Action.StartsWith("manual-refund.", StringComparison.Ordinal)) throw new CommerceException(409, "operation_conflict", "This operation ID refers to different input.");
        return JsonSerializer.Deserialize<ManualRefundView>(prior.OutcomeJson, CatalogService.Json) ?? throw new InvalidOperationException("Invalid manual operation snapshot.");
    }
    private void Record(string key, string hash, string action, string reason, ShopperActor actor, ManualRefund row, string before, ManualRefundView result)
    {
        db.FinanceOperations.Add(new FinanceOperation { CaseId = row.CaseId, OrderId = row.OrderId, OperationKey = key, RequestHash = hash, Action = action, Actor = actor.Key, Reason = reason.Trim(), BeforeJson = before, OutcomeJson = JsonSerializer.Serialize(result, CatalogService.Json), CreatedAt = clock.GetUtcNow() });
        db.AuditEvents.Add(new AuditEvent { Actor = actor.Key, Action = action, Resource = $"manual-refund:{row.Id}", DataJson = JsonSerializer.Serialize(new { operationKey = key, before, after = result, source = "ManualExternalProcess", noBankRequest = true }), CreatedAt = clock.GetUtcNow() });
    }
    private static string Key(string value) => Guid.TryParseExact(value, "D", out var key) ? key.ToString("D") : throw new CommerceException(400, "operation_id_invalid", "Provide a stable UUID operation ID.");
    private static void Reason(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 500 || value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) throw new CommerceException(400, "reason_required", "Provide a reason of at most500 characters."); }
    private void Finance(ShopperActor actor)
    {
        if (!actor.HasRole("finance")) throw new CommerceException(403, "role_required", "Finance permission is required.");
        if (!(commerce.Environment == CommerceEnvironment.Local && commerce.PaymentMode == PaymentMode.Mock || commerce.Environment is CommerceEnvironment.Uat or CommerceEnvironment.Production && commerce.PaymentMode == PaymentMode.Bank)) throw Missing();
    }
    private static CommerceException Missing() => new(404, "not_found", "The credit or manual refund case was not found.");
}
