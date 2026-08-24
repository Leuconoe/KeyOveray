using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using KeyOverlay.Widget.Models;
using KeyOverlay.Widget.Services;
using Microsoft.Gaming.XboxGameBar;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace KeyOverlay.Widget
{
    public sealed partial class OverlayPage : Page
    {
        private readonly InputBridgeClient _inputBridge = new InputBridgeClient();
        private XboxGameBarWidget _widget;
        private XboxGameBarAppTargetTracker _targetTracker;
        private DisplayInformation _displayInformation;
        private bool _isEditMode;
        private bool _isCollapsed;
        private int _columns;
        private Size? _expandedWindowSize;
        private int _resizeRevision;

        public ObservableCollection<KeyButtonDefinition> Keys { get; }
            = new ObservableCollection<KeyButtonDefinition>();

        public OverlayPage()
        {
            InitializeComponent();

            var settings = SettingsService.Load();
            _columns = settings.Columns;
            _isCollapsed = settings.IsCollapsed;
            foreach (var key in settings.Keys)
            {
                Keys.Add(key);
            }

            Keys.CollectionChanged += Keys_CollectionChanged;
            KeyGrid.ItemsSource = Keys;
            Loaded += OverlayPage_Loaded;
            Unloaded += OverlayPage_Unloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _widget = e.Parameter as XboxGameBarWidget;
            if (_widget == null)
            {
                return;
            }

            _widget.PinningSupported = true;
            _widget.MinWindowSize = new Size(280, 88);
            _widget.MaxWindowSize = new Size(720, 720);
            _widget.ClickThroughEnabledChanged += Widget_ClickThroughEnabledChanged;
            _widget.WindowBoundsChanged += Widget_WindowBoundsChanged;

            CaptureExpandedWindowSize(_widget);

            _targetTracker = new XboxGameBarAppTargetTracker(_widget);
            _targetTracker.TargetChanged += TargetTracker_TargetChanged;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_widget != null)
            {
                _widget.ClickThroughEnabledChanged -= Widget_ClickThroughEnabledChanged;
                _widget.WindowBoundsChanged -= Widget_WindowBoundsChanged;
            }
            if (_targetTracker != null)
            {
                _targetTracker.TargetChanged -= TargetTracker_TargetChanged;
            }
            base.OnNavigatedFrom(e);
        }

        private async void OverlayPage_Loaded(object sender, RoutedEventArgs e)
        {
            _displayInformation = DisplayInformation.GetForCurrentView();
            _displayInformation.OrientationChanged += DisplayInformation_Changed;
            _displayInformation.DpiChanged += DisplayInformation_Changed;
            DisplayInformation.DisplayContentsInvalidated += DisplayInformation_Changed;

            ApplyCollapsedState();
            ApplyEditMode();
            UpdateGridMetrics();
            UpdateEmptyState();
            RefreshTargetStatus();

            try
            {
                await _inputBridge.EnsureStartedAsync();
                RefreshTargetStatus();
            }
            catch
            {
                StatusText.Text = "브리지 오류";
                StatusText.Foreground = ResourceBrush("ColorDangerBrush");
            }

            await ResizeWidgetAsync();
        }

        private void OverlayPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_displayInformation != null)
            {
                _displayInformation.OrientationChanged -= DisplayInformation_Changed;
                _displayInformation.DpiChanged -= DisplayInformation_Changed;
                DisplayInformation.DisplayContentsInvalidated -= DisplayInformation_Changed;
                _displayInformation = null;
            }
        }

        private async void DisplayInformation_Changed(DisplayInformation sender, object args)
        {
            if (_widget == null)
            {
                return;
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                await Task.Delay(180);
                await ResizeWidgetAsync();
                await _widget.CenterWindowAsync();
            });
        }

        private void Widget_ClickThroughEnabledChanged(XboxGameBarWidget sender, object args)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, RefreshTargetStatus);
        }

        private void Widget_WindowBoundsChanged(XboxGameBarWidget sender, object args)
        {
            if (!_isCollapsed && !_isEditMode)
            {
                CaptureExpandedWindowSize(sender);
            }
        }

        private void TargetTracker_TargetChanged(XboxGameBarAppTargetTracker sender, object args)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, RefreshTargetStatus);
        }

        private void RefreshTargetStatus()
        {
            StatusText.Foreground = ResourceBrush("ColorMutedBrush");
            if (_widget != null && _widget.ClickThroughEnabled)
            {
                StatusText.Text = "클릭 통과 켜짐 · Win + G에서 끄세요";
                return;
            }

            try
            {
                var target = _targetTracker?.GetTarget();
                StatusText.Text = target != null && target.IsGame
                    ? "대상 · " + target.DisplayName
                    : "게임 입력 대기";
            }
            catch
            {
                StatusText.Text = "게임 입력 대기";
            }
        }

        private async void KeyGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (_isEditMode || !(e.ClickedItem is KeyButtonDefinition key))
            {
                return;
            }

            var sent = await _inputBridge.SendAsync(key);
            if (!sent)
            {
                StatusText.Text = "입력 실패";
                StatusText.Foreground = ResourceBrush("ColorDangerBrush");
            }
            else
            {
                RefreshTargetStatus();
            }
        }

        private void KeyGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeleteButton.IsEnabled = _isEditMode && KeyGrid.SelectedItem != null;
        }

        private void KeyGrid_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            SaveSettings();
        }

        private async void CenterButton_Click(object sender, RoutedEventArgs e)
        {
            if (_widget != null)
            {
                await _widget.CenterWindowAsync();
            }
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            _isEditMode = !_isEditMode;
            ApplyEditMode();
            await ResizeWidgetAsync();
        }

        private async void CollapseButton_Click(object sender, RoutedEventArgs e)
        {
            var isExpanding = _isCollapsed;
            if (!_isCollapsed && _isEditMode)
            {
                _isEditMode = false;
                ApplyEditMode();
                _expandedWindowSize = CalculateExpandedWindowSize();
            }
            else if (!_isCollapsed)
            {
                CaptureExpandedWindowSize(_widget);
            }

            _isCollapsed = !_isCollapsed;
            ApplyCollapsedState();
            SaveSettings();
            await ResizeWidgetAsync(isExpanding);
        }

        private void ApplyCollapsedState()
        {
            GridBody.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
            EditPanel.Visibility = !_isCollapsed && _isEditMode ? Visibility.Visible : Visibility.Collapsed;
            CollapseButton.Content = _isCollapsed ? "펼치기" : "접기";
            EditButton.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ApplyEditMode()
        {
            EditPanel.Visibility = !_isCollapsed && _isEditMode ? Visibility.Visible : Visibility.Collapsed;
            EditButton.Content = _isEditMode ? "완료" : "편집";
            KeyGrid.SelectionMode = _isEditMode ? ListViewSelectionMode.Single : ListViewSelectionMode.None;
            KeyGrid.CanReorderItems = _isEditMode;
            KeyGrid.ReorderMode = _isEditMode ? ListViewReorderMode.Enabled : ListViewReorderMode.Disabled;
            if (!_isEditMode)
            {
                KeyGrid.SelectedItem = null;
            }
            ColumnCountText.Text = _columns + "열";
        }

        private void AddKeyButton_Click(object sender, RoutedEventArgs e)
        {
            CapturePanel.Visibility = Visibility.Visible;
            CaptureSink.Content = "키 입력 대기";
            CaptureSink.Focus(FocusState.Programmatic);
        }

        private void CancelCaptureButton_Click(object sender, RoutedEventArgs e)
        {
            CapturePanel.Visibility = Visibility.Collapsed;
            EditButton.Focus(FocusState.Programmatic);
        }

        private async void CaptureSink_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (IsModifierKey(e.Key))
            {
                CaptureSink.Content = "마지막 키를 함께 누르세요";
                e.Handled = true;
                return;
            }

            var modifiers = CurrentModifiers();
            var displayName = BuildDisplayName((int)e.Key, modifiers);
            e.Handled = true;

            if (Keys.Any(item => item.VirtualKey == (int)e.Key && item.Modifiers == modifiers))
            {
                CaptureSink.Content = "이미 등록된 키입니다";
                return;
            }

            Keys.Add(KeyButtonDefinition.Create((int)e.Key, displayName, modifiers));
            CapturePanel.Visibility = Visibility.Collapsed;
            UpdateGridMetrics();
            UpdateEmptyState();
            await ResizeWidgetAsync();
        }

        private static bool IsModifierKey(VirtualKey key)
        {
            return key == VirtualKey.Control
                || key == VirtualKey.Menu
                || key == VirtualKey.Shift
                || key == VirtualKey.LeftWindows
                || key == VirtualKey.RightWindows;
        }

        private static VirtualKeyModifiers CurrentModifiers()
        {
            var coreWindow = Window.Current.CoreWindow;
            var modifiers = VirtualKeyModifiers.None;
            if (IsKeyDown(coreWindow, VirtualKey.Control))
            {
                modifiers |= VirtualKeyModifiers.Control;
            }
            if (IsKeyDown(coreWindow, VirtualKey.Menu))
            {
                modifiers |= VirtualKeyModifiers.Menu;
            }
            if (IsKeyDown(coreWindow, VirtualKey.Shift))
            {
                modifiers |= VirtualKeyModifiers.Shift;
            }
            if (IsKeyDown(coreWindow, VirtualKey.LeftWindows) || IsKeyDown(coreWindow, VirtualKey.RightWindows))
            {
                modifiers |= VirtualKeyModifiers.Windows;
            }
            return modifiers;
        }

        private static bool IsKeyDown(CoreWindow window, VirtualKey key)
        {
            return (window.GetKeyState(key) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        }

        private static string BuildDisplayName(int virtualKey, VirtualKeyModifiers modifiers)
        {
            var keyName = KeyName(virtualKey);
            var prefix = string.Empty;
            if ((modifiers & VirtualKeyModifiers.Control) != 0) prefix += "CTRL+";
            if ((modifiers & VirtualKeyModifiers.Shift) != 0) prefix += "SHIFT+";
            if ((modifiers & VirtualKeyModifiers.Menu) != 0) prefix += "ALT+";
            if ((modifiers & VirtualKeyModifiers.Windows) != 0) prefix += "WIN+";
            return prefix + keyName;
        }

        private static string KeyName(int virtualKey)
        {
            if (virtualKey >= 0x30 && virtualKey <= 0x39)
            {
                return ((char)virtualKey).ToString();
            }
            if (virtualKey >= 0x41 && virtualKey <= 0x5A)
            {
                return ((char)virtualKey).ToString();
            }
            if (virtualKey >= 0x70 && virtualKey <= 0x87)
            {
                return "F" + (virtualKey - 0x6F);
            }
            if (virtualKey >= 0x60 && virtualKey <= 0x69)
            {
                return "NUM " + (virtualKey - 0x60);
            }

            switch (virtualKey)
            {
                case 0x08: return "BACK";
                case 0x09: return "TAB";
                case 0x0D: return "ENTER";
                case 0x13: return "PAUSE";
                case 0x14: return "CAPS";
                case 0x1B: return "ESC";
                case 0x20: return "SPACE";
                case 0x21: return "PGUP";
                case 0x22: return "PGDN";
                case 0x23: return "END";
                case 0x24: return "HOME";
                case 0x25: return "LEFT";
                case 0x26: return "UP";
                case 0x27: return "RIGHT";
                case 0x28: return "DOWN";
                case 0x2D: return "INS";
                case 0x2E: return "DEL";
                case 0x6A: return "NUM *";
                case 0x6B: return "NUM +";
                case 0x6D: return "NUM −";
                case 0x6E: return "NUM .";
                case 0x6F: return "NUM /";
                case 0x90: return "NUM LOCK";
                case 0x91: return "SCROLL";
                default: return ((VirtualKey)virtualKey).ToString().ToUpperInvariant();
            }
        }

        private async void AddColumnButton_Click(object sender, RoutedEventArgs e)
        {
            if (_columns >= 8) return;
            _columns++;
            ColumnCountText.Text = _columns + "열";
            UpdateGridMetrics();
            SaveSettings();
            await ResizeWidgetAsync();
        }

        private async void RemoveColumnButton_Click(object sender, RoutedEventArgs e)
        {
            if (_columns <= 2) return;
            _columns--;
            ColumnCountText.Text = _columns + "열";
            UpdateGridMetrics();
            SaveSettings();
            await ResizeWidgetAsync();
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (KeyGrid.SelectedItem is KeyButtonDefinition selected)
            {
                Keys.Remove(selected);
                KeyGrid.SelectedItem = null;
                UpdateGridMetrics();
                UpdateEmptyState();
                await ResizeWidgetAsync();
            }
        }

        private void Keys_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SaveSettings();
            UpdateEmptyState();
        }

        private void UpdateGridMetrics()
        {
            KeyGrid.UpdateLayout();
            if (KeyGrid.ItemsPanelRoot is ItemsWrapGrid panel)
            {
                panel.MaximumRowsOrColumns = _columns;
            }
        }

        private void UpdateEmptyState()
        {
            EmptyStateText.Visibility = Keys.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Size CalculateExpandedWindowSize()
        {
            var minimumWidth = _isEditMode ? 440 : 320;
            var width = Math.Max(minimumWidth, _columns * 72 + 32);
            var rows = Math.Max(1, (int)Math.Ceiling(Keys.Count / (double)_columns));
            var height = 76 + rows * 64 + (_isEditMode ? 64 : 0);
            return new Size(width, Math.Min(700, height));
        }

        private void CaptureExpandedWindowSize(XboxGameBarWidget widget)
        {
            if (widget == null)
            {
                return;
            }

            try
            {
                var bounds = widget.WindowBounds;
                if (bounds.Width >= 280 && bounds.Height > 88)
                {
                    _expandedWindowSize = new Size(bounds.Width, bounds.Height);
                }
            }
            catch
            {
            }
        }

        private async Task ResizeWidgetAsync(bool restoreExpandedSize = false)
        {
            if (_widget == null)
            {
                return;
            }

            Size targetSize;
            if (_isCollapsed)
            {
                if (!_expandedWindowSize.HasValue)
                {
                    _expandedWindowSize = CalculateExpandedWindowSize();
                }
                targetSize = new Size(_expandedWindowSize.Value.Width, 88);
            }
            else if (restoreExpandedSize && _expandedWindowSize.HasValue)
            {
                targetSize = _expandedWindowSize.Value;
            }
            else
            {
                targetSize = CalculateExpandedWindowSize();
                _expandedWindowSize = targetSize;
            }

            var revision = ++_resizeRevision;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(90 * attempt);
                }
                if (revision != _resizeRevision)
                {
                    return;
                }

                try
                {
                    if (await _widget.TryResizeWindowAsync(targetSize))
                    {
                        return;
                    }
                }
                catch
                {
                    // Game Bar can briefly reject resize requests while its layout is changing.
                }
            }
        }

        private void SaveSettings()
        {
            SettingsService.Save(_columns, _isCollapsed, Keys);
        }

        private static Brush ResourceBrush(string key)
        {
            return (Brush)Application.Current.Resources[key];
        }
    }
}
