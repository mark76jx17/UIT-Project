#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MixedRealityProject.Drawing.EditorTools
{
    /// <summary>
    /// Costruisce i pannelli della UI (palette / Options / shortcuts, in tutte le lingue) come
    /// GameObject REALI nella scena, disposti a griglia, così si possono ispezionare/orbitare
    /// nella Scene view senza visore e senza Play. A differenza dei tool che salvano PNG, questo
    /// lascia oggetti in scena: per questo sono marcati `HideFlags.DontSave`, quindi **non vengono
    /// mai serializzati nel file .unity** anche salvando la scena (evita il bug del doppione
    /// PalettePreview / "tutto grigio", vedi ambiente-mr-pulizia.md).
    ///
    /// Gli oggetti sono **transitori**: le label TMP creano istanze di materiale che NON sono
    /// DontSave e verrebbero distrutte a un domain reload, lasciando i componenti TMP con
    /// riferimenti morti → MissingReferenceException a ripetizione (anche durante Build/Play).
    /// Per questo il preview si **autopulisce** prima di reload dominio, Play e Build (vedi
    /// l'[InitializeOnLoad] e il preprocessor di build in fondo). Resta anche il Clear manuale.
    /// Menu: Tools/Drawing/Show Panels In Scene  e  Clear Panels In Scene.
    /// </summary>
    [InitializeOnLoad]
    public static class ScenePanelPreview
    {
        const string RootName = "DrawingPanelPreview";
        const float ColStep = 1.2f;   // distanza tra colonne (tipi di pannello)
        const float RowStep = 1.0f;   // distanza tra righe (lingue)
        static readonly Vector3 GridCenter = new(0f, 1.4f, 0f);

        // Autopulizia: il preview non deve sopravvivere a un domain reload (ricompile scripts) né
        // all'ingresso in Play, altrimenti i materiali TMP distrutti dal reload lasciano i TMP con
        // riferimenti morti. [InitializeOnLoad] fa girare questo ctor a ogni reload, così le
        // sottoscrizioni si rinnovano.
        static ScenePanelPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearSilent;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
                ClearSilent();
        }

        [MenuItem("Tools/Drawing/Show Panels In Scene")]
        public static void Show()
        {
            ClearExisting();

            var root = new GameObject(RootName) { hideFlags = HideFlags.DontSave };
            root.transform.position = GridCenter;

            // Settare Localization.Current scrive in PlayerPrefs: salvo e ripristino la lingua
            // scelta dall'utente. Il testo dei pannelli è "cotto" nel TMP al momento del build,
            // quindi i pannelli già creati non cambiano quando passo alla lingua successiva.
            string savedLang = Localization.Current;
            try
            {
                PaletteController.DebugAnchorGrid = false;

                var langs = Localization.Languages;
                var panels = new (string name, System.Func<PaletteController, GameObject> build)[]
                {
                    ("palette",   pc => pc.EditorBuildMainPanel()),
                    ("options",   pc => pc.EditorBuildOptionsPanel()),
                    ("shortcuts", pc => pc.EditorBuildShortcutsPanel()),
                };

                // Griglia: colonne = tipi di pannello, righe = lingue. Centrata su GridCenter.
                float x0 = -(panels.Length - 1) * 0.5f * ColStep;
                float y0 = (langs.Count - 1) * 0.5f * RowStep;

                for (int row = 0; row < langs.Count; row++)
                {
                    Localization.Current = langs[row];
                    for (int col = 0; col < panels.Length; col++)
                    {
                        var pos = new Vector3(x0 + col * ColStep, y0 - row * RowStep, 0f);
                        BuildOne($"{panels[col].name}-{langs[row]}", panels[col].build, root.transform, pos);
                    }
                }
            }
            finally
            {
                Localization.Current = savedLang;
                PaletteController.DebugAnchorGrid = false;
            }

            // DontSave va forzato su TUTTO il sotto-albero (mesh comprese), non solo sul root,
            // perché i figli sono creati dai builder senza ereditarlo automaticamente.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave;

            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();

            Debug.Log($"[ScenePanelPreview] Pannelli in scena sotto '{RootName}' (HideFlags.DontSave: " +
                      "non vengono salvati; si autopuliscono a reload/Play/Build). " +
                      "Usa 'Clear Panels In Scene' per rimuoverli a mano.");
        }

        [MenuItem("Tools/Drawing/Clear Panels In Scene")]
        public static void Clear()
        {
            if (ClearExisting())
                Debug.Log($"[ScenePanelPreview] '{RootName}' rimosso dalla scena.");
            else
                Debug.Log($"[ScenePanelPreview] Nessun '{RootName}' in scena.");
        }

        // Costruisce un pannello con un PaletteController usa-e-getta su un holder posizionato
        // nella cella della griglia (il pannello viene creato figlio dell'holder dai builder).
        static void BuildOne(string holderName, System.Func<PaletteController, GameObject> build,
                             Transform parent, Vector3 localPos)
        {
            var holder = new GameObject(holderName);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPos;

            var pc = holder.AddComponent<PaletteController>();
            var panel = build(pc);
            if (panel == null)
                Debug.LogError($"[ScenePanelPreview] Pannello '{holderName}' non costruito.");
        }

        static void ClearSilent() => ClearExisting();

        static bool ClearExisting()
        {
            var existing = GameObject.Find(RootName);
            if (existing == null)
                return false;
            Object.DestroyImmediate(existing);
            return true;
        }

        // Rimuove il preview anche prima di una build (oltre al reload/Play coperti dall'evento):
        // così non finisce mai nel processo di build e non lascia TMP appesi.
        class ClearBeforeBuild : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;
            public void OnPreprocessBuild(BuildReport report) => ClearExisting();
        }
    }
}
#endif
