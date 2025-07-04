using System.Collections.Generic;
using Unity.Netcode.Components;
using UnityEngine;

public class PlayerVisual : MonoBehaviour
{

    public Animator CurrentAnimator { get; private set; }

    [SerializeField] private NetworkAnimator networkAnimator;


    private NetworkAnimator netAnim;
    public CharacterAudioProfile CurrentAudioProfile { get; private set; }

    private void Awake()
    {
        netAnim = GetComponent<NetworkAnimator>();
        if (netAnim != null)
        {
            netAnim.enabled = false;
        }
    }


}
