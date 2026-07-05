using AuthService.Contracts;
using SharedContracts.Messages;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public string UserId { get; private set; }
    public DemoTCP Network { get; private set; }

    // Kết quả màn chơi
    public enum LevelResult { None, Win, Lose }
    public LevelResult LastLevelResult { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Network = GetComponent<DemoTCP>();
        if (Network == null)
            Network = gameObject.AddComponent<DemoTCP>();
    }

    private void OnEnable()
    {
        SMLoginHandle.OnLoginSuccess += HandleLoginSuccess;
    }

    private void OnDisable()
    {
        SMLoginHandle.OnLoginSuccess -= HandleLoginSuccess;
    }

    private void HandleLoginSuccess(SMLogin msg)
    {
        UserId = msg.userId.ToString();
        SceneManager.LoadScene("Home");
    }

    public async void Connect()
    {
        await Network.ConnectTcpAsync();
        Network.SendAsync();
    }

    public void PlayLevel()
    {
        LastLevelResult = LevelResult.None;
        SceneManager.LoadScene("Level");
    }

    public void EndLevel(LevelResult result)
    {
        LastLevelResult = result;
        Debug.Log("Kết thúc màn chơi: " + result);
        SceneManager.LoadScene("Home");
    }
}
