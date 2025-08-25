using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using ScratchShell.Services.FileOperations;
using ScratchShell.Services.Navigation;
using ScratchShell.UserControls.BrowserControl;

namespace ScratchShell.Services.EventHandlers
{
    /// <summary>
    /// Interface for handling SFTP events with safe execution
    /// </summary>
    public interface ISftpEventHandler
    {
        void SetupBrowserEvents(BrowserUserControl browser, PathTextBoxAdapter pathTextBox, 
            ISftpNavigationManager navigationManager, ISftpFileOperationHandler fileOperationHandler);
        
        void HandleKeyDown(KeyEventArgs e, ISftpNavigationManager navigationManager, 
            ISftpFileOperationHandler fileOperationHandler);
        
        void SafeExecute(Action action);
        Task SafeExecuteAsync(Func<Task> asyncAction);
    }

    /// <summary>
    /// Adapter to make AutoSuggestBox work like a TextBox for navigation purposes
    /// </summary>
    public class PathTextBoxAdapter
    {
        private readonly Wpf.Ui.Controls.AutoSuggestBox _autoSuggestBox;

        public PathTextBoxAdapter(Wpf.Ui.Controls.AutoSuggestBox autoSuggestBox)
        {
            _autoSuggestBox = autoSuggestBox ?? throw new ArgumentNullException(nameof(autoSuggestBox));
        }

        public string Text
        {
            get => _autoSuggestBox.Text ?? string.Empty;
            set => _autoSuggestBox.Text = value;
        }

        public bool IsEnabled
        {
            get => _autoSuggestBox.IsEnabled;
            set => _autoSuggestBox.IsEnabled = value;
        }
    }
}