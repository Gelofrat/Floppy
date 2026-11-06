using Pigeon;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Horse.Floppy;

// BuyableUpgrade uses this same hover card and UnlockActionParams.Cost path.
// The native resource widget supplies currency icons, affordability and hold UI.
internal sealed class FloppyShopHint : HoverInfo
{
    internal FloppyStation Owner;
    private string shownText;
    private UpgradeInstance shownModule;
    private bool shownCanConfirm;
    private InputAction cancelAction;
    internal bool CancelPressed => cancelAction != null && cancelAction.WasPressedThisFrame();

    private void OnEnable()
    {
        // The menu map is disabled in the world. Use a private active copy for
        // the same Escape/gamepad-cancel binding shown by the native card.
        if (cancelAction == null) cancelAction = PlayerInput.Controls.Menu.Cancel.Clone();
        cancelAction.Enable();
    }

    public override bool CenterText => false;
    public override object Context => Owner != null ? (object)Owner.SelectedModule ?? Owner : this;

    public override bool HasAnchor(out AnchorParams anchor)
    {
        anchor = new AnchorParams
        {
            Pivot = new Vector2(.5f, 1f),
            Offset = new Vector2(0f, -110f),
            GamepadOnly = false
        };
        return true;
    }

    public override string GetInfo(HoverInfoDisplay display) => Owner != null ? Owner.HintText : "";
    public override bool GetTitle(out string title, out Color color)
    {
        title = "Floppy";
        color = Global.Instance.WhiteUIColor;
        return true;
    }
    public override bool GetSubtitle(out string subtitle, out Color color)
    {
        var module = Owner != null ? Owner.SelectedModule : null;
        subtitle = module != null ? module.Upgrade.GetInstanceName(module) : null;
        color = module != null ? module.Upgrade.Color : Color.white;
        return module != null;
    }
    public override Color GetOutlineColor(out Material material)
    {
        var module = Owner != null ? Owner.SelectedModule : null;
        // Some native rarity materials (ContrabandUI) cannot be masked. Null
        // lets each graphic use its standard UI material; rarity color stays.
        material = FloppyUiMaterial.Resolve(module != null ? module.UIMaterial : null);
        return module != null ? module.Upgrade.Color : Color.white;
    }
    public override bool GetPrimaryBinding(out InputAction binding, out string label)
    {
        binding = PlayerInput.Controls.Player.Interact;
        label = "Exchange";
        return Owner != null && Owner.CanConfirmExchange;
    }
    public override bool GetSecondaryBinding(out InputAction binding, out string label)
    {
        binding = cancelAction;
        label = "Cancel";
        return Owner != null && Owner.CanCancelSelection && cancelAction != null;
    }
    public override bool HasUnlockAction(out UnlockActionParams data)
    {
        data = default;
        if (Owner == null || !Owner.CanConfirmExchange) return false;
        data.Duration = .65f;
        data.Cost = ExchangeRules.Cost(Owner.SelectedModule);
        data.Action = PlayerInput.Controls.Player.Interact;
        data.UnlockLoop = Global.Instance.UnlockLoop;
        data.OnComplete = Confirm;
        return true;
    }
    private void Confirm()
    {
        if (Owner != null && Owner.CanConfirmExchange) Owner.Interact(Pigeon.Movement.Player.LocalPlayer);
    }
    private void Update()
    {
        var display = HoverInfoDisplay.Instance;
        if (display == null) return;
        bool focused = Owner != null && Highlighter.Instance != null &&
            object.ReferenceEquals(Highlighter.Instance.SelectedTarget, Owner) && Highlighter.Instance.IsSelectedTargetInInteractRange &&
            PlayerInput.IsPlayerEnabled && !PlayerInput.IsMenuEnabled;
        if (!focused || !PositionAnchor(display))
        {
            if (display.SelectedInfo == this && display.gameObject.activeSelf) display.Deactivate();
            return;
        }
        string text = Owner.HintText;
        var module = Owner.SelectedModule;
        bool canConfirm = Owner.CanConfirmExchange;
        if (display.SelectedInfo != this || !display.gameObject.activeSelf) display.ShowInfo(this);
        else if (shownText != text || shownModule != module || shownCanConfirm != canConfirm) display.Refresh();
        shownText = text;
        shownModule = module;
        shownCanConfirm = canConfirm;
    }
    private void LateUpdate()
    {
        var display = HoverInfoDisplay.Instance;
        if (display == null || display.SelectedInfo != this || !display.gameObject.activeSelf) return;
        if (!PlayerInput.IsPlayerEnabled || PlayerInput.IsMenuEnabled || !PositionAnchor(display)) display.Deactivate();
    }
    private bool PositionAnchor(HoverInfoDisplay display)
    {
        var canvas = display.GetComponentInParent<Canvas>();
        var camera = canvas != null ? canvas.worldCamera : null;
        if (camera == null || !camera.isActiveAndEnabled) camera = PlayerLook.LocalUICamera;
        if (camera == null || !camera.isActiveAndEnabled) return false;
        // The native card projects this transform and amplifies movement from
        // its initial screen position. Keep its own anchor at the reticle so
        // viewing the side of a large, nearby case cannot push it onto the HUD.
        transform.position = camera.ViewportToWorldPoint(new Vector3(.5f, .5f,
            Mathf.Max(1f, camera.nearClipPlane + 1f)));
        return true;
    }
    protected override void OnDisable()
    {
        cancelAction?.Disable();
        var display = HoverInfoDisplay.Instance;
        if (display != null && display.SelectedInfo == this) display.Deactivate();
        base.OnDisable();
    }
    private void OnDestroy() => cancelAction?.Dispose();
}
