using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Ricampionamento Catmull-Rom: a tratto concluso la polilinea grezza viene
    /// reinterpolata a passo fisso passando per i punti campionati, e la mesh
    /// viene rigenerata una volta sola. Toglie le spigolosità dei tratti veloci
    /// senza costare nulla durante il disegno.
    /// </summary>
    public static class StrokeSmoothing
    {
        public static void Resample(IReadOnlyList<Vector3> points, IReadOnlyList<float> radii,
            float spacing, List<Vector3> outPoints, List<float> outRadii)
        {
            outPoints.Clear();
            outRadii.Clear();
            if (points.Count == 0)
                return;

            outPoints.Add(points[0]);
            outRadii.Add(radii[0]);

            for (int i = 0; i < points.Count - 1; i++)
            {
                // Estremi duplicati come punti di controllo ai bordi.
                var p0 = points[Mathf.Max(i - 1, 0)];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = points[Mathf.Min(i + 2, points.Count - 1)];

                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(p1, p2) / spacing));
                for (int s = 1; s <= steps; s++)
                {
                    float t = (float)s / steps;
                    outPoints.Add(CatmullRom(p0, p1, p2, p3, t));
                    outRadii.Add(Mathf.Lerp(radii[i], radii[i + 1], t));
                }
            }
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (2f * p1
                + (p2 - p0) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
