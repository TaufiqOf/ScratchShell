using ScratchShell.ViewModels.Models;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace ScratchShell.View.Dialog
{
    /// <summary>
    /// Interaction logic for SnippetContentDialog.xaml
    /// </summary>
    public partial class SnippetContentDialog : ContentDialog
    {
        public SnippetViewModel ViewModel { get; }

        public SnippetContentDialog(ContentPresenter? contentPresenter, SnippetViewModel viewModel)
            : base(contentPresenter)
        {
            InitializeComponent();
            this.ViewModel = viewModel;
            DataContext = new SnippetViewModel(viewModel, viewModel.ContentDialogService);
        }

        protected override void OnButtonClick(ContentDialogButton button)
        {
            if (button == ContentDialogButton.Primary)
            {
                if (DataContext is SnippetViewModel viewModel)
                {
                    // Check if the form is valid
                    if (!viewModel.IsValid)
                    {
                        // Find and show validation errors
                        ShowValidationErrors();
                        return;
                    }

                    // Update the original view model with validated data
                    ViewModel.Name = viewModel.Name;
                    ViewModel.Code = viewModel.Code;
                }
            }
            base.OnButtonClick(button);
        }

        private void ShowValidationErrors()
        {
            // Force validation on all named textboxes by finding them
            var nameTextBox = FindChild<TextBox>(this, "NameTextBox");
            var codeTextBox = FindChild<TextBox>(this, "CodeTextBox");
            var validationSummary = FindChild<Border>(this, "ValidationSummary");
            var validationSummaryText = FindChild<System.Windows.Controls.TextBlock>(this, "ValidationSummaryText");

            // Force validation updates
            nameTextBox?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            codeTextBox?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

            // Show validation summary
            if (validationSummary != null && validationSummaryText != null && DataContext is SnippetViewModel viewModel)
            {
                var errors = new List<string>();

                var nameError = viewModel[nameof(viewModel.Name)];
                if (!string.IsNullOrEmpty(nameError))
                    errors.Add($"• {nameError}");

                var codeError = viewModel[nameof(viewModel.Code)];
                if (!string.IsNullOrEmpty(codeError))
                    errors.Add($"• {codeError}");

                if (errors.Any())
                {
                    validationSummaryText.Text = string.Join("\n", errors);
                    validationSummary.Visibility = System.Windows.Visibility.Visible;
                }
            }
        }

        // Helper method to find child controls by name
        private static T? FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            if (parent == null) return null;

            T? foundChild = null;

            int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is T t && (child as FrameworkElement)?.Name == childName)
                {
                    foundChild = t;
                    break;
                }

                foundChild = FindChild<T>(child, childName);
                if (foundChild != null) break;
            }

            return foundChild;
        }
    }
}