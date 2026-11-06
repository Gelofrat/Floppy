using System;
using System.Reflection;
using HarmonyLib;
using Pigeon;
using Pigeon.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Horse.Floppy;

// Keep this independent of MissionData.additionalData: Ouroboros uses those bits
// for its saved room and threat, and rebuilding a continued mission must not reset it.
[HarmonyPatch(typeof(MissionInfoWindow), nameof(MissionInfoWindow.Setup))]
internal static class FloppyMissionTogglePatch
{
    private static readonly FieldInfo ToggleField = AccessTools.Field(typeof(MissionInfoWindow), "toggleButton");
    private static bool reportedError;

    private static void Prefix(MissionInfoWindow __instance)
    {
        __instance.GetComponent<FloppyMissionToggle>()?.RestoreNativeLayout();
    }

    private static void Postfix(MissionInfoWindow __instance)
    {
        var view = __instance.GetComponent<FloppyMissionToggle>();
        if (FloppyPlugin.Instance == null || !(__instance.Mission.Mission is OuroMission))
        {
            view?.Hide();
            return;
        }

        try
        {
            var nativeToggle = ToggleField?.GetValue(__instance) as DefaultButton;
            if (nativeToggle == null)
                throw new InvalidOperationException("The mission options toggle is unavailable.");
            if (view == null)
                view = __instance.gameObject.AddComponent<FloppyMissionToggle>();
            view.Setup(__instance, nativeToggle);
        }
        catch (Exception error)
        {
            view?.Hide();
            if (!reportedError)
            {
                reportedError = true;
                FloppyPlugin.Log.LogError("Could not add the Floppy mission option: " + error);
            }
        }
    }
}

internal sealed class FloppyMissionToggle : MonoBehaviour
{
    private MissionInfoWindow window;
    private DefaultButton nativeToggle;
    private DefaultButton button;
    private RectTransform nativeRect;
    private RectTransform buttonRect;
    private TextMeshProUGUI label;
    private Image icon;
    private CanvasGroup appearance;
    private Vector2 nativePosition;
    private bool hasNativePosition;
    private bool visible;
    private bool lastEnabled;
    private bool lastCanEdit;
    private bool lastAuthority;
    private int lastLanguage = -1;

    internal bool CanEdit => FloppyPlugin.Instance != null && FloppyAvailability.CanEdit &&
        (DropPod.Instance == null || !DropPod.Instance.IsLaunching);

    internal void Setup(MissionInfoWindow owner, DefaultButton template)
    {
        window = owner;
        nativeToggle = template;
        nativeRect = (RectTransform)template.transform;
        nativePosition = nativeRect.anchoredPosition;
        hasNativePosition = true;
        if (button == null)
            CreateButton(template);

        visible = true;
        PositionControls();
        RefreshState(force: true);
        button.gameObject.SetActive(true);
    }

    private void CreateButton(DefaultButton template)
    {
        button = Instantiate(template, template.transform.parent, false);
        button.gameObject.name = "FloppyMissionToggle";
        button.gameObject.SetActive(false);
        buttonRect = (RectTransform)button.transform;

        // Remove the serialized Continue callback as well as any runtime listeners.
        // RemoveAllListeners alone leaves serialized UnityEvent callbacks in place.
        button.OnHoverEnter = new UnityEvent();
        button.OnHoverExit = new UnityEvent();
        button.OnClickDown = new UnityEvent();
        button.OnClickUp = new UnityEvent();
        button.OnClickUp.AddListener(OnClick);

        label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        icon = button.SecondaryGraphic as Image;
        if (label == null || icon == null || button.MainGraphic == null)
            throw new InvalidOperationException("The mission options toggle has an unsupported layout.");
        appearance = button.gameObject.AddComponent<CanvasGroup>();
        button.gameObject.AddComponent<FloppyMissionToggleHint>().Owner = this;
        // DefaultButton registers itself with the game's navigation registry and
        // retains its bevels, fonts, hover feedback, click sound and submit handling.
        button.GetComponent<Graphic>().raycastTarget = true;
    }

    private void PositionControls()
    {
        if (!visible || nativeRect == null || buttonRect == null)
            return;
        var parent = nativeRect.parent as RectTransform;
        if (parent == null)
            return;
        float quarterWidth = parent.rect.width * 0.25f;
        buttonRect.anchoredPosition = new Vector2(-quarterWidth, nativePosition.y);
        // Ouroboros has one native option (Continue). Put it beside Floppy when
        // present; its own state and localized label remain owned by the game.
        if (nativeToggle.gameObject.activeSelf)
            nativeRect.anchoredPosition = new Vector2(quarterWidth, nativePosition.y);
    }

    internal void RestoreNativeLayout()
    {
        if (hasNativePosition && nativeRect != null)
            nativeRect.anchoredPosition = nativePosition;
        hasNativePosition = false;
    }

    internal void Hide()
    {
        visible = false;
        RestoreNativeLayout();
        if (button != null)
            button.gameObject.SetActive(false);
    }

    private void OnClick()
    {
        if (!visible || !isActiveAndEnabled || window == null || !window.gameObject.activeInHierarchy ||
            !(window.Mission.Mission is OuroMission) || !CanEdit ||
            button.LastPressButton != PointerEventData.InputButton.Left)
            return;
        FloppyAvailability.SetEnabled(!FloppyAvailability.Enabled);
        RefreshState(force: true);
    }

    private void LateUpdate()
    {
        if (!visible || button == null)
            return;
        if (FloppyPlugin.Instance == null)
        {
            Hide();
            return;
        }
        RefreshState(force: false);
    }

    private void OnRectTransformDimensionsChange()
    {
        PositionControls();
    }

    private void RefreshState(bool force)
    {
        bool enabled = FloppyAvailability.Enabled;
        bool canEdit = CanEdit;
        bool authority = FloppyAvailability.CanEdit;
        int language = TextBlocks.LanguageIndex;
        if (!force && lastEnabled == enabled && lastCanEdit == canEdit && lastAuthority == authority && lastLanguage == language)
            return;
        lastEnabled = enabled;
        lastCanEdit = canEdit;
        lastAuthority = authority;
        lastLanguage = language;
        label.text = authority ? "Floppy" : "Floppy · HOST";
        button.IgnoreEvents = !canEdit;
        appearance.alpha = canEdit ? 1f : 0.55f;
        icon.sprite = enabled ? Global.Instance.CheckmarkIcon : Global.Instance.CrossIcon;
        button.SetDefaultColor(enabled ? Global.Instance.RedUIColor : Global.Instance.WhiteUIColor);

        // Use the same slider travel calculation as the native mission toggle.
        float thickness = button.TryGetComponent<RectOutline>(out var outline) ? outline.thickness : 1.5f;
        RectTransform slider = button.MainGraphic.rectTransform;
        float travel = ((RectTransform)slider.parent).rect.width * 0.5f - slider.rect.width * 0.5f - thickness;
        slider.anchoredPosition = new Vector2(enabled ? travel : -travel, slider.anchoredPosition.y);
        if (button.Hovering && HoverInfoDisplay.Instance != null)
            HoverInfoDisplay.Instance.Refresh();
    }

    private void OnDestroy()
    {
        RestoreNativeLayout();
        if (button != null)
        {
            button.OnClickUp.RemoveListener(OnClick);
            button.gameObject.SetActive(false);
            Destroy(button.gameObject);
        }
    }

    internal static void DisposeAll()
    {
        foreach (var view in UnityEngine.Object.FindObjectsByType<FloppyMissionToggle>(FindObjectsInactive.Include))
        {
            view.Hide();
            Destroy(view);
        }
    }
}

// The native Button cursor check counts serialized listeners only. A separate
// native hover-info component supplies the pointer cursor for our runtime action.
internal sealed class FloppyMissionToggleHint : HoverInfo
{
    internal FloppyMissionToggle Owner;

    public override string GetInfo(HoverInfoDisplay display)
    {
        if (!FloppyAvailability.CanEdit)
            return "The lobby host chooses whether Floppy appears.";
        if (Owner == null || !Owner.CanEdit)
            return "Deployment has already started.";
        return "Replace Ouroboros shops with Floppy. Change this before starting or continuing a run.";
    }

    public override bool GetTitle(out string title, out Color color)
    {
        title = "Floppy";
        color = Global.Instance.WhiteUIColor;
        return true;
    }

    public override bool CanChangeCursor(out CursorData cursor)
    {
        cursor = Global.Instance.GetPointCursor();
        return Owner != null && Owner.CanEdit;
    }
}
