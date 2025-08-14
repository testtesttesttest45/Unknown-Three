using System.Collections;
using TMPro;
using Unity.Netcode;
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

    [Tooltip("Degrees from pocket #0 center to pointer when rotator.z == 0")]
    public float pointerOffsetDegrees = 0f;

    public bool IsSpinning { get; private set; }
    private bool _holdOneFrame;
    private float _finalZ;
    private float _lastZ;

    void Awake()
    {
        Debug.Log($"[Wheel] Awake client {NetworkManager.Singleton?.LocalClientId}, instances={FindObjectsOfType<WheelSpinUI>(true).Length}"); 
        if (pocketsRotator == null)
            pocketsRotator = transform.Find("Roulette/Group_Pockets") as RectTransform;

        if (pocketsRotator) _lastZ = pocketsRotator.localEulerAngles.z;

        if (myWinConfetti != null)
        {
            var main = myWinConfetti.main;
            main.playOnAwake = false;
            myWinConfetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            myWinConfetti.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        if (!pocketsRotator) return;
        float z = pocketsRotator.localEulerAngles.z;
        float dz = Mathf.DeltaAngle(_lastZ, z);

        if (!IsSpinning && Mathf.Abs(dz) > 1f)
            Debug.Log($"[WheelSnapTRACE] External write after spin: Δ={dz:F2}, now={z:F2}", this);

        _lastZ = z;
    }

    void LateUpdate()
    {
        if (_holdOneFrame && pocketsRotator)
        {
            pocketsRotator.localEulerAngles = new Vector3(0f, 0f, _finalZ);
            _holdOneFrame = false;
        }
    }

    public void Show() => gameObject.SetActive(true);
    public void Hide()
    {
        StopLocalWinFX();
        gameObject.SetActive(false);
    }

    public void SetPocket(int index, Sprite avatarSprite, string label)
    {
        if ((uint)index >= 8) return;
        pocketIcons[index].sprite = avatarSprite;
        if (pocketIcons[index].sprite != null) pocketIcons[index].SetNativeSize();
        pocketTexts[index].text = label;
    }

    public void AlignWinnerUnderPointer(int pocketIndex)
    {
        if (pocketsRotator == null) return;
        float slice = 360f / 8f;
        float pocketCenter = pocketIndex * slice;
        float z = pocketCenter + pointerOffsetDegrees;
        pocketsRotator.localEulerAngles = new Vector3(0f, 0f, z);
    }

    public IEnumerator SpinTo(float targetZ, float duration)
    {
        if (pocketsRotator == null) yield break;
        IsSpinning = true;

        float startZ = pocketsRotator.localEulerAngles.z;
        if (startZ > 180f) startZ -= 360f;

        float targetWrap = Mathf.Repeat(targetZ, 360f);
        float startWrap = Mathf.Repeat(startZ, 360f);
        float shortest = Mathf.DeltaAngle(startWrap, targetWrap);
        int fullSpins = Mathf.RoundToInt((targetZ - targetWrap) / 360f);
        float targetTweenZ = startZ + shortest + fullSpins * 360f;

        float elapsed = 0f;
        float slice = 360f / 8f;
        int lastTick = Mathf.FloorToInt(Mathf.Repeat(startZ, 360f) / slice);

        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            float z = Mathf.Lerp(startZ, targetTweenZ, eased);
            pocketsRotator.localEulerAngles = new Vector3(0f, 0f, z);

            int tick = Mathf.FloorToInt(Mathf.Repeat(z, 360f) / slice);
            if (tick != lastTick)
            {
                lastTick = tick;
                OnPocketTick?.Invoke();
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        _finalZ = Mathf.Repeat(targetZ, 360f);
        pocketsRotator.localEulerAngles = new Vector3(0f, 0f, _finalZ);
        _holdOneFrame = true;
        IsSpinning = false;
    }

    public void ResetRotation()
    {
        if (IsSpinning) return;
        if (pocketsRotator != null)
            pocketsRotator.localEulerAngles = Vector3.zero;
    }

    public void PlayLocalWinFX()
    {
        Debug.Log($"[Confetti] PlayLocalWinFX on client {NetworkManager.Singleton?.LocalClientId}");
        if (myWinConfetti == null) return;
        myWinConfetti.gameObject.SetActive(true);
        myWinConfetti.Play();
        CardGameManager.PlaySound(GamePlayManager.instance.special_click);
    }


    public void StopLocalWinFX()
    {
        if (myWinConfetti == null) return;
        myWinConfetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        myWinConfetti.gameObject.SetActive(false);
    }
}
