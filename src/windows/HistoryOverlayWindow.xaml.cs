using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.IO;
using System.Text.Json;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public class HistoryWindowConfig
    {
        public double Top { get; set; } = 100;
        public double Left { get; set; } = 100;
        public double Width { get; set; } = 550;
        public double Height { get; set; } = 400;
        public double Opacity { get; set; } = 0.85;
    }

    public partial class HistoryOverlayWindow : Window
    {
        private const string ConfigFile = "HistoryOverlayConfig.json";
        private HistoryWindowConfig config = new HistoryWindowConfig();
        private bool isLoaded = false;

        public HistoryOverlayWindow()
        {
            InitializeComponent();

            LoadConfig();

            this.Top = config.Top;
            this.Left = config.Left;
            this.Width = config.Width;
            this.Height = config.Height;
            OpacitySlider.Value = config.Opacity;

            isLoaded = true;

            this.LocationChanged += (s, e) => SaveConfig();
            this.SizeChanged += (s, e) => SaveConfig();
            OpacitySlider.ValueChanged += (s, e) => SaveConfig();

            _ = RefreshHistoryAsync();

            Translator.TranslationLogged += OnTranslationLogged;

            Closed += (s, e) =>
            {
                Translator.TranslationLogged -= OnTranslationLogged;
                SaveConfig();
            };
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    string json = File.ReadAllText(ConfigFile);
                    config = JsonSerializer.Deserialize<HistoryWindowConfig>(json) ?? new HistoryWindowConfig();

                    if (config.Left < SystemParameters.VirtualScreenLeft || config.Top < SystemParameters.VirtualScreenTop ||
                        config.Left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 ||
                        config.Top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
                    {
                        config.Left = 100;
                        config.Top = 100;
                    }
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            if (!isLoaded) return;
            try
            {
                config.Top = this.Top;
                config.Left = this.Left;
                config.Width = this.Width;
                config.Height = this.Height;
                config.Opacity = OpacitySlider.Value;

                string json = JsonSerializer.Serialize(config);
                File.WriteAllText(ConfigFile, json);
            }
            catch { }
        }

        private void OnTranslationLogged()
        {
            _ = RefreshHistoryAsync();
        }

        private async Task RefreshHistoryAsync()
        {
            try
            {
                var data = await SQLiteHistoryLogger.LoadHistoryAsync(1, 30, string.Empty);
                List<TranslationHistoryEntry> historyList = data.Item1;
                historyList.Reverse();

                await Dispatcher.InvokeAsync(() =>
                {
                    HistoryList.ItemsSource = historyList;
                    HistoryScrollViewer.ScrollToBottom();
                });
            }
            catch
            {
                // 忽略数据库并发读取异常
            }
        }

        private void CopySourceText_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is TranslationHistoryEntry entry)
                {
                    if (!string.IsNullOrEmpty(entry.SourceText))
                    {
                        Clipboard.SetText(entry.SourceText);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Copy failed: {ex.Message}");
            }
        }

        // ==========================================
        // 新增：点击 Ollama 星星按钮的逻辑
        // ==========================================
        private void OllamaAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is TranslationHistoryEntry entry)
                {
                    if (!string.IsNullOrEmpty(entry.SourceText))
                    {
                        // 实例化新窗口，并将原文传入
                        var ollamaWindow = new OllamaChatWindow(entry.SourceText);
                        ollamaWindow.Show();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ollama action failed: {ex.Message}");
            }
        }
        // ==========================================

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Thumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb) return;

            double newWidth = this.Width;
            double newHeight = this.Height;
            double newLeft = this.Left;
            double newTop = this.Top;

            if (thumb.Name.Contains("Left"))
            {
                newWidth -= e.HorizontalChange;
                newLeft += e.HorizontalChange;
            }
            if (thumb.Name.Contains("Right"))
            {
                newWidth += e.HorizontalChange;
            }
            if (thumb.Name.Contains("Top"))
            {
                newHeight -= e.VerticalChange;
                newTop += e.VerticalChange;
            }
            if (thumb.Name.Contains("Bottom"))
            {
                newHeight += e.VerticalChange;
            }

            if (newWidth >= this.MinWidth)
            {
                this.Width = newWidth;
                this.Left = newLeft;
            }
            if (newHeight >= this.MinHeight)
            {
                this.Height = newHeight;
                this.Top = newTop;
            }
        }
    }
}