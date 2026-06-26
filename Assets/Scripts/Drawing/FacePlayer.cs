using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Orienta questo transform verso la testa del giocatore (Camera.main) a ogni frame,
    /// con smorzamento per togliere il micro-jitter. Usato dai pannelli modali della
    /// palette (Options/Shortcuts): aprendosi di lato giacerebbero sul piano della palette
    /// e si leggerebbero male, così invece guardano sempre l'utente.
    ///
    /// Agisce solo quando il GameObject è attivo (OnEnable/LateUpdate). La POSIZIONE resta
    /// gestita dalla palette (PositionMenus): qui tocchiamo solo la rotazione. La convenzione
    /// di orientamento è la stessa della palette (forward = dalla testa al pannello), così le
    /// label disegnate verso -Z restano rivolte all'utente.
    /// </summary>
    public class FacePlayer : MonoBehaviour
    {
        const float TurnSpeed = 15f; // velocità di allineamento (slerp esponenziale al secondo)

        Camera head;
        Camera Head => head != null ? head : (head = Camera.main);

        // All'apertura punta subito la testa, senza interpolazione (niente "scatto" iniziale).
        void OnEnable() => Face(instant: true);

        void LateUpdate() => Face(instant: false);

        void Face(bool instant)
        {
            if (Head == null)
                return;
            Vector3 toPanel = transform.position - Head.transform.position;
            if (toPanel.sqrMagnitude < 1e-6f)
                return;
            var target = Quaternion.LookRotation(toPanel.normalized, Vector3.up);
            transform.rotation = instant
                ? target
                : Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-TurnSpeed * Time.deltaTime));
        }
    }
}
