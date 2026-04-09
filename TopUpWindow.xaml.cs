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

        // Auto-uppercase and O→0 on transaction code input
        private void TransactionCode_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (TransactionCodeInput == null) return;
            string current = TransactionCodeInput.Text;
            string fixed_ = current.ToUpper().Replace("O", "0");
            if (fixed_ != current)
            {
                int caret = TransactionCodeInput.CaretIndex;
                TransactionCodeInput.Text = fixed_;
                TransactionCodeInput.CaretIndex = Math.Min(caret, fixed_.Length);
            }
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

            // Switch to payment step
            AmountPanel.Visibility = Visibility.Collapsed;
            PaymentPanel.Visibility = Visibility.Visible;
            PaybillText.Text = result.Paybill ?? "4167789";
            TokenText.Text = result.Token;
            AmountDisplayText.Text = $"KES {amount:F0}";

            ActionButton.Content = "✅ Verify Payment";
            _inPaymentStep = true;

            ShowStatus("Pay M-Pesa then enter your transaction code above.", "#0078D4");
            TransactionCodeInput.Focus();
        }

        private async Task VerifyPayment()
        {
            if (_currentToken == null) return;

            string code = TransactionCodeInput?.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(code))
            {
                ShowStatus("Please enter your M-Pesa transaction code.", "#DC3545");
                return;
            }

            // Basic format check before hitting the server
            if (code.Length < 8 || code.Length > 12)
            {
                ShowStatus("Transaction code should be 10 characters (e.g. UD9QB03GS7). Please check and try again.", "#DC3545");
                return;
            }

            SetBusy(true, "⏳ Verifying transaction code...");

            var result = await WalletManager.VerifyTransactionAsync(_currentToken, code, _requestedAmount, _hardwareId);

            SetBusy(false);

            if (result.Success)
            {
                FinalBalance = result.NewBalance;
                ShowStatus($"✅ Payment confirmed! KES {result.Amount:F0} added.\nNew balance: KES {FinalBalance:F2}", "#28A745");
                ActionButton.IsEnabled = false;
                ActionButton.Content = "Done ✓";
                await Task.Delay(2500);
                Close();
            }
            else
            {
                // Show helpful error
                string errMsg = result.Error ?? "Verification failed";
                if (errMsg.Contains("already been used"))
                    ShowStatus("❌ This transaction code has already been used.", "#DC3545");
                else if (errMsg.Contains("not found") || errMsg.Contains("expired"))
                    ShowStatus("❌ Token expired. Please close and generate a new payment code.", "#DC3545");
                else
                    ShowStatus($"❌ Invalid transaction code. Please check the code from your M-Pesa SMS and try again.", "#DC3545");
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
