using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Xml;
using OsmSharp.Osm.Streams.Complete;
using OsmSharp.Osm;
using OsmSharp.Osm.Data.Memory;
using OsmSharp.UI.Map.Styles.MapCSS;
using OsmSharp.Data.SQLServer.Osm.Streams;
using OsmSharp.Osm.Xml.Streams;
using OsmSharp.Osm.Streams;
using System.Threading;
using OsmSharp.WinForms.UI;
using OsmSharp.Data.SQLServer.Osm;

namespace OsmSharpWinPrototype
{
    public partial class Form1 : Form
    {
        private ImportToDbWorker importWorker = null;
        private Thread importThread = null;
        private System.Threading.Timer tim;
        private SQLServerDataSource sqlSource;
        private System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();

        public Form1()
        {
            InitializeComponent();
            MapControl mapcon = new MapControl();
            sqlSource = new SQLServerDataSource("Server=VIRTUALBOX;Database=osm;User Id=sa; Password=0773", false);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            openFileDialog1.ShowDialog(this);
        }

        private void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {
            if (tim != null)
            {
                tim.Dispose();
            }

            importWorker = new ImportToDbWorker(openFileDialog1.FileName, this.stopTimeOfWorker);
            importThread = new Thread(importWorker.doWork);
            importThread.Name = importWorker.GetType().Name;
            importThread.Start();

            TimerCallback callback = this.updateStatus;
            tim = new System.Threading.Timer(callback, null, 500, 100);

        }

        private void stopTimeOfWorker(bool workerRunning)
        {
            if (workerRunning)
            {
                stopWatch.Restart();

                button2.BeginInvoke(new Action(() => button2.Enabled = false));
            }
            else
            {
                TimeSpan span = stopWatch.Elapsed;
                if (tim != null)
                {
                    tim.Dispose();
                    tim = null;
                }
                try
                {
                    button2.BeginInvoke(new Action(() => button2.Enabled = true));
                    labelImportCount.BeginInvoke(new Action(() => labelImportCount.Text = labelImportCount.Text + " - Dauer: " + span.ToString()));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }

            }
        }

        private void updateStatus(Object obj)
        {

            try
            {
                StringBuilder text = new StringBuilder();
                foreach (KeyValuePair<OsmGeoType, Int32> current in importWorker.typeCount)
                {
                    if (text.Length > 0)
                        text.Append("; ");
                    text.Append(current.Key).Append("s=").Append(current.Value);

                }

                TimeSpan span = stopWatch.Elapsed;
                text.Append(" - Zeit benötigt: ").Append(span.ToString());

                labelImportCount.BeginInvoke(new Action(() => labelImportCount.Text = text.ToString()));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
        }

        public class ImportToDbWorker
        {
            public delegate void WorkerIsRunning(bool running);

            private String filename;
            public Dictionary<OsmGeoType, Int32> typeCount = new Dictionary<OsmGeoType, int>();
            private WorkerIsRunning handler;

            public ImportToDbWorker(String filename, WorkerIsRunning handler)
            {
                this.filename = filename;
                this.handler = handler;
                _shouldStop = false;
            }

            public void doWork()
            {
                handler.Invoke(true);
                SQLServerOsmStreamTarget sqlTarget = new SQLServerOsmStreamTarget("Server=VIRTUALBOX;Database=osm_test;User Id=sa; Password=0773", true);

                sqlTarget.Initialize();
                FileStream stream = File.OpenRead(filename);
                OsmStreamSource src;

                switch (Path.GetExtension(filename).ToLower())
                {
                    case ".pbf":
                        src = new OsmSharp.Osm.PBF.Streams.PBFOsmStreamSource(stream);
                        break;
                    case ".osm":
                    default:
                        src = new XmlOsmStreamSource(stream);
                        break;
                }

                if (!_shouldStop)
                    transferData(src, sqlTarget);
                handler.Invoke(false);

            }

            private void transferData(OsmStreamSource src, OsmStreamTarget sqlTarget)
            {
                src.Initialize();
                sqlTarget.Initialize();

                while (src.MoveNext(false, false, false) && !_shouldStop)
                {

                    OsmGeo sourceObject = src.Current();

                    if (!typeCount.ContainsKey(sourceObject.Type))
                    {
                        typeCount.Add(sourceObject.Type, 0);
                    }

                    typeCount[sourceObject.Type] = 1 + typeCount[sourceObject.Type];

                    if (sourceObject is Node)
                    {
                        sqlTarget.AddNode(sourceObject as Node);
                    }
                    else if (sourceObject is Way)
                    {
                        sqlTarget.AddWay(sourceObject as Way);
                    }
                    else if (sourceObject is Relation)
                    {
                        sqlTarget.AddRelation(sourceObject as Relation);
                    }
                }

                try
                {

                    sqlTarget.Flush();
                    sqlTarget.Close();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }
            }

            public void requestStop()
            {
                _shouldStop = true;
            }

            // Volatile is used as hint to the compiler that this data
            // member will be accessed by multiple threads.
            private volatile bool _shouldStop;

        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (importWorker != null)
            {
                importWorker.requestStop();
                importWorker = null;
                importThread.Interrupt();
                importThread = null;
            }
            if (tim != null)
            {
                tim.Dispose();
                tim = null;
            }
        }

    }

}
