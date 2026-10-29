using System;
using System.Collections.Generic;
using System.Linq;

namespace DuckovSharedBike.Domain
{
    public enum AdjustmentKind { Add = 0, PercentageAdd = 100, PercentageMultiply = 200 }

    public readonly struct StatAdjustment
    {
        public AdjustmentKind Kind { get; }
        public float Value { get; }
        public int Order { get; }
        public bool FromWeight { get; }
        public StatAdjustment(AdjustmentKind kind, float value, int order, bool fromWeight)
        { Kind = kind; Value = value; Order = order; FromWeight = fromWeight; }
    }

    public static class MovementMath
    {
        // Mirrors the inspected native Stat grouping order; removes only weight-origin adjustments.
        // Does not mutate the native stat, buff, modifier list, or inventory.
        public static float WithoutWeight(float baseValue, IEnumerable<StatAdjustment> adjustments)
        {
            if (float.IsNaN(baseValue) || float.IsInfinity(baseValue)) throw new ArgumentOutOfRangeException(nameof(baseValue));
            var value = baseValue;
            float percentageSum = 0;
            var grouping = false;
            var previousOrder = int.MinValue;
            foreach (var adjustment in adjustments.Where(a => !a.FromWeight).OrderBy(a => a.Order))
            {
                if (float.IsNaN(adjustment.Value) || float.IsInfinity(adjustment.Value)) throw new ArgumentOutOfRangeException(nameof(adjustments));
                if (grouping && (adjustment.Order != previousOrder || adjustment.Kind != AdjustmentKind.PercentageAdd))
                {
                    value *= Math.Max(0, 1 + percentageSum); percentageSum = 0; grouping = false;
                }
                previousOrder = adjustment.Order;
                switch (adjustment.Kind)
                {
                    case AdjustmentKind.Add: value += adjustment.Value; break;
                    case AdjustmentKind.PercentageAdd: grouping = true; percentageSum += adjustment.Value; break;
                    case AdjustmentKind.PercentageMultiply: value *= Math.Max(0, 1 + adjustment.Value); break;
                    default: throw new ArgumentOutOfRangeException(nameof(adjustments));
                }
            }
            if (grouping) value *= Math.Max(0, 1 + percentageSum);
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArithmeticException("Movement stat is not finite.");
            return value;
        }
    }
}
