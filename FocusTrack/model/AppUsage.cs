using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FocusTrack.Model
{
    public class AppUsage
    {
        public int Id { get; set; }
        public string AppName { get; set; }
        public string WindowTitle { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public byte[] AppIcon { get; set; } 
        public string ExePath { get; set; }
    }
}
