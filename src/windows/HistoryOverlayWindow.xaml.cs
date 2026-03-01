using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices; // 新增：引入底层交互支持

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

        private OllamaChatWindow? _currentOllamaWindow = null;

        private enum DockPosition { None, Top, Left, Right }
        private DockPosition _dockPosition = DockPosition.None;
        private bool _isHidden = false;
        private DispatcherTimer _hideDockTimer;

        // ==========================================
        // 引入底层 Win32 API，用来获取最精确的物理鼠标位置
        // ==========================================
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public HistoryOverlayWindow()
        {
            InitializeComponent();

            LoadConfig();

            this.Top = config.Top;
            this.Left = config.Left;
            this.Width = config.Width;
            this.Height = config.Height;
            OpacitySlider.Value = config.Opacity;

            _hideDockTimer = new DispatcherTimer();
            _hideDockTimer.Interval = TimeSpan.FromMilliseconds(300);
            _hideDockTimer.Tick += (s, e) =>
            {
                // 核心防闪烁拦截：如果鼠标实际上还在窗口里面，直接无视并打断隐藏动作！
                if (!IsMouseTrulyOutside())
                {
                    return; // 保留定时器，下次继续检查，直到鼠标真的离开
                }

                _hideDockTimer.Stop();
                PerformHide();
            };

            isLoaded = true;

            this.LocationChanged += (s, e) => SaveConfig();
            this.SizeChanged += (s, e) => SaveConfig();
            OpacitySlider.ValueChanged += (s, e) => SaveConfig();

            this.Loaded += (s, e) => CheckDocking();

            _ = RefreshHistoryAsync();

            Translator.TranslationLogged += OnTranslationLogged;

            Closed += (s, e) =>
            {
                Translator.TranslationLogged -= OnTranslationLogged;
                SaveConfig();

                if (_currentOllamaWindow != null && _currentOllamaWindow.IsLoaded)
                {
                    _currentOllamaWindow.Close();
                }
            };
        }

        // ==========================================
        // 精准判断鼠标是否“真的”离开了窗口范围
        // ==========================================
        private bool IsMouseTrulyOutside()
        {
            if (GetCursorPos(out POINT pt))
            {
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source != null && source.CompositionTarget != null)
                {
                    // 考虑缩放比例（DPI），将物理像素转换为 WPF 逻辑像素
                    Point logicalPoint = source.CompositionTarget.TransformFromDevice.Transform(new Point(pt.X, pt.Y));

                    // 增加 2 像素的容错缓冲带
                    if (logicalPoint.X >= this.Left - 2 && logicalPoint.X <= this.Left + this.Width + 2 &&
                        logicalPoint.Y >= this.Top - 2 && logicalPoint.Y <= this.Top + this.Height + 2)
                    {
                        return false; // 鼠标明明还在里面！
                    }
                }
            }
            return true; // 鼠标真的走了
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
                double saveTop = this.Top;
                double saveLeft = this.Left;

                // 保存时还原准确的边缘坐标
                if (_isHidden)
                {
                    var workArea = SystemParameters.WorkArea;
                    if (_dockPosition == DockPosition.Top) saveTop = workArea.Top;
                    if (_dockPosition == DockPosition.Left) saveLeft = workArea.Left;
                    if (_dockPosition == DockPosition.Right) saveLeft = workArea.Right - this.Width;
                }

                config.Top = saveTop;
                config.Left = saveLeft;
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

        private void OllamaAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is TranslationHistoryEntry entry)
                {
                    if (!string.IsNullOrEmpty(entry.SourceText))
                    {
                        if (_currentOllamaWindow == null || !_currentOllamaWindow.IsLoaded)
                        {
                            _currentOllamaWindow = new OllamaChatWindow(entry.SourceText);
                            _currentOllamaWindow.Closed += (s, args) => _currentOllamaWindow = null;
                            _currentOllamaWindow.Show();
                        }
                        else
                        {
                            _currentOllamaWindow.SourceTextBox.Text = entry.SourceText;
                            if (_currentOllamaWindow.WindowState == WindowState.Minimized)
                            {
                                _currentOllamaWindow.WindowState = WindowState.Normal;
                            }
                            _currentOllamaWindow.Activate();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ollama action failed: {ex.Message}");
            }
        }

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.BeginAnimation(Window.LeftProperty, null);
                this.BeginAnimation(Window.TopProperty, null);

                _hideDockTimer.Stop();
                _isHidden = false;
                _dockPosition = DockPosition.None;

                this.DragMove();

                CheckDocking();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Thumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb) return;

            _dockPosition = DockPosition.None;

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

        private void CheckDocking()
        {
            this.BeginAnimation(Window.LeftProperty, null);
            this.BeginAnimation(Window.TopProperty, null);

            var workArea = SystemParameters.WorkArea;
            double edgeTolerance = 30;

            // 恢复最精确的完美贴合
            if (this.Top <= workArea.Top + edgeTolerance)
            {
                _dockPosition = DockPosition.Top;
                this.Top = workArea.Top;
            }
            else if (this.Left <= workArea.Left + edgeTolerance)
            {
                _dockPosition = DockPosition.Left;
                this.Left = workArea.Left;
            }
            else if (this.Left + this.Width >= workArea.Right - edgeTolerance)
            {
                _dockPosition = DockPosition.Right;
                this.Left = workArea.Right - this.Width;
            }
            else
            {
                _dockPosition = DockPosition.None;
            }
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_dockPosition != DockPosition.None && !_isHidden)
            {
                // WPF 说鼠标离开了，但我们用定时器作为防抖
                // 300 毫秒后，由底层的 IsMouseTrulyOutside 来最后把关！
                _hideDockTimer.Start();
            }
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            // 鼠标一旦移入，立刻打断隐藏倒计时
            _hideDockTimer.Stop();

            if (_dockPosition != DockPosition.None && _isHidden)
            {
                PerformShow();
            }
        }

        private void PerformHide()
        {
            if (_dockPosition == DockPosition.None || _isHidden) return;
            _isHidden = true;

            var workArea = SystemParameters.WorkArea;
            double hiddenOffset = 15;

            if (_dockPosition == DockPosition.Top)
                AnimateWindow(Window.TopProperty, workArea.Top - this.Height + hiddenOffset);
            else if (_dockPosition == DockPosition.Left)
                AnimateWindow(Window.LeftProperty, workArea.Left - this.Width + hiddenOffset);
            else if (_dockPosition == DockPosition.Right)
                AnimateWindow(Window.LeftProperty, workArea.Right - hiddenOffset);
        }

        private void PerformShow()
        {
            if (_dockPosition == DockPosition.None || !_isHidden) return;
            _isHidden = false;

            var workArea = SystemParameters.WorkArea;

            if (_dockPosition == DockPosition.Top)
                AnimateWindow(Window.TopProperty, workArea.Top);
            else if (_dockPosition == DockPosition.Left)
                AnimateWindow(Window.LeftProperty, workArea.Left);
            else if (_dockPosition == DockPosition.Right)
                AnimateWindow(Window.LeftProperty, workArea.Right - this.Width);
        }

        private void AnimateWindow(DependencyProperty prop, double toValue)
        {
            DoubleAnimation anim = new DoubleAnimation(toValue, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            this.BeginAnimation(prop, anim);
        }
    }
}