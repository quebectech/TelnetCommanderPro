using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TelnetCommanderPro
{
    public partial class App : Application
    {
        private static Mutex? _mutex;

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Single instance check
            _mutex = new Mutex(true, "TelnetCommanderPro_SingleInstance", out bool isNewInstance);
            if (!isNewInstance)
            {
                MessageBox.Show("Telnet Commander Pro is already running.", "Already Running",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);
            
            // Check if running in background mode
            if (e.Args.Contains("--background"))
            {
                // Start background service only
                BackgroundService.StartBackgroundService();
                
                // Don't show UI, just run background checks
                this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                
                // Keep app running in background
                return;
            }
            
            // Normal startup - CHECK FOR REVOKED LICENSE FIRST
            await CheckForRevokedLicense();
            
            // CHECK FOR UPDATES SECOND (MANDATORY)
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            
            // Always start background service for license monitoring (mandatory)
            BackgroundService.StartBackgroundService();
            
            // Check for mandatory updates BEFORE showing splash screen
            try
            {
                bool updateRequired = await UpdateManager.CheckForUpdatesAsync();
                if (updateRequired)
                {
                    // App will be shut down by UpdateManager if update is required
                    return;
                }
            }
            catch
            {
                // If update check fails, continue with app startup
            }
            
            // Show splash screen only if no update is required
            var splashWindow = new SplashWindow();
            splashWindow.Show();
        }

        private async Task CheckForRevokedLicense()
        {
            try
            {
                var licenseManager = new LicenseManager();
                if (!licenseManager.IsActivated()) return;

                const string RegistryPath = @"SOFTWARE\TelnetCommanderPro";
                const string LicenseKeyName = "LicenseKey";
                
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key == null) return;

                string? encryptedLicense = key.GetValue(LicenseKeyName)?.ToString();
                if (string.IsNullOrEmpty(encryptedLicense)) return;

                byte[] data = System.Convert.FromBase64String(encryptedLicense);
                string licenseKey = System.Text.Encoding.UTF8.GetString(data);

                // READ-ONLY revoke check - does NOT call SaveLicense, does NOT touch registry
                bool isRevoked = await licenseManager.IsLicenseRevokedAsync(licenseKey);

                if (isRevoked)
                {
                    var result = ModernMessageBox.ShowQuestion(
                        "🚫 LICENSE SUSPENDED\n\n" +
                        "We're sorry, but your license has been temporarily suspended.\n" +
                        "This usually happens due to payment issues or policy violations.\n\n" +
                        "💡 SOLUTIONS:\n" +
                        "• Contact support to resolve the issue\n" +
                        "• Purchase a new license to continue using the software\n\n" +
                        "Would you like to view license options now?",
                        "License Suspended",
                        ModernMessageBox.MessageBoxButtons.YesNo,
                        null);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        try { new LicenseTiersWindow().ShowDialog(); } catch { }
                    }
                    
                    Shutdown();
                }
            }
            catch
            {
                // If check fails, allow app to start (offline mode)
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            BackgroundService.StopBackgroundService();
            base.OnExit(e);
        }
    }
}
