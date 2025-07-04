using System.Collections.Generic;
using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance;

    public AudioClip forestBgmClip;
    public AudioClip hellBgmClip;
    public AudioClip countdownClip;
    public AudioClip suddenDeathClip;
    public AudioClip victoryClip;
    public AudioClip defeatClip;
    public AudioClip drawClip;

    public AudioClip arrowFireClip;
    public AudioClip spikeballThrowClip;
    public AudioClip shieldActivateClip;
    public AudioClip tumbleClip;
    public AudioClip supershotChargeClip;
    public AudioClip supershotReleaseClip;
    public AudioClip lavaBurnClip;
    public AudioClip[] gruntClips;
    public AudioClip superGruntClip;

    private AudioSource bgmSource;
    private AudioSource abilitySource;

    private Dictionary<string, AudioSource> loopingSources = new Dictionary<string, AudioSource>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.volume = 0.3f;
        bgmSource.spatialBlend = 0f;

        abilitySource = gameObject.AddComponent<AudioSource>();
        abilitySource.volume = 1f;
        abilitySource.spatialBlend = 0f;
        abilitySource.loop = false;
    }

    public void PlayBGMByEnvironment(bool useForest)
    {
        AudioClip bgmToPlay = useForest ? forestBgmClip : hellBgmClip;
        if (bgmSource.clip == bgmToPlay && bgmSource.isPlaying)
        {
            Debug.Log("BGM already playing: " + bgmToPlay?.name);
            return;
        }

        Debug.Log("Starting BGM: " + bgmToPlay?.name);
        bgmSource.Stop();
        bgmSource.clip = bgmToPlay;
        if (bgmToPlay != null)
            bgmSource.Play();
    }

    public void StopBGM()
    {
        if (bgmSource != null)
        {
            Debug.Log($"Stopping BGM: {bgmSource.clip?.name}, isPlaying={bgmSource.isPlaying}");
            bgmSource.Stop();
            bgmSource.clip = null; // extra safe
        }
        else
        {
            Debug.LogWarning("[SoundManager] Tried to stop BGM but bgmSource is null!");
        }
    }

    public bool IsSoundPlaying(string name)
    {
        return loopingSources.ContainsKey(name) && loopingSources[name] != null && loopingSources[name].isPlaying;
    }

    public void PlayGlobalSound(AudioClip clip, float volume = 1f)
    {
        if (clip == null)
        {
            Debug.LogWarning("PlayGlobalSound: Clip is null");
            return;
        }

        Debug.Log($"Playing global sound: {clip.name}");

        GameObject obj = new GameObject("OneShotGlobalSound");
        AudioSource source = obj.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = 0f;
        source.Play();
        Destroy(obj, clip.length);
    }

    public void PlaySoundByName(string name)
    {
        switch (name)
        {
            case "arrow_fire":
                abilitySource.PlayOneShot(arrowFireClip);
                return;
            case "grunt":
                if (gruntClips.Length > 0)
                {
                    int idx = Random.Range(0, gruntClips.Length);
                    abilitySource.PlayOneShot(gruntClips[idx]);
                }
                return;
            case "spikeball_throw":
                abilitySource.PlayOneShot(spikeballThrowClip);
                break;
            case "shield_activate":
                abilitySource.PlayOneShot(shieldActivateClip);
                break;
            case "tumble":
                abilitySource.PlayOneShot(tumbleClip);
                break;
            case "supershot_release":
                abilitySource.PlayOneShot(supershotReleaseClip);
                break;
            case "super_grunt":
                abilitySource.PlayOneShot(superGruntClip);
                break;

            case "supershot_charge":
                if (!loopingSources.ContainsKey("supershot_charge") || loopingSources["supershot_charge"] == null)
                {
                    var source = gameObject.AddComponent<AudioSource>();
                    source.clip = supershotChargeClip;
                    source.loop = true;
                    source.volume = 0.5f;
                    source.spatialBlend = 0f;
                    source.Play();

                    loopingSources["supershot_charge"] = source;
                }
                break;

            case "lava_burn":
                if (!loopingSources.ContainsKey("lava_burn") || loopingSources["lava_burn"] == null)
                {
                    var source = gameObject.AddComponent<AudioSource>();
                    source.clip = lavaBurnClip;
                    source.loop = true;
                    source.volume = 0.4f;
                    source.spatialBlend = 0f;
                    source.Play();

                    loopingSources["lava_burn"] = source;
                }
                break;
        }
    }
    public void PlayLavaBurn(float volume)
    {
        if (!loopingSources.ContainsKey("lava_burn") || loopingSources["lava_burn"] == null)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = lavaBurnClip;
            source.loop = true;
            source.volume = volume;
            source.spatialBlend = 0f;
            source.Play();

            loopingSources["lava_burn"] = source;
        }
        else
        {
            loopingSources["lava_burn"].volume = volume;
            if (!loopingSources["lava_burn"].isPlaying)
                loopingSources["lava_burn"].Play();
        }
    }

    public AudioSource GetAbilitySource()
    {
        return abilitySource;
    }

    public void StopSoundByName(string name)
    {
        if (loopingSources.ContainsKey(name) && loopingSources[name] != null)
        {
            loopingSources[name].Stop();
            Destroy(loopingSources[name]);
            loopingSources.Remove(name);
        }
    }
}
