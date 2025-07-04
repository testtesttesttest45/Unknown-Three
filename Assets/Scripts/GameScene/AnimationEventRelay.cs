using UnityEngine;
using UnityEngine.UIElements;

public class AnimationEventRelay : MonoBehaviour
{
    public Player player;
    public Bot bot;

    private void Awake()
    {
        player = GetComponentInParent<Player>();
    }


}
