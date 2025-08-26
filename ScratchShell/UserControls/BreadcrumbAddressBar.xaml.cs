using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScratchShell.UserControls
{
    /// <summary>
    /// A Windows Explorer-style breadcrumb address bar that displays clickable path segments
    /// and allows switching to text input mode for direct path entry
    /// </summary>
    public partial class BreadcrumbAddressBar : UserControl
    {
        private string _currentPath = "/";
        private bool _isInTextMode = false;

        /// <summary>
        /// Event fired when a path segment is clicked or a new path is entered
        /// </summary>
        public event EventHandler<PathChangedEventArgs>? PathChanged;

        /// <summary>
        /// Gets or sets the current path displayed in the address bar
        /// </summary>
        public string CurrentPath
        {
            get => _currentPath;
            set
            {
                if (_currentPath != value)
                {
                    _currentPath = value ?? "/";
                    UpdateBreadcrumbs();
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether the control is in text input mode
        /// </summary>
        public bool IsInTextMode
        {
            get => _isInTextMode;
            private set
            {
                if (_isInTextMode != value)
                {
                    _isInTextMode = value;
                    UpdateDisplayMode();
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Dependency property for CurrentPath to support binding
        /// </summary>
        public static readonly DependencyProperty CurrentPathProperty =
            DependencyProperty.Register(
                nameof(CurrentPath),
                typeof(string),
                typeof(BreadcrumbAddressBar),
                new PropertyMetadata("/", OnCurrentPathChanged));

        public BreadcrumbAddressBar()
        {
            InitializeComponent();
            UpdateBreadcrumbs();
            
            // Handle clicks on the control background to enter text mode
            this.MouseLeftButtonDown += BreadcrumbAddressBar_MouseLeftButtonDown;
        }

        /// <summary>
        /// Switches to text input mode for manual path entry
        /// </summary>
        public void EnterTextMode()
        {
            IsInTextMode = true;
            PathTextBox.Text = CurrentPath;
            PathTextBox.Focus();
            PathTextBox.SelectAll();
        }

        /// <summary>
        /// Switches back to breadcrumb display mode
        /// </summary>
        public void ExitTextMode()
        {
            IsInTextMode = false;
            UpdateBreadcrumbs();
        }

        private static void OnCurrentPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BreadcrumbAddressBar control)
            {
                control.CurrentPath = e.NewValue?.ToString() ?? "/";
            }
        }

        private void UpdateDisplayMode()
        {
            if (IsInTextMode)
            {
                BreadcrumbScrollViewer.Visibility = Visibility.Collapsed;
                PathTextBox.Visibility = Visibility.Visible;
            }
            else
            {
                BreadcrumbScrollViewer.Visibility = Visibility.Visible;
                PathTextBox.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateBreadcrumbs()
        {
            BreadcrumbPanel.Children.Clear();

            if (string.IsNullOrEmpty(CurrentPath))
                return;

            var pathSegments = GetPathSegments(CurrentPath);
            
            for (int i = 0; i < pathSegments.Count; i++)
            {
                var segment = pathSegments[i];
                
                // Add separator before each segment except the first
                if (i > 0)
                {
                    var separator = new TextBlock();
                    separator.Style = (Style)FindResource("SeparatorStyle");
                    BreadcrumbPanel.Children.Add(separator);
                }

                // Create clickable button for the segment
                var button = new Button
                {
                    Content = segment.DisplayName,
                    Style = (Style)FindResource("BreadcrumbButtonStyle"),
                    Tag = segment.FullPath,
                    ToolTip = segment.FullPath
                };

                button.Click += BreadcrumbButton_Click;
                BreadcrumbPanel.Children.Add(button);
            }

            // Scroll to the end to show the current location
            BreadcrumbScrollViewer.ScrollToRightEnd();
        }

        private List<PathSegment> GetPathSegments(string path)
        {
            var segments = new List<PathSegment>();
            
            if (string.IsNullOrEmpty(path) || path == "/")
            {
                segments.Add(new PathSegment("Root", "/"));
                return segments;
            }

            // Handle different path formats (Windows and Unix style)
            var normalizedPath = path.Replace('\\', '/');
            if (!normalizedPath.StartsWith("/"))
                normalizedPath = "/" + normalizedPath;

            // Split path into segments
            var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            // Add root
            segments.Add(new PathSegment("Root", "/"));
            
            // Add each path segment
            var currentPath = "";
            foreach (var part in parts)
            {
                currentPath += "/" + part;
                segments.Add(new PathSegment(part, currentPath));
            }

            return segments;
        }

        private void BreadcrumbButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string targetPath)
            {
                NavigateToPath(targetPath);
            }
        }

        private void BreadcrumbAddressBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Only enter text mode if clicking on empty space, not on buttons
            if (e.OriginalSource == this || e.OriginalSource == BreadcrumbScrollViewer || e.OriginalSource == BreadcrumbPanel)
            {
                EnterTextMode();
                e.Handled = true;
            }
        }

        private void PathTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var newPath = PathTextBox.Text?.Trim();
                if (!string.IsNullOrEmpty(newPath))
                {
                    NavigateToPath(newPath);
                }
                ExitTextMode();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ExitTextMode();
                e.Handled = true;
            }
        }

        private void PathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Exit text mode when focus is lost, but don't navigate
            ExitTextMode();
        }

        private void NavigateToPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            var args = new PathChangedEventArgs(path);
            PathChanged?.Invoke(this, args);

            // Update current path if navigation wasn't cancelled
            if (!args.Cancel)
            {
                CurrentPath = path;
            }
        }

        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            // This could be used for INotifyPropertyChanged if needed in the future
        }

        /// <summary>
        /// Represents a single segment in the breadcrumb path
        /// </summary>
        private class PathSegment
        {
            public string DisplayName { get; }
            public string FullPath { get; }

            public PathSegment(string displayName, string fullPath)
            {
                DisplayName = displayName;
                FullPath = fullPath;
            }
        }
    }

    /// <summary>
    /// Event arguments for path change events
    /// </summary>
    public class PathChangedEventArgs : EventArgs
    {
        public string NewPath { get; }
        public bool Cancel { get; set; }

        public PathChangedEventArgs(string newPath)
        {
            NewPath = newPath;
        }
    }
}