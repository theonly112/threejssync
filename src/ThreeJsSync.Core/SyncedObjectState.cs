using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ThreeJsSync.Core
{
    public sealed class SyncedObjectState : INotifyPropertyChanged
    {
        private double _positionX;
        private double _positionY;
        private double _positionZ;
        private double _quaternionX;
        private double _quaternionY;
        private double _quaternionZ;
        private double _quaternionW = 1;
        private double _scaleX = 1;
        private double _scaleY = 1;
        private double _scaleZ = 1;
        private bool _visible = true;
        private string _name = "Synchronized cube";
        private string _materialColor = "#4f8cff";
        private double _materialOpacity = 1;
        private readonly Dictionary<string, object> _metadata = new Dictionary<string, object>(StringComparer.Ordinal);

        public event PropertyChangedEventHandler PropertyChanged;

        public double PositionX { get => _positionX; set => Set(ref _positionX, value); }
        public double PositionY { get => _positionY; set => Set(ref _positionY, value); }
        public double PositionZ { get => _positionZ; set => Set(ref _positionZ, value); }
        public double QuaternionX { get => _quaternionX; set => Set(ref _quaternionX, value); }
        public double QuaternionY { get => _quaternionY; set => Set(ref _quaternionY, value); }
        public double QuaternionZ { get => _quaternionZ; set => Set(ref _quaternionZ, value); }
        public double QuaternionW { get => _quaternionW; set => Set(ref _quaternionW, value); }
        public double ScaleX { get => _scaleX; set => Set(ref _scaleX, value); }
        public double ScaleY { get => _scaleY; set => Set(ref _scaleY, value); }
        public double ScaleZ { get => _scaleZ; set => Set(ref _scaleZ, value); }
        public bool Visible { get => _visible; set => Set(ref _visible, value); }
        public string Name { get => _name; set => Set(ref _name, value); }
        public string MaterialColor { get => _materialColor; set => Set(ref _materialColor, value); }
        public double MaterialOpacity { get => _materialOpacity; set => Set(ref _materialOpacity, value); }
        public IReadOnlyDictionary<string, object> Metadata => _metadata;

        public void SetMetadata(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Metadata key is required.", nameof(key));
            if (_metadata.TryGetValue(key, out var existing) && Equals(existing, value)) return;
            _metadata[key] = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Metadata." + key));
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        internal void NotifyVector(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

