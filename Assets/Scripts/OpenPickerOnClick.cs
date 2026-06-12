using UnityEngine;
using UnityEngine.EventSystems;

namespace MixedRealityProject
{
    /// <summary>
    /// Apre la pagina del color picker al click sulla cella "colore
    /// personalizzato". Convive con il Toggle della cella: un secondo
    /// Selectable (Button) non è ammesso sullo stesso GameObject, e un
    /// Toggle già selezionato non emette eventi al click — questo handler
    /// scatta sempre.
    /// </summary>
    public class OpenPickerOnClick : MonoBehaviour, IPointerClickHandler
    {
        [Tooltip("Il PalettePages su PaletteCanvas.")]
        [SerializeField] PalettePages pages;

        public void OnPointerClick(PointerEventData eventData)
        {
            pages.OpenPicker();
        }
    }
}
