using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Marcatore sul piano specchio: lo rende afferrabile col grip (come gli oggetti
    /// disegnati) SENZA renderlo cancellabile o salvabile (non è un DrawnItem).
    /// GrabController lo riconosce; gomma/magnete/fill/contagocce — che cercano
    /// DrawnItem — lo ignorano.
    /// </summary>
    public class MirrorHandle : MonoBehaviour
    {
    }
}
