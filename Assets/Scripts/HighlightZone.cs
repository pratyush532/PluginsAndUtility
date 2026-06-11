using UnityEngine;

/// <summary>
/// Place on an empty GameObject to define a highlight zone.
/// Move/rotate/scale the transform in the editor to frame the part
/// of the model you want to stay fully visible when this zone is active.
/// The coloured wire box is editor-only; invisible at runtime.
/// </summary>
public class HighlightZone : MonoBehaviour
{
    [Tooltip("Label shown on the UI button for this zone.")]
    public string buttonLabel = "Zone";

    [Tooltip("Wire colour of this zone's gizmo in the editor.")]
    public Color gizmoColor = Color.cyan;

    // ── Gizmo (editor only) ──────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        Gizmos.color  = gizmoColor;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color  = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.15f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, Vector3.one);
    }

    // ── Data the manager reads ───────────────────────────────────────────
    public Vector3 WorldCenter  => transform.position;
    public Vector3 LocalExtents => transform.lossyScale * 0.5f;
    public Vector3 AxisX        => transform.right;
    public Vector3 AxisY        => transform.up;
    public Vector3 AxisZ        => transform.forward;
}