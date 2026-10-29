using System;
using System.Linq;

namespace DuckovSharedBike.Domain
{
    // Business rules have no Unity, disk or inventory dependencies. UTC stamps are display-only; billing uses active elapsed time.
    public sealed class RentalEngine
    {
        private RentalState _state;
        public string SaveId => _state.SaveId;
        private RentalRecord? Active => _state.Rentals.SingleOrDefault(r => r.Status == RentalStatus.Active);
        public RentalRecord? CurrentRental => Active?.Copy();
        public bool CanEvacuate => Active == null;
        public bool HasUnpaidOrders => _state.Orders.Any(o => o.PaymentStatus != PaymentStatus.Paid);
        public RuleCode RentalEligibility => Active != null ? RuleCode.ActiveRentalExists
            : HasUnpaidOrders ? RuleCode.UnpaidOrders : RuleCode.Success;

        public RentalEngine(string saveId)
        {
            RequireId(saveId, nameof(saveId));
            _state = new RentalState { SaveId = saveId };
        }

        public RentalState Snapshot() => _state.Copy();

        public void Restore(RentalState snapshot)
        {
            Validate(snapshot);
            if (snapshot.SaveId != SaveId) throw new InvalidOperationException("Cannot restore another save's rental data.");
            var copy = snapshot.Copy();
            // Riding is a scene-dependent posture. Restoring debt/ownership never assumes a safe mounted pose.
            foreach (var rental in copy.Rentals) rental.RideState = RideState.Parked;
            _state = copy;
        }

        internal void RestoreTransaction(RentalState snapshot)
        {
            Validate(snapshot);
            if (snapshot.SaveId != SaveId) throw new InvalidOperationException("Wrong save.");
            _state = snapshot.Copy();
        }

        public RuleResult Rent(string stationId, string vehicleId, RatePlan rates, bool vehicleCreated)
        {
            if (RentalEligibility != RuleCode.Success) return new RuleResult(RentalEligibility);
            RequireId(stationId, nameof(stationId));
            RequireId(vehicleId, nameof(vehicleId));
            rates.Validate();
            if (!vehicleCreated) return new RuleResult(RuleCode.VehicleCreationFailed);
            if (_state.Rentals.Any(r => r.VehicleId == vehicleId)) throw new InvalidOperationException("Vehicle IDs must be unique.");
            var rental = new RentalRecord
            {
                RentalId = Guid.NewGuid().ToString("N"), VehicleId = vehicleId,
                RentStationId = stationId, Rates = rates.Copy(), Status = RentalStatus.Active,
                RideState = RideState.Parked,StartedUtc=DisplayStamp()
            };
            _state.Rentals.Add(rental);
            return new RuleResult(RuleCode.Success, rental.RentalId);
        }

        public RuleResult SetRiding(string vehicleId, bool riding)
        {
            var rental = Active;
            if (rental == null) return new RuleResult(RuleCode.NoActiveRental);
            if (rental.VehicleId != vehicleId) return new RuleResult(RuleCode.WrongVehicle);
            rental.RideState = riding ? RideState.Riding : RideState.Parked;
            return new RuleResult(RuleCode.Success, rental.RentalId);
        }

        public void AdvanceTime(decimal elapsedMs, PlayContext context)
        {
            if (elapsedMs < 0) throw new ArgumentOutOfRangeException(nameof(elapsedMs));
            if (!Enum.IsDefined(typeof(PlayContext), context)) throw new ArgumentOutOfRangeException(nameof(context));
            var rental = Active;
            if (rental == null || context != PlayContext.Active) return;
            var combined = checked(elapsedMs + rental.FractionalMs);
            var whole = checked((long)decimal.Floor(combined));
            var total = checked(rental.ElapsedActiveMs + whole);
            var fraction = combined - whole;
            // Validate representable charges before committing the elapsed-time mutation.
            Billing.Quote(total, fraction, rental.Rates, CloseReason.Returned);
            rental.ElapsedActiveMs = total;
            rental.FractionalMs = fraction;
        }

        public FeeQuote? CurrentFee => Active == null ? (FeeQuote?)null
            : Billing.Quote(Active.ElapsedActiveMs, Active.FractionalMs, Active.Rates, CloseReason.Returned);

        public bool Allows(PlayerAction action)
        {
            if (!Enum.IsDefined(typeof(PlayerAction), action)) return false;
            if (Active?.RideState != RideState.Riding) return true;
            return action == PlayerAction.Move || action == PlayerAction.InspectMap
                || action == PlayerAction.InspectBackpack || action == PlayerAction.ReturnBike
                || action == PlayerAction.Dismount;
        }

        public RuleResult Close(string rentalId, CloseReason reason, string? returnStationId = null)
        {
            var rental = _state.Rentals.SingleOrDefault(r => r.RentalId == rentalId);
            if (rental == null) return new RuleResult(RuleCode.UnknownRental);
            if (rental.Status == RentalStatus.Closed) return new RuleResult(RuleCode.Success, rental.OrderId);
            if (reason == CloseReason.Returned) RequireId(returnStationId, nameof(returnStationId));
            var quote = Billing.Quote(rental.ElapsedActiveMs, rental.FractionalMs, rental.Rates, reason);
            var order = new OrderRecord
            {
                OrderId = "order-" + rental.RentalId, RentalId = rental.RentalId,
                DurationMs = rental.ElapsedActiveMs, FractionalMs = rental.FractionalMs,
                BilledHours = quote.Hours, RentFee = quote.RentFee, AdditionalFee = quote.AdditionalFee,
                Total = quote.Total, CloseReason = reason, PaymentStatus = PaymentStatus.Unpaid
            };
            _state.Orders.Add(order);
            rental.Status = RentalStatus.Closed;
            rental.RideState = RideState.Parked;
            rental.CloseReason = reason;
            rental.ReturnStationId = reason == CloseReason.Returned ? returnStationId : null;
            rental.OrderId = order.OrderId;
            rental.ClosedUtc=DisplayStamp();
            return new RuleResult(RuleCode.Success, order.OrderId);
        }

        public OrderRecord? FindOrder(string orderId) => _state.Orders.SingleOrDefault(o => o.OrderId == orderId)?.Copy();

        public RuleResult BeginPayment(string orderId, long availableCash)
        {
            var order = _state.Orders.SingleOrDefault(o => o.OrderId == orderId);
            if (order == null) return new RuleResult(RuleCode.UnknownOrder);
            if (order.PaymentStatus == PaymentStatus.Paid) return new RuleResult(RuleCode.AlreadyPaid, order.PaymentOperationId);
            // A pending operation may already have charged money before a crash; reuse its identity.
            if (order.PaymentStatus == PaymentStatus.Pending) return new RuleResult(RuleCode.Success, order.PaymentOperationId);
            if (_state.Orders.Any(o => o.PaymentStatus == PaymentStatus.Pending)) return new RuleResult(RuleCode.PaymentBusy);
            if (availableCash < order.Total) return new RuleResult(RuleCode.InsufficientCash);
            order.PaymentOperationId = Guid.NewGuid().ToString("N");
            order.PaymentStatus = PaymentStatus.Pending;
            return new RuleResult(RuleCode.Success, order.PaymentOperationId);
        }

        public RuleResult CompletePayment(string orderId, string operationId, bool charged)
        {
            var order = _state.Orders.SingleOrDefault(o => o.OrderId == orderId);
            if (order == null) return new RuleResult(RuleCode.UnknownOrder);
            if (order.PaymentOperationId != operationId) return new RuleResult(RuleCode.WrongPaymentAttempt);
            if (order.PaymentStatus == PaymentStatus.Paid) return new RuleResult(RuleCode.AlreadyPaid, operationId);
            if (order.PaymentStatus != PaymentStatus.Pending) return new RuleResult(RuleCode.WrongPaymentAttempt);
            order.PaymentStatus = charged ? PaymentStatus.Paid : PaymentStatus.Unpaid;
            order.PaidUtc=charged ? DisplayStamp() : null;
            if (!charged) order.PaymentOperationId = null;
            return new RuleResult(charged ? RuleCode.Success : RuleCode.PaymentDeclined, operationId);
        }

        private static void RequireId(string? id, string name)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A non-empty ID is required.", name);
        }

        private static string DisplayStamp()=>DateTimeOffset.UtcNow.ToString("O",System.Globalization.CultureInfo.InvariantCulture);

        private static void Validate(RentalState state)
        {
            if (state == null || state.SchemaVersion != 1 || state.Rentals == null || state.Orders == null)
                throw new InvalidOperationException("Unsupported or incomplete rental snapshot.");
            RequireId(state.SaveId, nameof(state.SaveId));
            if (state.Rentals.Any(r => r == null) || state.Orders.Any(o => o == null))
                throw new InvalidOperationException("Null records are invalid.");
            if (state.Rentals.Select(r => r.RentalId).Distinct().Count() != state.Rentals.Count
                || state.Rentals.Select(r => r.VehicleId).Distinct().Count() != state.Rentals.Count
                || state.Orders.Select(o => o.OrderId).Distinct().Count() != state.Orders.Count
                || state.Orders.Select(o => o.RentalId).Distinct().Count() != state.Orders.Count
                || state.Orders.Where(o => o.PaymentStatus != PaymentStatus.Unpaid).Select(o => o.PaymentOperationId).Distinct().Count()
                    != state.Orders.Count(o => o.PaymentStatus != PaymentStatus.Unpaid)
                || state.Rentals.Count(r => r.Status == RentalStatus.Active) > 1
                || state.Orders.Count(o => o.PaymentStatus == PaymentStatus.Pending) > 1)
                throw new InvalidOperationException("Duplicate identities or conflicting active operations.");
            foreach (var rental in state.Rentals)
            {
                RequireId(rental.RentalId, nameof(rental.RentalId));
                RequireId(rental.VehicleId, nameof(rental.VehicleId));
                RequireId(rental.RentStationId, nameof(rental.RentStationId));
                if (rental.Rates == null || !Enum.IsDefined(typeof(RentalStatus), rental.Status)
                    || !Enum.IsDefined(typeof(RideState), rental.RideState)) throw new InvalidOperationException("Invalid rental state.");
                Billing.Quote(rental.ElapsedActiveMs, rental.FractionalMs, rental.Rates, CloseReason.Returned);
                var orders = state.Orders.Where(o => o.RentalId == rental.RentalId).ToArray();
                if (rental.Status == RentalStatus.Active)
                {
                    if (rental.CloseReason != null || rental.OrderId != null || rental.ReturnStationId != null || orders.Length != 0)
                        throw new InvalidOperationException("An active rental cannot have a settlement.");
                }
                else
                {
                    if (rental.CloseReason == null || orders.Length != 1 || rental.OrderId != orders[0].OrderId)
                        throw new InvalidOperationException("Closed rental must have exactly one linked order.");
                    if (rental.RideState != RideState.Parked)
                        throw new InvalidOperationException("A closed rental cannot still be ridden.");
                    if (rental.CloseReason == CloseReason.Returned) RequireId(rental.ReturnStationId, nameof(rental.ReturnStationId));
                    else if (rental.ReturnStationId != null)
                        throw new InvalidOperationException("Recovery cannot be marked as a station return.");
                }
            }
            foreach (var order in state.Orders)
            {
                RequireId(order.OrderId, nameof(order.OrderId));
                var rental = state.Rentals.SingleOrDefault(r => r.RentalId == order.RentalId);
                if (rental == null || rental.Status != RentalStatus.Closed || rental.CloseReason != order.CloseReason
                    || rental.ElapsedActiveMs != order.DurationMs || rental.FractionalMs != order.FractionalMs)
                    throw new InvalidOperationException("Order does not match its rental.");
                var quote = Billing.Quote(order.DurationMs, order.FractionalMs, rental.Rates, order.CloseReason);
                if (order.BilledHours != quote.Hours || order.RentFee != quote.RentFee
                    || order.AdditionalFee != quote.AdditionalFee || order.Total != quote.Total)
                    throw new InvalidOperationException("Order amounts do not match the frozen contract.");
                if (!Enum.IsDefined(typeof(PaymentStatus), order.PaymentStatus)) throw new InvalidOperationException("Invalid payment state.");
                if (order.PaymentStatus == PaymentStatus.Unpaid && order.PaymentOperationId != null)
                    throw new InvalidOperationException("Unpaid order cannot retain a payment operation.");
                if (order.PaymentStatus != PaymentStatus.Unpaid) RequireId(order.PaymentOperationId, nameof(order.PaymentOperationId));
            }
            if (state.Rentals.Any(r => r.Status == RentalStatus.Active) && state.Orders.Any(o => o.PaymentStatus != PaymentStatus.Paid))
                throw new InvalidOperationException("Active rental cannot coexist with unpaid orders.");
        }
    }
}
