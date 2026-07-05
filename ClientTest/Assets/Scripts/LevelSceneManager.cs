using UnityEngine;
using UnityEngine.UI;

public class LevelSceneManager : MonoBehaviour
{
    [SerializeField] Button btnWin;
    [SerializeField] Button btnLose;
    [SerializeField] Text txtResult;

    private void Start()
    {
        if (txtResult != null)
            txtResult.text = "";

        btnWin.onClick.AddListener(OnWin);
        btnLose.onClick.AddListener(OnLose);
    }

    private void OnWin()
    {
        if (txtResult != null) txtResult.text = "🏆 Chiến thắng!";
        btnWin.interactable = false;
        btnLose.interactable = false;
        Invoke(nameof(FinishWin), 1f);
    }

    private void OnLose()
    {
        if (txtResult != null) txtResult.text = "💀 Thất bại!";
        btnWin.interactable = false;
        btnLose.interactable = false;
        Invoke(nameof(FinishLose), 1f);
    }

    private void FinishWin() => GameManager.Instance.EndLevel(GameManager.LevelResult.Win);
    private void FinishLose() => GameManager.Instance.EndLevel(GameManager.LevelResult.Lose);
}
