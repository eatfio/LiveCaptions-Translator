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

            // 监听底层翻译完成的事件（只要有新翻译，就会触发刷新）
            Translator.TranslationLogged += OnTranslationLogged;

            // 窗口关闭时注销事件，防止后台继续占用资源引发内存泄漏
            Closed += (s, e) =>
            {
                Translator.TranslationLogged -= OnTranslationLogged;
            };
        }

        private void OnTranslationLogged()
        {
            // 收到新翻译时，在后台刷新列表
            _ = RefreshHistoryAsync();
        }

        private async Task RefreshHistoryAsync()
        {
            try
            {
                // 完美复用 HistoryPage.xaml.cs 的数据库读取逻辑
                // 1 表示第一页，30 表示获取最新 30 条记录（你可以按需把 30 改成 50 或 100）
                var data = await SQLiteHistoryLogger.LoadHistoryAsync(1, 30, string.Empty);
                List<TranslationHistoryEntry> historyList = data.Item1;

                // 数据库返回的数据通常是最新记录在最前面。作为悬浮窗，我们习惯最新的消息在最下面，所以反转它
                historyList.Reverse();

                await Dispatcher.InvokeAsync(() =>
                {
                    HistoryList.ItemsSource = historyList;
                    // 数据绑定后，自动滚动到底部
                    HistoryScrollViewer.ScrollToBottom();
                });
            }
            catch
            {
                // 捕获异常：防止在多线程高并发读写数据库时导致程序崩溃
            }
        }

        // --- 以下是窗口拖拽与缩放逻辑（与之前保持一致） ---

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