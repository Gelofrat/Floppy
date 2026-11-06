using UnityEngine;

namespace Horse.Floppy;

// The source geometry is baked once at shop scale. BuyableUpgrade's shelf
// scales its Engram by .5; the larger dropped collectable uses 1.3316923.
// Match assets/model-spec.json without applying another scale to the root.
internal static class ModelMetrics
{
    internal const float BakedScale = .5f / 1.3316923f;
    internal const float ModuleScale = .5f;
    internal const float TrayTravel = 2.05f * BakedScale;
    internal static readonly Vector3 SocketCenter = new Vector3(0, 2.59f, .37f) * BakedScale;
    internal static readonly Vector3 CaseCenter = new Vector3(0, 4.82f * .5f, 0) * BakedScale;
    internal static readonly Vector3 CaseSize = new Vector3(2.45f, 4.82f, 2.8f) * BakedScale;
    internal static readonly Vector3 TrayCenter = new Vector3(0, 2.51f, 1.49f) * BakedScale;
    internal static readonly Vector3 TraySize = new Vector3(1.96f, 1.74f, .15f) * BakedScale;
    internal const float EmitterHeight = 2.4f * BakedScale;
    internal const float WaypointHeight = 3f * BakedScale;
}
