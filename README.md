# ScratchShell

ScratchShell is a desktop application built on .NET 9 using WPF. It aims to provide a simple and efficient interface for tasks such as SFTP management and browser-based interactions.

## Features

- **SFTP Management:**  
  Utilize the integrated SFTP user control for efficient file transfers and remote server management.

- **Browser Integration:**  
  The included browser control allows you to embed web content and interact with online services directly within the application.

- **Modern WPF UI:**  
  The application leverages up-to-date WPF patterns utilizing XAML for a flexible, modular design.

## Project Structure

- **App.xaml:**  
  The starting point of the application, responsible for application-wide resources and initialization.

- **MainWindow.xaml:**  
  The primary window hosting the application’s main interface.

- **User Controls:**  
  - `SftpUserControl.xaml`: Contains UI components for SFTP functionality.
  - `BrowserUserControl.xaml`: Embeds web browser functionality into the main application window.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) installed on your machine.
- An environment capable of running WPF applications (Windows 10 or later recommended).

## Getting Started

1. **Clone the Repository:**
