using System;
using System.Collections.Generic;

namespace DuckovSharedBike.Domain
{
    public enum RentalStatus { Active, Closed }
    public enum RideState { Parked, Riding }
    public enum CloseReason { Returned, DeathRecovery, ReportedLost, SystemRecovery }
    public enum PaymentStatus { Unpaid, Pending, Paid }
    public enum PlayContext { Active, Paused, Loading, Offline }
    public enum PlayerAction { Move, Sprint, Aim, Shoot, Melee, Throw, AttackSkill, UseConsumable, InspectMap, InspectBackpack, Loot, Pickup, Drop, Talk, WorldInteract, ReturnBike, Dismount }
    public enum RuleCode
    {
        Success, ActiveRentalExists, UnpaidOrders, NoActiveRental, WrongVehicle,
        VehicleCreationFailed, UnknownRental, UnknownOrder, AlreadyPaid,
        InsufficientCash, PaymentBusy, WrongPaymentAttempt, PaymentDeclined,
        PaymentUncertain, PersistenceFailed
    }

    public sealed class RatePlan
    {
        public int HourlyRate { get; set; } = 200;
        public int DeathPenalty { get; set; } = 2000;
        public int ReportFee { get; set; } = 2000;
        public RatePlan Copy() => new RatePlan { HourlyRate = HourlyRate, DeathPenalty = DeathPenalty, ReportFee = ReportFee };
        public void Validate()
        {
            if (HourlyRate <= 0 || DeathPenalty < 0 || ReportFee < 0)
                throw new ArgumentOutOfRangeException(nameof(RatePlan), "Rates must be positive; penalties cannot be negative.");
        }
    }

    public sealed class RentalRecord
    {
        public string RentalId { get; set; } = "";
        public string VehicleId { get; set; } = "";
        public string RentStationId { get; set; } = "";
        public string? ReturnStationId { get; set; }
        public RentalStatus Status { get; set; }
        public RideState RideState { get; set; }
        public long ElapsedActiveMs { get; set; }
        public decimal FractionalMs { get; set; }
        public RatePlan Rates { get; set; } = new RatePlan();
        public CloseReason? CloseReason { get; set; }
        public string? OrderId { get; set; }
        public string? StartedUtc {get;set;}
        public string? ClosedUtc {get;set;}
        public RentalRecord Copy() => new RentalRecord
        {
            RentalId = RentalId, VehicleId = VehicleId, RentStationId = RentStationId,
            ReturnStationId = ReturnStationId, Status = Status, RideState = RideState,
            ElapsedActiveMs = ElapsedActiveMs, FractionalMs = FractionalMs,
            Rates = Rates.Copy(), CloseReason = CloseReason, OrderId = OrderId,StartedUtc=StartedUtc,ClosedUtc=ClosedUtc
        };
    }

    public sealed class OrderRecord
    {
        public string OrderId { get; set; } = "";
        public string RentalId { get; set; } = "";
        public long DurationMs { get; set; }
        public decimal FractionalMs { get; set; }
        public long BilledHours { get; set; }
        public long RentFee { get; set; }
        public long AdditionalFee { get; set; }
        public long Total { get; set; }
        public CloseReason CloseReason { get; set; }
        public PaymentStatus PaymentStatus { get; set; }
        public string? PaymentOperationId { get; set; }
        public string? PaidUtc {get;set;}
        public OrderRecord Copy() => (OrderRecord)MemberwiseClone();
    }

    public sealed class RentalState
    {
        public int SchemaVersion { get; set; } = 1;
        public string SaveId { get; set; } = "";
        public List<RentalRecord> Rentals { get; set; } = new List<RentalRecord>();
        public List<OrderRecord> Orders { get; set; } = new List<OrderRecord>();
        public RentalState Copy()
        {
            var result = new RentalState { SchemaVersion = SchemaVersion, SaveId = SaveId };
            foreach (var rental in Rentals) result.Rentals.Add(rental.Copy());
            foreach (var order in Orders) result.Orders.Add(order.Copy());
            return result;
        }
    }

    public sealed class RuleResult
    {
        public RuleCode Code { get; }
        public string? Id { get; }
        public bool Succeeded => Code == RuleCode.Success;
        public RuleResult(RuleCode code, string? id = null) { Code = code; Id = id; }
    }

    public readonly struct FeeQuote
    {
        public long Hours { get; }
        public long RentFee { get; }
        public long AdditionalFee { get; }
        public long Total => checked(RentFee + AdditionalFee);
        public FeeQuote(long hours, long rentFee, long additionalFee)
        { Hours = hours; RentFee = rentFee; AdditionalFee = additionalFee; }
    }

    public static class Billing
    {
        public const long HourMs = 3_600_000;
        public static FeeQuote Quote(long elapsedMs, decimal fractionalMs, RatePlan rates, CloseReason reason)
        {
            rates.Validate();
            if (elapsedMs < 0 || fractionalMs < 0 || fractionalMs >= 1)
                throw new ArgumentOutOfRangeException(nameof(elapsedMs));
            if (!Enum.IsDefined(typeof(CloseReason), reason)) throw new ArgumentOutOfRangeException(nameof(reason));
            var hours = elapsedMs / HourMs;
            if (elapsedMs % HourMs != 0 || fractionalMs > 0) hours++;
            hours = Math.Max(1, hours);
            var extra = reason == CloseReason.DeathRecovery ? rates.DeathPenalty
                : reason == CloseReason.ReportedLost ? rates.ReportFee : 0;
            var quote = new FeeQuote(hours, checked(hours * rates.HourlyRate), extra);
            _ = quote.Total;
            return quote;
        }
    }
}
