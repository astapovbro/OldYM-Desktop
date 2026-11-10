using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;

namespace OldYM
{
    public partial class LoginWindow : Window
    {
        readonly Net net;
        System.Threading.Timer pollTimer;
        volatile bool closed;
        int polling;
        int interval = 5;
        DateTime expires;
        string deviceCode;
        string verificationUrl;

        public string Token { get; private set; }
        public bool Remember { get { return RememberCheck.IsChecked == true; } }

        public LoginWindow(Net transport)
        {
            InitializeComponent();
            net = transport;
            Closed += delegate
            {
                closed = true;
                if (pollTimer != null) pollTimer.Dispose();
            };
        }

        void DeviceButton_Click(object sender, RoutedEventArgs e)
        {
            DeviceButton.IsEnabled = false;
            StatusText.Text = "Получаю код входа…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    DeviceAuth auth = new DeviceAuth(net);
                    object response = auth.Request();
                    string url = DeviceAuth.VerificationUrl(response);
                    string code = Json.Str(response, "device_code", "");
                    if (code.Length == 0) throw new IOException("Сервис не прислал код устройства");
                    int seconds = Math.Max(3, Json.Int(response, "interval", 5));
                    DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Min(1800, Json.Int(response, "expires_in", 600)));
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (closed) return;
                        deviceCode = code;
                        verificationUrl = url;
                        interval = seconds;
                        expires = deadline;
                        CodeText.Text = Json.Str(response, "user_code", "Код не получен");
                        OpenUrlButton.IsEnabled = true;
                        StatusText.Text = "Подтверди код на странице Яндекса. Ожидаю подтверждения…";
                        if (pollTimer != null) pollTimer.Dispose();
                        pollTimer = new System.Threading.Timer(delegate { Poll(auth); }, null, interval * 1000, interval * 1000);
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (closed) return;
                        StatusText.Text = ex.Message;
                        DeviceButton.IsEnabled = true;
                    }));
                }
            });
        }

        void Poll(DeviceAuth auth)
        {
            if (closed || Interlocked.Exchange(ref polling, 1) == 1) return;
            try
            {
                if (DateTime.UtcNow >= expires) throw new IOException("Код истёк. Получи новый код.");
                object response = auth.Poll(deviceCode);
                string access = Json.Str(response, "access_token", "");
                if (access.Length > 0)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (closed) return;
                        Token = access;
                        DialogResult = true;
                    }));
                    return;
                }

                string error = Json.Str(response, "error", "");
                if (error == "slow_down")
                {
                    interval += 5;
                    if (!closed && pollTimer != null) pollTimer.Change(interval * 1000, interval * 1000);
                }
                else if (error != "authorization_pending")
                {
                    throw new IOException("Авторизация: " + (error.Length == 0 ? "неизвестная ошибка" : error));
                }
            }
            catch (Exception ex)
            {
                if (pollTimer != null) pollTimer.Dispose();
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (closed) return;
                    StatusText.Text = ex.Message;
                    DeviceButton.IsEnabled = true;
                }));
            }
            finally
            {
                Interlocked.Exchange(ref polling, 0);
            }
        }

        void OpenUrlButton_Click(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrEmpty(verificationUrl)) return;
            try
            {
                Process.Start(new ProcessStartInfo(verificationUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusText.Text = "Не удалось открыть браузер: " + ex.Message + " Скопируй адрес вручную.";
            }
        }

        void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            string token = TokenBox.Password.Trim();
            if (token.Length == 0)
            {
                StatusText.Text = "Получи код через Яндекс или вставь OAuth-токен.";
                return;
            }
            Token = token;
            DialogResult = true;
        }
    }
}
