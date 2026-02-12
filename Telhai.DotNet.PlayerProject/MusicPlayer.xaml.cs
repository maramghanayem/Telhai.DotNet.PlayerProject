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

        // iTunes API
        private readonly ItunesService _itunesService = new ItunesService();
        private CancellationTokenSource? _itunesCts;

        public MusicPlayer()
        {
            InitializeComponent();

            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += new EventHandler(Timer_Tick);
            this.Loaded += MusicPlayer_Loaded;

            ShowDefaultCover();
            ClearMetadataUI();
        }

        private void MusicPlayer_Loaded(object sender, RoutedEventArgs e)
        {
            this.LoadLibrary();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (mediaPlayer.Source != null && mediaPlayer.NaturalDuration.HasTimeSpan && !isDragging)
            {
                sliderProgress.Maximum = mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                sliderProgress.Value = mediaPlayer.Position.TotalSeconds;
            }
        }

        // -----------------------
        // UI behavior requirements
        // -----------------------

        // Single click: show song name + file path (local)
        private void LstLibrary_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                txtCurrentSong.Text = track.Title;
                txtFilePath.Text = track.FilePath;
            }
        }

        // Double click: play song + start async API call
        private void LstLibrary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                StartPlayingTrack(track);
            }
        }

        // PLAY button: should play selected song
        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                StartPlayingTrack(track);
                return;
            }

            // if nothing selected -> resume current
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

            // cancel API if running
            _itunesCts?.Cancel();
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

        // -----------------------
        // Library management
        // -----------------------

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Multiselect = true;
            ofd.Filter = "MP3 Files|*.mp3;*.mpeg;*.wav;*.wma";

            if (ofd.ShowDialog() == true)
            {
                foreach (string file in ofd.FileNames)
                {
                    MusicTrack track = new MusicTrack
                    {
                        Title = System.IO.Path.GetFileNameWithoutExtension(file),
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

        // -----------------------
        // Core play + async metadata
        // -----------------------

        private void StartPlayingTrack(MusicTrack track)
        {
            if (!File.Exists(track.FilePath))
                return;

            // show local info immediately
            txtCurrentSong.Text = track.Title;
            txtFilePath.Text = track.FilePath;

            // play audio immediately (no UI blocking)
            mediaPlayer.Open(new Uri(track.FilePath));
            mediaPlayer.Play();
            timer.Start();
            txtStatus.Text = "Playing";

            // clear metadata + default image
            ClearMetadataUI();
            ShowDefaultCover();
            txtMetaTrack.Text = track.Title;

            // cancel previous API call (avoid unnecessary calls)
            _itunesCts?.Cancel();
            _itunesCts = new CancellationTokenSource();

            string searchTerm = BuildSearchTermFromFileName(track.Title);

            // start async call in parallel (no await here)
            _ = LoadAndShowMetadataAsync(searchTerm, track, _itunesCts.Token);
        }

        private async Task LoadAndShowMetadataAsync(string searchTerm, MusicTrack track, CancellationToken token)
        {
            try
            {
                txtStatus.Dispatcher.Invoke(() =>
                {
                    txtStatus.Text = "Searching iTunes info...";
                });

                ItunesTrackInfo? info = await _itunesService.SearchOneAsync(searchTerm, token);

                if (info == null)
                {
                    // no info found -> show fallback (file name + path already shown)
                    Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = "No information found. Showing local file data.";
                        txtMetaTrack.Text = track.Title;
                        txtMetaArtist.Text = "-";
                        txtMetaAlbum.Text = "-";
                        ShowDefaultCover();
                    });
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    txtMetaTrack.Text = info.TrackName ?? track.Title;
                    txtMetaArtist.Text = info.ArtistName ?? "-";
                    txtMetaAlbum.Text = info.AlbumName ?? "-";
                    txtStatus.Text = "Info loaded.";

                    if (!string.IsNullOrWhiteSpace(info.ArtworkUrl))
                    {
                        try
                        {
                            imgCover.Source = new BitmapImage(new Uri(info.ArtworkUrl));
                        }
                        catch
                        {
                            ShowDefaultCover();
                        }
                    }
                    else
                    {
                        ShowDefaultCover();
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // song changed -> ignore
            }
            catch
            {
                // on error -> show required fallback: file name (no extension) + full path
                Dispatcher.Invoke(() =>
                {
                    txtStatus.Text = "Error loading info. Showing local file data.";
                    txtMetaTrack.Text = track.Title;
                    txtMetaArtist.Text = "-";
                    txtMetaAlbum.Text = "-";
                    ShowDefaultCover();
                });
            }
        }

        // -----------------------
        // Helpers
        // -----------------------

        private static string BuildSearchTermFromFileName(string fileNameNoExt)
        {
            // Example: "Artist - Song" or "Artist-Song" or "Song Name"
            string term = fileNameNoExt;

            term = term.Replace("_", " ");
            term = term.Replace("-", " "); // requirement: separated by spaces or hyphen
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
            // Option 1: keep null (no crash) OR add your own default image later
            imgCover.Source = null;

            // If you want a real default image:
            // 1) create folder Assets
            // 2) add default_cover.png
            // 3) set Build Action = Resource
            // then uncomment:

            // try
            // {
            //     imgCover.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/default_cover.png"));
            // }
            // catch
            // {
            //     imgCover.Source = null;
            // }
        }
    }
}
