using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Renci.SshNet;
using ScratchShell.Services.EventHandlers;
using ScratchShell.UserControls.BrowserControl;

namespace ScratchShell.Services.Navigation
{
    /// <summary>
    /// Represents the navigation buttons for SFTP control
    /// </summary>
    public class NavigationButtons
    {
        public System.Windows.Controls.Button? BackButton { get; set; }
        public System.Windows.Controls.Button? ForwardButton { get; set; }
        public System.Windows.Controls.Button? UpButton { get; set; }
        public System.Windows.Controls.Button? RefreshButton { get; set; }
        public System.Windows.Controls.Button? CreateFolderButton { get; set; }
        public System.Windows.Controls.Button? PasteButton { get; set; }
        public System.Windows.Controls.Button? OptionsButton { get; set; }
        public System.Windows.Controls.Button? FullScreenButton { get; set; }
        public System.Windows.Controls.ProgressBar? Progress { get; set; }
    }

    /// <summary>
    /// Interface for managing SFTP navigation
    /// </summary>
    public interface ISftpNavigationManager : IDisposable
    {
        string CurrentPath { get; }
        bool CanNavigateBack { get; }
        bool CanNavigateForward { get; }
        bool IsAtRoot { get; }
        
        void Initialize(SftpClient? client, PathTextBoxAdapter? pathTextBox, NavigationButtons? buttons);
        Task GoToFolderAsync(string path);
        Task NavigateBackAsync();
        Task NavigateForwardAsync();
        Task NavigateUpAsync();
        Task RefreshCurrentDirectoryAsync();
        void UpdateNavigationButtonStates();
    }
}