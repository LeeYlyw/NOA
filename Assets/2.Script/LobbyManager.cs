using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance;

    public TMP_Text statusText;
    public Button startButton;
    public string gameSceneName = "Demo";

    private int currentPlayerCount = 0;
    private int maxPlayerCount = 2;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        UpdatePlayerCountText();

        if (startButton != null)
            startButton.onClick.AddListener(StartGame);
    }

    public void SetPlayerCount(int count)
    {
        currentPlayerCount = count;
        UpdatePlayerCountText();
    }

    void UpdatePlayerCountText()
    {
        if (statusText != null)
            statusText.text = currentPlayerCount + " / " + maxPlayerCount;
    }

    public void StartGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }
}