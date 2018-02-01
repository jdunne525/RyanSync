﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using System.Net;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Diagnostics;
using System.IO.Ports;
using UsbEject.Library;

namespace RyanSync
{
    public partial class frmMain : Form
    {

        //Server:
        private string ServerPath = "C:\\Users\\jdunne\\Documents\\Dropbox\\Photos";
        private string[] serverFiles = null;

        //Frame:
        private string frameDriveLetter = "";
        private DirectoryInfo frameDirectory = null;
        private DirectoryInfo serverDirectory = null;
        private string[] frameFiles = null;
        private Dictionary<string, string> FileNamesTable = new Dictionary<string, string>();
        private string[] ServerFileNames = new string[1000];
        string DictionaryPath = System.IO.Path.Combine(Application.StartupPath, "Dictionary.bin");


        private bool UseDebugPath = false;
        private bool ClearDictionarOnStart = false;
        private string DebugPath = "c:\\temp";

        System.IO.Ports.SerialPort mySerialPort = new System.IO.Ports.SerialPort();

        public frmMain()
        {
            InitializeComponent();
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            int HighestPortNumber = 1;
            int HighestIndex = 0;
            int PortNumber = 1;
            string[] PortNames = System.IO.Ports.SerialPort.GetPortNames();

            if (PortNames.Length > 0)
            {
                //select the last port.  (Lame solution.. for now it'll work though)
                mySerialPort.PortName = PortNames[PortNames.Length - 1];

                for (int i = 0; i < PortNames.Length; i++)
                {
                    PortNumber = int.Parse(PortNames[i].Substring(3, PortNames[i].Length - 3));
                    if (PortNumber > HighestPortNumber)
                    {
                        HighestPortNumber = PortNumber;
                        HighestIndex = i;
                    }
                }

                mySerialPort.PortName = PortNames[HighestIndex];
            }

            pgbUpdateProgress.Visible = false;
            tmrUpdate.Enabled = true;
            tmrUpdate.Interval = 900000;
            lblNotification.Text = "";
            refreshServerSync();
            //refreshFrame();

            //read dictionary from the file:
            if (ClearDictionarOnStart)
            {
                FileNamesTable = new Dictionary<string, string>();
            }
            else
            {
                FileNamesTable = ReadDictionary(DictionaryPath);
            }


            FileInfo fi = new FileInfo(@"framefiles.cache");
            if (fi.Exists)
            {
                StreamReader sr = new StreamReader(@"framefiles.cache");

                lstFolder.Items.Clear();
                while (!sr.EndOfStream)
                {
                    lstFolder.Items.Add(sr.ReadLine());
                }
                sr.Close();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>Returns internal variable shouldSync indicating whether the frame needs to be be synced or not.</returns>
        private bool refreshServerSync()
        {

            serverDirectory = new DirectoryInfo(ServerPath);
            if (!serverDirectory.Exists) return false;

            serverFiles = (
                from fi in serverDirectory.GetFiles()
                orderby fi.Name ascending
                select fi.Name
            ).ToArray();

            StringBuilder sb = new StringBuilder();
            StreamWriter sw = File.CreateText(@"serverfiles.cache");

            lstServer.Items.Clear();
            foreach (var name in serverFiles)
            {
                lstServer.Items.Add(name);
                sb.AppendLine(name);            //cache frame files
            }
            string servercache = sb.ToString();
            sw.Write(servercache);
            sw.Close();

            FileInfo fcache = new FileInfo(@"framefiles.cache");
            if (fcache.Exists)
            {
                string framecache = File.ReadAllText(@"framefiles.cache");
                if (framecache != servercache)
                {
                    return true;
                }
                else return false;
            }
            else return true;

        }

        private string GetNewFileName(string OldName, string FileDate)
        {
            FileInfo fi = new FileInfo(OldName);

            int offset = 0;
            for (offset = 0; offset < 999; offset++)
            {
                string newname = FileDate + offset.ToString("000") + fi.Extension;
                if (!FileNamesTable.ContainsValue(newname))
                {

                    return newname;
                }
            }
            return "errorname";
        }

        private void refreshFrameAndAsk()
        {
            if (!refreshFrame())
            {
                //MessageBox.Show(this, "Digital frame not connected!", "Not connected!", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                lblNotification.Text = "Digital frame not connected!";
            }
        }

        private bool refreshFrame()
        {
            try
            {

                if (!UseDebugPath)
                {
                    var frameDrive = (
                    from drv in System.IO.DriveInfo.GetDrives()
                    where drv.IsReady   // must be first check or else "Drive Not Ready" exception is thrown
                    where drv.DriveType == DriveType.Removable
                    where drv.VolumeLabel == Properties.Settings.Default.DigitalFrameLabel
                    select drv
                    ).SingleOrDefault();

                    if (frameDrive == null)
                        return false;

                    //retrive drive letter:
                    frameDriveLetter = frameDrive.Name.Substring(0, 1);

                    // Now find the dedicated subdirectory (currently only one level deep allowed):
                    frameDirectory = (
                        from dir in frameDrive.RootDirectory.GetDirectories()
                        where String.Compare(dir.Name, Properties.Settings.Default.DigitalFrameSubdirectory, true) == 0
                        select dir
                    ).SingleOrDefault();

                    if (frameDirectory == null) frameDirectory = frameDrive.RootDirectory;
                }
                else
                {
                    frameDirectory = new DirectoryInfo(DebugPath);
                }

                if (!frameDirectory.Exists) return false;

                lblFrame.Text = "Digital Frame (" + frameDirectory.FullName + "):";

                frameFiles = (
                    from fi in frameDirectory.GetFiles()
                    orderby fi.Name ascending
                    select fi.Name
                ).ToArray();

                StringBuilder sb = new StringBuilder();
                StreamWriter sw = File.CreateText(@"framefiles.cache");

                lstFolder.Items.Clear();
                foreach (var name in frameFiles)
                {
                    lstFolder.Items.Add(name);
                    sb.AppendLine(name);            //cache frame files
                }
                sw.Write(sb.ToString());
                sw.Close();

                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
                //Dispatcher.Invoke((Action)(() =>
                UIBlockingInvoke(new MethodInvoker(delegate ()
                {
                    //MessageBox.Show(this, ex.Message, "Client Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    lblNotification.Text = "Client Error";
                }));
                return false;
            }
        }

        private void btnSync_Click(object sender, EventArgs e)
        {
            if (!syncServerToFrame())
            {
                //MessageBox.Show(this, "Failed", "Fail", MessageBoxButton.OK, MessageBoxImage.Error);
                //lblNotification.Text = "Failed Sync";
            }
        }

        private int filesSynchronizing = 0;
        private int filesToSynchronize = 0;

        private bool syncServerToFrame()
        {
            lblNotification.Text = "Syncing...";
            Application.DoEvents();         //Display Syncing..

            if (filesSynchronizing < filesToSynchronize) return false;

            if (!refreshServerSync() && !cbxForceSync.Checked)
            {
                lblNotification.Text = DateTime.Now.ToString("HH:mm tt") + " No new files to Sync with Server.";
                return false;
            }
            if (serverFiles == null)
            {
                lblNotification.Text = "No files found on server.";
                return false;
            }

            if (!UseDebugPath)
            {
                try
                {
                    if (!mySerialPort.IsOpen)
                    {
                        mySerialPort.Open();
                        System.Threading.Thread.Sleep(1000);        //give it a second to open the port before doing anything..
                    }
                    mySerialPort.DtrEnable = false;              //Connect
                    mySerialPort.RtsEnable = true;             //Connect
                    System.Threading.Thread.Sleep(15000);        //give it a LONG WHILE to find all the drives before doing anything..
                }
                catch (Exception ex)
                {
                    lblNotification.Text = "Failed to open RS232 port. Error:" + ex.Message;
                    return false;
                }
            }

            refreshFrameAndAsk();
            if (frameFiles == null)
            {
                lblNotification.Text = "Couldn't connect to the frame.";
                Application.DoEvents();
                DisconnectFromDrive(frameDriveLetter);
                return false;
            }


            ServerFileNames = new string[1000];             //max of 1000 files.. because I'm lazy
            //get appropriate names for each of the server files:
            int i;
            i = 0;
            foreach (string file in serverFiles)
            {
                if (FileNamesTable.ContainsKey(file))
                {
                    //We have a key for this server file.  Replace the name with the appropriate name:
                    ServerFileNames[i++] = FileNamesTable[file];
                }
                else
                {
                    //We don't have a replacement name for this file.. just add it as is.  (We'll rename it as we download it)
                    ServerFileNames[i++] = file;
                }
            }

            bool RemovedFileFromFrame = false;
            List<string> FrameFileOriginalNames = new List<string>();
            //Delete files off the frame if they are not on the Server:
            foreach (string FrameFile in frameFiles)
            {

                bool FileExistsOnServer = false;

                foreach (string serverfile in serverFiles)
                {
                    if (FileNamesTable.ContainsKey(serverfile))
                    {
                        if (FrameFile == FileNamesTable[serverfile])
                        {
                            FileExistsOnServer = true;
                            FrameFileOriginalNames.Add(serverfile);
                            break;
                        }
                    }
                }

                if (!FileExistsOnServer)
                {
                    RemovedFileFromFrame = true;
                    //Delete the file from the frame..
                    File.Delete(System.IO.Path.Combine(frameDirectory.FullName, FrameFile));
                }
            }

            if (RemovedFileFromFrame)
            {
                //Refresh frame file listing if we deleted something off the frame:
                refreshFrameAndAsk();
            }

            // Pick only new files from the server:
            var toSync = serverFiles.Except(FrameFileOriginalNames).ToList();
            if (toSync.Count == 0)
            {
                //MessageBox.Show(this, "Completed", "Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                lblNotification.Text = "Frame is already up to date.";
                Application.DoEvents();
                DisconnectFromDrive(frameDriveLetter);
                return true;
            }

            pgbUpdateProgress.Maximum = toSync.Count;
            pgbUpdateProgress.Value = 0;
            pgbUpdateProgress.Visible = true;
            Application.DoEvents();         //display the progress bar change..

            // Disable the sync button and proceed:
            btnSync.Enabled = false;
            filesToSynchronize = toSync.Count;
            filesSynchronizing = 0;

            foreach (string name in toSync)
            {
                string fileName = name;
                try
                {
                    FileInfo serverfile = new FileInfo(System.IO.Path.Combine(serverDirectory.ToString(), fileName));

                    serverfile.CopyTo(System.IO.Path.Combine(frameDirectory.FullName, fileName));

                    //Check if we already have a proper name for this server file:
                    if (!FileNamesTable.ContainsKey(fileName))
                    {
                        //FileInfo fileinfo = new FileInfo(System.IO.Path.Combine(frameDirectory.FullName, fileName));
                        string filedate = serverfile.LastWriteTime.ToString("yyMMddhhmmss");
                        //string filedate = serverfile.LastWriteTime.ToString("yyMMddhhmm");

                        //string filedate = GetDateTakenFromImage(System.IO.Path.Combine(frameDirectory.FullName, fileName)).ToString("yyMMddhhmmss");
                        string newname = GetNewFileName(fileName, filedate);
                        FileNamesTable[fileName] = newname;

                        //can't do this here because we'll get file lock issues since this is multi threaded.
                        //WriteDictionary(FileNamesTable, DictionaryPath);        //save the dictionary to file so we don't lose this file name
                    }

                    //rename the file using the new name:
                    File.Move(System.IO.Path.Combine(frameDirectory.FullName, fileName), System.IO.Path.Combine(frameDirectory.FullName, FileNamesTable[fileName]));

                    // Add the filename to the frame's list:
                    UIBlockingInvoke(new MethodInvoker(delegate ()
                    {
                        lstFolder.Items.Add(fileName);
                        pgbUpdateProgress.Value++;
                    }));
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.ToString());
                    UIBlockingInvoke(new MethodInvoker(delegate ()
                    {
                        lstFolder.Items.Add(fileName + " (ERROR: " + ex.Message + ")");
                    }));
                }
                finally
                {
                    // Increment the number of files synchronized:
                    if (Interlocked.Increment(ref filesSynchronizing) == filesToSynchronize)
                    {
                        // If we're last in line, re-enable the sync button:
                        UIBlockingInvoke(new MethodInvoker(delegate ()
                        {
                            pgbUpdateProgress.Visible = false;
                                //MessageBox.Show(this, "Completed", "Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                lblNotification.Text = DateTime.Now.ToString("HH:mm tt") + " Completed Successfully";
                            Application.DoEvents();

                            System.Threading.Thread.Sleep(5000);     //give time to finish writing

                                DisconnectFromDrive(frameDriveLetter);

                            WriteDictionary(FileNamesTable, DictionaryPath);        //save the dictionary to file

                                btnSync.Enabled = true;
                        }));
                    }
                }
            }
            return true;
        }

        private void DisconnectFromDrive(string frameDriveLetter)
        {

            if (UseDebugPath) return;

            //Eject the USB drive:
            EjectUSBDrive(frameDriveLetter + ":");
            System.Threading.Thread.Sleep(10000);     //give time to eject

            try
            {
                if (mySerialPort.IsOpen)
                {
                    mySerialPort.DtrEnable = true;
                    mySerialPort.RtsEnable = false;
                    System.Threading.Thread.Sleep(10000);     //give time to disconnect fully.
                    mySerialPort.Close();
                }
            }
            catch
            {
                //don't care..
            }
        }

        public static void EjectUSBDrive(string DriveLetterToEject)
        {
            VolumeDeviceClass volumeDeviceClass = new VolumeDeviceClass();

            foreach (Volume device in volumeDeviceClass.Devices)
            {
                if (DriveLetterToEject == device.LogicalDrive)
                {
                    //Eject if found matching drive letter
                    device.Eject(false);
                    break;
                }
            }
        }

        public static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[8 * 1024];
            int len;
            while ((len = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, len);
            }
        }

        private void tmrUpdate_Tick(object sender, EventArgs e)
        {
            if (!syncServerToFrame())
            {
                //MessageBox.Show(this, "Failed", "Fail", MessageBoxButton.OK, MessageBoxImage.Error);
                //lblNotification.Text = "Failed Sync";
            }
        }

        /// <summary>
        /// Runs a MethodInvoker delegate on the UI thread from whichever thread we are currently calling from and BLOCKS until it is complete
        /// </summary>
        /// <param name="ivk"></param>
        public void UIBlockingInvoke(MethodInvoker ivk)
        {
            System.Threading.ManualResetEvent UIAsyncComplete = new System.Threading.ManualResetEvent(false);
            UIAsyncComplete.Reset();
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MethodInvoker(delegate ()
                {
                    try
                    {
                        ivk();
                    }
                    finally
                    {
                        UIAsyncComplete.Set();
                    }
                }));

                UIAsyncComplete.WaitOne();
            }
            else
            {
                ivk();
            }
        }

        private void cbxForceConnection_CheckedChanged(object sender, EventArgs e)
        {
            if (cbxForceConnection.Checked)
            {
                if (!mySerialPort.IsOpen)
                {
                    mySerialPort.Open();
                    System.Threading.Thread.Sleep(1000);        //give it a second to open the port before doing anything..
                }
                mySerialPort.DtrEnable = false;              //Connect
                mySerialPort.RtsEnable = true;             //Connect
                System.Threading.Thread.Sleep(3000);        //give it a second to open the port before doing anything..
            }
            else
            {
                try
                {
                    if (mySerialPort.IsOpen)
                    {
                        mySerialPort.DtrEnable = true;
                        mySerialPort.RtsEnable = false;
                        System.Threading.Thread.Sleep(5000);     //give a WHILE to disconnect fully otherwise the frame goes NUTS!
                        mySerialPort.Close();
                    }
                }
                catch
                {
                    //nothing special..
                }
            }
        }

        private void btnEject_Click(object sender, EventArgs e)
        {
            string Letter = Microsoft.VisualBasic.Interaction.InputBox("Enter drive letter to Eject: ", "Letter", "", 0, 0);
            EjectUSBDrive(Letter + ":");
        }


        private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            //if (mySerialPort.IsOpen)
            //{
            //    mySerialPort.DtrEnable = false;
            //    mySerialPort.RtsEnable = true;
            //    System.Threading.Thread.Sleep(5000);     //give a WHILE to disconnect fully otherwise the frame goes NUTS!
            //    mySerialPort.Close();
            //}
        }

        private void frmMain_Resize(object sender, EventArgs e)
        {
            if (FormWindowState.Minimized == this.WindowState)
            {
                myNotifyIcon.Visible = true;
                myNotifyIcon.BalloonTipText = "RyanSync";
                myNotifyIcon.ShowBalloonTip(500);
                this.Hide();
            }
            else if (FormWindowState.Normal == this.WindowState)
            {
                myNotifyIcon.Visible = false;
            }
        }

        private void myNotifyIcon_Click(object sender, EventArgs e)
        {
            //Restore:
            Show();
            this.WindowState = FormWindowState.Normal;
        }

        private void viewToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }


        static void WriteDictionary(Dictionary<string, string> dictionary, string file)
        {
            using (FileStream fs = File.OpenWrite(file))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                // Put count.
                writer.Write(dictionary.Count);
                // Write pairs.
                foreach (var pair in dictionary)
                {
                    writer.Write(pair.Key);
                    writer.Write(pair.Value);
                }
            }
        }

        static Dictionary<string, string> ReadDictionary(string file)
        {
            var result = new Dictionary<string, string>();
            using (FileStream fs = File.OpenRead(file))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                // Get count.
                int count = reader.ReadInt32();
                // Read in all pairs.
                for (int i = 0; i < count; i++)
                {
                    string key = reader.ReadString();
                    string value = reader.ReadString();
                    result[key] = value;
                }
            }
            return result;
        }


        //we init this once so that if the function is repeatedly called
        //it isn't stressing the garbage man
        private static System.Text.RegularExpressions.Regex r = new System.Text.RegularExpressions.Regex(":");

        //retrieves the datetime WITHOUT loading the whole image
        public static DateTime GetDateTakenFromImage(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (Image myImage = Image.FromStream(fs, false, false))
            {
                System.Drawing.Imaging.PropertyItem propItem = myImage.GetPropertyItem(36867);
                string dateTaken = r.Replace(Encoding.UTF8.GetString(propItem.Value), "-", 2);
                return DateTime.Parse(dateTaken);
            }
        }
    }
}
