using System;
using System.Collections.Generic;
using System.Linq;

namespace DuckovSharedBike.Domain
{
    public sealed class PersistenceSettings
    {
        // A full native save (character + inventory + file replace + read-back) is expensive; 5 seconds
        // was too aggressive. Key events still persist immediately.
        public int FullSaveSeconds { get; set; } = 20;

        public void Validate()
        {
            if (FullSaveSeconds < 5 || FullSaveSeconds > 300)
                throw new InvalidOperationException("FullSaveSeconds must be between 5 and 300.");
        }
    }

    public sealed class UiSettings
    {
        public string InvoicePanelSide { get; set; } = "right";
        public bool ShowWorldLabels { get; set; } = true;
        public bool Sounds { get; set; } = true;

        public void Validate()
        {
            var side = (InvoicePanelSide ?? "").Trim().ToLowerInvariant();
            if (side != "left" && side != "right")
                throw new InvalidOperationException("InvoicePanelSide must be left or right.");
        }
    }

    public sealed class KeyBindingSettings
    {
        public string Station { get; set; } = "F6";
        public string Mount { get; set; } = "F";
        public string Loss { get; set; } = "F8";
        public string Diagnostics { get; set; } = "F9";
        public string Snapshot { get; set; } = "F10";
        public string Summon { get; set; } = "F5";

        internal IEnumerable<string> All()
        {
            yield return Station;
            yield return Mount;
            yield return Loss;
            yield return Diagnostics;
            yield return Snapshot;
            yield return Summon;
        }

        public void Validate()
        {
            var names = All().Select(name => (name ?? "").Trim()).ToArray();
            if (names.Any(string.IsNullOrEmpty))
                throw new InvalidOperationException("Key bindings cannot be empty.");
            if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
                throw new InvalidOperationException("Key bindings must be unique.");
        }
    }
}
