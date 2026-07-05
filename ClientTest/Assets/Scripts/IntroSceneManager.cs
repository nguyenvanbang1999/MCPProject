using AuthService.Contracts;
using SharedContracts.Messages;
using UnityEngine;
using UnityEngine.UI;

public class IntroSceneManager : MonoBehaviour
{
    [SerializeField] Button btnConnect;
    [SerializeField] Text txtStatus;

    private void Start()
    {
        SMLoginHandle.OnLoginSuccess += OnLoginSuccess;
        SetStatus("Nhấn Connect để đăng nhập...");
        btnConnect.onClick.AddListener(OnConnectClicked);
    }

    private void OnDestroy()
    {
        SMLoginHandle.OnLoginSuccess -= OnLoginSuccess;
    }

    private void OnConnectClicked()
    {
        SetStatus("Đang kết nối...");
        btnConnect.interactable = false;
        GameManager.Instance.Connect();
    }

    private void OnLoginSuccess(SMLogin msg)
    {
        SetStatus("Đăng nhập thành công! Đang vào Home...");
    }

    private void SetStatus(string msg)
    {
        if (txtStatus != null)
            txtStatus.text = msg;
    }
}
