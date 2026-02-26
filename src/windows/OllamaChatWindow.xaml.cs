using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LiveCaptionsTranslator
{
    // ==========================================
    // 扩充管家类：增加语言、牌型、位置、尺寸的记忆插槽
    // ==========================================
    public class OllamaChatSettings
    {
        public string ModelName { get; set; } = "qwen2.5:7b";
        public string PromptText { get; set; } = "你是一名专业的意大利语翻译与语言学习专家，熟悉意大利语口语表达、语法结构和高频词汇。\n\n我提供影视剧字幕中的单条意大利语句子。\n\n请严格按照以下格式输出：\n正面：(这里填入外语原文或者提炼出的生词)\n反面：(这里填入精准的中文翻译，以及语法或俚语解释)\n\n原文内容：\n{text}";

        public int LanguageIndex { get; set; } = 3; // 默认 3 (意大利语)
        public int CardTypeIndex { get; set; } = 1; // 默认 1 (Basic and reversed)

        public double Top { get; set; } = double.NaN;
        public double Left { get; set; } = double.NaN;
        public double Width { get; set; } = 550;
        public double Height { get; set; } = 600;
    }

    public partial class OllamaChatWindow : Window
    {
        private const string OllamaApiUrl = "http://localhost:11434/api/generate";
        private const string AnkiApiUrl = "http://localhost:8765";
        private const string ConfigFile = "OllamaChatSettings.json";

        private OllamaChatSettings config = new OllamaChatSettings();
        private CancellationTokenSource? _cts;
        private bool _isGenerating = false;
        private DispatcherTimer _typingTimer;
        private bool _isLoaded = false; // 用于防止窗口初始化时误触发保存

        private MediaPlayer _audioPlayer = new MediaPlayer();

        public OllamaChatWindow(string sourceText)
        {
            InitializeComponent();

            // 1. 读取历史记忆
            LoadConfig();

            // 2. 将记忆应用到界面元素
            ModelTextBox.Text = config.ModelName;
            PromptTextBox.Text = config.PromptText;
            SourceTextBox.Text = sourceText;
            LangComboBox.SelectedIndex = config.LanguageIndex;
            CardTypeComboBox.SelectedIndex = config.CardTypeIndex;

            // 3. 将记忆应用到窗口位置与大小
            if (!double.IsNaN(config.Top) && !double.IsNaN(config.Left))
            {
                // 如果有历史位置记录，则取消居中，使用自定义位置
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.Top = config.Top;
                this.Left = config.Left;
            }
            this.Width = config.Width;
            this.Height = config.Height;

            _isLoaded = true; // 标记初始化完成，允许保存

            // 4. 监听所有的变化：位置、大小、下拉框选项改变时，立刻存档
            this.LocationChanged += (s, e) => SaveConfig();
            this.SizeChanged += (s, e) => SaveConfig();
            LangComboBox.SelectionChanged += (s, e) => SaveConfig();
            CardTypeComboBox.SelectionChanged += (s, e) => SaveConfig();
            this.Closed += (s, e) => SaveConfig();

            // 5. 初始化打字防抖计时器
            _typingTimer = new DispatcherTimer();
            _typingTimer.Interval = TimeSpan.FromMilliseconds(800);
            _typingTimer.Tick += TypingTimer_Tick;

            SourceTextBox.TextChanged += AutoGenerate_TextChanged;
            PromptTextBox.TextChanged += AutoGenerate_TextChanged;

            StartGeneration();
        }

        private void AutoGenerate_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_typingTimer != null)
            {
                _typingTimer.Stop();
                _typingTimer.Start();
            }
        }

        private void TypingTimer_Tick(object? sender, EventArgs e)
        {
            _typingTimer.Stop();
            if (_isGenerating) _cts?.Cancel();
            StartGeneration();
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    string json = File.ReadAllText(ConfigFile);
                    config = JsonSerializer.Deserialize<OllamaChatSettings>(json) ?? new OllamaChatSettings();

                    // 安全防护：防止多屏幕断开后窗口跑到屏幕外
                    if (!double.IsNaN(config.Left) && !double.IsNaN(config.Top))
                    {
                        if (config.Left < SystemParameters.VirtualScreenLeft || config.Top < SystemParameters.VirtualScreenTop ||
                            config.Left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 ||
                            config.Top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
                        {
                            config.Left = double.NaN;
                            config.Top = double.NaN;
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            if (!_isLoaded) return; // 界面还没完全画好前，不要瞎存
            try
            {
                config.ModelName = ModelTextBox.Text.Trim();
                config.PromptText = PromptTextBox.Text;
                config.LanguageIndex = LangComboBox.SelectedIndex;
                config.CardTypeIndex = CardTypeComboBox.SelectedIndex;

                config.Top = this.Top;
                config.Left = this.Left;
                config.Width = this.Width;
                config.Height = this.Height;

                string json = JsonSerializer.Serialize(config);
                File.WriteAllText(ConfigFile, json);
            }
            catch { }
        }

        private void GenerateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isGenerating) _cts?.Cancel();
            else StartGeneration();
        }

        private void StartGeneration()
        {
            _cts = new CancellationTokenSource();
            _ = CallOllamaStreamAsync(_cts.Token);
        }

        private async Task CallOllamaStreamAsync(CancellationToken cancellationToken)
        {
            string modelName = ModelTextBox.Text.Trim();
            string rawPrompt = PromptTextBox.Text;
            string sourceText = SourceTextBox.Text;

            if (string.IsNullOrEmpty(modelName)) return;

            string finalPrompt = rawPrompt.Replace("{text}", sourceText);

            _isGenerating = true;
            GenerateBtn.Content = "停止生成";
            GenerateBtn.Foreground = Brushes.LightCoral;
            ResponseTextBox.Text = "";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);

                    var requestData = new { model = modelName, prompt = finalPrompt, stream = true };
                    string jsonContent = JsonSerializer.Serialize(requestData);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var request = new HttpRequestMessage(HttpMethod.Post, OllamaApiUrl) { Content = content };
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();

                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var reader = new StreamReader(stream))
                        {
                            string? line;
                            while ((line = await reader.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
                            {
                                using (JsonDocument doc = JsonDocument.Parse(line))
                                {
                                    if (doc.RootElement.TryGetProperty("response", out var respElement))
                                    {
                                        string? chunk = respElement.GetString();
                                        if (chunk != null)
                                        {
                                            await Application.Current.Dispatcher.InvokeAsync(() =>
                                            {
                                                ResponseTextBox.AppendText(chunk);
                                                ResponseTextBox.ScrollToEnd();
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _isGenerating = false;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        GenerateBtn.Content = "发送 / 重新生成";
                        GenerateBtn.Foreground = Brushes.White;
                    });
                }
                SaveConfig(); // 生成完毕顺便再保存一次
            }
        }

        private async void AnkiBtn_Click(object sender, RoutedEventArgs e)
        {
            string text = ResponseTextBox.Text;

            var frontMatch = Regex.Match(text, @"(?:正面|front|前)[:：]\s*(.*?)(?=\s*(?:反面|back|后)[:：]|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var backMatch = Regex.Match(text, @"(?:反面|back|后)[:：]\s*(.*)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (!frontMatch.Success || !backMatch.Success)
            {
                MessageBox.Show("解析失败！\n请确保回复框中包含 '正面：' 和 '反面：' 等格式前缀。", "格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string frontText = frontMatch.Groups[1].Value.Trim();
            string backText = backMatch.Groups[1].Value.Trim().Replace("\n", "<br>");

            string cardType = (CardTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Basic";

            string langCode = "it";
            if (LangComboBox.SelectedItem is ComboBoxItem selectedLang)
            {
                langCode = selectedLang.Tag?.ToString() ?? "it";
            }

            AnkiBtn.IsEnabled = false;
            AnkiBtn.Content = "处理中...";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string ttsUrl = $"https://translate.google.com/translate_tts?ie=UTF-8&tl={langCode}&client=tw-ob&q={Uri.EscapeDataString(frontText)}";
                    byte[] audioBytes = await client.GetByteArrayAsync(ttsUrl);

                    string b64Audio = Convert.ToBase64String(audioBytes);
                    string filename = "anki_ai_" + Guid.NewGuid().ToString("N") + ".mp3";

                    string tempFile = Path.Combine(Path.GetTempPath(), filename);
                    File.WriteAllBytes(tempFile, audioBytes);
                    _audioPlayer.Open(new Uri(tempFile));
                    _audioPlayer.Play();

                    var mediaPayload = new
                    {
                        action = "storeMediaFile",
                        version = 6,
                        @params = new { filename = filename, data = b64Audio }
                    };
                    await client.PostAsync(AnkiApiUrl, new StringContent(JsonSerializer.Serialize(mediaPayload), Encoding.UTF8, "application/json"));

                    var notePayload = new
                    {
                        action = "addNote",
                        version = 6,
                        @params = new
                        {
                            note = new
                            {
                                deckName = "Default",
                                modelName = cardType,
                                fields = new
                                {
                                    Front = frontText + $"<br><br>[sound:{filename}]",
                                    Back = backText
                                },
                                tags = new[] { "AI自动" }
                            }
                        }
                    };

                    var response = await client.PostAsync(AnkiApiUrl, new StringContent(JsonSerializer.Serialize(notePayload), Encoding.UTF8, "application/json"));
                    string resultStr = await response.Content.ReadAsStringAsync();

                    if (resultStr.Contains("\"error\": null") || resultStr.Contains("\"error\":null"))
                    {
                        AnkiBtn.Content = "制卡成功！";
                        AnkiBtn.Foreground = Brushes.LightGreen;

                        Task.Run(async () => {
                            await Task.Delay(3000);
                            if (File.Exists(tempFile)) File.Delete(tempFile);
                        });
                    }
                    else
                    {
                        MessageBox.Show($"Anki 报错:\n{resultStr}", "制卡失败", MessageBoxButton.OK, MessageBoxImage.Error);
                        AnkiBtn.Content = "一键传给 Anki";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"网络请求或 AnkiConnect 发生错误:\n{ex.Message}\n\n请确认 Anki 已打开并且安装了 AnkiConnect 插件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                AnkiBtn.Content = "一键传给 Anki";
            }
            finally
            {
                await Task.Delay(3000);
                AnkiBtn.IsEnabled = true;
                AnkiBtn.Content = "一键传给 Anki";
                AnkiBtn.Foreground = Brushes.White;
            }
        }
    }
}