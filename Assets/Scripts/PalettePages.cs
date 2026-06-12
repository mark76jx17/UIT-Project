using UnityEngine;
using UnityEngine.UI;

namespace MixedRealityProject
{
    /// <summary>
    /// Commuta la palette tra la pagina principale (strumenti/colori/slider)
    /// e la pagina del color picker. L'apertura arriva da OpenPickerOnClick
    /// (sulla cella "colore personalizzato"); la chiusura dal bottone OK.
    /// </summary>
    public class PalettePages : MonoBehaviour
    {
        [Tooltip("Pagina principale della palette (UIBackplate).")]
        [SerializeField] GameObject mainPage;

        [Tooltip("Pagina con il Flexible Color Picker (PickerPanel).")]
        [SerializeField] GameObject pickerPage;

        [Tooltip("Bottone 'OK' nella pagina del picker.")]
        [SerializeField] Button closePickerButton;

        void Awake()
        {
            closePickerButton.onClick.AddListener(ClosePicker);
            ClosePicker();
        }

        public void OpenPicker()
        {
            mainPage.SetActive(false);
            pickerPage.SetActive(true);
        }

        public void ClosePicker()
        {
            pickerPage.SetActive(false);
            mainPage.SetActive(true);
        }
    }
}
