using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Telhai.DotNet.PlayerProject.Models;

namespace Telhai.DotNet.PlayerProject.Services
{
    public class SongMetadataCacheService
    {
     
        private static readonly SongMetadataCacheService _instance = new SongMetadataCacheService();
        public static SongMetadataCacheService Instance => _instance;

        private const string CACHE_FILE = "songs_cache.json";

        private List<SongMetadata> _items = new List<SongMetadata>();
        private bool _loaded = false;

       
        private SongMetadataCacheService() { }

        private void EnsureLoaded()
        {
            if (_loaded) return;

            if (File.Exists(CACHE_FILE))
            {
                try
                {
                    string json = File.ReadAllText(CACHE_FILE);
                    _items = JsonSerializer.Deserialize<List<SongMetadata>>(json) ?? new List<SongMetadata>();
                }
                catch
                {
                    _items = new List<SongMetadata>();
                }
            }

            _loaded = true;
        }

        
        public void ReloadFromDisk()
        {
            _loaded = false;
            EnsureLoaded();
        }

        public SongMetadata? GetByFilePath(string filePath)
        {
            EnsureLoaded();
            return _items.FirstOrDefault(x =>
                string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        }

        public void Upsert(SongMetadata item)
        {
            EnsureLoaded();

            var existing = GetByFilePath(item.FilePath);
            if (existing == null)
            {
               
                if (item.ImageUrls == null) item.ImageUrls = new List<string>();
                _items.Add(item);
            }
            else
            {
                existing.TrackName = item.TrackName;
                existing.ArtistName = item.ArtistName;
                existing.AlbumName = item.AlbumName;

                
                if (!string.IsNullOrWhiteSpace(item.ItunesArtworkUrl))
                    existing.ItunesArtworkUrl = item.ItunesArtworkUrl;

               
                existing.ImageUrls = item.ImageUrls ?? new List<string>();
            }

            Save();
        }

        private void Save()
        {
            string json = JsonSerializer.Serialize(_items,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CACHE_FILE, json);
        }
    }
}
