using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using System.IO;

namespace PasteMix
{

    public class ExternalProcess
    {
        public delegate void JobProcessingStatusUpdateCallback(String line, Object id);
        public enum StreamType : ushort { None = 0, Stderr = 1, Stdout = 2 }
        protected Thread readFromStdErrThread;
        protected Thread readFromStdOutThread;
        protected ManualResetEvent stdoutDone = new ManualResetEvent(false);
        protected ManualResetEvent stderrDone = new ManualResetEvent(false);
        private StringBuilder stdoutBuilder = new StringBuilder();
        private StringBuilder stderrBuilder = new StringBuilder();
        Process proc;
        Object id;

        public ExternalProcess(Process _proc, Object internal_id, JobProcessingStatusUpdateCallback Update = null, bool start = true)
        {
            id = internal_id;
            proc = _proc;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.Exited += new EventHandler(proc_Exited);
            if (Update != null) StatusUpdate += Update;
            if (start && proc.Start()) {
                readFromStdErrThread = new Thread(new ThreadStart(readStdErr));
                readFromStdOutThread = new Thread(new ThreadStart(readStdOut));
                readFromStdOutThread.Start();
                readFromStdErrThread.Start();
            }
        }

        public bool Start()
        {
            bool start = proc.Start();
            if (start) {
                readFromStdErrThread = new Thread(new ThreadStart(readStdErr));
                readFromStdOutThread = new Thread(new ThreadStart(readStdOut));
                readFromStdOutThread.Start();
                readFromStdErrThread.Start();
            }
            return start;
        }


        protected void proc_Exited(object sender, EventArgs e)
        {
            stdoutDone.WaitOne(); // wait for stdout to finish processing
            stderrDone.WaitOne(); // wait for stderr to finish processing

            //App.log("[" + _jobname + "] [CommandLineJob.proc_Exited] Processus terminé, PID=" + _pid + " jobname='" + _jobname + "' exitcode=" + proc.ExitCode);
            //ProcessusDone(proc.ExitCode);
        }

        public String stdout
        { get { return stdoutBuilder.ToString(); }  }
        public String stderr
        { get { return stderrBuilder.ToString(); } }

        #region reading process output
        protected virtual void readStream(StreamReader sr, ManualResetEvent rEvent, StreamType str)
        {
            string line;

            try {
                while ((line = sr.ReadLine()) != null)  //&& !proc.HasExited)
                {
                    ProcessLine(line, str);
                    //Trace.WriteLine("[CommandLineJob.readStream] "+line);
                    StatusUpdate(line, id);
                }
            } catch (Exception e) {
                // ProcessLine("[" + _jobname + "] [readStream] Exception in readStdErr: " + e.Message, str);
            }
            rEvent.Set();

            // }
        }
        protected void readStdOut()
        {
            StreamReader sr = null;
            try {
                sr = proc.StandardOutput;
            } catch (Exception e) {
                //log.LogValue("Exception getting IO reader for stdout", e, ImageType.Error);
                stdoutDone.Set();
                return;
            }
            readStream(sr, stdoutDone, StreamType.Stdout);
        }
        protected void readStdErr()
        {
            StreamReader sr = null;
            try {
                sr = proc.StandardError;
            } catch (Exception e) {
                //log.LogValue("Exception getting IO reador for stderr", e, ImageType.Error);
                stderrDone.Set();
                return;
            }
            readStream(sr, stderrDone, StreamType.Stderr);
        }

        public virtual void ProcessLine(string line, StreamType stream)
        {
            // App.log(line);
            if (stream == StreamType.Stdout)
                stdoutBuilder.AppendLine(line);
            if (stream == StreamType.Stderr) {
                stderrBuilder.AppendLine(line);

            }
        }
        public event JobProcessingStatusUpdateCallback StatusUpdate;

        #endregion
    }
}
