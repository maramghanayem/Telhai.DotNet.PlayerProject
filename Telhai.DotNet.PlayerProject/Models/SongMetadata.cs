using System.Collections.Generic;

namespace Telhai.DotNet.PlayerProject.Models
{
    public class SongMetadata
    {
        public string FilePath { get; set; } = string.Empty;

        public string TrackName { get; set; } = string.Empty;
        public string ArtistName { get; set; } = string.Empty;
        public string AlbumName { get; set; } = string.Empty;

        public List<string> ImageUrls { get; set; } = new List<string>();

        public string ItunesArtworkUrl { get; set; } = string.Empty;
    }
}
