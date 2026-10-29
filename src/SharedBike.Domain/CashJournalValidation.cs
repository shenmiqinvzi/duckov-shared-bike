using System;
using System.Collections.Generic;
using System.Linq;

namespace DuckovSharedBike.Domain
{
    public static class CashJournalValidation
    {
        public static void Validate(IReadOnlyList<CashJournalEntry> receipts, RentalState state)
        {
            if (receipts == null) throw new InvalidOperationException("Cash receipt list is missing.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var receipt in receipts)
            {
                if (receipt == null || receipt.Amount <= 0 || string.IsNullOrWhiteSpace(receipt.OperationId)
                    || !ids.Add(receipt.OperationId) || !Enum.IsDefined(typeof(CashDecision), receipt.Decision)
                    || receipt.CashBefore < 0 || receipt.CashAfter < 0)
                    throw new InvalidOperationException("Invalid cash receipt data.");
                var delta = receipt.CashBefore - receipt.CashAfter;
                if ((receipt.Decision == CashDecision.Charged && delta != receipt.Amount)
                    || (receipt.Decision == CashDecision.Declined && delta != 0))
                    throw new InvalidOperationException("Cash receipt disagrees with its recorded balance change.");
                var order = state.Orders.SingleOrDefault(o => o.PaymentOperationId == receipt.OperationId);
                // Declined operations can outlive the order's cleared operation ID.
                if (order == null && receipt.Decision != CashDecision.Declined)
                    throw new InvalidOperationException("Cash receipt has no matching payment operation.");
                if (order != null && (order.Total != receipt.Amount
                    || (order.PaymentStatus == PaymentStatus.Paid && receipt.Decision != CashDecision.Charged)))
                    throw new InvalidOperationException("Cash receipt disagrees with its order.");
            }
        }
    }
}
