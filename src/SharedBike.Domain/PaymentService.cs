using System;

namespace DuckovSharedBike.Domain
{
    public enum CashDecision { Charged, Declined, Uncertain }

    public sealed class CashReceipt
    {
        public string OperationId { get; }
        public long Amount { get; }
        public CashDecision Decision { get; }
        public CashReceipt(string operationId, long amount, CashDecision decision)
        { OperationId = operationId; Amount = amount; Decision = decision; }
    }

    public interface ICashGateway
    {
        long AvailableCash { get; }
        // Required contract: durable, idempotent debit + receipt for this operation ID.
        // A retry with the same ID must return the original result, never charge again.
        CashReceipt Charge(string operationId, long amount);
    }

    public interface IRentalStateStore
    {
        // Must atomically persist the whole snapshot or fail without replacing its previous value.
        void Save(RentalState snapshot);
    }

    public static class PaymentService
    {
        public static RuleResult Attempt(RentalEngine engine, string orderId, ICashGateway cash, IRentalStateStore store)
        {
            var original = engine.Snapshot();
            var existing = engine.FindOrder(orderId);
            if (existing == null) return new RuleResult(RuleCode.UnknownOrder);
            if (existing.PaymentStatus == PaymentStatus.Paid) return new RuleResult(RuleCode.AlreadyPaid, existing.PaymentOperationId);
            var begin = engine.BeginPayment(orderId, existing.PaymentStatus == PaymentStatus.Pending ? 0 : cash.AvailableCash);
            if (!begin.Succeeded) return begin;
            var prepared = engine.Snapshot();
            try { store.Save(prepared); }
            catch (Exception)
            {
                engine.RestoreTransaction(original);
                return new RuleResult(RuleCode.PersistenceFailed);
            }

            CashReceipt receipt;
            try { receipt = cash.Charge(begin.Id!, existing.Total); }
            catch (Exception) { return new RuleResult(RuleCode.PaymentUncertain, begin.Id); }
            if (receipt == null || receipt.OperationId != begin.Id || receipt.Amount != existing.Total
                || (receipt.Decision != CashDecision.Charged && receipt.Decision != CashDecision.Declined))
                return new RuleResult(RuleCode.PaymentUncertain, begin.Id);

            var completed = engine.CompletePayment(orderId, begin.Id!, receipt.Decision == CashDecision.Charged);
            try { store.Save(engine.Snapshot()); }
            catch (Exception)
            {
                // The debit may have succeeded. Retain the already-durable operation ID for recovery.
                engine.RestoreTransaction(prepared);
                return new RuleResult(RuleCode.PersistenceFailed, begin.Id);
            }
            return completed;
        }
    }
}
