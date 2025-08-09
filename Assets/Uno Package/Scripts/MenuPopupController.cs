using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MenuPopupController : MonoBehaviour
{
    [Header("Popup")]
    [SerializeField] private Popup popup;
    [SerializeField] private Button bgButton; 

    [Header("Sound Toggle UI")]
    [SerializeField] private Toggle soundToggle;
    [SerializeField] private GameObject soundOnIcon;
    [SerializeField] private Text soundLabel;

    [Header("Audio (optional)")]
    [Tooltip("If left empty, will use CardGameManager.audioSource")]
    [SerializeField] private AudioSource mainAudioSource;

    private void Awake()
    {
        if (mainAudioSource == null)
            mainAudioSource = CardGameManager.audioSource;

        if (soundToggle != null)
        {
            soundToggle.onValueChanged.RemoveAllListeners();
            soundToggle.onValueChanged.AddListener(OnSoundToggled);
        }

        // click outside to close
        if (bgButton != null)
        {
            bgButton.onClick.RemoveAllListeners();
            bgButton.onClick.AddListener(CloseIfTop);
        }
    }

    private void OnEnable()
    {
        // Sync UI from saved state
        bool isOn = CardGameManager.IsSound;
        if (soundToggle != null) soundToggle.isOn = isOn;
        ApplySoundState(isOn, playClick: false);
    }

    private void OnSoundToggled(bool isOn)
    {
        // Persist preference
        CardGameManager.IsSound = isOn;

        // Optional click sound (only if turning ON and we have sound)
        ApplySoundState(isOn, playClick: true);
    }

    private void ApplySoundState(bool isOn, bool playClick)
    {
        if (soundOnIcon != null) soundOnIcon.SetActive(isOn);
        if (soundLabel != null) soundLabel.text = isOn ? "Sound On" : "Sound Off";

        var globalSrc = CardGameManager.audioSource;
        if (mainAudioSource != null) mainAudioSource.enabled = isOn;
        if (globalSrc != null && globalSrc != mainAudioSource) globalSrc.enabled = isOn;

        AudioListener.pause = !isOn;

        if (playClick && isOn)
            CardGameManager.PlayButton();
    }

    private void CloseIfTop()
    {
        if (popup == null) return;

        if (Popup.currentPopup == popup)
            popup.HidePopup();
    }
}
