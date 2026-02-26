using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.IO;                  // 新增：用于文件读写
using System.Text.Json;           // 新增：用于解析和保存 JSON 配置

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    // 新增：专门用来存放历史窗口设置的小管家类
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
        // 新增：配置文件相关的变量
        private const string ConfigFile = "HistoryOverlayConfig.json";
        private HistoryWindowConfig config = new HistoryWindowConfig();
        private bool isLoaded = false; // 用于防止在窗口初始化时误触发保存

        public HistoryOverlayWindow()
        {
            InitializeComponent();

            // 1. 读取历史配置
            LoadConfig();

            // 2. 将配置应用到当前窗口
            this.Top = config.Top;
            this.Left = config.Left;
            this.Width = config.Width;
            this.Height = config.Height;
            OpacitySlider.Value = config.Opacity;

            isLoaded = true; // 标记加载完成

            // 3. 监听变化，一旦改变立即保存
            this.LocationChanged += (s, e) => SaveConfig();
            this.SizeChanged += (s, e) => SaveConfig();
            OpacitySlider.ValueChanged += (s, e) => SaveConfig();

            // 原有的数据库加载逻辑
            _ = RefreshHistoryAsync();

            Translator.TranslationLogged += OnTranslationLogged;

            Closed += (s, e) =>
            {
                Translator.TranslationLogged -= OnTranslationLogged;
                SaveConfig(); // 窗口关闭时再强制保存一次，确保万无一失
            };
        }

        // ==========================================
        // 新增：读取与保存配置的逻辑
        // ==========================================
        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    string json = File.ReadAllText(ConfigFile);
                    config = JsonSerializer.Deserialize<HistoryWindowConfig>(json) ?? new HistoryWindowConfig();

                    // 安全防护：防止外接显示器拔掉后，窗口定位在屏幕外导致找不到
                    if (config.Left < SystemParameters.VirtualScreenLeft || config.Top < SystemParameters.VirtualScreenTop ||
                        config.Left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 ||
                        config.Top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
                    {
                        config.Left = 100;
                        config.Top = 100;
                    }
                }
            }
            catch { } // 如果配置文件损坏或不存在，就不管它，使用默认值
        }

        private void SaveConfig()
        {
            if (!isLoaded) return; // 窗口还没完全加载完时不保存
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
        // ==========================================


        // ==========================================
        // 以下为你原本的代码逻辑，一字未改，完美保留
        // ==========================================
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