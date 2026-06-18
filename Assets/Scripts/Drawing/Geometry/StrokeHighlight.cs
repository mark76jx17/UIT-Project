using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Evidenziazione di un oggetto disegnato (hover/presa) schiarendone il
    /// colore via MaterialPropertyBlock: non tocca i materiali condivisi della
    /// cache (altrimenti si illuminerebbero tutti i tratti dello stesso colore).
    /// </summary>
    public static class StrokeHighlight
    {
        static readonly MaterialPropertyBlock block = new();
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static void Set(Transform root, float brightness)
        {
            if (root == null)
                return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (Mathf.Approximately(brightness, 1f))
                {
                    renderer.SetPropertyBlock(null);
                    continue;
                }
                var baseColor = renderer.sharedMaterial.GetColor(BaseColorId);
                var lit = baseColor * brightness;
                lit.a = baseColor.a;
                block.Clear();
                block.SetColor(BaseColorId, lit);
                renderer.SetPropertyBlock(block);
            }
        }

        public static void Clear(Transform root) => Set(root, 1f);
    }
}
