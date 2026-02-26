using System.Windows;

namespace LiveCaptionsTranslator
{
    public partial class OllamaChatWindow : Window
    {
        // 构造函数：强制要求传入外语原文
        public OllamaChatWindow(string sourceText)
        {
            InitializeComponent();

            // 将传进来的外语显示在界面的文本框里
            SourceTextBox.Text = sourceText;

            // 预留的 AI 提示
            ResponseTextBox.Text = "窗口接收数据成功！\n等待后续接入 Ollama API 服务...";
        }
    }
}