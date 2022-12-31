using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Diagnostics;
using Nolife.Diagnostics;
using System.Text.RegularExpressions;

namespace PasteMix
{

    public class MediaFile
    {
        public String Format = "";
        public String[] AudioCodec = new String[] { "" };
        public String[] VideoScanOrder = new String[] { "" };
        public String[] VideoCodec = new String[] { "" };
        //public String[] ScanOrder = new String[] { "" };
        public int[] AudioTracks;
        public int[] AudioChannels;
        public int[] VideoTracks;
        public int AudioCount = 0;
        public int VideoCount = 0;
        public long[] VideoFrames;
        public TimeSpan Duration = TimeSpan.FromTicks(0);

        Regex regDuration = new Regex(@"(?<hour>\d\d):(?<minute>\d\d):(?<second>\d\d).(?<millisecond>\d\d)");

        public MediaFile(String source_video)
        {
            //MediaInfo minfo = new MediaInfo()
            //minfo.Open(source_video);

            Process proc = new Process();
            ProcessStartInfo pstart = new ProcessStartInfo();
            pstart.FileName = "mediainfo.exe";
            pstart.Arguments = @"--Full --Output=XML """ + source_video + @"""";
            log.print("[MediaInfo] Analyzing {0}", source_video);
            pstart.RedirectStandardOutput = true;
            //pstart.RedirectStandardError = true;
            pstart.WindowStyle = ProcessWindowStyle.Minimized;
            pstart.CreateNoWindow = true;
            pstart.UseShellExecute = false;
            proc.StartInfo = pstart;
            proc.Start();

            String output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            if (output.Trim().Length < 10) return;

            XDocument xdoc = XDocument.Parse(output);

            var tracks = (from track in xdoc.Descendants("track")
                          select track);

            int audiotrack = -1;
            int videotrack = -1;
            int tmp = 0;
            bool vob = source_video.ToUpper().EndsWith(".VOB");

            foreach (XElement track in tracks) {

                String type = track.Attribute("type").Value;
                if (type == null) continue;




                switch (type) {
                    case "General":
                        foreach (XElement xel in track.Descendants()) {
                            if (xel.Name == "Count_of_audio_streams" && int.TryParse(xel.Value, out tmp)) {
                                AudioCount = tmp;
                                AudioTracks = new int[AudioCount];
                                AudioChannels = new int[AudioCount];
                                AudioCodec = new String[AudioCount];
                            } else if (xel.Name == "Count_of_video_streams" && int.TryParse(xel.Value, out tmp)) {
                                VideoCount = tmp;
                                VideoTracks = new int[VideoCount];
                                VideoScanOrder = new String[VideoCount];
                                VideoCodec = new String[VideoCount];
                                VideoFrames = new long[VideoCount];
                            } else if (xel.Name == "Format") {
                                Format = xel.Value;
                            } else if (xel.Name == "Duration") {
                                Match m = regDuration.Match(xel.Value);
                                if (m.Success) {
                                    Duration = TimeSpan.FromHours(int.Parse(m.Groups["hour"].Value))
                                        + TimeSpan.FromMinutes(int.Parse(m.Groups["minute"].Value))
                                        + TimeSpan.FromSeconds(int.Parse(m.Groups["second"].Value))
                                        + TimeSpan.FromMilliseconds(int.Parse(m.Groups["millisecond"].Value));
                                }
                            }
                        }
                        break;
                    case "Video":
                        videotrack++;
                        if (vob) VideoTracks[videotrack] = 1;
                        VideoFrames[videotrack] = 0;
                        foreach (XElement xel in track.Descendants()) {
                            if (xel.Name == "StreamOrder" && !vob) {
                                int.TryParse(xel.Value, out VideoTracks[videotrack]);
                            } else if (xel.Name == "Codec_ID")
                                VideoCodec[videotrack] = xel.Value;
                            else if (xel.Name == "Scan_order") {
                                if (xel.Value == "TFF") VideoScanOrder[videotrack] = "TFF";
                                else if (xel.Value == "BFF") VideoScanOrder[videotrack] = "BFF";
                            } else if (xel.Name == "Frame_count") {
                                long.TryParse(xel.Value, out VideoFrames[videotrack]);
                            }
                        }
                        break;
                    case "Audio":
                        audiotrack++;
                        foreach (XElement xel in track.Descendants()) {
                            if (xel.Name == "StreamOrder")
                                int.TryParse(xel.Value, out AudioTracks[audiotrack]);
                            else if (xel.Name == "Codec_ID")
                                AudioCodec[audiotrack] = xel.Value;
                            else if (xel.Name == "Channel_s_") {
                                if (int.TryParse(xel.Value, out tmp)) AudioChannels[audiotrack] = tmp;
                            }
                        }
                        break;
                }
            }



            log.print("Format='{0}' VideoCount={1} AudioCount={2}", Format, VideoCount, AudioCount);

            for (int i = 0; i < VideoCount; i++) {
                log.print("Video #{0} Stream={1} Codec={2} ScanOrder={3}", i, VideoTracks[i], VideoCodec[i], VideoScanOrder[i]);
            }
            for (int i = 0; i < AudioCount; i++) {
                log.print("Audio #{0} Stream={1} Codec={2} Channels={3}", i, AudioTracks[i], AudioCodec[i], AudioChannels[i]);
            }

        }
    }
}
