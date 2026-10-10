using System.Collections.Generic;
using UnityEngine;

// Dev tool for the presentation: F9 swaps everything drawn with a TimeTrap/ shader to plain URP Lit, and back.
//   - opaque custom materials (the zombies) become a URP Lit material with the same textures
//   - materials listed under Overrides become the plain material given there (the portal -> its old placeholder)
//   - any other transparent custom material (the beam, the dust, the pickup outline) has no "plain" look, so it is hidden
//   - the full-screen effect is switched off as well (ScreenFX checks Plain)
// Editor and development builds only; in a release build this script does nothing.
public class ShaderDevToggle : MonoBehaviour
{
    [System.Serializable]
    public struct Override
    {
        public Material custom;
        public Material plain;
    }

    public KeyCode key = KeyCode.F9;
    [Tooltip("Shaders whose name starts with this are swapped.")]
    public string shaderPrefix = "TimeTrap/";
    [Tooltip("Plain replacements for particular custom materials.")]
    public Override[] overrides = new Override[0];
    [Tooltip("While plain is on, look for newly spawned objects this often (seconds).")]
    public float rescanInterval = 0.5f;

    // True while the plain look is showing.
    public static bool Plain { get; private set; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    readonly Dictionary<Renderer, Material[]> swapped = new Dictionary<Renderer, Material[]>();
    readonly HashSet<Renderer> hidden = new HashSet<Renderer>();
    readonly Dictionary<Material, Material> plainCopies = new Dictionary<Material, Material>();
    float nextScan;

    void Update()
    {
        if (Input.GetKeyDown(key)) SetPlain(!Plain);

        // zombies spawned while plain is on
        if (Plain && Time.unscaledTime >= nextScan) Scan();
    }

    void OnDisable()
    {
        if (Plain) SetPlain(false);
    }

    void OnDestroy()
    {
        foreach (var copy in plainCopies.Values)
            if (copy != null) Destroy(copy);
    }

    public void SetPlain(bool plain)
    {
        Plain = plain;
        if (plain) Scan();
        else Restore();
    }

    // Swaps every renderer that still shows a custom material.
    void Scan()
    {
        nextScan = Time.unscaledTime + rescanInterval;
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            if (swapped.ContainsKey(r) || hidden.Contains(r)) continue;
            var materials = r.sharedMaterials;
            Material[] replaced = null;
            bool hide = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var m = materials[i];
                if (m == null || m.shader == null || !m.shader.name.StartsWith(shaderPrefix)) continue;
                var plain = PlainFor(m);
                if (plain == null) { hide = true; continue; }
                if (replaced == null) replaced = (Material[])materials.Clone();
                replaced[i] = plain;
            }
            if (replaced != null)
            {
                swapped[r] = materials;
                r.sharedMaterials = replaced;
            }
            else if (hide)
            {
                hidden.Add(r);
                r.forceRenderingOff = true;
            }
        }
    }

    void Restore()
    {
        foreach (var pair in swapped)
            if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
        foreach (var r in hidden)
            if (r != null) r.forceRenderingOff = false;
        swapped.Clear();
        hidden.Clear();
    }

    // The plain material for a custom one, or null when it should be hidden instead.
    Material PlainFor(Material custom)
    {
        foreach (var o in overrides)
            if (o.custom == custom && o.plain != null) return o.plain;
        // transparent effects have no URP Lit equivalent
        if (custom.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent) return null;

        if (plainCopies.TryGetValue(custom, out var copy)) return copy;
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) return null;

        copy = new Material(lit) { name = custom.name + " (URP Lit)" };
        if (custom.HasProperty("_BaseMap")) copy.SetTexture("_BaseMap", custom.GetTexture("_BaseMap"));
        if (custom.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", custom.GetColor("_BaseColor"));
        if (custom.HasProperty("_BumpMap") && custom.GetTexture("_BumpMap") != null)
        {
            copy.SetTexture("_BumpMap", custom.GetTexture("_BumpMap"));
            copy.EnableKeyword("_NORMALMAP");
        }
        if (custom.HasProperty("_Cull")) copy.SetFloat("_Cull", custom.GetFloat("_Cull"));
        if (custom.HasProperty("_Cutoff") && custom.GetFloat("_Cutoff") > 0f)
        {
            copy.SetFloat("_AlphaClip", 1f);
            copy.SetFloat("_Cutoff", custom.GetFloat("_Cutoff"));
            copy.EnableKeyword("_ALPHATEST_ON");
        }
        copy.SetFloat("_Smoothness", 0.45f);
        copy.renderQueue = custom.renderQueue;
        plainCopies[custom] = copy;
        return copy;
    }

    void OnGUI()
    {
        if (!Plain) return;
        GUI.Label(new Rect(12f, Screen.height - 30f, 400f, 22f), "Custom shaders OFF (plain URP Lit) - " + key + " to switch back");
    }
#endif
}
