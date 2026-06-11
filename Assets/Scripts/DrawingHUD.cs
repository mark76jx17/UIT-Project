using UnityEngine;

/// <summary>
/// HUD OnGUI minimale. Mostra istruzioni e stato corrente.
/// </summary>
public class DrawingHUD : MonoBehaviour
{
    public GravitySketchManager manager;

    private GUIStyle _labelStyle;
    private GUIStyle _titleStyle;

    void OnGUI()
    {
        if (_labelStyle == null) InitStyles();

        // Pannello sfondo
        GUI.color = new Color(0, 0, 0, 0.5f);
        GUI.DrawTexture(new Rect(10, 10, 260, 170), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Titolo
        GUI.Label(new Rect(20, 18, 240, 28), "✏ GRAVITY SKETCH", _titleStyle);

        // Istruzioni
        string instructions =
            "🖱 Tieni premuto LMB → Disegna\n" +
            "🖱 Rotella → Cambia profondità\n" +
            "🖱 RMB / Z → Undo ultimo stroke\n" +
            "⌨  C → Cancella tutto";

        GUI.Label(new Rect(20, 50, 240, 130), instructions, _labelStyle);

        // Profondità corrente
        if (manager != null)
        {
            string depth = $"Profondità: {manager.drawDepth:F1} m";
            GUI.Label(new Rect(20, 155, 240, 20), depth, _labelStyle);
        }
    }

    private void InitStyles()
    {
        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 14,
            fontStyle = FontStyle.Bold,
            normal    = { textColor = Color.white }
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            normal    = { textColor = new Color(0.9f, 0.9f, 0.9f) },
            wordWrap  = true
        };
    }
}