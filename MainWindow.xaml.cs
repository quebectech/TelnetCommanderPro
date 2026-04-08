using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace TelnetCommanderPro
{
    public partial class MainWindow : Window
    {
        private TelnetClient? _telnetClient;
        private bool _isConnected = false;
        private string _currentRouterIP = string.Empty;

        private int _selectedOperationIndex = -1;
        private List<string> _currentOperations = new List<string>();
        private string _currentLicenseTier = "Basic";
        private readonly LicenseManager _licenseManager;
        private decimal _walletBalance = 0;
        
        // Execution tracking for history
        private int _executionCommandCount = 0;
        private int _executionSuccessCount = 0;
        private int _executionFailedCount = 0;

        public MainWindow()
        {
            InitializeComponent();
            _licenseManager = new LicenseManager();
            Title = $"Telnet Commander Pro v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLicenseInfo();
            ApplyLicenseRestrictions();
            LoadOperations("V6");

            // On startup: restore count from JSONBin if local is 0 (e.g. after fresh install/update)
            // then sync any pending offline ops, and load wallet balance
            Task.Run(async () =>
            {
                try
                {
                    await _licenseManager.RestoreOperationCountFromOnlineIfNeeded();
                    await _licenseManager.TrySyncOnConnectivityRestored();
                    
                    // Load wallet balance
                    string hwid = _licenseManager.GetHardwareId();
                    _walletBalance = await WalletManager.GetBalanceAsync(hwid);
                    
                    Dispatcher.Invoke(() =>
                    {
                        UpdateLicenseDisplay();
                        UpdateWalletDisplay();
                    });
                }
                catch { }
            });
        }

        private void LoadLicenseInfo()
        {
            _currentLicenseTier = _licenseManager.GetLicenseTier();
            UpdateLicenseDisplay();
            
            // Display version
            if (VersionText != null)
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                VersionText.Text = $"v{version?.Major}.{version?.Minor}.{version?.Build}";
            }
        }

        private void UpdateLicenseDisplay()
        {
            if (LicenseTierText != null)
            {
                // Get remaining operations - this count persists across app updates
                int remaining = _licenseManager.GetRemainingOperationsSync();
                LicenseTierText.Text = $"License: {_currentLicenseTier} | Operations: {remaining}/50";
            }

            if (LicenseFeaturesText != null)
            {
                switch (_currentLicenseTier)
                {
                    case "Basic":
                        LicenseFeaturesText.Text = "✓ Router: V5 | ✓ SSIDs: 1, 5 (Fixed)";
                        break;
                    case "Standard":
                        LicenseFeaturesText.Text = "✓ Routers: V5, V6 | ✓ SSIDs: 1, 5 (Fixed)";
                        break;
                    case "Premium":
                        LicenseFeaturesText.Text = "✓ Routers: V5, V6 | ✓ All SSIDs (Choose any)";
                        break;
                    case "Enterprise":
                        LicenseFeaturesText.Text = "✓ All Routers (V5, V6, X6) | ✓ All SSIDs (Choose any)";
                        break;
                }
            }

            // Show pending sync indicator if there are unsynced operations
            if (PendingSyncText != null)
            {
                int pending = _licenseManager.GetPendingOperationCount();
                if (pending > 0)
                {
                    PendingSyncText.Text = $"⏳ {pending} op{(pending > 1 ? "s" : "")} pending sync";
                    PendingSyncText.Visibility = Visibility.Visible;
                }
                else
                {
                    PendingSyncText.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void UpdateLicenseInfo()
        {
            UpdateLicenseDisplay();
        }

        private void UpdateWalletDisplay()
        {
            if (WalletBorder == null) return;
            WalletBorder.Visibility = Visibility.Visible;
            WalletBalanceText.Text = $"KES {_walletBalance:F2}";
        }

        private async void TopUp_Click(object sender, RoutedEventArgs e)
        {
            string hwid = _licenseManager.GetHardwareId();
            var topUpWindow = new TopUpWindow(hwid, _walletBalance) { Owner = this };
            topUpWindow.ShowDialog();
            _walletBalance = topUpWindow.FinalBalance;
            UpdateWalletDisplay();
        }

        private void ApplyLicenseRestrictions()
        {
            // Restrict router dropdown based on license tier
            if (RouterTypeCombo != null)
            {
                foreach (ComboBoxItem item in RouterTypeCombo.Items)
                {
                    string? tag = item.Tag?.ToString();
                    
                    switch (_currentLicenseTier)
                    {
                        case "Basic":
                            // Only V5
                            item.IsEnabled = tag == "V5";
                            if (tag == "V5") RouterTypeCombo.SelectedItem = item;
                            break;
                        case "Standard":
                            // V5 and V6
                            item.IsEnabled = tag == "V5" || tag == "V6";
                            break;
                        case "Premium":
                            // V5 and V6 only (no X6)
                            item.IsEnabled = tag == "V5" || tag == "V6";
                            break;
                        case "Enterprise":
                            // All routers (V5, V6, X6)
                            item.IsEnabled = true;
                            break;
                    }
                }
            }

            // Show/Hide SSID selection based on license tier and adjust window height
            if (SsidSelectionBorder != null)
            {
                if (_currentLicenseTier == "Basic" || _currentLicenseTier == "Standard")
                {
                    // Hide SSID selection for Basic/Standard - they get fixed SSIDs 1 & 5
                    SsidSelectionBorder.Visibility = Visibility.Collapsed;
                    // Reduce window height
                    this.Height = 750;
                }
                else
                {
                    // Show SSID selection for Premium/Enterprise
                    SsidSelectionBorder.Visibility = Visibility.Visible;
                    // Increase window height to accommodate SSID selection
                    this.Height = 950;
                    
                    // Enable all checkboxes
                    if (Ssid2CheckBox != null) Ssid2CheckBox.IsEnabled = true;
                    if (Ssid3CheckBox != null) Ssid3CheckBox.IsEnabled = true;
                    if (Ssid4CheckBox != null) Ssid4CheckBox.IsEnabled = true;
                    if (Ssid6CheckBox != null) Ssid6CheckBox.IsEnabled = true;
                }
            }
        }

        private void SsidCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Update operations list when SSID selection changes
            if (RouterTypeCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string routerType = item.Tag.ToString() ?? "V6";
                LoadOperations(routerType);
            }
        }

        private List<int> GetSelectedSsids()
        {
            var selectedSsids = new List<int>();
            
            // For Basic/Standard licenses, always return fixed SSIDs 1 & 5
            if (_currentLicenseTier == "Basic" || _currentLicenseTier == "Standard")
            {
                selectedSsids.Add(1);
                selectedSsids.Add(5);
                return selectedSsids;
            }
            
            // For Premium/Enterprise, use checkbox selections
            if (Ssid1CheckBox?.IsChecked == true) selectedSsids.Add(1);
            if (Ssid2CheckBox?.IsChecked == true) selectedSsids.Add(2);
            if (Ssid3CheckBox?.IsChecked == true) selectedSsids.Add(3);
            if (Ssid4CheckBox?.IsChecked == true) selectedSsids.Add(4);
            if (Ssid5CheckBox?.IsChecked == true) selectedSsids.Add(5);
            if (Ssid6CheckBox?.IsChecked == true) selectedSsids.Add(6);
            
            return selectedSsids;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                this.DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Instructions_Click(object sender, RoutedEventArgs e)
        {
            var instructionsWindow = new InstructionsWindow();
            instructionsWindow.ShowDialog();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        private void UpgradeLicense_Click(object sender, RoutedEventArgs e)
        {
            var upgradeWindow = new UpgradeLicenseWindow(_currentLicenseTier);
            upgradeWindow.Owner = this;
            
            if (upgradeWindow.ShowDialog() == true && upgradeWindow.LicenseUpgraded)
            {
                // Reload license info and apply new restrictions
                LoadLicenseInfo();
                ApplyLicenseRestrictions();
                
                // Reload operations with new license
                if (RouterTypeCombo?.SelectedItem is ComboBoxItem item && item.Tag != null)
                {
                    string routerType = item.Tag.ToString() ?? "V6";
                    LoadOperations(routerType);
                }
                
                ModernMessageBox.ShowSuccess(
                    $"License successfully upgraded to {_currentLicenseTier}!\n\nYour new features are now active.",
                    "Upgrade Successful",
                    this);
            }
        }

        private void RouterTypeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (RouterTypeCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag != null)
            {
                string routerType = item.Tag.ToString() ?? "V6";
                LoadOperations(routerType);
                if (OperationsInfo != null)
                {
                    OperationsInfo.Text = $"(⚡ Huawei {routerType} - {_currentOperations.Count} operations)";
                }
            }
        }

        private void LoadOperations(string routerType)
        {
            _currentOperations.Clear();
            if (OperationsPanel != null)
            {
                OperationsPanel.Children.Clear();
            }
            
            var selectedSsids = GetSelectedSsids();
            
            // Always add superuser mode first
            _currentOperations.Add("Switch to superuser mode");
            
            // Generate operations based on router type and selected SSIDs
            if (routerType == "V5")
            {
                foreach (int ssid in selectedSsids)
                {
                    _currentOperations.Add($"Configure SSID Index {ssid} - Security");
                }
            }
            else if (routerType == "V6" || routerType == "X6")
            {
                foreach (int ssid in selectedSsids)
                {
                    _currentOperations.Add($"Configure WLAN Instance {ssid} - Authentication");
                    _currentOperations.Add($"Configure WLAN Instance {ssid} - Encryption");
                    _currentOperations.Add($"Configure WLAN Instance {ssid} - Beacon Type");
                }
            }
            
            // Create UI for each operation
            if (OperationsPanel != null)
            {
                for (int i = 0; i < _currentOperations.Count; i++)
                {
                    CreateOperationItem(i, _currentOperations[i]);
                }
            }
        }

        private void CreateOperationItem(int index, string operation)
        {
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(222, 226, 230)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 12, 16, 12),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var grid = new System.Windows.Controls.Grid();
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var numberText = new TextBlock
            {
                Text = $"{index + 1}.",
                FontSize = 13,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(108, 117, 125)),
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(numberText, 0);

            var iconText = new TextBlock
            {
                Text = "⚙",
                FontSize = 14,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)),
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(iconText, 1);

            var operationText = new TextBlock
            {
                Text = operation,
                FontSize = 13,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 37, 41)),
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(operationText, 2);

            grid.Children.Add(numberText);
            grid.Children.Add(iconText);
            grid.Children.Add(operationText);
            border.Child = grid;

            border.MouseLeftButtonDown += (s, e) =>
            {
                _selectedOperationIndex = index;
                if (SelectedActionText != null)
                {
                    SelectedActionText.Text = operation;
                }
                HighlightSelectedOperation();
                if (ExecuteSelectedButton != null)
                {
                    ExecuteSelectedButton.IsEnabled = true;
                }
            };

            border.MouseEnter += (s, e) =>
            {
                border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 249, 250));
            };

            border.MouseLeave += (s, e) =>
            {
                if (_selectedOperationIndex != index)
                {
                    border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
                }
            };

            if (OperationsPanel != null)
            {
                OperationsPanel.Children.Add(border);
            }
        }

        private void HighlightSelectedOperation()
        {
            if (OperationsPanel == null) return;
            
            for (int i = 0; i < OperationsPanel.Children.Count; i++)
            {
                if (OperationsPanel.Children[i] is Border border)
                {
                    if (i == _selectedOperationIndex)
                    {
                        border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 244, 253));
                    }
                    else
                    {
                        border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
                    }
                }
            }
        }

        private async void ExecuteSelected_Click(object sender, RoutedEventArgs e)
        {
            // Read-only revoke check - never touches registry/count
            if (_licenseManager.IsActivated())
            {
                var licenseKey = GetStoredLicenseKey();
                if (!string.IsNullOrEmpty(licenseKey) && await _licenseManager.IsLicenseRevokedAsync(licenseKey))
                {
                    ShowRevokedDialog();
                    return;
                }
            }

            if (_selectedOperationIndex < 0)
            {
                ModernMessageBox.ShowInformation("Please select an operation first.", "No Selection", this);
                return;
            }

            if (!_isConnected || _telnetClient == null)
            {
                ModernMessageBox.ShowWarning("Please connect to a router first.", "Not Connected", this);
                return;
            }

            // Check license first, then wallet fallback
            string routerTypeForCost = ((ComboBoxItem)RouterTypeCombo.SelectedItem).Tag?.ToString() ?? "V6";
            bool useLicense = _licenseManager.HasOperationsRemainingSync();
            bool useWallet = false;

            if (!useLicense)
            {
                int cost = WalletManager.GetOperationCost(routerTypeForCost);
                if (_walletBalance >= cost)
                {
                    useWallet = true;
                }
                else
                {
                    ModernMessageBox.ShowWarning(
                        $"Your license has expired and your wallet balance is insufficient.\n\n" +
                        $"Operation cost: KES {cost}\n" +
                        $"Wallet balance: KES {_walletBalance:F2}\n\n" +
                        $"Please top up your wallet to continue.",
                        "Insufficient Balance", this);
                    return;
                }
            }

            AppendToConsole($"▶ Executing: {_currentOperations[_selectedOperationIndex]}\n");
            
            string routerType = routerTypeForCost;
            var selectedSsids = GetSelectedSsids();
            
            try
            {
                // Generate all commands
                var allCommands = GenerateCommands(routerType, selectedSsids);
                
                // Execute only the selected command by index
                if (_selectedOperationIndex < allCommands.Count)
                {
                    string command = allCommands[_selectedOperationIndex];
                    // Show user-friendly message instead of technical command
                    string friendlyMessage = GetFriendlyMessage(command, routerType);
                    AppendToConsole($"  {friendlyMessage}\n");
                    await _telnetClient.SendCommandAsync(command);
                    await Task.Delay(500);
                }
                
                // Increment license count OR deduct from wallet
                if (useLicense)
                {
                    bool incrementSuccess = _licenseManager.IncrementOperationCountSync();
                    AppendToConsole("✓ Operation completed successfully\n");
                    if (incrementSuccess)
                    {
                        int remaining = _licenseManager.GetRemainingOperationsSync();
                        AppendToConsole($"📊 License ops remaining: {remaining}/50\n\n");
                        UpdateLicenseInfo();
                    }
                }
                else if (useWallet)
                {
                    string hwid = _licenseManager.GetHardwareId();
                    var deduct = await WalletManager.DeductAsync(hwid, routerType);
                    AppendToConsole("✓ Operation completed successfully\n");
                    if (deduct.Success)
                    {
                        _walletBalance = deduct.NewBalance;
                        AppendToConsole($"💰 Wallet balance: KES {_walletBalance:F2}\n\n");
                        Dispatcher.Invoke(UpdateWalletDisplay);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendToConsole($"Error: {ex.Message}\n");
            }
        }

        private async void ExecuteAll_Click(object sender, RoutedEventArgs e)
        {
            // Read-only revoke check - never touches registry/count
            if (_licenseManager.IsActivated())
            {
                var licenseKey = GetStoredLicenseKey();
                if (!string.IsNullOrEmpty(licenseKey) && await _licenseManager.IsLicenseRevokedAsync(licenseKey))
                {
                    ShowRevokedDialog();
                    return;
                }
            }

            if (!_isConnected || _telnetClient == null)
            {
                ModernMessageBox.ShowWarning("Please connect to a router first.", "Not Connected", this);
                return;
            }

            var selectedSsids = GetSelectedSsids();
            if (selectedSsids.Count == 0)
            {
                ModernMessageBox.ShowWarning("Please select at least one SSID to configure.", "No SSID Selected", this);
                return;
            }

            // Check license first, then wallet fallback
            string routerTypeAll = ((System.Windows.Controls.ComboBoxItem)RouterTypeCombo.SelectedItem).Tag?.ToString() ?? "V6";
            bool useLicenseAll = _licenseManager.HasOperationsRemainingSync();
            bool useWalletAll = false;

            if (!useLicenseAll)
            {
                int cost = WalletManager.GetOperationCost(routerTypeAll);
                if (_walletBalance >= cost)
                {
                    useWalletAll = true;
                }
                else
                {
                    ModernMessageBox.ShowWarning(
                        $"Your license has expired and your wallet balance is insufficient.\n\n" +
                        $"Operation cost: KES {cost}\n" +
                        $"Wallet balance: KES {_walletBalance:F2}\n\n" +
                        $"Please top up your wallet to continue.",
                        "Insufficient Balance", this);
                    return;
                }
            }

            AppendToConsole($"▶ Configuring SSIDs: {string.Join(", ", selectedSsids)}\n");
            
            string routerType = routerTypeAll;
            
            try
            {
                // Generate dynamic commands based on router type and selected SSIDs
                var commands = GenerateCommands(routerType, selectedSsids);
                
                foreach (string command in commands)
                {
                    // Show user-friendly message instead of technical command
                    string friendlyMessage = GetFriendlyMessage(command, routerType);
                    AppendToConsole($"  {friendlyMessage}\n");
                    await _telnetClient.SendCommandAsync(command);
                    await Task.Delay(500);
                }
                
                AppendToConsole($"✓ All operations completed successfully\n");

                if (useLicenseAll)
                {
                    bool incrementSuccess = _licenseManager.IncrementOperationCountSync();
                    if (incrementSuccess)
                    {
                        int remaining = _licenseManager.GetRemainingOperationsSync();
                        AppendToConsole($"📊 License ops remaining: {remaining}/50\n\n");
                        UpdateLicenseInfo();
                    }
                }
                else if (useWalletAll)
                {
                    string hwid = _licenseManager.GetHardwareId();
                    var deduct = await WalletManager.DeductAsync(hwid, routerType);
                    if (deduct.Success)
                    {
                        _walletBalance = deduct.NewBalance;
                        AppendToConsole($"💰 Wallet balance: KES {_walletBalance:F2}\n\n");
                        Dispatcher.Invoke(UpdateWalletDisplay);
                    }
                }

                await PromptForNextRouter();
            }
            catch (Exception ex)
            {
                AppendToConsole($"Error: {ex.Message}\n");
            }
        }

        private List<string> GenerateCommands(string routerType, List<int> ssids)
        {
            var commands = new List<string>();
            
            // Always start with superuser mode
            commands.Add("su");
            
            if (routerType == "V5")
            {
                // V5 commands: set ssid index X security None
                foreach (int ssid in ssids)
                {
                    commands.Add($"set ssid index {ssid} security None");
                }
            }
            else if (routerType == "V6" || routerType == "X6")
            {
                // V6/X6 commands: set wlan basic laninst 1 wlaninst X ...
                foreach (int ssid in ssids)
                {
                    commands.Add($"set wlan basic laninst 1 wlaninst {ssid} basicauthenticationmode OpenSystem");
                    commands.Add($"set wlan basic laninst 1 wlaninst {ssid} basicencryptionmodes None");
                    commands.Add($"set wlan basic laninst 1 wlaninst {ssid} beaconType None");
                }
            }
            
            return commands;
        }

        private string GetFriendlyMessage(string command, string routerType)
        {
            // Convert technical commands to user-friendly messages
            if (command == "su")
            {
                return "→ Switching to administrator mode...";
            }
            
            if (command.Contains("set ssid index"))
            {
                // V5 command
                var parts = command.Split(' ');
                if (parts.Length >= 4)
                {
                    string ssidNum = parts[3];
                    return $"→ Removing security from SSID {ssidNum}...";
                }
            }
            else if (command.Contains("basicauthenticationmode"))
            {
                // V6/X6 authentication command
                var match = System.Text.RegularExpressions.Regex.Match(command, @"wlaninst (\d+)");
                if (match.Success)
                {
                    string ssidNum = match.Groups[1].Value;
                    return $"→ Setting authentication mode for SSID {ssidNum}...";
                }
            }
            else if (command.Contains("basicencryptionmodes"))
            {
                // V6/X6 encryption command
                var match = System.Text.RegularExpressions.Regex.Match(command, @"wlaninst (\d+)");
                if (match.Success)
                {
                    string ssidNum = match.Groups[1].Value;
                    return $"→ Disabling encryption for SSID {ssidNum}...";
                }
            }
            else if (command.Contains("beaconType"))
            {
                // V6/X6 beacon command
                var match = System.Text.RegularExpressions.Regex.Match(command, @"wlaninst (\d+)");
                if (match.Success)
                {
                    string ssidNum = match.Groups[1].Value;
                    return $"→ Configuring beacon settings for SSID {ssidNum}...";
                }
            }
            
            return $"→ Executing configuration...";
        }

        private void ExecutionHistory_Click(object sender, RoutedEventArgs e)
        {
            var historyWindow = new ExecutionHistoryWindow();
            historyWindow.Owner = this;
            historyWindow.ShowDialog();
        }

        private void NewRouter_Click(object sender, RoutedEventArgs e)
        {
            var result = ModernMessageBox.ShowQuestion(
                "Start a new router configuration?\n\n" +
                "This will:\n" +
                "• Disconnect from current router\n" +
                "• Clear the execution log\n" +
                "• Reset all selections\n\n" +
                "Continue?",
                "New Router Configuration",
                ModernMessageBox.MessageBoxButtons.YesNo,
                this);

            if (result == MessageBoxResult.Yes)
            {
                // Disconnect if connected
                if (_isConnected)
                {
                    _telnetClient?.Disconnect();
                    _isConnected = false;
                    ConnectButton.IsEnabled = true;
                    DisconnectButton.IsEnabled = false;
                    ExecuteAllButton.IsEnabled = false;
                    ExecuteSelectedButton.IsEnabled = false;
                }

                // Clear console
                ConsoleOutput.Text = string.Empty;
                
                // Reset selection
                _selectedOperationIndex = -1;
                SelectedActionText.Text = "Select a command to see details";
                
                // Reset to default router
                RouterTypeCombo.SelectedIndex = 1; // V6
                
                AppendToConsole("Ready for new router configuration.\n");
            }
        }



        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected)
            {
                _telnetClient?.Disconnect();
                _isConnected = false;
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
                ExecuteAllButton.IsEnabled = false;
                if (_selectedOperationIndex >= 0)
                {
                    ExecuteSelectedButton.IsEnabled = false;
                }
                AppendToConsole("Disconnected from router.\n");
                return;
            }

            string host = HostIpInput.Text.Trim();
            string password = PasswordInput.Password;
            string routerType = ((System.Windows.Controls.ComboBoxItem)RouterTypeCombo.SelectedItem).Content.ToString() ?? "Huawei V5";

            if (string.IsNullOrEmpty(host))
            {
                ModernMessageBox.ShowError("Please enter a valid host IP.", "Error", this);
                return;
            }

            ConnectButton.IsEnabled = false;
            AppendToConsole($"Connecting to {routerType} at {host}...\n");

            try
            {
                _telnetClient = new TelnetClient(host, 23, "root", password);
                await _telnetClient.ConnectAsync();
                
                // Don't display router responses for security - only show commands sent
                _telnetClient.OnDataReceived += (data) =>
                {
                    // Router responses are hidden for security purposes
                    // Only commands sent by user are shown in the log
                };

                _isConnected = true;
                ConnectButton.IsEnabled = false;
                DisconnectButton.IsEnabled = true;
                ExecuteAllButton.IsEnabled = true;
                if (_selectedOperationIndex >= 0)
                {
                    ExecuteSelectedButton.IsEnabled = true;
                }
                AppendToConsole($"✓ Successfully connected to {routerType}!\n");
                AppendToConsole("Ready to execute commands.\n\n");
            }
            catch (Exception ex)
            {
                AppendToConsole($"Connection failed: {ex.Message}\n");
                ModernMessageBox.ShowError($"Failed to connect: {ex.Message}", "Connection Error", this);
            }
            finally
            {
                ConnectButton.IsEnabled = true;
            }
        }



        private async void QuickCommand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string command)
            {
                if (!_isConnected || _telnetClient == null)
                {
                    ModernMessageBox.ShowWarning("Please connect to a router first.", "Not Connected", this);
                    return;
                }

                if (command == "reboot")
                {
                    var result = ModernMessageBox.ShowQuestion("Are you sure you want to reboot the router?", 
                        "Confirm Reboot", ModernMessageBox.MessageBoxButtons.YesNo, this);
                    if (result != MessageBoxResult.Yes) return;
                }

                AppendToConsole($"> {command}\n");
                try
                {
                    await _telnetClient.SendCommandAsync(command);
                }
                catch (Exception ex)
                {
                    AppendToConsole($"Error: {ex.Message}\n");
                }
            }
        }

        private async void UploadScript_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected || _telnetClient == null)
            {
                ModernMessageBox.ShowWarning("Please connect to a router first.", "Not Connected", this);
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                Title = "Select Telnet Script"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string[] commands = File.ReadAllLines(dialog.FileName);
                    AppendToConsole($"Executing script: {Path.GetFileName(dialog.FileName)}\n");
                    AppendToConsole($"Total commands: {commands.Length}\n\n");

                    foreach (string command in commands)
                    {
                        string trimmedCommand = command.Trim();
                        if (string.IsNullOrEmpty(trimmedCommand) || trimmedCommand.StartsWith("#"))
                            continue;

                        AppendToConsole($"> {trimmedCommand}\n");
                        await _telnetClient.SendCommandAsync(trimmedCommand);
                        await Task.Delay(500); // Delay between commands
                    }

                    AppendToConsole("\nScript execution completed.\n");
                }
                catch (Exception ex)
                {
                    ModernMessageBox.ShowError($"Error executing script: {ex.Message}", "Script Error", this);
                }
            }
        }

        private void ClearConsole_Click(object sender, RoutedEventArgs e)
        {
            ConsoleOutput.Text = string.Empty;
        }

        private void SaveLog_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt",
                FileName = $"telnet_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, ConsoleOutput.Text);
                    ModernMessageBox.ShowSuccess("Log saved successfully!", "Success", this);
                }
                catch (Exception ex)
                {
                    ModernMessageBox.ShowError($"Error saving log: {ex.Message}", "Error", this);
                }
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        private void AppendToConsole(string text)
        {
            ConsoleOutput.Text += text;
            // Auto-scroll to bottom
            ConsoleScrollViewer.ScrollToEnd();
        }

        private void ShowRevokedDialog()
        {
            var result = ModernMessageBox.ShowQuestion(
                "🚫 LICENSE SUSPENDED\n\n" +
                "Your license has been temporarily suspended.\n\n" +
                "💡 SOLUTIONS:\n" +
                "• Contact support to resolve the issue\n" +
                "• Purchase a new license to continue\n\n" +
                "Would you like to view license options now?",
                "License Suspended",
                ModernMessageBox.MessageBoxButtons.YesNo,
                this);

            if (result == MessageBoxResult.Yes)
            {
                try { new LicenseTiersWindow { Owner = this }.ShowDialog(); } catch { }
            }
            Application.Current.Shutdown();
        }

        private string GetStoredLicenseKey()
        {
            try
            {
                const string RegistryPath = @"SOFTWARE\TelnetCommanderPro";
                const string LicenseKeyName = "LicenseKey";
                
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key == null) return string.Empty;

                string? encryptedLicense = key.GetValue(LicenseKeyName)?.ToString();
                if (string.IsNullOrEmpty(encryptedLicense)) return string.Empty;

                // Decode license key
                byte[] data = Convert.FromBase64String(encryptedLicense);
                return System.Text.Encoding.UTF8.GetString(data);
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task PromptForNextRouter()
        {
            string routerType = ((ComboBoxItem)RouterTypeCombo.SelectedItem).Tag?.ToString() ?? "V6";

            AppendToConsole("─────────────────────────────────────\n");
            AppendToConsole("✓ Configuration applied successfully.\n");

            if (routerType == "V6" || routerType == "X6")
            {
                AppendToConsole("⚠ IMPORTANT: Restart the router from the web interface to make settings permanent.\n");
                AppendToConsole("  Do NOT unplug or press the power button - this will relock the router.\n");
                AppendToConsole("  Open browser → 192.168.100.1 → Home → RESET button → Restart\n\n");

                var guide = new RouterRestartGuideWindow { Owner = this };
                guide.ShowDialog();
            }
            else
            {
                ModernMessageBox.ShowInformation(
                    "✅ Router unlocked successfully!\n\nReady for the next router.\nSimply click Execute All to continue.",
                    "Operation Complete", this);
            }

            AppendToConsole("Ready for next router. Click Execute All when ready.\n\n");
        }

        protected override void OnClosed(EventArgs e)
        {
            _telnetClient?.Disconnect();
            base.OnClosed(e);
        }
    }
}
