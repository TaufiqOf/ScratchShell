using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using ListViewItem = Wpf.Ui.Controls.ListViewItem;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace ScratchShell.UserControls.BrowserControl;

public partial class BrowserUserControl : UserControl
{
    public ObservableCollection<BrowserItem> Items { get; private set; } = new ObservableCollection<BrowserItem>();

    // Delegate events
    public event Action<BrowserItem>? CopyRequested;

    public event Action<BrowserItem>? CutRequested;

    public event Action<BrowserItem>? PasteRequested;

    public event Action<BrowserItem>? UploadRequested;

    public event Action<BrowserItem>? DownloadRequested;

    public event Action<BrowserItem>? EnterRequested;

    private ContextMenu contextMenu;
    private readonly Dictionary<string, MenuItem> menuItems = new();

    public BrowserUserControl()
    {
        InitializeComponent();
        BrowserList.ItemsSource = Items;
        BrowserList.MouseDoubleClick += BrowserListMouseDoubleClick;
        BrowserList.PreviewMouseDown += BrowserListPreviewMouseDown;
        SetupContextMenu();
    }

    private void BrowserListPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            // Find the container that was clicked (ListViewItem / TreeViewItem / etc.)
            var depObj = (DependencyObject)e.OriginalSource;
            while (depObj != null && depObj is not ListViewItem)
            {
                depObj = VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is ListViewItem listViewItem && listViewItem.DataContext is BrowserItem item)
            {
                if (item.Name == "..")
                {
                    SetMenuVisibility("Cut", false);
                    SetMenuVisibility("Copy", false);
                    SetMenuVisibility("Paste", false);
                    SetMenuVisibility("Separator", false);
                    SetMenuVisibility("Upload", false);
                    SetMenuVisibility("Download", false);
                    e.Handled = true;
                    return;
                }
                // Now you have the actual BrowserItem
                if (item.IsFolder)
                {
                    SetMenuVisibility("Cut", true);
                    SetMenuVisibility("Copy", true);
                    SetMenuVisibility("Paste", true);
                    SetMenuVisibility("Separator", false);
                    SetMenuVisibility("Upload", true);
                    SetMenuVisibility("Download", true);
                }
                else
                {
                    SetMenuVisibility("Cut", true);
                    SetMenuVisibility("Copy", true);
                    SetMenuVisibility("Paste", true);
                    SetMenuVisibility("Separator", true);
                    SetMenuVisibility("Upload", false);
                    SetMenuVisibility("Download", true);
                }

                e.Handled = true;
            }
        }
    }

    private void BrowserListMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            RaiseContextEvent(EnterRequested);
        }
    }

    private void SetupContextMenu()
    {
        contextMenu = new ContextMenu();
        AddMenuItem("Cut", (_, __) => RaiseContextEvent(CutRequested), SymbolRegular.Cut24);
        AddMenuItem("Copy", (_, __) => RaiseContextEvent(CopyRequested), SymbolRegular.Copy24);
        AddMenuItem("Paste", (_, __) => RaiseContextEvent(PasteRequested), SymbolRegular.ClipboardPaste24);
        var sep = new Separator();
        contextMenu.Items.Add(sep);
        menuItems["Separator"] = new MenuItem { Header = "Separator", Visibility = Visibility.Collapsed };
        sep.DataContext = menuItems["Separator"];
        AddMenuItem("Upload", (_, __) => RaiseContextEvent(UploadRequested), SymbolRegular.ArrowUpload24);
        AddMenuItem("Download", (_, __) => RaiseContextEvent(DownloadRequested), SymbolRegular.ArrowDownload24);
    }

    private void AddMenuItem(string header, RoutedEventHandler handler, SymbolRegular? icon = null)
    {
        var menuItem = new MenuItem
        {
            Header = header,
        };
        menuItem.Icon = icon.HasValue ? new SymbolIcon(icon.Value) : null;
        menuItem.Click += handler;
        contextMenu.Items.Add(menuItem);
        menuItems[header] = menuItem;
    }

    private void RaiseContextEvent(Action<BrowserItem>? action)
    {
        if (BrowserList.SelectedItem is BrowserItem item)
            action?.Invoke(item);
    }

    private void BrowserList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            // Find the container that was clicked (ListViewItem / TreeViewItem / etc.)
            var depObj = (DependencyObject)e.OriginalSource;
            while (depObj != null && depObj is not ListViewItem)
            {
                depObj = VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is ListViewItem listViewItem && listViewItem.DataContext is BrowserItem item)
            {
                if (item.Name != "..")
                {
                    contextMenu.PlacementTarget = BrowserList;
                    contextMenu.IsOpen = true;
                }
            }
        }
    }

    // 📣 Public API

    public void LoadItems(IEnumerable<BrowserItem> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
    }

    public void AddItem(BrowserItem item)
    {
        Items.Add(item);
    }

    public void RemoveItem(BrowserItem item)
    {
        Items.Remove(item);
    }

    public void Clear()
    {
        Items.Clear();
    }

    public void Refresh()
    {
        BrowserList.Items.Refresh();
    }

    // 📣 Context Menu Visibility / Enable Control

    public void SetMenuVisibility(string menuHeader, bool visible)
    {
        if (menuItems.TryGetValue(menuHeader, out var item))
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetMenuEnabled(string menuHeader, bool enabled)
    {
        if (menuItems.TryGetValue(menuHeader, out var item))
            item.IsEnabled = enabled;
    }
}