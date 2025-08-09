using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WheelSpinUI : MonoBehaviour
{
    [Header("Refs")]
    public RectTransform wheel;
    public RectTransform pocketsRotator;
    public Image[] pocketIcons = new Image[8];
    public TMP_Text[] pocketTexts = new TMP_Text[8];
    public GameObject pointer;
    public System.Action OnPocketTick;

    [Header("FX")]
    public ParticleSystem myWinConfetti;

    [Header("Spin")]
    public float spinDuration = 3f;


    void Awake()
    {
        if (pocketsRotator == null)
            pocketsRotator = transform.Find("Roulette/Group_Pockets") as RectTransform;
    }

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);

    public void SetPocket(int index, Sprite avatarSprite, string label)
    {
        if ((uint)index >= 8) return;
        pocketIcons[index].sprite = avatarSprite;
        if (pocketIcons[index].sprite != null) pocketIcons[index].SetNativeSize();
        pocketTexts[index].text = label;
    }

    public IEnumerator SpinTo(float targetZ, float duration)
    {
        if (pocketsRotator == null) yield break;

        float startZ = pocketsRotator.localEulerAngles.z;
        if (startZ > 180f) startZ -= 360f;

        float elapsed = 0f;
        float slice = 360f / 8f;

        int lastTick = Mathf.FloorToInt(startZ / slice);

        while (elapsed < duration)
        {
            float t = 1f - Mathf.Pow(1f - (elapsed / duration), 3f);
            float z = Mathf.Lerp(startZ, targetZ, t);
            pocketsRotator.localEulerAngles = new Vector3(0, 0, z);

            int tick = Mathf.FloorToInt(z / slice);
            if (tick != lastTick)
            {
                lastTick = tick;
                OnPocketTick?.Invoke();
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        pocketsRotator.localEulerAngles = new Vector3(0, 0, targetZ);
    }

    public void ResetRotation()
    {
        if (pocketsRotator != null)
            pocketsRotator.localEulerAngles = Vector3.zero;
    }

    public void PlayLocalWinFX()
    {
        if (myWinConfetti == null) return;
        Debug.Log("[WheelSpinUI] Playing local win confetti FX");
        myWinConfetti.gameObject.SetActive(true);

        myWinConfetti.Play();
        CardGameManager.PlaySound(GamePlayManager.instance.uno_btn_clip);
    }
}
