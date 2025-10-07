// Views/ActivityCenterSection.xaml.cs
using System.Windows;  
using System.Windows.Controls;
namespace TaskMate.Views {
    public partial class ConnectionsSection : UserControl {
        public ConnectionsSection() { InitializeComponent(); }
        private void CopyUserCode_Click(object sender, RoutedEventArgs e) {
            if(!string.IsNullOrWhiteSpace(UserCodeBox?.Text)) {
                Clipboard.SetText(UserCodeBox.Text);
                // optional: visual feedback
                UserCodeBox.SelectAll();
            }
        }
    }
}