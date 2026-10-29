using System;
using System.Collections.Generic;
using System.Linq;

namespace DuckovSharedBike.Domain
{
    public readonly struct CashStack
    {
        public int InstanceId { get; }
        public int Count { get; }
        public CashStack(int instanceId,int count) { InstanceId=instanceId; Count=count; }
    }

    public sealed class CashInventorySnapshot
    {
        private readonly Dictionary<string,CashStack[]> _containers=new Dictionary<string,CashStack[]>(StringComparer.Ordinal);
        public void Add(string container,IEnumerable<CashStack> stacks)
        {
            if (string.IsNullOrWhiteSpace(container) || stacks==null) throw new ArgumentException("Invalid cash container.");
            var copy=stacks.OrderBy(s=>s.InstanceId).ToArray();
            if (copy.Any(s=>s.Count<0) || copy.Select(s=>s.InstanceId).Distinct().Count()!=copy.Length)
                throw new InvalidOperationException("Invalid cash stack data.");
            _containers.Add(container,copy);
        }
        public void Verify(CashInventorySnapshot actual)
        {
            if (actual==null || _containers.Count!=actual._containers.Count)
                throw new InvalidOperationException("Cash save containers do not match.");
            foreach (var expected in _containers)
                if (!actual._containers.TryGetValue(expected.Key,out var stacks) || !expected.Value.SequenceEqual(stacks))
                    throw new InvalidOperationException("Cash inventory was not committed: "+expected.Key);
        }
    }
}
