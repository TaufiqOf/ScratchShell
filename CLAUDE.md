# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

ScratchShell is a WPF desktop application built on .NET 9 that provides a unified terminal and remote connection management interface. It supports SSH, FTP, and SFTP protocols with advanced terminal emulation capabilities.

## Build and Development Commands

```bash
# Build the project
dotnet build

# Run the application
dotnet run --project ScratchShell/ScratchShell.csproj

# Build in Release mode
dotnet build -c Release

# Clean build artifacts
dotnet clean

# Restore NuGet packages
dotnet restore

# Publish for deployment
dotnet publish -c Release -r win-x64 --self-contained
```

## Architecture

### Core Components

**MVVM Architecture**: The application follows Model-View-ViewModel pattern with:
- Views: XAML-based UI components in `Views/` directory
- ViewModels: Business logic and data binding in `ViewModels/`
- Models: Data structures in `Models/`

**Dependency Injection**: Uses Microsoft.Extensions.Hosting with service registration in `App.xaml.cs`. All pages and view models are registered as singletons in the DI container.

**Navigation System**: WPF-UI based navigation with:
- `MainWindow` as navigation host
- Pages: DashboardPage (server list), SessionPage (active sessions), SettingsPage
- Navigation items defined in `MainWindowViewModel`

### Terminal Emulation System

The terminal system (`UserControls/TerminalControl/`) is a critical component with multiple implementations:

**Terminal Controls**:
- `TerminalUserControl`: Main terminal control with ANSI support
- `SecureTerminal`: Security-focused terminal using TextBlocks for read-only output
- `ReadOnlyTerminal`: Physically separated input/output areas
- `TerminalBuffer`: Character-grid buffer system for full-screen apps

**ANSI Processing Pipeline**:
1. `AnsiParser`: Parses ANSI escape sequences into segments
2. `AnsiControlParser`: Handles control sequences (cursor, colors, modes)
3. Command handlers in `Handler/` directory:
   - `CursorCommandHandler`: Cursor positioning
   - `EraseCommandHandler`: Screen/line clearing
   - `ModeCommandHandler`: Terminal modes
   - `OscCommandHandler`: Operating System Commands
   - `AsciiControlHandler`: ASCII control characters
4. `TextRenderer`: Converts segments to WPF Run elements

**Terminal State Management**:
- `TerminalState`: Maintains cursor position, colors, modes, buffers
- Supports alternate screen buffer for full-screen apps (vim, htop)
- Tracks terminal dimensions and scroll regions

### Server Management

**ServerManager** (`Services/ServerManager.cs`):
- Central service managing server connections
- Maintains encrypted server list in application settings
- Fires events for server operations (add/remove/edit/select)
- Uses `EncryptionHelper` for secure credential storage

### Protocol Support

**Connection Types**:
- SSH: Full terminal emulation with PTY support
- FTP: Basic file transfer protocol
- SFTP: Secure file transfer over SSH

**Terminal System** (`Services/Terminal/`):
- `ITerminalLauncher`: Interface for shell launchers (CMD, PowerShell, Windows Terminal)
- `IShellCommandBuilder`: Protocol-specific command builders
- `CommandBuilderFactory`: Factory pattern for creating appropriate builders

### User Controls

- **SshUserControl**: SSH terminal with full ANSI support
- **FtpUserControl**: FTP client interface
- **SftpUserControl**: SFTP file transfer interface
- **BrowserUserControl**: Embedded browser functionality
- **TerminalUserControl**: Core terminal emulator

## Key Dependencies

- **WPF-UI**: Modern WPF controls and theming
- **SSH.NET (Renci.SshNet)**: SSH/SFTP protocol implementation
- **CommunityToolkit.Mvvm**: MVVM helpers and observable properties
- **Newtonsoft.Json**: JSON serialization
- **Microsoft.Extensions.Hosting**: Dependency injection and application hosting
- **Humanizer**: String formatting utilities

## Terminal Implementation Details

### Security Features
- Output is read-only and cannot be edited
- Input is restricted to command line area only
- Mouse clicks in output area are blocked
- Special handling for full-screen applications

### ANSI Support
- Full CSI sequence support (cursor movement, colors, clearing)
- OSC sequences (window title, colors)
- Alternate screen buffer for TUI applications
- 256-color and RGB color support
- Text styling (bold, italic, underline, etc.)

### Known Terminal Modes
- Line wrap mode
- Application cursor keys
- Alternate screen buffer
- Mouse tracking modes
- Focus event tracking
- 132 column mode

## Data Persistence

- Server configurations stored encrypted in `Properties.Settings`
- Uses Windows DPAPI via `EncryptionHelper`
- Settings include servers list and theme preference

## Protocol Details

Defined in `Enums/ProtocolType.cs`:
- SSH = 21 (typically port 22)
- FTP = 22 (typically port 21)  
- SFTP = 23 (typically port 22)

Each protocol has dedicated UI controls and command builders for protocol-specific features.