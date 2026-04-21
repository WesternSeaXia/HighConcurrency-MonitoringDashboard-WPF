using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WPFHighConcurrencyDemo.Model;

namespace WPFHighConcurrencyDemo
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<int, double> _deviceValues = new();
        private readonly Dictionary<int, Color> _subscribedDevices = new();
        private readonly Color[] _lineColors = { Colors.Cyan, Colors.Chartreuse, Colors.Yellow, Colors.Orange, Colors.DeepSkyBlue, Colors.Fuchsia };

        // 声明清理定时器和时间戳数组
        private readonly DispatcherTimer _cleanupTimer;
        private long[] _deviceLastErrorTicks = Array.Empty<long>();

        private int _currentDeviceCount = 0;
        private int _colorIndex = 0;

        // 定时器与连接状态
        private readonly DispatcherTimer _simTimer;
        private readonly DispatcherTimer _renderTimer;
        private readonly Random _rnd = new();
        private bool _isSimulating = false;
        private HubConnection? _hubConnection;
        private bool _isHubConnected = false;

        private readonly ConcurrentQueue<DeviceStateUpdate> _updateBuffer = new();
        private record DeviceStateUpdate(int DeviceId, bool IsError, double? Value = null);

        public MainWindow()
        {
            InitializeComponent();

            _simTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _simTimer.Tick += SimTimer_Tick;

            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) }; // ~60FPS
            _renderTimer.Tick += RenderTimer_Tick;

            MapControl.DeviceSelectionToggled += MapControl_DeviceSelectionToggled;

            // 每 1 秒执行一次的扫码清理器
            _cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cleanupTimer.Tick += CleanupTimer_Tick;
        }

        private async void MapControl_DeviceSelectionToggled(int deviceId, bool isSelected)
        {
            string strId = deviceId.ToString();

            if (isSelected)
            {
                Color lineColor = _lineColors[_colorIndex++ % _lineColors.Length];
                _subscribedDevices[deviceId] = lineColor;

                // 通知服务器加入高速推流名单
                if (_isHubConnected && _hubConnection != null)
                {
                    await _hubConnection.InvokeAsync("SubscribeDevice", strId);
                }
            }
            else
            {
                _subscribedDevices.Remove(deviceId);
                ChartControl.RemoveDevice(deviceId);
                MapControl.UpdateDeviceState(deviceId, false);

                // 通知服务器取消高速推流订阅
                if (_isHubConnected && _hubConnection != null)
                {
                    await _hubConnection.InvokeAsync("UnsubscribeDevice", strId);
                }
            }
        }

        private void BtnInit_Click(object sender, RoutedEventArgs e)
        {
            _simTimer.Stop();
            _renderTimer.Stop();
            _isSimulating = false;

            ChartControl.RemoveAllDevices();

            if (int.TryParse(TxtCount.Text, out int count) && double.TryParse(TxtSize.Text, out double size))
            {
                _currentDeviceCount = count;
                _deviceValues.Clear();
                _subscribedDevices.Clear();

                // 根据设备总数初始化时间戳数组
                _deviceLastErrorTicks = new long[_currentDeviceCount];

                MapControl.InitializeGrid(_currentDeviceCount, size);
                TxtInfo.Text = $"网格已重置: {_currentDeviceCount} 个设备";
            }
        }

        private void BtnStartSimulation_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDeviceCount <= 0) return;
            if (_isHubConnected) return;

            _isSimulating = !_isSimulating;
            if (_isSimulating)
            {
                _simTimer.Start();
                BtnSim.Content = "停止本地模拟";
                BtnSim.Background = new SolidColorBrush(Color.FromRgb(192, 57, 43));
                TxtInfo.Text = "状态：模拟运行中...";
            }
            else
            {
                _simTimer.Stop();
                BtnSim.Content = "2A. 开启本地模拟";
                BtnSim.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96));
                TxtInfo.Text = "状态：模拟已停止";
            }
        }

        private void SimTimer_Tick(object? sender, EventArgs e)
        {
            // 背景噪音
            int bgChangeCount = _rnd.Next(50, 150);
            for (int i = 0; i < bgChangeCount; i++)
            {
                int rndId = _rnd.Next(0, _currentDeviceCount);
                if (!_subscribedDevices.ContainsKey(rndId))
                    MapControl.UpdateDeviceState(rndId, _rnd.NextDouble() < 0.01);
            }

            // 订阅设备的均值回归随机游走
            foreach (var kvp in _subscribedDevices)
            {
                int id = kvp.Key;
                if (!_deviceValues.TryGetValue(id, out double lastVal)) lastVal = _rnd.NextDouble() * 20 + 40;

                double newVal = lastVal + ((_rnd.NextDouble() - 0.5) * 10.0) + ((50.0 - lastVal) * 0.1);
                if (_rnd.NextDouble() < 0.015) newVal += _rnd.Next(35, 60);

                newVal = Math.Clamp(newVal, 0, 110);
                _deviceValues[id] = newVal;

                ChartControl.AddData(id, newVal, kvp.Value);
                MapControl.UpdateDeviceState(id, newVal > 100);
            }

            if (_subscribedDevices.Count > 0) ChartControl.RenderChart();
        }

        private async void BtnConnectHub_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDeviceCount <= 0 || _isSimulating) return;

            BtnHub.IsEnabled = false;
            if (!_isHubConnected) await ConnectToHubAsync();
            else await DisconnectHubAsync();
            BtnHub.IsEnabled = true;
        }

        private async Task ConnectToHubAsync()
        {
            BtnHub.Content = "连接中...";
            try
            {
                // 彻底清理历史模拟留下的图表脏数据和内部状态
                ChartControl.RemoveAllDevices();
                _deviceValues.Clear();
                _updateBuffer.Clear();

                if (_hubConnection == null)
                {
                    _hubConnection = new HubConnectionBuilder()
                        .WithUrl(TxtHubUrl.Text.Trim())
                        .WithAutomaticReconnect()
                        .Build();

                    _hubConnection.On<dynamic>("ReceiveStatus", (status) =>
                    {
                        Dispatcher.InvokeAsync(() => {
                            TxtInfo.Text = $"网关 TPS: {status.GetProperty("Tps").GetInt64()} | 总处理量: {status.GetProperty("TotalProcessed").GetInt64()}";
                        });
                    });

                    _hubConnection.On<List<SensorEntity>>("ReceiveAlertBatch", (alerts) =>
                    {
                        foreach (var alert in alerts)
                        {
                            if (int.TryParse(alert.SensorId, out int id))
                            {
                                _updateBuffer.Enqueue(new DeviceStateUpdate(id, true, null));
                            }
                        }
                    });

                    _hubConnection.On<List<SensorMessage>>("ReceiveDetailBatch", (details) =>
                    {
                        foreach (var detail in details)
                        {
                            if (int.TryParse(detail.Id, out int id))
                            {
                                _updateBuffer.Enqueue(new DeviceStateUpdate(id, detail.Val > 100, detail.Val));
                            }
                        }
                    });
                }

                await _hubConnection.StartAsync();
                _isHubConnected = true;
                _renderTimer.Start();

                // 启动超时清理定时器
                _cleanupTimer.Start();

                BtnHub.Content = "断开 SignalR";
                BtnHub.Background = new SolidColorBrush(Color.FromRgb(192, 57, 43));

                foreach (var deviceId in _subscribedDevices.Keys)
                {
                    await _hubConnection.InvokeAsync("SubscribeDevice", deviceId.ToString());
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"连接失败: {ex.Message}");
                BtnHub.Content = "2B. 连接真实 SignalR";
            }
        }

        private async Task DisconnectHubAsync()
        {
            if (_hubConnection != null) await _hubConnection.StopAsync();

            _renderTimer.Stop();

            // 停止超时清理定时器
            _cleanupTimer.Stop();

            _isHubConnected = false;
            _updateBuffer.Clear();

            BtnHub.Content = "2B. 连接真实 SignalR";
            BtnHub.Background = new SolidColorBrush(Color.FromRgb(41, 128, 185));
            TxtInfo.Text = "状态：已断开。";
        }

        private void RenderTimer_Tick(object? sender, EventArgs e)
        {
            if (_updateBuffer.IsEmpty) return;
            bool chartNeedsRender = false;

            while (_updateBuffer.TryDequeue(out var update))
            {
                // 如果收到异常状态，更新该设备最后一次异常的毫秒级时间戳
                if (update.IsError && update.DeviceId >= 0 && update.DeviceId < _currentDeviceCount)
                {
                    _deviceLastErrorTicks[update.DeviceId] = Environment.TickCount64;
                }

                MapControl.UpdateDeviceState(update.DeviceId, update.IsError);

                if (update.Value.HasValue && _subscribedDevices.TryGetValue(update.DeviceId, out Color lineColor))
                {
                    ChartControl.AddData(update.DeviceId, update.Value.Value, lineColor);
                    chartNeedsRender = true;
                }
            }

            if (chartNeedsRender) ChartControl.RenderChart();
        }

        private void CleanupTimer_Tick(object? sender, EventArgs e)
        {
            if (_currentDeviceCount == 0 || !_isHubConnected) return;

            long currentTicks = Environment.TickCount64;
            const long timeoutMs = 3000; // 3秒超时

            // 遍历整个数组（C# 遍历一个 5000 长度的 long 数组只需要不到 1 毫秒，毫无性能压力）
            for (int i = 0; i < _currentDeviceCount; i++)
            {
                // 如果该设备有过异常记录
                if (_deviceLastErrorTicks[i] > 0)
                {
                    // 判断是否超过 3 秒
                    if (currentTicks - _deviceLastErrorTicks[i] > timeoutMs)
                    {
                        // 状态恢复
                        _deviceLastErrorTicks[i] = 0; // 重置时间戳
                        MapControl.UpdateDeviceState(i, false); // 前端强制变绿
                    }
                }
            }
        }

        protected override async void OnClosed(EventArgs e)
        {
            if (_hubConnection != null) await _hubConnection.DisposeAsync();
            base.OnClosed(e);
        }
    }
}