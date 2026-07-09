using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Salvataggio/caricamento del disegno su JSON. Si serializzano i dati
    /// sorgente (polilinea + raggi + colore, vedi StrokeRecord) e la trasformata
    /// locale di ogni oggetto, con l'indice del genitore per ricostruire le
    /// gerarchie create dal magnete: la mesh viene rigenerata al caricamento.
    /// File: [persistentDataPath]/drawing.json.
    /// </summary>
    public static class DrawingStore
    {
        [Serializable]
        public class ItemData
        {
            public Vector3[] points;
            public float[] radii;
            public Color color;
            public int brushType;
            public bool isPoint;
            public bool filled;
            public bool isFill; // questo oggetto È un riempimento (points = contorno esterno)
            public Color fillColor;
            public HoleData[] holes; // buchi del riempimento (ciambella); null se nessuno
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public int parent = -1;

            // Un buco (contorno interno) di un riempimento. Classe a sé perché JsonUtility
            // non serializza liste annidate direttamente.
            [Serializable]
            public class HoleData
            {
                public Vector3[] points;
            }
        }

        [Serializable]
        class FileData
        {
            public List<ItemData> items = new();
        }

        public static string FilePath =>
            Path.Combine(Application.persistentDataPath, "drawing.json");

        /// <summary>Copia di sicurezza salvata prima di un'operazione distruttiva (Load/Clear).</summary>
        public static string BackupPath =>
            Path.Combine(Application.persistentDataPath, "drawing_backup.json");

        /// <summary>
        /// Esporta la scena corrente come file OBJ standard in [persistentDataPath]/drawing.obj.
        /// Vedi DrawingExporter per i dettagli del formato.
        /// </summary>
        public static void ExportOBJ()
        {
            DrawingExporter.ExportToFile();
            Toast.Show("Esportato OBJ");
        }

        public static void Save()
        {
            // Solo gli oggetti attivi: i tratti annullati con undo non si salvano.
            var records = new List<StrokeRecord>(
                UnityEngine.Object.FindObjectsByType<StrokeRecord>(FindObjectsSortMode.None));
            var file = new FileData { items = Capture(records) };
            File.WriteAllText(FilePath, JsonUtility.ToJson(file));
            Debug.Log($"[DrawingStore] Salvati {file.items.Count} oggetti in {FilePath}");
            Toast.Show($"Salvato ✓ ({file.items.Count})");
        }

        /// <summary>
        /// Duplica un oggetto disegnato (gerarchie del magnete incluse)
        /// ricostruendolo dai suoi StrokeRecord: la stessa strada del load.
        /// La copia nasce sulla posa dell'originale; spetta al chiamante
        /// scostarla e push-arla nella history.
        /// </summary>
        public static GameObject Duplicate(Transform root)
        {
            var records = new List<StrokeRecord>(root.GetComponentsInChildren<StrokeRecord>());
            if (records.Count == 0)
                return null;
            var created = Instantiate(Capture(records));
            var copyRoot = created[0]; // ordinati per profondità: il primo è la radice
            copyRoot.SetPositionAndRotation(root.position, root.rotation);
            copyRoot.localScale = root.localScale;
            return copyRoot.gameObject;
        }

        static List<ItemData> Capture(List<StrokeRecord> records)
        {
            // Genitori prima dei figli, così alla ricostruzione il parent esiste già.
            records.Sort((a, b) => Depth(a.transform).CompareTo(Depth(b.transform)));

            var items = new List<ItemData>(records.Count);
            foreach (var record in records)
            {
                var parentRecord = record.transform.parent != null
                    ? record.transform.parent.GetComponent<StrokeRecord>()
                    : null;
                items.Add(new ItemData
                {
                    points = record.points.ToArray(),
                    radii = record.radii.ToArray(),
                    color = record.color,
                    brushType = (int)record.brushType,
                    isPoint = record.isPoint,
                    filled = record.filled,
                    isFill = record.isFill,
                    fillColor = record.fillColor,
                    holes = CaptureHoles(record.holes),
                    localPosition = record.transform.localPosition,
                    localRotation = record.transform.localRotation,
                    localScale = record.transform.localScale,
                    // IndexOf = -1 se il genitore non è nell'insieme (radice).
                    parent = parentRecord != null ? records.IndexOf(parentRecord) : -1,
                });
            }
            return items;
        }

        static ItemData.HoleData[] CaptureHoles(List<List<Vector3>> holes)
        {
            if (holes == null || holes.Count == 0)
                return null;
            var arr = new ItemData.HoleData[holes.Count];
            for (int i = 0; i < holes.Count; i++)
                arr[i] = new ItemData.HoleData { points = holes[i].ToArray() };
            return arr;
        }

        /// <summary>
        /// Salva una copia di sicurezza della scena corrente in BackupPath, prima di
        /// un'operazione che la cancella (Load o Clear). Recupero: rinominare
        /// drawing_backup.json in drawing.json e premere Load. Niente da salvare → no-op.
        /// </summary>
        public static void SaveBackup()
        {
            var records = new List<StrokeRecord>(
                UnityEngine.Object.FindObjectsByType<StrokeRecord>(FindObjectsSortMode.None));
            if (records.Count == 0)
                return;
            var file = new FileData { items = Capture(records) };
            File.WriteAllText(BackupPath, JsonUtility.ToJson(file));
            Debug.Log($"[DrawingStore] Backup di {file.items.Count} oggetti in {BackupPath}");
        }

        /// <summary>
        /// Nuovo disegno: svuota la scena come UNA singola azione di gomma annullabile
        /// (Undo la ripristina tutta in un colpo), salvando prima anche un backup su file
        /// come ulteriore rete di sicurezza. Riusa il modello della gomma: gli oggetti non
        /// vengono distrutti ma nascosti, e restano recuperabili finché l'azione è nello
        /// stack di undo (MaxUndo) o finché non si svuota di nuovo.
        /// </summary>
        public static void NewScene()
        {
            SaveBackup();
            // Solo gli oggetti attivi (i visibili): gli inattivi sono già "undo-ati" nel
            // ramo redo e verranno liberati da PushErase→ClearRedo.
            var active = new List<GameObject>();
            foreach (var item in UnityEngine.Object.FindObjectsByType<DrawnItem>(FindObjectsSortMode.None))
                active.Add(item.gameObject);
            if (active.Count == 0)
                return;
            foreach (var go in active)
                go.SetActive(false); // nascondi, non distruggere: così Undo può riattivarli
            StrokeHistory.PushErase(active.ToArray()); // un solo passo di undo per tutta la scena
            Debug.Log($"[DrawingStore] Scena svuotata ({active.Count} oggetti, annullabile).");
            // Niente toast: la scena che si svuota è già feedback evidente.
        }

        public static void Load()
        {
            if (!File.Exists(FilePath))
            {
                Debug.LogWarning($"[DrawingStore] Nessun salvataggio in {FilePath}");
                Toast.Show("Nessun salvataggio");
                return;
            }

            FileData file = null;
            try { file = JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath)); }
            catch (System.Exception e) { Debug.LogError($"[DrawingStore] JSON illeggibile: {e.Message}"); }
            if (file == null || file.items == null)
            {
                Toast.Show("Salvataggio illeggibile");
                return;
            }

            // Backup della scena corrente prima di sovrascriverla, poi si riparte puliti:
            // via la history e ogni oggetto disegnato rimasto.
            SaveBackup();
            StrokeHistory.Clear();
            foreach (var item in UnityEngine.Object.FindObjectsByType<DrawnItem>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.Destroy(item.gameObject);

            var created = Instantiate(file.items);
            Debug.Log($"[DrawingStore] Caricati {created.Count} oggetti da {FilePath}");
            Toast.Show($"Caricati {created.Count} oggetti");
        }

        static List<Transform> Instantiate(List<ItemData> items)
        {
            var created = new List<Transform>();
            foreach (var data in items)
            {
                GameObject go;
                if (data.isFill)
                {
                    // Riempimento ricostruito dal contorno salvato + eventuali buchi. Senza
                    // genitore = indipendente (afferrabile); con genitore = fill di gruppo,
                    // riagganciato sotto la sua radice dal blocco parent sotto.
                    var holeLoops = new List<List<Vector3>>();
                    if (data.holes != null)
                        foreach (var h in data.holes)
                            holeLoops.Add(new List<Vector3>(h.points));
                    go = Stroke.RebuildFill(data.points, holeLoops, data.fillColor, (BrushType)data.brushType, data.parent < 0);
                }
                else if (data.isPoint)
                {
                    go = Stroke.CreatePoint(Vector3.zero, data.radii[0], data.color, (BrushType)data.brushType);
                }
                else
                {
                    var stroke = Stroke.Rebuild(data.points, data.radii, data.color, (BrushType)data.brushType);
                    if (data.filled)
                    {
                        stroke.GetComponent<StrokeRecord>().fillColor = data.fillColor;
                        stroke.CreateFill();
                    }
                    go = stroke.gameObject;
                }

                if (data.parent >= 0 && data.parent < created.Count)
                {
                    go.transform.SetParent(created[data.parent], false);
                    UnityEngine.Object.Destroy(go.GetComponent<DrawnItem>());
                }
                go.transform.localPosition = data.localPosition;
                go.transform.localRotation = data.localRotation;
                go.transform.localScale = data.localScale;
                created.Add(go.transform);
            }
            return created;
        }

        static int Depth(Transform t)
        {
            int depth = 0;
            while (t.parent != null)
            {
                depth++;
                t = t.parent;
            }
            return depth;
        }
    }
}
