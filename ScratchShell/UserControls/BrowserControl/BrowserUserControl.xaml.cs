using ScratchShell.UserControls.BrowserControl;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScratchShell.UserControls.BrowserControl
{
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
            SetupContextMenu();
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

            AddMenuItem("Copy", (_, __) => RaiseContextEvent(CopyRequested));
            AddMenuItem("Cut", (_, __) => RaiseContextEvent(CutRequested));
            AddMenuItem("Paste", (_, __) => RaiseContextEvent(PasteRequested));
            AddMenuItem("Download", (_, __) => RaiseContextEvent(DownloadRequested));
            AddMenuItem("Upload", (_, __) => RaiseContextEvent(UploadRequested));
        }

        private void AddMenuItem(string header, RoutedEventHandler handler)
        {
            var menuItem = new MenuItem { Header = header };
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
            if (BrowserList.SelectedItem != null)
            {
                contextMenu.PlacementTarget = BrowserList;
                contextMenu.IsOpen = true;
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
}
