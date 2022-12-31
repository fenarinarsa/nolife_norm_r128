using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.ComponentModel;
using Nolife.Diagnostics;

namespace Normalisation_audio_R128
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        BackgroundWorker bw = null;
        bool analyzeonly = false;
        bool alwaysnormalize = false;
        bool forceoutput = false;
        bool output_mov = false;
        bool remix_multimono = false;
        int normalize_level = -23;
        String selected_outputformat="";
        Dictionary<String, String> outputmovformat = new Dictionary<string, string>();

        //Regex ffmpeg_output_video = new Regex(@"frame= *(?<frame>\d+) fps= *(?<fps>\d+)");
        Regex ffmpeg_output_audio = new Regex(@"time=(?<hour>\d\d):(?<minute>\d\d):(?<second>\d\d).\d\d");

        String configfile = "config.txt";

        public MainWindow()
        {
            InitializeComponent();

            log.printVersion();

            if (File.Exists(configfile)) {
                String[] conf = File.ReadAllLines(configfile);
                if (conf.Length >= 4) {
                    rdbAnalyze.IsChecked = conf[0] == "True";
                    cbxEverytime.IsChecked = conf[1] == "True";
                    cbxForceOutput.IsChecked = conf[2] == "True";
                    cbxOutputMov.IsChecked = conf[3] == "True";
                    cbxStereoRemix.IsChecked = conf.Length >= 5 && conf[4] == "True";
                    if (conf.Length >= 6) {
                        rdbTVlevel.IsChecked = conf[5] == "True";
                        rdbWeblevel.IsChecked = conf[5] != "True";
                    }
                }
            }

            pgbProgress.Visibility = Visibility.Hidden;

        }

        private void txbMain_DragEnter(object sender, DragEventArgs e)
        {
            // if (e.Data.GetDataPresent(DataFormats.FileDrop, false))
            e.Effects = DragDropEffects.All;
        }

        private void txbMain_Drop(object sender, DragEventArgs e)
        {
            String[] files = (String[])e.Data.GetData(DataFormats.FileDrop);

            txbMain.AllowDrop = false;
            analyzeonly = rdbAnalyze.IsChecked ?? false;
            alwaysnormalize = cbxEverytime.IsChecked ?? false;
            forceoutput = cbxForceOutput.IsChecked ?? false;
            output_mov = cbxOutputMov.IsChecked ?? false;
            remix_multimono = cbxStereoRemix.IsChecked ?? false;
            normalize_level = ((rdbTVlevel.IsChecked??true)?-23:-16);
            selected_outputformat = (String)comboBoxOutputMovFormat.SelectedValue;

            pgbProgress.Visibility = Visibility.Visible;
            pgbProgress.Maximum = 100;

            bw = new BackgroundWorker();
            bw.WorkerSupportsCancellation = true;
            bw.WorkerReportsProgress = true;
            bw.DoWork += bw_DoWork;
            bw.ProgressChanged += bw_ProgressChanged;
            bw.RunWorkerCompleted += bw_RunWorkerCompleted;
            bw.RunWorkerAsync(files);

        }

        void bw_DoWork(object sender, DoWorkEventArgs e)
        {

            String[] files = (String[])e.Argument;

            StringBuilder result = new StringBuilder();

           // String outdircreated = "";
            Regex findlufs = new Regex(@"\[ALBUM\]: ([\-\d\.]+) LUFS");

            foreach (String s in files) {
                result.AppendLine();
                FileInfo source = new FileInfo(s);

                String extension = source.Extension.ToUpper();
                if (extension != ".MOV" && extension != ".WAV" && extension != ".MPG" && extension != ".M2T" && extension != ".MTS" && extension != ".TS"
                    && extension != ".MP4" && extension != ".AVI" && extension != ".MP3" && extension != ".M4A" && extension != ".AIF" && extension != ".FLAC"
                    && extension != ".VOB" && extension != ".MXF" && extension != ".WEBM" && extension != ".MKV")
                    continue;

                result.AppendLine("Processing " + source.FullName);
                bw.ReportProgress(0, result.ToString());

                MediaFile mf = new MediaFile(s);

                bool error = true;
                if (mf.VideoCount > 1)
                    result.AppendLine("-> ERREUR : " + mf.VideoCount + " pistes vidéo trouvées");
                else if (mf.AudioCount == 0)
                    result.AppendLine("-> ERREUR : pas d'audio dans ce fichier");
                else
                    error = false;

                if (error) {
                    bw.ReportProgress(0, result.ToString());
                    continue;
                }

                String name = source.Name;
                String basename = name;
                if (name.IndexOf(".") > 0)
                    basename = name.Substring(0, name.LastIndexOf("."));

                long ticks = DateTime.UtcNow.Ticks;
                String tmpaudio = System.IO.Path.Combine(System.IO.Path.GetTempPath(), ticks + "-tmp.wav");
                String tmpaudio_norm = source.DirectoryName + @"\" + ticks + "-tmp-norm.wav";
                String finalname = source.DirectoryName + @"\" + basename + "-norm.wav";

                bw.ReportProgress(0, mf.Duration.TotalSeconds);

                #region demux
                // Demux audio track to WAV if needed
                ProcessStartInfo startInfo = new ProcessStartInfo();
                if (extension == ".WAV") {
                    tmpaudio = source.FullName;
                } else {
                    String audio_remap = "";
                    if (remix_multimono && mf.AudioCount > 1) {
                        if (mf.AudioCount >= 2) {
                            if (mf.AudioChannels[0] == 1 && mf.AudioChannels[1] == 1) {
                                result.AppendLine("ATTENTION Audio multi-mono détecté, les canaux 1 et 2 vont être remappé en stéréo !");
                                //audio_remap = " -af pan=stereo|FL=c0|FR=c1";
                                // audio_remap = " -map_channel 0.1.0 -map_channel 0.2.0";
                                audio_remap = @" -filter_complex ""[0:1][0:2]amerge=inputs=2[aout]"" -map ""[aout]""";
                            }
                        }
                    }
                    result.Append(" demux...");
                    bw.ReportProgress(0, result.ToString());
                    if (File.Exists(tmpaudio)) {
                        result.AppendLine(" -> ERREUR, fichier tmp existe déjà");
                        bw.ReportProgress(0, result.ToString());
                        continue;
                    }
                    Process ffmpeg3 = new Process();

                    startInfo.FileName = @"ffmpeg.exe";
                    startInfo.Arguments = String.Format(@"-i ""{0}""{2} -acodec pcm_s16le -ar 48000 ""{1}""", source.FullName, tmpaudio, audio_remap);
                    ffmpeg3.StartInfo = startInfo;
                    ExternalProcess ep2 = new ExternalProcess(ffmpeg3, 0, Update);
                    ffmpeg3.WaitForExit();
                    if (ffmpeg3.ExitCode != 0) {
                        result.AppendLine("-> ERREUR : fail conversion en WAV");
                        bw.ReportProgress(0, result.ToString());
                        continue;
                    }

                }
                #endregion demux

                #region r128_analysis
                // Run r128 analysis
                result.Append(" analyse...");
                bw.ReportProgress(0, result.ToString());
                if (File.Exists(tmpaudio_norm))
                    File.Delete(tmpaudio_norm);
                Process r128gain = new Process();
                startInfo = new ProcessStartInfo();
                startInfo.FileName = @"r128gain.exe";

                startInfo.Arguments = "--progress=off \"" + tmpaudio + "\""; // progress don't show on newlines

                startInfo.WorkingDirectory = source.DirectoryName;
                r128gain.StartInfo = startInfo;

                ExternalProcess ep = new ExternalProcess(r128gain, 0, Update);

                r128gain.WaitForExit();

                int exit = -1;
                exit = r128gain.ExitCode;
                #endregion r128_analysis

                String r128output = ep.stdout;

                Match m = findlufs.Match(r128output.ToString());
                if (!m.Success) {
                    result.AppendLine("-> ERREUR : r128gain fail");
                    bw.ReportProgress(0, result.ToString());
                    continue;
                } else {
                    double dblufs = Double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.CreateSpecificCulture("en-US"));
                    double correction = normalize_level - dblufs;
                    if (dblufs <= normalize_level+1 && !analyzeonly && !alwaysnormalize && !forceoutput) {
                        result.AppendLine("-> WARNING : déjà normalisé à " + dblufs + " dBLUFS");
                        bw.ReportProgress(0, result.ToString());

                        if (extension != ".WAV")
                            File.Delete(tmpaudio);
                        continue;
                    } else {
                        if (!analyzeonly) {
                            if (dblufs < normalize_level+1 && !alwaysnormalize) {
                                if (forceoutput) {
                                    int i = 1;
                                    while (File.Exists(finalname)) {
                                        finalname = finalname.Substring(0, finalname.LastIndexOf("-norm")) + "-norm_" + i + ".wav";
                                        i++;
                                    }

                                    File.Move(tmpaudio, finalname);
                                }
                            } else {
                                result.Append(" (" + dblufs + " dBLUFS)");
                                result.Append(" normalisation...");

                                bw.ReportProgress(0, result.ToString());
                                if (File.Exists(tmpaudio_norm))
                                    File.Delete(tmpaudio_norm);
                                Process sox = new Process();
                                startInfo = new ProcessStartInfo();
                                startInfo.FileName = @"r128gain-tools\sox.exe";
                                startInfo.Arguments = "\"" + tmpaudio + "\" \"" + tmpaudio_norm + "\" gain " + correction.ToString("F1", System.Globalization.CultureInfo.CreateSpecificCulture("en-US"));
                                startInfo.WorkingDirectory = source.DirectoryName;
                                sox.StartInfo = startInfo;
                                ep = new ExternalProcess(sox, 0, Update);
                              
                                sox.WaitForExit();

                                if (sox.ExitCode != 0) {
                                    result.AppendLine("-> ERREUR : sox fail");
                                } else {
                                    result.AppendLine(" ==> Correction appliquée : " + correction.ToString("+0.0;-0.0") + " dB");

                                    int i = 1;
                                    while (File.Exists(finalname)) {
                                        finalname = finalname.Substring(0, finalname.LastIndexOf("-norm")) + "-norm_" + i + ".wav";
                                        i++;
                                    }

                                    File.Move(tmpaudio_norm, finalname);
                                }
                            }

                            if (output_mov && File.Exists(finalname)) {
                                result.Append("remux to final file... ");
                                bw.ReportProgress(0, result.ToString());
                                Regex extwav = new Regex(@"\.wav$");
                               // bw.ReportProgress(0, (double)mf.Duration.TotalSeconds);
                                if (remux(mf, source.FullName, finalname, extwav.Replace(finalname, ""), selected_outputformat))
                                    result.AppendLine("done.");
                                else
                                    result.AppendLine("failed :(");
                                bw.ReportProgress(0, result.ToString());
                            }

                        } else {
                            if (correction != 0)
                                result.AppendLine(" ==> Correction à appliquer : " + correction.ToString("+0.0;-0.0") + " dB");
                            else
                                result.AppendLine(" ==> Correction à appliquer : aucune");
                        }
                    }
                }


                if (exit != 0) {
                    result.AppendLine("-> ERREUR : r128gain fail");
                    bw.ReportProgress(0, result.ToString());
                    continue;
                }

                if (extension != ".WAV")
                    File.Delete(tmpaudio);

                if (!analyzeonly)
                    result.AppendLine(" OK !");
                bw.ReportProgress(0, result.ToString());
            }

            result.AppendLine();
            result.AppendLine("Terminé.");

            e.Result = result.ToString();
        }

        public void Update(String line, Object id)
        {
            log.print(line);
            Match m = ffmpeg_output_audio.Match(line);
            if (m.Success) {
                TimeSpan d = TimeSpan.FromHours(int.Parse(m.Groups["hour"].Value))
                                        + TimeSpan.FromMinutes(int.Parse(m.Groups["minute"].Value))
                                        + TimeSpan.FromSeconds(int.Parse(m.Groups["second"].Value));
                bw.ReportProgress((int)d.TotalSeconds);
            }
        }

        bool remux(MediaFile mf, String source_video, String source_audio, String dest_video, String outputformat)
        {

            if (outputformat == "mkv")
                dest_video = dest_video + ".mkv";
            else
                dest_video = dest_video + ".mov";

            String format = mf.Format;
            String vcodec = mf.VideoCodec[0];
            String scan = mf.VideoScanOrder[0];
            log.print("{0} {1} {2}", format, vcodec, scan);

            if (vcodec == "DV" || vcodec == "dvcp") scan = "BFF";
            // Check iv video track must be recompressed or not

            bool recomp = (!(vcodec == "apch"
                || vcodec == "apcn"
                || vcodec == "apcs"
                || vcodec == "apco"
                // || vcodec == "AVj2"
                || vcodec == "AVdn"
                || vcodec == "DV"
                || vcodec == "dvcp")
                && (outputformat!="mkv"));


            if (File.Exists(dest_video)) {
                try { File.Delete(dest_video); } catch (Exception ex) {
                    log.print(ex.Message);
                    return false;
                }
            }

            String codec = "prores";
            String profile = " -profile:v " + (outputformat == "mov_proresLT" ? 1 : 2);


            Process ffmpeg3 = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = @"ffmpeg.exe";
            startInfo.Arguments = String.Format(@"-i ""{0}"" -i ""{1}""{2} -map 0:{5} -map 1:0 {3} -acodec {6} ""{4}""",
                source_video,
                source_audio,
                ((scan == "TFF" || scan == "BFF") && recomp ? " -vf scale=interl=1" : ""),
                (recomp ? "-vcodec "+codec+profile : "-vcodec copy"),
                dest_video,
                mf.VideoTracks[0],
                (outputformat == "mkv"?"aac -ab 256k": "pcm_s16be -ac 2 -ar 48000")
                 );
            log.print(startInfo.Arguments);

            ffmpeg3.StartInfo = startInfo;
            ExternalProcess ep = new ExternalProcess(ffmpeg3, 0, Update);
            ffmpeg3.WaitForExit();
            if (ffmpeg3.ExitCode != 0) return false;

            if (!File.Exists(dest_video)) return false;

            return true;
        }

        void AnalyzeMedia(String path)
        {
        }


        void bw_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            pgbProgress.Value = e.ProgressPercentage;
            //log.print(e.ProgressPercentage + " " + (e.UserState==null?"null":e.UserState.ToString()));
            if (e.UserState is String)
                txbMain.Text = (String)e.UserState;
            else if (e.UserState is double)
                pgbProgress.Maximum = (double)e.UserState;
            else if (e.UserState is int)
                pgbProgress.Maximum = (int)e.UserState;
        }

        void bw_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            pgbProgress.Visibility = Visibility.Hidden;
            txbMain.Text = (String)e.Result;
            txbMain.AllowDrop = true;
        }





        private void txbMain_PreviewDragOver_1(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        private void CheckBox_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void Window_Closed_1(object sender, EventArgs e)
        {
            List<String> c = new List<string>();

            c.Add((rdbAnalyze.IsChecked ?? true).ToString());
            c.Add((cbxEverytime.IsChecked ?? true).ToString());
            c.Add((cbxForceOutput.IsChecked ?? true).ToString());
            c.Add((cbxOutputMov.IsChecked ?? true).ToString());
            c.Add((cbxStereoRemix.IsChecked ?? true).ToString());
            c.Add((rdbTVlevel.IsChecked ?? true).ToString());

            File.WriteAllLines(configfile, c);

        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            outputmovformat.Add("mov_prores", "mov ProRes");
            outputmovformat.Add("mov_proresLT", "mov ProRes LT (light)");
            outputmovformat.Add("mkv", "mkv");
            comboBoxOutputMovFormat.ItemsSource = outputmovformat;
            comboBoxOutputMovFormat.SelectedValue = "mkv";
        }
    }


}
