using UnityEngine;

namespace Horse.Floppy;

internal static class FloppyUiMaterial
{
    // StencilMaterial.Add in the installed UnityEngine.UI checks these six
    // properties before it can render a material through a UGUI mask.
    private static readonly string[] MaskProperties =
    {
        "_Stencil", "_StencilOp", "_StencilComp",
        "_StencilReadMask", "_StencilWriteMask", "_ColorMask"
    };

    internal static Material Resolve(Material material)
    {
        if (material == null) return null;
        foreach (string property in MaskProperties)
            if (!material.HasProperty(property)) return null;
        return material;
    }
}
