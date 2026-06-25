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
        static readonly Color EraseTint = new(1f, 0.25f, 0.25f, 1f);

        /// <summary>
        /// Tinge l'oggetto di rosso: anteprima di cosa cancellerà la gomma. Va sempre
        /// rimosso con Clear (anche prima di nasconderlo, vedi BrushController.EraseAt),
        /// altrimenti riapparirebbe rosso dopo un undo della cancellazione.
        /// </summary>
        public static void SetEraseHover(Transform root)
        {
            if (root == null)
                return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                var baseColor = renderer.sharedMaterial.GetColor(BaseColorId);
                var tinted = Color.Lerp(baseColor, EraseTint, 0.6f);
                tinted.a = baseColor.a;
                block.Clear();
                block.SetColor(BaseColorId, tinted);
                renderer.SetPropertyBlock(block);
            }
        }

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
