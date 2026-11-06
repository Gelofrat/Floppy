using System;
using System.Collections.Generic;

namespace Horse.Floppy;

// One registry belongs to a local player's station. Disabling its Unity object
// must not reset the successful exchanges already recorded for that visit.
internal sealed class FloppyVisitState
{
    internal const int Limit = 3;
    internal sealed class Visit
    {
        internal int Used { get; private set; }
        internal int Remaining => Limit - Used;
        internal bool IsExhausted => Used >= Limit;
        internal void RecordSuccess()
        {
            if (IsExhausted) throw new InvalidOperationException("Floppy visit has no exchanges remaining.");
            Used++;
        }
    }
    private readonly Dictionary<string, Visit> visits = new Dictionary<string, Visit>();
    internal string Key { get; private set; }
    internal Visit Current { get; private set; }

    internal bool CanKeep(string key, bool active, bool powered, bool busy)
        => Key == key && Current != null && active && (powered || busy || Current.IsExhausted);

    internal Visit Enter(string key)
    {
        Key = key;
        if (!visits.TryGetValue(key, out var visit)) visits.Add(key, visit = new Visit());
        Current = visit;
        return visit;
    }
}
