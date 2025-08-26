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
    /// Adapter to make different path controls work uniformly for navigation purposes
    /// </summary>
    public class PathTextBoxAdapter
    {
        private readonly object _control;

        public PathTextBoxAdapter(Wpf.Ui.Controls.AutoSuggestBox autoSuggestBox)
        {
            _control = autoSuggestBox ?? throw new ArgumentNullException(nameof(autoSuggestBox));
        }

        public PathTextBoxAdapter(ScratchShell.UserControls.BreadcrumbAddressBar breadcrumbAddressBar)
        {
            _control = breadcrumbAddressBar ?? throw new ArgumentNullException(nameof(breadcrumbAddressBar));
        }

        public string Text
        {
            get
            {
                return _control switch
                {
                    Wpf.Ui.Controls.AutoSuggestBox autoSuggest => autoSuggest.Text ?? string.Empty,
                    ScratchShell.UserControls.BreadcrumbAddressBar breadcrumb => breadcrumb.CurrentPath,
                    _ => string.Empty
                };
            }
            set
            {
                switch (_control)
                {
                    case Wpf.Ui.Controls.AutoSuggestBox autoSuggest:
                        autoSuggest.Text = value;
                        break;
                    case ScratchShell.UserControls.BreadcrumbAddressBar breadcrumb:
                        breadcrumb.CurrentPath = value;
                        break;
                }
            }
        }

        public bool IsEnabled
        {
            get
            {
                return _control switch
                {
                    Wpf.Ui.Controls.AutoSuggestBox autoSuggest => autoSuggest.IsEnabled,
                    ScratchShell.UserControls.BreadcrumbAddressBar breadcrumb => breadcrumb.IsEnabled,
                    _ => false
                };
            }
            set
            {
                switch (_control)
                {
                    case Wpf.Ui.Controls.AutoSuggestBox autoSuggest:
                        autoSuggest.IsEnabled = value;
                        break;
                    case ScratchShell.UserControls.BreadcrumbAddressBar breadcrumb:
                        breadcrumb.IsEnabled = value;
                        break;
                }
            }
        }

        /// <summary>
        /// Gets the underlying control instance
        /// </summary>
        public object Control => _control;
    }
}