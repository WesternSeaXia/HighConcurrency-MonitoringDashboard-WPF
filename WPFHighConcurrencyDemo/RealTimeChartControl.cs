using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WPFHighConcurrencyDemo
{
    public class RealTimeChartControl : FrameworkElement
    {
        private readonly VisualCollection _visuals;
        private readonly DrawingVisual _bgVisual;
        private readonly DrawingVisual _linesVisual;

        private readonly int _maxPoints = 100;
        private readonly double _maxY = 110;
        private const double LeftMargin = 30; // 给 Y 轴文本预留空间
        private const double BottomMargin = 20; // 给 X 轴文本预留空间

        private readonly Dictionary<int, Queue<double>> _dataSeries = new();
        private readonly Dictionary<int, Pen> _pens = new();

        public RealTimeChartControl()
        {
            _visuals = new VisualCollection(this);
            _bgVisual = new DrawingVisual();
            _linesVisual = new DrawingVisual();

            _visuals.Add(_bgVisual);
            _visuals.Add(_linesVisual);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            RenderBackground();
            RenderChart();
        }

        public void AddData(int deviceId, double value, Color color)
        {
            if (!_dataSeries.TryGetValue(deviceId, out var q))
            {
                q = new Queue<double>(_maxPoints + 1);
                _dataSeries[deviceId] = q;

                var pen = new Pen(new SolidColorBrush(color), 1.5);
                pen.Freeze();
                _pens[deviceId] = pen;
            }

            q.Enqueue(value);
            if (q.Count > _maxPoints) q.Dequeue();
        }

        public void RemoveDevice(int deviceId)
        {
            _dataSeries.Remove(deviceId);
            _pens.Remove(deviceId);
            RenderChart();
        }

        public void RemoveAllDevices()
        {
            _dataSeries.Clear();
            _pens.Clear();
            RenderChart();
        }

        private void RenderBackground()
        {
            if (ActualWidth <= LeftMargin || ActualHeight <= BottomMargin) return;

            using DrawingContext dc = _bgVisual.RenderOpen();
            double chartWidth = ActualWidth - LeftMargin;
            double chartHeight = ActualHeight - BottomMargin;

            // 背景底色
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(25, 25, 25)), null, new Rect(0, 0, ActualWidth, ActualHeight));

            var labelBrush = Brushes.Gray;
            var linePen = new Pen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 0.5);
            linePen.Freeze();

            var typeface = new Typeface("Verdana");
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            // 2. 纵轴刻度和辅助线
            for (int i = 0; i <= 5; i++)
            {
                double yVal = (_maxY / 5) * i;
                double yPos = chartHeight - (yVal / _maxY) * chartHeight;

                dc.DrawLine(linePen, new Point(LeftMargin, yPos), new Point(ActualWidth, yPos));

                var text = new FormattedText(yVal.ToString("F0"), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10, labelBrush, dpi);
                dc.DrawText(text, new Point(2, yPos - 7));
            }

            // 3. 横轴相对时间标签
            string[] xLabels = { "-10s", "-5s", "Now" };
            for (int i = 0; i < xLabels.Length; i++)
            {
                double xPos = LeftMargin + (chartWidth / (xLabels.Length - 1)) * i;
                var text = new FormattedText(xLabels[i], CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10, labelBrush, dpi);
                dc.DrawText(text, new Point(xPos - (i == 2 ? 25 : 10), chartHeight + 2)); // 调整最后 "Now" 的偏移
            }

            // 4. 阈值红线
            double thresholdY = chartHeight - (100 / _maxY) * chartHeight;
            var dashPen = new Pen(Brushes.Red, 1) { DashStyle = DashStyles.Dash };
            dashPen.Freeze();
            dc.DrawLine(dashPen, new Point(LeftMargin, thresholdY), new Point(ActualWidth, thresholdY));
        }

        public void RenderChart()
        {
            if (ActualWidth <= LeftMargin || ActualHeight <= BottomMargin) return;

            using DrawingContext dc = _linesVisual.RenderOpen();
            double chartWidth = ActualWidth - LeftMargin;
            double chartHeight = ActualHeight - BottomMargin;
            double stepX = chartWidth / (_maxPoints - 1);

            foreach (var kvp in _dataSeries)
            {
                int id = kvp.Key;
                var queue = kvp.Value;
                if (queue.Count < 2) continue;

                int startIndex = _maxPoints - queue.Count;
                var geometry = new StreamGeometry();

                using (var ctx = geometry.Open())
                {
                    bool isFirst = true;
                    int i = startIndex;

                    foreach (double val in queue)
                    {
                        // 加上 LeftMargin 偏移
                        Point pt = new Point(LeftMargin + (i * stepX), chartHeight - (val / _maxY) * chartHeight);

                        if (isFirst)
                        {
                            ctx.BeginFigure(pt, false, false);
                            isFirst = false;
                        }
                        else
                        {
                            ctx.LineTo(pt, true, false);
                        }
                        i++;
                    }
                }
                geometry.Freeze();
                dc.DrawGeometry(null, _pens[id], geometry);
            }
        }

        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];
    }
}