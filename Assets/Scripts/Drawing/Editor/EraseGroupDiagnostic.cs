#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MixedRealityProject.Drawing.Editor
{
    /// <summary>
    /// Diagnostica in Edit mode della gomma sui gruppi fusi (Stroke.EraseNodeAndRebuild):
    /// costruisce un gruppo sintetico di due tratti fusi con posa/scala non banali, applica
    /// l'erosione e verifica NUMERICAMENTE che i superstiti restino sulla polilinea
    /// originale (niente spostamenti/deformazioni) e fuori dalla sfera erosa.
    /// Si esegue dal menu (anche via MCP, senza Play): l'esito è in console ([EraseDiag]).
    /// </summary>
    public static class EraseGroupDiagnostic
    {
        const float Tolerance = 0.001f; // 1 mm: deviazione massima ammessa dai punti originali

        [MenuItem("Tools/Drawing/Test Group Erase")]
        public static void Run()
        {
            bool allPass = true;
            try
            {
                allPass &= TestChildErase();
                allPass &= TestRootErase();
            }
            finally
            {
                Debug.Log(allPass
                    ? "[EraseDiag] TUTTI I TEST PASS"
                    : "[EraseDiag] TEST FALLITI — vedi sopra");
            }
        }

        // Gruppo: A (radice, linea lungo X) + B (fuso, linea lungo Y), con posa e scala
        // non banali per stanare errori di trasformazione (il bug localScale/lossyScale).
        static Transform BuildGroup(out Stroke a, out Stroke b, out List<Vector3> aWorld, out List<Vector3> bWorld)
        {
            a = Stroke.Rebuild(Line(new Vector3(-0.2f, 0, 0), new Vector3(0.2f, 0, 0), 41),
                Radii(41), Color.red, BrushType.Round);
            b = Stroke.Rebuild(Line(new Vector3(0, -0.15f, 0), new Vector3(0, 0.15f, 0), 31),
                Radii(31), Color.blue, BrushType.Round);

            // Fusione come in EndPress/ReleaseHolding: B figlio di A, via il suo DrawnItem.
            b.transform.SetParent(a.transform, true);
            Object.DestroyImmediate(b.GetComponent<DrawnItem>());

            // Posa/scala "cattive" del gruppo.
            a.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(20f, 30f, 10f));
            a.transform.localScale = Vector3.one * 1.5f;

            aWorld = RecordWorldPoints(a.GetComponent<StrokeRecord>());
            bWorld = RecordWorldPoints(b.GetComponent<StrokeRecord>());
            return a.transform;
        }

        static bool TestChildErase()
        {
            var root = BuildGroup(out _, out var b, out var aWorld, out var bWorld);
            GameObject rebuilt = null;
            try
            {
                Vector3 center = bWorld[bWorld.Count / 2]; // metà del tratto B, in mondo
                const float radius = 0.03f;
                rebuilt = Stroke.EraseNodeAndRebuild(b.transform, center, radius, out bool erodible);

                if (!Check("figlio: erodibile", erodible) | !Check("figlio: superstiti", rebuilt != null))
                    return false;

                bool pass = VerifySurvivors(rebuilt, bWorld, center, radius, "figlio");
                // Il tratto A (radice) NON deve essere stato toccato.
                pass &= Check("figlio: radice intatta", MaxDeviation(RecordWorldPoints(
                    root.GetComponent<StrokeRecord>()), aWorld) < Tolerance);
                return pass;
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
                if (rebuilt != null)
                    Object.DestroyImmediate(rebuilt);
            }
        }

        static bool TestRootErase()
        {
            var root = BuildGroup(out var a, out _, out var aWorld, out var bWorld);
            GameObject rebuilt = null;
            try
            {
                Vector3 center = aWorld[aWorld.Count / 4]; // su A (radice), fuori dall'incrocio
                const float radius = 0.03f;
                rebuilt = Stroke.EraseNodeAndRebuild(a.transform, center, radius, out bool erodible);

                if (!Check("radice: erodibile", erodible) | !Check("radice: superstiti", rebuilt != null))
                    return false;

                // I record del risultato: quelli di A (erosi) e il CLONE di B (intatto).
                bool pass = true;
                var survivorsOnA = new List<Vector3>();
                var survivorsOnB = new List<Vector3>();
                foreach (var rec in rebuilt.GetComponentsInChildren<StrokeRecord>())
                {
                    var world = RecordWorldPoints(rec);
                    // Attribuisce il record alla polilinea più vicina (A rossa, B blu).
                    if (DeviationFrom(world, aWorld) <= DeviationFrom(world, bWorld))
                        survivorsOnA.AddRange(world);
                    else
                        survivorsOnB.AddRange(world);
                }
                pass &= Check("radice: pezzi di A sulla polilinea", DeviationFrom(survivorsOnA, aWorld) < Tolerance);
                pass &= Check("radice: pezzi di A fuori dalla sfera",
                    MinDistance(survivorsOnA, center) > radius - Tolerance);
                pass &= Check("radice: clone di B fedele (posizione)", DeviationFrom(survivorsOnB, bWorld) < Tolerance);
                pass &= Check("radice: clone di B completo (n. punti)", survivorsOnB.Count == bWorld.Count);
                return pass;
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
                if (rebuilt != null)
                    Object.DestroyImmediate(rebuilt);
            }
        }

        // I punti del risultato devono stare sulla polilinea originale e fuori dalla sfera.
        static bool VerifySurvivors(GameObject rebuilt, List<Vector3> original, Vector3 center,
            float radius, string label)
        {
            var world = new List<Vector3>();
            foreach (var rec in rebuilt.GetComponentsInChildren<StrokeRecord>())
                world.AddRange(RecordWorldPoints(rec));
            bool pass = Check($"{label}: punti rimasti > 0", world.Count > 0);
            pass &= Check($"{label}: nessuno spostamento (dev. max {DeviationFrom(world, original):F5} m)",
                DeviationFrom(world, original) < Tolerance);
            pass &= Check($"{label}: area erosa vuota (dist. min {MinDistance(world, center):F5} m)",
                MinDistance(world, center) > radius - Tolerance);
            pass &= Check($"{label}: qualcosa è stato eroso", world.Count < original.Count);
            return pass;
        }

        static bool Check(string name, bool ok)
        {
            if (ok) Debug.Log($"[EraseDiag] PASS — {name}");
            else Debug.LogError($"[EraseDiag] FAIL — {name}");
            return ok;
        }

        // ---- misure geometriche ----

        static float DeviationFrom(List<Vector3> points, List<Vector3> polyline)
        {
            float worst = 0f;
            foreach (var p in points)
                worst = Mathf.Max(worst, DistanceToPolyline(p, polyline));
            return worst;
        }

        static float MaxDeviation(List<Vector3> a, List<Vector3> b)
        {
            if (a.Count != b.Count)
                return float.MaxValue;
            float worst = 0f;
            for (int i = 0; i < a.Count; i++)
                worst = Mathf.Max(worst, Vector3.Distance(a[i], b[i]));
            return worst;
        }

        static float MinDistance(List<Vector3> points, Vector3 center)
        {
            float best = float.MaxValue;
            foreach (var p in points)
                best = Mathf.Min(best, Vector3.Distance(p, center));
            return best;
        }

        static float DistanceToPolyline(Vector3 p, List<Vector3> line)
        {
            float best = float.MaxValue;
            for (int i = 1; i < line.Count; i++)
            {
                Vector3 a = line[i - 1], ab = line[i] - a;
                float t = ab.sqrMagnitude > 1e-12f
                    ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, Vector3.Distance(p, a + t * ab));
            }
            return best;
        }

        static Vector3[] Line(Vector3 from, Vector3 to, int n)
        {
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
                pts[i] = Vector3.Lerp(from, to, i / (float)(n - 1));
            return pts;
        }

        static float[] Radii(int n)
        {
            var r = new float[n];
            for (int i = 0; i < n; i++)
                r[i] = 0.005f;
            return r;
        }

        static List<Vector3> RecordWorldPoints(StrokeRecord rec)
        {
            var world = new List<Vector3>(rec.points.Count);
            foreach (var p in rec.points)
                world.Add(rec.transform.TransformPoint(p));
            return world;
        }
    }
}
#endif
