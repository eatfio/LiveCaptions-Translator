using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class HistoryOverlayWindow : Window
    {
        public HistoryOverlayWindow()
        {
            InitializeComponent();

            // 首次打开窗口时加载历史数据
            _ = RefreshHistoryAsync();

            // 监听底层翻译完成的事件
            Translator.TranslationLogged += OnTranslationLogged;

            // 窗口关闭时注销事件
            Closed += (s, e) =>
            {
                Translator.TranslationLogged -= OnTranslationLogged;
            };
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
                // 异常保护
            }
        }

        // 新增：点击按钮复制源文本的功能
        private void CopySourceText_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取按钮所在行的内容并复制到剪贴板
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