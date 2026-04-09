using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace TelnetCommanderPro
{
    public partial class TopUpWindow : Window
    {
        private readonly string _hardwareId;
        private string? _currentToken;
        private decimal _requestedAmount;
        private bool _inPaymentStep = false;
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

        private void ReceiptInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (ReceiptInput == null) return;
            string current = ReceiptInput.Text;
            // Replace letter O with zero, force uppercase
            string fixed_ = current.ToUpper().Replace("O", "0");
            if (fixed_ != current)
            {
                int caret = ReceiptInput.CaretIndex;
                ReceiptInput.Text = fixed_;
                ReceiptInput.CaretIndex = Math.Min(caret, fixed_.Length);
            }
        }

        private void WhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (_currentToken == null) return;
            string receipt = ReceiptInput?.Text?.Trim() ?? "";
            string waMsg = Uri.EscapeDataString(
                $"TCP Top-Up Request\n" +
                $"Token: {_currentToken}\n" +
                $"Amount: KES {_requestedAmount:F0}\n" +
                $"Hardware ID: {_hardwareId}" +
                (string.IsNullOrEmpty(receipt) ? "" : $"\nM-Pesa Receipt: {receipt}"));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"https://wa.me/254741876354?text={waMsg}",
                UseShellExecute = true
            });
        }

        private async void Action_Click(object sender, RoutedEventArgs e)
        {
            if (!_inPaymentStep)
                await GenerateToken();
            else
                await VerifyPayment();
        }

        private async Task GenerateToken()
        {
            if (!decimal.TryParse(AmountInput.Text.Trim(), out decimal amount) || amount < 1)
            {
                ShowStatus("Please enter a valid amount.", "#DC3545");
                return;
            }
            _requestedAmount = amount;

            SetBusy(true, "⏳ Generating payment code...");

            var result = await WalletManager.GeneratePaymentTokenAsync(_hardwareId);

            SetBusy(false);

            if (!result.Success || result.Token == null)
            {
                ShowStatus($"❌ {result.Error ?? "Failed to generate token"}", "#DC3545");
                return;
            }

            _currentToken = result.Token;

            // Show payment instructions
            AmountPanel.Visibility = Visibility.Collapsed;
            PaymentPanel.Visibility = Visibility.Visible;
            PaybillText.Text = result.Paybill ?? "4167789";
            TokenText.Text = result.Token;
            AmountDisplayText.Text = $"KES {amount:F0}";

            ActionButton.Content = "✅ I've Paid - Verify";
            _inPaymentStep = true;

            // Open WhatsApp with pre-filled message to admin
            string waMsg = Uri.EscapeDataString(
                $"TCP Top-Up Request\n" +
                $"Token: {result.Token}\n" +
                $"Amount: KES {amount:F0}\n" +
                $"Hardware ID: {_hardwareId}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"https://wa.me/254741876354?text={waMsg}",
                UseShellExecute = true
            });

            ShowStatus("WhatsApp opened - send the message to admin, then pay M-Pesa and click Verify.", "#0078D4");
        }

        private async Task VerifyPayment()
        {
            if (_currentToken == null) return;

            SetBusy(true, "⏳ Verifying payment...");

            // If user entered a receipt number, submit it for faster verification
            string receipt = ReceiptInput?.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(receipt))
            {
                var receiptResult = await WalletManager.SubmitReceiptAsync(_currentToken, receipt, _hardwareId);
                if (receiptResult.Pending)
                {
                    SetBusy(false);
                    ShowStatus("✅ Receipt submitted! Admin will verify and credit your wallet shortly. Click Verify again in a few minutes.", "#0078D4");
                    return;
                }
            }

            var result = await WalletManager.VerifyPaymentAsync(_currentToken, _hardwareId);

            SetBusy(false);

            if (result.Success)
            {
                FinalBalance = result.NewBalance;
                ShowStatus($"✅ Payment confirmed! KES {result.Amount:F0} added. New balance: KES {FinalBalance:F2}", "#28A745");
                ActionButton.IsEnabled = false;
                ActionButton.Content = "Done ✓";
                await Task.Delay(2500);
                Close();
            }
            else
            {
                ShowStatus($"❌ {result.Error}", "#DC3545");
            }
        }

        private void SetBusy(bool busy, string? message = null)
        {
            ActionButton.IsEnabled = !busy;
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
