using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ModelHighlightManager : MonoBehaviour
{
    [Header("Shader Reference")]
    [Tooltip("Drag the HighlightZone.shader asset directly here.")]
    public Shader highlightShader;

    [Header("Model")]
    [Tooltip("Root GameObject of the Voyager model.")]
    public GameObject modelRoot;

    [Header("Zones (order matches buttons)")]
    public List<HighlightZone> zones = new List<HighlightZone>();

    [Header("UI Buttons")]
    public List<Button> zoneButtons = new List<Button>();
    public Button resetButton;

    [Header("Ghost Settings")]
    [Range(0f, 1f)]
    public float ghostAlpha = 0.15f;

    // ── private ──────────────────────────────────────────────────────────
    private Renderer[]   _renderers;
    private Material[][] _originalMaterials;
    private Material[][] _highlightMaterials;
    private int          _activeZone = -1;

    // Shader property IDs (our custom highlight shader uses _BaseMap / _BaseColor)
    private static readonly int ID_BoxCenter  = Shader.PropertyToID("_BoxCenter");
    private static readonly int ID_BoxExtents = Shader.PropertyToID("_BoxExtents");
    private static readonly int ID_BoxR0      = Shader.PropertyToID("_BoxR0");
    private static readonly int ID_BoxR1      = Shader.PropertyToID("_BoxR1");
    private static readonly int ID_BoxR2      = Shader.PropertyToID("_BoxR2");
    private static readonly int ID_FullOpaque = Shader.PropertyToID("_FullOpaque");
    private static readonly int ID_GhostAlpha = Shader.PropertyToID("_GhostAlpha");
    private static readonly int ID_BaseMap    = Shader.PropertyToID("_BaseMap");
    private static readonly int ID_BaseColor  = Shader.PropertyToID("_BaseColor");

    // UnityGLTF/PBRGraph source property names to READ from
    private static readonly int ID_GLTF_Tex   = Shader.PropertyToID("baseColorTexture");
    private static readonly int ID_GLTF_Color = Shader.PropertyToID("baseColorFactor");
    // Fallbacks for standard URP and Built-in shaders
    private static readonly int ID_URP_Tex    = Shader.PropertyToID("_BaseMap");
    private static readonly int ID_URP_Color  = Shader.PropertyToID("_BaseColor");
    private static readonly int ID_BI_Tex     = Shader.PropertyToID("_MainTex");
    private static readonly int ID_BI_Color   = Shader.PropertyToID("_Color");

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
    }

    private void OnDestroy()
    {
        if (_highlightMaterials == null) return;
        foreach (var mats in _highlightMaterials)
            foreach (var m in mats)
                if (m != null) Destroy(m);
    }

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
                hl.name = $"{(origMats[m] != null ? origMats[m].name : "null")}_highlight";

                if (origMats[m] != null)
                {
                    // ── Texture: try GLTF → URP → Built-in ──────────────
                    Texture tex = null;
                    if (origMats[m].HasProperty(ID_GLTF_Tex))
                        tex = origMats[m].GetTexture(ID_GLTF_Tex);
                    if (tex == null && origMats[m].HasProperty(ID_URP_Tex))
                        tex = origMats[m].GetTexture(ID_URP_Tex);
                    if (tex == null && origMats[m].HasProperty(ID_BI_Tex))
                        tex = origMats[m].GetTexture(ID_BI_Tex);
                    if (tex != null)
                        hl.SetTexture(ID_BaseMap, tex);

                    // ── Colour: try GLTF → URP → Built-in ───────────────
                    Color col = Color.white;
                    bool  hasCol = false;
                    if (origMats[m].HasProperty(ID_GLTF_Color)) { col = origMats[m].GetColor(ID_GLTF_Color); hasCol = true; }
                    else if (origMats[m].HasProperty(ID_URP_Color))  { col = origMats[m].GetColor(ID_URP_Color);  hasCol = true; }
                    else if (origMats[m].HasProperty(ID_BI_Color))   { col = origMats[m].GetColor(ID_BI_Color);   hasCol = true; }
                    if (hasCol) hl.SetColor(ID_BaseColor, col);

                    Debug.Log($"[Highlight] {origMats[m].name} → tex={tex?.name ?? "none"} col={col}");
                }

                hl.SetFloat(ID_GhostAlpha, ghostAlpha);
                hl.SetFloat(ID_FullOpaque, 1f);
                hlMats[m] = hl;
            }

            _highlightMaterials[i] = hlMats;
            Debug.Log($"[Highlight] Renderer '{_renderers[i].name}': {origMats.Length} material(s) → {hlMats.Length} highlight material(s)");
        }
    }

    private void SetupButtons()
    {
        for (int z = 0; z < zoneButtons.Count && z < zones.Count; z++)
        {
            if (zoneButtons[z] == null) continue;
            int captured = z;
            zoneButtons[z].onClick.AddListener(() => ActivateZone(captured));
        }
        if (resetButton != null)
            resetButton.onClick.AddListener(ResetToNormal);
    }

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

        HighlightActiveButton(index);
    }

    public void ResetToNormal()
    {
        _activeZone = -1;
        for (int i = 0; i < _renderers.Length; i++)
            _renderers[i].materials = _originalMaterials[i];
        HighlightActiveButton(-1);
    }

    private void HighlightActiveButton(int activeIndex)
    {
        for (int i = 0; i < zoneButtons.Count; i++)
        {
            if (zoneButtons[i] == null) continue;
            var colors = zoneButtons[i].colors;
            colors.normalColor = (i == activeIndex) ? new Color(0.3f, 0.8f, 1f) : Color.white;
            zoneButtons[i].colors = colors;
        }
    }
}