using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Telhai.DotNet.PlayerProject.Models;
using Telhai.DotNet.PlayerProject.Services;
//m
namespace Telhai.DotNet.PlayerProject.ViewModels
{
    public class EditSongViewModel : INotifyPropertyChanged
    {
        private readonly SongMetadataCacheService _cache = SongMetadataCacheService.Instance;

        private readonly string _filePath;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string FilePath => _filePath;

        private string _trackName = "";
        public string TrackName
        {
            get => _trackName;
            set { _trackName = value; OnPropertyChanged(); }
        }

        private string _artistName = "";
        public string ArtistName
        {
            get => _artistName;
            set { _artistName = value; OnPropertyChanged(); }
        }

        private string _albumName = "";
        public string AlbumName
        {
            get => _albumName;
            set { _albumName = value; OnPropertyChanged(); }
        }

        // Images list for this song
        public ObservableCollection<string> Images { get; set; } = new ObservableCollection<string>();

        public EditSongViewModel(string filePath)
        {
            _filePath = filePath;

            var cached = _cache.GetByFilePath(filePath);
            if (cached != null)
            {
                TrackName = cached.TrackName ?? "";
                ArtistName = cached.ArtistName ?? "";
                AlbumName = cached.AlbumName ?? "";

                if (cached.ImageUrls != null)
                {
                    foreach (var img in cached.ImageUrls)
                        Images.Add(img);
                }
            }
        }

        public void AddImage(string imagePath)
        {
            if (!Images.Contains(imagePath))
                Images.Add(imagePath);
        }

        public void RemoveImage(string imagePath)
        {
            if (Images.Contains(imagePath))
                Images.Remove(imagePath);
        }

        public void Save()
        {
            // Keep existing iTunes artwork if it exists
            var existing = _cache.GetByFilePath(_filePath);

            var meta = new SongMetadata
            {
                FilePath = _filePath,
                TrackName = TrackName,
                ArtistName = ArtistName,
                AlbumName = AlbumName,

                // ✅ THIS is what must be saved (including deletions)
                ImageUrls = new System.Collections.Generic.List<string>(Images),

                // keep iTunes artwork so player can show it when no images exist
                ItunesArtworkUrl = existing?.ItunesArtworkUrl ?? ""
            };

            _cache.Upsert(meta);
        }

        private void OnPropertyChanged([CallerMemberName] string name = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
