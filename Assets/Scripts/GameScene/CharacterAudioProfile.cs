using UnityEngine;

[CreateAssetMenu(fileName = "CharacterAudioProfile", menuName = "Audio/Character Audio Profile")]
public class CharacterAudioProfile : ScriptableObject
{
    public AudioClip[] gruntClips;
    public AudioClip deathClip;
    public AudioClip supershotGrunt;
    public AudioClip shieldDeflect;
}
