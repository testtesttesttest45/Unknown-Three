using UnityEngine;
using System;

[System.Serializable]
public class GameConfig : MonoBehaviour
{
    public Admob admob;

    [Header("")]
    public int adPeriod;
    public int rewardedVideoAmount;
    public string iosAppID;

    public static GameConfig instance;
    private void Awake()
    {
        instance = this;
    }
}

[System.Serializable]
public class Admob
{
    [Header("Banner")]
    public string androidBanner;
    public string iosBanner;

    [Header("Interstitial")]
    public string androidInterstitial;
    public string iosInterstitial;

    [Header("RewardedVideo")]
    public string androidRewarded;
    public string iosRewarded;
}
