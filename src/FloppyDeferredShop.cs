using UnityEngine;

namespace Horse.Floppy;

// A late-joining player can receive the vanilla shop RPC before the host's
// setting snapshot. Wait for that snapshot instead of choosing the wrong shop.
internal sealed class FloppyDeferredShop : MonoBehaviour
{
    private OuroStoreRoom shop;
    private OuroObjective objective;
    private OuroItemPositions positions;
    private OuroRoom room;
    private int seed;
    private bool pending;

    internal void Defer(OuroStoreRoom owner, OuroObjective source, OuroItemPositions itemPositions, OuroRoom sourceRoom, int shopSeed)
    {
        shop = owner; objective = source; positions = itemPositions; room = sourceRoom; seed = shopSeed;
        pending = true;
        owner.gameObject.SetActive(true);
    }
    internal void Clear() => pending = false;
    internal static void DisposeAll()
    {
        // Called after Harmony is removed: finish suppressed vanilla Setup
        // so unloading during a late join cannot leave an empty shop behind.
        foreach (var request in Object.FindObjectsByType<FloppyDeferredShop>(FindObjectsInactive.Include))
        {
            bool restore = request.pending;
            request.pending = false;
            if (restore && request.shop != null && request.objective != null &&
                !request.objective.HasEnded && GameManager.Instance != null)
            {
                try { request.shop.Setup(request.objective, request.positions, request.room, request.seed); }
                catch (System.Exception error) { FloppyPlugin.Log?.LogWarning("Could not restore deferred shop: " + error); }
            }
            Destroy(request);
        }
    }
    private void Update()
    {
        if (!pending || !FloppyAvailability.IsSynchronized) return;
        pending = false;
        if (shop != null && objective != null) shop.Setup(objective, positions, room, seed);
    }
    private void OnDisable() => pending = false;
}
