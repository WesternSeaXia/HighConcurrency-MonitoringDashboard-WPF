using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WPFHighConcurrencyDemo
{
    public class DeviceMapControl : FrameworkElement
    {
        private static readonly SolidColorBrush NormalBrush;
        private static readonly SolidColorBrush ErrorBrush;
        private static readonly Pen SelectedPen;

        public event Action<int, bool>? DeviceSelectionToggled;

        static DeviceMapControl()
        {
            NormalBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113));
            NormalBrush.Freeze();
            ErrorBrush = new SolidColorBrush(Color.FromRgb(231, 76, 60));
            ErrorBrush.Freeze();
            SelectedPen = new Pen(new SolidColorBrush(Color.FromRgb(52, 152, 219)), 2.5);
            SelectedPen.Freeze();
        }

        private class DeviceNode
        {
            public bool IsError { get; set; }
            public bool IsSelected { get; set; }
            public DrawingVisual Visual { get; set; } = new();
            public Rect Bounds { get; set; }
        }

        private readonly VisualCollection _visuals;
        private DeviceNode[] _devices = Array.Empty<DeviceNode>();
        private int _deviceCount;
        private int _devicesPerRow = 1;
        private double _cellSize = 16;
        private const double Spacing = 2;

        public DeviceMapControl()
        {
            _visuals = new VisualCollection(this);
            this.SizeChanged += (s, e) => { if (e.WidthChanged) ReflowVisuals(); };
        }

        public void InitializeGrid(int deviceCount, double cellSize)
        {
            _visuals.Clear();
            _devices = new DeviceNode[deviceCount];
            _deviceCount = deviceCount;
            _cellSize = cellSize;

            for (int i = 0; i < deviceCount; i++)
            {
                var node = new DeviceNode();
                _devices[i] = node;
                _visuals.Add(node.Visual);
            }

            this.InvalidateMeasure();
            this.InvalidateVisual();

            Dispatcher.BeginInvoke(ReflowVisuals, System.Windows.Threading.DispatcherPriority.Render);
        }

        private void ReflowVisuals()
        {
            if (_deviceCount == 0 || ActualWidth <= 0) return;

            double step = _cellSize + Spacing;
            _devicesPerRow = Math.Max(1, (int)(ActualWidth / step));

            for (int i = 0; i < _deviceCount; i++)
            {
                int row = i / _devicesPerRow;
                int col = i % _devicesPerRow;

                var newBounds = new Rect(col * step, row * step, _cellSize, _cellSize);

                if (_devices[i].Bounds != newBounds)
                {
                    _devices[i].Bounds = newBounds;
                    RenderNode(_devices[i]);
                }
            }
        }

        public void UpdateDeviceState(int deviceId, bool isError)
        {
            if (deviceId < 0 || deviceId >= _deviceCount) return;
            var node = _devices[deviceId];
            if (node.IsError != isError)
            {
                node.IsError = isError;
                RenderNode(node);
            }
        }

        private void RenderNode(DeviceNode node)
        {
            using DrawingContext dc = node.Visual.RenderOpen();
            Brush brush = node.IsError ? ErrorBrush : NormalBrush;
            dc.DrawRectangle(brush, node.IsSelected ? SelectedPen : null, node.Bounds);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            Point pt = e.GetPosition(this);
            double step = _cellSize + Spacing;

            int col = (int)(pt.X / step);
            int row = (int)(pt.Y / step);

            if (col >= _devicesPerRow) return;

            int id = row * _devicesPerRow + col;
            if (id >= 0 && id < _deviceCount)
            {
                var node = _devices[id];
                node.IsSelected = !node.IsSelected;
                RenderNode(node);
                DeviceSelectionToggled?.Invoke(id, node.IsSelected);
            }
        }

        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_deviceCount == 0) return new Size(0, 0);

            double step = _cellSize + Spacing;
            double width = double.IsInfinity(availableSize.Width) ? ActualWidth : availableSize.Width;
            if (width <= 0) width = 800;

            int cols = Math.Max(1, (int)(width / step));
            int totalRows = (int)Math.Ceiling((double)_deviceCount / cols);

            return new Size(width, totalRows * step);
        }
    }
}