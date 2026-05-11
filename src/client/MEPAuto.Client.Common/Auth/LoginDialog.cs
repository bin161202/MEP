using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MEPAuto.Contracts.Auth;

namespace MEPAuto.Client.Common.Auth
{
    /// <summary>
    /// WPF window đăng nhập — hiện lúc Revit khởi động (nếu cache hết hạn) hoặc khi session expire.
    /// Code-only (không XAML) để tránh phức tạp multi-target net48/net8 XAML compile.
    /// </summary>
    public class LoginDialog : Window
    {
        private readonly IServerProxy _server;
        private readonly JwtCache _cache;
        private readonly TextBox _emailBox;
        private readonly PasswordBox _passwordBox;
        private readonly TextBlock _errorText;
        private readonly Button _loginButton;

        public bool LoggedIn { get; private set; }
        public JwtCache.Payload? Token { get; private set; }

        public LoginDialog(IServerProxy server, JwtCache cache, string serverBaseUrl = "")
        {
            _server = server;
            _cache = cache;

            Title = "MEPAuto — Đăng nhập";
            Width = 400; Height = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var emailLabel = new TextBlock { Text = "Email:", Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetRow(emailLabel, 0); grid.Children.Add(emailLabel);

            _emailBox = new TextBox { Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(4) };
            Grid.SetRow(_emailBox, 1); grid.Children.Add(_emailBox);

            var pwdLabel = new TextBlock { Text = "Mật khẩu:", Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetRow(pwdLabel, 2); grid.Children.Add(pwdLabel);

            _passwordBox = new PasswordBox { Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(4) };
            _passwordBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) _ = OnLoginClick();
            };
            Grid.SetRow(_passwordBox, 3); grid.Children.Add(_passwordBox);

            _errorText = new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.Red,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4),
                Visibility = Visibility.Collapsed,
            };
            Grid.SetRow(_errorText, 4); grid.Children.Add(_errorText);

            _loginButton = new Button
            {
                Content = "Đăng nhập",
                Padding = new Thickness(20, 6, 20, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true,
            };
            _loginButton.Click += async (s, e) => await OnLoginClick();
            Grid.SetRow(_loginButton, 6); grid.Children.Add(_loginButton);

            // Footer hiển thị ServerBaseUrl đang dùng — giúp member phát hiện trỏ sai
            // (vd `localhost:5000` thay vì VPS production). Xem ERR-028.
            if (!string.IsNullOrWhiteSpace(serverBaseUrl))
            {
                var serverInfo = new TextBlock
                {
                    Text = "Server: " + serverBaseUrl,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 11,
                    Margin = new Thickness(0, 8, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(serverInfo, 6); grid.Children.Add(serverInfo);
            }

            Content = grid;
            Loaded += (s, e) => _emailBox.Focus();
        }

        private async Task OnLoginClick()
        {
            var email = _emailBox.Text.Trim();
            var password = _passwordBox.Password;
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowError("Vui lòng nhập email và mật khẩu.");
                return;
            }

            _loginButton.IsEnabled = false;
            _errorText.Visibility = Visibility.Collapsed;
            try
            {
                var resp = await _server.Post<LoginResponse>("/api/v1/auth/login",
                    new LoginRequest { Email = email, Password = password });

                Token = new JwtCache.Payload
                {
                    AccessToken = resp.AccessToken,
                    RefreshToken = resp.RefreshToken,
                    ExpiresAt = resp.ExpiresAt,
                    Email = email,
                };
                _cache.Save(Token);
                LoggedIn = true;
                DialogResult = true;
                Close();
            }
            catch (ServerErrorException ex) when (ex.StatusCode == 401)
            {
                ShowError("Email hoặc mật khẩu không đúng.");
            }
            catch (Exception ex)
            {
                ShowError("Lỗi kết nối server: " + ex.Message);
            }
            finally
            {
                _loginButton.IsEnabled = true;
            }
        }

        private void ShowError(string msg)
        {
            _errorText.Text = msg;
            _errorText.Visibility = Visibility.Visible;
        }
    }
}
