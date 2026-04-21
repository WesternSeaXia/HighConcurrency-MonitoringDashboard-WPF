using System;
using System.Collections.Generic;
using System.Text;

namespace WPFHighConcurrencyDemo.Model
{
    public record SensorMessage(string Id, double Val, long Timestamp);

    public class SensorEntity
    {
        public string SensorId { get; set; } = string.Empty;
        public double Value { get; set; }
        public long Timestamp { get; set; }
    }
}
