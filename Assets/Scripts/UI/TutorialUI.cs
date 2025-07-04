using UnityEngine;
using UnityEngine.UI;

public class TutorialUI : MonoBehaviour
{
    [SerializeField] private GameObject image1;
    [SerializeField] private GameObject image2;

    private int tapCount = 0;

    private void Start()
    {
        gameObject.SetActive(true);
        image1.SetActive(true);
        image2.SetActive(false);
    }

    public void OnTutorialTapped()
    {
        tapCount++;

        if (tapCount == 1)
        {
            image1.SetActive(false);
            image2.SetActive(true);
        }
        else if (tapCount == 2)
        {
            GameManager.Instance.SetTutorialReadyServerRpc();
            gameObject.SetActive(false);
        }
    }
}
