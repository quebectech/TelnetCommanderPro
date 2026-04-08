using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;
using Microsoft.Win32;

namespace TelnetCommanderProInstaller
{
    public partial class InstallerWindow : Window
    {
        private string installPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Telnet Commander Pro");
        
        private string _newVersion = "1.2.3";
        private bool isUpdate = false;
        private string? installedVersion = null;
        
        public InstallerWindow()
        {
            InitializeComponent();
            if (!CheckDotNetRuntime()) return; // abort if .NET missing
            CheckExistingInstallation();
        }

        private bool CheckDotNetRuntime()
        {
            try
            {
                // Check if .NET 6 desktop runtime is installed
                var result = Process.Start(new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "--list-runtimes",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (result == null) return ShowDotNetMissing();

                string output = result.StandardOutput.ReadToEnd();
                result.WaitForExit();

                // Need Microsoft.WindowsDesktop.App 6.x
                if (output.Contains("Microsoft.WindowsDesktop.App 6."))
                    return true;

                return ShowDotNetMissing();
            }
            catch
            {
                // dotnet command not found at all
                return ShowDotNetMissing();
            }
        }

        private bool ShowDotNetMissing()
        {
            var overlay = new Window
            {
                Width = 500,
                Height = 260,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true
            };

            var border = new System.Windows.Controls.Border
            {
                Background = System.Windows.Media.Brushes.White,
                CornerRadius = new CornerRadius(10),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(229, 231, 235)),
                BorderThickness = new Thickness(1)
            };
            border.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                Opacity = 0.2,
                BlurRadius = 20,
                ShadowDepth = 0
            };

            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(50) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(65) });

            // Header
            var header = new System.Windows.Controls.Border { CornerRadius = new CornerRadius(10, 10, 0, 0) };
            header.Background = new System.Windows.Media.LinearGradientBrush(
                System.Windows.Media.Color.FromRgb(0, 120, 212),
                System.Windows.Media.Color.FromRgb(0, 188, 242),
                new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
            header.Child = new System.Windows.Controls.TextBlock
            {
                Text = "  .NET 6 Runtime Required",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 0, 0)
            };
            System.Windows.Controls.Grid.SetRow(header, 0);

            // Message
            var msg = new System.Windows.Controls.TextBlock
            {
                Text = "Telnet Commander Pro requires .NET 6 Desktop Runtime to run.\n\n" +
                       "Please download and install it, then run this installer again.",
                FontSize = 13,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(31, 41, 55)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(24, 10, 24, 10)
            };
            System.Windows.Controls.Grid.SetRow(msg, 1);

            // Buttons
            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 20, 0)
            };
            System.Windows.Controls.Grid.SetRow(btnPanel, 2);

            var closeBtn = new System.Windows.Controls.Button
            {
                Content = "Close",
                Width = 90, Height = 36,
                Margin = new Thickness(0, 0, 10, 0),
                FontSize = 13,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(107, 114, 128)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            closeBtn.Click += (s, ev) => { overlay.Close(); Application.Current.Shutdown(); };

            var downloadBtn = new System.Windows.Controls.Button
            {
                Content = "Download .NET 6",
                Width = 140, Height = 36,
                FontSize = 13,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 120, 212)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            downloadBtn.Click += (s, ev) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://dotnet.microsoft.com/en-us/download/dotnet/6.0",
                    UseShellExecute = true
                });
            };

            btnPanel.Children.Add(closeBtn);
            btnPanel.Children.Add(downloadBtn);

            grid.Children.Add(header);
            grid.Children.Add(msg);
            grid.Children.Add(btnPanel);
            border.Child = grid;
            overlay.Content = border;
            overlay.ShowDialog();

            Application.Current.Shutdown();
            return false;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            ShowThemedConfirm(
                "Are you sure you want to cancel the installation?",
                "Cancel Installation",
                () => Application.Current.Shutdown());
        }

        private void ShowThemedConfirm(string message, string title, Action onYes)
        {
            // Themed modal overlay
            var overlay = new Window
            {
                Width = 420,
                Height = 200,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Topmost = true
            };

            var border = new System.Windows.Controls.Border
            {
                Background = System.Windows.Media.Brushes.White,
                CornerRadius = new CornerRadius(10),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(229, 231, 235)),
                BorderThickness = new Thickness(1)
            };
            border.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                Opacity = 0.2,
                BlurRadius = 20,
                ShadowDepth = 0
            };

            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(50) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(60) });

            // Header
            var header = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(10, 10, 0, 0)
            };
            header.Background = new System.Windows.Media.LinearGradientBrush(
                System.Windows.Media.Color.FromRgb(0, 120, 212),
                System.Windows.Media.Color.FromRgb(0, 188, 242),
                new System.Windows.Point(0, 0),
                new System.Windows.Point(1, 1));
            var titleText = new System.Windows.Controls.TextBlock
            {
                Text = title,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 0, 0)
            };
            header.Child = titleText;
            System.Windows.Controls.Grid.SetRow(header, 0);

            // Message
            var msgText = new System.Windows.Controls.TextBlock
            {
                Text = message,
                FontSize = 13,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(31, 41, 55)),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(20, 10, 20, 10)
            };
            System.Windows.Controls.Grid.SetRow(msgText, 1);

            // Buttons
            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 20, 0)
            };
            System.Windows.Controls.Grid.SetRow(btnPanel, 2);

            var noBtn = new System.Windows.Controls.Button
            {
                Content = "No",
                Width = 80,
                Height = 36,
                Margin = new Thickness(0, 0, 10, 0),
                FontSize = 13,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(107, 114, 128)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            noBtn.Click += (s, ev) => overlay.Close();

            var yesBtn = new System.Windows.Controls.Button
            {
                Content = "Yes",
                Width = 80,
                Height = 36,
                FontSize = 13,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 120, 212)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            yesBtn.Click += (s, ev) => { overlay.Close(); onYes(); };

            btnPanel.Children.Add(noBtn);
            btnPanel.Children.Add(yesBtn);

            grid.Children.Add(header);
            grid.Children.Add(msgText);
            grid.Children.Add(btnPanel);
            border.Child = grid;
            overlay.Content = border;
            overlay.ShowDialog();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close_Click(sender, e);
        }

        private async void Action_Click(object sender, RoutedEventArgs e)
        {
            if (WelcomePage.Visibility == Visibility.Visible)
            {
                await StartInstallation();
            }
            else if (CompletePage.Visibility == Visibility.Visible)
            {
                if (LaunchCheckBox.IsChecked == true)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = Path.Combine(installPath, "TelnetCommanderPro.exe"),
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
                Application.Current.Shutdown();
            }
        }

        private async Task StartInstallation()
        {
            WelcomePage.Visibility = Visibility.Collapsed;
            InstallingPage.Visibility = Visibility.Visible;
            CancelButton.IsEnabled = false;
            ActionButton.IsEnabled = false;

            try
            {
                // Kill any running instance of the app before installing
                UpdateStatus("Closing running instances...");
                await Task.Run(() =>
                {
                    try
                    {
                        var processes = System.Diagnostics.Process.GetProcessesByName("TelnetCommanderPro");
                        foreach (var proc in processes)
                        {
                            try
                            {
                                proc.Kill();
                                proc.WaitForExit(5000);
                            }
                            catch { }
                        }
                    }
                    catch { }
                });
                await Task.Delay(1500); // Give OS time to release file locks

                UpdateStatus("Creating installation directory...");
                await Task.Delay(300);
                Directory.CreateDirectory(installPath);

                UpdateStatus("Copying application files...");
                await Task.Delay(300);
                await CopyFilesAsync();

                UpdateStatus("Creating shortcuts...");
                await Task.Delay(300);
                CreateShortcuts();

                UpdateStatus("Registering application...");
                await Task.Delay(300);
                RegisterUninstaller();

                await Task.Delay(500);
                ShowCompletePage();
            }
            catch (Exception ex)
            {
                ShowThemedConfirm(
                    $"Installation failed:\n{ex.Message}",
                    "Installation Error",
                    () => Application.Current.Shutdown());
            }
        }

        private void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
            });
        }

        private async Task CopyFilesAsync()
        {
            await Task.Run(() =>
            {
                UpdateStatus("Extracting application files...");
                
                // Get embedded zip resource
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "TelnetCommanderProInstaller.app_payload.zip";
                
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        throw new Exception("Application payload not found in installer. Please rebuild the installer.");
                    }
                    
                    // Save zip to temp location
                    string tempZip = Path.Combine(Path.GetTempPath(), "tcp_install_temp.zip");
                    
                    using (var fileStream = File.Create(tempZip))
                    {
                        stream.CopyTo(fileStream);
                    }
                    
                    UpdateStatus("Installing application files...");
                    
                    // Extract zip to install location
                    System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, installPath, true);
                    
                    // Clean up temp file
                    File.Delete(tempZip);
                }
            });
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                System.IO.File.Copy(file, destFile, true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }

        private void CreateShortcuts()
        {
            string exePath = Path.Combine(installPath, "TelnetCommanderPro.exe");
            
            // Create simple .url shortcuts (works without COM)
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            CreateUrlShortcut(Path.Combine(desktopPath, "Telnet Commander Pro.url"), exePath);

            string startMenuPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                "Programs", "Telnet Commander Pro");
            Directory.CreateDirectory(startMenuPath);
            CreateUrlShortcut(Path.Combine(startMenuPath, "Telnet Commander Pro.url"), exePath);
        }

        private void CreateUrlShortcut(string shortcutPath, string targetPath)
        {
            using (StreamWriter writer = new StreamWriter(shortcutPath))
            {
                writer.WriteLine("[InternetShortcut]");
                writer.WriteLine("URL=file:///" + targetPath);
                writer.WriteLine("IconIndex=0");
                writer.WriteLine("IconFile=" + targetPath);
            }
        }

        private void RegisterUninstaller()
        {
            string uninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TelnetCommanderPro";
            
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(uninstallKey))
            {
                key.SetValue("DisplayName", "Telnet Commander Pro");
                key.SetValue("DisplayVersion", _newVersion);
                key.SetValue("Publisher", "Telnet Commander Pro");
                key.SetValue("DisplayIcon", Path.Combine(installPath, "TelnetCommanderPro.exe"));
                key.SetValue("InstallLocation", installPath);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        private void ShowCompletePage()
        {
            InstallingPage.Visibility = Visibility.Collapsed;
            CompletePage.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Collapsed;
            ActionButton.Content = "Finish";
            ActionButton.IsEnabled = true;
            
            // Update completion message based on installation type
            Dispatcher.Invoke(() =>
            {
                if (isUpdate)
                {
                    CompleteTitle.Text = "Update Complete!";
                    if (!string.IsNullOrEmpty(installedVersion))
                    {
                        CompleteMessage.Text = $"Telnet Commander Pro has been successfully updated from version {installedVersion} to {_newVersion}.";
                    }
                    else
                    {
                        CompleteMessage.Text = $"Telnet Commander Pro has been successfully updated to version {_newVersion}.";
                    }
                }
                else
                {
                    CompleteTitle.Text = "Installation Complete!";
                    CompleteMessage.Text = "Telnet Commander Pro has been successfully installed on your computer.";
                }
            });
        }
        
        private void CheckExistingInstallation()
        {
            try
            {
                // Check if application is already installed
                if (Directory.Exists(installPath))
                {
                    string exePath = Path.Combine(installPath, "TelnetCommanderPro.exe");
                    if (File.Exists(exePath))
                    {
                        isUpdate = true;
                        
                        // Try to get installed version from registry
                        try
                        {
                            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TelnetCommanderPro"))
                            {
                                if (key != null)
                                {
                                    installedVersion = key.GetValue("DisplayVersion") as string;
                                }
                            }
                        }
                        catch { }
                        
                        // If couldn't get from registry, try from file
                        if (string.IsNullOrEmpty(installedVersion))
                        {
                            try
                            {
                                var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                                installedVersion = versionInfo.FileVersion;
                            }
                            catch { }
                        }
                        
                        UpdateWelcomePageForUpdate();
                    }
                }
            }
            catch { }
        }
        
        private void UpdateWelcomePageForUpdate()
        {
            Dispatcher.Invoke(() =>
            {
                if (isUpdate)
                {
                    // Change title
                    WelcomeTitle.Text = "Update Telnet Commander Pro";
                    
                    // Change description
                    if (!string.IsNullOrEmpty(installedVersion))
                    {
                        WelcomeDescription.Text = $"This wizard will update Telnet Commander Pro from version {installedVersion} to {_newVersion} on your computer.";
                    }
                    else
                    {
                        WelcomeDescription.Text = $"This wizard will update Telnet Commander Pro to version {_newVersion} on your computer.";
                    }
                    
                    // Add update details
                    UpdateDetails.Visibility = Visibility.Visible;
                    UpdateDetailsText.Text = 
                        $"What's New in Version {_newVersion}:\n\n" +
                        "• V6/X6 routers: restart guidance after unlock\n" +
                        "• Warns not to power cycle - use web interface reboot\n" +
                        "• Operation count preserved across updates\n" +
                        "• Smaller installer size (~72 MB)\n" +
                        "• .NET 6 check with download link if missing";
                    
                    // Change button text
                    ActionButton.Content = "Update";
                    
                    // Change tagline
                    WelcomeTagline.Text = "⚡ Upgrading your router control experience";
                }
            });
        }
    }
}
