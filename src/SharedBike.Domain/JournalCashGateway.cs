using System;

namespace DuckovSharedBike.Domain
{
    public sealed class CashJournalEntry
    {
        public string OperationId { get; set; } = "";
        public long Amount { get; set; }
        public CashDecision Decision { get; set; }
        public long CashBefore { get; set; }
        public long CashAfter { get; set; }
    }
    public interface ICashMutationPort
    {
        long Balance { get; }
        bool Debit(long amount);
        IDisposable EnterMutation();
    }
    public interface ICashJournal
    {
        CashJournalEntry? Find(string operationId);
        void Add(CashJournalEntry entry);
        // Flush cash and receipts together; must include the prepared order in the same checkpoint.
        void Flush();
    }

    public sealed class JournalCashGateway : ICashGateway
    {
        private readonly ICashMutationPort _cash;
        private readonly ICashJournal _journal;
        private readonly Action<Exception>? _onInterrupted;
        public JournalCashGateway(ICashMutationPort cash,ICashJournal journal,Action<Exception>? onInterrupted=null)
        { _cash=cash; _journal=journal; _onInterrupted=onInterrupted; }
        public long AvailableCash => _cash.Balance;
        public CashReceipt Charge(string operationId,long amount)
        {
            if (string.IsNullOrWhiteSpace(operationId) || amount<=0) throw new ArgumentException("Invalid cash operation.");
            var existing=_journal.Find(operationId);
            if (existing!=null)
            {
                if (existing.Amount!=amount) throw new InvalidOperationException("Operation reused with another amount.");
                // A previous disk flush may have failed after an in-memory debit. Recommit before acknowledging it.
                _journal.Flush();
                return new CashReceipt(operationId,amount,existing.Decision);
            }
            using (_cash.EnterMutation())
            {
                var before=_cash.Balance;
                CashDecision decision;
                try
                {
                    var paid=_cash.Debit(amount);
                    var after=_cash.Balance;
                    decision=paid && before-after==amount ? CashDecision.Charged
                        : !paid && after==before ? CashDecision.Declined : CashDecision.Uncertain;
                }
                catch (Exception ex)
                {
                    _onInterrupted?.Invoke(ex);
                    var after=_cash.Balance;
                    decision=before-after==amount ? CashDecision.Charged
                        : after==before ? CashDecision.Declined : CashDecision.Uncertain;
                }
                var entry=new CashJournalEntry { OperationId=operationId,Amount=amount,Decision=decision,CashBefore=before,CashAfter=_cash.Balance };
                _journal.Add(entry);
                _journal.Flush();
                return new CashReceipt(operationId,amount,decision);
            }
        }
    }
}
