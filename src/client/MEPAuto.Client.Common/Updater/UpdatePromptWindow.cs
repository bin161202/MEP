using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MEPAuto.Client.Common.Updater
{
    /// <summary>
    /// Dialog hiển thị release notes + 3 lựa chọn: Cập nhật ngay / Để sau / Bỏ qua bản này.
    /// Code-only WPF (theo pattern LoginDialog) — tránh XAML compile complexity giữa net48 và net8.
    /// </summary>
    public class UpdatePromptWindow : Window
    {
        public enum Choice { UpdateNow, Later, SkipThisVersion }
        public Choice Result { get; private set; } = Choice.Later;

        public UpdatePromptWindow(UpdateState state)
        {
            Title = "MEPAuto — Có bản cập nhật mới";
            Width = 520; Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = state.Mandatory
                    ? $"Cần cập nhật bắt buộc: {state.CurrentVersion} → {state.LatestVersion}"
                    : $"Có bản mới {state.LatestVersion} (đang dùng {state.CurrentVersion})",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = state.Mandatory ? Brushes.OrangeRed : Brushes.DarkSlateBlue,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(header, 0); grid.Children.Add(header);

            var subText = new TextBlock
            {
                Text = state.Mandatory
                    ? "Phiên bản hiện tại không còn được hỗ trợ. Vui lòng cập nhật để tiếp tục sử dụng."
                    : "Bạn có muốn cập nhật ngay bây giờ không? Cần đóng Revit để cài.",
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(subText, 1); grid.Children.Add(subText);

            var notesLabel = new TextBlock
            {
                Text = "Thay đổi:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
            };
            var notesBox = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Content = new TextBlock
                {
                    Text = string.IsNullOrEmpty(state.ReleaseNotes)
                        ? "(Không có ghi chú phiên bản)"
                        : state.ReleaseNotes,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas, Courier New"),
                },
            };
            var notesPanel = new StackPanel();
            notesPanel.Children.Add(notesLabel);
            notesPanel.Children.Add(notesBox);
            Grid.SetRow(notesPanel, 2); grid.Children.Add(notesPanel);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };

            var updateBtn = new Button
            {
                Content = "Cập nhật ngay",
                Padding = new Thickness(16, 6, 16, 6),
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0),
            };
            updateBtn.Click += (s, e) => { Result = Choice.UpdateNow; DialogResult = true; Close(); };
            buttons.Children.Add(updateBtn);

            if (!state.Mandatory)
            {
                var laterBtn = new Button
                {
                    Content = "Để sau",
                    Padding = new Thickness(16, 6, 16, 6),
                    Margin = new Thickness(0, 0, 8, 0),
                };
                laterBtn.Click += (s, e) => { Result = Choice.Later; DialogResult = false; Close(); };
                buttons.Children.Add(laterBtn);

                var skipBtn = new Button
                {
                    Content = $"Bỏ qua {state.LatestVersion}",
                    Padding = new Thickness(16, 6, 16, 6),
                };
                skipBtn.Click += (s, e) => { Result = Choice.SkipThisVersion; DialogResult = false; Close(); };
                buttons.Children.Add(skipBtn);
            }

            Grid.SetRow(buttons, 3); grid.Children.Add(buttons);
            Content = grid;
        }
    }
}
