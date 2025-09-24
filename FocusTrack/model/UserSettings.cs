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
    }
}
