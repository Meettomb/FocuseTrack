using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FocusTrack.model
{
    public class UserSettings
    {
        public int Id { get; set; }
        public bool TrackPrivateMode { get; set; }
        public bool TrackVPN { get; set; }
        public string BreakTime { get; set; }
        public bool NotifyBreakEveryTime { get; set; }
        public bool ActivityTrackingScope { get; set; }
        public string HistoryRetentionPeriod { get; set; }
        public string LastCleanupDate { get; set; }
    }
}