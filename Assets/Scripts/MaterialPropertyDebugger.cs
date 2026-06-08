using UnityEngine;

/// <summary>
/// Attach to your Voyager root temporarily.
/// In Play mode, check the Console — it prints every property name
/// and type for every material on every renderer in the children.
/// Remove this script once you have the property names.
/// </summary>
public class MaterialPropertyDebugger : MonoBehaviour
{
    private void Start()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null) continue;
                Debug.Log($"=== [{r.gameObject.name}] Material: {mat.name} | Shader: {mat.shader.name} ===");
                var shader = mat.shader;
                int count  = UnityEditor.ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < count; i++)
                {
                    var propName = UnityEditor.ShaderUtil.GetPropertyName(shader, i);
                    var propType = UnityEditor.ShaderUtil.GetPropertyType(shader, i);
                    // Print texture properties and color properties only — skip floats/ranges
                    if (propType == UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv ||
                        propType == UnityEditor.ShaderUtil.ShaderPropertyType.Color)
                    {
                        string value = propType == UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv
                            ? (mat.GetTexture(propName) != null ? mat.GetTexture(propName).name : "null")
                            : mat.GetColor(propName).ToString();
                        Debug.Log($"  [{propType}] {propName} = {value}");
                    }
                }
            }
        }
    }
}