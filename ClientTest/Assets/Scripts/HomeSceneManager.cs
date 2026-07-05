using UnityEngine;
using UnityEngine.UI;

public class HomeSceneManager : MonoBehaviour
{
    [SerializeField] Text txtAccount;
    [SerializeField] Button btnPlay;

    private void Start()
    {
        if (txtAccount != null && GameManager.Instance != null)
            txtAccount.text = "Account: " + GameManager.Instance.UserId;

        if (btnPlay != null)
            btnPlay.onClick.AddListener(OnPlayClicked);

        // Hiển thị kết quả màn trước (nếu có)
        if (GameManager.Instance != null &&
            GameManager.Instance.LastLevelResult != GameManager.LevelResult.None)
        {
            Debug.Log("Kết quả màn trước: " + GameManager.Instance.LastLevelResult);
        }
    }

    private void OnPlayClicked()
    {
        GameManager.Instance.PlayLevel();
    }
}
