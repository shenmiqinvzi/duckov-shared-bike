using System;

namespace DuckovSharedBike.Domain
{
    // Samples a monotonic runtime clock. No DateTime or saved wall-clock timestamp is used.
    public sealed class ActivePlayClock
    {
        private decimal? _previousMs;
        private PlayContext _previousContext;
        public decimal Sample(decimal nowMs, PlayContext context)
        {
            if (nowMs < 0) throw new ArgumentOutOfRangeException(nameof(nowMs));
            if (!Enum.IsDefined(typeof(PlayContext), context)) throw new ArgumentOutOfRangeException(nameof(context));
            if (_previousMs.HasValue && nowMs < _previousMs.Value)
                throw new ArgumentOutOfRangeException(nameof(nowMs), "Clock must be monotonic.");
            var elapsed = _previousMs.HasValue && _previousContext == PlayContext.Active && context == PlayContext.Active
                ? nowMs - _previousMs.Value : 0;
            _previousMs = nowMs;
            _previousContext = context;
            return elapsed;
        }
        public void Reset() { _previousMs = null; _previousContext = PlayContext.Offline; }
    }
}
