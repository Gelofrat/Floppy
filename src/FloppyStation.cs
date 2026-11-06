using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Horse.Floppy;

public sealed class FloppyStation : MonoBehaviour, IInteractable, ISelectable
{
    private readonly FloppyVisitState visits = new FloppyVisitState();
    private readonly List<GameObject> hiddenVanilla = new List<GameObject>();
    private FloppyVisitState.Visit visit;
    private OuroStoreRoom shop;
    private OuroObjective objective;
    private int seed;
    private Transform tray;
    private Transform carriage;
    private Transform socket;
    private PcSpeaker speaker;
    private Renderer powerLed;
    private Renderer readLed;
    private GameObject preview;
    private FloppyShopHint shopHint;
    private UpgradeInstance pending;
    private bool busy;
    private bool reading;
    private bool powered;

    // Once a module is selected, the shop hover card owns hold confirmation.
    public bool HoldToInteract => false;
    public bool IsInspectable => false;
    public bool EndInteractOnFocusLoss => false;
    public InputAction InteractBinding => PlayerInput.Controls.Player.Interact;
    public bool UsesInteractBinding => true;
    public bool EnableInteractableHighlighting => pending == null && CanInteract(Player.LocalPlayer);
    public bool DrawWithInteractableMaterial => true;
    public float Health => 1;
    public float NormalizedHealth => 1;
    public float LastSelectedTime => 0;
    public bool Exists() => this != null;
    public void EndInteract(Player player) { }
    public float HoldToInteractDuration => .65f;
    public bool ShowHighlightVisual => false;
    public bool EnableHighlighting => visit != null;
    public List<TargetMesh> Meshes => null;
    public string InteractText => "Floppy";
    internal UpgradeInstance SelectedModule => pending;
    internal bool CanConfirmExchange => pending != null && CanInteract(Player.LocalPlayer);
    internal string HintText => "Attempts: " + (visit?.Remaining ?? FloppyVisitState.Limit) + "/" + FloppyVisitState.Limit;
    internal bool CanCancelSelection => pending != null && !busy;

    internal void Setup(OuroStoreRoom owner, OuroObjective source, OuroItemPositions positions, OuroRoom room, int shopSeed)
    {
        string key = source.GetEntityId() + ":" + shopSeed + ":" +
            (room != null ? room.GetEntityId().ToString() : positions != null ? positions.GetEntityId().ToString() : "0");
        if (visits.CanKeep(key, gameObject.activeInHierarchy, powered, busy)) return;
        // A menu opened by the previous appearance must not target this one.
        FloppyInventorySelection.Cancel(this);
        StopAllCoroutines();
        if (speaker != null) PcAudio.Stop(speaker);
        ClearPreview();
        pending = null;
        busy = false;
        reading = false;
        shop = owner;
        objective = source;
        seed = shopSeed;
        visit = visits.Enter(key);
        if (shopHint == null)
        {
            var hintObject = new GameObject("Floppy_ShopHint");
            hintObject.transform.SetParent(transform, false);
            hintObject.transform.localPosition = Vector3.up * ModelMetrics.EmitterHeight;
            shopHint = hintObject.AddComponent<FloppyShopHint>();
            shopHint.Owner = this;
        }
        if (tray == null)
        {
            tray = transform.Find("Tray");
            carriage = transform.Find("RailCarriage");
            socket = tray.Find("ModuleSocket");
            speaker = gameObject.AddComponent<PcSpeaker>();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == "PowerLED_Mesh") powerLed = renderer;
                if (renderer.name == "ReadLED_Mesh") readLed = renderer;
            }
        }
        Vector3 position;
        Quaternion rotation;
        if (room != null)
        {
            position = room.transform.position;
            rotation = room.transform.rotation;
        }
        else if (positions != null && positions.Positions.Length > 0)
            positions.Positions[0].GetGlobalPositionAndRotation(positions.transform, out position, out rotation);
        else throw new InvalidOperationException("Shop has no item position.");
        // Keep the authored front direction while seating every foot on the
        // floor plane. A yaw-only rotation leaves the case floating on slopes.
        if (Physics.Raycast(position + Vector3.up * 2, Vector3.down, out var floor, 6f, 8193,
                QueryTriggerInteraction.Ignore))
        {
            var forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, floor.normal);
            if (forward.sqrMagnitude < .0001f)
                forward = Vector3.ProjectOnPlane(rotation * Vector3.up, floor.normal);
            rotation = Quaternion.LookRotation(forward, floor.normal);
            position = floor.point;
        }
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = Vector3.one;
        HideVanilla(owner);
        owner.gameObject.SetActive(true);
        gameObject.SetActive(true);
        tray.localPosition = Vector3.zero;
        carriage.localPosition = Vector3.zero;
        var sprite = AccessTools.Field(typeof(OuroStoreRoom), "waypointSprite")?.GetValue(owner) as Sprite;
        if (sprite != null && Highlighter.Instance != null)
            Highlighter.Instance.AddWaypointPing(owner.transform, sprite, new Color(.2f,.85f,.62f)).localPosition
                = owner.transform.InverseTransformPoint(position + Vector3.up * ModelMetrics.WaypointHeight);
        StartCoroutine(PowerOn());
    }

    private void HideVanilla(OuroStoreRoom owner)
    {
        foreach (string name in new[] { "shelves", "upgrades" })
        {
            var items = AccessTools.Field(typeof(OuroStoreRoom), name)?.GetValue(owner) as IEnumerable;
            if (items == null) continue;
            foreach (var item in items)
            {
                var go = item as GameObject ?? (item as Component)?.gameObject;
                if (go != null && go.activeSelf)
                {
                    hiddenVanilla.Add(go);
                    go.SetActive(false);
                }
            }
        }
    }

    internal void RestoreVanilla()
    {
        foreach (var go in hiddenVanilla) if (go != null) go.SetActive(true);
        hiddenVanilla.Clear();
    }

    private IEnumerator PowerOn()
    {
        busy = true;
        powered = !visit.IsExhausted;
        if (powered)
        {
            Play("power_on");
            yield return new WaitForSeconds(1.6f);
            speaker.StartIdle();
            yield return Slide(ModelMetrics.TrayTravel);
        }
        busy = false;
    }

    public void Interact(Player player)
    {
        if (!CanInteract(player)) return;
        if (pending != null)
        {
            if (!ExchangeRules.CanSubmit(pending, out var reason))
            {
                Notice(reason); pending = null; ClearPreview(); return;
            }
            if (!PlayerData.Instance.CanAffordCost(ExchangeRules.Cost(pending)))
            {
                Notice("Not enough currency for this exchange."); return;
            }
            // Game-owned filter preserves the world pool's eligibility rules.
            var output = ExchangeRules.Prepare(pending, unchecked(seed + visit.Used * 104729 + pending.InstanceID));
            if (output == null)
            {
                Notice("No other module of this rarity is available for this gear."); return;
            }
            StartCoroutine(Exchange(pending, output));
            return;
        }
        FloppyInventorySelection.Open(this, Select);
    }

    private bool CanInteract(Player player)
        => !busy && powered && visit != null && !visit.IsExhausted && player != null && player == Player.LocalPlayer
            && player.IsAlive && objective != null && !objective.HasEnded && !objective.ContinuingToNextLevel;

    private void Select(UpgradeInstance input)
    {
        if (!CanInteract(Player.LocalPlayer)) return;
        if (!ExchangeRules.CanSubmit(input, out var reason)) { Notice(reason); return; }
        pending = input;
        ShowModule(input);
    }

    private IEnumerator Exchange(UpgradeInstance input, UpgradeInstance output)
    {
        busy = true;
        pending = null;
        yield return new WaitForSeconds(.3f);
        yield return Slide(0);
        reading = true;
        Play("disk_read");
        yield return new WaitForSeconds(1.9f);
        reading = false;
        string reason = "Exchange cancelled: the stage has ended.";
        if (CanFinish()) FloppyExchangeDiagnostics.LogState("before", input, output);
        if (!CanFinish() || !ExchangeRules.Commit(input, output, out reason))
        {
            if (CanFinish()) FloppyExchangeDiagnostics.LogState("cancelled", input, output);
            Notice(CanFinish() ? reason : "Exchange cancelled: the stage has ended.");
            ClearPreview();
            yield return Slide(ModelMetrics.TrayTravel);
            busy = false;
            yield break;
        }
        FloppyExchangeDiagnostics.LogState("after", input, output);
        visit.RecordSuccess();
        ShowModule(output);
        Play("complete");
        yield return Slide(ModelMetrics.TrayTravel);
        Notice("Received: " + output.Upgrade.GetInstanceName(output) + ".");
        yield return new WaitForSeconds(1.8f);
        ClearPreview();
        if (visit.IsExhausted)
        {
            yield return Slide(0);
            speaker.StopIdle();
            Play("power_off");
            yield return new WaitForSeconds(1.35f);
            powered = false;
        }
        busy = false;
    }

    private bool CanFinish()
        => objective != null && !objective.HasEnded && !objective.ContinuingToNextLevel
            && Player.LocalPlayer != null && Player.LocalPlayer.IsAlive;

    private IEnumerator Slide(float distance)
    {
        Play("tray");
        float start = tray.localPosition.z;
        for (float t = 0; t < .75f; t += Time.deltaTime)
        {
            tray.localPosition = Vector3.forward * Mathf.Lerp(start, distance, Mathf.SmoothStep(0,1,t/.75f));
            carriage.localPosition = tray.localPosition * .5f;
            yield return null;
        }
        tray.localPosition = Vector3.forward * distance;
        carriage.localPosition = tray.localPosition * .5f;
    }

    private void ShowModule(UpgradeInstance module)
    {
        ClearPreview();
        var mesh = Global.Instance.UpgradeEngramMesh;
        preview = new GameObject("Floppy_ModulePreview");
        preview.transform.SetParent(socket, false);
        preview.transform.localScale = Vector3.one * ModelMetrics.ModuleScale;
        // Mesh pivot is offset; center its actual bounds in the receiving bay.
        preview.transform.localPosition = -mesh.bounds.center * ModelMetrics.ModuleScale;
        preview.AddComponent<MeshFilter>().sharedMesh = mesh;
        preview.AddComponent<MeshRenderer>().sharedMaterial = Global.GetRarity(module.Upgrade.Rarity).collectableMat;
    }

    private void ClearPreview()
    {
        if (preview != null) Destroy(preview);
        preview = null;
    }
    private void Play(string name) => speaker.Play(name);
    private void Notice(string text) => FloppyPlugin.Log.LogDebug(text);
    internal void CancelSelection()
    {
        if (!CanCancelSelection) return;
        pending = null;
        ClearPreview();
    }
    private void Update()
    {
        if (shopHint != null && shopHint.CancelPressed) CancelSelection();
        SetLed(powerLed, powered ? new Color(.19f,.84f,.39f) : Color.black);
        SetLed(readLed, reading && Mathf.FloorToInt(Time.time * 15) % 3 != 0 ? new Color(1,.4f,.045f) : Color.black);
    }
    private static void SetLed(Renderer renderer, Color color)
    {
        if (renderer == null) return;
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetColor("_EmissionColor", color * 2);
        block.SetFloat("_Emission", color.maxColorComponent > 0 ? 2f : 0f);
        block.SetFloat("_UseEmission", color.maxColorComponent > 0 ? 1f : 0f);
        block.SetColor("_ShadowColor", color);
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
    }
    private void OnDisable()
    {
        FloppyInventorySelection.Cancel(this);
        StopAllCoroutines();
        ClearPreview();
        if (tray != null) tray.localPosition = Vector3.zero;
        if (carriage != null) carriage.localPosition = Vector3.zero;
        pending = null;
        reading = false;
        busy = false;
        powered = false;
        if (speaker != null) PcAudio.Stop(speaker);
    }
    private void OnDestroy()
    {
        if (shop != null && Highlighter.Instance != null) Highlighter.Instance.RemovePing(shop.transform);
    }
}
