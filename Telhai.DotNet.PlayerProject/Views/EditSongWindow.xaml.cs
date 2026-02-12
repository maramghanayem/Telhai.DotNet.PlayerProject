using Microsoft.Win32;
using System.Windows;
using Telhai.DotNet.PlayerProject.ViewModels;

namespace Telhai.DotNet.PlayerProject.Views
{
    public partial class EditSongWindow : Window
    {
        private readonly EditSongViewModel _vm;

        public EditSongWindow(string filePath)
        {
            InitializeComponent();
            _vm = new EditSongViewModel(filePath);
            DataContext = _vm;
        }

        private void BtnAddImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp";
            ofd.Multiselect = true;

            if (ofd.ShowDialog() == true)
            {
                foreach (var f in ofd.FileNames)
                {
                    _vm.AddImage(f);
                }
            }
        }

        
        private void BtnRemoveImage_Click(object sender, RoutedEventArgs e)
        {
            if (lstImages.SelectedItem is string path)
            {
                _vm.RemoveImage(path);
                lstImages.Items.Refresh(); 
            }
            else
            {
                MessageBox.Show("Select an image first.");
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _vm.Save();
            MessageBox.Show("Saved!");
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
