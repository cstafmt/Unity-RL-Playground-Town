using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoboChatController_Vrag : MonoBehaviour
{
    [Header("Dependencies")]
    public RobotAgent_VRAG robotAgent; // 拖拽你的 RobotAgent

    [Header("UI Components")]
    public TMP_InputField inputField;
    public Button sendButton;
    public GameObject chatPanel; // 整个对话框面板
    public ScrollRect scrollRect;

    [Header("Optional History Display")]
    public TMP_Text historyText; // 如果你想显示历史对话

    void Start()
    {
        // 自动绑定按钮点击事件
        if (sendButton != null)
        {
            sendButton.onClick.AddListener(OnSendClicked);
        }

        // 监听回车键发送
        if (inputField != null)
        {
            inputField.onSubmit.AddListener(delegate { OnSendClicked(); });
        }

        // 隐藏面板（可选，默认隐藏）
        // if (chatPanel != null) chatPanel.SetActive(false);
    }

    void Update()
    {
        // 按回车或 T 键切换对话框显示/隐藏
        if (Input.GetKeyDown(KeyCode.T) && chatPanel != null)
        {
            bool isActive = !chatPanel.activeSelf;
            chatPanel.SetActive(isActive);

            if (isActive)
            {
                inputField.ActivateInputField(); // 自动聚焦输入框
            }
        }
    }

    private void OnSendClicked()
    {
        string text = inputField.text.Trim();

        if (!string.IsNullOrEmpty(text))
        {
            // 1. 在UI上显示用户说的话
            AppendHistory($"<color=yellow>You:</color> {text}");

            // 2. 调用 RobotAgent 处理逻辑
            if (robotAgent != null)
            {
                robotAgent.OnUserInteract(text);
            }

            // 3. 清空输入框
            inputField.text = "";

            // 保持焦点以便继续输入
            inputField.ActivateInputField();
        }
    }

    /// <summary>
    /// 供外部调用，用于在对话框里显示机器人的回复
    /// </summary>
    public void AppendHistory(string message)
    {
        if (historyText != null)
        {
            historyText.text += $"{message}\n";

            // 【新增】自动滚动到底部
            if (scrollRect != null)
            {
                // 强制刷新 Layout 并滚动到底部
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }
}
