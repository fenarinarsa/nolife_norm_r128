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
using Nolife.Diagnostics;
using System.ComponentModel;
using System.Threading;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PasteMix
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        BackgroundWorker bw;
        Regex ffmpeg_output_audio = new Regex(@"time=(?<hour>\d\d):(?<minute>\d\d):(?<second>\d\d).\d\d");

        public MainWindow()
        {
            log.printVersion();

            InitializeComponent();

            this.Title = "PasteMix 1.1";

            if (File.Exists(App.configfile)) {
                String[] conf = File.ReadAllLines(App.configfile);
                if (conf.Length >= 2) {
                    App.output_dir = conf[0];
                    App.mixok_dir = conf[1];
                }
            }

            txbOutput.Text = "Configuration lue dans "+App.configfile+" : "+System.Environment.NewLine
                + "...dossier de sortie des fichiers = " + (App.output_dir.Length==0?"(le même que la source)":App.output_dir) + System.Environment.NewLine
                + "...dossier MIX OK = " + App.mixok_dir + System.Environment.NewLine
                + System.Environment.NewLine
                + "Droppez vos fichiers ici";
        }

        private void txbOutput_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.All;
        }

        private void txbOutput_Drop(object sender, DragEventArgs e)
        {
            String[] files = (String[])e.Data.GetData(DataFormats.FileDrop);


            foreach (String f in files) {
                if (f.ToUpper().StartsWith("Z:")) {
                    if (MessageBox.Show("Attention, faire cette opération à partir du réseau est 10 fois plus lent qu'en local ! (c'est pas une blague)\n\nContinuer quand même ?", "C'est pas bien",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.No)
                        return;
                    else break;
                }
            }

            txbOutput.AllowDrop = false;
            txbOutput.Text = "";


            bw = new BackgroundWorker();
            bw.WorkerSupportsCancellation = true;
            bw.WorkerReportsProgress = true;
            bw.DoWork += bw_DoWork;
            bw.ProgressChanged += bw_ProgressChanged;
            bw.RunWorkerCompleted += bw_RunWorkerCompleted;
            bw.RunWorkerAsync(files);
        }

        private void txbOutput_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }


        void bw_DoWork(object sender, DoWorkEventArgs e)
        {

            String[] files = (String[])e.Argument;

            StringBuilder result = new StringBuilder();


            foreach (String s in files) {
                result.AppendLine();
                FileInfo source = new FileInfo(s);

                String extension = source.Extension.ToUpper();
                if (extension != ".MOV") {
                    bw.ReportProgress(0, "ERREUR, Fichier " + source.Name + " ignoré => n'est pas un .mov");
                    continue;
                }

                bw.ReportProgress(0, "Analyse de " + source.FullName);

                MediaFile mf = new MediaFile(s);

                if (mf.VideoCount != 1) {
                    bw.ReportProgress(0, "ERREUR, " + mf.VideoCount + " piste(s) vidéo trouvée(s)");
                    continue;
                }

                bw.ReportProgress(0, mf.Duration.TotalSeconds);

                String name = source.Name;
                String basename = name;
                if (name.IndexOf(".") > 0)
                    basename = name.Substring(0, name.LastIndexOf("."));
                //DirectoryInfo sourcedir = source.Directory;

                String audiofile = "";

                foreach (String ext in new String[] { ".aif", ".aiff", ".wav" }) {
                    String tmp = System.IO.Path.Combine(source.DirectoryName, basename + ext);
                    if (File.Exists(tmp)) {
                        audiofile = tmp;
                        break;
                    }
                }
                if (audiofile == "") {
                    bw.ReportProgress(0, "Mix introuvable à côté du fichier vidéo, recherche dans " + App.mixok_dir + " ...");
                    List<DirectoryInfo> dirs = new List<DirectoryInfo>();
                    dirs.Add(new DirectoryInfo(App.mixok_dir));
                    for (int i = 0; i < dirs.Count && audiofile == ""; i++) {
                        foreach (String ext in new String[] { ".aif", ".aiff", ".wav" }) {
                            try {
                                FileInfo[] ff = dirs[i].GetFiles(basename + ext);
                                if (ff.Length != 0) {
                                    audiofile = ff[0].FullName;
                                    break;
                                }
                            } catch (Exception) { }
                        }
                        try {
                            if (audiofile == "") {
                                dirs.AddRange(dirs[i].GetDirectories());
                            }
                        } catch (Exception) { }
                    }
                }

                if (audiofile == "") {
                    bw.ReportProgress(0, "ERREUR, mix introuvable pour " + basename + System.Environment.NewLine);
                    continue;
                }

                bw.ReportProgress(0, "Mix trouvé : " + audiofile);

                

                String final_basename = basename;

                String final_file = "";
                int j = 0;
                do {
                    final_file = System.IO.Path.Combine((App.output_dir == "" ? source.DirectoryName : App.output_dir), final_basename+(j>0?"_"+j:"")+" {mix ok}.mov");
                    //if (File.Exists(final_file)) final_basename = basename + " {mix ok}";
                    j++;
                } while (File.Exists(final_file) && j<100);

                bw.ReportProgress(0, "Collage en cours vers " + final_file+ " ..." + System.Environment.NewLine);

                if (remux(mf, source.FullName, audiofile, final_file))
                    bw.ReportProgress(0, "Collage OK \\o/");
                else
                    bw.ReportProgress(0, "ERREUR, le collage a échoué :(" + System.Environment.NewLine);
            }

            bw.ReportProgress(0, "Terminé.");

            e.Result = true;
        }

        bool remux(MediaFile mf, String source_video, String source_audio, String dest_video)
        {
            //MediaInfo minfo = new MediaInfo();
            //minfo.Open(source_video);

            String format = mf.Format;
            String vcodec = mf.VideoCodec[0];
            String scan = mf.VideoScanOrder[0];
            Console.WriteLine("{0} {1} {2}", format, vcodec, scan);

            if (vcodec == "DV" || vcodec == "dvcp") scan = "BFF";
            bool recomp = !(vcodec == "apch"
                || vcodec == "apcn"
                || vcodec == "apcs"
                || vcodec == "apco"
                // || vcodec == "AVj2"
                || vcodec == "AVdn"
                || vcodec == "DV"
                || vcodec == "dvcp");

            //bool vob = source_video.ToLower().EndsWith(".vob");

            if (File.Exists(dest_video)) {
                try { File.Delete(dest_video); } catch (Exception ex) {
                    Console.WriteLine(ex.Message);
                    return false;
                }
            }

            Process ffmpeg3 = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            //startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.FileName = @"ffmpeg.exe";
            startInfo.Arguments = String.Format(@"-i ""{0}"" -i ""{1}""{2} -map 0:{5} -map 1:0 {3} -acodec pcm_s16be -ac 2 -ar 48000 ""{4}""",
                source_video,
                source_audio,
                ((scan == "TFF" || scan == "BFF") && recomp ? " -vf scale=interl=1" : ""),
                (recomp ? "-vcodec prores" : "-vcodec copy"),
                dest_video,
                mf.VideoTracks[0]
                 );
            log.print(startInfo.Arguments);
            //startInfo.CreateNoWindow = true;
            ffmpeg3.StartInfo = startInfo;
            ExternalProcess ep = new ExternalProcess(ffmpeg3, 0, Update);
            ffmpeg3.WaitForExit();
            if (ffmpeg3.ExitCode != 0) return false;

            if (!File.Exists(dest_video)) return false;

            return true;
        }

        void bw_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            pgbProgress.Value = e.ProgressPercentage;
            if (e.UserState is String)
                txbOutput.Text += (String)e.UserState + System.Environment.NewLine;
            else if (e.UserState is double)
                pgbProgress.Maximum = (double)e.UserState;
            else if (e.UserState is int)
                pgbProgress.Maximum = (int)e.UserState;
        }


        void bw_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //txbOutput.Text += (String)e.Result;
            txbOutput.AllowDrop = true;
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


    }
}
