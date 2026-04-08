using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace TelnetCommanderPro
{
    public partial class TopUpWindow : Window
    {
        private readonly string _hardwareId;
        public decimal FinalBalance { get; private set; }

        public TopUpWindow(string hardwareId, decimal currentBalance)
        {
            InitializeComponent();
            _hardwareId = hardwareId;
            CurrentBalanceText.Text = $"KES {currentBalance:F2}";
            FinalBalance = currentBalance;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private async void Pay_Click(object sender, RoutedEventArgs e)
        {
            string phone = PhoneInput.Text.Trim();
            string amountStr = AmountInput.Text.Trim();

            // Validate Kenyan phone number
            string normalized = phone.Replace(" ", "").Replace("+", "");
            if (normalized.StartsWith("0")) normalized = "254" + normalized.Substring(1);
            if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^254(7|1)\d{8}$"))
            {
                ShowStatus("Invalid phone number. Use format 07XXXXXXXX or 01XXXXXXXX", "#DC3545");
                return;
            }

            if (!decimal.TryParse(amountStr, out decimal amount) || amount < 100)
            {
                ShowStatus("Minimum amount is KES 100.", "#DC3545");
                return;
            }

            SetBusy(true, "Sending M-Pesa request to your phone...");

            var result = await WalletManager.InitiateTopUpAsync(_hardwareId, phone, amount);

            if (!result.Success || result.CheckoutRequestId == null)
            {
                SetBusy(false);
                ShowStatus($"Failed: {result.Error ?? result.Message}", "#DC3545");
                return;
            }

            ShowStatus("✅ Check your phone and enter your M-Pesa PIN.\nWaiting for confirmation...", "#0078D4");

            // Poll for balance update for up to 90 seconds
            decimal balanceBefore = FinalBalance;
            bool confirmed = false;

            for (int i = 0; i < 18; i++) // 18 × 5s = 90s
            {
                await Task.Delay(5000);
                decimal newBalance = await WalletManager.GetBalanceAsync(_hardwareId);

                if (newBalance > balanceBefore)
                {
                    FinalBalance = newBalance;
                    confirmed = true;
                    break;
                }
            }

            SetBusy(false);

            if (confirmed)
            {
                ShowStatus($"✅ Top-up successful! New balance: KES {FinalBalance:F2}", "#28A745");
                CurrentBalanceText.Text = $"KES {FinalBalance:F2}";
                PayButton.IsEnabled = false;
                PayButton.Content = "Done";

                await Task.Delay(2000);
                Close();
            }
            else
            {
                ShowStatus("Payment not confirmed yet. Please check your M-Pesa messages and try again.", "#E67E22");
                PayButton.IsEnabled = true;
            }
        }

        private void SetBusy(bool busy, string? message = null)
        {
            PayButton.IsEnabled = !busy;
            Spinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (message != null) ShowStatus(message, "#0078D4");
        }

        private void ShowStatus(string message, string color)
        {
            StatusText.Text = message;
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            StatusText.Visibility = Visibility.Visible;
        }
    }
}
