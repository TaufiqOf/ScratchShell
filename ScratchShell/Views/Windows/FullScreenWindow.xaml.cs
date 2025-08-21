using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Wpf.Ui.Controls;

namespace ScratchShell.Views.Windows
{
    /// <summary>
    /// Interaction logic for FullScreenWindow.xaml
    /// </summary>
    public partial class FullScreenWindow : FluentWindow
    {
        public FullScreenWindow()
        {
            InitializeComponent();
        }

        public FullScreenWindow(object content, string name, object? menuControl = null)
        {
            InitializeComponent();
            this.Title = name;
            TitleBar.Title = name;
            RootContentDialog.Content = content;
            MenuControl.Content = menuControl;

        }
    }
}
