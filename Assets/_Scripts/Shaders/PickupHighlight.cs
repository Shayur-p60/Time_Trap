using System.Collections.Generic;
using UnityEngine;

// Puts the TimeTrap/PickupHighlight outline on an item. Added at runtime by the item scripts (Pickup, Chest,
// PathogenSample, DataDownload), so it works for pickups the PickupSpawner creates as well as ones placed by hand.
//
// For every mesh of the item it makes a child object that draws the same mesh again with the highlight material
// (a "shell"). The item's own renderers and materials are not touched. The shader decides by itself whether the
// outline shows (player within about 6 m, or the torch pointing at it), so nothing runs here per frame.
//
// When the item is collected the shells are detached and fade out over Fade Out Time, so the outline does not
// just vanish with the item.
public class PickupHighlight : MonoBehaviour
{
    // The highlight material, set by PickupHighlightConfig in the scene.
    public static Material SharedMaterial;

    public float fadeOutTime = 0.4f;

    static readonly int FadeId = Shader.PropertyToID("_Fade");
    const string ShellName = "Highlight";

    readonly List<Renderer> shells = new List<Renderer>();
    bool collected;

    public bool HasShells => shells.Count > 0;
    public bool IsCollected => collected;

    // Statics survive a scene reload: GameManager.ResetAllStatics calls this on Restart.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetForNewPlaySession()
    {
        SharedMaterial = null;
    }

    // Gives an item its highlight (once). Safe to call again, for example after its visual has been spawned.
    public static PickupHighlight Ensure(GameObject item)
    {
        if (item == null || !Application.isPlaying) return null;
        var highlight = item.GetComponent<PickupHighlight>();
        if (highlight == null) highlight = item.AddComponent<PickupHighlight>();
        highlight.Build();
        return highlight;
    }

    // The item has been collected (or used up): fade its highlight out.
    public static void Collect(GameObject item)
    {
        if (item == null) return;
        var highlight = item.GetComponent<PickupHighlight>();
        if (highlight != null) highlight.FadeOut();
    }

    // The item is back (progress reset): show the highlight again.
    public static void Restore(GameObject item)
    {
        if (item == null) return;
        var highlight = item.GetComponent<PickupHighlight>();
        if (highlight == null) return;
        highlight.collected = false;
        highlight.Build();
    }

    void Start()
    {
        // the material or the visual may not have existed yet when Ensure was called
        Build();
    }

    // Makes one shell per mesh of the item.
    public void Build()
    {
        if (collected || shells.Count > 0 || SharedMaterial == null) return;

        foreach (var source in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (source.name == ShellName) continue;
            var filter = source.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            var go = new GameObject(ShellName);
            go.layer = source.gameObject.layer;
            go.transform.SetParent(source.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;

            var shell = go.AddComponent<MeshRenderer>();
            // one highlight material per sub-mesh, so every part of the mesh gets the outline
            var materials = new Material[filter.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = SharedMaterial;
            shell.sharedMaterials = materials;
            shell.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shell.receiveShadows = false;
            shell.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            shell.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            shells.Add(shell);
        }
    }

    // Detaches the shells (the item may be destroyed or hidden this frame) and lets them fade out on their own.
    public void FadeOut()
    {
        if (collected) return;
        collected = true;
        if (shells.Count == 0) return;

        var holder = new GameObject("HighlightFade");
        foreach (var shell in shells)
            if (shell != null) shell.transform.SetParent(holder.transform, true);
        var fader = holder.AddComponent<Fader>();
        fader.shells = shells.ToArray();
        fader.duration = fadeOutTime;
        shells.Clear();
    }

    // Lives on the detached shells: takes _Fade from 1 to 0, then removes them.
    class Fader : MonoBehaviour
    {
        public Renderer[] shells;
        public float duration;

        float fade = 1f;
        MaterialPropertyBlock block;

        void Update()
        {
            fade = Mathf.MoveTowards(fade, 0f, Time.deltaTime / Mathf.Max(0.01f, duration));
            if (block == null) block = new MaterialPropertyBlock();
            block.SetFloat(FadeId, fade);
            foreach (var shell in shells)
                if (shell != null) shell.SetPropertyBlock(block);
            if (fade <= 0f) Destroy(gameObject);
        }
    }
}
