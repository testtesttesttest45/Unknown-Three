using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HomeScene : MonoBehaviour
{
    [Header("Mainscreen")]
    public Image playerAvatar1;
    public Image playerAvatar2;
    public Text playerName;
    public Toggle soundToggle;
    [Header("AvatarSetting")]
    public GameObject avatarSetting;
    public Transform chooseAvatarPanel;
    public Toggle avatarOptionPrefab;
    public InputField playerNameInput;
    private int tempAvatarIndex;
    private string tempPlayerName;
    public GameObject illegalEdition;
    [Header("Tutorial")]
    public Button tutorialButton;
    public GameObject tutorialPanel;
    public Image tutorialImageDisplay; 
    public Sprite[] tutorialImages;
    public Button nextButton; 
    public Button prevButton;
    public Button closeTutorialButton;

    private int currentTutorialIndex = 0;
    private List<Toggle> toggleList;

    void Start()
    {
        Time.timeScale = 1f;
        SetupUI();

        if (tutorialButton != null)
            tutorialButton.onClick.AddListener(OpenTutorial);

        if (nextButton != null)
            nextButton.onClick.AddListener(NextTutorial);

        if (prevButton != null)
            prevButton.onClick.AddListener(PrevTutorial);

        if (closeTutorialButton != null)
            closeTutorialButton.onClick.AddListener(CloseTutorial);

        if (tutorialPanel != null)
            tutorialPanel.SetActive(false);

        if (string.IsNullOrEmpty(CardGameManager.PlayerAvatarName))
        {
            ShowProfileChooser();
        }
        else
        {
            UpdateUI();
        }
    }

    private void OpenTutorial()
    {
        if (tutorialImages == null || tutorialImages.Length == 0) return;

        currentTutorialIndex = 0;
        tutorialPanel.SetActive(true);
        UpdateTutorialImage();
    }

    private void CloseTutorial()
    {
        tutorialPanel.SetActive(false);
    }

    private void NextTutorial()
    {
        if (tutorialImages == null) return;

        currentTutorialIndex++;
        if (currentTutorialIndex >= tutorialImages.Length)
            currentTutorialIndex = tutorialImages.Length - 1;

        UpdateTutorialImage();
    }

    private void PrevTutorial()
    {
        if (tutorialImages == null) return;

        currentTutorialIndex--;
        if (currentTutorialIndex < 0)
            currentTutorialIndex = 0;

        UpdateTutorialImage();
    }

    private void UpdateTutorialImage()
    {
        if (tutorialImageDisplay != null && tutorialImages.Length > 0)
            tutorialImageDisplay.sprite = tutorialImages[currentTutorialIndex];

        if (prevButton != null) prevButton.interactable = (currentTutorialIndex > 0);
        if (nextButton != null) nextButton.interactable = (currentTutorialIndex < tutorialImages.Length - 1);
    }



    void SetupUI()
    {
        soundToggle.isOn = CardGameManager.IsSound;

        soundToggle.onValueChanged.RemoveAllListeners();
        soundToggle.onValueChanged.AddListener((arg0) =>
        {
            CardGameManager.PlayButton();
            CardGameManager.IsSound = arg0;
        });

        toggleList = new List<Toggle>();
        for (int i = 0; i < CardGameManager.TOTAL_AVATAR; i++)
        {
            Toggle temp = Instantiate<Toggle>(avatarOptionPrefab, chooseAvatarPanel);
            temp.group = chooseAvatarPanel.GetComponent<ToggleGroup>();
            temp.GetComponentsInChildren<Image>()[2].sprite = Resources.Load<Sprite>("Avatar/" + i);
            int index = i;
            temp.onValueChanged.AddListener((arg0) =>
            {
                if (arg0)
                {
                    tempAvatarIndex = index;
                    UpdateAvatarPreview();
                }
            });
            toggleList.Add(temp);
        }
        UpdateUI();
    }

    void UpdateUI()
    {
        playerAvatar1.sprite = Resources.Load<Sprite>("Avatar/" + CardGameManager.PlayerAvatarIndex);
        playerAvatar2.sprite = Resources.Load<Sprite>("Avatar/" + CardGameManager.PlayerAvatarIndex);
        playerName.text = CardGameManager.PlayerAvatarName;
        playerName.GetComponent<EllipsisText>().UpdateText();
    }

    void UpdateAvatarPreview()
    {
        Sprite avatarSprite = Resources.Load<Sprite>("Avatar/" + tempAvatarIndex);
        playerAvatar1.sprite = avatarSprite;
        playerAvatar2.sprite = avatarSprite;
    }

    public void ShowProfileChooser()
    {
        avatarSetting.SetActive(true);
        if (illegalEdition != null)
        {
            illegalEdition.SetActive(false);
        }

        tempAvatarIndex = CardGameManager.PlayerAvatarIndex;
        tempPlayerName = CardGameManager.PlayerAvatarName;

        playerNameInput.text = tempPlayerName;

        foreach (Toggle t in toggleList)
            t.isOn = false;

        toggleList[tempAvatarIndex].isOn = true;
    }


    public void OnContine()
    {
        var inputName = playerNameInput.text.Trim();
        if (inputName.Length == 0)
        {
            Toast.instance.ShowMessage("You need to enter your name");
            return;
        }

        CardGameManager.PlayerAvatarName = inputName;
        CardGameManager.PlayerAvatarIndex = tempAvatarIndex;

        avatarSetting.SetActive(false);
        if (illegalEdition != null)
        {
            illegalEdition.SetActive(true);
        }
        UpdateUI();
        CardGameManager.PlayButton();
    }


    public void Cancel()
    {
        avatarSetting.SetActive(false);
        if (illegalEdition != null)
        {
            illegalEdition.SetActive(true);
        }
        UpdateUI();
        CardGameManager.PlayButton();
    }



    //public void OnComputerPlay()
    //{
    //    CardGameManager.currentGameMode = GameMode.Computer;
    //    CardGameManager.PlayButton();
    //    UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
    //}

    //public void OnMultiPlayerPlay()
    //{
    //    EnterMultiplayer();
    //    CardGameManager.PlayButton();
    //}


    //private void EnterMultiplayer()
    //{
    //    CardGameManager.currentGameMode = GameMode.MultiPlayer;
    //    UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
    //}

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Application.Quit();
        }
    }
}
