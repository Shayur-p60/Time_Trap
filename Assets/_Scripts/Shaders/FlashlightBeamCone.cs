using UnityEngine;

// The visible torch beam. Sits on a child of FlashlightBeamPivot, so it follows the light wherever the pivot puts
// it (beside the camera, or under the pistol barrel at WeaponLightMount).
// It builds a cone mesh to match the spot light (apex at the light, pointing along +Z, same angle and range) and
// keeps the TimeTrap/FlashlightBeam material's cone shape and intensity up to date. Intensity comes from
// TorchShaderGlobals.Level: off with the torch, flickering with a low battery, dimmer as the battery runs down.
[DefaultExecutionOrder(110)]   // after TorchShaderGlobals
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class FlashlightBeamCone : MonoBehaviour
{
    [Tooltip("The torch's spot light. Left empty, the Light on a parent.")]
    public Light spot;
    [Tooltip("Where the on/off, flicker and battery level come from. Left empty, found on a parent.")]
    public TorchShaderGlobals torch;
    [Tooltip("Sides of the cone.")]
    [Range(8, 64)] public int segments = 32;
    [Tooltip("Overall beam strength. Keep it low: the beam should be felt, not seen as a solid cone.")]
    [Range(0f, 1f)] public float strength = 1f;

    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int RangeId = Shader.PropertyToID("_Range");
    static readonly int TanOuterId = Shader.PropertyToID("_TanOuter");
    static readonly int TanInnerId = Shader.PropertyToID("_TanInner");
    static readonly int ColourId = Shader.PropertyToID("_Color");

    MeshRenderer meshRenderer;
    Mesh mesh;
    Material material;
    float builtAngle = -1f;
    float builtInner = -1f;
    float builtRange = -1f;
    Color normalColour = Color.white;
    bool shownGolden;

    void Awake()
    {
        if (spot == null) spot = GetComponentInParent<Light>();
        if (torch == null) torch = GetComponentInParent<TorchShaderGlobals>();
        meshRenderer = GetComponent<MeshRenderer>();
        // this object's own copy of the material, so its intensity can change without a property block
        material = meshRenderer.material;
        if (material.HasProperty(ColourId)) normalColour = material.GetColor(ColourId);
        Rebuild();
    }

    void LateUpdate()
    {
        if (spot == null) return;
        if (spot.spotAngle != builtAngle || spot.innerSpotAngle != builtInner || spot.range != builtRange) Rebuild();

        // golden beam (Time Lord): gold, and as bright as the normal beam
        bool golden = torch != null && torch.Golden;
        if (golden != shownGolden)
        {
            shownGolden = golden;
            // the beam colour is an HDR property: used as it is, so matched in brightness as a linear colour
            material.SetColor(ColourId, golden ? TorchShaderGlobals.GoldLinear(normalColour) : normalColour);
        }

        float level = torch != null ? torch.Level : (spot.enabled ? 1f : 0f);
        material.SetFloat(IntensityId, level * strength);
        meshRenderer.enabled = level > 0.001f;
    }

    // Builds the cone: one vertex at the apex, a ring at the far end, and a cap closing the far end.
    void Rebuild()
    {
        if (spot == null) return;
        builtAngle = spot.spotAngle;
        builtInner = spot.innerSpotAngle;
        builtRange = spot.range;

        float tanOuter = Mathf.Tan(builtAngle * 0.5f * Mathf.Deg2Rad);
        float tanInner = Mathf.Tan(builtInner * 0.5f * Mathf.Deg2Rad);
        float radius = builtRange * tanOuter;

        var vertices = new Vector3[segments + 2];
        var triangles = new int[segments * 6];
        vertices[0] = Vector3.zero;                                  // apex
        vertices[segments + 1] = new Vector3(0f, 0f, builtRange);    // centre of the cap
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            vertices[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, builtRange);
        }
        for (int i = 0; i < segments; i++)
        {
            int current = i + 1;
            int next = (i + 1) % segments + 1;
            // side (faces outward)
            triangles[i * 6] = 0;
            triangles[i * 6 + 1] = next;
            triangles[i * 6 + 2] = current;
            // cap (faces away from the light)
            triangles[i * 6 + 3] = segments + 1;
            triangles[i * 6 + 4] = current;
            triangles[i * 6 + 5] = next;
        }

        if (mesh == null)
        {
            mesh = new Mesh { name = "FlashlightBeamCone" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        material.SetFloat(RangeId, builtRange);
        material.SetFloat(TanOuterId, tanOuter);
        material.SetFloat(TanInnerId, tanInner);
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
    }
}
