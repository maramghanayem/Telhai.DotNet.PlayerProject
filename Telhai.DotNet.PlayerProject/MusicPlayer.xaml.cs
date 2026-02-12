using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Telhai.DotNet.PlayerProject.Models;
using Telhai.DotNet.PlayerProject.Services;

namespace Telhai.DotNet.PlayerProject
{
    public partial class MusicPlayer : Window
    {
        private MediaPlayer mediaPlayer = new MediaPlayer();
        private DispatcherTimer timer = new DispatcherTimer();
        private List<MusicTrack> library = new List<MusicTrack>();
        private bool isDragging = false;
        private const string FILE_NAME = "library.json";

        // API + cancellation
        private readonly ItunesService _itunesService = new ItunesService();
        private CancellationTokenSource? _itunesCts;

        // Cache
        private readonly SongMetadataCacheService _cache = SongMetadataCacheService.Instance;


        // Slideshow (2 seconds loop)
        private DispatcherTimer _slideshowTimer = new DispatcherTimer();
        private List<string> _slideshowImages = new List<string>();
        private int _slideshowIndex = 0;

        public MusicPlayer()
        {
            InitializeComponent();

            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += new EventHandler(Timer_Tick);
            this.Loaded += MusicPlayer_Loaded;

            _slideshowTimer.Interval = TimeSpan.FromSeconds(2);
            _slideshowTimer.Tick += SlideshowTimer_Tick;

            ClearMetadataUI();
            ShowDefaultCover();
        }

        private void MusicPlayer_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLibrary();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (mediaPlayer.Source != null && mediaPlayer.NaturalDuration.HasTimeSpan && !isDragging)
            {
                sliderProgress.Maximum = mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                sliderProgress.Value = mediaPlayer.Position.TotalSeconds;
            }
        }

        // Single click: show local info + cached metadata if exists
        private void LstLibrary_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                txtCurrentSong.Text = track.Title;
                txtFilePath.Text = track.FilePath;

                var cached = _cache.GetByFilePath(track.FilePath);
                if (cached != null)
                {
                    ShowMetadataFromCacheOrLocal(track, cached);
                    txtStatus.Text = "Loaded from cache (selection).";
                }
                else
                {
                    StopSlideshow();
                    ClearMetadataUI();
                    ShowDefaultCover();
                    txtMetaTrack.Text = track.Title;
                    txtStatus.Text = "Ready";
                }
            }
        }

        // Double click: play + async metadata
        private void LstLibrary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
                StartPlayingTrack(track);
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                StartPlayingTrack(track);
                return;
            }

            mediaPlayer.Play();
            timer.Start();
            txtStatus.Text = "Playing";
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Pause();
            txtStatus.Text = "Paused";
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Stop();
            timer.Stop();
            sliderProgress.Value = 0;
            txtStatus.Text = "Stopped";

            _itunesCts?.Cancel();
            StopSlideshow();
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            mediaPlayer.Volume = sliderVolume.Value;
        }

        private void Slider_DragStarted(object sender, MouseButtonEventArgs e)
        {
            isDragging = true;
        }

        private void Slider_DragCompleted(object sender, MouseButtonEventArgs e)
        {
            isDragging = false;
            mediaPlayer.Position = TimeSpan.FromSeconds(sliderProgress.Value);
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Multiselect = true;
            ofd.Filter = "Audio Files|*.mp3;*.mpeg;*.wav;*.wma|MP3 Files|*.mp3";

            if (ofd.ShowDialog() == true)
            {
                foreach (string file in ofd.FileNames)
                {
                    MusicTrack track = new MusicTrack
                    {
                        Title = Path.GetFileNameWithoutExtension(file),
                        FilePath = file
                    };
                    library.Add(track);
                }
                UpdateLibraryUI();
                SaveLibrary();
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                library.Remove(track);
                UpdateLibraryUI();
                SaveLibrary();
            }
        }

        // EDIT BUTTON
        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is not MusicTrack track)
            {
                MessageBox.Show("Please select a song first.");
                return;
            }

            var win = new Telhai.DotNet.PlayerProject.Views.EditSongWindow(track.FilePath);
            win.Owner = this;
            win.ShowDialog();

            // Reload from cache after edit
            var cached = _cache.GetByFilePath(track.FilePath);
            if (cached != null)
            {
                ShowMetadataFromCacheOrLocal(track, cached);
                txtStatus.Text = "Updated after edit (cache).";

                // ✅ חשוב: אם השיר כבר מנגן - תתחילי מיד את הלופ לפי התמונות החדשות
                if (mediaPlayer.Source != null && mediaPlayer.Source.LocalPath == track.FilePath)
                {
                    if (cached.ImageUrls != null && cached.ImageUrls.Count > 0)
                        StartSlideshow(cached.ImageUrls);
                    else
                        StopSlideshow(); // יחזור לתמונת API רגילה
                }
            }
        }

        private void UpdateLibraryUI()
        {
            lstLibrary.ItemsSource = null;
            lstLibrary.ItemsSource = library;
        }

        private void SaveLibrary()
        {
            string json = JsonSerializer.Serialize(library);
            File.WriteAllText(FILE_NAME, json);
        }

        private void LoadLibrary()
        {
            if (File.Exists(FILE_NAME))
            {
                string json = File.ReadAllText(FILE_NAME);
                library = JsonSerializer.Deserialize<List<MusicTrack>>(json) ?? new List<MusicTrack>();
                UpdateLibraryUI();
            }
        }

        private void StartPlayingTrack(MusicTrack track)
        {
            if (!File.Exists(track.FilePath))
                return;

            txtCurrentSong.Text = track.Title;
            txtFilePath.Text = track.FilePath;

            mediaPlayer.Open(new Uri(track.FilePath));
            mediaPlayer.Play();
            timer.Start();
            txtStatus.Text = "Playing";

            StopSlideshow();
            ClearMetadataUI();
            ShowDefaultCover();
            txtMetaTrack.Text = track.Title;

            // 1) cache first
            var cached = _cache.GetByFilePath(track.FilePath);
            if (cached != null)
            {
                ShowMetadataFromCacheOrLocal(track, cached);
                txtStatus.Text = "Loaded from cache.";
                return;
            }

            // 2) not in cache -> API
            _itunesCts?.Cancel();
            _itunesCts = new CancellationTokenSource();

            string searchTerm = BuildSearchTermFromFileName(track.Title);
            _ = LoadAndShowMetadataAsync(searchTerm, track, _itunesCts.Token);
        }

        private async Task LoadAndShowMetadataAsync(string searchTerm, MusicTrack track, CancellationToken token)
        {
            try
            {
                Dispatcher.Invoke(() => txtStatus.Text = "Searching iTunes info...");

                ItunesTrackInfo? info = await _itunesService.SearchOneAsync(searchTerm, token);

                if (info == null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = "No information found. Showing local file data.";
                        txtMetaTrack.Text = track.Title;
                        txtMetaArtist.Text = "-";
                        txtMetaAlbum.Text = "-";
                        StopSlideshow();
                        ShowDefaultCover();
                    });
                    return;
                }

                // save to cache
                var meta = new SongMetadata
                {
                    FilePath = track.FilePath,
                    TrackName = info.TrackName ?? track.Title,
                    ArtistName = info.ArtistName ?? "",
                    AlbumName = info.AlbumName ?? "",
                    ItunesArtworkUrl = info.ArtworkUrl ?? "",
                    ImageUrls = new List<string>()
                };
                _cache.Upsert(meta);

                Dispatcher.Invoke(() =>
                {
                    txtMetaTrack.Text = info.TrackName ?? track.Title;
                    txtMetaArtist.Text = info.ArtistName ?? "-";
                    txtMetaAlbum.Text = info.AlbumName ?? "-";
                    txtStatus.Text = "Info loaded (saved to cache).";

                    // No user images yet -> no slideshow (API image only)
                    StopSlideshow();

                    if (!string.IsNullOrWhiteSpace(info.ArtworkUrl))
                    {
                        try { imgCover.Source = new BitmapImage(new Uri(info.ArtworkUrl)); }
                        catch { ShowDefaultCover(); }
                    }
                    else
                    {
                        ShowDefaultCover();
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    txtStatus.Text = "Error loading info. Showing local file data.";
                    txtMetaTrack.Text = track.Title;
                    txtMetaArtist.Text = "-";
                    txtMetaAlbum.Text = "-";
                    StopSlideshow();
                    ShowDefaultCover();
                });
            }
        }

        private void ShowMetadataFromCacheOrLocal(MusicTrack track, SongMetadata cached)
        {
            txtMetaTrack.Text = string.IsNullOrWhiteSpace(cached.TrackName) ? track.Title : cached.TrackName;
            txtMetaArtist.Text = string.IsNullOrWhiteSpace(cached.ArtistName) ? "-" : cached.ArtistName;
            txtMetaAlbum.Text = string.IsNullOrWhiteSpace(cached.AlbumName) ? "-" : cached.AlbumName;

            // slideshow priority: images added by user (Edit window)
            if (cached.ImageUrls != null && cached.ImageUrls.Count > 0)
            {
                StartSlideshow(cached.ImageUrls);
                return;
            }

            // otherwise show iTunes artwork (single)
            StopSlideshow();
            if (!string.IsNullOrWhiteSpace(cached.ItunesArtworkUrl))
            {
                SetCoverImage(cached.ItunesArtworkUrl);
            }
            else
            {
                ShowDefaultCover();
            }
        }

        private static string BuildSearchTermFromFileName(string fileNameNoExt)
        {
            string term = fileNameNoExt;
            term = term.Replace("_", " ");
            term = term.Replace("-", " ");
            term = string.Join(" ", term.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            return term.Trim();
        }

        private void ClearMetadataUI()
        {
            txtMetaTrack.Text = "-";
            txtMetaArtist.Text = "-";
            txtMetaAlbum.Text = "-";
        }

        private void ShowDefaultCover()
        {
            imgCover.Source = null;
        }

        // ---------------- Slideshow (2 seconds) ----------------

        private void StartSlideshow(List<string> images)
        {
            StopSlideshow();

            _slideshowImages = images
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (_slideshowImages.Count == 0)
                return;

            _slideshowIndex = 0;
            SetCoverImage(_slideshowImages[_slideshowIndex]);

            if (_slideshowImages.Count > 1)
                _slideshowTimer.Start();
        }

        private void StopSlideshow()
        {
            _slideshowTimer.Stop();
            _slideshowImages.Clear();
            _slideshowIndex = 0;
        }

        private void SlideshowTimer_Tick(object? sender, EventArgs e)
        {
            if (_slideshowImages.Count <= 1)
                return;

            _slideshowIndex = (_slideshowIndex + 1) % _slideshowImages.Count;
            SetCoverImage(_slideshowImages[_slideshowIndex]);
        }

        private void SetCoverImage(string pathOrUrl)
        {
            try
            {
                imgCover.Source = new BitmapImage(new Uri(pathOrUrl, UriKind.Absolute));
            }
            catch
            {
                ShowDefaultCover();
            }
        }
    }
}
