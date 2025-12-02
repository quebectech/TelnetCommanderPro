# Telnet Commander Pro

**Your gateway to precision router control**

A modern Windows desktop application for executing Telnet commands on Huawei routers (V5, V6, and X6).

## Features

- ✨ Modern dark-themed UI with gradient accents
- 🔐 Hardware-locked license validation (one PC per license key)
- 📡 Built-in Telnet client for Huawei routers
- 🚀 Offline operation after activation
- 📝 Script execution support
- 💾 Session log saving
- ⚡ Quick command shortcuts
- 🔄 Auto-update system

## System Requirements

- Windows 10/11 (64-bit)
- .NET 8.0 Runtime
- Internet connection (for initial activation only)

## Building from Source

1. Install .NET 8.0 SDK
2. Clone this repository
3. Run the build command:

```cmd
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The compiled `.exe` will be in `bin\Release\net8.0-windows\win-x64\publish\`

## Usage

1. Launch the application
2. Enter your license key on first run
3. Select router type (Huawei V5, V6, or X6)
4. Enter connection details (default: 192.168.100.1)
5. Click Connect
6. Execute commands or upload scripts

## License Activation

- Each license key is bound to one PC's hardware ID
- Hardware ID is generated from CPU, Disk, and MAC address
- Online validation required for first activation
- Offline mode available after successful activation

## Quick Commands

- Show Config
- Show Version
- Show Interfaces
- Reboot Router

## Script Execution

Upload `.txt` files with Telnet commands (one per line). Comments start with `#`.

Example script:
```
# Display router information
display version
display current-configuration
display interface brief
```

## Configuration

License data is stored in Windows Registry:
`HKEY_CURRENT_USER\SOFTWARE\TelnetCommanderPro`

## Support

For license issues or technical support, contact your administrator.

## Version

Current Version: 1.0.0
