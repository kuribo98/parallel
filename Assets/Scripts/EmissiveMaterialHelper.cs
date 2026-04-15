using UnityEngine;

public static class EmissiveMaterialHelper
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public static void Apply(
        Renderer targetRenderer,
        int materialIndex,
        Color emissionColor,
        float emissionIntensity,
        Texture emissionTexture,
        bool setBaseColorToo,
        Object warningContext)
    {
        if (targetRenderer == null)
        {
            return;
        }

        Material[] materials = targetRenderer.materials;
        if (materialIndex < 0 || materialIndex >= materials.Length)
        {
            Debug.LogWarning(
                $"{warningContext.name} tried to update material slot {materialIndex}, but {targetRenderer.name} only has {materials.Length} material slot(s).",
                warningContext);
            return;
        }

        Material material = materials[materialIndex];
        if (material == null)
        {
            return;
        }

        material.EnableKeyword("_EMISSION");

        Color finalEmissionColor = emissionColor * Mathf.Max(0.0f, emissionIntensity);
        finalEmissionColor.a = emissionColor.a;

        if (material.HasProperty(EmissionColorId))
        {
            material.SetColor(EmissionColorId, finalEmissionColor);
        }

        if (emissionTexture != null && material.HasProperty(EmissionMapId))
        {
            material.SetTexture(EmissionMapId, emissionTexture);
        }

        if (setBaseColorToo)
        {
            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, emissionColor);
            }
            else if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, emissionColor);
            }
        }

        targetRenderer.materials = materials;
    }
}
