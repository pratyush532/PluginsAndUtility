using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages highlight zones and owns all button UI logic.
///
/// SETUP:
///  1. Drag HighlightZone.shader into 'Highlight Shader'.
///  2. Drag your Voyager root into 'Model Root'.
///  3. Drag each HighlightZone GameObject into 'Zones' in order.
///  4. Drag the matching UI Buttons into 'Zone Buttons' in the same order.
///     No Reset button needed — clicking the active button again exits.
///
/// BUTTON BEHAVIOUR:
///  - Click a button  → activates that zone, button tints active colour.
///  - Click it again  → deactivates, everything back to normal, button resets.
///  - Click another   → switches zone, previous button resets, new one tints.
/// </summary>
public class ModelHighlightManager : MonoBehaviour
{
    [Header("Shader Reference")]
    [Tooltip("Drag the HighlightZone.shader asset directly here.")]
    public Shader highlightShader;

    [Header("Model")]
    [Tooltip("Root GameObject of the Voyager model.")]
    public GameObject modelRoot;

    [Header("Zones (order must match Zone Buttons)")]
    public List<HighlightZone> zones = new List<HighlightZone>();

    [Header("UI Buttons (order must match Zones)")]
    [Tooltip("One button per zone. Clicking active button again exits highlight mode.")]
    public List<Button> zoneButtons = new List<Button>();

    [Header("Button Colours")]
    [Tooltip("Button colour when idle.")]
    public Color buttonNormalColor  = Color.white;
    [Tooltip("Button colour when its zone is active.")]
    public Color buttonActiveColor  = new Color(0.3f, 0.85f, 1f, 1f);
    [Tooltip("Button text colour when idle (optional — leave alpha 0 to skip).")]
    public Color labelNormalColor   = Color.black;
    [Tooltip("Button text colour when active (optional — leave alpha 0 to skip).")]
    public Color labelActiveColor   = Color.white;

    [Header("Ghost Settings")]
    [Range(0f, 1f)]
    public float ghostAlpha = 0.15f;

    // ── private ──────────────────────────────────────────────────────────
    private Renderer[]   _renderers;
    private Material[][] _originalMaterials;
    private Material[][] _highlightMaterials;
    private int          _activeZone = -1;   // -1 = none active

    // Highlight shader property IDs
    private static readonly int ID_BoxCenter  = Shader.PropertyToID("_BoxCenter");
    private static readonly int ID_BoxExtents = Shader.PropertyToID("_BoxExtents");
    private static readonly int ID_BoxR0      = Shader.PropertyToID("_BoxR0");
    private static readonly int ID_BoxR1      = Shader.PropertyToID("_BoxR1");
    private static readonly int ID_BoxR2      = Shader.PropertyToID("_BoxR2");
    private static readonly int ID_FullOpaque = Shader.PropertyToID("_FullOpaque");
    private static readonly int ID_GhostAlpha = Shader.PropertyToID("_GhostAlpha");
    private static readonly int ID_BaseMap    = Shader.PropertyToID("_BaseMap");
    private static readonly int ID_BaseColor  = Shader.PropertyToID("_BaseColor");

    // Source material property IDs (read from original mats)
    private static readonly int ID_GLTF_Tex   = Shader.PropertyToID("baseColorTexture");
    private static readonly int ID_GLTF_Color = Shader.PropertyToID("baseColorFactor");
    private static readonly int ID_URP_Tex    = Shader.PropertyToID("_BaseMap");
    private static readonly int ID_URP_Color  = Shader.PropertyToID("_BaseColor");
    private static readonly int ID_BI_Tex     = Shader.PropertyToID("_MainTex");
    private static readonly int ID_BI_Color   = Shader.PropertyToID("_Color");

    // ── Unity lifecycle ───────────────────────────────────────────────────

    private void Awake()
    {
        if (highlightShader == null)
        {
            Debug.LogError("[ModelHighlightManager] 'Highlight Shader' is not assigned.");
            enabled = false;
            return;
        }
        if (modelRoot == null)
        {
            Debug.LogError("[ModelHighlightManager] 'Model Root' is not assigned.");
            enabled = false;
            return;
        }

        CacheRenderers();
        BuildHighlightMaterials();
        SetupButtons();
        RefreshButtonVisuals(-1); // all buttons start idle
    }

    private void OnDestroy()
    {
        if (_highlightMaterials == null) return;
        foreach (var mats in _highlightMaterials)
            foreach (var m in mats)
                if (m != null) Destroy(m);
    }

    // ── Initialisation ────────────────────────────────────────────────────

    private void CacheRenderers()
    {
        _renderers          = modelRoot.GetComponentsInChildren<Renderer>(true);
        _originalMaterials  = new Material[_renderers.Length][];
        _highlightMaterials = new Material[_renderers.Length][];

        for (int i = 0; i < _renderers.Length; i++)
            _originalMaterials[i] = _renderers[i].sharedMaterials;
    }

    private void BuildHighlightMaterials()
    {
        for (int i = 0; i < _renderers.Length; i++)
        {
            var origMats = _originalMaterials[i];
            var hlMats   = new Material[origMats.Length];

            for (int m = 0; m < origMats.Length; m++)
            {
                var hl = new Material(highlightShader);

                if (origMats[m] != null)
                {
                    // Texture: GLTF → URP → Built-in
                    Texture tex = null;
                    if (origMats[m].HasProperty(ID_GLTF_Tex))  tex = origMats[m].GetTexture(ID_GLTF_Tex);
                    if (tex == null && origMats[m].HasProperty(ID_URP_Tex)) tex = origMats[m].GetTexture(ID_URP_Tex);
                    if (tex == null && origMats[m].HasProperty(ID_BI_Tex))  tex = origMats[m].GetTexture(ID_BI_Tex);
                    if (tex != null) hl.SetTexture(ID_BaseMap, tex);

                    // Colour: GLTF → URP → Built-in
                    Color col = Color.white;
                    if      (origMats[m].HasProperty(ID_GLTF_Color)) col = origMats[m].GetColor(ID_GLTF_Color);
                    else if (origMats[m].HasProperty(ID_URP_Color))  col = origMats[m].GetColor(ID_URP_Color);
                    else if (origMats[m].HasProperty(ID_BI_Color))   col = origMats[m].GetColor(ID_BI_Color);
                    hl.SetColor(ID_BaseColor, col);
                }

                hl.SetFloat(ID_GhostAlpha, ghostAlpha);
                hl.SetFloat(ID_FullOpaque, 1f);
                hlMats[m] = hl;
            }

            _highlightMaterials[i] = hlMats;
        }
    }

    private void SetupButtons()
    {
        for (int z = 0; z < zoneButtons.Count && z < zones.Count; z++)
        {
            if (zoneButtons[z] == null) continue;
            int captured = z;
            zoneButtons[z].onClick.AddListener(() => OnZoneButtonClicked(captured));
        }
    }

    // ── Button click handler ──────────────────────────────────────────────

    private void OnZoneButtonClicked(int index)
    {
        if (_activeZone == index)
        {
            // Clicking the already-active button → exit highlight mode
            ResetToNormal();
        }
        else
        {
            // Switch to (or activate) this zone
            ActivateZone(index);
        }
    }

    // ── Zone logic ────────────────────────────────────────────────────────

    public void ActivateZone(int index)
    {
        if (index < 0 || index >= zones.Count) return;
        _activeZone = index;

        HighlightZone zone = zones[index];

        foreach (var mats in _highlightMaterials)
        {
            foreach (var mat in mats)
            {
                mat.SetVector(ID_BoxCenter,  zone.WorldCenter);
                mat.SetVector(ID_BoxExtents, zone.LocalExtents);
                mat.SetVector(ID_BoxR0,      zone.AxisX);
                mat.SetVector(ID_BoxR1,      zone.AxisY);
                mat.SetVector(ID_BoxR2,      zone.AxisZ);
                mat.SetFloat (ID_FullOpaque, 0f);
                mat.SetFloat (ID_GhostAlpha, ghostAlpha);
            }
        }

        for (int i = 0; i < _renderers.Length; i++)
            _renderers[i].materials = _highlightMaterials[i];

        RefreshButtonVisuals(index);
    }

    public void ResetToNormal()
    {
        _activeZone = -1;

        for (int i = 0; i < _renderers.Length; i++)
            _renderers[i].materials = _originalMaterials[i];

        RefreshButtonVisuals(-1);
    }

    // ── Button visuals ────────────────────────────────────────────────────

    private void RefreshButtonVisuals(int activeIndex)
    {
        for (int i = 0; i < zoneButtons.Count; i++)
        {
            if (zoneButtons[i] == null) continue;

            bool isActive = (i == activeIndex);

            // Background colour via ColorBlock
            var colors = zoneButtons[i].colors;
            colors.normalColor      = isActive ? buttonActiveColor  : buttonNormalColor;
            colors.selectedColor    = isActive ? buttonActiveColor  : buttonNormalColor;
            colors.highlightedColor = isActive
                ? Color.Lerp(buttonActiveColor, Color.white, 0.15f)
                : Color.Lerp(buttonNormalColor, Color.white, 0.15f);
            zoneButtons[i].colors = colors;

            // Label colour (only if alpha > 0 meaning user wants to control it)
            if (labelNormalColor.a > 0f || labelActiveColor.a > 0f)
            {
                var label = zoneButtons[i].GetComponentInChildren<Text>();
                if (label != null)
                    label.color = isActive ? labelActiveColor : labelNormalColor;

                // Also handle TextMeshPro if present
                var tmpLabel = zoneButtons[i].GetComponentInChildren<TMPro.TMP_Text>();
                if (tmpLabel != null)
                    tmpLabel.color = isActive ? labelActiveColor : labelNormalColor;
            }
        }
    }
}