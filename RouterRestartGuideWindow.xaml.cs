using System.Windows;
using System.Windows.Input;

namespace TelnetCommanderPro
{
    public partial class RouterRestartGuideWindow : Window
    {
        public RouterRestartGuideWindow()
        {
            InitializeComponent();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
